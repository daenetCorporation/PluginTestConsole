using Azure;
using Azure.AI.OpenAI;
using Daenet.LLMPlugin.Common;
using Daenet.LLMPlugin.TestConsole.Entities;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Reflection;

namespace Daenet.LLMPlugin.TestConsole
{
    public class TestConsole
    {
        private static IConfigurationRoot? _config;

        private readonly TestConsoleConfig _consoleCfg;
        private readonly ILogger<TestConsole> _logger;
        private readonly PluginManager _pluginMgr;
        private readonly McpToolsConfig _mcpToolsCfg;
        private static IChatClient? _innerChatClient;

        public TestConsole(TestConsoleConfig cfg, PluginManager pluginMgr, McpToolsConfig mcpToolsCfg, ILogger<TestConsole> logger)
        {
            _consoleCfg = cfg;
            _logger = logger;
            _pluginMgr = pluginMgr;
            _mcpToolsCfg = mcpToolsCfg;
        }

        /// <summary>
        /// Initializes the singleton IChatClient and registers it (and optionally an embedding generator) in DI.
        /// </summary>
        public static void UseAgentFramework(IServiceCollection svcCollection)
        {
            _innerChatClient = CreateInnerChatClient();

            if (svcCollection != null)
            {
                svcCollection.AddSingleton<IChatClient>(_innerChatClient);

                var embeddingGenerator = TryCreateEmbeddingGenerator();
                if (embeddingGenerator != null)
                    svcCollection.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(embeddingGenerator);
            }
        }

        /// <summary>
        /// Loads all plugins/tools and runs the agent conversation loop.
        /// </summary>
        public async Task RunAsync(AIAgent agent, AgentSession sess, ILogger<McpClientResilent> mcpLogger)
        {
            _testConsolePlugin.Session = sess;

            var clr = ConsoleColor.White;
            Console.ForegroundColor = clr;

            Console.WriteLine("Plugin and Tool Test Console started ...");

            Console.ForegroundColor = _consoleCfg.UserInputColor;
            Console.Write(_consoleCfg.SystemPrompt);

            string? userInput;
            while ((userInput = Console.ReadLine()) != null)
            {
                Console.ForegroundColor = clr;

                try
                {
                    var response = await agent.RunAsync(userInput, sess);

                    Console.ForegroundColor = _consoleCfg.AssistentMessageColor;

                    List<ToolApprovalRequestContent> approvalRequests = response.Messages
    .SelectMany(m => m.Contents)
    .OfType<ToolApprovalRequestContent>()
    .ToList();

                    while (approvalRequests.Count > 0)
                    {
                        List<ChatMessage> userInputResponses = approvalRequests
                            .ConvertAll(request =>
                            {
                                var toolCall = (FunctionCallContent)request.ToolCall;
                                Console.WriteLine($"Approve {toolCall.Name}? (Y/N)");
                                bool approved = Console.ReadLine()?.Equals("Y", StringComparison.OrdinalIgnoreCase) ?? false;
                                return new ChatMessage(ChatRole.User, [request.CreateResponse(approved)]);
                            });

                        response = await agent.RunAsync(userInputResponses, sess);
                        approvalRequests = response.Messages
                            .SelectMany(m => m.Contents)
                            .OfType<ToolApprovalRequestContent>()
                            .ToList();                        
                    }

                    Console.WriteLine("Assistant > " + response.Text);
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Assistant > {ex.Message}");
                }

                Console.ForegroundColor = _consoleCfg.PromptColor;
                Console.Write(_consoleCfg.SystemPrompt);
                Console.ForegroundColor = _consoleCfg.UserInputColor;
            }
        }

        private TestConsolePlugin _testConsolePlugin;

