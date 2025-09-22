using Azure.Core;
using Daenet.LLMPlugin.TestConsole.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static System.Net.WebRequestMethods;

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

        private readonly ILogger<McpClientResilent>? _mcpClientLogger = null;

        public McpToolImporter(Kernel kernel, McpToolsConfig mcpToolsCfg,
            ILogger<TestConsole> logger,
            ILogger<McpClientResilent>? mcpClientLogger = null)
        {
            _mcpToolsCfg = mcpToolsCfg;
            _logger = logger;
            _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel), "Kernel cannot be null.");
            _mcpClientLogger = mcpClientLogger;
        }

        /// <summary>
        /// It prevents that Import is invoked withing import then the MCP Server is connected and change its status.
        /// </summary>
        private bool _isImporting = false;

        private void OnMcpServerStatusChanged(bool isConnected)
        {
            // Every time one of MCP servers is reconnected, we have to recreate the kernel.
            if (isConnected && !_isImporting)
                _ = ImportMcpTools();
        }

        public async Task ImportMcpTools()
        {
            try
            {
                _isImporting = true;

#pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

                if (_mcpToolsCfg == null || _mcpToolsCfg.McpServers == null)
                    return;

                var mcpTools = ListMcpTools();

                foreach (var kvp in await mcpTools)
                {
                    _logger.LogInformation($"MCP Server: {kvp.Key} has {kvp.Value.Count} tools.");

                    RemovePluginIfExist(kvp.Key);

                    _kernel.Plugins.AddFromFunctions(kvp.Key, kvp.Value.Select(aiFunction =>
                    {
                        var kernelFunc = aiFunction.AsKernelFunction();

                        return kernelFunc;
                    }
                    ));

                }
#pragma warning restore SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            }
            finally { _isImporting = false; }

        }

        private void RemovePluginIfExist(string pluginName)
        {
            foreach (var plugin in new List<KernelPlugin>(_kernel.Plugins))
            {
                if (plugin.Name == pluginName)
                    _kernel.Plugins.Remove(plugin);
            }
        }

        private Dictionary<string, McpClientResilent> _mcpClients = new Dictionary<string, McpClientResilent>();

        /// <summary>
        /// Traverse the MCP server configuration, lists all tools available on the MCP servers 
        /// and returns them to the kernel as kernel plugin tools.
        /// </summary>
        /// <returns>The list of imported tools.</returns>
        /// <exception cref="Exception"></exception>
        private async Task<Dictionary<string, IList<McpClientTool>>> ListMcpTools()
        {
            Dictionary<string, IList<McpClientTool>> toolsDict = new Dictionary<string, IList<McpClientTool>>();

            IClientTransport? transport = null;

            foreach (var mcpServer in _mcpToolsCfg?.McpServers!)
            {
                string mcpServerName = GetMcpServerName(mcpServer);

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
                    if (_mcpClients.ContainsKey(mcpServerName) == false)
                    {
                        var newResilentMcpClient = (McpClientResilent)await McpClientResilent.CreateAsync(transport!, options, _mcpClientLogger);

                        newResilentMcpClient.SetOnConnectionStateChangedDelegate(OnMcpServerStatusChanged);

                        _mcpClients.Add(mcpServerName, newResilentMcpClient);
                    }

                    var mcpTools = await _mcpClients[mcpServerName].ListToolsAsync();

                    toolsDict[mcpServerName] = mcpTools;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to connect to MCP Server: {mcpServer.Name ?? "Name not specified"} - {mcpServer.Url}");
                }
            }

            return toolsDict;
        }

        private static string GetMcpServerName(McpServer mcpServer)
        {
            return mcpServer?.Name ?? $"MCP Server {mcpServer?.Url}";
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

                if (!string.IsNullOrEmpty(mcpServer.ImpersonatingUser))
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
