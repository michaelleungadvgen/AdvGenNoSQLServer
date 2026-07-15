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

public class AttachmentRbacTests : IAsyncLifetime
{
    private ServerNoSql _server = null!;
    private string _dir = null!;
    private const string Conn = "att-rbac";

    public async Task InitializeAsync()
    {
        _dir = Path.Combine(Path.GetTempPath(), "advgen-attrbac-" + Guid.NewGuid().ToString("N"));
        var config = new ServerConfiguration
        {
            Host = "127.0.0.1", Port = 19321, StoragePath = _dir,
            RequireAuthentication = true, MasterPassword = "master-pw", MaxAttachmentSizeMB = 1
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

    private async Task<NoSqlMessage> Send(MessageType t, string json, string conn = Conn)
    {
        var m = typeof(ServerNoSql).GetMethod("HandleMessageAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return await (Task<NoSqlMessage>)m.Invoke(_server, new object[] { NoSqlMessage.Create(t, json), conn })!;
    }

    private static string Code(NoSqlMessage r) => JsonDocument.Parse(r.GetPayloadAsString()).RootElement.GetProperty("error").GetProperty("code").GetString()!;
    private static string UploadJson => """{"command":"uploadattachment","collection":"c","id":"d","name":"n.txt","contentType":"text/plain","contentBase64":"aGk="}""";

    [Fact]
    public async Task ReadOnly_CannotUpload_CanList()
    {
        await Send(MessageType.Authentication, """{"username":"admin","password":"master-pw"}""");
        await Send(MessageType.Command, """{"command":"createuser","username":"ro","password":"pw123456","role":"readonly"}""");
        await Send(MessageType.Authentication, """{"username":"ro","password":"pw123456"}""");

        var up = await Send(MessageType.Command, UploadJson);
        Assert.Equal("FORBIDDEN", Code(up));

        var list = await Send(MessageType.Command, """{"command":"listattachments","collection":"c","id":"d"}""");
        Assert.NotEqual(MessageType.Error, list.MessageType);
    }

    [Fact]
    public async Task ReadWrite_CanUpload()
    {
        await Send(MessageType.Authentication, """{"username":"admin","password":"master-pw"}""");
        await Send(MessageType.Command, """{"command":"createuser","username":"rw","password":"pw123456","role":"readwrite"}""");
        await Send(MessageType.Authentication, """{"username":"rw","password":"pw123456"}""");

        var up = await Send(MessageType.Command, UploadJson);
        Assert.Equal(MessageType.Response, up.MessageType);
    }
}
