# AdminClient Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `AdvGenNoSqlServer.AdminClient`, a Blazor Server admin UI that connects directly to the NoSQL server via TCP with SSL.

**Architecture:** Blazor Server (net9.0) with a single Scoped `TcpAdminService` owning the `AdvGenNoSqlClient` TCP connection. Browser → app HTTPS on port 7210; app → NoSQL TCP always SSL. Four new TCP commands (`createcollection`, `dropcollection`, `listdocuments`, `stats`) added to the Host before building the UI. Client `GetAsync` response-key bug fixed first.

**Tech Stack:** ASP.NET Core Blazor Server net9.0, MudBlazor 7.15.0, `AdvGenNoSqlServer.Client` + `AdvGenNoSqlServer.Core` project references, xUnit + Moq for tests.

---

## File Map

### Files to modify
- `AdvGenNoSqlServer.Client/AdvGenNoSqlClient.Commands.cs:54` — fix `"value"` → `"document"`
- `AdvGenNoSqlServer.Host/Program.cs:825-834` — add 4 cases to switch
- `AdvGenNoSqlServer.Host/Program.cs:979` — add 4 new handler methods after `HandleListCollectionsCommandAsync`
- `AdvGenNoSqlServer.Host/appsettings.json` — add SSL cert config keys
- `AdvGenNoSqlServer.sln` — add new project entry
- `.gitignore` — add `certs/` exclusion

### Files to create
- `AdvGenNoSqlServer.AdminClient/AdvGenNoSqlServer.AdminClient.csproj`
- `AdvGenNoSqlServer.AdminClient/Program.cs`
- `AdvGenNoSqlServer.AdminClient/appsettings.json`
- `AdvGenNoSqlServer.AdminClient/Properties/launchSettings.json`
- `AdvGenNoSqlServer.AdminClient/_Imports.razor`
- `AdvGenNoSqlServer.AdminClient/App.razor`
- `AdvGenNoSqlServer.AdminClient/wwwroot/index.html` (empty; not used by Blazor Server but keeps IDE happy)
- `AdvGenNoSqlServer.AdminClient/Services/TcpAdminService.cs`
- `AdvGenNoSqlServer.AdminClient/Shared/MainLayout.razor`
- `AdvGenNoSqlServer.AdminClient/Shared/NavMenu.razor`
- `AdvGenNoSqlServer.AdminClient/Shared/ConfirmDialog.razor`
- `AdvGenNoSqlServer.AdminClient/Shared/TextInputDialog.razor`
- `AdvGenNoSqlServer.AdminClient/Pages/Login.razor`
- `AdvGenNoSqlServer.AdminClient/Pages/Index.razor`
- `AdvGenNoSqlServer.AdminClient/Pages/Collections.razor`
- `AdvGenNoSqlServer.AdminClient/Pages/Documents.razor`
- `AdvGenNoSqlServer.AdminClient/Pages/Query.razor`

---

## Task 1: Fix `GetAsync` client bug

**Files:**
- Modify: `AdvGenNoSqlServer.Client/AdvGenNoSqlClient.Commands.cs:54`

The server's `HandleGetCommandAsync` returns `{ found = true, document }` but the client reads `"value"`. This means `GetDocumentAsync` in the admin service will always return null.

- [ ] **Step 1: Write the failing test**

Add to `AdvGenNoSqlServer.Tests/ClientGetFixTests.cs` (new file):

```csharp
using System.Text.Json;
using AdvGenNoSqlServer.Client;

namespace AdvGenNoSqlServer.Tests;

public class ClientGetFixTests
{
    [Fact]
    public void ParseResponse_WithDocumentKey_ReturnsDocument()
    {
        // Simulate what GetAsync does with the JsonElement from ParseResponse
        var json = """{"found":true,"document":{"_id":"abc","name":"test"}}""";
        using var doc = JsonDocument.Parse(json);
        var dataElement = doc.RootElement;

        Dictionary<string, object>? result = null;
        if (dataElement.TryGetProperty("document", out var documentElement) &&
            documentElement.ValueKind != JsonValueKind.Null)
        {
            result = JsonSerializer.Deserialize<Dictionary<string, object>>(documentElement.GetRawText());
        }

        Assert.NotNull(result);
        Assert.Equal("abc", result["_id"].ToString());
    }

    [Fact]
    public void ParseResponse_WithValueKey_ReturnsNull()
    {
        // Proves the bug: "value" key yields nothing since server sends "document"
        var json = """{"found":true,"document":{"_id":"abc","name":"test"}}""";
        using var doc = JsonDocument.Parse(json);
        var dataElement = doc.RootElement;

        Dictionary<string, object>? result = null;
        if (dataElement.TryGetProperty("value", out var valueElement) &&
            valueElement.ValueKind != JsonValueKind.Null)
        {
            result = JsonSerializer.Deserialize<Dictionary<string, object>>(valueElement.GetRawText());
        }

        Assert.Null(result); // proves current bug returns null
    }
}
```

- [ ] **Step 2: Run tests to confirm current state**

```bash
cd e:/Projects/AdvGenNoSQLServer
dotnet test AdvGenNoSqlServer.Tests --filter "ClientGetFixTests" --no-build 2>/dev/null || dotnet test AdvGenNoSqlServer.Tests --filter "ClientGetFixTests"
```

Expected: `ParseResponse_WithDocumentKey_ReturnsDocument` passes (the logic itself is correct), `ParseResponse_WithValueKey_ReturnsNull` passes (confirms the bug returns null). This proves the fix direction.

- [ ] **Step 3: Apply the one-line fix**

In `AdvGenNoSqlServer.Client/AdvGenNoSqlClient.Commands.cs` line 54, change:

```csharp
                if (dataElement.TryGetProperty("value", out var valueElement) &&
                    valueElement.ValueKind != System.Text.Json.JsonValueKind.Null)
                {
                    // Convert JsonElement to Dictionary<string, object> to avoid disposal issues
                    return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(valueElement.GetRawText());
                }
```

To:

```csharp
                if (dataElement.TryGetProperty("document", out var documentElement) &&
                    documentElement.ValueKind != System.Text.Json.JsonValueKind.Null)
                {
                    // Convert JsonElement to Dictionary<string, object> to avoid disposal issues
                    return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(documentElement.GetRawText());
                }
```

- [ ] **Step 4: Build to verify**

```bash
dotnet build AdvGenNoSqlServer.Client/AdvGenNoSqlServer.Client.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add AdvGenNoSqlServer.Client/AdvGenNoSqlClient.Commands.cs AdvGenNoSqlServer.Tests/ClientGetFixTests.cs
git commit -m "fix: GetAsync reads 'document' key matching server response"
```

---

## Task 2: Add 4 new TCP command handlers to Host

**Files:**
- Modify: `AdvGenNoSqlServer.Host/Program.cs`

