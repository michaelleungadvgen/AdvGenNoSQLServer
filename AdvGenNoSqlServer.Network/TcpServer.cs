// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Configuration;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace AdvGenNoSqlServer.Network
{
    /// <summary>
    /// Async TCP Server for NoSQL database connections
    /// </summary>
    public class TcpServer : IDisposable, IAsyncDisposable
    {
        private TcpListener? _listener;
        private readonly ConcurrentDictionary<string, ConnectionHandler> _activeConnections;
        private readonly ConnectionPool _connectionPool;
        private readonly MessageProtocol _messageProtocol;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _acceptTask;
        private bool _isRunning;
        private readonly object _startStopLock = new();

        /// <summary>
        /// Server configuration
        /// </summary>
        public ServerConfiguration Configuration { get; }

        /// <summary>
        /// Event raised when a new connection is established
        /// </summary>
        public event EventHandler<ConnectionEventArgs>? ConnectionEstablished;

        /// <summary>
        /// Event raised when a connection is closed
        /// </summary>
        public event EventHandler<ConnectionEventArgs>? ConnectionClosed;

        /// <summary>
        /// Event raised when a message is received from a client
        /// </summary>
        public event EventHandler<MessageReceivedEventArgs>? MessageReceived;

        /// <summary>
        /// Number of active connections
        /// </summary>
        public int ActiveConnectionCount => _activeConnections.Count;

        /// <summary>
        /// Whether the server is currently running
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// Creates a new TCP server with the specified configuration
        /// </summary>
        public TcpServer(ServerConfiguration configuration)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _activeConnections = new ConcurrentDictionary<string, ConnectionHandler>();
            _connectionPool = new ConnectionPool(configuration.MaxConcurrentConnections);
            _messageProtocol = new MessageProtocol();
        }

        /// <summary>
        /// Starts the TCP server and begins accepting connections
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            lock (_startStopLock)
            {
                if (_isRunning)
                    throw new InvalidOperationException("Server is already running");

                _isRunning = true;
                _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            }

            try
            {
                _listener = new TcpListener(IPAddress.Parse(Configuration.Host), Configuration.Port);
                _listener.Start();

                // Accept connections in background
                _acceptTask = AcceptConnectionsAsync(_cancellationTokenSource.Token);

                await Task.CompletedTask;
            }
            catch (Exception)
            {
                _isRunning = false;
                throw;
            }
        }

        /// <summary>
        /// Stops the TCP server gracefully
        /// </summary>
        public async Task StopAsync(TimeSpan timeout = default)
        {
            lock (_startStopLock)
            {
                if (!_isRunning)
                    return;

                _isRunning = false;
            }

            if (timeout == default)
                timeout = TimeSpan.FromSeconds(30);

            // Signal cancellation
            _cancellationTokenSource?.Cancel();

            // Stop accepting new connections
            _listener?.Stop();

            // Wait for accept task to complete
            if (_acceptTask != null)
            {
                try
                {
                    await Task.WhenAny(_acceptTask, Task.Delay(timeout));
                }
                catch (OperationCanceledException)
                {
                    // Expected during shutdown
                }
            }

            // Close all active connections
            var closeTasks = _activeConnections.Values.Select(conn => 
                conn.CloseAsync().AsTask()).ToList();

            if (closeTasks.Count > 0)
            {
                await Task.WhenAll(closeTasks);
            }

            _activeConnections.Clear();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }

        private async Task AcceptConnectionsAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _isRunning)
            {
                try
                {
                    var client = await _listener!.AcceptTcpClientAsync(cancellationToken);
                    
                    // Configure socket options for performance
                    ConfigureSocket(client);

                    _ = HandleConnectionAsync(client, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Log error but continue accepting connections
                    Console.Error.WriteLine($"Error accepting connection: {ex.Message}");
                    await Task.Delay(100, cancellationToken);
                }
            }
        }

        private void ConfigureSocket(TcpClient client)
        {
            client.NoDelay = true; // Disable Nagle's algorithm
            client.ReceiveBufferSize = Configuration.ReceiveBufferSize;
            client.SendBufferSize = Configuration.SendBufferSize;
            client.ReceiveTimeout = (int)Configuration.ConnectionTimeout.TotalMilliseconds;
            client.SendTimeout = (int)Configuration.ConnectionTimeout.TotalMilliseconds;
            
            if (client.Client.AddressFamily == AddressFamily.InterNetwork ||
                client.Client.AddressFamily == AddressFamily.InterNetworkV6)
            {
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            }
        }

        private async Task HandleConnectionAsync(TcpClient client, CancellationToken cancellationToken)
        {
            var connectionId = Guid.NewGuid().ToString("N");
            var handler = new ConnectionHandler(connectionId, client, _messageProtocol, Configuration);

            if (!_connectionPool.TryAcquire())
            {
                await SendConnectionRejectedAsync(handler, "Server at maximum capacity");
                handler.Dispose();
                return;
            }

            _activeConnections.TryAdd(connectionId, handler);
            ConnectionEstablished?.Invoke(this, new ConnectionEventArgs(connectionId, client));

            try
            {
                await ProcessConnectionLoopAsync(handler, cancellationToken);
            }
            finally
            {
                _activeConnections.TryRemove(connectionId, out _);
                _connectionPool.Release();
                ConnectionClosed?.Invoke(this, new ConnectionEventArgs(connectionId, client));
                handler.Dispose();
            }
        }

        private async Task ProcessConnectionLoopAsync(ConnectionHandler handler, CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var message in handler.ReadMessagesAsync(cancellationToken))
                {
                    var eventArgs = new MessageReceivedEventArgs(
                        handler.ConnectionId,
                        handler.Client,
                        message,
                        async (response) =>
                        {
                            await handler.SendAsync(response, cancellationToken);
                        });

                    MessageReceived?.Invoke(this, eventArgs);

                    // Wait for the event handler to send a response before reading the next message
                    // This ensures the async event handler completes before we continue
                    await eventArgs.WaitForResponseAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Connection error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.Error.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
            }
        }

        private async Task SendConnectionRejectedAsync(ConnectionHandler handler, string reason)
        {
            try
            {
                var rejectionMessage = new NoSqlMessage
                {
                    MessageType = MessageType.Error,
                    Payload = System.Text.Encoding.UTF8.GetBytes($"{{\"error\":\"{reason}\"}}")
                };
                await handler.SendAsync(rejectionMessage, CancellationToken.None);
            }
            catch { /* Best effort */ }
        }

        /// <summary>
        /// Broadcasts a message to all connected clients
        /// </summary>
        public async Task BroadcastAsync(NoSqlMessage message, CancellationToken cancellationToken = default)
        {
            var tasks = _activeConnections.Values.Select(conn => 
                conn.SendAsync(message, cancellationToken).AsTask());
            
            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Sends a message to a specific client
        /// </summary>
        public async Task SendToClientAsync(string connectionId, NoSqlMessage message, CancellationToken cancellationToken = default)
        {
            if (_activeConnections.TryGetValue(connectionId, out var handler))
            {
                await handler.SendAsync(message, cancellationToken);
            }
            else
            {
                throw new InvalidOperationException($"Connection {connectionId} not found");
            }
        }

        /// <summary>
        /// Disposes the server and all resources
        /// </summary>
        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Asynchronously disposes the server and all resources
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (_isRunning)
            {
                await StopAsync();
            }

            _cancellationTokenSource?.Dispose();
            _listener?.Stop();
            
            foreach (var handler in _activeConnections.Values)
            {
                handler.Dispose();
            }
            _activeConnections.Clear();
        }
    }

    /// <summary>
    /// Event args for connection events
    /// </summary>
    public class ConnectionEventArgs : EventArgs
    {
        /// <summary>
        /// Unique connection identifier
        /// </summary>
        public string ConnectionId { get; }

        /// <summary>
        /// The TCP client
        /// </summary>
        public TcpClient Client { get; }

        public ConnectionEventArgs(string connectionId, TcpClient client)
        {
            ConnectionId = connectionId;
            Client = client;
        }
    }

    /// <summary>
    /// Event args for message received events
    /// </summary>
    public class MessageReceivedEventArgs : EventArgs
    {
        private readonly TaskCompletionSource _responseSent = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Func<NoSqlMessage, Task> _sendResponse;

        /// <summary>
        /// Connection ID that sent the message
        /// </summary>
        public string ConnectionId { get; }

        /// <summary>
        /// TCP client
        /// </summary>
        public TcpClient Client { get; }

        /// <summary>
        /// The received message
        /// </summary>
        public NoSqlMessage Message { get; }

        /// <summary>
        /// Function to send a response. Must be called by the event handler.
        /// </summary>
        public async Task SendResponseAsync(NoSqlMessage response)
        {
            try
            {
                await _sendResponse(response);
            }
            finally
            {
                _responseSent.TrySetResult();
            }
        }

        /// <summary>
        /// Waits for the response to be sent by the event handler
        /// </summary>
        internal Task WaitForResponseAsync(CancellationToken cancellationToken = default)
        {
            return _responseSent.Task.WaitAsync(cancellationToken);
        }

        /// <summary>
        /// Marks the response as complete without sending (for handlers that don't need to respond)
        /// </summary>
        public void CompleteWithoutResponse()
        {
            _responseSent.TrySetResult();
        }

        public MessageReceivedEventArgs(
            string connectionId,
            TcpClient client,
            NoSqlMessage message,
            Func<NoSqlMessage, Task> sendResponse)
        {
            ConnectionId = connectionId;
            Client = client;
            Message = message;
            _sendResponse = sendResponse;
        }
    }
}
