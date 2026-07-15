# User Management + RBAC Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add persistent user accounts, three enforced roles (`admin`/`readwrite`/`readonly`), user-management TCP commands, and an Admin UI Users page, per `docs/superpowers/specs/2026-07-15-user-management-rbac-design.md` (read it first).

**Architecture:** New shared components in `AdvGenNoSqlServer.Core/Authentication` — `UserRole`, `IUserStore`/`FileUserStore` (atomic `users.json`), role fields on `AuthenticationManager`/`AuthToken`/`UserCredentials`, and a `CommandAuthorizer` (command→access map). Both duplicated TCP dispatchers (Host `NoSqlServerHost`, Server `NoSqlServer`) get identical thin hooks: capture identity+role on Authentication, gate commands when `RequireAuthentication=true`, clear on disconnect. New user-management commands added to both. Client + AdminClient get thin wrappers and a Users page.

**Tech Stack:** .NET 9, xUnit + Moq, Blazor Server + MudBlazor 7, PBKDF2 (existing), System.Text.Json.

**Conventions:**
- Test command: `dotnet test AdvGenNoSqlServer.Tests/AdvGenNoSqlServer.Tests.csproj -c Release --filter "FullyQualifiedName~<TestClass>"` from repo root `e:\Projects\AdvGenNoSQLServer`.
- Dispatcher handler tests invoke the private `HandleMessageAsync` via the reflection helper used by `AdminCommandsTests`/`ClusterCommandTests` (copy `SendCommandAsync`).
- Standard repo copyright header on new files.
- Follow @superpowers:test-driven-development.
- Test ports: 19310–19319 (earlier features used 19291–19302).
- **Enforcement rule (critical):** command gating only runs when `config.RequireAuthentication == true`. With `false`, everything is anonymous exactly as today — this is what keeps the existing 3,200-test suite green.

---

## File Structure Overview

| File | Action | Responsibility |
|---|---|---|
| `Core/Authentication/UserRole.cs` | Create | Role constants + validation |
| `Core/Authentication/IUserStore.cs` | Create | User persistence contract + `PersistedUser` DTO |
| `Core/Authentication/FileUserStore.cs` | Create | Atomic `users.json` load/save |
| `Core/Authentication/AuthenticationManager.cs` | Modify | Role field, persistence, SetPassword/SetRole/ListUsers, last-admin guard, AuthToken.Role |
| `Core/Authentication/CommandAuthorizer.cs` | Create | Command→access map + `IsAllowed` |
| `Core/Configuration/ServerConfiguration.cs` | Modify | `UserStorePath` |
| `Server/NoSqlServer.cs` | Modify | Real auth manager, identity tracking, gating, user commands |
| `Host/Program.cs` | Modify | Identity tracking, gating, user commands, pass UserStore to AuthManager |
| `Client/AdvGenNoSqlClient.Users.cs` | Create | `ListUsersAsync` etc. + `UserInfo` |
| `Client/Client.cs` | Modify | `AuthenticateAsync` captures role; `CurrentRole` property |
| `AdminClient/Services/TcpAdminService.cs` | Modify | User wrappers + `CurrentRole` |
| `AdminClient/Pages/Users.razor` | Create | Users management page |
| `AdminClient/Shared/NavMenu.razor` | Modify | Users nav link (admin only) |
| `AdminClient/Shared/MainLayout.razor` | Modify | Change-password button |
| Tests: `FileUserStoreTests`, `AuthenticationManagerRoleTests`, `CommandAuthorizerTests`, `RbacEnforcementTests`, `UserCommandsTests` | Create | Coverage |

---

### Task 1: UserRole + IUserStore + FileUserStore

**Files:**
- Create: `AdvGenNoSqlServer.Core/Authentication/UserRole.cs`
- Create: `AdvGenNoSqlServer.Core/Authentication/IUserStore.cs`
- Create: `AdvGenNoSqlServer.Core/Authentication/FileUserStore.cs`
- Test: `AdvGenNoSqlServer.Tests/FileUserStoreTests.cs`

- [ ] **Step 1.1: Write failing tests**

```csharp
// AdvGenNoSqlServer.Tests/FileUserStoreTests.cs
using AdvGenNoSqlServer.Core.Authentication;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

public class FileUserStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "advgen-userstore-" + Guid.NewGuid().ToString("N"));
    private string PathFor() => Path.Combine(_dir, "users.json");

    public FileUserStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void Roundtrip_SaveThenLoad_PreservesUsers()
    {
        var store = new FileUserStore(PathFor());
        var users = new List<PersistedUser>
        {
            new() { Username = "admin", PasswordHash = "h1", Salt = "s1", Role = "admin", CreatedAt = DateTime.UtcNow },
            new() { Username = "bob", PasswordHash = "h2", Salt = "s2", Role = "readonly", CreatedAt = DateTime.UtcNow },
        };
        store.Save(users);

        var loaded = new FileUserStore(PathFor()).Load();
        Assert.Equal(2, loaded.Count);
        var bob = loaded.Single(u => u.Username == "bob");
        Assert.Equal("readonly", bob.Role);
        Assert.Equal("h2", bob.PasswordHash);
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmpty()
        => Assert.Empty(new FileUserStore(PathFor()).Load());

    [Fact]
    public void Load_CorruptFile_ReturnsEmpty_AndBacksUp()
    {
        File.WriteAllText(PathFor(), "{ this is not valid json");
        var loaded = new FileUserStore(PathFor()).Load();
        Assert.Empty(loaded);
        Assert.Contains(Directory.GetFiles(_dir), f => f.Contains("users.json.corrupt-"));
    }

    [Fact]
    public void Save_IsAtomic_LeavesNoTempFile()
    {
        var store = new FileUserStore(PathFor());
        store.Save(new List<PersistedUser> { new() { Username = "a", PasswordHash = "h", Salt = "s", Role = "admin", CreatedAt = DateTime.UtcNow } });
        Assert.DoesNotContain(Directory.GetFiles(_dir), f => f.EndsWith(".tmp"));
        Assert.True(File.Exists(PathFor()));
    }
}
```

- [ ] **Step 1.2: Run, verify compile failure**

Run: `dotnet test AdvGenNoSqlServer.Tests/AdvGenNoSqlServer.Tests.csproj -c Release --filter "FullyQualifiedName~FileUserStoreTests"`

- [ ] **Step 1.3: Implement the three files**

```csharp
// AdvGenNoSqlServer.Core/Authentication/UserRole.cs
// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
namespace AdvGenNoSqlServer.Core.Authentication;

/// <summary>Built-in user roles enforced on TCP commands.</summary>
public static class UserRole
{
    public const string Admin = "admin";
    public const string ReadWrite = "readwrite";
    public const string ReadOnly = "readonly";

    public static bool IsValid(string? role) =>
        role is Admin or ReadWrite or ReadOnly;
}
```

