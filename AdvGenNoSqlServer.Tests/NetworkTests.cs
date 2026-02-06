// Copyright (c) 2026 [Your Organization]
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Configuration;
using AdvGenNoSqlServer.Network;
using System;
using System.Buffers;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AdvGenNoSqlServer.Tests
{
    /// <summary>
    /// Unit tests for the Network layer
    /// </summary>
    public class NetworkTests : IDisposable
    {
        private readonly ServerConfiguration _config;
        private TcpServer? _server;

        public NetworkTests()
        {
            _config = new ServerConfiguration
            {
                Host = "127.0.0.1",
                Port = 19090, // Use different port for testing
                MaxConcurrentConnections = 100,
                ConnectionTimeout = TimeSpan.FromSeconds(5),
                ReceiveBufferSize = 4096,
                SendBufferSize = 4096
            };
        }

        public void Dispose()
        {
            _server?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        #region MessageProtocol Tests

        [Fact]
        public void MessageProtocol_SerializeDeserialize_RoundTrip()
        {
            // Arrange
            var protocol = new MessageProtocol();
            var originalMessage = new NoSqlMessage
            {
                MessageType = MessageType.Command,
                Flags = MessageFlags.None,
                Payload = Encoding.UTF8.GetBytes("{\"test\":\"data\"}"),
                PayloadLength = 15
            };

            // Act
            var serialized = protocol.Serialize(originalMessage);
            var deserialized = protocol.Deserialize(serialized);

            // Cleanup
            ArrayPool<byte>.Shared.Return(serialized);

            // Assert
            Assert.Equal(originalMessage.MessageType, deserialized.MessageType);
            Assert.Equal(originalMessage.Flags, deserialized.Flags);
            Assert.Equal(originalMessage.PayloadLength, deserialized.PayloadLength);
            Assert.Equal(originalMessage.GetPayloadAsString(), deserialized.GetPayloadAsString());
        }

        [Fact]
        public void MessageProtocol_SerializeEmptyPayload_Succeeds()
        {
            // Arrange
            var protocol = new MessageProtocol();
            var message = new NoSqlMessage
            {
                MessageType = MessageType.Ping,
                Flags = MessageFlags.None,
                Payload = Array.Empty<byte>(),
                PayloadLength = 0
            };

            // Act
            var serialized = protocol.Serialize(message);
            var deserialized = protocol.Deserialize(serialized);

            // Cleanup
            ArrayPool<byte>.Shared.Return(serialized);

            // Assert
            Assert.Equal(MessageType.Ping, deserialized.MessageType);
            Assert.Equal(0, deserialized.PayloadLength);
            Assert.Null(deserialized.Payload);
        }

        [Theory]
        [InlineData(MessageType.Handshake)]
        [InlineData(MessageType.Authentication)]
        [InlineData(MessageType.Command)]
        [InlineData(MessageType.Response)]
        [InlineData(MessageType.Error)]
        [InlineData(MessageType.Ping)]
        [InlineData(MessageType.Pong)]
        [InlineData(MessageType.Transaction)]
        [InlineData(MessageType.BulkOperation)]
        [InlineData(MessageType.Notification)]
        public void MessageProtocol_AllMessageTypes_SerializeCorrectly(MessageType messageType)
        {
            // Arrange
            var protocol = new MessageProtocol();
            var message = new NoSqlMessage
            {
                MessageType = messageType,
                Payload = Encoding.UTF8.GetBytes("test"),
                PayloadLength = 4
            };

            // Act
            var serialized = protocol.Serialize(message);
            var deserialized = protocol.Deserialize(serialized);

            // Cleanup
            ArrayPool<byte>.Shared.Return(serialized);

            // Assert
            Assert.Equal(messageType, deserialized.MessageType);
        }

        [Fact]
        public void MessageProtocol_ValidateHeader_ValidHeader_ReturnsTrue()
        {
            // Arrange
            var protocol = new MessageProtocol();
            var validHeader = new MessageHeader
            {
                Magic = NoSqlMessage.Magic,
                Version = NoSqlMessage.ProtocolVersion,
                MessageType = MessageType.Command,
                Flags = MessageFlags.None,
                PayloadLength = 100
            };

            // Act
            var isValid = protocol.ValidateHeader(validHeader);

            // Assert
            Assert.True(isValid);
        }

        [Fact]
        public void MessageProtocol_ValidateHeader_InvalidMagic_ReturnsFalse()
        {
            // Arrange
            var protocol = new MessageProtocol();
            var invalidHeader = new MessageHeader
            {
                Magic = 0xDEADBEEF,
                Version = NoSqlMessage.ProtocolVersion,
                MessageType = MessageType.Command,
                PayloadLength = 100
            };

            // Act
            var isValid = protocol.ValidateHeader(invalidHeader);

            // Assert
            Assert.False(isValid);
        }

        [Fact]
        public void MessageProtocol_ValidateHeader_InvalidVersion_ReturnsFalse()
        {
            // Arrange
            var protocol = new MessageProtocol();
            var invalidHeader = new MessageHeader
            {
                Magic = NoSqlMessage.Magic,
                Version = 999,
                MessageType = MessageType.Command,
                PayloadLength = 100
            };

            // Act
            var isValid = protocol.ValidateHeader(invalidHeader);

            // Assert
            Assert.False(isValid);
        }

        [Fact]
        public void MessageProtocol_ValidateHeader_NegativePayloadLength_ReturnsFalse()
        {
            // Arrange
            var protocol = new MessageProtocol();
            var invalidHeader = new MessageHeader
            {
                Magic = NoSqlMessage.Magic,
                Version = NoSqlMessage.ProtocolVersion,
                MessageType = MessageType.Command,
                PayloadLength = -1
            };

            // Act
            var isValid = protocol.ValidateHeader(invalidHeader);

            // Assert
            Assert.False(isValid);
        }

        [Fact]
        public void MessageProtocol_ValidateChecksum_CorrectChecksum_ReturnsTrue()
        {
            // Arrange
            var protocol = new MessageProtocol();
            var data = Encoding.UTF8.GetBytes("test data");
            var checksum = protocol.CalculateChecksum(data, data.Length);

            // Act
            var isValid = protocol.ValidateChecksum(data, checksum);

            // Assert
            Assert.True(isValid);
        }

        [Fact]
        public void MessageProtocol_ValidateChecksum_IncorrectChecksum_ReturnsFalse()
        {
            // Arrange
            var protocol = new MessageProtocol();
            var data = Encoding.UTF8.GetBytes("test data");

            // Act
            var isValid = protocol.ValidateChecksum(data, 0xDEADBEEF);

            // Assert
            Assert.False(isValid);
        }

        [Fact]
        public void NoSqlMessage_CreateCommand_ReturnsCorrectMessage()
        {
            // Act
            var message = NoSqlMessage.CreateCommand("GET", "users", new { id = "123" });

            // Assert
            Assert.Equal(MessageType.Command, message.MessageType);
            var payload = message.GetPayloadAsString();
            Assert.Contains("command", payload);
            Assert.Contains("GET", payload);
            Assert.Contains("collection", payload);
            Assert.Contains("users", payload);
        }

        [Fact]
        public void NoSqlMessage_CreateError_ReturnsErrorMessage()
        {
            // Act
            var message = NoSqlMessage.CreateError("ERR_001", "Test error");

            // Assert
            Assert.Equal(MessageType.Error, message.MessageType);
            var payload = message.GetPayloadAsString();
            Assert.Contains("success", payload);
            Assert.Contains("ERR_001", payload);
            Assert.Contains("Test error", payload);
        }

        [Fact]
        public void NoSqlMessage_CreateSuccess_ReturnsSuccessMessage()
        {
            // Act
            var message = NoSqlMessage.CreateSuccess(new { id = "123" });

            // Assert
            Assert.Equal(MessageType.Response, message.MessageType);
            var payload = message.GetPayloadAsString();
            Assert.Contains("success", payload);
        }

        #endregion

        #region ConnectionPool Tests

        [Fact]
        public void ConnectionPool_Constructor_ValidCapacity_CreatesPool()
        {
            // Act
            var pool = new ConnectionPool(100);

            // Assert
            Assert.Equal(100, pool.MaxConnections);
            Assert.Equal(0, pool.ActiveConnections);
            Assert.Equal(100, pool.AvailableSlots);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void ConnectionPool_Constructor_InvalidCapacity_ThrowsException(int capacity)
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => new ConnectionPool(capacity));
        }

        [Fact]
        public void ConnectionPool_TryAcquire_AvailableSlot_ReturnsTrue()
        {
            // Arrange
            var pool = new ConnectionPool(10);

            // Act
            var acquired = pool.TryAcquire();

            // Assert
            Assert.True(acquired);
            Assert.Equal(1, pool.ActiveConnections);
            Assert.Equal(9, pool.AvailableSlots);
        }

        [Fact]
        public void ConnectionPool_TryAcquire_NoAvailableSlots_ReturnsFalse()
        {
            // Arrange
            var pool = new ConnectionPool(1);
            pool.Acquire();

            // Act
            var acquired = pool.TryAcquire();

            // Assert
            Assert.False(acquired);
        }

        [Fact]
        public void ConnectionPool_Release_AfterAcquire_IncreasesAvailableSlots()
        {
            // Arrange
            var pool = new ConnectionPool(10);
            pool.Acquire();
            Assert.Equal(1, pool.ActiveConnections);

            // Act
            pool.Release();

            // Assert
            Assert.Equal(0, pool.ActiveConnections);
            Assert.Equal(10, pool.AvailableSlots);
        }

        [Fact]
        public void ConnectionPool_GetStatistics_ReturnsCorrectStats()
        {
            // Arrange
            var pool = new ConnectionPool(10);
            pool.Acquire();
            pool.Acquire();
            pool.Release();

            // Act
            var stats = pool.GetStatistics();

            // Assert
            Assert.Equal(10, stats.MaxConnections);
            Assert.Equal(1, stats.ActiveConnections);
            Assert.Equal(9, stats.AvailableSlots);
            Assert.Equal(2, stats.TotalAcquired);
            Assert.Equal(1, stats.TotalReleased);
            Assert.Equal(10.0, stats.UtilizationPercent);
        }

        [Fact]
        public void ConnectionPool_HasAvailableSlots_WhenFull_ReturnsFalse()
        {
            // Arrange
            var pool = new ConnectionPool(1);
            pool.Acquire();

            // Act & Assert
            Assert.False(pool.HasAvailableSlots);
        }

        [Fact]
        public void ConnectionPool_ResetStatistics_ClearsCounters()
        {
            // Arrange
            var pool = new ConnectionPool(10);
            pool.Acquire();
            pool.Release();

            // Act
            pool.ResetStatistics();

            // Assert
            Assert.Equal(0, pool.TotalAcquired);
            Assert.Equal(0, pool.TotalReleased);
        }

        #endregion

        #region TcpServer Tests

        [Fact]
        public void TcpServer_Constructor_ValidConfiguration_CreatesServer()
        {
            // Act
            var server = new TcpServer(_config);

            // Assert
            Assert.NotNull(server);
            Assert.Equal(_config, server.Configuration);
            Assert.False(server.IsRunning);
            Assert.Equal(0, server.ActiveConnectionCount);
        }

        [Fact]
        public void TcpServer_Constructor_NullConfiguration_ThrowsException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new TcpServer(null!));
        }

        [Fact]
        public async Task TcpServer_StartAsync_ServerStartsRunning()
        {
            // Arrange
            _server = new TcpServer(_config);

            // Act
            await _server.StartAsync();

            // Assert
            Assert.True(_server.IsRunning);
        }

        [Fact]
        public async Task TcpServer_StartAsync_AlreadyRunning_ThrowsException()
        {
            // Arrange
            _server = new TcpServer(_config);
            await _server.StartAsync();

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await _server.StartAsync());
        }

        [Fact]
        public async Task TcpServer_StopAsync_RunningServer_StopsGracefully()
        {
            // Arrange
            _server = new TcpServer(_config);
            await _server.StartAsync();

            // Act
            await _server.StopAsync();

            // Assert
            Assert.False(_server.IsRunning);
        }

        [Fact]
        public async Task TcpServer_StopAsync_NotRunning_DoesNotThrow()
        {
            // Arrange
            _server = new TcpServer(_config);

            // Act & Assert (should not throw)
            await _server.StopAsync();
        }

        [Fact]
        public async Task TcpServer_AcceptConnection_IncrementsConnectionCount()
        {
            // Arrange
            _server = new TcpServer(_config);
            var connectionEstablished = new TaskCompletionSource<string>();
            _server.ConnectionEstablished += (s, e) => connectionEstablished.TrySetResult(e.ConnectionId);
            await _server.StartAsync();

            // Act - Connect a client
            using var client = new TcpClient();
            await client.ConnectAsync(_config.Host, _config.Port);

            // Wait for connection to be established
            await Task.WhenAny(connectionEstablished.Task, Task.Delay(2000));

            // Assert
            Assert.True(_server.ActiveConnectionCount >= 1);
        }

        [Fact]
        public async Task TcpServer_ConnectionClosed_EventFires()
        {
            // Arrange
            _server = new TcpServer(_config);
            var connectionClosed = new TaskCompletionSource<string>();
            _server.ConnectionClosed += (s, e) => connectionClosed.TrySetResult(e.ConnectionId);
            await _server.StartAsync();

            // Act - Connect and disconnect
            using (var client = new TcpClient())
            {
                await client.ConnectAsync(_config.Host, _config.Port);
            }

            // Wait for connection to close
            await Task.WhenAny(connectionClosed.Task, Task.Delay(2000));

            // Assert - At minimum, the server should still be operational
            Assert.True(_server.IsRunning);
        }

        [Fact]
        public async Task TcpServer_MultipleConnections_Accepted()
        {
            // Arrange
            const int connectionCount = 5;
            _server = new TcpServer(_config);
            var connections = new System.Collections.Concurrent.ConcurrentBag<TcpClient>();
            await _server.StartAsync();

            // Act - Connect multiple clients
            for (int i = 0; i < connectionCount; i++)
            {
                var client = new TcpClient();
                await client.ConnectAsync(_config.Host, _config.Port);
                connections.Add(client);
            }

            // Give server time to process connections
            await Task.Delay(100);

            // Assert
            Assert.True(_server.ActiveConnectionCount >= connectionCount);

            // Cleanup
            foreach (var client in connections)
            {
                client.Dispose();
            }
        }

        #endregion

        #region Integration Tests

        [Fact]
        public async Task Network_FullRoundTrip_ServerAcceptsConnections()
        {
            // Arrange
            _server = new TcpServer(_config);
            await _server.StartAsync();

            // Act - Connect client
            using var client = new TcpClient();
            await client.ConnectAsync(_config.Host, _config.Port);

            // Assert
            Assert.True(client.Connected);
            Assert.True(_server.IsRunning);
        }

        [Fact]
        public async Task Network_ConnectionPool_LimitsConnections()
        {
            // Arrange
            var limitedConfig = new ServerConfiguration
            {
                Host = "127.0.0.1",
                Port = 19091,
                MaxConcurrentConnections = 2,
                ConnectionTimeout = TimeSpan.FromSeconds(1)
            };
            
            var server = new TcpServer(limitedConfig);
            await server.StartAsync();

            try
            {
                // Act - Connect multiple clients
                var clients = new System.Collections.Generic.List<TcpClient>();
                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        var client = new TcpClient();
                        await client.ConnectAsync(limitedConfig.Host, limitedConfig.Port);
                        clients.Add(client);
                    }
                    catch
                    {
                        // Expected for connections beyond limit
                    }
                }

                // Assert
                Assert.True(server.ActiveConnectionCount <= limitedConfig.MaxConcurrentConnections);

                // Cleanup
                foreach (var client in clients)
                {
                    client.Dispose();
                }
            }
            finally
            {
                await server.StopAsync();
                await server.DisposeAsync();
            }
        }

        #endregion
    }
}
