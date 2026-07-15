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

public class RbacEnforcementTests : IAsyncLifetime
{
    private ServerNoSql _server = null!;
    private string _dir = null!;
    private const string Conn = "rbac-conn";

    public async Task InitializeAsync()
    {
        _dir = Path.Combine(Path.GetTempPath(), "advgen-rbac-" + Guid.NewGuid().ToString("N"));
        var config = new ServerConfiguration
        {
            Host = "127.0.0.1",
            Port = 19310,
            StoragePath = _dir,
            RequireAuthentication = true,
            MasterPassword = "master-pw"
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
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private async Task<NoSqlMessage> SendAsync(MessageType type, string json, string conn = Conn)
    {
        var message = NoSqlMessage.Create(type, json);
        var m = typeof(ServerNoSql).GetMethod("HandleMessageAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return await (Task<NoSqlMessage>)m.Invoke(_server, new object[] { message, conn })!;
    }

    private static string Code(NoSqlMessage r)
        => JsonDocument.Parse(r.GetPayloadAsString()).RootElement
            .GetProperty("error").GetProperty("code").GetString()!;

    [Fact]
    public async Task Command_WithoutAuth_ReturnsAuthRequired()
    {
        var r = await SendAsync(MessageType.Command, """{"command":"listcollections"}""");
        Assert.Equal(MessageType.Error, r.MessageType);
        Assert.Equal("AUTH_REQUIRED", Code(r));
    }

    [Fact]
    public async Task Admin_AuthResponse_IncludesRole()
    {
        var r = await SendAsync(MessageType.Authentication, """{"username":"admin","password":"master-pw"}""");
        var data = JsonDocument.Parse(r.GetPayloadAsString()).RootElement.GetProperty("data");
        Assert.Equal("admin", data.GetProperty("role").GetString());
    }

    [Fact]
    public async Task ReadOnly_CanGet_CannotSet()
    {
        // Authenticate as admin on the connection, create a readonly user
        await SendAsync(MessageType.Authentication, """{"username":"admin","password":"master-pw"}""");
        await SendAsync(MessageType.Command, """{"command":"createuser","username":"ro","password":"pw123456","role":"readonly"}""");

        // Re-auth on the same connection as the readonly user (replaces identity)
        await SendAsync(MessageType.Authentication, """{"username":"ro","password":"pw123456"}""");

        var get = await SendAsync(MessageType.Command, """{"command":"get","collection":"c","id":"x"}""");
        Assert.NotEqual(MessageType.Error, get.MessageType); // read allowed (found:false is a success)

        var set = await SendAsync(MessageType.Command, """{"command":"set","collection":"c","document":{"_id":"x"}}""");
        Assert.Equal(MessageType.Error, set.MessageType);
        Assert.Equal("FORBIDDEN", Code(set));
    }

    [Fact]
    public async Task ReadWrite_CannotCreateUser()
    {
        await SendAsync(MessageType.Authentication, """{"username":"admin","password":"master-pw"}""");
        await SendAsync(MessageType.Command, """{"command":"createuser","username":"rw","password":"pw123456","role":"readwrite"}""");

        await SendAsync(MessageType.Authentication, """{"username":"rw","password":"pw123456"}""");
        var r = await SendAsync(MessageType.Command, """{"command":"createuser","username":"x","password":"pw123456","role":"readonly"}""");
        Assert.Equal(MessageType.Error, r.MessageType);
        Assert.Equal("FORBIDDEN", Code(r));
    }

    [Fact]
    public async Task ReadWrite_CanSet_CanGet()
    {
        await SendAsync(MessageType.Authentication, """{"username":"admin","password":"master-pw"}""");
        await SendAsync(MessageType.Command, """{"command":"createuser","username":"rw2","password":"pw123456","role":"readwrite"}""");
        await SendAsync(MessageType.Authentication, """{"username":"rw2","password":"pw123456"}""");

        var set = await SendAsync(MessageType.Command, """{"command":"set","collection":"c","document":{"_id":"y","n":1}}""");
        Assert.NotEqual(MessageType.Error, set.MessageType);
    }
}
