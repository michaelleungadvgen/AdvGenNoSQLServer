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

public class UserCommandsTests : IAsyncLifetime
{
    private ServerNoSql _server = null!;
    private string _dir = null!;
    private const string Admin = "admin-conn";

    public async Task InitializeAsync()
    {
        _dir = Path.Combine(Path.GetTempPath(), "advgen-usercmd-" + Guid.NewGuid().ToString("N"));
        var config = new ServerConfiguration
        {
            Host = "127.0.0.1",
            Port = 19311,
            StoragePath = _dir,
            RequireAuthentication = true,
            MasterPassword = "master-pw"
        };
        var configManager = new Mock<IConfigurationManager>();
        configManager.Setup(c => c.Configuration).Returns(config);
        _server = new ServerNoSql(new Mock<ILogger<ServerNoSql>>().Object, configManager.Object,
            new AdvGenNoSqlServer.Server.ApiDataService());
        await _server.StartAsync(CancellationToken.None);
        // Authenticate the shared admin connection
        await SendAsync(MessageType.Authentication, """{"username":"admin","password":"master-pw"}""", Admin);
    }

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private async Task<NoSqlMessage> SendAsync(MessageType type, string json, string conn = Admin)
    {
        var message = NoSqlMessage.Create(type, json);
        var m = typeof(ServerNoSql).GetMethod("HandleMessageAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return await (Task<NoSqlMessage>)m.Invoke(_server, new object[] { message, conn })!;
    }

    private static JsonElement Data(NoSqlMessage r) => JsonDocument.Parse(r.GetPayloadAsString()).RootElement.GetProperty("data");
    private static string Code(NoSqlMessage r) => JsonDocument.Parse(r.GetPayloadAsString()).RootElement.GetProperty("error").GetProperty("code").GetString()!;

    [Fact]
    public async Task CreateUser_New_ThenListShowsIt()
    {
        var r = await SendAsync(MessageType.Command, """{"command":"createuser","username":"alice","password":"pw123456","role":"readwrite"}""");
        Assert.True(Data(r).GetProperty("created").GetBoolean());

        var list = await SendAsync(MessageType.Command, """{"command":"listusers"}""");
        var users = Data(list).GetProperty("users").EnumerateArray().ToList();
        Assert.Contains(users, u => u.GetProperty("username").GetString() == "alice" && u.GetProperty("role").GetString() == "readwrite");
        Assert.Contains(users, u => u.GetProperty("username").GetString() == "admin" && u.GetProperty("role").GetString() == "admin");
    }

    [Fact]
    public async Task CreateUser_Duplicate_ReturnsUserExists()
    {
        await SendAsync(MessageType.Command, """{"command":"createuser","username":"dup","password":"pw123456","role":"readonly"}""");
        var r = await SendAsync(MessageType.Command, """{"command":"createuser","username":"dup","password":"pw123456","role":"readonly"}""");
        Assert.Equal("USER_EXISTS", Code(r));
    }

    [Fact]
    public async Task CreateUser_BadRole_ReturnsInvalidRole()
    {
        var r = await SendAsync(MessageType.Command, """{"command":"createuser","username":"x","password":"pw123456","role":"superuser"}""");
        Assert.Equal("INVALID_ROLE", Code(r));
    }

    [Fact]
    public async Task CreateUser_ShortPassword_ReturnsWeakPassword()
    {
        var r = await SendAsync(MessageType.Command, """{"command":"createuser","username":"x","password":"pw","role":"readonly"}""");
        Assert.Equal("WEAK_PASSWORD", Code(r));
    }

    [Fact]
    public async Task SetRole_Existing_Changes()
    {
        await SendAsync(MessageType.Command, """{"command":"createuser","username":"sr","password":"pw123456","role":"readonly"}""");
        var r = await SendAsync(MessageType.Command, """{"command":"setrole","username":"sr","role":"readwrite"}""");
        Assert.True(Data(r).GetProperty("changed").GetBoolean());
    }

    [Fact]
    public async Task SetRole_Missing_ReturnsUserNotFound()
    {
        var r = await SendAsync(MessageType.Command, """{"command":"setrole","username":"ghost","role":"readwrite"}""");
        Assert.Equal("USER_NOT_FOUND", Code(r));
    }

    [Fact]
    public async Task SetRole_DemoteLastAdmin_ReturnsLastAdmin()
    {
        var r = await SendAsync(MessageType.Command, """{"command":"setrole","username":"admin","role":"readwrite"}""");
        Assert.Equal("LAST_ADMIN", Code(r));
    }

    [Fact]
    public async Task SetPassword_Existing_ThenReauthWorks()
    {
        await SendAsync(MessageType.Command, """{"command":"createuser","username":"sp","password":"oldpass1","role":"readwrite"}""");
        var r = await SendAsync(MessageType.Command, """{"command":"setpassword","username":"sp","password":"newpass1"}""");
        Assert.True(Data(r).GetProperty("changed").GetBoolean());

        var auth = await SendAsync(MessageType.Authentication, """{"username":"sp","password":"newpass1"}""", "sp-conn");
        Assert.Equal(MessageType.Response, auth.MessageType);
    }

    [Fact]
    public async Task DeleteUser_Existing_Removes()
    {
        await SendAsync(MessageType.Command, """{"command":"createuser","username":"del","password":"pw123456","role":"readonly"}""");
        var r = await SendAsync(MessageType.Command, """{"command":"deleteuser","username":"del"}""");
        Assert.True(Data(r).GetProperty("deleted").GetBoolean());
    }

    [Fact]
    public async Task DeleteUser_LastAdmin_ReturnsLastAdmin()
    {
        var r = await SendAsync(MessageType.Command, """{"command":"deleteuser","username":"admin"}""");
        Assert.Equal("LAST_ADMIN", Code(r));
    }

    [Fact]
    public async Task ChangePassword_WrongOld_ReturnsAuthFailed()
    {
        await SendAsync(MessageType.Command, """{"command":"createuser","username":"cp","password":"rightpw1","role":"readwrite"}""");
        await SendAsync(MessageType.Authentication, """{"username":"cp","password":"rightpw1"}""", "cp-conn");
        var r = await SendAsync(MessageType.Command, """{"command":"changepassword","oldPassword":"wrongpw","newPassword":"newpw123"}""", "cp-conn");
        Assert.Equal("AUTH_FAILED", Code(r));
    }

    [Fact]
    public async Task ChangePassword_CorrectOld_Succeeds()
    {
        await SendAsync(MessageType.Command, """{"command":"createuser","username":"cp2","password":"rightpw1","role":"readwrite"}""");
        await SendAsync(MessageType.Authentication, """{"username":"cp2","password":"rightpw1"}""", "cp2-conn");
        var r = await SendAsync(MessageType.Command, """{"command":"changepassword","oldPassword":"rightpw1","newPassword":"newpw123"}""", "cp2-conn");
        Assert.True(Data(r).GetProperty("changed").GetBoolean());
    }
}
