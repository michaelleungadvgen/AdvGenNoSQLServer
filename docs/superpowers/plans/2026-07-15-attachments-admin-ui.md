# Document Attachments over TCP + Admin UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose the existing `AttachmentStore` over the TCP protocol (six base64-JSON commands, RBAC-gated), add a `client.Attachments` API, and surface files in the Admin UI (attachments dialog per Documents row + a Files tile on the Dashboard), per `docs/superpowers/specs/2026-07-15-attachments-admin-ui-design.md` (read it first).

**Architecture:** Both TCP dispatchers construct one `AttachmentStore` at startup and gain six commands (`listattachments`, `attachmentinfo`, `uploadattachment`, `downloadattachment`, `deleteattachment`, `totalstorage`) with bytes as base64 inside the existing JSON envelope. `CommandAuthorizer` gains Read entries (list/info/download/totalstorage) and Write entries (upload/delete). The client gets an `Attachments` API; the AdminClient gets a dialog + tile.

**Tech Stack:** .NET 9, xUnit + Moq, Blazor Server + MudBlazor 7 (`MudFileUpload`), System.Text.Json, SHA-256 (already in AttachmentStore).

**Conventions:**
- Test command: `dotnet test AdvGenNoSqlServer.Tests/AdvGenNoSqlServer.Tests.csproj -c Release --filter "FullyQualifiedName~<TestClass>"` from repo root.
- Dispatcher tests use the reflection helper (`typeof(ServerNoSql).GetMethod("HandleMessageAsync", NonPublic|Instance)`) as in `RbacEnforcementTests`/`AdminCommandsTests`.
- Test ports: 19320–19329.
- Standard repo copyright header on new files. Follow @superpowers:test-driven-development.
- **Both dispatchers stay in lockstep** (audit D6): every command edit lands in BOTH `Host/Program.cs` (`NoSqlServerHost`) and `Server/NoSqlServer.cs`. A change to one only will pass the Server-based test suite yet break production, or vice versa.
- **RBAC regression guard:** after Task 2 (authorizer) and Task 4 (commands), run the FULL suite — the new authorizer entries and gating must not disturb `RequireAuthentication=false` tests.

---

## File Structure Overview

| File | Action | Responsibility |
|---|---|---|
| `Core/Configuration/ServerConfiguration.cs` | Modify | `MaxAttachmentSizeMB` (default 25) |
| `Core/Authentication/CommandAuthorizer.cs` | Modify | Six attachment commands in the map |
| `Server/NoSqlServer.cs` | Modify | Construct AttachmentStore; 6 command handlers |
| `Host/Program.cs` | Modify | Construct AttachmentStore; 6 command handlers |
| `Client/AdvGenNoSqlClient.Attachments.cs` | Create | `client.Attachments` API + `AttachmentMetadata` |
| `AdminClient/Services/TcpAdminService.cs` | Modify | Attachment wrappers |
| `AdminClient/Shared/AttachmentsDialog.razor` | Create | Per-document attachments dialog |
| `AdminClient/Pages/Documents.razor` | Modify | Paperclip action opens the dialog |
| `AdminClient/Pages/Index.razor` | Modify | Files tile + FormatBytes helper |
| `AdminClient/wwwroot/js/download.js` + `Pages/_Host.cshtml` | Create/Modify | Browser save-file JS interop |
| Tests: `CommandAuthorizerTests` (extend), `AttachmentCommandsTests`, `AttachmentRbacTests`, `AttachmentClientTests` | Create/Modify | Coverage |

---

### Task 1: Config — MaxAttachmentSizeMB

**Files:**
- Modify: `AdvGenNoSqlServer.Core/Configuration/ServerConfiguration.cs`

- [ ] **Step 1.1: Add property** near `UserStorePath`:

```csharp
    /// <summary>
    /// Maximum size in MB for a single document attachment (default 25).
    /// Kept well under the 100MB protocol frame limit even after base64 encoding.
    /// </summary>
    public int MaxAttachmentSizeMB { get; set; } = 25;
```

- [ ] **Step 1.2: Build Core** — `dotnet build AdvGenNoSqlServer.Core -c Release`, clean.
- [ ] **Step 1.3: Commit** — `git commit -m "feat: add MaxAttachmentSizeMB configuration"`

---

### Task 2: CommandAuthorizer — attachment commands

**Files:**
- Modify: `AdvGenNoSqlServer.Core/Authentication/CommandAuthorizer.cs`
- Test: `AdvGenNoSqlServer.Tests/CommandAuthorizerTests.cs` (extend)

- [ ] **Step 2.1: Add failing matrix rows** to `CommandAuthorizerTests.IsAllowed_MatrixMatchesSpec`:

```csharp
    [InlineData("listattachments", UserRole.ReadOnly, true)]
    [InlineData("downloadattachment", UserRole.ReadOnly, true)]
    [InlineData("attachmentinfo", UserRole.ReadOnly, true)]
    [InlineData("totalstorage", UserRole.ReadOnly, true)]
    [InlineData("uploadattachment", UserRole.ReadOnly, false)]
    [InlineData("deleteattachment", UserRole.ReadOnly, false)]
    [InlineData("uploadattachment", UserRole.ReadWrite, true)]
    [InlineData("deleteattachment", UserRole.ReadWrite, true)]
    [InlineData("uploadattachment", UserRole.Admin, true)]
```

- [ ] **Step 2.2: Run, verify failure** (`unknown → true` makes the readonly-forbidden rows fail).

Run: `dotnet test AdvGenNoSqlServer.Tests/AdvGenNoSqlServer.Tests.csproj -c Release --filter "FullyQualifiedName~CommandAuthorizerTests"`

- [ ] **Step 2.3: Implement** — add to `CommandAuthorizer.Map`:

```csharp
        // Read (attachments)
        ["listattachments"] = CommandAccess.Read,
        ["attachmentinfo"] = CommandAccess.Read,
        ["downloadattachment"] = CommandAccess.Read,
        ["totalstorage"] = CommandAccess.Read,
        // Write (attachments)
        ["uploadattachment"] = CommandAccess.Write,
        ["deleteattachment"] = CommandAccess.Write,
```

- [ ] **Step 2.4: Run tests, verify pass.**
- [ ] **Step 2.5: Commit** — `git commit -m "feat: authorize attachment commands in CommandAuthorizer"`

---

### Task 3: Server dispatcher — AttachmentStore + six commands

**Files:**
- Modify: `AdvGenNoSqlServer.Server/NoSqlServer.cs`
- Test: `AdvGenNoSqlServer.Tests/AttachmentCommandsTests.cs`

- [ ] **Step 3.1: Write failing tests** (`RequireAuthentication=false` so this task tests commands without RBAC noise; RBAC is Task 5)

```csharp
// AdvGenNoSqlServer.Tests/AttachmentCommandsTests.cs
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
        var r = await Send(Upload("huge.bin", "application/octet-stream", new byte[2 * 1024 * 1024])); // > 1MB cap
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
```

- [ ] **Step 3.2: Run, verify failure** (`UNKNOWN_COMMAND`).

- [ ] **Step 3.3: Implement in `NoSqlServer.cs`**

1. Add `using AdvGenNoSqlServer.Core.Attachments;` and `using AdvGenNoSqlServer.Storage.Attachments;`.
2. Field: `private AttachmentStore? _attachmentStore;`.
3. In `StartAsync`, after `_authManager` is created (storagePath already resolved):

```csharp
        var attachmentPath = Path.Combine(storagePath, "attachments");
        _attachmentStore = new AttachmentStore(new AttachmentStoreOptions
        {
            BasePath = attachmentPath,
            MaxAttachmentSize = (long)Math.Max(config.MaxAttachmentSizeMB, 1) * 1024 * 1024
        });
```

4. In the shutdown path (where `_authManager`/stores are cleaned up in `StopAsync`/`DisposeAsync`), add `_attachmentStore?.Dispose(); _attachmentStore = null;` (guard both places, mirroring how the cache/hybrid stores are handled).
5. Add switch arms (after the user-management arms). **The Server `HandleCommandAsync` is NOT async** — it returns the `command switch` expression directly and every existing arm returns a `Task<NoSqlMessage>` un-awaited. So the attachment arms must be un-awaited too:

```csharp
                "listattachments" => HandleListAttachmentsCommand(doc.RootElement),
                "attachmentinfo" => HandleAttachmentInfoCommand(doc.RootElement),
                "uploadattachment" => HandleUploadAttachmentCommand(doc.RootElement),
                "downloadattachment" => HandleDownloadAttachmentCommand(doc.RootElement),
                "deleteattachment" => HandleDeleteAttachmentCommand(doc.RootElement),
                "totalstorage" => HandleTotalStorageCommand(),
```

The handler bodies are still declared `async Task<NoSqlMessage>` (they `await` the store) — they are just returned un-awaited from the switch. This is safe: each handler reads all `JsonElement` values before its first `await`, so the caller's `using var doc` is not disposed mid-read (matching the existing `HandleGetCommand` etc. pattern). **Contrast with Task 4:** the Host's `HandleCommandAsync` IS `async` and its arms ARE awaited — do not paste the Server arm syntax into the Host.