Add `createcollection`, `dropcollection`, `listdocuments`, and `stats` to the switch at line 825 and add their handler methods after line 979.

- [ ] **Step 1: Update the switch statement**

In `AdvGenNoSqlServer.Host/Program.cs`, find the switch at lines 825–834:

```csharp
            return command switch
            {
                "get" => await HandleGetCommandAsync(doc.RootElement),
                "set" => await HandleSetCommandAsync(doc.RootElement),
                "delete" => await HandleDeleteCommandAsync(doc.RootElement),
                "exists" => await HandleExistsCommandAsync(doc.RootElement),
                "count" => await HandleCountCommandAsync(doc.RootElement),
                "listcollections" => await HandleListCollectionsCommandAsync(),
                _ => NoSqlMessage.CreateError("UNKNOWN_COMMAND", $"Unknown command: {command}")
            };
```

Replace with:

```csharp
            return command switch
            {
                "get" => await HandleGetCommandAsync(doc.RootElement),
                "set" => await HandleSetCommandAsync(doc.RootElement),
                "delete" => await HandleDeleteCommandAsync(doc.RootElement),
                "exists" => await HandleExistsCommandAsync(doc.RootElement),
                "count" => await HandleCountCommandAsync(doc.RootElement),
                "listcollections" => await HandleListCollectionsCommandAsync(),
                "createcollection" => await HandleCreateCollectionCommandAsync(doc.RootElement),
                "dropcollection" => await HandleDropCollectionCommandAsync(doc.RootElement),
                "listdocuments" => await HandleListDocumentsCommandAsync(doc.RootElement),
                "stats" => await HandleStatsCommandAsync(),
                _ => NoSqlMessage.CreateError("UNKNOWN_COMMAND", $"Unknown command: {command}")
            };
```

- [ ] **Step 2: Add the 4 handler methods**

After line 979 (the closing brace of `HandleListCollectionsCommandAsync`), insert:

```csharp
    private async Task<NoSqlMessage> HandleCreateCollectionCommandAsync(System.Text.Json.JsonElement commandElement)
    {
        if (!commandElement.TryGetProperty("collection", out var collectionProp))
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection");

        var collection = collectionProp.GetString();
        if (string.IsNullOrEmpty(collection))
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Collection name is required");

        await _apiData.DocumentStore!.CreateCollectionAsync(collection);
        return NoSqlMessage.CreateSuccess(new { created = true, name = collection });
    }

    private async Task<NoSqlMessage> HandleDropCollectionCommandAsync(System.Text.Json.JsonElement commandElement)
    {
        if (!commandElement.TryGetProperty("collection", out var collectionProp))
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection");

        var collection = collectionProp.GetString();
        if (string.IsNullOrEmpty(collection))
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Collection name is required");

        var dropped = await _apiData.DocumentStore!.DropCollectionAsync(collection);
        return NoSqlMessage.CreateSuccess(new { dropped, name = collection });
    }

    private async Task<NoSqlMessage> HandleListDocumentsCommandAsync(System.Text.Json.JsonElement commandElement)
    {
        if (!commandElement.TryGetProperty("collection", out var collectionProp))
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Missing collection");

        var collection = collectionProp.GetString();
        if (string.IsNullOrEmpty(collection))
            return NoSqlMessage.CreateError("INVALID_COMMAND", "Collection name is required");

        int skip = 0;
        int take = 50;
        if (commandElement.TryGetProperty("document", out var docProp) &&
            docProp.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            if (docProp.TryGetProperty("skip", out var skipProp)) skip = skipProp.GetInt32();
            if (docProp.TryGetProperty("take", out var takeProp)) take = takeProp.GetInt32();
        }

        var all = (await _apiData.DocumentStore!.GetAllAsync(collection)).ToList();
        var page = all.Skip(skip).Take(take > 0 ? take : 50).ToList();
        var total = all.Count;
        return NoSqlMessage.CreateSuccess(new { documents = page, total, collection });
    }

    private async Task<NoSqlMessage> HandleStatsCommandAsync()
    {
        var uptime = DateTime.UtcNow - _apiData.StartTime;
        var memMB = (int)(GC.GetTotalMemory(false) / 1_048_576);
        long totalDocuments = 0;
        int totalCollections = 0;
        if (_apiData.DocumentStore != null)
        {
            var cols = (await _apiData.DocumentStore.GetCollectionsAsync()).ToList();
            totalCollections = cols.Count;
            foreach (var c in cols) totalDocuments += await _apiData.DocumentStore.CountAsync(c);
        }
        return NoSqlMessage.CreateSuccess(new
        {
            version = "1.0.0",
            uptimeSeconds = (long)uptime.TotalSeconds,
            memoryUsageMB = memMB,
            totalDocuments,
            totalCollections,
            activeConnections = _apiData.TcpServer?.ActiveConnectionCount ?? 0
        });
    }
```

- [ ] **Step 3: Build the Host**

```bash
dotnet build AdvGenNoSqlServer.Host/AdvGenNoSqlServer.Host.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add AdvGenNoSqlServer.Host/Program.cs
git commit -m "feat: add createcollection, dropcollection, listdocuments, stats TCP commands to Host"
```

---

## Task 3: Scaffold the AdminClient project

**Files:**
- Create: `AdvGenNoSqlServer.AdminClient/AdvGenNoSqlServer.AdminClient.csproj`
- Create: `AdvGenNoSqlServer.AdminClient/Program.cs`
- Create: `AdvGenNoSqlServer.AdminClient/appsettings.json`
- Create: `AdvGenNoSqlServer.AdminClient/Properties/launchSettings.json`
- Create: `AdvGenNoSqlServer.AdminClient/_Imports.razor`
- Create: `AdvGenNoSqlServer.AdminClient/App.razor`
- Modify: `AdvGenNoSqlServer.sln`

- [ ] **Step 1: Create the .csproj**

Create `AdvGenNoSqlServer.AdminClient/AdvGenNoSqlServer.AdminClient.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="MudBlazor" Version="7.15.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\AdvGenNoSqlServer.Client\AdvGenNoSqlServer.Client.csproj" />
    <ProjectReference Include="..\AdvGenNoSqlServer.Core\AdvGenNoSqlServer.Core.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create Program.cs**

Create `AdvGenNoSqlServer.AdminClient/Program.cs`:

```csharp
using MudBlazor.Services;
using AdvGenNoSqlServer.AdminClient.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddMudServices();

builder.Services.AddScoped<TcpAdminService>();

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
```

- [ ] **Step 3: Create appsettings.json**

Create `AdvGenNoSqlServer.AdminClient/appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

- [ ] **Step 4: Create launchSettings.json**

Create `AdvGenNoSqlServer.AdminClient/Properties/launchSettings.json`:

```json
{
  "profiles": {
    "https": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": true,
      "applicationUrl": "https://localhost:7210;http://localhost:5210",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

- [ ] **Step 5: Create _Imports.razor**

Create `AdvGenNoSqlServer.AdminClient/_Imports.razor`:

```razor
@using System.Net.Http
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using Microsoft.JSInterop
@using MudBlazor
@using AdvGenNoSqlServer.AdminClient
@using AdvGenNoSqlServer.AdminClient.Shared
@using AdvGenNoSqlServer.AdminClient.Services
@using AdvGenNoSqlServer.AdminClient.Pages
@using AdvGenNoSqlServer.Client
@using AdvGenNoSqlServer.Core.Models
```

- [ ] **Step 6: Create App.razor**

Create `AdvGenNoSqlServer.AdminClient/App.razor`:

```razor
<Router AppAssembly="@typeof(App).Assembly">
    <Found Context="routeData">
        <RouteView RouteData="@routeData" DefaultLayout="@typeof(MainLayout)" />
        <FocusOnNavigate RouteData="@routeData" Selector="h1" />
    </Found>
    <NotFound>
        <PageTitle>Not found</PageTitle>
        <LayoutView Layout="@typeof(MainLayout)">
            <MudContainer MaxWidth="MaxWidth.Small" Class="mt-16">
                <MudPaper Elevation="3" Class="pa-8 text-center">
                    <MudText Typo="Typo.h4" Class="mb-4">Page Not Found</MudText>
                    <MudButton Variant="Variant.Filled" Color="Color.Primary" Href="/">Go Home</MudButton>
                </MudPaper>
            </MudContainer>
        </LayoutView>
    </NotFound>
</Router>
```

- [ ] **Step 7: Create an empty wwwroot/index.html (IDE placeholder)**

```bash
mkdir -p AdvGenNoSqlServer.AdminClient/wwwroot
touch AdvGenNoSqlServer.AdminClient/wwwroot/index.html
```

This file is not served by Blazor Server (which uses `_Host.cshtml`) but prevents IDE warnings about a missing wwwroot.

- [ ] **Step 8: Create the Razor host page**

Create `AdvGenNoSqlServer.AdminClient/Pages/_Host.cshtml`:

```cshtml
@page "/"
@namespace AdvGenNoSqlServer.AdminClient.Pages
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
@{
    Layout = null;
}

<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>AdvGenNoSQL Admin Client</title>
    <base href="~/" />
    <link href="https://fonts.googleapis.com/css?family=Roboto:300,400,500,700&display=swap" rel="stylesheet" />
    <link href="_content/MudBlazor/MudBlazor.min.css" rel="stylesheet" />
</head>
<body>
    <component type="typeof(App)" render-mode="ServerPrerendered" />
    <script src="_framework/blazor.server.js"></script>
    <script src="_content/MudBlazor/MudBlazor.min.js"></script>
</body>
</html>
```

- [ ] **Step 9: Add project to solution**

```bash
cd e:/Projects/AdvGenNoSQLServer
dotnet sln AdvGenNoSqlServer.sln add AdvGenNoSqlServer.AdminClient/AdvGenNoSqlServer.AdminClient.csproj
```

- [ ] **Step 10: Build to verify scaffold**

```bash
dotnet build AdvGenNoSqlServer.AdminClient/AdvGenNoSqlServer.AdminClient.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 11: Commit**

```bash
git add AdvGenNoSqlServer.AdminClient/ AdvGenNoSqlServer.sln
git commit -m "feat: scaffold AdvGenNoSqlServer.AdminClient Blazor Server project"
```

---

## Task 4: Implement TcpAdminService

**Files:**
- Create: `AdvGenNoSqlServer.AdminClient/Services/TcpAdminService.cs`

This is the only service. It owns the `AdvGenNoSqlClient` TCP connection and exposes all admin operations. Scoped lifetime = one instance per browser tab (Blazor Server circuit).

- [ ] **Step 1: Create TcpAdminService.cs**

Create `AdvGenNoSqlServer.AdminClient/Services/TcpAdminService.cs`:

```csharp
using System.Text.Json;
using AdvGenNoSqlServer.Client;

namespace AdvGenNoSqlServer.AdminClient.Services;

public class TcpAdminService : IAsyncDisposable
{
    private AdvGenNoSqlClient? _client;

    public bool IsConnected => _client?.IsConnected == true;
    public string? CurrentUser { get; private set; }
    public string Host { get; private set; } = string.Empty;
    public int Port { get; private set; }

    public async Task ConnectAndAuthenticateAsync(string host, int port, string username, string password)
    {
        if (_client != null)
        {
            await _client.DisposeAsync();
            _client = null;
        }

        var options = new AdvGenNoSqlClientOptions
        {
            UseSsl = true,
            SslTargetHost = host,
            CheckCertificateRevocation = false
        };

        _client = new AdvGenNoSqlClient($"{host}:{port}", options);
        await _client.ConnectAsync();
        await _client.AuthenticateAsync(username, password);

        Host = host;
        Port = port;
        CurrentUser = username;
    }

    public async Task DisconnectAsync()
    {
        if (_client != null)
        {
            await _client.DisconnectAsync();
            await _client.DisposeAsync();
            _client = null;
        }
        CurrentUser = null;
        Host = string.Empty;
        Port = 0;
    }

    public async Task<ServerStats> GetStatsAsync()
    {
        EnsureConnected();
        var response = await _client!.ExecuteCommandAsync("stats", "");
        EnsureSuccess(response, "stats");
        var data = (JsonElement)response.Data!;
        return new ServerStats
        {
            Version = data.GetProperty("version").GetString() ?? "",
            UptimeSeconds = data.GetProperty("uptimeSeconds").GetInt64(),
            MemoryUsageMB = data.GetProperty("memoryUsageMB").GetInt32(),
            TotalDocuments = data.GetProperty("totalDocuments").GetInt64(),
            TotalCollections = data.GetProperty("totalCollections").GetInt32(),
            ActiveConnections = data.GetProperty("activeConnections").GetInt32()
        };
    }

    public async Task<List<string>> GetCollectionsAsync()
    {
        EnsureConnected();
        var response = await _client!.ExecuteCommandAsync("listcollections", "");
        EnsureSuccess(response, "listcollections");
        var data = (JsonElement)response.Data!;
        return data.GetProperty("collections")
            .EnumerateArray()
            .Select(e => e.GetString() ?? "")
            .ToList();
    }

    public async Task<bool> CreateCollectionAsync(string name)
    {
        EnsureConnected();
        var response = await _client!.ExecuteCommandAsync("createcollection", name);
        EnsureSuccess(response, "createcollection");
        var data = (JsonElement)response.Data!;
        return data.GetProperty("created").GetBoolean();
    }

    public async Task<bool> DeleteCollectionAsync(string name)
    {
        EnsureConnected();
        var response = await _client!.ExecuteCommandAsync("dropcollection", name);
        EnsureSuccess(response, "dropcollection");
        var data = (JsonElement)response.Data!;
        return data.GetProperty("dropped").GetBoolean();
    }

    public async Task<long> CountAsync(string collection)
    {
        EnsureConnected();
        var response = await _client!.ExecuteCommandAsync("count", collection);
        EnsureSuccess(response, "count");
        var data = (JsonElement)response.Data!;
        return data.GetProperty("count").GetInt64();
    }

    public async Task<(List<Dictionary<string, object>> Documents, long Total)> GetDocumentsAsync(
        string collection, int skip, int take)
    {
        EnsureConnected();
        var response = await _client!.ExecuteCommandAsync(
            "listdocuments", collection, new { skip, take });
        EnsureSuccess(response, "listdocuments");
        var data = (JsonElement)response.Data!;

        var documents = data.GetProperty("documents")
            .EnumerateArray()
            .Select(e => JsonSerializer.Deserialize<Dictionary<string, object>>(e.GetRawText())!)
            .ToList();
        var total = data.GetProperty("total").GetInt64();
        return (documents, total);
    }

    public async Task<Dictionary<string, object>?> GetDocumentAsync(string collection, string id)
    {
        EnsureConnected();
        return await _client!.GetAsync(collection, id);
    }

    public async Task<string> UpsertDocumentAsync(string collection, object document)
    {
        EnsureConnected();
        return await _client!.SetAsync(collection, document);
    }

    public async Task<bool> DeleteDocumentAsync(string collection, string id)
    {
        EnsureConnected();
        return await _client!.DeleteAsync(collection, id);
    }

    public async Task<NoSqlResponse> ExecuteQueryAsync(string query)
    {
        EnsureConnected();
        return await _client!.ExecuteQueryAsync(query);
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
            throw new InvalidOperationException("Not connected to server.");
    }

    private static void EnsureSuccess(NoSqlResponse response, string command)
    {
        if (!response.Success)
            throw new InvalidOperationException(
                $"Command '{command}' failed: {response.Error?.Message ?? "Unknown error"}");
    }

    public async ValueTask DisposeAsync()
    {
        if (_client != null)
        {
            await _client.DisposeAsync();
            _client = null;
        }
    }
}

public record ServerStats
{
    public string Version { get; init; } = "";
    public long UptimeSeconds { get; init; }
    public int MemoryUsageMB { get; init; }
    public long TotalDocuments { get; init; }
    public int TotalCollections { get; init; }
    public int ActiveConnections { get; init; }
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build AdvGenNoSqlServer.AdminClient/AdvGenNoSqlServer.AdminClient.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add AdvGenNoSqlServer.AdminClient/Services/TcpAdminService.cs
git commit -m "feat: implement TcpAdminService with all admin operations"
```

---

## Task 5: Create shared layout components

**Files:**
- Create: `AdvGenNoSqlServer.AdminClient/Shared/MainLayout.razor`
- Create: `AdvGenNoSqlServer.AdminClient/Shared/NavMenu.razor`
- Create: `AdvGenNoSqlServer.AdminClient/Shared/ConfirmDialog.razor`
- Create: `AdvGenNoSqlServer.AdminClient/Shared/TextInputDialog.razor`

- [ ] **Step 1: Create ConfirmDialog.razor**

Create `AdvGenNoSqlServer.AdminClient/Shared/ConfirmDialog.razor`:

```razor
<MudDialog>
    <DialogContent>
        <MudText>@Message</MudText>
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="Cancel">@CancelText</MudButton>
        <MudButton Color="Color.Error" Variant="Variant.Filled" OnClick="Submit">@ConfirmText</MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter]
    private MudDialogInstance MudDialog { get; set; } = default!;

    [Parameter] public string Title { get; set; } = "Confirm";
    [Parameter] public string Message { get; set; } = "Are you sure?";
    [Parameter] public string ConfirmText { get; set; } = "Confirm";
    [Parameter] public string CancelText { get; set; } = "Cancel";

    private void Submit() => MudDialog.Close(DialogResult.Ok(true));
    private void Cancel() => MudDialog.Cancel();
}
```

- [ ] **Step 2: Create TextInputDialog.razor**

Create `AdvGenNoSqlServer.AdminClient/Shared/TextInputDialog.razor`:

```razor
<MudDialog>
    <DialogContent>
        <MudText Class="mb-4">@Message</MudText>
        <MudTextField T="string"
                      @bind-Value="_inputValue"
                      Label="@Label"
                      Variant="Variant.Outlined"
                      Immediate="true"
                      Required="true" />
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="Cancel">Cancel</MudButton>
        <MudButton Color="Color.Primary" Variant="Variant.Filled" OnClick="Submit"
                   Disabled="string.IsNullOrWhiteSpace(_inputValue)">OK</MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter]
    private MudDialogInstance MudDialog { get; set; } = default!;

    [Parameter] public string Title { get; set; } = "Input";
    [Parameter] public string Message { get; set; } = "Enter a value:";
    [Parameter] public string Label { get; set; } = "Value";

    private string _inputValue = "";

    private void Submit() => MudDialog.Close(DialogResult.Ok(_inputValue));
    private void Cancel() => MudDialog.Cancel();
}
```

- [ ] **Step 3: Create NavMenu.razor**

Create `AdvGenNoSqlServer.AdminClient/Shared/NavMenu.razor`:

```razor
<MudNavMenu>
    <MudNavLink Href="" Match="NavLinkMatch.All" Icon="@Icons.Material.Filled.Dashboard">
        Dashboard
    </MudNavLink>
    <MudNavLink Href="collections" Match="NavLinkMatch.Prefix" Icon="@Icons.Material.Filled.Storage">
        Collections
    </MudNavLink>
    <MudNavLink Href="documents" Match="NavLinkMatch.Prefix" Icon="@Icons.Material.Filled.Description">
        Documents
    </MudNavLink>
    <MudNavLink Href="query" Match="NavLinkMatch.Prefix" Icon="@Icons.Material.Filled.Search">
        Query
    </MudNavLink>
</MudNavMenu>
```

- [ ] **Step 4: Create MainLayout.razor**

Create `AdvGenNoSqlServer.AdminClient/Shared/MainLayout.razor`:

