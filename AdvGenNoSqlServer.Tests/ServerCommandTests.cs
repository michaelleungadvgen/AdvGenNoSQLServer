// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Core.Configuration;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Network;
using ServerNoSql = AdvGenNoSqlServer.Server.NoSqlServer;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AdvGenNoSqlServer.Tests
{
    /// <summary>
    /// Unit tests for server command handlers (listcollections, count, etc.).
    /// </summary>
    public class ServerCommandTests : IDisposable
    {
        private readonly Mock<ILogger<ServerNoSql>> _loggerMock;
        private readonly Mock<IConfigurationManager> _configManagerMock;
        private readonly ServerConfiguration _config;
        private readonly string _testDataPath;

        public ServerCommandTests()
        {
            _loggerMock = new Mock<ILogger<ServerNoSql>>();
            _configManagerMock = new Mock<IConfigurationManager>();

            _testDataPath = Path.Combine(Path.GetTempPath(), $"nosql_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_testDataPath);

            _config = new ServerConfiguration
            {
                Host = "127.0.0.1",
                Port = 19090,
                MaxConcurrentConnections = 100,
                RequireAuthentication = false,
                StoragePath = _testDataPath
            };

            _configManagerMock.Setup(c => c.Configuration).Returns(_config);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testDataPath))
                {
                    Directory.Delete(_testDataPath, true);
                }
            }
            catch { /* Ignore cleanup errors */ }
        }

        #region LISTCOLLECTIONS Tests

        [Fact]
        public async Task ListCollections_WithoutStorage_ShouldReturnError()
        {
            // Arrange - Create server without initializing (documentStore will be null)
            var server = new ServerNoSql(_loggerMock.Object, _configManagerMock.Object, null);
            var command = CreateCommand("listcollections");

            // Act
            var result = await InvokeHandleCommandAsync(server, command);

            // Assert
            Assert.Equal(MessageType.Error, result.MessageType);
            var payload = GetPayload(result);
            Assert.Contains("STORAGE_ERROR", payload);
        }

        [Fact]
        public async Task ListCollections_WithEmptyStorage_ShouldReturnEmptyList()
        {
            // Arrange
            var server = new ServerNoSql(_loggerMock.Object, _configManagerMock.Object, null);
            await server.StartAsync(CancellationToken.None);
            
            try
            {
                var command = CreateCommand("listcollections");

                // Act
                var result = await InvokeHandleCommandAsync(server, command);

                // Assert
                Assert.Equal(MessageType.Response, result.MessageType);
                var payload = GetPayload(result);
                Assert.Contains("\"count\":0", payload);
                Assert.Contains("\"collections\":[]", payload);
            }
            finally
            {
                await server.StopAsync(CancellationToken.None);
            }
        }

        [Fact]
        public async Task ListCollections_WithCollections_ShouldReturnCollections()
        {
            // Arrange
            var server = new ServerNoSql(_loggerMock.Object, _configManagerMock.Object, null);
            await server.StartAsync(CancellationToken.None);
            
            try
            {
                // Insert some documents using SET command to create collections
                await SetDocumentAsync(server, "doc1", "users", new { name = "Test1" });
                await SetDocumentAsync(server, "doc2", "products", new { name = "Test2" });
                await SetDocumentAsync(server, "doc3", "orders", new { name = "Test3" });

                var command = CreateCommand("listcollections");

                // Act
                var result = await InvokeHandleCommandAsync(server, command);

                // Assert
                Assert.Equal(MessageType.Response, result.MessageType);
                var payload = GetPayload(result);
                Assert.Contains("\"count\":3", payload);
                Assert.Contains("users", payload);
                Assert.Contains("products", payload);
                Assert.Contains("orders", payload);
            }
            finally
            {
                await server.StopAsync(CancellationToken.None);
            }
        }

        #endregion

        #region COUNT Tests

        [Fact]
        public async Task Count_WithoutStorage_ShouldReturnError()
        {
            // Arrange
            var server = new ServerNoSql(_loggerMock.Object, _configManagerMock.Object, null);
            var command = CreateCommand("count");

            // Act
            var result = await InvokeHandleCommandAsync(server, command);

            // Assert
            Assert.Equal(MessageType.Error, result.MessageType);
            var payload = GetPayload(result);
            Assert.Contains("STORAGE_ERROR", payload);
        }

        [Fact]
        public async Task Count_WithCollection_ShouldReturnCount()
        {
            // Arrange
            var server = new ServerNoSql(_loggerMock.Object, _configManagerMock.Object, null);
            await server.StartAsync(CancellationToken.None);
            
            try
            {
                // Insert some documents using SET command
                await SetDocumentAsync(server, "doc1", "users", new { name = "Test1" });
                await SetDocumentAsync(server, "doc2", "users", new { name = "Test2" });
                await SetDocumentAsync(server, "doc3", "users", new { name = "Test3" });

                var command = CreateCommand("count", "users");

                // Act
                var result = await InvokeHandleCommandAsync(server, command);

                // Assert
                Assert.Equal(MessageType.Response, result.MessageType);
                var payload = GetPayload(result);
                Assert.Contains("\"count\":3", payload);
                Assert.Contains("\"collection\":\"users\"", payload);
            }
            finally
            {
                await server.StopAsync(CancellationToken.None);
            }
        }

        [Fact]
        public async Task Count_WithoutCollection_ShouldReturnTotalCount()
        {
            // Arrange
            var server = new ServerNoSql(_loggerMock.Object, _configManagerMock.Object, null);
            await server.StartAsync(CancellationToken.None);
            
            try
            {
                // Insert some documents into different collections
                await SetDocumentAsync(server, "doc1", "users", new { name = "Test1" });
                await SetDocumentAsync(server, "doc2", "users", new { name = "Test2" });
                await SetDocumentAsync(server, "doc3", "products", new { name = "Test3" });

                var command = CreateCommand("count"); // No collection specified

                // Act
                var result = await InvokeHandleCommandAsync(server, command);

                // Assert
                Assert.Equal(MessageType.Response, result.MessageType);
                var payload = GetPayload(result);
                Assert.Contains("\"count\":3", payload);
                Assert.Contains("\"collection\":\"*\"", payload);
                Assert.Contains("\"totalCollections\":2", payload);
            }
            finally
            {
                await server.StopAsync(CancellationToken.None);
            }
        }

        [Fact]
        public async Task Count_EmptyCollection_ShouldReturnZero()
        {
            // Arrange
            var server = new ServerNoSql(_loggerMock.Object, _configManagerMock.Object, null);
            await server.StartAsync(CancellationToken.None);
            
            try
            {
                var command = CreateCommand("count", "nonexistent");

                // Act
                var result = await InvokeHandleCommandAsync(server, command);

                // Assert
                Assert.Equal(MessageType.Response, result.MessageType);
                var payload = GetPayload(result);
                Assert.Contains("\"count\":0", payload);
            }
            finally
            {
                await server.StopAsync(CancellationToken.None);
            }
        }

        #endregion

        #region Helper Methods

        private static NoSqlMessage CreateCommand(string command, string? collection = null)
        {
            var payload = new { command, collection };
            var json = JsonSerializer.Serialize(payload);
            return NoSqlMessage.Create(MessageType.Command, json);
        }

        private static async Task SetDocumentAsync(ServerNoSql server, string id, string collection, object data)
        {
            var document = new System.Collections.Generic.Dictionary<string, object>(data.GetType()
                .GetProperties()
                .ToDictionary(p => p.Name, p => p.GetValue(data)!));
            document["_id"] = id;

            var payload = new { command = "set", collection, document };
            var json = JsonSerializer.Serialize(payload);
            var command = NoSqlMessage.Create(MessageType.Command, json);

            await InvokeHandleCommandAsync(server, command);
        }

        private static async Task<NoSqlMessage> InvokeHandleCommandAsync(ServerNoSql server, NoSqlMessage command)
        {
            // Use reflection to access the private HandleMessageAsync method
            var method = typeof(ServerNoSql).GetMethod("HandleMessageAsync", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (method == null)
            {
                throw new InvalidOperationException("HandleMessageAsync method not found");
            }

            var task = (Task<NoSqlMessage>)method.Invoke(server, new object[] { command, "test-connection" })!;
            return await task;
        }

        private static string GetPayload(NoSqlMessage message)
        {
            if (message.Payload == null || message.PayloadLength == 0)
                return string.Empty;
            
            return System.Text.Encoding.UTF8.GetString(message.Payload, 0, message.PayloadLength);
        }

        #endregion
    }
}
