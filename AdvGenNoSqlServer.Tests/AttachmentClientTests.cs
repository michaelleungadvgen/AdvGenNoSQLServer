// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Client;
using AdvGenNoSqlServer.Core.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using ServerNoSql = AdvGenNoSqlServer.Server.NoSqlServer;

namespace AdvGenNoSqlServer.Tests;

public class AttachmentClientTests : IAsyncLifetime
{
    private const int Port = 19322;
    private ServerNoSql _server = null!;
    private string _dir = null!;
    private AdvGenNoSqlClient _client = null!;

    public async Task InitializeAsync()
    {
        _dir = Path.Combine(Path.GetTempPath(), "advgen-attclient-" + Guid.NewGuid().ToString("N"));
        var config = new ServerConfiguration
        {
            Host = "127.0.0.1", Port = Port, StoragePath = _dir,
            RequireAuthentication = false, EnableSsl = false, MaxAttachmentSizeMB = 5
        };
        var cm = new Mock<IConfigurationManager>();
        cm.Setup(c => c.Configuration).Returns(config);
        _server = new ServerNoSql(new Mock<ILogger<ServerNoSql>>().Object, cm.Object,
            new AdvGenNoSqlServer.Server.ApiDataService());
        await _server.StartAsync(CancellationToken.None);

        _client = new AdvGenNoSqlClient($"127.0.0.1:{Port}");
        await _client.ConnectAsync();
    }

    public async Task DisposeAsync()
    {
        await _client.DisposeAsync();
        await _server.DisposeAsync();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public async Task Upload_Download_ByteIdentical()
    {
        var content = Encoding.UTF8.GetBytes("the quick brown fox jumps over the lazy dog");
        var meta = await _client.Attachments.UploadAsync("c", "doc", "fox.txt", "text/plain", content);
        Assert.Equal(content.Length, meta.Size);
        Assert.False(string.IsNullOrEmpty(meta.Hash));

        var got = await _client.Attachments.DownloadAsync("c", "doc", "fox.txt");
        Assert.NotNull(got);
        Assert.Equal(content, got);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(content)), meta.Hash, ignoreCase: true);
    }

    [Fact]
    public async Task List_Info_Delete_TotalStorage()
    {
        await _client.Attachments.UploadAsync("c", "doc2", "a.bin", "application/octet-stream", new byte[500]);

        var list = await _client.Attachments.ListAsync("c", "doc2");
        Assert.Contains(list, a => a.Name == "a.bin");

        var info = await _client.Attachments.InfoAsync("c", "doc2", "a.bin");
        Assert.NotNull(info);
        Assert.Equal(500, info!.Size);

        Assert.True(await _client.Attachments.TotalStorageBytesAsync() >= 500);

        Assert.True(await _client.Attachments.DeleteAsync("c", "doc2", "a.bin"));
        Assert.Null(await _client.Attachments.DownloadAsync("c", "doc2", "a.bin"));
    }

    [Fact]
    public async Task Upload_BlockedType_Throws()
    {
        var ex = await Assert.ThrowsAsync<NoSqlClientException>(
            () => _client.Attachments.UploadAsync("c", "doc3", "evil.exe", "application/x-msdownload", new byte[10]));
        Assert.Contains("CONTENT_TYPE_BLOCKED", ex.Message);
    }

    [Fact]
    public async Task Download_Missing_ReturnsNull()
        => Assert.Null(await _client.Attachments.DownloadAsync("c", "nope", "x"));
}
