
using Daenet.ClawLib;
using System.Linq;
using Daenet.LLMPlugin.Common;
using Daenet.LLMPlugin.TestConsole.Entities;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;

namespace Daenet.LLMPlugin.TestConsole.App
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            await SampleClaw(args);
        }


        private static async Task SampleClaw(string[] args)
        {
            var cfg = InitializeConfig(args);

            McpToolsConfig mcpToolsConfig = new McpToolsConfig();
            cfg.GetSection("McpToolsConfig").Bind(mcpToolsConfig);

            TestConsoleConfig consCfg = new TestConsoleConfig();
            cfg.GetSection("TestConsoleConfig").Bind(consCfg);

            // Set up a service collection for dependency injection.
            var serviceCollection = new ServiceCollection();

            serviceCollection.AddSingleton<McpToolsConfig>(mcpToolsConfig);

            // Initializes the logging.
            serviceCollection.AddLogging(configure => configure.AddConsole());

            UsePluginLibrary(serviceCollection, cfg);

            // Register the provider for creating instances of plugins.
            serviceCollection.AddSingleton<IPlugInProvider, DefaultPlugInProvider>();

            // Register the configuration with the dependency injection container.
            serviceCollection.AddSingleton<PluginManager>();

            // Register TestConsoleConfig with the dependency injection container.
            serviceCollection.AddSingleton<TestConsoleConfig>(new TestConsoleConfig()
            { 
                SystemPrompt = "-> ",
                SystemMessage = GetSystemMessageExtension(mcpToolsConfig)
            });

            // Register the configuration of the built-in plugin.
            serviceCollection.AddSingleton<TestConsole>();

       
           // Creates the IChatClient and registers it (+ optional embedding generator) for DI.
            // Required for chat completion and, when embedding env vars are set, for EmbeddingsPlugin.
            TestConsole.UseAgentFramework(serviceCollection);

            // Build the service provider.
            var serviceProvider = serviceCollection.BuildServiceProvider();

            // Get an instance of TestConsole from the service provider.
            var testConsole = serviceProvider.GetRequiredService<TestConsole>();

            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

            var mcpLogger = serviceProvider.GetRequiredService<ILogger<McpClientResilent>>();          

            logger.LogInformation("Application running...");

            List<AITool> tools = new List<AITool>();

            var clawSession = ClawSession.FromEnvironment(tools);

            await testConsole.ImportToolsAsync(tools, mcpLogger);

            //var logFact = serviceProvider.GetRequiredService<ILoggerFactory>();

            using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Debug);
            });

            var agent = await clawSession.InitializeAsync(loggerFactory);           

            await testConsole.RunAsync(agent, clawSession.AgentSession, mcpLogger);

        }

        private static async Task Sample1(string[] args)
        {
            var cfg = InitializeConfig(args);

            McpToolsConfig mcpToolsConfig = new McpToolsConfig();
            cfg.GetSection("McpToolsConfig").Bind(mcpToolsConfig);

            TestConsoleConfig consCfg = new TestConsoleConfig();
            cfg.GetSection("TestConsoleConfig").Bind(consCfg);

            // Set up a service collection for dependency injection.
            var serviceCollection = new ServiceCollection();

            serviceCollection.AddSingleton<McpToolsConfig>(mcpToolsConfig);

            // Initializes the logging.
            serviceCollection.AddLogging(configure => configure.AddConsole());

            UsePluginLibrary(serviceCollection, cfg);

            // Register the provider for creating instances of plugins.
            serviceCollection.AddSingleton<IPlugInProvider, DefaultPlugInProvider>();

            // Register the configuration with the dependency injection container.
            serviceCollection.AddSingleton<PluginManager>();

            // Register TestConsoleConfig with the dependency injection container.
            serviceCollection.AddSingleton<TestConsoleConfig>(new TestConsoleConfig()
            {
                SystemPrompt = "-> ",
                SystemMessage = GetSystemMessageExtension(mcpToolsConfig)
            });

            // Register the configuration of the built-in plugin.
            serviceCollection.AddSingleton<TestConsole>();


            // Creates the IChatClient and registers it (+ optional embedding generator) for DI.
            // Required for chat completion and, when embedding env vars are set, for EmbeddingsPlugin.
            TestConsole.UseAgentFramework(serviceCollection);

            // Build the service provider.
            var serviceProvider = serviceCollection.BuildServiceProvider();

            // Get an instance of TestConsole from the service provider.
            var testConsole = serviceProvider.GetRequiredService<TestConsole>();

            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

            var mcpLogger = serviceProvider.GetRequiredService<ILogger<McpClientResilent>>();

            logger.LogInformation("Application running...");

            Stopwatch sw = new Stopwatch();

            var tools = new List<AITool>();

            var agentChannel = TestConsole.CreateInnerChatClient();

            // Create a temporary placeholder agent to initialize the session
            // We'll recreate the real agent after tools are loaded
            AIAgent tempAgent = agentChannel.AsAIAgent(
                instructions: consCfg.SystemMessage,
                name: "TempAgent",
                tools: new List<AITool>());

            AgentSession agentSession = await tempAgent.CreateSessionAsync();

            await testConsole.ImportToolsAsync(tools, mcpLogger);

            // Create the final agent with all loaded tools
            AIAgent agent = agentChannel.AsAIAgent(
                instructions: consCfg.SystemMessage,
                name: "TestConsoleAgent",
                tools: tools);

            // Call the RunAsync method on the TestConsole instance.
            await testConsole.RunAsync(agent, agentSession, mcpLogger);
        }

        /// <summary>
        /// Loads the configuration.
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        private static IConfiguration InitializeConfig(string[] args)
        {
            var configBuilder = new ConfigurationBuilder();
            configBuilder.AddJsonFile("appsettings.json");
            configBuilder.AddEnvironmentVariables();
            configBuilder.AddCommandLine(args);
            configBuilder.AddUserSecrets<Program>();

            return configBuilder.Build();
        }

        /// <summary>
        /// Loads the list of required plugins from the appsetings and creates the Plugin Library.
        /// </summary>
        /// <param name="builder"></param>
        private static void UsePluginLibrary(ServiceCollection svcCollection, IConfiguration configuration)
        {
            PluginLibrary pluginLib = new PluginLibrary();

            var pluginCfgs = configuration.GetSection("Plugins").GetChildren();

            foreach (var item in pluginCfgs)
            {
                var plugin = new SkPlugin();

                item.Bind(plugin);
                if (plugin.JsonConfiguration == null)
                {
                    //_logger.LogWarning($"The plugin section contains a definition of the plugin without JSON content. Possible configuration mistake!!");
                }
                if (string.IsNullOrEmpty(plugin.Name) == false)
                    pluginLib.Plugins.Add(plugin);
            }

            svcCollection.AddSingleton(pluginLib);
        }

        private static string GetSystemMessageExtension(McpToolsConfig mcpToolsConfig)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"You are the agent who provide informaiton for user's intent and invoke plugin functions.");
           
            if (mcpToolsConfig == null || mcpToolsConfig.McpServers == null || mcpToolsConfig.McpServers.Count ==0)
            {
                return sb.ToString();
            }

            foreach (var server in mcpToolsConfig.McpServers)
            {
                if (!string.IsNullOrEmpty(server.ServerSystemMessage))
                {
                    sb.AppendLine($"{server.ServerSystemMessage}");
                }
            }

            return sb.ToString();
        }

        private static void UseSemanticSearchApi(IConfiguration configuration, ServiceCollection serviceCollection)
        {
            //
            //SearchApi.UseSemantSearchApi(configuration, serviceCollection);
        }

    }
}
