// Copyright (c) 2026 [Your Organization]
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Client;
using AdvGenNoSqlServer.Core.Configuration;
using AdvGenNoSqlServer.Network;

// Disable parallel execution for this test class since they use TCP ports
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace NoSqlServer.Tests
{
    [CollectionDefinition("ClientTests", DisableParallelization = true)]
    public class ClientTestsCollection { }

    [Collection("ClientTests")]
    public class AdvGenNoSqlClientTests : IDisposable
    {
        private TcpServer? _server;
        private readonly List<AdvGenNoSqlClient> _clients = new();
        private static int _currentPort = 19200;
        private static readonly object _portLock = new();

        public void Dispose()
        {
            foreach (var client in _clients)
            {
                try { client.Dispose(); } catch { /* Best effort */ }
            }
            try { _server?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5)); } catch { /* Best effort */ }

            // Small delay to allow socket cleanup
            Thread.Sleep(100);
        }

        private int GetNextPort()
        {
            lock (_portLock)
            {
                return _currentPort++;
            }
        }

        private async Task<TcpServer> CreateServerWithHandshake(int port)
        {
            var config = new ServerConfiguration
            {
                Host = "127.0.0.1",
                Port = port,
                MaxConcurrentConnections = 10
            };

            var server = new TcpServer(config);

            // Add handler for handshake responses
            server.MessageReceived += async (_, args) =>
            {
                if (args.Message.MessageType == MessageType.Handshake)
                {
                    var response = NoSqlMessage.CreateSuccess(new { version = "1.0", server = "AdvGenNoSqlServer" });
                    await args.SendResponseAsync(response);
                }
                else if (args.Message.MessageType == MessageType.Ping)
                {
                    await args.SendResponseAsync(new NoSqlMessage
                    {
                        MessageType = MessageType.Pong,
                        Payload = Array.Empty<byte>(),
                        PayloadLength = 0
                    });
                }
            };

            await server.StartAsync();

            // Give server time to start accepting connections
            await Task.Delay(50);

            return server;
        }

        [Fact]
        public void AdvGenNoSqlClient_Initialization_ShouldSetServerAddress()
        {
            // Arrange
            var serverAddress = "localhost:8080";

            // Act
            var client = new AdvGenNoSqlClient(serverAddress);
            _clients.Add(client);

            // Assert
            Assert.NotNull(client);
            Assert.False(client.IsConnected);
        }

        [Fact]
        public void AdvGenNoSqlClient_Initialization_WithNullAddress_ShouldThrow()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new AdvGenNoSqlClient(null!));
        }

        [Fact]
        public void AdvGenNoSqlClientFactory_CreateClientWithOptions_ShouldCreateClient()
        {
            // Arrange
            var options = new AdvGenNoSqlClientOptions
            {
                ServerAddress = "localhost:8080"
            };

            // Act
            var client = AdvGenNoSqlClientFactory.CreateClient(options);
            _clients.Add(client);

            // Assert
            Assert.NotNull(client);
            Assert.False(client.IsConnected);
        }

        [Fact]
        public void AdvGenNoSqlClientFactory_CreateClientWithAddress_ShouldCreateClient()
        {
            // Arrange
            var serverAddress = "localhost:8080";

            // Act
            var client = AdvGenNoSqlClientFactory.CreateClient(serverAddress);
            _clients.Add(client);

            // Assert
            Assert.NotNull(client);
            Assert.False(client.IsConnected);
        }

        [Fact]
        public async Task AdvGenNoSqlClient_ConnectAsync_WithRunningServer_ShouldConnect()
        {
            // Arrange
            var port = GetNextPort();
            _server = await CreateServerWithHandshake(port);

            var client = new AdvGenNoSqlClient($"127.0.0.1:{port}");
            _clients.Add(client);

            bool connectedEventFired = false;
            client.Connected += (_, _) => connectedEventFired = true;

            // Act
            await client.ConnectAsync();

            // Assert
            Assert.True(client.IsConnected);
            Assert.True(connectedEventFired);
        }

        [Fact]
        public async Task AdvGenNoSqlClient_ConnectAsync_WhenAlreadyConnected_ShouldThrow()
        {
            // Arrange
            var port = GetNextPort();
            _server = await CreateServerWithHandshake(port);

            var client = new AdvGenNoSqlClient($"127.0.0.1:{port}");
            _clients.Add(client);

            await client.ConnectAsync();

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync());
        }

        [Fact]
        public async Task AdvGenNoSqlClient_ConnectAsync_WithNoServer_ShouldThrow()
        {
            // Arrange - use a port that's unlikely to have a server
            var client = new AdvGenNoSqlClient("127.0.0.1:59999");
            _clients.Add(client);

            // Act & Assert
            var ex = await Assert.ThrowsAsync<NoSqlClientException>(() => client.ConnectAsync());
            Assert.Contains("Failed to connect", ex.Message);
        }

        [Fact]
        public async Task AdvGenNoSqlClient_DisconnectAsync_ShouldDisconnect()
        {
            // Arrange
            var port = GetNextPort();
            _server = await CreateServerWithHandshake(port);

            var client = new AdvGenNoSqlClient($"127.0.0.1:{port}");
            _clients.Add(client);

            bool disconnectedEventFired = false;
            client.Disconnected += (_, _) => disconnectedEventFired = true;

            await client.ConnectAsync();
            Assert.True(client.IsConnected);

            // Act
            await client.DisconnectAsync();

            // Assert
            Assert.False(client.IsConnected);
            Assert.True(disconnectedEventFired);
        }

        [Fact]
        public async Task AdvGenNoSqlClient_DisconnectAsync_WhenNotConnected_ShouldNotThrow()
        {
            // Arrange
            var client = new AdvGenNoSqlClient("127.0.0.1:19093");
            _clients.Add(client);

            // Act - should not throw
            await client.DisconnectAsync();

            // Assert
            Assert.False(client.IsConnected);
        }

        [Fact]
        public async Task AdvGenNoSqlClient_PingAsync_WhenConnected_ShouldReturnTrue()
        {
            // Arrange
            var port = GetNextPort();
            _server = await CreateServerWithHandshake(port);

            var client = new AdvGenNoSqlClient($"127.0.0.1:{port}");
            _clients.Add(client);
            await client.ConnectAsync();

            // Act
            var result = await client.PingAsync();

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task AdvGenNoSqlClient_PingAsync_WhenNotConnected_ShouldThrow()
        {
            // Arrange
            var client = new AdvGenNoSqlClient("127.0.0.1:19095");
            _clients.Add(client);

            // Act - ping without connecting
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.PingAsync());
        }

        [Fact]
        public async Task AdvGenNoSqlClient_ExecuteCommandAsync_ShouldSendAndReceive()
        {
            // Arrange
            var port = GetNextPort();
            var config = new ServerConfiguration
            {
                Host = "127.0.0.1",
                Port = port,
                MaxConcurrentConnections = 10
            };

            _server = new TcpServer(config);
            _server.MessageReceived += async (_, args) =>
            {
                if (args.Message.MessageType == MessageType.Handshake)
                {
                    await args.SendResponseAsync(NoSqlMessage.CreateSuccess());
                }
                else if (args.Message.MessageType == MessageType.Command)
                {
                    await args.SendResponseAsync(NoSqlMessage.CreateSuccess(new { id = "123", name = "test" }));
                }
            };
            await _server.StartAsync();
            await Task.Delay(50);

            var client = new AdvGenNoSqlClient($"127.0.0.1:{port}");
            _clients.Add(client);
            await client.ConnectAsync();

            // Act
            var response = await client.ExecuteCommandAsync("GET", "users", new { id = "123" });

            // Assert
            Assert.True(response.Success);
            Assert.NotNull(response.Data);
        }

        [Fact]
        public async Task AdvGenNoSqlClient_ExecuteQueryAsync_ShouldSendAndReceive()
        {
            // Arrange
            var port = GetNextPort();
            var config = new ServerConfiguration
            {
                Host = "127.0.0.1",
                Port = port,
                MaxConcurrentConnections = 10
            };

            _server = new TcpServer(config);
            _server.MessageReceived += async (_, args) =>
            {
                if (args.Message.MessageType == MessageType.Handshake)
                {
                    await args.SendResponseAsync(NoSqlMessage.CreateSuccess());
                }
                else if (args.Message.MessageType == MessageType.Command)
                {
                    await args.SendResponseAsync(NoSqlMessage.CreateSuccess(new { results = new[] { "item1", "item2" } }));
                }
            };
            await _server.StartAsync();
            await Task.Delay(50);

            var client = new AdvGenNoSqlClient($"127.0.0.1:{port}");
            _clients.Add(client);
            await client.ConnectAsync();

            // Act
            var response = await client.ExecuteQueryAsync("{\"find\":\"users\"}");

            // Assert
            Assert.True(response.Success);
            Assert.NotNull(response.Data);
        }

        [Fact]
        public async Task AdvGenNoSqlClient_ExecuteCommandAsync_WhenNotConnected_ShouldThrow()
        {
            // Arrange
            var client = new AdvGenNoSqlClient("127.0.0.1:19098");
            _clients.Add(client);

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.ExecuteCommandAsync("GET", "users"));
        }

        [Fact]
        public async Task AdvGenNoSqlClient_AuthenticateAsync_WithValidCredentials_ShouldReturnTrue()
        {
            // Arrange
            var port = GetNextPort();
            var config = new ServerConfiguration
            {
                Host = "127.0.0.1",
                Port = port,
                MaxConcurrentConnections = 10
            };

            _server = new TcpServer(config);
            _server.MessageReceived += async (_, args) =>
            {
                if (args.Message.MessageType == MessageType.Handshake)
                {
                    await args.SendResponseAsync(NoSqlMessage.CreateSuccess());
                }
                else if (args.Message.MessageType == MessageType.Authentication)
                {
                    await args.SendResponseAsync(NoSqlMessage.CreateSuccess(new { token = "abc123" }));
                }
            };
            await _server.StartAsync();
            await Task.Delay(50);

            var client = new AdvGenNoSqlClient($"127.0.0.1:{port}");
            _clients.Add(client);
            await client.ConnectAsync();

            // Act
            var result = await client.AuthenticateAsync("user", "pass");

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task AdvGenNoSqlClient_AuthenticateAsync_WithInvalidCredentials_ShouldReturnFalse()
        {
            // Arrange
            var port = GetNextPort();
            var config = new ServerConfiguration
            {
                Host = "127.0.0.1",
                Port = port,
                MaxConcurrentConnections = 10
            };

            _server = new TcpServer(config);
            _server.MessageReceived += async (_, args) =>
            {
                if (args.Message.MessageType == MessageType.Handshake)
                {
                    await args.SendResponseAsync(NoSqlMessage.CreateSuccess());
                }
                else if (args.Message.MessageType == MessageType.Authentication)
                {
                    await args.SendResponseAsync(NoSqlMessage.CreateError("AUTH_FAILED", "Invalid credentials"));
                }
            };
            await _server.StartAsync();
            await Task.Delay(50);

            var client = new AdvGenNoSqlClient($"127.0.0.1:{port}");
            _clients.Add(client);
            await client.ConnectAsync();

            // Act
            var result = await client.AuthenticateAsync("user", "wrongpass");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task AdvGenNoSqlClient_ServerReturnsError_ShouldParseError()
        {
            // Arrange
            var port = GetNextPort();
            var config = new ServerConfiguration
            {
                Host = "127.0.0.1",
                Port = port,
                MaxConcurrentConnections = 10
            };

            _server = new TcpServer(config);
            _server.MessageReceived += async (_, args) =>
            {
                if (args.Message.MessageType == MessageType.Handshake)
                {
                    await args.SendResponseAsync(NoSqlMessage.CreateSuccess());
                }
                else
                {
                    await args.SendResponseAsync(NoSqlMessage.CreateError("NOT_FOUND", "Document not found"));
                }
            };
            await _server.StartAsync();
            await Task.Delay(50);

            var client = new AdvGenNoSqlClient($"127.0.0.1:{port}");
            _clients.Add(client);
            await client.ConnectAsync();

            // Act
            var response = await client.ExecuteCommandAsync("GET", "users");

            // Assert
            Assert.False(response.Success);
            Assert.NotNull(response.Error);
            Assert.Equal("NOT_FOUND", response.Error.Code);
            Assert.Equal("Document not found", response.Error.Message);
        }

        [Fact]
        public async Task AdvGenNoSqlClient_MultipleClients_CanConnectSimultaneously()
        {
            // Arrange
            var port = GetNextPort();
            _server = await CreateServerWithHandshake(port);

            var client1 = new AdvGenNoSqlClient($"127.0.0.1:{port}");
            var client2 = new AdvGenNoSqlClient($"127.0.0.1:{port}");
            _clients.Add(client1);
            _clients.Add(client2);

            // Act
            await client1.ConnectAsync();
            await client2.ConnectAsync();

            // Assert
            Assert.True(client1.IsConnected);
            Assert.True(client2.IsConnected);
        }

        [Fact]
        public void AdvGenNoSqlClient_Options_WithDefaultPort()
        {
            // Arrange - address without port
            var client = new AdvGenNoSqlClient("127.0.0.1");
            _clients.Add(client);

            // Act - verify it parses correctly (default port 9090)
            // We can't actually connect, but we can verify no exception during construction
            Assert.NotNull(client);
        }

        [Fact]
        public void AdvGenNoSqlClientOptions_DefaultValues_ShouldBeSet()
        {
            // Act
            var options = new AdvGenNoSqlClientOptions();

            // Assert
            Assert.Equal("localhost:9090", options.ServerAddress);
            Assert.Equal(5000, options.ConnectionTimeout);
            Assert.False(options.UseSsl);
            Assert.True(options.EnableKeepAlive);
            Assert.Equal(TimeSpan.FromSeconds(30), options.KeepAliveInterval);
            Assert.Equal(3, options.MaxRetryAttempts);
            Assert.Equal(1000, options.RetryDelayMs);
            Assert.False(options.AutoReconnect);
        }

        [Fact]
        public void AdvGenNoSqlResponse_SuccessResponse_ShouldHaveProperties()
        {
            // Arrange & Act
            var response = new NoSqlResponse
            {
                Success = true,
                Data = new { id = "123", name = "Test" },
                Error = null
            };

            // Assert
            Assert.True(response.Success);
            Assert.NotNull(response.Data);
            Assert.Null(response.Error);
        }

        [Fact]
        public void AdvGenNoSqlResponse_ErrorResponse_ShouldHaveError()
        {
            // Arrange & Act
            var response = new NoSqlResponse
            {
                Success = false,
                Data = null,
                Error = new NoSqlError { Code = "ERROR", Message = "Test error" }
            };

            // Assert
            Assert.False(response.Success);
            Assert.Null(response.Data);
            Assert.NotNull(response.Error);
            Assert.Equal("ERROR", response.Error.Code);
            Assert.Equal("Test error", response.Error.Message);
        }

        [Fact]
        public void NoSqlClientException_ShouldPreserveMessage()
        {
            // Arrange & Act
            var ex = new NoSqlClientException("Test message");

            // Assert
            Assert.Equal("Test message", ex.Message);
        }

        [Fact]
        public void NoSqlClientException_WithInnerException_ShouldPreserveInner()
        {
            // Arrange
            var inner = new InvalidOperationException("Inner error");

            // Act
            var ex = new NoSqlClientException("Outer message", inner);

            // Assert
            Assert.Equal("Outer message", ex.Message);
            Assert.Same(inner, ex.InnerException);
        }

        [Fact]
        public void NoSqlProtocolException_ShouldInheritFromNoSqlClientException()
        {
            // Arrange & Act
            var ex = new NoSqlProtocolException("Protocol error");

            // Assert
            Assert.IsAssignableFrom<NoSqlClientException>(ex);
            Assert.Equal("Protocol error", ex.Message);
        }
    }
}
