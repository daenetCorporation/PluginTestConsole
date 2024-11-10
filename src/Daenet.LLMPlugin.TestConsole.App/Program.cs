
using Daenet.LLMPlugin.Common;
using Daenet.LLMPlugin.TestConsole;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Daenet.LLMPlugin.TestConsole.App
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            await Sample1(args);
        }

        private static async Task Sample1(string[] args)
        {
            var cfg = InitializeConfig(args);

            // Set up a service collection for dependency injection.
            var serviceCollection = new ServiceCollection();

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
                SystemMessage = $"You are the agent who provide informaiton for user's intent and invoke plugin functions. Today is {DateTime.Now}."
            });

            // Register the configuration of the built-in plugin.
            serviceCollection.AddSingleton<TestConsole>();

            UseSemanticSearchApi(cfg, serviceCollection);

           // Creates the instance of Semantic kernel and register it for DI.
            // This is required if there is at least a single plugin, which requires Semantik Kernel.
            TestConsole.UseSemantikKernel(serviceCollection);

            // Build the service provider.
            var serviceProvider = serviceCollection.BuildServiceProvider();

            // Get an instance of TestConsole from the service provider.
            var testConsole = serviceProvider.GetRequiredService<TestConsole>();

            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
            
            logger.LogInformation("Application running...");
            
            // Call the RunAsync method on the TestConsole instance.
            await testConsole.RunAsync();
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

        private static void UseSemanticSearchApi(IConfiguration configuration, ServiceCollection serviceCollection)
        {
            //
            //SearchApi.UseSemantSearchApi(configuration, serviceCollection);
        }

    }
}
