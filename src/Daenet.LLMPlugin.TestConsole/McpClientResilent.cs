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
    public class McpClientResilent : McpClient
    {
        private Task? _retryingWorkerTask;

        private McpClient? _mcpClient;

        private readonly IClientTransport _transport;

        private readonly McpClientOptions _options;

        private Action<bool>? _onConnectionStateChanged;

        private readonly ILogger<McpClientResilent>? _logger;

        public override ServerCapabilities ServerCapabilities
        {
            get
            {
                ThrowIfNotConnected();
                return _mcpClient!.ServerCapabilities;
            }
        }
 
        public override Implementation ServerInfo
        {
            get
            {
                ThrowIfNotConnected();
                return _mcpClient!.ServerInfo;
            }
        }


        public override string? ServerInstructions
        {
            get
            {
                ThrowIfNotConnected();
                return _mcpClient!.ServerInstructions;
            }
        }

        public override string? SessionId
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


        public override string? NegotiatedProtocolVersion => throw new NotImplementedException();

    

        public void SetOnConnectionStateChangedDelegate(Action<bool> onConnectionStateChanged)
        {
            _onConnectionStateChanged = onConnectionStateChanged;
        }

        private McpClientResilent(McpClient? underlyingMcpClient,
            IClientTransport transport, McpClientOptions options,
            ILogger<McpClientResilent>? logger = null)
        {
            _mcpClient = underlyingMcpClient;

            _transport = transport;

            _options = options;

            _retryingWorkerTask = RunRetryingWorkerAsync(CancellationToken.None);
        }

        public static async Task<McpClient> CreateAsync(IClientTransport transport, McpClientOptions options,
            ILogger<McpClientResilent>? mcpClientLogger = null)
        {
            McpClient mcpClient = null!;

            try
            {
                mcpClient = await McpClient.CreateAsync(transport!, options);
            }
            catch (Exception ex)
            {
                mcpClientLogger?.LogWarning($"Ping to the MCP Server '{options?.ClientInfo?.Name}' has failed.");
            }

            McpClientResilent resilentClient = new McpClientResilent(mcpClient, transport, options, mcpClientLogger);

            return resilentClient;
        }

        public override ValueTask DisposeAsync()
        {
            if (_mcpClient == null)
                return ValueTask.CompletedTask;

            return _mcpClient.DisposeAsync();
        }

        public override IAsyncDisposable RegisterNotificationHandler(string method, Func<JsonRpcNotification, CancellationToken, ValueTask> handler)
        {
            ThrowIfNotConnected();

            return _mcpClient!.RegisterNotificationHandler(method, handler);
        }

        public override Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
        {
            ThrowIfNotConnected();

            return _mcpClient!.SendMessageAsync(message, cancellationToken);
        }

        public override Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken = default)
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
            McpClient mcpPingClient = null!;

            try
            {
                if (_mcpClient == null)
                    mcpPingClient = await McpClient.CreateAsync(_transport, _options);
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
        private async Task PingServerAsync(McpClient mcpClient)
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
