using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Daenet.LLMPlugin.TestConsole
{
    public class McpClientResilent : IMcpClient
    {
        private Task? _retryingWorkerTask;

        private IMcpClient? _mcpClient;

        private readonly IClientTransport _transport;

        private readonly McpClientOptions _options;

        private Action<bool>? _onConnectionStateChanged;

        private readonly ILogger<McpClientResilent>? _logger;

        public ServerCapabilities ServerCapabilities
        {
            get
            {
                ThrowIfNotConnected();
                return _mcpClient!.ServerCapabilities;
            }
        }


        public Implementation ServerInfo
        {
            get
            {
                ThrowIfNotConnected();
                return _mcpClient!.ServerInfo;
            }
        }


        public string? ServerInstructions
        {
            get
            {
                ThrowIfNotConnected();
                return _mcpClient!.ServerInstructions;
            }
        }

        public string? SessionId
        {
            get
            {
                ThrowIfNotConnected();
                return _mcpClient!.SessionId;
            }
        }

        /// <summary>
        /// True if the MCP Server is connected.
        /// </summary>
        public bool IsConnected { get; private set; }

        public void SetOnConnectionStateChangedDelegate(Action<bool> onConnectionStateChanged)
        {
            _onConnectionStateChanged = onConnectionStateChanged;
        }

        private McpClientResilent(IMcpClient? underlyingMcpClient,
            IClientTransport transport, McpClientOptions options,
            ILogger<McpClientResilent>? logger = null)
        {
            _mcpClient = underlyingMcpClient;

            _transport = transport;

            _options = options;

            _retryingWorkerTask = RunRetryingWorkerAsync(CancellationToken.None);
        }

        public static async Task<IMcpClient> CreateAsync(IClientTransport transport, McpClientOptions options,
            ILogger<McpClientResilent>? mcpClientLogger = null)
        {
            IMcpClient mcpClient = null!;

            try
            {
                mcpClient = await McpClientFactory.CreateAsync(transport!, options);
            }
            catch (Exception ex)
            {
                mcpClientLogger?.LogWarning($"Ping to the MCP Server '{options?.ClientInfo?.Name}' has failed.");
            }

            McpClientResilent resilentClient = new McpClientResilent(mcpClient, transport, options, mcpClientLogger);

            return resilentClient;
        }

        public ValueTask DisposeAsync()
        {
            if (_mcpClient == null)
                return ValueTask.CompletedTask;

            return _mcpClient.DisposeAsync();
        }

        public IAsyncDisposable RegisterNotificationHandler(string method, Func<JsonRpcNotification, CancellationToken, ValueTask> handler)
        {
            ThrowIfNotConnected();

            return _mcpClient!.RegisterNotificationHandler(method, handler);
        }

        public Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
        {
            ThrowIfNotConnected();

            return _mcpClient!.SendMessageAsync(message, cancellationToken);
        }

        public Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken = default)
        {
            ThrowIfNotConnected();

            return _mcpClient!.SendRequestAsync(request, cancellationToken);
        }

        private void ThrowIfNotConnected()
        {
            if (_mcpClient == null)
                throw new InvalidOperationException("MCP client is not connected!");
        }

        protected async Task RunRetryingWorkerAsync(CancellationToken cancelationToken)
        {
            while (cancelationToken.IsCancellationRequested == false)
            {
                await PingOrReconnectClient();

                await Task.Delay(10000);
            }
        }

        private async Task PingOrReconnectClient()
        {
            IMcpClient mcpPingClient = null!;

            try
            {
                if (_mcpClient == null)
                    mcpPingClient = await McpClientFactory.CreateAsync(_transport, _options);
                else
                    mcpPingClient = _mcpClient;


                await PingServerAsync(mcpPingClient);

                if (_mcpClient == null)
                    _mcpClient = mcpPingClient;

                if (_onConnectionStateChanged != null && !IsConnected)
                {
                    _onConnectionStateChanged(true);
                    IsConnected = true;
                }
            }
            catch
            {
                _logger?.LogWarning($"Ping to the MCP Server '{_options?.ClientInfo?.Name}' has failed.");

                if (_onConnectionStateChanged != null && IsConnected)
                {
                    _onConnectionStateChanged(false);
                    IsConnected = false;
                }

                _mcpClient = null;
            }
        }

        /// <summary>
        /// Sends the ping request to the MCP server to check if it is alive.
        /// </summary>
        /// <param name="mcpClient"></param>
        /// <returns></returns>
        private async Task PingServerAsync(IMcpClient mcpClient)
        {
            _logger?.LogTrace($"Pinging the MCP Server '{mcpClient.ServerInfo.Title}'.");

            JsonRpcRequest pingReq = new JsonRpcRequest
            {
                JsonRpc = "2.0",
                Id = new RequestId(Guid.NewGuid().ToString()),
                Method = "ping",
            };

            JsonRpcResponse res = await mcpClient.SendRequestAsync(pingReq);

            _logger?.LogTrace($"MCP Server '{mcpClient.ServerInfo.Title}' available.");
        }
    }
}
