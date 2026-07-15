// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Authentication;
using AdvGenNoSqlServer.Core.Configuration;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

public class AuthenticationManagerRoleTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "advgen-authmgr-" + Guid.NewGuid().ToString("N"));
    private string StorePath() => Path.Combine(_dir, "users.json");
    private FileUserStore Store() => new(StorePath());
    private static ServerConfiguration Config() => new() { MasterPassword = "master-pw", TokenExpirationHours = 1 };

    public AuthenticationManagerRoleTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void SeedsAdminFromMasterPassword_WhenStoreEmpty()
    {
        var mgr = new AuthenticationManager(Config(), Store());
        var token = mgr.Authenticate("admin", "master-pw");
        Assert.NotNull(token);
        Assert.Equal(UserRole.Admin, token!.Role);
    }

    [Fact]
    public void RegisterUser_WithRole_PersistsAcrossReload()
    {
        var mgr = new AuthenticationManager(Config(), Store());
        Assert.True(mgr.RegisterUser("bob", "pw123456", UserRole.ReadOnly));

        var mgr2 = new AuthenticationManager(Config(), Store());
        var token = mgr2.Authenticate("bob", "pw123456");
        Assert.NotNull(token);
        Assert.Equal(UserRole.ReadOnly, token!.Role);
    }

    [Fact]
    public void SetRole_ChangesRole_AndPersists()
    {
        var mgr = new AuthenticationManager(Config(), Store());
        mgr.RegisterUser("bob", "pw123456", UserRole.ReadOnly);
        Assert.True(mgr.SetRole("bob", UserRole.ReadWrite));
        Assert.Equal(UserRole.ReadWrite, new AuthenticationManager(Config(), Store()).Authenticate("bob", "pw123456")!.Role);
    }

    [Fact]
    public void SetPassword_ChangesPassword_NoOldPasswordNeeded()
    {
        var mgr = new AuthenticationManager(Config(), Store());
        mgr.RegisterUser("bob", "oldpass", UserRole.ReadWrite);
        Assert.True(mgr.SetPassword("bob", "newpass1"));
        Assert.Null(mgr.Authenticate("bob", "oldpass"));
        Assert.NotNull(mgr.Authenticate("bob", "newpass1"));
    }

    [Fact]
    public void ListUsers_ReturnsUsernamesAndRoles_NoHashes()
    {
        var mgr = new AuthenticationManager(Config(), Store());
        mgr.RegisterUser("bob", "pw123456", UserRole.ReadOnly);
        var users = mgr.ListUsers();
        Assert.Contains(users, u => u.Username == "admin" && u.Role == UserRole.Admin);
        Assert.Contains(users, u => u.Username == "bob" && u.Role == UserRole.ReadOnly);
    }

    [Fact]
    public void RemoveUser_LastAdmin_Fails()
    {
        var mgr = new AuthenticationManager(Config(), Store());
        Assert.Equal(UserOperationResult.LastAdmin, mgr.RemoveUserGuarded("admin"));
    }

    [Fact]
    public void SetRole_DemotingLastAdmin_Fails()
    {
        var mgr = new AuthenticationManager(Config(), Store());
        Assert.Equal(UserOperationResult.LastAdmin, mgr.SetRoleGuarded("admin", UserRole.ReadWrite));
    }

    [Fact]
    public void RemoveUser_AdminWhenAnotherAdminExists_Succeeds()
    {
        var mgr = new AuthenticationManager(Config(), Store());
        mgr.RegisterUser("admin2", "pw123456", UserRole.Admin);
        Assert.Equal(UserOperationResult.Ok, mgr.RemoveUserGuarded("admin"));
    }

    [Fact]
    public void DeletedAdmin_NotResurrected_WhenAnotherAdminExists()
    {
        var mgr = new AuthenticationManager(Config(), Store());
        mgr.RegisterUser("admin2", "pw123456", UserRole.Admin);
        mgr.RemoveUserGuarded("admin");
        var mgr2 = new AuthenticationManager(Config(), Store());
        Assert.Null(mgr2.Authenticate("admin", "master-pw"));
    }
}