```csharp
// AdvGenNoSqlServer.Core/Authentication/IUserStore.cs
// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
namespace AdvGenNoSqlServer.Core.Authentication;

/// <summary>A user record as persisted to disk (hash + salt, never plaintext).</summary>
public sealed class PersistedUser
{
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Salt { get; set; } = "";
    public string Role { get; set; } = UserRole.ReadWrite;
    public DateTime CreatedAt { get; set; }
}

/// <summary>Persistence contract for user accounts.</summary>
public interface IUserStore
{
    IReadOnlyList<PersistedUser> Load();
    void Save(IEnumerable<PersistedUser> users);
}
```

```csharp
// AdvGenNoSqlServer.Core/Authentication/FileUserStore.cs
// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
using System.Text.Json;

namespace AdvGenNoSqlServer.Core.Authentication;

/// <summary>
/// Stores users in a JSON file, independent of the document store so it keeps
/// working under CacheOnly mode. Writes are atomic (temp file + move). A corrupt
/// file is backed up as users.json.corrupt-&lt;timestamp&gt; and treated as empty.
/// </summary>
public sealed class FileUserStore : IUserStore
{
    private sealed class FileShape { public List<PersistedUser> Users { get; set; } = new(); }

    private readonly string _path;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public FileUserStore(string path) => _path = path;

    public IReadOnlyList<PersistedUser> Load()
    {
        if (!File.Exists(_path)) return Array.Empty<PersistedUser>();
        try
        {
            var json = File.ReadAllText(_path);
            var shape = JsonSerializer.Deserialize<FileShape>(json, Options);
            return shape?.Users ?? new List<PersistedUser>();
        }
        catch (JsonException)
        {
            var backup = _path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            try { File.Copy(_path, backup, overwrite: true); } catch (IOException) { }
            return Array.Empty<PersistedUser>();
        }
    }

    public void Save(IEnumerable<PersistedUser> users)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var shape = new FileShape { Users = users.ToList() };
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(shape, Options));
        File.Move(tmp, _path, overwrite: true);
    }
}
```

- [ ] **Step 1.4: Run tests, verify pass**

- [ ] **Step 1.5: Commit**

```bash
git add AdvGenNoSqlServer.Core/Authentication/UserRole.cs AdvGenNoSqlServer.Core/Authentication/IUserStore.cs AdvGenNoSqlServer.Core/Authentication/FileUserStore.cs AdvGenNoSqlServer.Tests/FileUserStoreTests.cs
git commit -m "feat: add UserRole, IUserStore and atomic FileUserStore"
```

---

### Task 2: AuthenticationManager — roles + persistence + admin operations

**Files:**
- Modify: `AdvGenNoSqlServer.Core/Authentication/AuthenticationManager.cs`
- Test: `AdvGenNoSqlServer.Tests/AuthenticationManagerRoleTests.cs`

- [ ] **Step 2.1: Write failing tests**

```csharp
// AdvGenNoSqlServer.Tests/AuthenticationManagerRoleTests.cs
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

        // New manager, same store file — bob survives, keeps his role
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
        var mgr = new AuthenticationManager(Config(), Store()); // only admin exists
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
        // Reload: admin must NOT come back (admin2 is still an admin)
        var mgr2 = new AuthenticationManager(Config(), Store());
        Assert.Null(mgr2.Authenticate("admin", "master-pw"));
    }
}
```

- [ ] **Step 2.2: Run, verify failure**

- [ ] **Step 2.3: Implement**

In `AuthenticationManager.cs`:
1. Add a result enum near the bottom:

```csharp
public enum UserOperationResult { Ok, NotFound, LastAdmin, InvalidRole }
```

2. `UserCredentials` gains `public string Role { get; set; } = UserRole.ReadWrite;`.
3. `AuthToken` gains `public string Role { get; set; } = UserRole.ReadWrite;`.
4. Add fields + updated constructor:

```csharp
    private readonly IUserStore? _userStore;
    private readonly object _mutationLock = new();

    public AuthenticationManager(ServerConfiguration configuration) : this(configuration, null) { }

    public AuthenticationManager(ServerConfiguration configuration, IUserStore? userStore)
    {
        _configuration = configuration;
        _tokenExpiration = TimeSpan.FromHours(configuration.TokenExpirationHours);
        _userStore = userStore;

        // Load persisted users first
        if (_userStore != null)
        {
            foreach (var u in _userStore.Load())
            {
                _users[u.Username] = new UserCredentials
                {
                    Username = u.Username, PasswordHash = u.PasswordHash,
                    Salt = u.Salt, Role = u.Role, CreatedAt = u.CreatedAt
                };
            }
        }

        // Seed admin from MasterPassword only if no admin-role user exists
        if (!string.IsNullOrEmpty(configuration.MasterPassword) &&
            !_users.Values.Any(c => c.Role == UserRole.Admin))
        {
            RegisterUser("admin", configuration.MasterPassword, UserRole.Admin);
        }
    }
```

5. Add a role-aware `RegisterUser` (keep the old 2-arg as an overload delegating with `UserRole.ReadWrite`), and persist inside it. Set `Role` on the created `UserCredentials`. On `Authenticate`, copy `credentials.Role` into the `AuthToken`.

6. New methods (all take `_mutationLock` and call `Persist()` after mutating):

```csharp
    public bool SetPassword(string username, string newPassword)
    {
        lock (_mutationLock)
        {
            if (!_users.TryGetValue(username, out var c)) return false;
            var (salt, hash) = HashPassword(newPassword);
            c.Salt = salt; c.PasswordHash = hash;
            RevokeAllUserTokens(username);
            Persist();
            return true;
        }
    }

    public bool SetRole(string username, string role)
    {
        lock (_mutationLock)
        {
            if (!UserRole.IsValid(role)) return false;
            if (!_users.TryGetValue(username, out var c)) return false;
            c.Role = role;
            Persist();
            return true;
        }
    }

    public IReadOnlyList<(string Username, string Role, DateTime CreatedAt)> ListUsers()
        => _users.Values.Select(c => (c.Username, c.Role, c.CreatedAt)).ToList();

    // Guarded variants used by the command layer for precise error codes
    public UserOperationResult RemoveUserGuarded(string username)
    {
        lock (_mutationLock)
        {
            if (!_users.ContainsKey(username)) return UserOperationResult.NotFound;
            if (IsLastAdmin(username)) return UserOperationResult.LastAdmin;
            _users.TryRemove(username, out _);
            RevokeAllUserTokens(username);
            Persist();
            return UserOperationResult.Ok;
        }
    }

    public UserOperationResult SetRoleGuarded(string username, string role)
    {
        lock (_mutationLock)
        {
            if (!UserRole.IsValid(role)) return UserOperationResult.InvalidRole;
            if (!_users.TryGetValue(username, out var c)) return UserOperationResult.NotFound;
            if (c.Role == UserRole.Admin && role != UserRole.Admin && IsLastAdmin(username))
                return UserOperationResult.LastAdmin;
            c.Role = role;
            Persist();
            return UserOperationResult.Ok;
        }
    }

    private bool IsLastAdmin(string username)
        => _users.TryGetValue(username, out var c) && c.Role == UserRole.Admin
           && _users.Values.Count(x => x.Role == UserRole.Admin) == 1;

    private void Persist()
    {
        _userStore?.Save(_users.Values.Select(c => new PersistedUser
        {
            Username = c.Username, PasswordHash = c.PasswordHash,
            Salt = c.Salt, Role = c.Role, CreatedAt = c.CreatedAt
        }));
    }
```