```razor
@inherits LayoutComponentBase
@inject TcpAdminService AdminService
@inject NavigationManager Navigation
@inject ISnackbar Snackbar

<MudThemeProvider />
<MudPopoverProvider />
<MudDialogProvider />
<MudSnackbarProvider />

<MudLayout>
    <MudAppBar Elevation="1">
        <MudIconButton Icon="@Icons.Material.Filled.Menu" Color="Color.Inherit" Edge="Edge.Start"
                       OnClick="@(_ => _drawerOpen = !_drawerOpen)" />
        <MudText Typo="Typo.h6" Class="ml-3">AdvGenNoSQL Admin (TCP)</MudText>
        <MudSpacer />
        @if (AdminService.IsConnected)
        {
            <MudChip T="string" Color="Color.Success" Size="Size.Small" Class="mr-2">
                <MudIcon Icon="@Icons.Material.Filled.Circle" Size="Size.Small" Class="mr-1" />
                @AdminService.Host:@AdminService.Port
            </MudChip>
            <MudText Typo="Typo.body2" Class="mr-4">@AdminService.CurrentUser</MudText>
            <MudIconButton Icon="@Icons.Material.Filled.Logout" Color="Color.Inherit" OnClick="Logout" />
        }
        else
        {
            <MudChip T="string" Color="Color.Error" Size="Size.Small" Class="mr-2">
                <MudIcon Icon="@Icons.Material.Filled.Circle" Size="Size.Small" Class="mr-1" />
                Disconnected
            </MudChip>
            <MudButton Variant="Variant.Text" Color="Color.Inherit" Href="/login">Login</MudButton>
        }
    </MudAppBar>
    <MudDrawer @bind-Open="_drawerOpen" ClipMode="DrawerClipMode.Always" Elevation="2">
        <MudDrawerHeader>
            <MudText Typo="Typo.h6">Navigation</MudText>
        </MudDrawerHeader>
        <NavMenu />
    </MudDrawer>
    <MudMainContent>
        <MudContainer MaxWidth="MaxWidth.ExtraLarge" Class="pt-4 pb-4">
            @Body
        </MudContainer>
    </MudMainContent>
</MudLayout>

@code {
    private bool _drawerOpen = true;

    private async Task Logout()
    {
        await AdminService.DisconnectAsync();
        Snackbar.Add("Logged out", Severity.Success);
        Navigation.NavigateTo("/login");
    }
}
```

- [ ] **Step 5: Build to verify**

```bash
dotnet build AdvGenNoSqlServer.AdminClient/AdvGenNoSqlServer.AdminClient.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add AdvGenNoSqlServer.AdminClient/Shared/
git commit -m "feat: add shared layout components for AdminClient"
```

---

## Task 6: Create Login page

**Files:**
- Create: `AdvGenNoSqlServer.AdminClient/Pages/Login.razor`

- [ ] **Step 1: Create Login.razor**

Create `AdvGenNoSqlServer.AdminClient/Pages/Login.razor`:

```razor
@page "/login"
@inject TcpAdminService AdminService
@inject NavigationManager Navigation
@inject ISnackbar Snackbar

<PageTitle>Login - AdvGenNoSQL Admin (TCP)</PageTitle>

<MudContainer MaxWidth="MaxWidth.Small" Class="d-flex align-center" Style="height: 100vh;">
    <MudPaper Elevation="4" Class="pa-8" Style="width: 100%;">
        <div class="d-flex justify-center mb-6">
            <MudIcon Icon="@Icons.Material.Filled.Lock" Size="Size.Large" Color="Color.Primary"
                     Style="font-size: 64px;" />
        </div>
        <MudText Typo="Typo.h4" Align="Align.Center" Class="mb-2">AdvGenNoSQL Admin</MudText>
        <MudText Typo="Typo.body2" Align="Align.Center" Class="mb-6">TCP Administration Console</MudText>

        <MudTextField T="string" @bind-Value="_host" Label="Host" Variant="Variant.Outlined"
                      Placeholder="localhost" Class="mb-3" />
        <MudNumericField T="int" @bind-Value="_port" Label="Port" Variant="Variant.Outlined"
                         Min="1" Max="65535" Class="mb-3" />
        <MudTextField T="string" @bind-Value="_username" Label="Username" Variant="Variant.Outlined"
                      Adornment="Adornment.Start" AdornmentIcon="@Icons.Material.Filled.Person"
                      Class="mb-3" />
        <MudTextField T="string" @bind-Value="_password" Label="Password" Variant="Variant.Outlined"
                      InputType="InputType.Password"
                      Adornment="Adornment.Start" AdornmentIcon="@Icons.Material.Filled.Lock"
                      Class="mb-6" />

        <MudButton Variant="Variant.Filled" Color="Color.Primary" FullWidth="true"
                   Size="Size.Large" OnClick="HandleLogin" Disabled="_isLoading">
            @if (_isLoading)
            {
                <MudProgressCircular Size="Size.Small" Indeterminate="true" Class="mr-2" />
                <span>Connecting...</span>
            }
            else
            {
                <span>Connect</span>
            }
        </MudButton>

        <MudText Typo="Typo.caption" Align="Align.Center" Class="d-block mt-4">
            SSL is always required — server must have EnableSsl: true
        </MudText>
    </MudPaper>
</MudContainer>

@code {
    private string _host = "localhost";
    private int _port = 9191;
    private string _username = "";
    private string _password = "";
    private bool _isLoading;

    protected override async Task OnInitializedAsync()
    {
        if (AdminService.IsConnected)
            Navigation.NavigateTo("/");
        await Task.CompletedTask;
    }

    private async Task HandleLogin()
    {
        if (string.IsNullOrWhiteSpace(_host) || string.IsNullOrWhiteSpace(_username) ||
            string.IsNullOrWhiteSpace(_password))
        {
            Snackbar.Add("All fields are required", Severity.Warning);
            return;
        }

        _isLoading = true;
        try
        {
            await AdminService.ConnectAndAuthenticateAsync(_host, _port, _username, _password);
            Snackbar.Add("Connected successfully", Severity.Success);
            Navigation.NavigateTo("/");
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Connection failed: {ex.Message}", Severity.Error);
        }
        finally
        {
            _isLoading = false;
        }
    }
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build AdvGenNoSqlServer.AdminClient/AdvGenNoSqlServer.AdminClient.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add AdvGenNoSqlServer.AdminClient/Pages/Login.razor
git commit -m "feat: add Login page for AdminClient"
```

---

## Task 7: Create Dashboard (Index) page

**Files:**
- Create: `AdvGenNoSqlServer.AdminClient/Pages/Index.razor`

- [ ] **Step 1: Create Index.razor**

Create `AdvGenNoSqlServer.AdminClient/Pages/Index.razor`:

```razor
@page "/"
@inject TcpAdminService AdminService
@inject NavigationManager Navigation
@implements IAsyncDisposable

<PageTitle>Dashboard - AdvGenNoSQL Admin</PageTitle>

<MudText Typo="Typo.h4" Class="mb-6">Server Dashboard</MudText>

@if (_stats == null)
{
    <MudProgressCircular Indeterminate="true" />
}
else
{
    <MudGrid>
        <MudItem xs="12" sm="6" md="4">
            <MudPaper Elevation="2" Class="pa-4">
                <MudText Typo="Typo.subtitle2" Color="Color.Secondary">Version</MudText>
                <MudText Typo="Typo.h5">@_stats.Version</MudText>
            </MudPaper>
        </MudItem>
        <MudItem xs="12" sm="6" md="4">
            <MudPaper Elevation="2" Class="pa-4">
                <MudText Typo="Typo.subtitle2" Color="Color.Secondary">Uptime</MudText>
                <MudText Typo="Typo.h5">@FormatUptime(_stats.UptimeSeconds)</MudText>
            </MudPaper>
        </MudItem>
        <MudItem xs="12" sm="6" md="4">
            <MudPaper Elevation="2" Class="pa-4">
                <MudText Typo="Typo.subtitle2" Color="Color.Secondary">Active Connections</MudText>
                <MudText Typo="Typo.h5">@_stats.ActiveConnections</MudText>
            </MudPaper>
        </MudItem>
        <MudItem xs="12" sm="6" md="4">
            <MudPaper Elevation="2" Class="pa-4">
                <MudText Typo="Typo.subtitle2" Color="Color.Secondary">Total Documents</MudText>
                <MudText Typo="Typo.h5">@_stats.TotalDocuments.ToString("N0")</MudText>
            </MudPaper>
        </MudItem>
        <MudItem xs="12" sm="6" md="4">
            <MudPaper Elevation="2" Class="pa-4">
                <MudText Typo="Typo.subtitle2" Color="Color.Secondary">Collections</MudText>
                <MudText Typo="Typo.h5">@_stats.TotalCollections</MudText>
            </MudPaper>
        </MudItem>
        <MudItem xs="12" sm="6" md="4">
            <MudPaper Elevation="2" Class="pa-4">
                <MudText Typo="Typo.subtitle2" Color="Color.Secondary">Memory Usage</MudText>
                <MudText Typo="Typo.h5">@_stats.MemoryUsageMB MB</MudText>
            </MudPaper>
        </MudItem>
    </MudGrid>
}

@if (_error != null)
{
    <MudAlert Severity="Severity.Error" Class="mt-4">@_error</MudAlert>
}

@code {
    private ServerStats? _stats;
    private string? _error;
    private PeriodicTimer? _timer;
    private Task? _timerTask;

    protected override async Task OnInitializedAsync()
    {
        if (!AdminService.IsConnected)
        {
            Navigation.NavigateTo("/login");
            return;
        }
        await LoadStatsAsync();
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        _timerTask = RunAutoRefreshAsync();
    }

    private async Task LoadStatsAsync()
    {
        try
        {
            _stats = await AdminService.GetStatsAsync();
            _error = null;
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
    }

    private async Task RunAutoRefreshAsync()
    {
        try
        {
            while (_timer != null && await _timer.WaitForNextTickAsync())
            {
                await LoadStatsAsync();
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException) { }
    }

    private static string FormatUptime(long seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalDays >= 1) return $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m";
        if (ts.TotalHours >= 1) return $"{ts.Hours}h {ts.Minutes}m";
        return $"{ts.Minutes}m {ts.Seconds}s";
    }

    public async ValueTask DisposeAsync()
    {
        _timer?.Dispose();
        _timer = null;
        if (_timerTask != null) await _timerTask.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
    }
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build AdvGenNoSqlServer.AdminClient/AdvGenNoSqlServer.AdminClient.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add AdvGenNoSqlServer.AdminClient/Pages/Index.razor
git commit -m "feat: add Dashboard page with auto-refresh stats"
```

---

## Task 8: Create Collections page

**Files:**
- Create: `AdvGenNoSqlServer.AdminClient/Pages/Collections.razor`

- [ ] **Step 1: Create Collections.razor**

Create `AdvGenNoSqlServer.AdminClient/Pages/Collections.razor`:

```razor
@page "/collections"
@inject TcpAdminService AdminService
@inject NavigationManager Navigation
@inject ISnackbar Snackbar
@inject IDialogService DialogService

<PageTitle>Collections - AdvGenNoSQL Admin</PageTitle>

<div class="d-flex align-center mb-4">
    <MudText Typo="Typo.h4" Class="flex-grow-1">Collections</MudText>
    <MudButton Variant="Variant.Filled" Color="Color.Primary" StartIcon="@Icons.Material.Filled.Add"
               OnClick="CreateCollection">New Collection</MudButton>
</div>

@if (_loading)
{
    <MudProgressCircular Indeterminate="true" />
}
else if (_collections.Count == 0)
{
    <MudText>No collections found.</MudText>
}
else
{
    <MudTable Items="_collections" Hover="true" Dense="true">
        <HeaderContent>
            <MudTh>Name</MudTh>
            <MudTh>Documents</MudTh>
            <MudTh>Actions</MudTh>
        </HeaderContent>
        <RowTemplate>
            <MudTd>
                <MudLink Href="@($"/documents?collection={Uri.EscapeDataString(context)}")">@context</MudLink>
            </MudTd>
            <MudTd>@(_counts.TryGetValue(context, out var c) ? c.ToString("N0") : "...")</MudTd>
            <MudTd>
                <MudIconButton Icon="@Icons.Material.Filled.Delete" Color="Color.Error" Size="Size.Small"
                               OnClick="@(() => DeleteCollection(context))" />
            </MudTd>
        </RowTemplate>
    </MudTable>
}

@code {
    private List<string> _collections = new();
    private Dictionary<string, long> _counts = new();
    private bool _loading = true;

    protected override async Task OnInitializedAsync()
    {
        if (!AdminService.IsConnected)
        {
            Navigation.NavigateTo("/login");
            return;
        }
        await LoadCollectionsAsync();
    }

    private async Task LoadCollectionsAsync()
    {
        _loading = true;
        try
        {
            _collections = await AdminService.GetCollectionsAsync();
            _counts.Clear();
            foreach (var col in _collections)
            {
                try { _counts[col] = await AdminService.CountAsync(col); }
                catch { _counts[col] = -1; }
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Failed to load collections: {ex.Message}", Severity.Error);
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task CreateCollection()
    {
        var parameters = new DialogParameters<TextInputDialog>
        {
            { x => x.Title, "Create Collection" },
            { x => x.Message, "Enter the collection name:" },
            { x => x.Label, "Collection Name" }
        };
        var dialog = await DialogService.ShowAsync<TextInputDialog>("Create Collection", parameters);
        var result = await dialog.Result;
        if (result?.Canceled == false && result.Data is string name && !string.IsNullOrWhiteSpace(name))
        {
            try
            {
                await AdminService.CreateCollectionAsync(name);
                Snackbar.Add($"Collection '{name}' created", Severity.Success);
                await LoadCollectionsAsync();
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Failed to create collection: {ex.Message}", Severity.Error);
            }
        }
    }

    private async Task DeleteCollection(string name)
    {
        var parameters = new DialogParameters<ConfirmDialog>
        {
            { x => x.Title, "Delete Collection" },
            { x => x.Message, $"Delete collection '{name}' and all its documents?" },
            { x => x.ConfirmText, "Delete" }
        };
        var dialog = await DialogService.ShowAsync<ConfirmDialog>("Delete Collection", parameters);
        var result = await dialog.Result;
        if (result?.Canceled == false)
        {
            try
            {
                await AdminService.DeleteCollectionAsync(name);
                Snackbar.Add($"Collection '{name}' deleted", Severity.Success);
                await LoadCollectionsAsync();
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Failed to delete collection: {ex.Message}", Severity.Error);
            }
        }
    }
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build AdvGenNoSqlServer.AdminClient/AdvGenNoSqlServer.AdminClient.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add AdvGenNoSqlServer.AdminClient/Pages/Collections.razor
git commit -m "feat: add Collections page"
```