6. Handlers (shared logic — see the "Attachment handler bodies" appendix at the end of this plan; paste them verbatim into `NoSqlServer.cs`, using the field `_attachmentStore` and `_configurationManager.Configuration.MaxAttachmentSizeMB`).

- [ ] **Step 3.4: Run attachment tests, verify pass.**
- [ ] **Step 3.5: Run FULL suite** (regression guard): `dotnet test AdvGenNoSqlServer.Tests/AdvGenNoSqlServer.Tests.csproj -c Release` — green except the known `BackgroundIndexBuilderTests` flake.
- [ ] **Step 3.6: Commit** — `git commit -m "feat: add attachment commands to server dispatcher"`

---

### Task 4: Host dispatcher — AttachmentStore + six commands

**Files:**
- Modify: `AdvGenNoSqlServer.Host/Program.cs`

Mirror Task 3 in `NoSqlServerHost` so production matches.

- [ ] **Step 4.1: Implement**

1. Add `using AdvGenNoSqlServer.Core.Attachments;` (Storage.Attachments too if not present).
2. Field `private AttachmentStore? _attachmentStore;` on `NoSqlServerHost`.
3. In `StartAsync`, resolve the storage path the same way the AuthenticationManager DI does (`config.StoragePath` → absolute via `AppContext.BaseDirectory`) and construct:

```csharp
        var storagePath = string.IsNullOrEmpty(config.StoragePath) ? "data" : config.StoragePath;
        if (!System.IO.Path.IsPathRooted(storagePath))
            storagePath = System.IO.Path.Combine(AppContext.BaseDirectory, storagePath);
        _attachmentStore = new AttachmentStore(new AttachmentStoreOptions
        {
            BasePath = System.IO.Path.Combine(storagePath, "attachments"),
            MaxAttachmentSize = (long)Math.Max(config.MaxAttachmentSizeMB, 1) * 1024 * 1024
        });
```

4. Dispose in `StopAsync`/`DisposeAsync`.
5. Add the six switch arms to the Host's `HandleCommandAsync` (awaited, after the user-management arms).
6. Paste the same six handler bodies (appendix), adapted to the Host's field names (`_attachmentStore`, `_configManager.Configuration.MaxAttachmentSizeMB`).

- [ ] **Step 4.2: Build Host** — `dotnet build AdvGenNoSqlServer.Host -c Release`, 0 errors.
- [ ] **Step 4.3: Commit** — `git commit -m "feat: add attachment commands to Host dispatcher"`

---

### Task 5: RBAC enforcement test for attachments

**Files:**
- Test: `AdvGenNoSqlServer.Tests/AttachmentRbacTests.cs`

- [ ] **Step 5.1: Write tests** (`RequireAuthentication=true`, single connection with re-auth, following `RbacEnforcementTests`)

```csharp
// Authenticate admin, create a readonly user + a readwrite user.
// As readonly: uploadattachment -> FORBIDDEN; listattachments -> not FORBIDDEN (allowed).
// As readwrite: uploadattachment -> succeeds (stored:true).
// Assert on error code "FORBIDDEN" / success as in RbacEnforcementTests.
```

Model this file on `RbacEnforcementTests` (port 19321, `MasterPassword`, reflection `Send`). Full body:

```csharp
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
```

- [ ] **Step 5.2: Run, verify pass** (commands already exist from Tasks 3–4; this proves gating).
- [ ] **Step 5.3: Commit** — `git commit -m "test: cover attachment command RBAC enforcement"`

---

### Task 6: Client Attachments API

**Files:**
- Create: `AdvGenNoSqlServer.Client/AdvGenNoSqlClient.Attachments.cs`
- Test: `AdvGenNoSqlServer.Tests/AttachmentClientTests.cs`

- [ ] **Step 6.1: Write failing end-to-end tests** (`RequireAuthentication=false`, real server + client over plain TCP, port 19322, following `UserClientTests`/`CacheClientTests` startup)

```csharp
// Cover:
//   UploadAsync(bytes) -> metadata (Size == bytes.Length, Hash non-empty)
//   DownloadAsync -> byte-identical to uploaded
//   ListAsync contains the name; InfoAsync returns non-null; total storage > 0
//   DeleteAsync -> true; subsequent DownloadAsync -> null
//   UploadAsync of an application/x-msdownload -> throws NoSqlClientException containing "CONTENT_TYPE_BLOCKED"
```

- [ ] **Step 6.2: Run, verify compile failure.**

- [ ] **Step 6.3: Implement**

