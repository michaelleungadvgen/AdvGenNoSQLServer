# Admin UI Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every existing AdminClient page actually work against the real server, per the audit in `docs/superpowers/specs/2026-07-14-admin-ui-audit-design.md` (read it first — it contains the evidence and fix rationale).

**Architecture:** Three missing TCP command handlers are added to `NoSqlServer` (`createcollection`, `dropcollection`, `listdocuments`) matching the wire shapes `TcpAdminService` already sends. The `get` contract is fixed by having the server return a flat `document` property. `NoSqlMessage.CreateCommand` switches from string concatenation to real JSON serialization. The Query page becomes a JSON Command Console; JSON dialogs become multiline.

**Tech Stack:** .NET 9, xUnit + Moq (existing patterns), Blazor Server + MudBlazor 7.

**Conventions:**
- Test command: `dotnet test AdvGenNoSqlServer.Tests/AdvGenNoSqlServer.Tests.csproj -c Release --filter "FullyQualifiedName~<TestClass>"` from repo root.
- Server handler tests invoke the private `HandleMessageAsync` via the same reflection helper `ClusterCommandTests.InvokeHandleCommandAsync` uses (see that file, ~line 663) — copy the helper into new test classes.
- Standard repo copyright header on new files.
- Follow @superpowers:test-driven-development.
- Independent of the cache plans; can be executed before or after them. (If the cache plans land first, `NoSqlServer.StartAsync` looks different — the changes here don't touch it.)

---

## File Structure Overview

| File | Action | Responsibility |
|---|---|---|
| `AdvGenNoSqlServer.Server/NoSqlServer.cs` | Modify | 3 new command handlers; `get` returns flat `document` |
| `AdvGenNoSqlServer.Network/MessageProtocol.cs` | Modify | `CreateCommand` proper JSON serialization |
| `AdvGenNoSqlServer.Client/AdvGenNoSqlClient.Commands.cs` | No change needed | `GetAsync` already reads `document` — becomes correct once server sends it |
| `AdvGenNoSqlServer.AdminClient/Shared/TextInputDialog.razor` | Modify | `Lines` parameter |
| `AdvGenNoSqlServer.AdminClient/Pages/Documents.razor` | Modify | Pass `Lines=12` to JSON dialogs |
| `AdvGenNoSqlServer.AdminClient/Pages/Query.razor` | Rewrite | JSON Command Console |
| Tests: `AdminCommandsTests.cs` (new), `ClientGetFixTests.cs` (replace), `MessageProtocolCreateCommandTests.cs` (new) | Create/Modify | Coverage per component |

---

### Task 1: Server — `createcollection` and `dropcollection` commands

**Files:**
- Modify: `AdvGenNoSqlServer.Server/NoSqlServer.cs` (command switch ~line 286; new handlers near `HandleListCollectionsCommand` ~line 813)
- Test: `AdvGenNoSqlServer.Tests/AdminCommandsTests.cs` (new)

- [ ] **Step 1.1: Write failing tests**

```csharp
// AdvGenNoSqlServer.Tests/AdminCommandsTests.cs
using AdvGenNoSqlServer.Core.Configuration;
using AdvGenNoSqlServer.Network;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;
using ServerNoSql = AdvGenNoSqlServer.Server.NoSqlServer;

namespace AdvGenNoSqlServer.Tests;

public class AdminCommandsTests : IAsyncLifetime
{
    private ServerNoSql _server = null!;
    private string _storagePath = null!;

    public async Task InitializeAsync()
    {
        _storagePath = Path.Combine(Path.GetTempPath(), "advgen-admincmd-test-" + Guid.NewGuid().ToString("N"));
        var config = new ServerConfiguration
        {
            Host = "127.0.0.1", Port = 19301,
            StoragePath = _storagePath,
            RequireAuthentication = false
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
        if (Directory.Exists(_storagePath)) Directory.Delete(_storagePath, recursive: true);
    }

    // Copy the reflection helper from ClusterCommandTests (~line 663) verbatim:
    private Task<NoSqlMessage> SendCommandAsync(string json) { /* invoke private HandleMessageAsync via reflection */ }

    private static JsonElement Data(NoSqlMessage response)
        => JsonDocument.Parse(response.GetPayloadAsString()).RootElement.GetProperty("data");

    [Fact]
    public async Task CreateCollection_NewName_ReturnsCreatedTrue()
    {
        var response = await SendCommandAsync("""{"command":"createcollection","collection":"newcol"}""");
        Assert.Equal(MessageType.Response, response.MessageType);
        Assert.True(Data(response).GetProperty("created").GetBoolean());

        // Collection is now listed
        var list = await SendCommandAsync("""{"command":"listcollections"}""");
        Assert.Contains("newcol", Data(list).GetProperty("collections").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task CreateCollection_ExistingName_ReturnsCreatedFalse()
    {
        await SendCommandAsync("""{"command":"createcollection","collection":"dupe"}""");
        var response = await SendCommandAsync("""{"command":"createcollection","collection":"dupe"}""");
        Assert.False(Data(response).GetProperty("created").GetBoolean());
    }

    [Fact]
    public async Task CreateCollection_MissingCollection_ReturnsError()
    {
        var response = await SendCommandAsync("""{"command":"createcollection"}""");
        Assert.Equal(MessageType.Error, response.MessageType);
    }

    [Fact]
    public async Task DropCollection_Existing_ReturnsDroppedTrue_AndRemoves()
    {
        await SendCommandAsync("""{"command":"createcollection","collection":"togo"}""");
        var response = await SendCommandAsync("""{"command":"dropcollection","collection":"togo"}""");
        Assert.True(Data(response).GetProperty("dropped").GetBoolean());

        var list = await SendCommandAsync("""{"command":"listcollections"}""");
        Assert.DoesNotContain("togo", Data(list).GetProperty("collections").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task DropCollection_Missing_ReturnsDroppedFalse()
    {
        var response = await SendCommandAsync("""{"command":"dropcollection","collection":"never-existed"}""");
        Assert.False(Data(response).GetProperty("dropped").GetBoolean());
    }
}
```

(Port 19301; the cache plans use 19291–19299.)

- [ ] **Step 1.2: Run, verify failure** — `UNKNOWN_COMMAND` errors.

Run: `dotnet test AdvGenNoSqlServer.Tests/AdvGenNoSqlServer.Tests.csproj -c Release --filter "FullyQualifiedName~AdminCommandsTests"`

- [ ] **Step 1.3: Implement**

Add to the command switch in `HandleCommandAsync` (after `"count"`):

```csharp
                "createcollection" => HandleCreateCollectionCommand(doc.RootElement),
                "dropcollection" => HandleDropCollectionCommand(doc.RootElement),
```

New handlers (place after `HandleCountCommand`, mirroring its style):

```csharp
    private async Task<NoSqlMessage> HandleCreateCollectionCommand(JsonElement commandElement)
    {
        if (_documentStore == null)
            return NoSqlMessage.CreateError("STORAGE_ERROR", "Storage not initialized");

        if (!commandElement.TryGetProperty("collection", out var collectionProp) ||
            string.IsNullOrEmpty(collectionProp.GetString()))
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection property");

        var collection = collectionProp.GetString()!;
        try
        {
            var existing = await _documentStore.GetCollectionsAsync();
            bool created = !existing.Contains(collection);
            if (created)
                await _documentStore.CreateCollectionAsync(collection);
            return NoSqlMessage.CreateSuccess(new { created, collection });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating collection {Collection}", collection);
            return NoSqlMessage.CreateError("STORAGE_ERROR", $"Failed to create collection: {ex.Message}");
        }
    }

    private async Task<NoSqlMessage> HandleDropCollectionCommand(JsonElement commandElement)
    {
        if (_documentStore == null)
            return NoSqlMessage.CreateError("STORAGE_ERROR", "Storage not initialized");

        if (!commandElement.TryGetProperty("collection", out var collectionProp) ||
            string.IsNullOrEmpty(collectionProp.GetString()))
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection property");

        var collection = collectionProp.GetString()!;
        try
        {
            var dropped = await _documentStore.DropCollectionAsync(collection);
            return NoSqlMessage.CreateSuccess(new { dropped, collection });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error dropping collection {Collection}", collection);
            return NoSqlMessage.CreateError("STORAGE_ERROR", $"Failed to drop collection: {ex.Message}");
        }
    }
```

Note: check `IDocumentStore.CreateCollectionAsync`'s behavior on duplicates (`AdvGenNoSqlServer.Storage/DocumentStore.cs` / `HybridDocumentStore.cs`) — if it throws on existing, keep the `created` pre-check as the guard; if idempotent, the pre-check still yields the correct flag.

- [ ] **Step 1.4: Run tests, verify pass**

- [ ] **Step 1.5: Commit**

```bash
git add AdvGenNoSqlServer.Server/NoSqlServer.cs AdvGenNoSqlServer.Tests/AdminCommandsTests.cs
git commit -m "feat: add createcollection and dropcollection TCP commands"
```

---

### Task 2: Server — `listdocuments` command (paged, flattened)

**Files:**
- Modify: `AdvGenNoSqlServer.Server/NoSqlServer.cs`
- Test: `AdvGenNoSqlServer.Tests/AdminCommandsTests.cs` (append)

- [ ] **Step 2.1: Write failing tests** (append to `AdminCommandsTests`)

```csharp
    [Fact]
    public async Task ListDocuments_ReturnsPagedFlattenedDocumentsAndTotal()
    {
        for (int i = 0; i < 12; i++)
            await SendCommandAsync($$"""{"command":"set","collection":"pagecol","document":{"_id":"doc{{i:D2}}","n":{{i}}}}""");

        var response = await SendCommandAsync(
            """{"command":"listdocuments","collection":"pagecol","document":{"skip":10,"take":5}}""");
        var data = Data(response);

        Assert.Equal(12, data.GetProperty("total").GetInt64());
        var docs = data.GetProperty("documents").EnumerateArray().ToList();
        Assert.Equal(2, docs.Count);                       // 12 total, skip 10
        Assert.True(docs[0].TryGetProperty("_id", out _)); // flattened shape
        Assert.True(docs[0].TryGetProperty("n", out _));   // data fields at top level
    }

    [Fact]
    public async Task ListDocuments_DefaultsAndEmptyCollection()
    {
        var response = await SendCommandAsync("""{"command":"listdocuments","collection":"emptycol"}""");
        var data = Data(response);
        Assert.Equal(0, data.GetProperty("total").GetInt64());
        Assert.Empty(data.GetProperty("documents").EnumerateArray());
    }

    [Fact]
    public async Task ListDocuments_MissingCollection_ReturnsError()
    {
        var response = await SendCommandAsync("""{"command":"listdocuments"}""");
        Assert.Equal(MessageType.Error, response.MessageType);
    }

    [Fact]
    public async Task ListDocuments_TakeIsCappedAt500()
    {
        var response = await SendCommandAsync(
            """{"command":"listdocuments","collection":"emptycol","document":{"skip":0,"take":99999}}""");
        Assert.Equal(MessageType.Response, response.MessageType); // capped, not rejected
    }
```

- [ ] **Step 2.2: Run, verify failure**

- [ ] **Step 2.3: Implement**

Switch entry: `"listdocuments" => HandleListDocumentsCommand(doc.RootElement),`

```csharp
    private async Task<NoSqlMessage> HandleListDocumentsCommand(JsonElement commandElement)
    {
        if (_documentStore == null)
            return NoSqlMessage.CreateError("STORAGE_ERROR", "Storage not initialized");

        if (!commandElement.TryGetProperty("collection", out var collectionProp) ||
            string.IsNullOrEmpty(collectionProp.GetString()))
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection property");

        var collection = collectionProp.GetString()!;
        int skip = 0, take = 50;
        if (commandElement.TryGetProperty("document", out var optionsProp) &&
            optionsProp.ValueKind == JsonValueKind.Object)
        {
            if (optionsProp.TryGetProperty("skip", out var skipProp)) skip = Math.Max(skipProp.GetInt32(), 0);
            if (optionsProp.TryGetProperty("take", out var takeProp)) take = Math.Clamp(takeProp.GetInt32(), 1, 500);
        }

        try
        {
            var total = await _documentStore.CountAsync(collection);
            var all = await _documentStore.GetAllAsync(collection);
            var page = all
                .OrderBy(d => d.Id, StringComparer.Ordinal)
                .Skip(skip).Take(take)
                .Select(d =>
                {
                    var flat = new Dictionary<string, object?>(d.Data.Count + 1) { ["_id"] = d.Id };
                    foreach (var kv in d.Data) flat[kv.Key] = kv.Value;
                    return flat;
                })
                .ToList();

            return NoSqlMessage.CreateSuccess(new { documents = page, total });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing documents in {Collection}", collection);
            return NoSqlMessage.CreateError("STORAGE_ERROR", $"Failed to list documents: {ex.Message}");
        }
    }
```

- [ ] **Step 2.4: Run tests, verify pass**

- [ ] **Step 2.5: Commit** — `git commit -m "feat: add listdocuments TCP command with paging"`

---

### Task 3: Fix the `get` contract (server sends flat `document`)

**Files:**
- Modify: `AdvGenNoSqlServer.Server/NoSqlServer.cs` (`HandleGetCommand`, ~line 311-339)
- Modify: `AdvGenNoSqlServer.Tests/ClientGetFixTests.cs` (replace simulation tests with real round-trip)

- [ ] **Step 3.1: Write failing round-trip test** (replace the file's contents; keep the class name)

```csharp
// AdvGenNoSqlServer.Tests/ClientGetFixTests.cs  (replaces the JSON-simulation tests,
// which asserted a contract the real server never honored — see audit D2)
using AdvGenNoSqlServer.Core.Configuration;
using AdvGenNoSqlServer.Network;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;
using ServerNoSql = AdvGenNoSqlServer.Server.NoSqlServer;

namespace AdvGenNoSqlServer.Tests;

public class ClientGetFixTests : IAsyncLifetime
{
    // Same InitializeAsync/DisposeAsync/reflection-helper scaffolding as AdminCommandsTests, port 19302.

    [Fact]
    public async Task GetCommand_ReturnsFlatDocumentProperty()
    {
        await SendCommandAsync("""{"command":"set","collection":"c","document":{"_id":"abc","name":"test"}}""");
        var response = await SendCommandAsync("""{"command":"get","collection":"c","id":"abc"}""");

        var data = JsonDocument.Parse(response.GetPayloadAsString()).RootElement.GetProperty("data");
        Assert.True(data.GetProperty("found").GetBoolean());
        var document = data.GetProperty("document");             // the property the client reads
        Assert.Equal("abc", document.GetProperty("_id").GetString());
        Assert.Equal("test", document.GetProperty("name").GetString());
    }

    [Fact]
    public async Task GetCommand_Missing_ReturnsFoundFalseNullDocument()
    {
        var response = await SendCommandAsync("""{"command":"get","collection":"c","id":"nope"}""");
        var data = JsonDocument.Parse(response.GetPayloadAsString()).RootElement.GetProperty("data");
        Assert.False(data.GetProperty("found").GetBoolean());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("document").ValueKind);
    }
}
```

- [ ] **Step 3.2: Run, verify failure** (no `document` property in response)

- [ ] **Step 3.3: Implement** — in `HandleGetCommand`, replace the two return statements:

```csharp
        var document = await _documentStore.GetAsync(collection, id);
        if (document == null)
        {
            return NoSqlMessage.CreateSuccess(new { found = false, document = (object?)null, value = (object?)null });
        }

        var flat = new Dictionary<string, object?>(document.Data.Count + 1) { ["_id"] = document.Id };
        foreach (var kv in document.Data) flat[kv.Key] = kv.Value;

        // "document" (flat) is the contract clients read; "value" retained for backward compatibility
        return NoSqlMessage.CreateSuccess(new { found = true, document = flat, value = document });
```

No client change is needed: `AdvGenNoSqlClient.GetAsync` already reads `document` (`AdvGenNoSqlClient.Commands.cs:54`) and now receives it. `TcpAdminService.GetDocumentAsync` starts working as a side effect.

- [ ] **Step 3.4: Run new tests + any existing get/set tests, verify pass**

Run: `dotnet test AdvGenNoSqlServer.Tests/AdvGenNoSqlServer.Tests.csproj -c Release --filter "FullyQualifiedName~ClientGetFixTests|FullyQualifiedName~ClientGet|FullyQualifiedName~BatchOperationTests"`
Then the full suite once: `dotnet test AdvGenNoSqlServer.Tests/AdvGenNoSqlServer.Tests.csproj -c Release` (some tests may assert on the old `value`-only shape — the shape is additive so failures indicate tests asserting exact property sets; update those to the new contract).

- [ ] **Step 3.5: Commit** — `git commit -m "fix: get command returns flat document property matching client contract"`

---

### Task 4: `NoSqlMessage.CreateCommand` — real JSON serialization

**Files:**
- Modify: `AdvGenNoSqlServer.Network/MessageProtocol.cs` (~lines 163-179)
- Test: `AdvGenNoSqlServer.Tests/MessageProtocolCreateCommandTests.cs` (new)

- [ ] **Step 4.1: Write failing tests**

```csharp
// AdvGenNoSqlServer.Tests/MessageProtocolCreateCommandTests.cs
using AdvGenNoSqlServer.Network;
using System.Text.Json;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

public class MessageProtocolCreateCommandTests
{
    [Fact]
    public void CreateCommand_EscapesSpecialCharactersInCollection()
    {
        var message = NoSqlMessage.CreateCommand("get", "we\"ird\\name");
        using var doc = JsonDocument.Parse(message.GetPayloadAsString()); // must be valid JSON
        Assert.Equal("get", doc.RootElement.GetProperty("command").GetString());
        Assert.Equal("we\"ird\\name", doc.RootElement.GetProperty("collection").GetString());
    }

    [Fact]
    public void CreateCommand_WithDocument_KeepsWireShape()
    {
        var message = NoSqlMessage.CreateCommand("set", "col", new { name = "x", n = 1 });
        using var doc = JsonDocument.Parse(message.GetPayloadAsString());
        Assert.Equal("set", doc.RootElement.GetProperty("command").GetString());
        Assert.Equal("col", doc.RootElement.GetProperty("collection").GetString());
        Assert.Equal("x", doc.RootElement.GetProperty("document").GetProperty("name").GetString());
    }

    [Fact]
    public void CreateCommand_WithoutDocument_OmitsDocumentProperty()
    {
        var message = NoSqlMessage.CreateCommand("count", "col");
        using var doc = JsonDocument.Parse(message.GetPayloadAsString());
        Assert.False(doc.RootElement.TryGetProperty("document", out _));
    }
}
```

- [ ] **Step 4.2: Run, verify the escaping test fails** (current concatenation produces invalid JSON → `JsonDocument.Parse` throws)

- [ ] **Step 4.3: Implement** — replace the `StringBuilder` body of `CreateCommand`:

```csharp
        public static NoSqlMessage CreateCommand(string command, string collection, object? document = null)
        {
            var payload = new System.Collections.Generic.Dictionary<string, object?>
            {
                ["command"] = command,
                ["collection"] = collection
            };
            if (document != null)
                payload["document"] = document;

            return Create(MessageType.Command, System.Text.Json.JsonSerializer.Serialize(payload));
        }
```

- [ ] **Step 4.4: Run new tests + full suite** (wire shape is unchanged for well-formed names; full suite guards regressions)

- [ ] **Step 4.5: Commit** — `git commit -m "fix: JSON-escape CreateCommand payloads instead of string concatenation"`

---

### Task 5: Query page → Command Console; multiline JSON dialogs

**Files:**
- Modify: `AdvGenNoSqlServer.AdminClient/Shared/TextInputDialog.razor`
- Modify: `AdvGenNoSqlServer.AdminClient/Pages/Documents.razor`
- Rewrite: `AdvGenNoSqlServer.AdminClient/Pages/Query.razor`
- Modify: `AdvGenNoSqlServer.AdminClient/Shared/NavMenu.razor` (label only)

No automated UI tests exist; Task 6 verifies by running the app.

- [ ] **Step 5.1: `TextInputDialog` — add `Lines`**

Add parameter `[Parameter] public int Lines { get; set; } = 1;` and set `Lines="@Lines"` on the `MudTextField`. In `Documents.razor`, add `{ x => x.Lines, 12 }` to the `DialogParameters` of both `InsertDocument` and `EditDocument`.

- [ ] **Step 5.2: Rewrite `Query.razor` as a Command Console**

```razor
@page "/query"
@inject TcpAdminService AdminService
@inject NavigationManager Navigation
@inject ISnackbar Snackbar

<PageTitle>Command Console - AdvGenNoSQL Admin</PageTitle>

<MudText Typo="Typo.h4" Class="mb-2">Command Console</MudText>
<MudText Typo="Typo.body2" Class="mb-4" Color="Color.Secondary">
    Sends a raw JSON command to the server over TCP. Pick a template or write your own —
    the payload must be a JSON object with a <code>command</code> property.
</MudText>

<MudSelect T="string" Label="Template" Value="_template" ValueChanged="ApplyTemplate" Class="mb-3" Clearable="true">
    @foreach (var name in Templates.Keys)
    {
        <MudSelectItem Value="@name">@name</MudSelectItem>
    }
</MudSelect>

<MudTextField T="string" @bind-Value="_commandJson" Label="Command JSON" Variant="Variant.Outlined"
              Lines="8" Class="mb-3" Style="font-family:monospace;" />

<MudButton Variant="Variant.Filled" Color="Color.Primary" OnClick="RunCommand"
           Disabled="_running" StartIcon="@Icons.Material.Filled.PlayArrow">
    @(_running ? "Running..." : "Send Command")
</MudButton>

@if (_result != null)
{
    <MudPaper Elevation="1" Class="pa-4 mt-4" Style="overflow-x:auto;">
        <pre style="font-family:monospace;white-space:pre-wrap;margin:0;">@_result</pre>
    </MudPaper>
}

@code {
    private string _template = "";
    private string _commandJson = "";
    private string? _result;
    private bool _running;

    private static readonly Dictionary<string, string> Templates = new()
    {
        ["stats"] = """{"command":"stats"}""",
        ["listcollections"] = """{"command":"listcollections"}""",
        ["count"] = """{"command":"count","collection":"myCollection"}""",
        ["listdocuments"] = """{"command":"listdocuments","collection":"myCollection","document":{"skip":0,"take":50}}""",
        ["get"] = """{"command":"get","collection":"myCollection","id":"documentId"}""",
        ["set (insert/update)"] = """{"command":"set","collection":"myCollection","document":{"_id":"documentId","field":"value"}}""",
        ["exists"] = """{"command":"exists","collection":"myCollection","id":"documentId"}""",
        ["delete"] = """{"command":"delete","collection":"myCollection","id":"documentId"}""",
        ["find_one"] = """{"command":"find_one","collection":"myCollection","filter":{"field":"value"}}""",
        ["createcollection"] = """{"command":"createcollection","collection":"newCollection"}""",
        ["dropcollection"] = """{"command":"dropcollection","collection":"oldCollection"}""",
        ["cluster info"] = """{"command":"cluster","action":"info"}""",
    };

    protected override void OnInitialized()
    {
        if (!AdminService.IsConnected)
            Navigation.NavigateTo("/login");
    }

    private void ApplyTemplate(string name)
    {
        _template = name;
        if (!string.IsNullOrEmpty(name) && Templates.TryGetValue(name, out var json))
            _commandJson = PrettyPrint(json);
    }

    private async Task RunCommand()
    {
        if (string.IsNullOrWhiteSpace(_commandJson)) return;

        // Validate client-side before sending: must be a JSON object with a "command" property
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(_commandJson);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("command", out _))
            {
                Snackbar.Add("Payload must be a JSON object with a \"command\" property.", Severity.Warning);
                return;
            }
        }
        catch (System.Text.Json.JsonException ex)
        {
            Snackbar.Add($"Invalid JSON: {ex.Message}", Severity.Warning);
            return;
        }

        _running = true;
        _result = null;
        try
        {
            var response = await AdminService.ExecuteQueryAsync(_commandJson);
            _result = response.Success
                ? PrettyPrint(System.Text.Json.JsonSerializer.Serialize(response.Data))
                : $"ERROR {response.Error?.Code}: {response.Error?.Message}";
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Command failed: {ex.Message}", Severity.Error);
        }
        finally
        {
            _running = false;
        }
    }

    private static string PrettyPrint(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return System.Text.Json.JsonSerializer.Serialize(doc.RootElement,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
        catch { return json; }
    }
}
```

Notes:
- `ExecuteQueryAsync` sends the payload verbatim as a Command message — with valid JSON it is exactly what `HandleCommandAsync` expects, so no service change is needed. Verify `MudSelect`'s `ValueChanged` usage compiles against MudBlazor 7 (`ValueChanged="ApplyTemplate"` with `Value="_template"`); if the two-way binding pattern differs, use `@bind-Value` with an `OnValueChanged` handler per MudBlazor 7 docs.
- Error responses come back as `response.Success == false` (the client's `ParseResponse` handles `MessageType.Error`) — the console renders the code + message rather than throwing.

- [ ] **Step 5.3: Update the nav label** in `NavMenu.razor`: change the Query link text to `Console` (keep `href="query"` so bookmarks survive).

- [ ] **Step 5.4: Build** — `dotnet build AdvGenNoSqlServer.AdminClient -c Release`, expected clean.

- [ ] **Step 5.5: Commit**

```bash
git add AdvGenNoSqlServer.AdminClient/
git commit -m "feat: command console page, multiline JSON dialogs in admin client"
```

---

### Task 6: End-to-end verification

SSL prerequisites are in `AdvGenNoSqlServer.AdminClient/README.md` (dev cert exported to `AdvGenNoSqlServer.Host/certs/advgen.pfx`, `EnableSsl: true`).

- [ ] **Step 6.1: Run the Host** (`dotnet run --project AdvGenNoSqlServer.Host/AdvGenNoSqlServer.Host.csproj -c Release`) **and the AdminClient** (`dotnet run --project AdvGenNoSqlServer.AdminClient/AdvGenNoSqlServer.AdminClient.csproj`), log in.

- [ ] **Step 6.2: Walk the checklist**

1. Collections: **New Collection** creates and appears in the list (was `UNKNOWN_COMMAND` before).
2. Collections: **Delete** removes a collection after confirm (was broken).
3. Documents: clicking a collection lists its documents with paging (**the page rendered nothing but an error before**); insert a doc via the now-multiline JSON dialog; edit it; delete it.
4. Insert 60 documents (console `set` template in a loop or repeated inserts) and verify Next/Previous paging works with the correct total.
5. Console: each template runs and pretty-prints a response; invalid JSON and JSON without `command` are rejected client-side with a warning snackbar; `get` on a missing id shows `found:false`.
6. Collection with special characters: send `createcollection` for `test"quote` from the console. Expected: the payload arrives as **valid JSON** and the server answers with a proper JSON response — in Hybrid mode likely a `STORAGE_ERROR` (`"` is not a valid Windows directory character), which is the correct behavior. Before the escaping fix the payload itself was malformed JSON and failed at the protocol layer with `INVALID_COMMAND`.
7. Logout/login still works; dashboard unaffected.

- [ ] **Step 6.3: Full test suite + finish**

```bash
dotnet test AdvGenNoSqlServer.Tests/AdvGenNoSqlServer.Tests.csproj -c Release
```

Use @superpowers:verification-before-completion, then @superpowers:finishing-a-development-branch.

---

## Execution addendum (2026-07-14, branch `feature/admin-ui-fixes`)

All tasks executed and verified. One discovery beyond the plan (audit doc §3.5, defect D6): the Host runs its own duplicated TCP dispatcher (`NoSqlServerHost` in `Host/Program.cs`), which had the same class of contract bugs — `set` crashed on all flat documents, `get`/`listdocuments` returned non-flat `Document` objects, `createcollection` always reported `created=true`. The same contract fixes were applied there. End-to-end verification ran a harness replicating `TcpAdminService` against the live Host over TCP+SSL: connect/auth, create/duplicate-create/drop collection, set/list/get(flat)/delete document, special-character collection name, stats — **all checks passed**. AdminClient serves at https://localhost:7210 (HTTP 200). Full suite: 3,199 passed / 1 pre-existing flake (`BackgroundIndexBuilderTests.StartBuildAsync_MultipleConcurrentBuilds_RespectsMaxConcurrent`, fails identically on master).

## Execution notes

- Task order: 1 → 2 → 3 → 4 → 5 → 6. Tasks 1–4 are server/protocol fixes each independently valuable; Task 5 depends on nothing but is best last so the console templates can exercise the new commands.
- Deliberately out of scope (see audit §4): session persistence across refresh, server-side per-command authorization, cluster/index/full-text/import-export pages, cache UI (separate plan `2026-07-14-cache-admin-ui.md`).