        public async Task ImportToolsAsync(List<AITool> tools, ILogger<McpClientResilent> mcpLogger)
        {
            // Built-in console management plugin.
            _testConsolePlugin = new TestConsolePlugin(tools, _consoleCfg);
            AddObjectToTools(tools, _testConsolePlugin);

            // Dynamically configured plugins from appsettings.
            var pluginInstances = _pluginMgr.CreateRequiredPlugins();
            foreach (var pluginInstance in pluginInstances)
                AddObjectToTools(tools, pluginInstance);

            // MCP tools (McpClientTool inherits AIFunction/AITool).
            var toolImporter = new McpToolImporter(tools, _mcpToolsCfg, _logger, mcpLogger);
            await toolImporter.ImportMcpTools();
        }

        /// <summary>
        /// Discovers all public instance methods decorated with [Description] on <paramref name="pluginInstance"/>
        /// and registers each as an <see cref="AIFunction"/> in <paramref name="tools"/>.
        /// </summary>
        internal static void AddObjectToTools(List<AITool> tools, object pluginInstance)
        {
            var methods = pluginInstance.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.DeclaringType != typeof(object)
                         && m.GetCustomAttribute<DescriptionAttribute>() != null);

            foreach (var method in methods)
                tools.Add(AIFunctionFactory.Create(method, pluginInstance));
        }

        /// <summary>
        /// Creates an IChatClient backed by Azure OpenAI or OpenAI, chosen by environment variables.
        /// </summary>
        public static IChatClient CreateInnerChatClient()
        {
            var client = TryGetAzureChatClient() ?? TryGetOpenAIChatClient();

            if (client == null)
                throw new Exception(
                    "No valid AI client found. Set AZURE_OPENAI_API_KEY + AZURE_OPENAI_ENDPOINT + AZURE_OPENAI_CHATCOMPLETION_DEPLOYMENT, " +
                    "or OPENAI_API_KEY + OPENAI_CHATCOMPLETION_DEPLOYMENT.");

            return client;
        }

        private static IChatClient? TryGetAzureChatClient()
        {
            var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
            var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
            var deployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME");

            if (apiKey == null || endpoint == null || deployment == null)
                return null;

            var azureClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
            return azureClient.GetChatClient(deployment).AsIChatClient();
        }

        private static IChatClient? TryGetOpenAIChatClient()
        {
            var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            var model = Environment.GetEnvironmentVariable("OPENAI_CHATCOMPLETION_DEPLOYMENT");

            if (apiKey == null || model == null)
                return null;

            var openAIClient = new OpenAI.OpenAIClient(new System.ClientModel.ApiKeyCredential(apiKey));
            return openAIClient.GetChatClient(model).AsIChatClient();
        }

        private static IEmbeddingGenerator<string, Embedding<float>>? TryCreateEmbeddingGenerator()
        {
            // Azure OpenAI embedding
            var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
            var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
            var deployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_DEPLOYMENT");

            if (apiKey != null && endpoint != null && deployment != null)
            {
                var azureClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
                return azureClient.GetEmbeddingClient(deployment).AsIEmbeddingGenerator();
            }

            // OpenAI embedding
            apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            deployment = Environment.GetEnvironmentVariable("OPENAI_EMBEDDING_DEPLOYMENT");

            if (apiKey != null && deployment != null)
            {
                var openAIClient = new OpenAI.OpenAIClient(new System.ClientModel.ApiKeyCredential(apiKey));
                return openAIClient.GetEmbeddingClient(deployment).AsIEmbeddingGenerator();
            }

            return null;
        }

        /// <summary>
        /// Makes Plugin configuration accessible.
        /// </summary>
        public static PluginLibrary GetPlugins(IConfigurationRoot configuration, string pluginConfigSection = "Plugins")
        {
            PluginLibrary pluginLib = new PluginLibrary();

            var pluginCfgs = configuration.GetSection(pluginConfigSection).GetChildren();

            foreach (var item in pluginCfgs)
            {
                var plugin = new SkPlugin();

                item.Bind(plugin);

                if (plugin.JsonConfiguration != null && !string.IsNullOrEmpty(plugin.Name))
                {
                    pluginLib.Plugins.Add(plugin);
                }
            }

            return pluginLib;
        }
    }
}