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
/// Tests for the admin-oriented TCP commands: createcollection, dropcollection, listdocuments.
/// </summary>
public class AdminCommandsTests : IAsyncLifetime
{
    private ServerNoSql _server = null!;
    private string _storagePath = null!;

    public async Task InitializeAsync()
    {
        _storagePath = Path.Combine(Path.GetTempPath(), "advgen-admincmd-test-" + Guid.NewGuid().ToString("N"));
        var config = new ServerConfiguration
        {
            Host = "127.0.0.1",
            Port = 19301,
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

    private static JsonElement Data(NoSqlMessage response)
        => JsonDocument.Parse(response.GetPayloadAsString()).RootElement.GetProperty("data");

    [Fact]
    public async Task CreateCollection_NewName_ReturnsCreatedTrue()
    {
        var response = await SendCommandAsync("""{"command":"createcollection","collection":"newcol"}""");
        Assert.Equal(MessageType.Response, response.MessageType);
        Assert.True(Data(response).GetProperty("created").GetBoolean());

        var list = await SendCommandAsync("""{"command":"listcollections"}""");
        Assert.Contains("newcol", Data(list).GetProperty("collections").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task CreateCollection_ExistingName_ReturnsCreatedFalse()
    {
        await SendCommandAsync("""{"command":"createcollection","collection":"dupe"}""");
        var response = await SendCommandAsync("""{"command":"createcollection","collection":"dupe"}""");
        Assert.Equal(MessageType.Response, response.MessageType);
        Assert.False(Data(response).GetProperty("created").GetBoolean());
    }

    [Fact]
    public async Task CreateCollection_MissingCollection_ReturnsError()
    {
        var response = await SendCommandAsync("""{"command":"createcollection"}""");
        Assert.Equal(MessageType.Error, response.MessageType);
    }

    [Fact]
    public async Task DropCollection_Existing_ReturnsDroppedTrue_AndRemoves()
    {
        await SendCommandAsync("""{"command":"createcollection","collection":"togo"}""");
        var response = await SendCommandAsync("""{"command":"dropcollection","collection":"togo"}""");
        Assert.Equal(MessageType.Response, response.MessageType);
        Assert.True(Data(response).GetProperty("dropped").GetBoolean());

        var list = await SendCommandAsync("""{"command":"listcollections"}""");
        Assert.DoesNotContain("togo", Data(list).GetProperty("collections").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task DropCollection_Missing_ReturnsDroppedFalse()
    {
        var response = await SendCommandAsync("""{"command":"dropcollection","collection":"never-existed"}""");
        Assert.Equal(MessageType.Response, response.MessageType);
        Assert.False(Data(response).GetProperty("dropped").GetBoolean());
    }

    [Fact]
    public async Task DropCollection_MissingCollectionProperty_ReturnsError()
    {
        var response = await SendCommandAsync("""{"command":"dropcollection"}""");
        Assert.Equal(MessageType.Error, response.MessageType);
    }

    [Fact]
    public async Task ListDocuments_ReturnsPagedFlattenedDocumentsAndTotal()
    {
        for (int i = 0; i < 12; i++)
        {
            var setJson = JsonSerializer.Serialize(new
            {
                command = "set",
                collection = "pagecol",
                document = new Dictionary<string, object> { ["_id"] = $"doc{i:D2}", ["n"] = i }
            });
            await SendCommandAsync(setJson);
        }

        var response = await SendCommandAsync(
            """{"command":"listdocuments","collection":"pagecol","document":{"skip":10,"take":5}}""");
        Assert.Equal(MessageType.Response, response.MessageType);
        var data = Data(response);

        Assert.Equal(12, data.GetProperty("total").GetInt64());
        var docs = data.GetProperty("documents").EnumerateArray().ToList();
        Assert.Equal(2, docs.Count);                       // 12 total, skip 10
        Assert.True(docs[0].TryGetProperty("_id", out _)); // flattened shape
        Assert.True(docs[0].TryGetProperty("n", out _));   // data fields at top level
    }

    [Fact]
    public async Task ListDocuments_DefaultsAndEmptyCollection()
    {
        var response = await SendCommandAsync("""{"command":"listdocuments","collection":"emptycol"}""");
        Assert.Equal(MessageType.Response, response.MessageType);
        var data = Data(response);
        Assert.Equal(0, data.GetProperty("total").GetInt64());
        Assert.Empty(data.GetProperty("documents").EnumerateArray());
    }

    [Fact]
    public async Task ListDocuments_MissingCollection_ReturnsError()
    {
        var response = await SendCommandAsync("""{"command":"listdocuments"}""");
        Assert.Equal(MessageType.Error, response.MessageType);
    }

    [Fact]
    public async Task ListDocuments_TakeIsCappedAt500()
    {
        var response = await SendCommandAsync(
            """{"command":"listdocuments","collection":"emptycol","document":{"skip":0,"take":99999}}""");
        Assert.Equal(MessageType.Response, response.MessageType); // capped, not rejected
    }
}
