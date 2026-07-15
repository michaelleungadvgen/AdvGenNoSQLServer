// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Client;
using AdvGenNoSqlServer.Core.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using ServerNoSql = AdvGenNoSqlServer.Server.NoSqlServer;

namespace AdvGenNoSqlServer.Tests;

public class UserClientTests : IAsyncLifetime
{
    private const int Port = 19312;
    private ServerNoSql _server = null!;
    private string _dir = null!;

    public async Task InitializeAsync()
    {
        _dir = Path.Combine(Path.GetTempPath(), "advgen-userclient-" + Guid.NewGuid().ToString("N"));
        var config = new ServerConfiguration
        {
            Host = "127.0.0.1",
            Port = Port,
            StoragePath = _dir,
            RequireAuthentication = true,
            MasterPassword = "master-pw",
            EnableSsl = false
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

    private static async Task<AdvGenNoSqlClient> ConnectAsync(string user, string pw)
    {
        var client = new AdvGenNoSqlClient($"127.0.0.1:{Port}");
        await client.ConnectAsync();
        var ok = await client.AuthenticateAsync(user, pw);
        Assert.True(ok);
        return client;
    }

    [Fact]
    public async Task Admin_ManagesUsers_EndToEnd()
    {
        await using var admin = await ConnectAsync("admin", "master-pw");
        Assert.Equal("admin", admin.CurrentRole);

        Assert.True(await admin.CreateUserAsync("e2e-ro", "pw123456", "readonly"));
        var users = await admin.ListUsersAsync();
        Assert.Contains(users, u => u.Username == "e2e-ro" && u.Role == "readonly");

        Assert.True(await admin.SetUserRoleAsync("e2e-ro", "readwrite"));
        Assert.True(await admin.SetUserPasswordAsync("e2e-ro", "newpass1"));
        Assert.True(await admin.DeleteUserAsync("e2e-ro"));
    }

    [Fact]
    public async Task CreateUser_Duplicate_Throws()
    {
        await using var admin = await ConnectAsync("admin", "master-pw");
        await admin.CreateUserAsync("dupe", "pw123456", "readonly");
        var ex = await Assert.ThrowsAsync<NoSqlClientException>(() => admin.CreateUserAsync("dupe", "pw123456", "readonly"));
        Assert.Contains("USER_EXISTS", ex.Message);
    }

    [Fact]
    public async Task ReadOnlyUser_CanGet_CannotSet()
    {
        await using (var admin = await ConnectAsync("admin", "master-pw"))
        {
            await admin.CreateUserAsync("ro-client", "pw123456", "readonly");
        }

        await using var ro = await ConnectAsync("ro-client", "pw123456");
        Assert.Equal("readonly", ro.CurrentRole);

        // GET works (missing doc → null, no exception)
        var doc = await ro.GetAsync("c", "missing");
        Assert.Null(doc);

        // SET is forbidden (client surfaces the server's human-readable message)
        var ex = await Assert.ThrowsAsync<NoSqlClientException>(
            () => ro.SetAsync("c", new Dictionary<string, object> { ["_id"] = "x" }));
        Assert.Contains("may not run", ex.Message);
    }
}