```csharp
// AdvGenNoSqlServer.Client/AdvGenNoSqlClient.Attachments.cs
// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
using AdvGenNoSqlServer.Network;

namespace AdvGenNoSqlServer.Client
{
    public record AttachmentMetadata(string Name, string ContentType, long Size, string Hash, DateTime CreatedAt, DateTime UpdatedAt);

    public partial class AdvGenNoSqlClient
    {
        private AttachmentOperations? _attachments;
        public AttachmentOperations Attachments => _attachments ??= new AttachmentOperations(this);

        public sealed class AttachmentOperations
        {
            private readonly AdvGenNoSqlClient _client;
            internal AttachmentOperations(AdvGenNoSqlClient client) => _client = client;

            private async Task<NoSqlResponse> SendAsync(object payload, CancellationToken ct)
            {
                _client.EnsureConnected();
                var msg = NoSqlMessage.Create(MessageType.Command, System.Text.Json.JsonSerializer.Serialize(payload));
                var response = await _client.SendAndReceiveAsync(msg, ct);
                var result = _client.ParseResponse(response);
                if (!result.Success)
                    throw new NoSqlClientException($"{result.Error?.Code}: {result.Error?.Message}");
                return result;
            }

            private static AttachmentMetadata ReadMeta(System.Text.Json.JsonElement e)
                => new(
                    e.GetProperty("name").GetString() ?? "",
                    e.GetProperty("contentType").GetString() ?? "",
                    e.GetProperty("size").GetInt64(),
                    e.TryGetProperty("hash", out var h) ? h.GetString() ?? "" : "",
                    e.TryGetProperty("createdAt", out var c) ? c.GetDateTime() : default,
                    e.TryGetProperty("updatedAt", out var u) ? u.GetDateTime() : default);

            public async Task<IReadOnlyList<AttachmentMetadata>> ListAsync(string collection, string id, CancellationToken ct = default)
            {
                var resp = await SendAsync(new { command = "listattachments", collection, id }, ct);
                var list = new List<AttachmentMetadata>();
                if (resp.Data is System.Text.Json.JsonElement d && d.TryGetProperty("attachments", out var arr))
                    foreach (var a in arr.EnumerateArray()) list.Add(ReadMeta(a));
                return list;
            }

            public async Task<AttachmentMetadata?> InfoAsync(string collection, string id, string name, CancellationToken ct = default)
            {
                var resp = await SendAsync(new { command = "attachmentinfo", collection, id, name }, ct);
                if (resp.Data is System.Text.Json.JsonElement d && d.TryGetProperty("found", out var f) && f.GetBoolean())
                    return ReadMeta(d.GetProperty("info"));
                return null;
            }

            public async Task<AttachmentMetadata> UploadAsync(string collection, string id, string name, string contentType, byte[] content, CancellationToken ct = default)
            {
                var resp = await SendAsync(new
                {
                    command = "uploadattachment", collection, id, name,
                    contentType, contentBase64 = Convert.ToBase64String(content)
                }, ct);
                var d = (System.Text.Json.JsonElement)resp.Data!;
                return new AttachmentMetadata(
                    d.GetProperty("name").GetString() ?? name, contentType,
                    d.GetProperty("size").GetInt64(),
                    d.TryGetProperty("hash", out var h) ? h.GetString() ?? "" : "",
                    default, default);
            }

            public async Task<byte[]?> DownloadAsync(string collection, string id, string name, CancellationToken ct = default)
            {
                var resp = await SendAsync(new { command = "downloadattachment", collection, id, name }, ct);
                if (resp.Data is System.Text.Json.JsonElement d && d.TryGetProperty("found", out var f) && f.GetBoolean())
                    return Convert.FromBase64String(d.GetProperty("contentBase64").GetString() ?? "");
                return null;
            }

            public async Task<bool> DeleteAsync(string collection, string id, string name, CancellationToken ct = default)
            {
                var resp = await SendAsync(new { command = "deleteattachment", collection, id, name }, ct);
                return resp.Data is System.Text.Json.JsonElement d && d.TryGetProperty("deleted", out var del) && del.GetBoolean();
            }

            public async Task<long> TotalStorageBytesAsync(CancellationToken ct = default)
            {
                var resp = await SendAsync(new { command = "totalstorage" }, ct);
                return resp.Data is System.Text.Json.JsonElement d && d.TryGetProperty("bytes", out var b) ? b.GetInt64() : 0;
            }
        }
    }
}
```

Note: `EnsureConnected`, `SendAndReceiveAsync`, `ParseResponse`, `NoSqlResponse`, `NoSqlClientException` are all accessible to the nested class (private members of the enclosing partial class are visible to nested types).

- [ ] **Step 6.4: Run client tests, verify pass.**
- [ ] **Step 6.5: Commit** — `git commit -m "feat: add client Attachments API"`

---

### Task 7: TcpAdminService wrappers

**Files:**
- Modify: `AdvGenNoSqlServer.AdminClient/Services/TcpAdminService.cs`

