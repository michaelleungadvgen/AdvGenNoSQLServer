// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Buffers;
using System.Net;
using System.Net.Sockets;
using AdvGenNoSqlServer.Network;

namespace AdvGenNoSqlServer.Client
{
    /// <summary>
    /// Exception thrown when a client connection error occurs
    /// </summary>
    public class NoSqlClientException : Exception
    {
        public NoSqlClientException(string message) : base(message) { }
        public NoSqlClientException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Exception thrown when a protocol error occurs
    /// </summary>
    public class NoSqlProtocolException : NoSqlClientException
    {
        public NoSqlProtocolException(string message) : base(message) { }
        public NoSqlProtocolException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Response from the NoSQL server
    /// </summary>
    public class NoSqlResponse
    {
        /// <summary>
        /// Whether the operation was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Response data (if any)
        /// </summary>
        public object? Data { get; set; }

        /// <summary>
        /// Error information (if operation failed)
        /// </summary>
        public NoSqlError? Error { get; set; }
    }

    /// <summary>
    /// Error information from the server
    /// </summary>
    public class NoSqlError
    {
        /// <summary>
        /// Error code
        /// </summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>
        /// Error message
        /// </summary>
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// A client for interacting with the NoSQL server over TCP
    /// </summary>
    public class AdvGenNoSqlClient : IDisposable, IAsyncDisposable
    {
        private readonly string _serverAddress;
        private readonly int _serverPort;
        private readonly AdvGenNoSqlClientOptions _options;
        private readonly MessageProtocol _messageProtocol;
        private TcpClient? _tcpClient;
        private NetworkStream? _networkStream;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly SemaphoreSlim _receiveLock = new(1, 1);
        private bool _isConnected;
        private CancellationTokenSource? _keepAliveCts;
        private Task? _keepAliveTask;

        /// <summary>
        /// Gets whether the client is currently connected to the server
        /// </summary>
        public bool IsConnected => _isConnected && _tcpClient?.Connected == true;

        /// <summary>
        /// Event raised when the client connects to the server
        /// </summary>
        public event EventHandler? Connected;

        /// <summary>
        /// Event raised when the client disconnects from the server
        /// </summary>
        public event EventHandler? Disconnected;

        /// <summary>
        /// Event raised when an error occurs
        /// </summary>
        public event EventHandler<NoSqlClientException>? ErrorOccurred;

        /// <summary>
        /// Initializes a new instance of the <see cref="AdvGenNoSqlClient"/> class.
        /// </summary>
        /// <param name="serverAddress">The server address to connect to (format: "host:port").</param>
        public AdvGenNoSqlClient(string serverAddress)
            : this(serverAddress, new AdvGenNoSqlClientOptions())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AdvGenNoSqlClient"/> class.
        /// </summary>
        /// <param name="serverAddress">The server address to connect to (format: "host:port").</param>
        /// <param name="options">Client options.</param>
        public AdvGenNoSqlClient(string serverAddress, AdvGenNoSqlClientOptions options)
        {
            if (string.IsNullOrWhiteSpace(serverAddress))
                throw new ArgumentNullException(nameof(serverAddress));

            _options = options ?? throw new ArgumentNullException(nameof(options));
            _messageProtocol = new MessageProtocol();

            // Parse server address
            var parts = serverAddress.Split(':');
            _serverAddress = parts[0];
            _serverPort = parts.Length > 1 && int.TryParse(parts[1], out var port) ? port : 9090;
        }

        /// <summary>
        /// Connects to the NoSQL server.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        /// <exception cref="NoSqlClientException">Thrown when connection fails.</exception>
        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (_isConnected)
                throw new InvalidOperationException("Client is already connected");

            try
            {
                _tcpClient = new TcpClient
                {
                    NoDelay = true, // Disable Nagle's algorithm for low latency
                    ReceiveBufferSize = 65536,
                    SendBufferSize = 65536
                };

                // Configure receive/send timeouts
                _tcpClient.ReceiveTimeout = _options.ConnectionTimeout;
                _tcpClient.SendTimeout = _options.ConnectionTimeout;

                // Connect to server
                await _tcpClient.ConnectAsync(_serverAddress, _serverPort);
                _networkStream = _tcpClient.GetStream();

                // Perform handshake
                await PerformHandshakeAsync(cancellationToken);

                _isConnected = true;

                // Start keep-alive mechanism
                if (_options.EnableKeepAlive)
                {
                    _keepAliveCts = new CancellationTokenSource();
                    _keepAliveTask = RunKeepAliveAsync(_keepAliveCts.Token);
                }

                Connected?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex) when (ex is not NoSqlClientException)
            {
                CleanupConnection();
                throw new NoSqlClientException($"Failed to connect to {_serverAddress}:{_serverPort}", ex);
            }
        }