---

## Task 9: Create Documents page

**Files:**
- Create: `AdvGenNoSqlServer.AdminClient/Pages/Documents.razor`

- [ ] **Step 1: Create Documents.razor**

Create `AdvGenNoSqlServer.AdminClient/Pages/Documents.razor`:

```razor
@page "/documents"
@inject TcpAdminService AdminService
@inject NavigationManager Navigation
@inject ISnackbar Snackbar
@inject IDialogService DialogService

<PageTitle>Documents - AdvGenNoSQL Admin</PageTitle>

<div class="d-flex align-center mb-4">
    <MudText Typo="Typo.h4" Class="flex-grow-1">@_collection</MudText>
    <MudButton Variant="Variant.Filled" Color="Color.Primary" StartIcon="@Icons.Material.Filled.Add"
               OnClick="InsertDocument">Insert</MudButton>
</div>

@if (_loading)
{
    <MudProgressCircular Indeterminate="true" />
}
else if (_documents.Count == 0)
{
    <MudText>No documents found.</MudText>
}
else
{
    <MudTable Items="_documents" Hover="true" Dense="true">
        <HeaderContent>
            <MudTh>ID</MudTh>
            <MudTh>Preview</MudTh>
            <MudTh>Actions</MudTh>
        </HeaderContent>
        <RowTemplate>
            <MudTd>@GetId(context)</MudTd>
            <MudTd Style="max-width:400px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">
                @GetPreview(context)
            </MudTd>
            <MudTd>
                <MudIconButton Icon="@Icons.Material.Filled.Visibility" Size="Size.Small"
                               OnClick="@(() => ViewDocument(context))" Title="View" />
                <MudIconButton Icon="@Icons.Material.Filled.Edit" Size="Size.Small"
                               OnClick="@(() => EditDocument(context))" Title="Edit" />
                <MudIconButton Icon="@Icons.Material.Filled.Delete" Color="Color.Error" Size="Size.Small"
                               OnClick="@(() => DeleteDocument(context))" Title="Delete" />
            </MudTd>
        </RowTemplate>
    </MudTable>

    <div class="d-flex align-center mt-4">
        <MudButton Disabled="_page == 0" OnClick="PreviousPage" Variant="Variant.Outlined">Previous</MudButton>
        <MudText Class="mx-4">Page @(_page + 1) — @_total total</MudText>
        <MudButton Disabled="(_page + 1) * _pageSize >= _total" OnClick="NextPage" Variant="Variant.Outlined">Next</MudButton>
    </div>
}

@code {
    [SupplyParameterFromQuery(Name = "collection")]
    public string? CollectionParam { get; set; }

    private string _collection = "";
    private List<Dictionary<string, object>> _documents = new();
    private long _total;
    private int _page;
    private const int _pageSize = 50;
    private bool _loading = true;

    protected override async Task OnInitializedAsync()
    {
        if (!AdminService.IsConnected)
        {
            Navigation.NavigateTo("/login");
            return;
        }
        _collection = CollectionParam ?? "";
        if (string.IsNullOrEmpty(_collection))
        {
            Navigation.NavigateTo("/collections");
            return;
        }
        await LoadPageAsync();
    }

    private async Task LoadPageAsync()
    {
        _loading = true;
        try
        {
            var (docs, total) = await AdminService.GetDocumentsAsync(_collection, _page * _pageSize, _pageSize);
            _documents = docs;
            _total = total;
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Failed to load documents: {ex.Message}", Severity.Error);
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task PreviousPage() { _page--; await LoadPageAsync(); }
    private async Task NextPage() { _page++; await LoadPageAsync(); }

    private static string GetId(Dictionary<string, object> doc)
        => doc.TryGetValue("_id", out var id) ? id?.ToString() ?? "" : "";

    private static string GetPreview(Dictionary<string, object> doc)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(doc);
        return json.Length > 120 ? json[..120] + "…" : json;
    }

    private async Task ViewDocument(Dictionary<string, object> doc)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(doc, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        var parameters = new DialogParameters<TextInputDialog>
        {
            { x => x.Title, "View Document" },
            { x => x.Message, json },
            { x => x.Label, "JSON" }
        };
        await DialogService.ShowAsync<ConfirmDialog>("Document", new DialogParameters<ConfirmDialog>
        {
            { x => x.Title, "View Document" },
            { x => x.Message, json },
            { x => x.ConfirmText, "Close" },
            { x => x.CancelText, "" }
        });
    }

    private async Task InsertDocument()
    {
        var parameters = new DialogParameters<TextInputDialog>
        {
            { x => x.Title, "Insert Document" },
            { x => x.Message, "Enter JSON document (without _id — server assigns one):" },
            { x => x.Label, "JSON" }
        };
        var dialog = await DialogService.ShowAsync<TextInputDialog>("Insert Document", parameters);
        var result = await dialog.Result;
        if (result?.Canceled == false && result.Data is string json && !string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var doc = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json)
                    ?? throw new InvalidOperationException("Invalid JSON");
                var id = await AdminService.UpsertDocumentAsync(_collection, doc);
                Snackbar.Add($"Document inserted with ID: {id}", Severity.Success);
                await LoadPageAsync();
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Failed to insert document: {ex.Message}", Severity.Error);
            }
        }
    }

    private async Task EditDocument(Dictionary<string, object> doc)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(doc, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        var parameters = new DialogParameters<TextInputDialog>
        {
            { x => x.Title, "Edit Document" },
            { x => x.Message, "Edit JSON (keep _id to update this document):" },
            { x => x.Label, "JSON" }
        };
        var dialog = await DialogService.ShowAsync<TextInputDialog>("Edit Document", parameters);
        var result = await dialog.Result;
        if (result?.Canceled == false && result.Data is string updatedJson && !string.IsNullOrWhiteSpace(updatedJson))
        {
            try
            {
                var updatedDoc = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(updatedJson)
                    ?? throw new InvalidOperationException("Invalid JSON");
                await AdminService.UpsertDocumentAsync(_collection, updatedDoc);
                Snackbar.Add("Document updated", Severity.Success);
                await LoadPageAsync();
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Failed to update document: {ex.Message}", Severity.Error);
            }
        }
    }

    private async Task DeleteDocument(Dictionary<string, object> doc)
    {
        var id = GetId(doc);
        var parameters = new DialogParameters<ConfirmDialog>
        {
            { x => x.Message, $"Delete document '{id}'?" },
            { x => x.ConfirmText, "Delete" }
        };
        var dialog = await DialogService.ShowAsync<ConfirmDialog>("Delete Document", parameters);
        var result = await dialog.Result;
        if (result?.Canceled == false)
        {
            try
            {
                await AdminService.DeleteDocumentAsync(_collection, id);
                Snackbar.Add("Document deleted", Severity.Success);
                await LoadPageAsync();
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Failed to delete document: {ex.Message}", Severity.Error);
            }
        }
    }
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build AdvGenNoSqlServer.AdminClient/AdvGenNoSqlServer.AdminClient.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add AdvGenNoSqlServer.AdminClient/Pages/Documents.razor
git commit -m "feat: add Documents page with paginated list, insert, edit, delete"
```

