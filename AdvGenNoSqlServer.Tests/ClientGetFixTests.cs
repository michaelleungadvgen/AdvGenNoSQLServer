// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Configuration;
using AdvGenNoSqlServer.Network;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;
using ServerNoSql = AdvGenNoSqlServer.Server.NoSqlServer;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Round-trip tests for the get command contract. Replaces earlier JSON-simulation
/// tests that asserted a "document" property the real server never sent (audit D2 in
/// docs/superpowers/specs/2026-07-14-admin-ui-audit-design.md). The server now returns
/// a flat "document" property matching what AdvGenNoSqlClient.GetAsync reads.
/// </summary>
public class ClientGetFixTests : IAsyncLifetime
{
    private ServerNoSql _server = null!;
    private string _storagePath = null!;

    public async Task InitializeAsync()
    {
        _storagePath = Path.Combine(Path.GetTempPath(), "advgen-getfix-test-" + Guid.NewGuid().ToString("N"));
        var config = new ServerConfiguration
        {
            Host = "127.0.0.1",
            Port = 19302,
            StoragePath = _storagePath,
            RequireAuthentication = false
        };
        var configManager = new Mock<IConfigurationManager>();
        configManager.Setup(c => c.Configuration).Returns(config);
        _server = new ServerNoSql(new Mock<ILogger<ServerNoSql>>().Object, configManager.Object,
            new AdvGenNoSqlServer.Server.ApiDataService());
        await _server.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        if (Directory.Exists(_storagePath))
        {
            try { Directory.Delete(_storagePath, recursive: true); } catch (IOException) { }
        }
    }

    private async Task<NoSqlMessage> SendCommandAsync(string json)
    {
        var message = NoSqlMessage.Create(MessageType.Command, json);
        var method = typeof(ServerNoSql).GetMethod("HandleMessageAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?? throw new InvalidOperationException("HandleMessageAsync method not found");
        var task = (Task<NoSqlMessage>?)method.Invoke(_server, new object[] { message, "test-connection-id" })
            ?? throw new InvalidOperationException("Failed to invoke HandleMessageAsync");
        return await task;
    }

    [Fact]
    public async Task GetCommand_ReturnsFlatDocumentProperty()
    {
        await SendCommandAsync("""{"command":"set","collection":"c","document":{"_id":"abc","name":"test"}}""");
        var response = await SendCommandAsync("""{"command":"get","collection":"c","id":"abc"}""");

        var data = JsonDocument.Parse(response.GetPayloadAsString()).RootElement.GetProperty("data");
        Assert.True(data.GetProperty("found").GetBoolean());
        var document = data.GetProperty("document");             // the property the client reads
        Assert.Equal("abc", document.GetProperty("_id").GetString());
        Assert.Equal("test", document.GetProperty("name").GetString());
    }

    [Fact]
    public async Task GetCommand_Missing_ReturnsFoundFalseNullDocument()
    {
        var response = await SendCommandAsync("""{"command":"get","collection":"c","id":"nope"}""");
        var data = JsonDocument.Parse(response.GetPayloadAsString()).RootElement.GetProperty("data");
        Assert.False(data.GetProperty("found").GetBoolean());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("document").ValueKind);
    }
}