Also call `Persist()` inside the existing `ChangePassword` and the role-aware `RegisterUser` (and keep `RemoveUser`/`ChangePassword` persisting). Ensure `RegisterUser` sets `Role`.

- [ ] **Step 2.4: Run tests + existing `AuthenticationServiceTests`/`JwtTokenProviderTests`, verify pass**

Run: `dotnet test AdvGenNoSqlServer.Tests/AdvGenNoSqlServer.Tests.csproj -c Release --filter "FullyQualifiedName~AuthenticationManagerRoleTests|FullyQualifiedName~AuthenticationServiceTests"`

- [ ] **Step 2.5: Commit** — `git commit -m "feat: add roles, persistence and admin ops to AuthenticationManager"`

---

### Task 3: CommandAuthorizer

**Files:**
- Create: `AdvGenNoSqlServer.Core/Authentication/CommandAuthorizer.cs`
- Test: `AdvGenNoSqlServer.Tests/CommandAuthorizerTests.cs`

- [ ] **Step 3.1: Write failing tests**

```csharp
// AdvGenNoSqlServer.Tests/CommandAuthorizerTests.cs
using AdvGenNoSqlServer.Core.Authentication;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

public class CommandAuthorizerTests
{
    [Theory]
    [InlineData("get", UserRole.ReadOnly, true)]
    [InlineData("count", UserRole.ReadOnly, true)]
    [InlineData("set", UserRole.ReadOnly, false)]
    [InlineData("delete", UserRole.ReadOnly, false)]
    [InlineData("createuser", UserRole.ReadOnly, false)]
    [InlineData("set", UserRole.ReadWrite, true)]
    [InlineData("createcollection", UserRole.ReadWrite, true)]
    [InlineData("listusers", UserRole.ReadWrite, false)]
    [InlineData("setrole", UserRole.ReadWrite, false)]
    [InlineData("get", UserRole.Admin, true)]
    [InlineData("set", UserRole.Admin, true)]
    [InlineData("createuser", UserRole.Admin, true)]
    [InlineData("deleteuser", UserRole.Admin, true)]
    public void IsAllowed_MatrixMatchesSpec(string command, string role, bool expected)
        => Assert.Equal(expected, CommandAuthorizer.IsAllowed(command, role));

    [Fact]
    public void IsAllowed_ChangePassword_AllowedForAnyRole()
    {
        Assert.True(CommandAuthorizer.IsAllowed("changepassword", UserRole.ReadOnly));
        Assert.True(CommandAuthorizer.IsAllowed("changepassword", UserRole.Admin));
    }

    [Fact]
    public void IsAllowed_UnknownCommand_PassesThrough()
        => Assert.True(CommandAuthorizer.IsAllowed("totally-unknown", UserRole.ReadOnly));

    [Fact]
    public void IsAllowed_IsCaseInsensitiveOnCommand()
        => Assert.False(CommandAuthorizer.IsAllowed("SET", UserRole.ReadOnly));
}
```

- [ ] **Step 3.2: Run, verify failure**

- [ ] **Step 3.3: Implement**

```csharp
// AdvGenNoSqlServer.Core/Authentication/CommandAuthorizer.cs
// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
namespace AdvGenNoSqlServer.Core.Authentication;

/// <summary>Access level a command requires.</summary>
public enum CommandAccess { Read, Write, Admin }

/// <summary>
/// Maps TCP commands to the minimum access they need and decides whether a role
/// may run them. Unknown commands pass through (allowed) so the dispatcher can
/// still return its own UNKNOWN_COMMAND rather than a misleading FORBIDDEN.
/// changepassword needs only an authenticated identity (handled as Read here;
/// dispatchers additionally require identity before dispatch).
/// </summary>
public static class CommandAuthorizer
{
    private static readonly Dictionary<string, CommandAccess> Map = new(StringComparer.Ordinal)
    {
        // Read
        ["get"] = CommandAccess.Read, ["exists"] = CommandAccess.Read,
        ["count"] = CommandAccess.Read, ["find_one"] = CommandAccess.Read,
        ["listcollections"] = CommandAccess.Read, ["listdocuments"] = CommandAccess.Read,
        ["stats"] = CommandAccess.Read, ["changepassword"] = CommandAccess.Read,
        // Write
        ["set"] = CommandAccess.Write, ["delete"] = CommandAccess.Write,
        ["insert"] = CommandAccess.Write, ["replace"] = CommandAccess.Write,
        ["upsert"] = CommandAccess.Write, ["touch"] = CommandAccess.Write,
        ["createcollection"] = CommandAccess.Write, ["dropcollection"] = CommandAccess.Write,
        // Admin
        ["listusers"] = CommandAccess.Admin, ["createuser"] = CommandAccess.Admin,
        ["deleteuser"] = CommandAccess.Admin, ["setpassword"] = CommandAccess.Admin,
        ["setrole"] = CommandAccess.Admin, ["cluster"] = CommandAccess.Admin,
    };

    public static bool IsAllowed(string command, string role)
    {
        if (!Map.TryGetValue(command, out var access))
            return true; // unknown → let the dispatcher handle it

        return role switch
        {
            UserRole.Admin => true,
            UserRole.ReadWrite => access != CommandAccess.Admin,
            UserRole.ReadOnly => access == CommandAccess.Read,
            _ => false
        };
    }
}
```

Note: command names are matched **case-sensitively** here because both dispatchers already lower-case the command before authorizing (they call `commandProp.GetString()?.ToLowerInvariant()`). The `SET` test passes because the authorizer receives already-lowercased input in production; the test calls it directly with `"SET"`, which is unknown to the map → would return `true`, contradicting the test. **Resolve by lowercasing inside `IsAllowed`:** change the first line to `command = command.ToLowerInvariant();` before the lookup, and keep the map keys lowercase. Update the doc comment accordingly.

- [ ] **Step 3.4: Run tests, verify pass** (all matrix + case-insensitive)

- [ ] **Step 3.5: Commit** — `git commit -m "feat: add CommandAuthorizer role/command access map"`

---

### Task 4: ServerConfiguration.UserStorePath

**Files:**
- Modify: `AdvGenNoSqlServer.Core/Configuration/ServerConfiguration.cs`

- [ ] **Step 4.1: Add property** near `StoragePath`:

```csharp
    /// <summary>
    /// Path to the JSON file holding user accounts. If empty, defaults to
    /// &lt;StoragePath&gt;/users.json (resolved absolute like StoragePath).
    /// </summary>
    public string? UserStorePath { get; set; }
```

- [ ] **Step 4.2: Build Core**, `dotnet build AdvGenNoSqlServer.Core -c Release` — clean.

- [ ] **Step 4.3: Commit** — `git commit -m "feat: add UserStorePath server configuration"`

---

### Task 5: Server dispatcher — real auth, identity tracking, gating

