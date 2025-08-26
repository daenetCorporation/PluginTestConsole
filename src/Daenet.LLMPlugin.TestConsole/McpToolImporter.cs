using Daenet.LLMPlugin.TestConsole.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Daenet.LLMPlugin.TestConsole
{
    /// <summary>
    /// Provides functionality for importing MCP Tools definmed in the configuration file.
    /// </summary>
    /// <remarks>This class is intended for internal use and facilitates operations related to MCP tool data
    /// import. It is not designed for direct use by external consumers.</remarks>
    internal class McpToolImporter
    {
        private readonly McpToolsConfig _mcpToolsCfg;

        private readonly ILogger<TestConsole> _logger;

        private readonly Kernel _kernel;

        public McpToolImporter(Kernel kernel, McpToolsConfig mcpToolsCfg, ILogger<TestConsole> logger)
        {
            _mcpToolsCfg = mcpToolsCfg;
            _logger = logger;
            _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel), "Kernel cannot be null.");
        }

        public async Task ImportMcpTools()
        {
#pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

            if (_mcpToolsCfg == null || _mcpToolsCfg.McpServers == null)
                return;

            var toolsDict = ListMcpTools();

            foreach (var kvp in await toolsDict)
            {
                _logger.LogInformation($"MCP Server: {kvp.Key} has {kvp.Value.Count} tools.");
                _kernel.Plugins.AddFromFunctions(kvp.Key, kvp.Value.Select(aiFunction =>
                {
                    var kernelFunc = aiFunction.AsKernelFunction();
                    
                    return kernelFunc;
                }
                ));

            }
#pragma warning restore SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

        }

 

        /// <summary>
        /// Traverse the MCP server configuration, lists all tools available on the MCP servers 
        /// and returns them to the kernel as kernel plugin tools.
        /// </summary>
        /// <returns>The list of imported tools.</returns>
        /// <exception cref="Exception"></exception>
        private async Task<Dictionary<string, IList<McpClientTool>>> ListMcpTools()
        {
            Dictionary<string, IList<McpClientTool>> dict = new Dictionary<string, IList<McpClientTool>>();

            IClientTransport? transport = null;

            foreach (var mcpServer in _mcpToolsCfg?.McpServers!)
            {
                transport = GetTransportFromConfiguration(mcpServer);

                McpClientOptions options = new McpClientOptions();
                options.Capabilities = new()
                {    
                    NotificationHandlers = [
                    new(NotificationMethods.ProgressNotification, (notification, cancellationToken) =>
                    {

                        //notificationReceived.TrySetResult(notification);
                        return default;
                    })],
                };

                try
                {
                    var mcpClient = await McpClientFactory.CreateAsync(transport!, options);
                    
                    //mcpClient.ServerCapabilities.NotificationHandlers.
                    var mcpTools = await mcpClient.ListToolsAsync();

                    dict.Add(mcpServer?.Name ?? $"MCP Server {mcpServer?.Url}", mcpTools);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to connect to MCP Server: {mcpServer.Name ?? "Name not specified"} - {mcpServer.Url}");
                    //throw new Exception($"The MCP Server cannot be connected: {mcpServer.Name ?? "Name not specified"} - {mcpServer.Url}");
                }
            }

            return dict;
        }

        private static IClientTransport GetTransportFromConfiguration(McpServer? mcpServer)
        {
            IClientTransport transport;
            if (!String.IsNullOrEmpty(mcpServer?.Url?.AbsoluteUri))
            {
                transport = GetSseTransport(mcpServer);
            }
            else if (!String.IsNullOrEmpty(mcpServer?.Command) && mcpServer?.Arguments != null)
            {
                transport = GetStdioTransport(mcpServer);
            }
            else
            {
                throw new Exception("MCP server configuration is not valid. Either URL or Command must be set.");
            }

            return transport;
        }

        private static IClientTransport GetStdioTransport(McpServer mcpServer)
        {
            IClientTransport transport;
            var opts = new StdioClientTransportOptions
            {
                Name = GetDefaultMCPServerName(mcpServer!),
                Command = mcpServer?.Command!,
                Arguments = JsonSerializer.Deserialize<string[]>(mcpServer?.Arguments!),
            };

            transport = new StdioClientTransport(opts);
            return transport;
        }

        private static IClientTransport GetSseTransport(McpServer mcpServer)
        {
            IClientTransport transport;
            SseClientTransportOptions opts = new SseClientTransportOptions
            {
                Name = GetDefaultMCPServerName(mcpServer),
                Endpoint = mcpServer.Url!,
                AdditionalHeaders = new Dictionary<string, string>()
            };

            if (mcpServer.ApiKey != null)
            {
                opts.AdditionalHeaders.Add("ApiKey", mcpServer.ApiKey);

                if(!string.IsNullOrEmpty(mcpServer.ImpersonatingUser))
                {
                    opts.AdditionalHeaders.Add("ImpersonatingUser", mcpServer.ImpersonatingUser);
                }
            }

            transport = new SseClientTransport(opts);
            return transport;
        }

        private static string GetDefaultMCPServerName(McpServer mcpServer)
        {
            return mcpServer.Name ?? $"MCP Server {mcpServer.Url}";
        }
    }
}