- [ ] **Step 7.1: Add wrappers** (before `EnsureConnected`):

```csharp
    public async Task<List<AttachmentMetadata>> ListAttachmentsAsync(string collection, string id)
    { EnsureConnected(); return (await _client!.Attachments.ListAsync(collection, id)).ToList(); }

    public Task<AttachmentMetadata> UploadAttachmentAsync(string collection, string id, string name, string contentType, byte[] content)
    { EnsureConnected(); return _client!.Attachments.UploadAsync(collection, id, name, contentType, content); }

    public Task<byte[]?> DownloadAttachmentAsync(string collection, string id, string name)
    { EnsureConnected(); return _client!.Attachments.DownloadAsync(collection, id, name); }

    public Task<bool> DeleteAttachmentAsync(string collection, string id, string name)
    { EnsureConnected(); return _client!.Attachments.DeleteAsync(collection, id, name); }

    public Task<long> GetTotalAttachmentStorageAsync()
    { EnsureConnected(); return _client!.Attachments.TotalStorageBytesAsync(); }
```

(`AttachmentMetadata` resolves via the existing `using AdvGenNoSqlServer.Client;`.)

- [ ] **Step 7.2: Build AdminClient**, clean.
- [ ] **Step 7.3: Commit** — `git commit -m "feat: add attachment wrappers to TcpAdminService"`

---

### Task 8: Admin UI — attachments dialog, Documents paperclip, Dashboard tile, download JS

**Files:**
- Create: `AdvGenNoSqlServer.AdminClient/wwwroot/js/download.js`
- Modify: `AdvGenNoSqlServer.AdminClient/Pages/_Host.cshtml` (reference the script)
- Create: `AdvGenNoSqlServer.AdminClient/Shared/AttachmentsDialog.razor`
- Modify: `AdvGenNoSqlServer.AdminClient/Pages/Documents.razor` (paperclip action)
- Modify: `AdvGenNoSqlServer.AdminClient/Pages/Index.razor` (Files tile + FormatBytes)

No AdminClient test project; verified by driving the app in Task 9.

- [ ] **Step 8.1: Download JS interop** — create `wwwroot/js/download.js`:

```javascript
window.saveAsFile = function (fileName, contentType, base64) {
    const bytes = atob(base64);
    const arr = new Uint8Array(bytes.length);
    for (let i = 0; i < bytes.length; i++) arr[i] = bytes.charCodeAt(i);
    const blob = new Blob([arr], { type: contentType || 'application/octet-stream' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url; a.download = fileName;
    document.body.appendChild(a); a.click();
    document.body.removeChild(a); URL.revokeObjectURL(url);
};
```

Reference it in `Pages/_Host.cshtml` after the Blazor script:

```html
<script src="js/download.js"></script>
```

(Confirm the existing `_Host.cshtml` script block location; add the line alongside the MudBlazor/blazor script includes.)

- [ ] **Step 8.2: FormatBytes + Files tile** in `Index.razor`:

Add the helper to `@code`:

```csharp
    private long? _attachmentBytes;

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F1} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }
```

In `LoadStatsAsync` (after stats load), fetch total storage, failing soft:

```csharp
        try { _attachmentBytes = await AdminService.GetTotalAttachmentStorageAsync(); }
        catch { _attachmentBytes = null; }
```

Add a tile in the `<MudGrid>` (mirror the Memory Usage tile):

```razor
        <MudItem xs="12" sm="6" md="4">
            <MudPaper Elevation="2" Class="pa-4">
                <MudText Typo="Typo.subtitle2" Color="Color.Secondary">Files (attachments)</MudText>
                <MudText Typo="Typo.h5">@(_attachmentBytes.HasValue ? FormatBytes(_attachmentBytes.Value) : "—")</MudText>
            </MudPaper>
        </MudItem>
```

- [ ] **Step 8.3: AttachmentsDialog** — create `Shared/AttachmentsDialog.razor`:

```razor
@inject TcpAdminService AdminService
@inject ISnackbar Snackbar
@inject IDialogService DialogService
@inject IJSRuntime JS

<MudDialog>
    <DialogContent>
        <MudText Typo="Typo.subtitle1" Class="mb-2">Attachments for @DocumentId</MudText>

        @if (_loading)
        {
            <MudProgressCircular Indeterminate="true" />
        }
        else
        {
            <MudTable Items="_items" Dense="true" Hover="true">
                <HeaderContent>
                    <MudTh>Name</MudTh><MudTh>Type</MudTh><MudTh>Size</MudTh><MudTh>Hash</MudTh><MudTh>Actions</MudTh>
                </HeaderContent>
                <RowTemplate>
                    <MudTd>@context.Name</MudTd>
                    <MudTd>@context.ContentType</MudTd>
                    <MudTd>@FormatBytes(context.Size)</MudTd>
                    <MudTd><code>@(context.Hash.Length > 12 ? context.Hash[..12] : context.Hash)</code></MudTd>
                    <MudTd>
                        <MudIconButton Icon="@Icons.Material.Filled.Download" Size="Size.Small"
                                       OnClick="@(() => Download(context))" title="Download" />
                        @if (AdminService.CurrentRole != "readonly")
                        {
                            <MudIconButton Icon="@Icons.Material.Filled.Delete" Color="Color.Error" Size="Size.Small"
                                           OnClick="@(() => Delete(context))" title="Delete" />
                        }
                    </MudTd>
                </RowTemplate>
                <NoRecordsContent><MudText>No attachments.</MudText></NoRecordsContent>
            </MudTable>

            @if (AdminService.CurrentRole != "readonly")
            {
                <MudFileUpload T="IBrowserFile" FilesChanged="Upload" Class="mt-3">
                    <ActivatorContent>
                        <MudButton Variant="Variant.Filled" Color="Color.Primary" StartIcon="@Icons.Material.Filled.Upload">
                            Upload file
                        </MudButton>
                    </ActivatorContent>
                </MudFileUpload>
            }
        }
        @if (_error != null) { <MudAlert Severity="Severity.Error" Class="mt-2">@_error</MudAlert> }
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="@(() => MudDialog.Close())">Close</MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] private MudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public string Collection { get; set; } = "";
    [Parameter] public string DocumentId { get; set; } = "";

    private const long MaxBytes = 25L * 1024 * 1024;
    private List<AttachmentMetadata> _items = new();
    private bool _loading = true;
    private string? _error;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _loading = true; _error = null;
        try { _items = await AdminService.ListAttachmentsAsync(Collection, DocumentId); }
        catch (Exception ex) { _error = ex.Message; }
        finally { _loading = false; }
    }

    private async Task Upload(IBrowserFile? file)
    {
        if (file == null) return;
        if (file.Size > MaxBytes) { Snackbar.Add($"File exceeds {FormatBytes(MaxBytes)} limit", Severity.Warning); return; }
        try
        {
            using var ms = new MemoryStream();
            await file.OpenReadStream(MaxBytes).CopyToAsync(ms);
            var ct = string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType;
            await AdminService.UploadAttachmentAsync(Collection, DocumentId, file.Name, ct, ms.ToArray());
            Snackbar.Add($"Uploaded {file.Name}", Severity.Success);
            await LoadAsync();
        }
        catch (Exception ex) { Snackbar.Add(ex.Message, Severity.Error); }
    }

    private async Task Download(AttachmentMetadata a)
    {
        try
        {
            var bytes = await AdminService.DownloadAttachmentAsync(Collection, DocumentId, a.Name);
            if (bytes == null) { Snackbar.Add("Attachment no longer exists", Severity.Warning); return; }
            await JS.InvokeVoidAsync("saveAsFile", a.Name, a.ContentType, Convert.ToBase64String(bytes));
        }
        catch (Exception ex) { Snackbar.Add(ex.Message, Severity.Error); }
    }

    private async Task Delete(AttachmentMetadata a)
    {
        var parameters = new DialogParameters<ConfirmDialog>
        {
            { x => x.Message, $"Delete attachment '{a.Name}'?" },
            { x => x.ConfirmText, "Delete" }
        };
        var dlg = await DialogService.ShowAsync<ConfirmDialog>("Delete Attachment", parameters);
        var res = await dlg.Result;
        if (res?.Canceled == false)
        {
            try { await AdminService.DeleteAttachmentAsync(Collection, DocumentId, a.Name); Snackbar.Add("Deleted", Severity.Success); await LoadAsync(); }
            catch (Exception ex) { Snackbar.Add(ex.Message, Severity.Error); }
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F1} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }
}
```

Confirm `_Imports.razor` includes `Microsoft.AspNetCore.Components.Forms` (for `IBrowserFile`); add `@using Microsoft.AspNetCore.Components.Forms` to the dialog if not globally imported.

- [ ] **Step 8.4: Paperclip action in `Documents.razor`** — add an icon button in the Actions cell:

```razor
                <MudIconButton Icon="@Icons.Material.Filled.AttachFile" Size="Size.Small"
                               OnClick="@(() => OpenAttachments(context))" Title="Attachments" />
```

And the handler in `@code`:

```csharp
    private async Task OpenAttachments(Dictionary<string, object> doc)
    {
        var id = GetId(doc);
        if (string.IsNullOrEmpty(id)) { Snackbar.Add("Document has no _id", Severity.Warning); return; }
        var parameters = new DialogParameters<AttachmentsDialog>
        {
            { x => x.Collection, _collection },
            { x => x.DocumentId, id }
        };
        await DialogService.ShowAsync<AttachmentsDialog>("Attachments", parameters,
            new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true });
    }
```