**Files:**
- Modify: `AdvGenNoSqlServer.Server/NoSqlServer.cs`
- Test: `AdvGenNoSqlServer.Tests/RbacEnforcementTests.cs`

The Server dispatcher is what the test suite exercises, so RBAC is proven here first.

- [ ] **Step 5.1: Write failing tests**

```csharp
// AdvGenNoSqlServer.Tests/RbacEnforcementTests.cs
using AdvGenNoSqlServer.Core.Authentication;
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

    private ServerConfiguration _config = null!;

    public async Task InitializeAsync()
    {
        _dir = Path.Combine(Path.GetTempPath(), "advgen-rbac-" + Guid.NewGuid().ToString("N"));
        _config = new ServerConfiguration
        {
            Host = "127.0.0.1", Port = 19310,
            StoragePath = _dir,
            RequireAuthentication = true,
            MasterPassword = "master-pw"
        };
        // See Step 5.3 for how the server receives an AuthenticationManager backed by a FileUserStore.
        _server = ServerTestFactory.Create(_config, _dir);
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
    public async Task ReadOnly_CanGet_CannotSet()
    {
        await SendAsync(MessageType.Command, """{"command":"createuser","username":"ro","password":"pw123456","role":"readonly"}""", "admin-conn-pre");
        // (admin-conn-pre needs admin auth first — see helper note below)

        await SendAsync(MessageType.Authentication, """{"username":"ro","password":"pw123456"}""");
        var get = await SendAsync(MessageType.Command, """{"command":"get","collection":"c","id":"x"}""");
        Assert.NotEqual("FORBIDDEN", get.MessageType == MessageType.Error ? Code(get) : "");

        var set = await SendAsync(MessageType.Command, """{"command":"set","collection":"c","document":{"_id":"x"}}""");
        Assert.Equal(MessageType.Error, set.MessageType);
        Assert.Equal("FORBIDDEN", Code(set));
    }

    [Fact]
    public async Task Admin_AuthResponse_IncludesRole()
    {
        var r = await SendAsync(MessageType.Authentication, """{"username":"admin","password":"master-pw"}""");
        var data = JsonDocument.Parse(r.GetPayloadAsString()).RootElement.GetProperty("data");
        Assert.Equal("admin", data.GetProperty("role").GetString());
    }

    [Fact]
    public async Task ReadWrite_CannotCreateUser()
    {
        // Seed a readwrite user as admin, authenticate as them, attempt createuser
        await SendAsync(MessageType.Authentication, """{"username":"admin","password":"master-pw"}""", "adminc");
        await SendAsync(MessageType.Command, """{"command":"createuser","username":"rw","password":"pw123456","role":"readwrite"}""", "adminc");

        await SendAsync(MessageType.Authentication, """{"username":"rw","password":"pw123456"}""");
        var r = await SendAsync(MessageType.Command, """{"command":"createuser","username":"x","password":"pw123456","role":"readonly"}""");
        Assert.Equal("FORBIDDEN", Code(r));
    }
}
```

Note on the test's connection model: each distinct `conn` string is an independent connection with its own auth state. The tests authenticate on one `conn` and issue commands on the same `conn`. Simplify the readonly test to first authenticate `admin` on `Conn`, create the `ro` user, then authenticate `ro` on `Conn` (re-auth replaces identity on that connection) — adjust the sample above so every command runs on `Conn` after the appropriate auth. Keep the assertions.

Also add a small `ServerTestFactory` helper in the test project that builds a `NoSqlServer` whose `AuthenticationManager` uses a `FileUserStore` at `<dir>/users.json` (mirrors production wiring). If `NoSqlServer`'s constructor doesn't accept an `AuthenticationManager`, add that constructor parameter in Step 5.3.

- [ ] **Step 5.2: Run, verify failure**

- [ ] **Step 5.3: Implement in `NoSqlServer.cs`**

1. Constructor: accept an optional `AuthenticationManager` (create a default one from config + a `FileUserStore` at the resolved `UserStorePath`/`<StoragePath>/users.json` when not supplied). Store it in a field `_authManager`.
2. Add `private readonly ConcurrentDictionary<string, (string Username, string Role)> _authConnections = new();`.
3. Rewrite `HandleAuthenticationAsync` to use `_authManager.Authenticate(username, password)`:
   - success → record `_authConnections[connectionId] = (username, token.Role)`; respond `{ authenticated=true, token=token.TokenId, username, role=token.Role }`.
   - When `RequireAuthentication == false`: keep returning anonymous success but also set `_authConnections[connectionId] = ("anonymous", UserRole.Admin)` and report `role="admin"` (so dev-mode admin UI works).
   - failure → `AUTH_FAILED`.
4. In `OnConnectionClosed` (find the existing handler), add `_authConnections.TryRemove(e.ConnectionId, out _);`.
5. In `HandleCommandAsync`, after resolving the lowercased `command` string and **only if `_configurationManager.Configuration.RequireAuthentication`**:

```csharp
            if (_configurationManager.Configuration.RequireAuthentication)
            {
                if (!_authConnections.TryGetValue(connectionId, out var identity))
                    return NoSqlMessage.CreateError("AUTH_REQUIRED", "Authenticate before sending commands");
                if (command == "changepassword" && identity.Username == "anonymous")
                    return NoSqlMessage.CreateError("AUTH_REQUIRED", "changepassword requires an authenticated user");
                if (!CommandAuthorizer.IsAllowed(command, identity.Role))
                    return NoSqlMessage.CreateError("FORBIDDEN", $"Role '{identity.Role}' may not run '{command}'");
            }
```

Note: `HandleCommandAsync` currently takes only `(message, connectionId)` on the Server dispatcher — confirm `connectionId` is in scope (it is; the switch is inside `HandleCommandAsync(NoSqlMessage, string connectionId)`). The command handlers for user management are added in Task 6.

- [ ] **Step 5.4: Run RBAC tests + full auth-related suite, verify pass; then full suite to confirm `RequireAuthentication=false` tests still green**

Run: `dotnet test AdvGenNoSqlServer.Tests/AdvGenNoSqlServer.Tests.csproj -c Release --filter "FullyQualifiedName~RbacEnforcementTests|FullyQualifiedName~ClusterCommandTests|FullyQualifiedName~AdminCommandsTests"`

- [ ] **Step 5.5: Commit** — `git commit -m "feat: enforce role-based command authorization in server dispatcher"`

---

### Task 6: User-management commands (both dispatchers)

**Files:**
- Modify: `AdvGenNoSqlServer.Server/NoSqlServer.cs`
- Modify: `AdvGenNoSqlServer.Host/Program.cs`
- Test: `AdvGenNoSqlServer.Tests/UserCommandsTests.cs`

- [ ] **Step 6.1: Write failing tests** (Server dispatcher, `RequireAuthentication=true`, authenticate as admin on the connection first)

