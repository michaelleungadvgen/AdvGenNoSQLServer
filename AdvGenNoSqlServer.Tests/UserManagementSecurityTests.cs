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
/// Account changes (setrole / deleteuser / setpassword) must revoke the affected
/// user's live TCP sessions so old privileges cannot persist on open connections.
/// </summary>
public class SessionRevocationTests : IAsyncLifetime
{
    private ServerNoSql _server = null!;
    private string _dir = null!;
    private const string AdminConn = "admin-conn";

    public async Task InitializeAsync()
    {
        _dir = Path.Combine(Path.GetTempPath(), "advgen-revoke-" + Guid.NewGuid().ToString("N"));
        var config = new ServerConfiguration
        {
            Host = "127.0.0.1",
            Port = 19312,
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

    private async Task<NoSqlMessage> SendAsync(MessageType type, string json, string conn = AdminConn)
    {
        var message = NoSqlMessage.Create(type, json);
        var m = typeof(ServerNoSql).GetMethod("HandleMessageAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return await (Task<NoSqlMessage>)m.Invoke(_server, new object[] { message, conn })!;
    }

    private static string Code(NoSqlMessage r)
        => JsonDocument.Parse(r.GetPayloadAsString()).RootElement
            .GetProperty("error").GetProperty("code").GetString()!;

    private async Task<string> CreateUserAsync(string name, string role)
    {
        await SendAsync(MessageType.Authentication, """{"username":"admin","password":"master-pw"}""");
        await SendAsync(MessageType.Command,
            $$"""{"command":"createuser","username":"{{name}}","password":"pw123456","role":"{{role}}"}""");
        var userConn = name + "-conn";
        await SendAsync(MessageType.Authentication,
            $$"""{"username":"{{name}}","password":"pw123456"}""", conn: userConn);
        return userConn;
    }

    [Fact]
    public async Task SetRole_RevokesTargetLiveSession()
    {
        var victimConn = await CreateUserAsync("victim", "readwrite");

        var r = await SendAsync(MessageType.Command, """{"command":"setrole","username":"victim","role":"readonly"}""");
        Assert.NotEqual(MessageType.Error, r.MessageType);

        var after = await SendAsync(MessageType.Command, """{"command":"listcollections"}""", conn: victimConn);
        Assert.Equal(MessageType.Error, after.MessageType);
        Assert.Equal("AUTH_REQUIRED", Code(after));
    }

    [Fact]
    public async Task DeleteUser_RevokesLiveSession()
    {
        var conn = await CreateUserAsync("gone", "readwrite");

        var r = await SendAsync(MessageType.Command, """{"command":"deleteuser","username":"gone"}""");
        Assert.NotEqual(MessageType.Error, r.MessageType);

        var after = await SendAsync(MessageType.Command, """{"command":"listcollections"}""", conn: conn);
        Assert.Equal(MessageType.Error, after.MessageType);
        Assert.Equal("AUTH_REQUIRED", Code(after));
    }

    [Fact]
    public async Task SetPassword_RevokesTargetSessions_KeepsAdminSession()
    {
        var conn = await CreateUserAsync("pwuser", "readwrite");

        var r = await SendAsync(MessageType.Command, """{"command":"setpassword","username":"pwuser","password":"newpw123456"}""");
        Assert.NotEqual(MessageType.Error, r.MessageType);

        var after = await SendAsync(MessageType.Command, """{"command":"listcollections"}""", conn: conn);
        Assert.Equal(MessageType.Error, after.MessageType);
        Assert.Equal("AUTH_REQUIRED", Code(after));

        var admin = await SendAsync(MessageType.Command, """{"command":"listcollections"}""");
        Assert.NotEqual(MessageType.Error, admin.MessageType);
    }
}

/// <summary>
/// With RequireAuthentication=false, anonymous connections must still be limited
/// to the configured AnonymousRole (least privilege), not unrestricted access.
/// </summary>
public class AnonymousRoleEnforcementTests : IAsyncLifetime
{
    private ServerNoSql _server = null!;
    private string _dir = null!;

    public async Task InitializeAsync()
    {
        _dir = Path.Combine(Path.GetTempPath(), "advgen-anon-" + Guid.NewGuid().ToString("N"));
        var config = new ServerConfiguration
        {
            Host = "127.0.0.1",
            Port = 19311,
            StoragePath = _dir,
            RequireAuthentication = false,
            AnonymousRole = "readonly",
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

    private async Task<NoSqlMessage> SendAsync(MessageType type, string json)
    {
        var message = NoSqlMessage.Create(type, json);
        var m = typeof(ServerNoSql).GetMethod("HandleMessageAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return await (Task<NoSqlMessage>)m.Invoke(_server, new object[] { message, "anon-conn" })!;
    }

    private static string Code(NoSqlMessage r)
        => JsonDocument.Parse(r.GetPayloadAsString()).RootElement
            .GetProperty("error").GetProperty("code").GetString()!;

    [Fact]
    public async Task Anonymous_ReadOnly_CannotWrite()
    {
        var r = await SendAsync(MessageType.Command, """{"command":"set","collection":"c","document":{"_id":"x"}}""");
        Assert.Equal(MessageType.Error, r.MessageType);
        Assert.Equal("FORBIDDEN", Code(r));
    }

    [Fact]
    public async Task Anonymous_ReadOnly_CanRead()
    {
        var r = await SendAsync(MessageType.Command, """{"command":"listcollections"}""");
        Assert.NotEqual(MessageType.Error, r.MessageType);
    }

    [Fact]
    public async Task Anonymous_CannotManageUsers()
    {
        var r = await SendAsync(MessageType.Command, """{"command":"createuser","username":"x","password":"pw123456","role":"admin"}""");
        Assert.Equal(MessageType.Error, r.MessageType);
        Assert.Equal("FORBIDDEN", Code(r));
    }
}