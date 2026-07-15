// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Configuration;
using AdvGenNoSqlServer.Network;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;
using ServerNoSql = AdvGenNoSqlServer.Server.NoSqlServer;

namespace AdvGenNoSqlServer.Tests;

public class AttachmentCommandsTests : IAsyncLifetime
{
    private ServerNoSql _server = null!;
    private string _dir = null!;

    public async Task InitializeAsync()
    {
        _dir = Path.Combine(Path.GetTempPath(), "advgen-attach-" + Guid.NewGuid().ToString("N"));
        var config = new ServerConfiguration
        {
            Host = "127.0.0.1", Port = 19320,
            StoragePath = _dir,
            RequireAuthentication = false,
            MaxAttachmentSizeMB = 1
        };
        var cm = new Mock<IConfigurationManager>();
        cm.Setup(c => c.Configuration).Returns(config);
        _server = new ServerNoSql(new Mock<ILogger<ServerNoSql>>().Object, cm.Object,
            new AdvGenNoSqlServer.Server.ApiDataService());
        await _server.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private async Task<NoSqlMessage> Send(string json)
    {
        var m = typeof(ServerNoSql).GetMethod("HandleMessageAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return await (Task<NoSqlMessage>)m.Invoke(_server,
            new object[] { NoSqlMessage.Create(MessageType.Command, json), "conn" })!;
    }

    private static JsonElement Data(NoSqlMessage r) => JsonDocument.Parse(r.GetPayloadAsString()).RootElement.GetProperty("data");
    private static string Code(NoSqlMessage r) => JsonDocument.Parse(r.GetPayloadAsString()).RootElement.GetProperty("error").GetProperty("code").GetString()!;

    private static string B64(byte[] b) => Convert.ToBase64String(b);
    private static string Upload(string name, string ct, byte[] content)
        => JsonSerializer.Serialize(new { command = "uploadattachment", collection = "c", id = "doc1", name, contentType = ct, contentBase64 = B64(content) });

    [Fact]
    public async Task Upload_List_Download_RoundTrips()
    {
        var content = Encoding.UTF8.GetBytes("hello attachment");
        var up = await Send(Upload("greeting.txt", "text/plain", content));
        Assert.True(Data(up).GetProperty("stored").GetBoolean());
        Assert.Equal(content.Length, Data(up).GetProperty("size").GetInt64());
        var expectedHash = Convert.ToHexString(SHA256.HashData(content));
        Assert.Equal(expectedHash, Data(up).GetProperty("hash").GetString(), ignoreCase: true);

        var list = await Send("""{"command":"listattachments","collection":"c","id":"doc1"}""");
        var items = Data(list).GetProperty("attachments").EnumerateArray().ToList();
        Assert.Contains(items, a => a.GetProperty("name").GetString() == "greeting.txt" && a.GetProperty("contentType").GetString() == "text/plain");

        var dl = await Send("""{"command":"downloadattachment","collection":"c","id":"doc1","name":"greeting.txt"}""");
        Assert.True(Data(dl).GetProperty("found").GetBoolean());
        var got = Convert.FromBase64String(Data(dl).GetProperty("contentBase64").GetString()!);
        Assert.Equal(content, got);
    }

    [Fact]
    public async Task Download_Missing_ReturnsFoundFalse()
    {
        var dl = await Send("""{"command":"downloadattachment","collection":"c","id":"nope","name":"x"}""");
        Assert.False(Data(dl).GetProperty("found").GetBoolean());
    }

    [Fact]
    public async Task Info_FoundAndNotFound()
    {
        await Send(Upload("a.txt", "text/plain", Encoding.UTF8.GetBytes("x")));
        var found = await Send("""{"command":"attachmentinfo","collection":"c","id":"doc1","name":"a.txt"}""");
        Assert.True(Data(found).GetProperty("found").GetBoolean());
        var missing = await Send("""{"command":"attachmentinfo","collection":"c","id":"doc1","name":"ghost"}""");
        Assert.False(Data(missing).GetProperty("found").GetBoolean());
    }

    [Fact]
    public async Task Delete_RemovesAttachment()
    {
        await Send(Upload("d.txt", "text/plain", Encoding.UTF8.GetBytes("x")));
        var del = await Send("""{"command":"deleteattachment","collection":"c","id":"doc1","name":"d.txt"}""");
        Assert.True(Data(del).GetProperty("deleted").GetBoolean());
        var dl = await Send("""{"command":"downloadattachment","collection":"c","id":"doc1","name":"d.txt"}""");
        Assert.False(Data(dl).GetProperty("found").GetBoolean());
    }

    [Fact]
    public async Task TotalStorage_IncreasesAfterUpload()
    {
        var before = Data(await Send("""{"command":"totalstorage"}""")).GetProperty("bytes").GetInt64();
        await Send(Upload("big.bin", "application/octet-stream", new byte[10_000]));
        var after = Data(await Send("""{"command":"totalstorage"}""")).GetProperty("bytes").GetInt64();
        Assert.True(after >= before + 10_000);
    }

    [Fact]
    public async Task Upload_Oversize_ReturnsTooLarge()
    {
        var r = await Send(Upload("huge.bin", "application/octet-stream", new byte[2 * 1024 * 1024]));
        Assert.Equal("ATTACHMENT_TOO_LARGE", Code(r));
    }

    [Fact]
    public async Task Upload_BlockedContentType_ReturnsBlocked()
    {
        var r = await Send(Upload("evil.exe", "application/x-msdownload", new byte[10]));
        Assert.Equal("CONTENT_TYPE_BLOCKED", Code(r));
    }

    [Fact]
    public async Task Upload_BadBase64_ReturnsInvalidContent()
    {
        var r = await Send("""{"command":"uploadattachment","collection":"c","id":"doc1","name":"x","contentType":"text/plain","contentBase64":"!!!not-base64!!!"}""");
        Assert.Equal("INVALID_CONTENT", Code(r));
    }

    [Fact]
    public async Task Upload_MissingFields_ReturnsInvalidCommand()
    {
        var r = await Send("""{"command":"uploadattachment","collection":"c"}""");
        Assert.Equal(MessageType.Error, r.MessageType);
    }
}