        /// <summary>
        /// Disconnects from the NoSQL server.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            if (!_isConnected)
                return;

            try
            {
                // Stop keep-alive
                if (_keepAliveCts != null)
                {
                    await _keepAliveCts.CancelAsync();
                    if (_keepAliveTask != null)
                    {
                        try
                        {
                            await _keepAliveTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                        }
                        catch (TimeoutException) { /* Best effort */ }
                    }
                    _keepAliveCts.Dispose();
                    _keepAliveCts = null;
                }
            }
            catch { /* Best effort during disconnect */ }
            finally
            {
                CleanupConnection();
                _isConnected = false;
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Sends a ping message to check server availability
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if pong received, false otherwise.</returns>
        public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
        {
            EnsureConnected();

            try
            {
                var pingMessage = new NoSqlMessage
                {
                    MessageType = MessageType.Ping,
                    Payload = Array.Empty<byte>(),
                    PayloadLength = 0
                };

                var response = await SendAndReceiveAsync(pingMessage, cancellationToken);
                return response.MessageType == MessageType.Pong;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Executes a command against the NoSQL server.
        /// </summary>
        /// <param name="command">The command to execute (e.g., "GET", "SET", "DELETE").</param>
        /// <param name="collection">The collection to operate on.</param>
        /// <param name="document">Optional document data.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The command response.</returns>
        public async Task<NoSqlResponse> ExecuteCommandAsync(
            string command, 
            string collection, 
            object? document = null,
            CancellationToken cancellationToken = default)
        {
            EnsureConnected();

            var message = NoSqlMessage.CreateCommand(command, collection, document);
            var response = await SendAndReceiveAsync(message, cancellationToken);

            return ParseResponse(response);
        }

        /// <summary>
        /// Executes a query against the NoSQL server.
        /// </summary>
        /// <param name="query">The query to execute.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The query results.</returns>
        public async Task<NoSqlResponse> ExecuteQueryAsync(string query, CancellationToken cancellationToken = default)
        {
            EnsureConnected();

            var message = NoSqlMessage.Create(MessageType.Command, query);
            var response = await SendAndReceiveAsync(message, cancellationToken);

            return ParseResponse(response);
        }

        /// <summary>
        /// Authenticates with the server using username and password.
        /// </summary>
        /// <param name="username">The username.</param>
        /// <param name="password">The password.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if authentication successful.</returns>
        public async Task<bool> AuthenticateAsync(
            string username, 
            string password,
            CancellationToken cancellationToken = default)
        {
            EnsureConnected();

            var authPayload = $"{{\"username\":\"{username}\",\"password\":\"{password}\"}}";
            var message = NoSqlMessage.Create(MessageType.Authentication, authPayload);
            var response = await SendAndReceiveAsync(message, cancellationToken);

            if (response.MessageType == MessageType.Response)
            {
                var result = ParseResponse(response);
                return result.Success;
            }

            return false;
        }

        private async Task PerformHandshakeAsync(CancellationToken cancellationToken)
        {
            // Create handshake message with client info
            var handshakePayload = "{\"version\":\"1.0\",\"client\":\"AdvGenNoSqlClient\"}";
            var handshakeMessage = NoSqlMessage.Create(MessageType.Handshake, handshakePayload);

            var response = await SendAndReceiveAsync(handshakeMessage, cancellationToken);

            if (response.MessageType == MessageType.Error)
            {
                var error = ParseResponse(response);
                throw new NoSqlClientException($"Handshake failed: {error.Error?.Message ?? "Unknown error"}");
            }
        }

        private async Task<NoSqlMessage> SendAndReceiveAsync(NoSqlMessage message, CancellationToken cancellationToken)
        {
            if (_networkStream == null)
                throw new InvalidOperationException("Not connected to server");

            // Serialize message
            byte[]? serializedMessage = null;
            int messageLength = 0;

            try
            {
                serializedMessage = _messageProtocol.Serialize(message);
                messageLength = MessageHeader.HeaderSize + message.PayloadLength + 4;

                // Send message (thread-safe)
                await _sendLock.WaitAsync(cancellationToken);
                try
                {
                    await _networkStream.WriteAsync(
                        serializedMessage.AsMemory(0, messageLength), 
                        cancellationToken);
                    await _networkStream.FlushAsync(cancellationToken);
                }
                finally
                {
                    _sendLock.Release();
                }

                // Return rented array
                ArrayPool<byte>.Shared.Return(serializedMessage);
                serializedMessage = null;

                // Receive response (thread-safe)
                await _receiveLock.WaitAsync(cancellationToken);
                try
                {
                    return await ReceiveMessageAsync(cancellationToken);
                }
                finally
                {
                    _receiveLock.Release();
                }
            }
            finally
            {
                if (serializedMessage != null)
                    ArrayPool<byte>.Shared.Return(serializedMessage);
            }
        }

        private async Task<NoSqlMessage> ReceiveMessageAsync(CancellationToken cancellationToken)
        {
            if (_networkStream == null)
                throw new InvalidOperationException("Not connected to server");

            // Read header (12 bytes)
            var headerBuffer = new byte[MessageHeader.HeaderSize];
            await ReadExactAsync(headerBuffer, MessageHeader.HeaderSize, cancellationToken);

            var header = _messageProtocol.ParseHeader(headerBuffer);

            if (!_messageProtocol.ValidateHeader(header))
            {
                throw new NoSqlProtocolException("Invalid message header received from server");
            }

            // Read payload + checksum
            var payloadLength = header.PayloadLength;
            var totalLength = payloadLength + 4; // payload + checksum

            byte[]? payload = null;
            if (payloadLength > 0)
            {
                payload = new byte[payloadLength];
                var payloadAndChecksum = new byte[totalLength];
                await ReadExactAsync(payloadAndChecksum, totalLength, cancellationToken);
                Buffer.BlockCopy(payloadAndChecksum, 0, payload, 0, payloadLength);

                // Verify checksum
                var checksumOffset = payloadLength;
                var expectedChecksum = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(
                    payloadAndChecksum.AsSpan(checksumOffset, 4));

                if (!_messageProtocol.ValidateChecksum(payload, expectedChecksum))
                    throw new NoSqlProtocolException("Checksum validation failed");
            }
            else
            {
                // Read checksum only
                var checksumBuffer = new byte[4];
                await ReadExactAsync(checksumBuffer, 4, cancellationToken);
            }

            return new NoSqlMessage
            {
                MessageType = header.MessageType,
                Flags = header.Flags,
                Payload = payload,
                PayloadLength = payloadLength
            };
        }

        private async Task ReadExactAsync(byte[] buffer, int count, CancellationToken cancellationToken)
        {
            if (_networkStream == null)
                throw new InvalidOperationException("Not connected to server");

            var totalRead = 0;
            while (totalRead < count)
            {
                var read = await _networkStream.ReadAsync(
                    buffer.AsMemory(totalRead, count - totalRead),
                    cancellationToken);

                if (read == 0)
                    throw new NoSqlClientException("Connection closed by server");

                totalRead += read;
            }
        }

        private async Task RunKeepAliveAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_options.KeepAliveInterval, cancellationToken);
                    
                    if (!await PingAsync(CancellationToken.None))
                    {
                        ErrorOccurred?.Invoke(this, new NoSqlClientException("Keep-alive ping failed"));
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke(this, new NoSqlClientException("Keep-alive error", ex));
                    break;
                }
            }
        }