- [ ] **Step 8.5: Build AdminClient**, `dotnet build AdvGenNoSqlServer.AdminClient -c Release`, 0 errors.
- [ ] **Step 8.6: Commit** — `git commit -m "feat: add attachments dialog, Documents paperclip and Files dashboard tile"`

---

### Task 9: E2E verification + docs + merge

- [ ] **Step 9.1: Full test suite** — `dotnet test AdvGenNoSqlServer.Tests/AdvGenNoSqlServer.Tests.csproj -c Release`. Green except the known flake.

- [ ] **Step 9.2: Live E2E over the Host (TCP+SSL)** — scratchpad console app referencing the Client (like the RBAC E2E). With the Host's default appsettings (`RequireAuthentication: true`, `MasterPassword: admin123`, SSL on):
  1. Authenticate `admin`. Upload a known byte array to `(col, docId)`, assert returned size matches and `ListAsync` shows it.
  2. `DownloadAsync`, assert `SHA256(downloaded) == SHA256(original)`.
  3. `TotalStorageBytesAsync` > 0.
  4. `DeleteAsync` → true; `DownloadAsync` → null.
  5. Create a `readonly` user; reconnect as it; `DownloadAsync` of a pre-uploaded file works, `UploadAsync` throws mentioning FORBIDDEN.
  6. Cleanup.

  Start the Host, run the harness, assert all pass, stop the Host.

- [ ] **Step 9.3: Verify AdminClient serves** — start it, `curl -k https://localhost:7210/login` → 200. Stop it.

- [ ] **Step 9.4: Docs** — add a bullet to root `README.md` (attachments over TCP + Admin UI) and a line to `AdvGenNoSqlServer.AdminClient/README.md` (paperclip on Documents rows, Files tile). Use @superpowers:verification-before-completion.

- [ ] **Step 9.5: Commit docs**, then @superpowers:requesting-code-review, then @superpowers:finishing-a-development-branch (merge to master, delete branch).

---

## Appendix: Attachment handler bodies (paste into BOTH dispatchers)

Server dispatcher uses field `_attachmentStore` and `_configurationManager.Configuration.MaxAttachmentSizeMB`. Host uses `_attachmentStore` and `_configManager.Configuration.MaxAttachmentSizeMB`. Response builders (`NoSqlMessage.CreateSuccess`/`CreateError`) are identical in both.

