using Daenet.LLMPlugin.Common;
using Daenet.LLMPlugin.TestConsole.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Daenet.LLMPlugin.TestConsole
{
    public class TestConsole
    {
        private static IConfigurationRoot? _config;

        private readonly TestConsoleConfig _consoleCfg;
        private readonly ILogger<TestConsole> _logger;
        private readonly PluginManager _pluginMgr;
        private readonly McpToolsConfig _mcpToolsCfg;
        private static Kernel? _kernel;

        public TestConsole(TestConsoleConfig cfg, PluginManager pluginMgr, McpToolsConfig mcpToolsCfg, ILogger<TestConsole> logger)
        {
            _consoleCfg = cfg;
            _logger = logger;
            _pluginMgr = pluginMgr;
            _mcpToolsCfg = mcpToolsCfg;
        }

        /// <summary>
        /// Initializes the singleton instance of the Semantic kernel for DI.
        /// </summary>
        /// <param name="svcCollection"></param>
        public static void UseSemantikKernel(IServiceCollection svcCollection)
        {
            _kernel = GetKernel();

            if (svcCollection != null)
                svcCollection.AddSingleton(_kernel);
        }

        /// <summary>
        /// Loads all plugins and runs the chatbot conversation.
        /// </summary>
        /// In this case plugins are initialized with the kernel instance.</param>
        /// <returns></returns>
        public async Task RunAsync(ILogger<McpClientResilent> mcpLogger)
        {
            _kernel = null;

            var clr = ConsoleColor.White;
            Console.ForegroundColor = clr;

            if (_kernel == null)
                _kernel = GetKernel();

            Console.WriteLine("Plugin and Tool Test Console started ...");

            // Create chat history
            var history = new ChatHistory();

            history.AddSystemMessage(_consoleCfg.SystemMessage);

            await ImportPlugins(_kernel, history,mcpLogger);

            // Get chat completion service
            var chatCompletionService = _kernel.GetRequiredService<IChatCompletionService>();

            string? userInput;

            // Start the conversation
            Console.ForegroundColor = _consoleCfg.UserInputColor;
            Console.Write(_consoleCfg.SystemPrompt);

            var sysPrompt = $@"Today is: {DateTime.UtcNow:yyyy MM dd HH:mm:ss}";

            while ((userInput = Console.ReadLine()) != null)
            {
                Console.ForegroundColor = clr;

                // Add user input
                history.AddUserMessage(userInput);

                // Enable auto function calling
                OpenAIPromptExecutionSettings openAIPromptExecutionSettings = new()
                {
                    ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
                    Temperature = 0,
                    ChatSystemPrompt = sysPrompt
                };

                ChatMessageContent? result = null;

                try
                {
                    // Get the response from the AI
                    result = await chatCompletionService.GetChatMessageContentAsync(
                        history,
                        executionSettings: openAIPromptExecutionSettings,
                        kernel: _kernel);

                    Console.ForegroundColor = _consoleCfg.AssistentMessageColor;

                    // Print the results
                    Console.WriteLine("Assistant > " + result);

                    // Add the message from the agent to the chat history
                    history.AddMessage(result.Role, result.Content ?? string.Empty);

                    //break;
                }
                catch (Exception ex)
                {
                    if (history.FirstOrDefault(m => (m.Content == null ? String.Empty : m.Content).ToString().Contains("Started the new conversation")) != null)
                    {
                        history.Clear();
                        history.AddSystemMessage(_consoleCfg.SystemMessage);
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Assistant > {ex.Message}");
                    }
                }


                // Get user input again
                Console.ForegroundColor = _consoleCfg.PromptColor;
                Console.Write(_consoleCfg.SystemPrompt);
                Console.ForegroundColor = _consoleCfg.UserInputColor;
            }
        }

        private async Task ImportPlugins(Kernel kernel, ChatHistory history, ILogger<McpClientResilent> mcpLogger)
        {
            var pluginInstances = _pluginMgr.CreateRequiredPlugins();

            kernel.ImportPluginFromObject(new TestConsolePlugin(kernel, history, _consoleCfg));

            foreach (var pluginObj in pluginInstances)
            {
                kernel.ImportPluginFromObject(pluginObj);
            }

            McpToolImporter toolImporter = new McpToolImporter(kernel, _mcpToolsCfg,
                _logger, mcpLogger);

            await toolImporter.ImportMcpTools();
        }


        /*
private static void UseSemantSearchApi(IServiceCollection svcCollection)
{
   ILoggerFactory lFact = LoggerFactory.Create(builder =>
   {
       builder.AddDebug();
   });

   var logger = lFact.CreateLogger<SearchApi>();

   var allowedDataSources = CreateAllowedDataSources(_config!);

   var embeddingIndexDal = CreateEmbeddingIndexDal(_config!, lFact);

   var embeddingGenerator = CreateEmbeddingGenerator(_config!);

   ISimilarityCalculator distanceCalculator = new CosineDistanceCalculator();

   var textConvertors = CreateTextConvertors();

   var blobStorageConfig = GetBlobStorageConfig(_config!);

   IDocumentSplitter documentSplitter = new DocumentSplitter();

   var searchApi = new SearchApi(embeddingGenerator, distanceCalculator, embeddingIndexDal, logger, textConvertors, documentSplitter, worker: null, blobStorageConfig, allowedDataSources);

   svcCollection.AddSingleton<ISearchApi>(searchApi);
}


private static BlobStorageConfig GetBlobStorageConfig(IConfigurationRoot config)
{
   var blobconfig = config.GetSection($"{nameof(BlobStorageConfig)}").Get<BlobStorageConfig>() ?? throw new Exception($"{nameof(BlobStorageConfig)} is missing");
   return blobconfig;
}

private static TextConvertors CreateTextConvertors()
{
   TextConvertors cvs = new()
   {
       Convertors = new List<ITextConvertor>()
       {
               new PdfToTextConvertor() , new WordConvertor()
       }
   };
   return cvs;
}

private static IEmbeddingGenerator CreateEmbeddingGenerator(IConfigurationRoot config)
{
   var openAICfg = config.GetSection("OpenAi").Get<AzureOpenAICfg>() ?? throw new Exception($"{nameof(AzureOpenAICfg)} is missing");

   if (string.IsNullOrEmpty(openAICfg.Key))
   {
       throw new Exception($"{nameof(AzureOpenAICfg)} Key is missing");
   }

   return new AzureOpenAIEmbeddingGenerator(openAICfg);
}

private static IVectorDbClient CreateEmbeddingIndexDal(IConfigurationRoot config, ILoggerFactory loggerFactory)
{
   IVectorDbClient vectorDbClient = null;
   var configDal = config.GetSection($"{nameof(QDrantDalConfig)}").Get<QDrantDalConfig>();
   if (configDal != null)
   {
       return CreateQdrantDal(config, loggerFactory);
   }
   else
   {
       return CreateSqlDal(config, loggerFactory);
   }
}

private static IVectorDbClient CreateSqlDal(IConfigurationRoot config, ILoggerFactory loggerFactory)
{
   var sqlCfg = config.GetSection($"{nameof(SqlDalConfig)}").Get<SqlDalConfig>() ?? throw new Exception($"{nameof(SqlDalConfig)} and {nameof(SqlDalConfig)} are missing");

   var logger = loggerFactory.CreateLogger<SqlServerDal>();

   return new SqlServerDal(sqlCfg, logger);
}

private static IVectorDbClient CreateQdrantDal(IConfigurationRoot config, ILoggerFactory loggerFactory)
{
   var qCfg = config.GetSection($"{nameof(QDrantDalConfig)}").Get<QDrantDalConfig>() ?? throw new Exception($"{nameof(QDrantDalConfig)} is missing");

   var logger = loggerFactory.CreateLogger<QDrantClient>();

   return new QDrantClient(qCfg, logger);
}

private static AllowedDataSources? CreateAllowedDataSources(IConfigurationRoot config)
{
   var sec = config.GetSection($"{nameof(AllowedDataSources)}");

   if (sec != null)
   {
       AllowedDataSources src = sec.Get<AllowedDataSources>()!;
       return src;
   }

   return null;
}
*/

        /// <summary>
        /// Gets the kernel from environment settings.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        private static Kernel GetKernel()
        {
            Kernel? kernel = TryGetAzureKernel();
            if (kernel == null)
                kernel = TryGetOpenAIKernel();

            if (kernel == null)
                throw new Exception("No valid kernel found.To initialize the kernel, please see documentation. Requred environment variables must be set.");

            return kernel;
        }


        private static Kernel? TryGetOpenAIKernel()
        {
            Kernel? kernel = null;

            if (Environment.GetEnvironmentVariable("OPENAI_API_KEY") != null &&
             Environment.GetEnvironmentVariable("OPENAI_ORGID") != null)
            {
                if (Environment.GetEnvironmentVariable("OPENAI_CHATCOMPLETION_DEPLOYMENT") != null &&
                     Environment.GetEnvironmentVariable("OPENAI_EMBEDDING_DEPLOYMENT") == null)
                {
                    kernel = Kernel.CreateBuilder()
                     .AddOpenAIChatCompletion(
                    Environment.GetEnvironmentVariable("OPENAI_CHATCOMPLETION_DEPLOYMENT")!, // The name of your deployment (e.g., "gpt-3.5-turbo")
                    Environment.GetEnvironmentVariable("OPENAI_API_KEY")!,
                    Environment.GetEnvironmentVariable("OPENAI_ORGID")!)
                .Build();
                }
                else if (Environment.GetEnvironmentVariable("OPENAI_CHATCOMPLETION_DEPLOYMENT") != null &&
                     Environment.GetEnvironmentVariable("OPENAI_EMBEDDING_DEPLOYMENT") != null)
                {
#pragma warning disable SKEXP0010 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
                    kernel = Kernel.CreateBuilder()
                     .AddOpenAIChatCompletion(
                    Environment.GetEnvironmentVariable("OPENAI_CHATCOMPLETION_DEPLOYMENT")!, // The name of your deployment (e.g., "gpt-3.5-turbo")
                    Environment.GetEnvironmentVariable("OPENAI_API_KEY")!,
                    Environment.GetEnvironmentVariable("OPENAI_ORGID")!)
                     .AddOpenAITextEmbeddingGeneration(
                     Environment.GetEnvironmentVariable("OPENAI_EMBEDDING_DEPLOYMENT")!,
                     Environment.GetEnvironmentVariable("OPENAI_API_KEY")!,
                     Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")!
                )
                .Build();
#pragma warning restore SKEXP0010 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
                }
                else
                {
#pragma warning disable SKEXP0010 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
                    kernel = Kernel.CreateBuilder()
                     .AddOpenAITextEmbeddingGeneration(
                    Environment.GetEnvironmentVariable("OPENAI_EMBEDDING_DEPLOYMENT")!, // The name of your deployment (e.g., "gpt-3.5-turbo")
                    Environment.GetEnvironmentVariable("OPENAI_API_KEY")!,
                    Environment.GetEnvironmentVariable("OPENAI_ORGID")!)
                .Build();
#pragma warning restore SKEXP0010 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
                }
            }
            return kernel;
        }

        private static Kernel? TryGetAzureKernel()
        {
            Kernel? kernel = null;

            if (Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY") != null &&
         Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") != null)
            {
                if (Environment.GetEnvironmentVariable("AZURE_OPENAI_CHATCOMPLETION_DEPLOYMENT") != null &&
                    Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_DEPLOYMENT") == null)
                {
#pragma warning disable SKEXP0010 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
                    kernel = Kernel.CreateBuilder()
                .AddAzureOpenAIChatCompletion(
                    Environment.GetEnvironmentVariable("AZURE_OPENAI_CHATCOMPLETION_DEPLOYMENT")!,  // The name of your deployment (e.g., "text-davinci-003")
                    Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")!,    // The endpoint of your Azure OpenAI service
                    Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")!      // The API key of your Azure OpenAI service
                )
                .Build();
#pragma warning restore SKEXP0010 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
                }
                else if (Environment.GetEnvironmentVariable("AZURE_OPENAI_CHATCOMPLETION_DEPLOYMENT") != null &&
                    Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_DEPLOYMENT") != null)
                {
#pragma warning disable SKEXP0010 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
                    kernel = Kernel.CreateBuilder()
                .AddAzureOpenAIChatCompletion(
                    Environment.GetEnvironmentVariable("AZURE_OPENAI_CHATCOMPLETION_DEPLOYMENT")!,  // The name of your deployment (e.g., "text-davinci-003")
                    Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")!,    // The endpoint of your Azure OpenAI service
                    Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")!      // The API key of your Azure OpenAI service
                )
                .AddAzureOpenAITextEmbeddingGeneration(
                     Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_DEPLOYMENT")!,
                     Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")!,    // The endpoint of your Azure OpenAI service
                     Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")!      // The API key of your Azure OpenAI service
                )
                .Build();
#pragma warning restore SKEXP0010 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
                }
                else
                {
                    #pragma warning disable SKEXP0010 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
                    //kernel = Kernel.CreateBuilder()
                    //                 .AddAzureOpenAITextEmbeddingGeneration(
                    //    Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_DEPLOYMENT")!,
                    //    Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")!,    // The endpoint of your Azure OpenAI service
                    //    Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")!      // The API key of your Azure OpenAI service
                   //)
                   //.Build();
#pragma warning restore SKEXP0010 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
                }
            }

            return kernel;
        }

        /// <summary>
        /// Makes Plugin configuration accessible.
        /// </summary>
        /// <param name="builder"></param>
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