        private NoSqlResponse ParseResponse(NoSqlMessage message)
        {
            var json = message.GetPayloadAsString();

            if (string.IsNullOrEmpty(json))
            {
                return new NoSqlResponse { Success = true };
            }

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;

                var response = new NoSqlResponse
                {
                    Success = root.GetProperty("success").GetBoolean()
                };

                if (root.TryGetProperty("data", out var dataElement))
                {
                    response.Data = dataElement;
                }

                if (root.TryGetProperty("error", out var errorElement))
                {
                    response.Error = new NoSqlError
                    {
                        Code = errorElement.GetProperty("code").GetString() ?? "UNKNOWN",
                        Message = errorElement.GetProperty("message").GetString() ?? "Unknown error"
                    };
                }

                return response;
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new NoSqlProtocolException("Failed to parse server response", ex);
            }
        }

        private void EnsureConnected()
        {
            if (!_isConnected)
                throw new InvalidOperationException("Client is not connected. Call ConnectAsync first.");
        }

        private void CleanupConnection()
        {
            _networkStream?.Dispose();
            _networkStream = null;

            _tcpClient?.Dispose();
            _tcpClient = null;
        }

        /// <summary>
        /// Disposes the client and releases all resources
        /// </summary>
        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Asynchronously disposes the client and releases all resources
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            await DisconnectAsync();
            _sendLock.Dispose();
            _receiveLock.Dispose();
        }
    }
}