---

## Task 10: Create Query page

**Files:**
- Create: `AdvGenNoSqlServer.AdminClient/Pages/Query.razor`

- [ ] **Step 1: Create Query.razor**

Create `AdvGenNoSqlServer.AdminClient/Pages/Query.razor`:

```razor
@page "/query"
@inject TcpAdminService AdminService
@inject NavigationManager Navigation
@inject ISnackbar Snackbar

<PageTitle>Query - AdvGenNoSQL Admin</PageTitle>

<MudText Typo="Typo.h4" Class="mb-4">Query</MudText>

<MudTextField T="string" @bind-Value="_query" Label="Query" Variant="Variant.Outlined"
              Lines="5" Class="mb-3" Placeholder="SELECT * FROM myCollection WHERE ..." />

<MudButton Variant="Variant.Filled" Color="Color.Primary" OnClick="RunQuery"
           Disabled="_running" StartIcon="@Icons.Material.Filled.PlayArrow">
    @(_running ? "Running..." : "Run Query")
</MudButton>

@if (_result != null)
{
    <MudPaper Elevation="1" Class="pa-4 mt-4" Style="background:#1e1e1e;">
        <MudText Typo="Typo.body2" Style="font-family:monospace;white-space:pre-wrap;color:#d4d4d4;">@_result</MudText>
    </MudPaper>
}

@code {
    private string _query = "";
    private string? _result;
    private bool _running;

    protected override async Task OnInitializedAsync()
    {
        if (!AdminService.IsConnected)
        {
            Navigation.NavigateTo("/login");
            return;
        }
        await Task.CompletedTask;
    }

    private async Task RunQuery()
    {
        if (string.IsNullOrWhiteSpace(_query)) return;
        _running = true;
        _result = null;
        try
        {
            var response = await AdminService.ExecuteQueryAsync(_query);
            _result = System.Text.Json.JsonSerializer.Serialize(response.Data,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            _result = $"Error: {ex.Message}";
            Snackbar.Add(ex.Message, Severity.Error);
        }
        finally
        {
            _running = false;
        }
    }
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build AdvGenNoSqlServer.AdminClient/AdvGenNoSqlServer.AdminClient.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add AdvGenNoSqlServer.AdminClient/Pages/Query.razor
git commit -m "feat: add Query page"
```

---

## Task 11: SSL configuration and .gitignore

**Files:**
- Modify: `AdvGenNoSqlServer.Host/appsettings.json`
- Modify: `.gitignore`

The TCP admin client always connects with SSL. The Host must be configured to accept SSL connections. The dev cert is exported once and never committed.

- [ ] **Step 1: Export the dev cert (run once)**

```bash
mkdir -p AdvGenNoSqlServer.Host/certs
dotnet dev-certs https -ep AdvGenNoSqlServer.Host/certs/advgen.pfx -p devpassword
```

Expected: `The HTTPS developer certificate was exported to file ...`
If it says "No valid certificate found", run `dotnet dev-certs https --trust` first, then retry.

- [ ] **Step 2: Add SSL keys to Host appsettings.json**

In `AdvGenNoSqlServer.Host/appsettings.json`, change only the `EnableSsl` line and add the two new cert lines. Do NOT replace the whole file — only merge these three keys so existing values (MasterPassword, JwtSecretKey, etc.) are not overwritten:

```json
"EnableSsl": true,
"SslCertificatePath": "./certs/advgen.pfx",
"SslCertificatePassword": "devpassword",
```

The result should have `EnableSsl: true` and the two cert keys present alongside all existing keys.

- [ ] **Step 3: Add certs/ to .gitignore**

Open `.gitignore` at the repo root. Add the following lines (create the file if it doesn't exist):

```
# SSL dev certs — never commit private keys
AdvGenNoSqlServer.Host/certs/
```

- [ ] **Step 4: Verify Host builds with SSL config**

```bash
dotnet build AdvGenNoSqlServer.Host/AdvGenNoSqlServer.Host.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit (exclude the .pfx via .gitignore)**

```bash
git add .gitignore AdvGenNoSqlServer.Host/appsettings.json
git commit -m "chore: enable SSL in Host appsettings, exclude certs dir from git"
```

---

## Task 12: Full solution build verification

- [ ] **Step 1: Build entire solution**

```bash
cd e:/Projects/AdvGenNoSQLServer
dotnet build AdvGenNoSqlServer.sln
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Smoke-test startup**

In one terminal, start the Host:

```bash
cd AdvGenNoSqlServer.Host
dotnet run
```

Expected: Server starts, logs `TCP server listening on 0.0.0.0:9191`.

In a second terminal, start the AdminClient:

```bash
cd AdvGenNoSqlServer.AdminClient
dotnet run --launch-profile https
```

Expected: `Now listening on: https://localhost:7210`.

Open `https://localhost:7210/login` in a browser. Enter `localhost`, `9191`, `admin`, `admin123`. Click **Connect**. Should redirect to the Dashboard showing server stats.

- [ ] **Step 3: Verify all pages**

- `/` — Dashboard shows version, uptime, connections, documents, collections, memory
- `/collections` — lists collections (or "No collections found")
- Create a collection via the New Collection button
- `/documents?collection=<name>` — opens from collection link, shows empty or paginated list
- Insert a document `{"name":"test","value":42}` — should succeed and reload list
- `/query` — run `SELECT * FROM <collectionName>` — should return results

- [ ] **Step 4: Commit final state**

```bash
git add .
git commit -m "feat: complete AdvGenNoSqlServer.AdminClient Blazor Server TCP admin UI"
```
