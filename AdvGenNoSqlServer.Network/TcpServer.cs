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
using Microsoft.Extensions.Logging;

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
        private int _inFlightMessages;

        /// <summary>
        /// Number of messages currently being handled across all connections.
        /// Watched during shutdown so in-flight work can drain before connections close.
        /// </summary>
        public int InFlightMessageCount => Volatile.Read(ref _inFlightMessages);

        /// <summary>
        /// Server configuration
        /// </summary>
        public ServerConfiguration Configuration { get; }

        private readonly Microsoft.Extensions.Logging.ILogger? _logger;

        private void LogError(string message, Exception? ex = null)
        {
            if (_logger != null)
            {
                _logger.LogError(ex, "{Message}", message);
            }
            else
            {
                Console.Error.WriteLine(ex != null ? $"{message}: {ex.Message}" : message);
            }
        }

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
        /// Connection pool statistics (acquired/released totals, utilization).
        /// </summary>
        public ConnectionPoolStatistics PoolStatistics => _connectionPool.GetStatistics();

        /// <summary>
        /// Creates a new TCP server with the specified configuration
        /// </summary>
        public TcpServer(ServerConfiguration configuration, Microsoft.Extensions.Logging.ILogger? logger = null)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger;
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

            // Drain: give in-flight message handlers a bounded grace period to finish
            // before their connections are closed underneath them.
            var drainTimeout = TimeSpan.FromSeconds(Math.Max(0, Configuration.ShutdownDrainSeconds));
            var drainDeadline = DateTime.UtcNow.Add(drainTimeout);
            while (InFlightMessageCount > 0 && DateTime.UtcNow < drainDeadline)
            {
                await Task.Delay(50);
            }

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
                    LogError("Error accepting connection", ex);
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
            ConnectionHandler? handler = null;

            try
            {
                // Perform SSL handshake if SSL is enabled
                Stream stream;
                if (Configuration.EnableSsl)
                {
                    try
                    {
                        var sslStream = await TlsStreamHelper.CreateServerSslStreamAsync(
                            client, Configuration, cancellationToken);
                        stream = sslStream;
                    }
                    catch (Exception ex)
                    {
                        LogError($"SSL handshake failed for connection {connectionId}", ex);
                        // Send plain text error if SSL fails
                        await SendSslErrorAsync(client, "SSL handshake failed");
                        client.Dispose();
                        return;
                    }
                }
                else
                {
                    stream = client.GetStream();
                }

                if (!_connectionPool.TryAcquire())
                {
                    // For rejected connection without handler yet, we can temporarily create it to send error
                    handler = new ConnectionHandler(connectionId, client, stream, _messageProtocol, Configuration, _logger);
                    await SendConnectionRejectedAsync(handler, "Server at maximum capacity");
                    handler.Dispose();
                    return;
                }

                handler = new ConnectionHandler(connectionId, client, stream, _messageProtocol, Configuration, _logger);

                _activeConnections.TryAdd(connectionId, handler);
                ConnectionEstablished?.Invoke(this, new ConnectionEventArgs(connectionId, client, handler.IsSecure));

                await ProcessConnectionLoopAsync(handler, cancellationToken);
            }
            finally
            {
                if (handler != null)
                {
                    bool wasActive = _activeConnections.TryRemove(connectionId, out _);
                    if (wasActive)
                    {
                        try
                        {
                            _connectionPool.Release();
                        }
                        catch (SemaphoreFullException)
                        {
                            // Ignore
                        }
                        ConnectionClosed?.Invoke(this, new ConnectionEventArgs(connectionId, client, handler?.IsSecure ?? false));
                    }
                    handler?.Dispose();
                }
                else
                {
                    client.Dispose();
                }
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

                    Interlocked.Increment(ref _inFlightMessages);
                    try
                    {
                        MessageReceived?.Invoke(this, eventArgs);

                        // Wait for the event handler to send a response before reading the next message
                        // This ensures the async event handler completes before we continue
                        await eventArgs.WaitForResponseAsync(cancellationToken);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _inFlightMessages);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }
            catch (Exception ex)
            {
                LogError($"Connection error ({handler.ConnectionId})", ex);
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

        private async Task SendSslErrorAsync(TcpClient client, string reason)
        {
            try
            {
                // Send a plain text error before SSL is established
                var stream = client.GetStream();
                var errorMessage = $"HTTP/1.1 400 Bad Request\r\nContent-Type: text/plain\r\n\r\nSSL/TLS Required: {reason}";
                var errorBytes = System.Text.Encoding.UTF8.GetBytes(errorMessage);
                await stream.WriteAsync(errorBytes);
                await stream.FlushAsync();
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
        /// Raises a connection's frame payload limit to the configured post-auth maximum
        /// (MaxMessageSizeMb). Called by the server once the connection has authenticated.
        /// </summary>
        public void RaisePayloadLimit(string connectionId)
        {
            if (_activeConnections.TryGetValue(connectionId, out var handler))
            {
                handler.MaxPayloadBytes = (long)Configuration.MaxMessageSizeMb * 1024 * 1024;
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

        /// <summary>
        /// Whether the connection is using SSL/TLS encryption
        /// </summary>
        public bool IsSecure { get; }

        public ConnectionEventArgs(string connectionId, TcpClient client, bool isSecure = false)
        {
            ConnectionId = connectionId;
            Client = client;
            IsSecure = isSecure;
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