```csharp
    private async Task<NoSqlMessage> HandleListAttachmentsCommand(System.Text.Json.JsonElement e)
    {
        if (_attachmentStore == null) return NoSqlMessage.CreateError("NOT_INITIALIZED", "Attachments not initialized");
        if (!e.TryGetProperty("collection", out var col) || !e.TryGetProperty("id", out var id) ||
            string.IsNullOrEmpty(col.GetString()) || string.IsNullOrEmpty(id.GetString()))
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection or id");
        var list = await _attachmentStore.ListAsync(col.GetString()!, id.GetString()!);
        return NoSqlMessage.CreateSuccess(new
        {
            attachments = list.Select(a => new { name = a.Name, contentType = a.ContentType, size = a.Size, hash = a.Hash, createdAt = a.CreatedAt, updatedAt = a.UpdatedAt })
        });
    }

    private async Task<NoSqlMessage> HandleAttachmentInfoCommand(System.Text.Json.JsonElement e)
    {
        if (_attachmentStore == null) return NoSqlMessage.CreateError("NOT_INITIALIZED", "Attachments not initialized");
        if (!e.TryGetProperty("collection", out var col) || !e.TryGetProperty("id", out var id) || !e.TryGetProperty("name", out var nm) ||
            string.IsNullOrEmpty(col.GetString()) || string.IsNullOrEmpty(id.GetString()) || string.IsNullOrEmpty(nm.GetString()))
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection, id or name");
        var info = await _attachmentStore.GetInfoAsync(col.GetString()!, id.GetString()!, nm.GetString()!);
        if (info == null) return NoSqlMessage.CreateSuccess(new { found = false, info = (object?)null });
        return NoSqlMessage.CreateSuccess(new { found = true, info = new { name = info.Name, contentType = info.ContentType, size = info.Size, hash = info.Hash, createdAt = info.CreatedAt, updatedAt = info.UpdatedAt } });
    }

    private async Task<NoSqlMessage> HandleUploadAttachmentCommand(System.Text.Json.JsonElement e)
    {
        if (_attachmentStore == null) return NoSqlMessage.CreateError("NOT_INITIALIZED", "Attachments not initialized");
        var collection = e.TryGetProperty("collection", out var col) ? col.GetString() : null;
        var id = e.TryGetProperty("id", out var idp) ? idp.GetString() : null;
        var name = e.TryGetProperty("name", out var nm) ? nm.GetString() : null;
        var contentType = e.TryGetProperty("contentType", out var ct) ? ct.GetString() : null;
        var b64 = e.TryGetProperty("contentBase64", out var cb) ? cb.GetString() : null;
        if (string.IsNullOrEmpty(collection) || string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name) || b64 == null)
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection, id, name or contentBase64");
        if (name.Length > 255)
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Attachment name too long (max 255)");
        if (string.IsNullOrEmpty(contentType)) contentType = "application/octet-stream";

        byte[] content;
        try { content = Convert.FromBase64String(b64); }
        catch (FormatException) { return NoSqlMessage.CreateError("INVALID_CONTENT", "contentBase64 is not valid base64"); }

        long maxBytes = (long)Math.Max(MAX_ATTACHMENT_MB, 1) * 1024 * 1024;   // MAX_ATTACHMENT_MB = config value
        if (content.Length > maxBytes)
            return NoSqlMessage.CreateError("ATTACHMENT_TOO_LARGE", $"Attachment exceeds {MAX_ATTACHMENT_MB} MB limit");

        var result = await _attachmentStore.StoreAsync(collection, id, name, contentType, content);
        if (!result.Success)
        {
            var msg = result.ErrorMessage ?? "Upload failed";
            if (msg.Contains("not allowed")) return NoSqlMessage.CreateError("CONTENT_TYPE_BLOCKED", msg);
            return NoSqlMessage.CreateError("COMMAND_ERROR", msg);
        }
        return NoSqlMessage.CreateSuccess(new { stored = true, name = result.Info!.Name, hash = result.Info.Hash, size = result.Info.Size });
    }

    private async Task<NoSqlMessage> HandleDownloadAttachmentCommand(System.Text.Json.JsonElement e)
    {
        if (_attachmentStore == null) return NoSqlMessage.CreateError("NOT_INITIALIZED", "Attachments not initialized");
        if (!e.TryGetProperty("collection", out var col) || !e.TryGetProperty("id", out var id) || !e.TryGetProperty("name", out var nm) ||
            string.IsNullOrEmpty(col.GetString()) || string.IsNullOrEmpty(id.GetString()) || string.IsNullOrEmpty(nm.GetString()))
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection, id or name");
        var att = await _attachmentStore.GetAsync(col.GetString()!, id.GetString()!, nm.GetString()!);
        if (att == null) return NoSqlMessage.CreateSuccess(new { found = false });
        return NoSqlMessage.CreateSuccess(new { found = true, name = att.Name, contentType = att.ContentType, size = att.Size, contentBase64 = Convert.ToBase64String(att.Content) });
    }

    private async Task<NoSqlMessage> HandleDeleteAttachmentCommand(System.Text.Json.JsonElement e)
    {
        if (_attachmentStore == null) return NoSqlMessage.CreateError("NOT_INITIALIZED", "Attachments not initialized");
        if (!e.TryGetProperty("collection", out var col) || !e.TryGetProperty("id", out var id) || !e.TryGetProperty("name", out var nm) ||
            string.IsNullOrEmpty(col.GetString()) || string.IsNullOrEmpty(id.GetString()) || string.IsNullOrEmpty(nm.GetString()))
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection, id or name");
        var deleted = await _attachmentStore.DeleteAsync(col.GetString()!, id.GetString()!, nm.GetString()!);
        return NoSqlMessage.CreateSuccess(new { deleted });
    }

    private async Task<NoSqlMessage> HandleTotalStorageCommand()
    {
        if (_attachmentStore == null) return NoSqlMessage.CreateError("NOT_INITIALIZED", "Attachments not initialized");
        var bytes = await _attachmentStore.GetTotalStorageSizeAsync();
        return NoSqlMessage.CreateSuccess(new { bytes });
    }
```

Replace `MAX_ATTACHMENT_MB` with the dispatcher's config accessor: Server `_configurationManager.Configuration.MaxAttachmentSizeMB`, Host `_configManager.Configuration.MaxAttachmentSizeMB`.

## Execution notes

- Task order 1→9. Tasks 3 and 4 must both land before Task 5 (RBAC test) and Task 6 (client) are meaningful end to end, but each is independently committable.
- Ports 19320–19322 for new test classes.
- The two dispatchers are duplicated (audit D6); every command/handler edit goes in both. The Server dispatcher is what the test suite exercises; the Host is production — the live E2E in Task 9 is the only thing that exercises the Host path, so do not skip it.
