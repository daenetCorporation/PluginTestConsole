using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Daenet.LLMPlugin.Common;

namespace Daenet.LLMPlugin.TestConsole
{
    public class TestConsole
    {
        private static IConfigurationRoot? _config;

        private readonly TestConsoleConfig _consoleCfg;
        private readonly ILogger<TestConsole> _logger;
        private readonly PluginManager _pluginMgr;
        private readonly IServiceCollection _svcCollection;

        public TestConsole(TestConsoleConfig cfg, PluginManager pluginMgr, ILogger<TestConsole> logger, IServiceCollection svcCollection)
        {
            _consoleCfg = cfg;
            _logger = logger;
            _pluginMgr = pluginMgr;
            _svcCollection = svcCollection;
        }

        /// <summary>
        /// Loads all plugins and runs the chatbot conversation.
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        public async Task RunAsync()
        {
            var clr = ConsoleColor.White;
            Console.ForegroundColor = clr;

            Console.WriteLine("CPDM Plugin Test Console started ...");

            var kernel = GetKernel();

            //var svcProvider = svcCollection.BuildServiceProvider();

            //var logger = svcProvider.GetService<ILogger<TestConsole>>();

            // Create chat history
            var history = new ChatHistory();

            history.AddSystemMessage(_consoleCfg.SystemMessage);

            ImportPlugins(kernel, history);

            // Get chat completion service
            var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

            string? userInput;

            // Start the conversation
            Console.ForegroundColor = _consoleCfg.UserInputColor;
            Console.Write(_consoleCfg.SystemPrompt);

            while ((userInput = Console.ReadLine()) != null)
            {
                Console.ForegroundColor = clr;

                // Add user input
                history.AddUserMessage(userInput);

                // Enable auto function calling
                OpenAIPromptExecutionSettings openAIPromptExecutionSettings = new()
                {
                    ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
                };

                ChatMessageContent? result = null;

                try
                {
                    // Get the response from the AI
                    result = await chatCompletionService.GetChatMessageContentAsync(
                        history,
                        executionSettings: openAIPromptExecutionSettings,
                        kernel: kernel);

                    Console.ForegroundColor = _consoleCfg.AssistentMessageColor;

                    // Print the results
                    Console.WriteLine("Assistant > " + result);

                    // Add the message from the agent to the chat history
                    history.AddMessage(result.Role, result.Content ?? string.Empty);
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

        private void ImportPlugins(Kernel kernel, ChatHistory history)
        {

            // var pluginLib = GetPlugins(_config!);

            ////PluginManager? mgr = ActivatorUtilities.CreateInstance(_svcProvider, typeof(PluginManager), pluginLib) as PluginManager;
            ////if (mgr == null)
            ////    throw new NullReferenceException("The PluginManager cannot be created.");

            //var names = pluginLib.Plugins.Select(p => p.Name).ToList();

            var pluginInstances = _pluginMgr.CreateRequiredPlugins();

            kernel.ImportPluginFromObject(new TestConsolePlugin(kernel, history, _consoleCfg));

            foreach (var pluginObj in pluginInstances)
            {
                kernel.ImportPluginFromObject(pluginObj);
            }
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
            Kernel? kernel = TryGetOpenAIKernel();
            if (kernel == null)
                kernel = TryGetAzureKernel();

            if(kernel == null)
                throw new Exception("No valid kernel found.To initialize the kernel, please see documentation. Requred environment variables must be set.");

            return kernel;
        }


        private static Kernel? TryGetOpenAIKernel()
        {
            Kernel? kernel = null;
          
            if (Environment.GetEnvironmentVariable("OPENAI_CHATCOMPLETION_DEPLOYMENT") != null &&
               Environment.GetEnvironmentVariable("OPENAI_API_KEY") != null &&
               Environment.GetEnvironmentVariable("OPENAI_ORGID") != null)
            {


                kernel = Kernel.CreateBuilder()
                 .AddOpenAIChatCompletion(
                Environment.GetEnvironmentVariable("OPENAI_CHATCOMPLETION_DEPLOYMENT")!, // The name of your deployment (e.g., "gpt-3.5-turbo")
                Environment.GetEnvironmentVariable("OPENAI_API_KEY")!,
                Environment.GetEnvironmentVariable("OPENAI_ORGID")!)
            .Build();
            }

            return kernel;
        }

        private static Kernel? TryGetAzureKernel()
        {
            Kernel? kernel = null;

            if (Environment.GetEnvironmentVariable("AZURE_OPENAI_CHATCOMPLETION_DEPLOYMENT") != null &&
              Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") != null &&
              Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY") != null)
            {
                kernel = Kernel.CreateBuilder()
            .AddAzureOpenAIChatCompletion(
                Environment.GetEnvironmentVariable("AZURE_OPENAI_CHATCOMPLETION_DEPLOYMENT")!,  // The name of your deployment (e.g., "text-davinci-003")
                Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")!,    // The endpoint of your Azure OpenAI service
                Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")!      // The API key of your Azure OpenAI service
            )
            .Build();
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