```csharp
// AdvGenNoSqlServer.Tests/UserCommandsTests.cs — same scaffolding as RbacEnforcementTests
// (ServerTestFactory, SendAsync helper, admin auth on the connection in InitializeAsync).
// Cover, asserting data/error codes:
//   createuser new -> {created:true}; duplicate -> USER_EXISTS; bad role -> INVALID_ROLE; short pw -> WEAK_PASSWORD
//   listusers -> contains admin(admin) and the created user with role
//   setrole existing -> {changed:true}; missing -> USER_NOT_FOUND; demote last admin -> LAST_ADMIN
//   setpassword existing -> {changed:true}; then re-auth with new password works
//   deleteuser existing -> {deleted:true}; last admin -> LAST_ADMIN
//   changepassword wrong old -> AUTH_FAILED; correct -> {changed:true}
```

Write these out fully following the `UserCommandsTests` cases above (one `[Fact]` each). Authenticate `admin` on the shared connection in `InitializeAsync`; for `changepassword`, create + authenticate a `readwrite` user on its own connection.

- [ ] **Step 6.2: Run, verify failure** (`UNKNOWN_COMMAND`)

- [ ] **Step 6.3: Implement — Server dispatcher**

Add to the command switch:

```csharp
                "listusers" => HandleListUsersCommand(),
                "createuser" => HandleCreateUserCommand(doc.RootElement),
                "deleteuser" => HandleDeleteUserCommand(doc.RootElement),
                "setpassword" => HandleSetPasswordCommand(doc.RootElement),
                "setrole" => HandleSetRoleCommand(doc.RootElement),
                "changepassword" => HandleChangePasswordCommand(doc.RootElement, connectionId),
```

Handlers (all synchronous over `_authManager`; return `Task.FromResult(...)` to match the switch's expression type — check whether sibling handlers are async and mirror them):

```csharp
    private NoSqlMessage HandleListUsersCommand()
        => NoSqlMessage.CreateSuccess(new
        {
            users = _authManager.ListUsers()
                .Select(u => new { username = u.Username, role = u.Role, createdAt = u.CreatedAt })
        });

    private NoSqlMessage HandleCreateUserCommand(JsonElement e)
    {
        var username = e.TryGetProperty("username", out var u) ? u.GetString() : null;
        var password = e.TryGetProperty("password", out var p) ? p.GetString() : null;
        var role = e.TryGetProperty("role", out var r) ? r.GetString() : UserRole.ReadWrite;
        if (string.IsNullOrWhiteSpace(username) || username.Length > 64)
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Username required (<= 64 chars)");
        if (string.IsNullOrEmpty(password) || password.Length < 6)
            return NoSqlMessage.CreateError("WEAK_PASSWORD", "Password must be at least 6 characters");
        if (!UserRole.IsValid(role))
            return NoSqlMessage.CreateError("INVALID_ROLE", $"Invalid role '{role}'");
        if (!_authManager.RegisterUser(username, password, role!))
            return NoSqlMessage.CreateError("USER_EXISTS", $"User '{username}' already exists");
        return NoSqlMessage.CreateSuccess(new { created = true, username });
    }

    private NoSqlMessage HandleDeleteUserCommand(JsonElement e)
    {
        var username = e.TryGetProperty("username", out var u) ? u.GetString() : null;
        if (string.IsNullOrWhiteSpace(username))
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Username required");
        return _authManager.RemoveUserGuarded(username) switch
        {
            UserOperationResult.Ok => NoSqlMessage.CreateSuccess(new { deleted = true }),
            UserOperationResult.NotFound => NoSqlMessage.CreateError("USER_NOT_FOUND", $"User '{username}' not found"),
            UserOperationResult.LastAdmin => NoSqlMessage.CreateError("LAST_ADMIN", "Cannot delete the last admin"),
            _ => NoSqlMessage.CreateError("COMMAND_ERROR", "Delete failed")
        };
    }

    private NoSqlMessage HandleSetPasswordCommand(JsonElement e)
    {
        var username = e.TryGetProperty("username", out var u) ? u.GetString() : null;
        var password = e.TryGetProperty("password", out var p) ? p.GetString() : null;
        if (string.IsNullOrWhiteSpace(username))
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Username required");
        if (string.IsNullOrEmpty(password) || password.Length < 6)
            return NoSqlMessage.CreateError("WEAK_PASSWORD", "Password must be at least 6 characters");
        return _authManager.SetPassword(username, password)
            ? NoSqlMessage.CreateSuccess(new { changed = true })
            : NoSqlMessage.CreateError("USER_NOT_FOUND", $"User '{username}' not found");
    }

    private NoSqlMessage HandleSetRoleCommand(JsonElement e)
    {
        var username = e.TryGetProperty("username", out var u) ? u.GetString() : null;
        var role = e.TryGetProperty("role", out var r) ? r.GetString() : null;
        if (string.IsNullOrWhiteSpace(username))
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Username required");
        return _authManager.SetRoleGuarded(username, role ?? "") switch
        {
            UserOperationResult.Ok => NoSqlMessage.CreateSuccess(new { changed = true }),
            UserOperationResult.NotFound => NoSqlMessage.CreateError("USER_NOT_FOUND", $"User '{username}' not found"),
            UserOperationResult.InvalidRole => NoSqlMessage.CreateError("INVALID_ROLE", $"Invalid role '{role}'"),
            UserOperationResult.LastAdmin => NoSqlMessage.CreateError("LAST_ADMIN", "Cannot demote the last admin"),
            _ => NoSqlMessage.CreateError("COMMAND_ERROR", "Set role failed")
        };
    }

    private NoSqlMessage HandleChangePasswordCommand(JsonElement e, string connectionId)
    {
        if (!_authConnections.TryGetValue(connectionId, out var identity) || identity.Username == "anonymous")
            return NoSqlMessage.CreateError("AUTH_REQUIRED", "changepassword requires an authenticated user");
        var oldPw = e.TryGetProperty("oldPassword", out var o) ? o.GetString() : null;
        var newPw = e.TryGetProperty("newPassword", out var n) ? n.GetString() : null;
        if (string.IsNullOrEmpty(newPw) || newPw.Length < 6)
            return NoSqlMessage.CreateError("WEAK_PASSWORD", "Password must be at least 6 characters");
        return _authManager.ChangePassword(identity.Username, oldPw ?? "", newPw)
            ? NoSqlMessage.CreateSuccess(new { changed = true })
            : NoSqlMessage.CreateError("AUTH_FAILED", "Old password is incorrect");
    }
```

If the switch arms are `await`-ed, wrap each return in `Task.FromResult(...)` or make these `async Task<NoSqlMessage>` with no `await` — match the file's existing pattern (the Server dispatcher's arms are `Handle...(doc.RootElement)` returning `Task<NoSqlMessage>`, so give these `Task<NoSqlMessage>` signatures via `Task.FromResult`).

- [ ] **Step 6.4: Implement — Host dispatcher** (`Host/Program.cs`)

Mirror the same six handlers and switch arms in `NoSqlServerHost`. It already has `_authManager` (constructor-injected `AuthenticationManager`) — but that manager is currently built without a user store. In `ConfigureServices` (DI registration ~line 387), change the `AuthenticationManager` factory to pass a `FileUserStore`:

