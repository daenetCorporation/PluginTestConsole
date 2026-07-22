using Daenet.LLMPlugin.TestConsole.Entities;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace Daenet.LLMPlugin.TestConsole
{
    /// <summary>
    /// Imports MCP tools defined in the configuration file and adds them to the shared tools list.
    /// McpClientTool inherits AIFunction (and therefore AITool), so no conversion is needed.
    /// </summary>
    internal class McpToolImporter
    {
        private readonly McpToolsConfig _mcpToolsCfg;
        private readonly ILogger<TestConsole> _logger;
        private readonly IList<AITool> _tools;
        private readonly ILogger<McpClientResilent>? _mcpClientLogger;

        /// <summary>
        /// Tracks which AIFunction instances were added for each MCP server name,
        /// so they can be removed and replaced on reconnection.
        /// </summary>
        private readonly Dictionary<string, IList<McpClientTool>> _importedByServer = new();

        /// <summary>
        /// Prevents re-entrant imports triggered by the reconnection callback.
        /// </summary>
        private bool _isImporting = false;

        public McpToolImporter(IList<AITool> tools, McpToolsConfig mcpToolsCfg,
            ILogger<TestConsole> logger,
            ILogger<McpClientResilent>? mcpClientLogger = null)
        {
            _tools = tools ?? throw new ArgumentNullException(nameof(tools));
            _mcpToolsCfg = mcpToolsCfg;
            _logger = logger;
            _mcpClientLogger = mcpClientLogger;
        }

        private void OnMcpServerStatusChanged(bool isConnected)
        {
            if (isConnected && !_isImporting)
                _ = ImportMcpTools();
        }

        public async Task ImportMcpTools()
        {
            try
            {
                _isImporting = true;

                if (_mcpToolsCfg == null || _mcpToolsCfg.McpServers == null)
                    return;

                var toolsDict = await ListMcpToolsAsync();

                foreach (var kvp in toolsDict)
                {
                    _logger.LogInformation($"MCP Server: {kvp.Key} has {kvp.Value.Count} tools.");

                    // Remove previously imported tools for this server.
                    if (_importedByServer.TryGetValue(kvp.Key, out var oldTools))
                    {
                        foreach (var oldTool in oldTools)
                            _tools.Remove(oldTool);
                    }

                    // Register and track the new tools.
                    _importedByServer[kvp.Key] = kvp.Value;
                    foreach (var tool in kvp.Value)
                        _tools.Add(tool);
                }
            }
            finally { _isImporting = false; }
        }

        private Dictionary<string, McpClientResilent> _mcpClients = new();

        /// <summary>
        /// Connects to each configured MCP server and retrieves its tool list.
        /// </summary>
        private async Task<Dictionary<string, IList<McpClientTool>>> ListMcpToolsAsync()
        {
            var toolsDict = new Dictionary<string, IList<McpClientTool>>();

            foreach (var mcpServer in _mcpToolsCfg?.McpServers!)
            {
                string mcpServerName = GetMcpServerName(mcpServer);
                var transport = GetTransportFromConfiguration(mcpServer);

                McpClientOptions options = new McpClientOptions();
                options.Capabilities = new()
                {
                     
                    //NotificationHandlers =
                    //[
                    //    new(NotificationMethods.ProgressNotification, (notification, cancellationToken) =>
                    //    {
                    //        return default;
                    //    })
                    //],
                };

                try
                {
                    if (!_mcpClients.ContainsKey(mcpServerName))
                    {
                        var newResilientClient = (McpClientResilent)await McpClientResilent.CreateAsync(transport!, options, _mcpClientLogger);
                        newResilientClient.SetOnConnectionStateChangedDelegate(OnMcpServerStatusChanged);
                        _mcpClients.Add(mcpServerName, newResilientClient);
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
            => mcpServer?.Name ?? $"MCP Server {mcpServer?.Url}";

        private static IClientTransport GetTransportFromConfiguration(McpServer? mcpServer)
        {
            if (!string.IsNullOrEmpty(mcpServer?.Url?.AbsoluteUri))
                return GetSseTransport(mcpServer);

            if (!string.IsNullOrEmpty(mcpServer?.Command) && mcpServer?.Arguments != null)
                return GetStdioTransport(mcpServer);

            throw new Exception("MCP server configuration is not valid. Either URL or Command must be set.");
        }

        private static IClientTransport GetStdioTransport(McpServer mcpServer)
        {
            var opts = new StdioClientTransportOptions
            {
                Name = GetDefaultMcpServerName(mcpServer!),
                Command = mcpServer?.Command!,
                Arguments = JsonSerializer.Deserialize<string[]>(mcpServer?.Arguments!),
            };
            return new StdioClientTransport(opts);
        }

        private static IClientTransport GetSseTransport(McpServer mcpServer)
        {
            var opts = new HttpClientTransportOptions
            {
                TransportMode = HttpTransportMode.AutoDetect,
                Name = GetDefaultMcpServerName(mcpServer),
                Endpoint = mcpServer.Url!,
                AdditionalHeaders = new Dictionary<string, string>()
            };

            if (mcpServer.ApiKey != null)
            {
                opts.AdditionalHeaders.Add("ApiKey", mcpServer.ApiKey);

                if (!string.IsNullOrEmpty(mcpServer.ImpersonatingUser))
                    opts.AdditionalHeaders.Add("ImpersonatingUser", mcpServer.ImpersonatingUser);
            }

            return new HttpClientTransport(opts);
        }

        private static string GetDefaultMcpServerName(McpServer mcpServer)
            => mcpServer.Name ?? $"MCP Server {mcpServer.Url}";
    }
}