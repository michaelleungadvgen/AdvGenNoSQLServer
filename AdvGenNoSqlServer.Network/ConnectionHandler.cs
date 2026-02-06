// Copyright (c) 2026 [Your Organization]
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Configuration;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace AdvGenNoSqlServer.Network
{
    /// <summary>
    /// Handles a single TCP connection with message framing and protocol handling
    /// </summary>
    public class ConnectionHandler : IDisposable
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly MessageProtocol _protocol;
        private readonly ServerConfiguration _configuration;
        private readonly PipeReader _reader;
        private readonly PipeWriter _writer;
        private readonly SemaphoreSlim _writeLock;
        private bool _disposed;

        /// <summary>
        /// Unique connection identifier
        /// </summary>
        public string ConnectionId { get; }

        /// <summary>
        /// When the connection was established
        /// </summary>
        public DateTime ConnectedAt { get; }

        /// <summary>
        /// The TCP client
        /// </summary>
        public TcpClient Client => _client;

        /// <summary>
        /// Whether the connection is still active
        /// </summary>
        public bool IsConnected => _client.Connected && !_disposed;

        /// <summary>
        /// Remote endpoint address
        /// </summary>
        public string RemoteAddress => _client.Client?.RemoteEndPoint?.ToString() ?? "unknown";

        /// <summary>
        /// Creates a new connection handler
        /// </summary>
        public ConnectionHandler(
            string connectionId,
            TcpClient client,
            MessageProtocol protocol,
            ServerConfiguration configuration)
        {
            ConnectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

            _stream = client.GetStream();
            _reader = PipeReader.Create(_stream);
            _writer = PipeWriter.Create(_stream);
            _writeLock = new SemaphoreSlim(1, 1);
            ConnectedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Reads messages from the connection asynchronously
        /// </summary>
        public async IAsyncEnumerable<NoSqlMessage> ReadMessagesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            while (!cancellationToken.IsCancellationRequested && IsConnected)
            {
                var message = await ReadMessageAsync(cancellationToken);
                if (message == null)
                    yield break;

                yield return message;
            }
        }

        /// <summary>
        /// Reads a single message from the connection
        /// </summary>
        private async Task<NoSqlMessage?> ReadMessageAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Read the header (12 bytes)
                var headerBuffer = await ReadExactAsync(MessageHeader.HeaderSize, cancellationToken);
                if (headerBuffer == null)
                    return null;

                // Parse header
                var header = _protocol.ParseHeader(headerBuffer);
                ArrayPool<byte>.Shared.Return(headerBuffer);

                // Validate header
                if (!_protocol.ValidateHeader(header))
                {
                    throw new ProtocolException("Invalid message header");
                }

                // Read the payload
                byte[]? payload = null;
                if (header.PayloadLength > 0)
                {
                    payload = await ReadExactAsync(header.PayloadLength, cancellationToken);
                    if (payload == null)
                        return null;
                }

                // Read the checksum (4 bytes)
                var checksumBuffer = await ReadExactAsync(4, cancellationToken);
                if (checksumBuffer == null)
                {
                    if (payload != null) ArrayPool<byte>.Shared.Return(payload);
                    return null;
                }

                var checksum = BinaryPrimitives.ReadUInt32BigEndian(checksumBuffer);
                ArrayPool<byte>.Shared.Return(checksumBuffer);

                // Validate checksum
                if (!_protocol.ValidateChecksum(payload ?? Array.Empty<byte>(), checksum))
                {
                    if (payload != null) ArrayPool<byte>.Shared.Return(payload);
                    throw new ProtocolException("Checksum validation failed");
                }

                return new NoSqlMessage
                {
                    MessageType = header.MessageType,
                    Flags = header.Flags,
                    Payload = payload,
                    PayloadLength = header.PayloadLength
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (IOException)
            {
                // Connection closed
                return null;
            }
            catch (Exception ex)
            {
                throw new ProtocolException("Error reading message", ex);
            }
        }

        /// <summary>
        /// Reads exactly the specified number of bytes from the stream
        /// </summary>
        private async Task<byte[]?> ReadExactAsync(int count, CancellationToken cancellationToken)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(count);
            int totalRead = 0;

            try
            {
                while (totalRead < count)
                {
                    var read = await _stream.ReadAsync(
                        buffer.AsMemory(totalRead, count - totalRead), 
                        cancellationToken);

                    if (read == 0)
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                        return null; // Connection closed
                    }

                    totalRead += read;
                }

                var result = new byte[count];
                Buffer.BlockCopy(buffer, 0, result, 0, count);
                ArrayPool<byte>.Shared.Return(buffer);
                return result;
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(buffer);
                throw;
            }
        }

        /// <summary>
        /// Sends a message to the client
        /// </summary>
        public async ValueTask SendAsync(NoSqlMessage message, CancellationToken cancellationToken = default)
        {
            if (_disposed || !_client.Connected)
                throw new InvalidOperationException("Connection is not active");

            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                var data = _protocol.Serialize(message);
                try
                {
                    await _stream.WriteAsync(data, cancellationToken);
                    await _stream.FlushAsync(cancellationToken);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(data);
                }
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// Sends a ping/keepalive message
        /// </summary>
        public async ValueTask SendPingAsync(CancellationToken cancellationToken = default)
        {
            var pingMessage = new NoSqlMessage
            {
                MessageType = MessageType.Ping,
                Flags = 0,
                Payload = Array.Empty<byte>(),
                PayloadLength = 0
            };

            await SendAsync(pingMessage, cancellationToken);
        }

        /// <summary>
        /// Sends an error response
        /// </summary>
        public async ValueTask SendErrorAsync(string errorCode, string errorMessage, CancellationToken cancellationToken = default)
        {
            var errorJson = $"{{\"code\":\"{errorCode}\",\"message\":\"{errorMessage}\"}}";
            var errorBytes = System.Text.Encoding.UTF8.GetBytes(errorJson);

            var errorMsg = new NoSqlMessage
            {
                MessageType = MessageType.Error,
                Flags = 0,
                Payload = errorBytes,
                PayloadLength = errorBytes.Length
            };

            await SendAsync(errorMsg, cancellationToken);
        }

        /// <summary>
        /// Closes the connection gracefully
        /// </summary>
        public async ValueTask CloseAsync()
        {
            if (_disposed)
                return;

            try
            {
                _client.Close();
            }
            catch { /* Best effort */ }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Disposes the connection handler
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _writeLock.Dispose();
            _reader?.Complete();
            _writer?.Complete();
            _stream?.Dispose();
            _client?.Dispose();
        }
    }

    /// <summary>
    /// Exception thrown for protocol errors
    /// </summary>
    public class ProtocolException : Exception
    {
        public ProtocolException(string message) : base(message) { }
        public ProtocolException(string message, Exception innerException) : base(message, innerException) { }
    }
}