```csharp
        services.AddSingleton<AuthenticationManager>(provider =>
        {
            var config = provider.GetRequiredService<Core.Configuration.IConfigurationManager>().Configuration;
            var storagePath = string.IsNullOrEmpty(config.StoragePath) ? "data" : config.StoragePath;
            if (!System.IO.Path.IsPathRooted(storagePath))
                storagePath = System.IO.Path.Combine(AppContext.BaseDirectory, storagePath);
            var userPath = string.IsNullOrEmpty(config.UserStorePath)
                ? System.IO.Path.Combine(storagePath, "users.json")
                : config.UserStorePath;
            return new AuthenticationManager(config, new FileUserStore(userPath));
        });
```

Add `_authConnections` to `NoSqlServerHost`, populate it in `HandleAuthenticationAsync` (both the real and the `RequireAuthentication==false` anonymous branch → role `admin`), clear it in `OnConnectionClosed`, and add the same gating block in `HandleCommandAsync`. Add the six user-command handlers (identical bodies, adapting to the Host's `_apiData`/`_authManager` field names; user commands only touch `_authManager`, not the document store).

- [ ] **Step 6.5: Run user-command tests + full suite, verify pass**

Run: `dotnet test AdvGenNoSqlServer.Tests/AdvGenNoSqlServer.Tests.csproj -c Release`
Expected: all green except the known pre-existing `BackgroundIndexBuilderTests.StartBuildAsync_MultipleConcurrentBuilds_RespectsMaxConcurrent` flake.

- [ ] **Step 6.6: Commit** — `git commit -m "feat: add user-management TCP commands to both dispatchers"`

---

### Task 7: Client library — Users API + role capture

**Files:**
- Create: `AdvGenNoSqlServer.Client/AdvGenNoSqlClient.Users.cs`
- Modify: `AdvGenNoSqlServer.Client/Client.cs` (`AuthenticateAsync`, add `CurrentRole`)
- Test: `AdvGenNoSqlServer.Tests/UserClientTests.cs`

- [ ] **Step 7.1: Write failing end-to-end tests** (real server + client, `RequireAuthentication=true`, following `CacheClientTests`/`ClientGetFixTests` server-start pattern with a temp storage dir and `MasterPassword`)

```csharp
// Cover:
//   after AuthenticateAsync("admin", masterPw) -> client.CurrentRole == "admin"
//   CreateUserAsync("ro","pw123456","readonly") -> true; ListUsersAsync contains it
//   SetUserRoleAsync("ro","readwrite") -> true
//   SetUserPasswordAsync("ro","newpass1") -> true
//   DeleteUserAsync("ro") -> true
//   CreateUserAsync duplicate -> throws NoSqlClientException with "USER_EXISTS" in message
//   a second client authenticating as a readonly user: CurrentRole=="readonly",
//     GetAsync works, SetAsync throws NoSqlClientException mentioning FORBIDDEN
```

- [ ] **Step 7.2: Run, verify failure**

- [ ] **Step 7.3: Implement**

`Client.cs` — extend `AuthenticateAsync` to capture role and expose it:

```csharp
        public string? CurrentRole { get; private set; }
```

In `AuthenticateAsync`, after a successful `ParseResponse`, read the role:

```csharp
            if (response.MessageType == MessageType.Response)
            {
                var result = ParseResponse(response);
                if (result.Success && result.Data is System.Text.Json.JsonElement data &&
                    data.TryGetProperty("role", out var roleEl))
                {
                    CurrentRole = roleEl.GetString();
                }
                return result.Success;
            }
```

`AdvGenNoSqlClient.Users.cs` (partial class):

```csharp
// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
using AdvGenNoSqlServer.Network;

namespace AdvGenNoSqlServer.Client
{
    public record UserInfo(string Username, string Role, DateTime CreatedAt);

    public partial class AdvGenNoSqlClient
    {
        public async Task<IReadOnlyList<UserInfo>> ListUsersAsync(CancellationToken ct = default)
        {
            var resp = await SendUserCommandAsync(new { command = "listusers" }, ct);
            var list = new List<UserInfo>();
            if (resp.Data is System.Text.Json.JsonElement data && data.TryGetProperty("users", out var arr))
            {
                foreach (var u in arr.EnumerateArray())
                {
                    list.Add(new UserInfo(
                        u.GetProperty("username").GetString() ?? "",
                        u.GetProperty("role").GetString() ?? "",
                        u.TryGetProperty("createdAt", out var c) ? c.GetDateTime() : default));
                }
            }
            return list;
        }

        public Task<bool> CreateUserAsync(string username, string password, string role, CancellationToken ct = default)
            => BoolUserCommandAsync(new { command = "createuser", username, password, role }, "created", ct);

        public Task<bool> DeleteUserAsync(string username, CancellationToken ct = default)
            => BoolUserCommandAsync(new { command = "deleteuser", username }, "deleted", ct);

        public Task<bool> SetUserPasswordAsync(string username, string password, CancellationToken ct = default)
            => BoolUserCommandAsync(new { command = "setpassword", username, password }, "changed", ct);

        public Task<bool> SetUserRoleAsync(string username, string role, CancellationToken ct = default)
            => BoolUserCommandAsync(new { command = "setrole", username, role }, "changed", ct);

        public Task<bool> ChangeMyPasswordAsync(string oldPassword, string newPassword, CancellationToken ct = default)
            => BoolUserCommandAsync(new { command = "changepassword", oldPassword, newPassword }, "changed", ct);

        private async Task<NoSqlResponse> SendUserCommandAsync(object payload, CancellationToken ct)
        {
            EnsureConnected();
            var message = NoSqlMessage.Create(MessageType.Command,
                System.Text.Json.JsonSerializer.Serialize(payload));
            var response = await SendAndReceiveAsync(message, ct);
            var result = ParseResponse(response);
            if (!result.Success)
                throw new NoSqlClientException(
                    $"{result.Error?.Code}: {result.Error?.Message}");
            return result;
        }

        private async Task<bool> BoolUserCommandAsync(object payload, string flag, CancellationToken ct)
        {
            var resp = await SendUserCommandAsync(payload, ct);
            return resp.Data is System.Text.Json.JsonElement d && d.TryGetProperty(flag, out var f) && f.GetBoolean();
        }
    }
}
```

Note: `SendUserCommandAsync`, `EnsureConnected`, `SendAndReceiveAsync`, `ParseResponse`, `NoSqlClientException` all already exist on the partial class / namespace. Verify `NoSqlResponse.Data` is a `JsonElement` (it is — `ParseResponse` deserializes to one).

- [ ] **Step 7.4: Run client tests, verify pass**

- [ ] **Step 7.5: Commit** — `git commit -m "feat: add client Users API and role capture on authenticate"`

---

### Task 8: TcpAdminService wrappers + CurrentRole

**Files:**
- Modify: `AdvGenNoSqlServer.AdminClient/Services/TcpAdminService.cs`

No AdminClient test project; correctness rides on Task 7's end-to-end tests (these are one-line pass-throughs).

- [ ] **Step 8.1: Add `CurrentRole`** — a `public string? CurrentRole { get; private set; }` set in `ConnectAndAuthenticateAsync` from `_client.CurrentRole` after authentication succeeds; cleared in `DisconnectAsync`.

- [ ] **Step 8.2: Add wrappers** (each `EnsureConnected()` then delegates):

```csharp
    public Task<List<UserInfo>> ListUsersAsync() { EnsureConnected(); return _client!.ListUsersAsync().ContinueWith(t => t.Result.ToList()); }
    public Task<bool> CreateUserAsync(string u, string p, string role) { EnsureConnected(); return _client!.CreateUserAsync(u, p, role); }
    public Task<bool> DeleteUserAsync(string u) { EnsureConnected(); return _client!.DeleteUserAsync(u); }
    public Task<bool> SetUserPasswordAsync(string u, string p) { EnsureConnected(); return _client!.SetUserPasswordAsync(u, p); }
    public Task<bool> SetUserRoleAsync(string u, string role) { EnsureConnected(); return _client!.SetUserRoleAsync(u, role); }
    public Task<bool> ChangeMyPasswordAsync(string oldP, string newP) { EnsureConnected(); return _client!.ChangeMyPasswordAsync(oldP, newP); }
```

Prefer clean `async`/`await` bodies over `ContinueWith` — use `public async Task<List<UserInfo>> ListUsersAsync() { EnsureConnected(); return (await _client!.ListUsersAsync()).ToList(); }`. Add `using AdvGenNoSqlServer.Client;` if needed (already referenced).

- [ ] **Step 8.3: Build AdminClient**, `dotnet build AdvGenNoSqlServer.AdminClient -c Release` — clean.

- [ ] **Step 8.4: Commit** — `git commit -m "feat: add user-management wrappers and CurrentRole to TcpAdminService"`

---

### Task 9: Admin UI — Users page, nav link, change-password button

**Files:**
- Create: `AdvGenNoSqlServer.AdminClient/Pages/Users.razor`
- Modify: `AdvGenNoSqlServer.AdminClient/Shared/NavMenu.razor`
- Modify: `AdvGenNoSqlServer.AdminClient/Shared/MainLayout.razor`

- [ ] **Step 9.1: Nav link** (admin-only) — after the Console link in `NavMenu.razor`:

```razor
    @if (AdminService.CurrentRole == "admin")
    {
        <MudNavLink Href="users" Match="NavLinkMatch.Prefix" Icon="@Icons.Material.Filled.People">
            Users
        </MudNavLink>
    }
```

Add `@inject AdvGenNoSqlServer.AdminClient.Services.TcpAdminService AdminService` at the top of `NavMenu.razor` if not present.

- [ ] **Step 9.2: Users page** — create `Pages/Users.razor` following `Collections.razor`'s structure (MudTable, dialogs, snackbar). Include:
  - Redirect to `/login` if not connected in `OnInitialized`.
  - If `AdminService.CurrentRole != "admin"`: show `<MudAlert Severity="Severity.Warning">User management requires the admin role.</MudAlert>` and nothing else.
  - Table columns: Username, Role (MudChip), Created, Actions (change role via MudSelect inline or a dialog, reset password via `TextInputDialog` with `InputType` password if available else plain, delete via `ConfirmDialog`). Disable delete when the row is the only admin (compute `_adminCount`).
  - "Create User" button → dialog collecting username, password, role (MudSelect of the three roles). Call `CreateUserAsync`, snackbar the specific error code on failure.
  - After each mutation, reload via `ListUsersAsync`.

Full component:

```razor
@page "/users"
@inject TcpAdminService AdminService
@inject NavigationManager Navigation
@inject ISnackbar Snackbar
@inject IDialogService DialogService

<PageTitle>Users - AdvGenNoSQL Admin</PageTitle>

<div class="d-flex align-center mb-4">
    <MudText Typo="Typo.h4" Class="flex-grow-1">Users</MudText>
    @if (AdminService.CurrentRole == "admin")
    {
        <MudButton Variant="Variant.Filled" Color="Color.Primary" StartIcon="@Icons.Material.Filled.PersonAdd"
                   OnClick="CreateUser">New User</MudButton>
    }
</div>

@if (AdminService.CurrentRole != "admin")
{
    <MudAlert Severity="Severity.Warning">User management requires the admin role.</MudAlert>
}
else if (_loading)
{
    <MudProgressCircular Indeterminate="true" />
}
else
{
    <MudTable Items="_users" Hover="true" Dense="true">
        <HeaderContent>
            <MudTh>Username</MudTh><MudTh>Role</MudTh><MudTh>Created</MudTh><MudTh>Actions</MudTh>
        </HeaderContent>
        <RowTemplate>
            <MudTd>@context.Username</MudTd>
            <MudTd><MudChip T="string" Size="Size.Small"
                            Color="@(context.Role == "admin" ? Color.Error : context.Role == "readwrite" ? Color.Info : Color.Default)">
                @context.Role</MudChip></MudTd>
            <MudTd>@context.CreatedAt.ToLocalTime().ToString("g")</MudTd>
            <MudTd>
                <MudIconButton Icon="@Icons.Material.Filled.ManageAccounts" Size="Size.Small"
                               OnClick="@(() => ChangeRole(context))" title="Change role" />
                <MudIconButton Icon="@Icons.Material.Filled.Password" Size="Size.Small"
                               OnClick="@(() => ResetPassword(context))" title="Reset password" />
                <MudIconButton Icon="@Icons.Material.Filled.Delete" Color="Color.Error" Size="Size.Small"
                               Disabled="@(context.Role == "admin" && _adminCount <= 1)"
                               OnClick="@(() => DeleteUser(context))" title="Delete" />
            </MudTd>
        </RowTemplate>
    </MudTable>
}

@code {
    private List<UserInfo> _users = new();
    private bool _loading = true;
    private int _adminCount;

    protected override async Task OnInitializedAsync()
    {
        if (!AdminService.IsConnected) { Navigation.NavigateTo("/login"); return; }
        if (AdminService.CurrentRole == "admin") await LoadAsync();
        else _loading = false;
    }

    private async Task LoadAsync()
    {
        _loading = true;
        try
        {
            _users = await AdminService.ListUsersAsync();
            _adminCount = _users.Count(u => u.Role == "admin");
        }
        catch (Exception ex) { Snackbar.Add($"Failed to load users: {ex.Message}", Severity.Error); }
        finally { _loading = false; }
    }

    private async Task CreateUser()
    {
        var dlg = await DialogService.ShowAsync<UserCreateDialog>("Create User");
        var res = await dlg.Result;
        if (res?.Canceled == false && res.Data is UserCreateDialog.NewUser nu)
        {
            try { await AdminService.CreateUserAsync(nu.Username, nu.Password, nu.Role); Snackbar.Add("User created", Severity.Success); await LoadAsync(); }
            catch (Exception ex) { Snackbar.Add(ex.Message, Severity.Error); }
        }
    }

    private async Task ChangeRole(UserInfo u)
    {
        var parameters = new DialogParameters<TextInputDialog>
        {
            { x => x.Title, $"Role for {u.Username}" },
            { x => x.Message, "Enter role: admin, readwrite, or readonly" },
            { x => x.Label, "Role" },
            { x => x.InitialValue, u.Role }
        };
        var dlg = await DialogService.ShowAsync<TextInputDialog>("Change Role", parameters);
        var res = await dlg.Result;
        if (res?.Canceled == false && res.Data is string role && !string.IsNullOrWhiteSpace(role))
        {
            try { await AdminService.SetUserRoleAsync(u.Username, role.Trim()); Snackbar.Add("Role updated", Severity.Success); await LoadAsync(); }
            catch (Exception ex) { Snackbar.Add(ex.Message, Severity.Error); }
        }
    }

    private async Task ResetPassword(UserInfo u)
    {
        var parameters = new DialogParameters<TextInputDialog>
        {
            { x => x.Title, $"Reset password for {u.Username}" },
            { x => x.Message, "Enter a new password (min 6 chars):" },
            { x => x.Label, "New password" }
        };
        var dlg = await DialogService.ShowAsync<TextInputDialog>("Reset Password", parameters);
        var res = await dlg.Result;
        if (res?.Canceled == false && res.Data is string pw && !string.IsNullOrWhiteSpace(pw))
        {
            try { await AdminService.SetUserPasswordAsync(u.Username, pw); Snackbar.Add("Password reset", Severity.Success); }
            catch (Exception ex) { Snackbar.Add(ex.Message, Severity.Error); }
        }
    }

    private async Task DeleteUser(UserInfo u)
    {
        var parameters = new DialogParameters<ConfirmDialog>
        {
            { x => x.Message, $"Delete user '{u.Username}'?" },
            { x => x.ConfirmText, "Delete" }
        };
        var dlg = await DialogService.ShowAsync<ConfirmDialog>("Delete User", parameters);
        var res = await dlg.Result;
        if (res?.Canceled == false)
        {
            try { await AdminService.DeleteUserAsync(u.Username); Snackbar.Add("User deleted", Severity.Success); await LoadAsync(); }
            catch (Exception ex) { Snackbar.Add(ex.Message, Severity.Error); }
        }
    }
}
```

Also create a tiny `Pages/UserCreateDialog.razor` (or reuse three sequential `TextInputDialog`s — simpler). To avoid a new dialog component, replace `CreateUser` with three prompts (username, password, role via `TextInputDialog`) mirroring the pattern above; skip `UserCreateDialog` entirely. **Recommended:** implement `CreateUser` with three sequential `TextInputDialog` prompts and delete the `UserCreateDialog` reference, so no new dialog component is needed.

- [ ] **Step 9.3: Change-password button** in `MainLayout.razor` — beside the logout button, inside the `@if (AdminService.IsConnected)` block:

```razor
            <MudIconButton Icon="@Icons.Material.Filled.Password" Color="Color.Inherit"
                           OnClick="ChangePassword" title="Change my password" />
```

And in `@code`:

```csharp
    private async Task ChangePassword()
    {
        var oldDlg = await DialogService.ShowAsync<TextInputDialog>("Change Password", new DialogParameters<TextInputDialog>
        {
            { x => x.Title, "Change Password" },
            { x => x.Message, "Current password:" },
            { x => x.Label, "Current password" }
        });
        var oldRes = await oldDlg.Result;
        if (oldRes?.Canceled != false || oldRes.Data is not string oldPw) return;

        var newDlg = await DialogService.ShowAsync<TextInputDialog>("Change Password", new DialogParameters<TextInputDialog>
        {
            { x => x.Title, "Change Password" },
            { x => x.Message, "New password (min 6 chars):" },
            { x => x.Label, "New password" }
        });
        var newRes = await newDlg.Result;
        if (newRes?.Canceled != false || newRes.Data is not string newPw) return;

        try { await AdminService.ChangeMyPasswordAsync(oldPw, newPw); Snackbar.Add("Password changed", Severity.Success); }
        catch (Exception ex) { Snackbar.Add(ex.Message, Severity.Error); }
    }
```

Add `@inject IDialogService DialogService` to `MainLayout.razor` if not present (it injects `AdminService`, `Navigation`, `Snackbar` already).

- [ ] **Step 9.4: Build AdminClient**, verify clean (`dotnet build AdvGenNoSqlServer.AdminClient -c Release`).

- [ ] **Step 9.5: Commit** — `git commit -m "feat: add Users admin page, nav link and change-password dialog"`

---

### Task 10: End-to-end verification + docs

**Files:**
- Modify: `AdvGenNoSqlServer.AdminClient/README.md` (mention roles + Users page)
- Modify: `README.md` (feature bullet: user management + RBAC)

- [ ] **Step 10.1: Full test suite**

Run: `dotnet test AdvGenNoSqlServer.Tests/AdvGenNoSqlServer.Tests.csproj -c Release`
Expected: green except the known `BackgroundIndexBuilderTests` flake.

- [ ] **Step 10.2: E2E over live Host** — write a scratchpad console app (like the admin-ui-fixes E2E) referencing the Client that:
  1. Connects+authenticates as `admin` (MasterPassword), asserts `CurrentRole=="admin"`.
  2. `CreateUserAsync("e2e-ro","pw123456","readonly")`, asserts `ListUsersAsync` contains it.
  3. Opens a second client, authenticates as `e2e-ro`, asserts `CurrentRole=="readonly"`, `GetAsync` works, `SetAsync` throws mentioning FORBIDDEN.
  4. Restart the Host process; reconnect as `e2e-ro` with the same password → still works (persistence).
  5. Cleanup: delete `e2e-ro` as admin.

Set `RequireAuthentication=true` and a known `MasterPassword` in the Host appsettings for the run (revert after). Start Host, run the harness, assert all pass.

- [ ] **Step 10.3: Verify AdminClient serves** — start it, `curl -k https://localhost:7210/login` returns 200; optionally confirm `/users` markup (it will redirect to login without a session, which is expected).

- [ ] **Step 10.4: Update READMEs**, then use @superpowers:verification-before-completion.

- [ ] **Step 10.5: Commit** — `git commit -m "docs: document user management and RBAC"`

- [ ] **Step 10.6: Use @superpowers:requesting-code-review, then @superpowers:finishing-a-development-branch**

---

## Execution notes

- Task order 1→10; Tasks 1–4 are pure Core and independent-ish (4 can move earlier). Tasks 5–6 depend on 1–4. Tasks 7–9 depend on 6. Task 10 is last.
- **Regression guard:** after Task 5 and again after Task 6, run the FULL suite, not just the new filters — the gating block is the highest-risk change. Any newly-failing test that connects without authenticating and expects success indicates the `RequireAuthentication` guard is wrong (it must be `false` in those tests) — do NOT weaken the guard; fix the test's config or confirm it sets `RequireAuthentication=false`.
- Both dispatchers must stay in lockstep (audit D6 debt). A change to one without the other will pass the test suite (Server only) but break production (Host) or vice versa — always edit both in Task 6.
- Ports 19310–19319 for new test classes.
