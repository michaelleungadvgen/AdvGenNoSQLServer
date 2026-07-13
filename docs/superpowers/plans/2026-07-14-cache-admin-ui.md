# Cache Admin UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Surface the new KV cache and CacheOnly storage mode in the admin experience: cache stats + storage-mode indicator on the dashboard, and a Cache browser page (search keys, view values/TTL, set, delete, expire, flush).

**Architecture:** All UI work happens in `AdvGenNoSqlServer.AdminClient` (Blazor Server + MudBlazor), which talks TCP through `TcpAdminService` wrapping `AdvGenNoSqlClient`. Since the main cache plan gives the client a `client.Cache.*` API, the admin needs only thin service wrappers plus Razor pages. One small server change is required: the `stats` command must report `storageMode` and cache statistics.

**Tech Stack:** Blazor Server, MudBlazor (already used by every AdminClient page), `AdvGenNoSqlClient.Cache` (from the main plan), xUnit for the server-side change.

---

## PREREQUISITE

This plan builds on `docs/superpowers/plans/2026-07-14-cache-only-mode.md` — **implement that plan first.** It provides `CacheStore`, `client.Cache.*`, `ServerConfiguration.StorageMode`, and the `_cacheStore` field in `NoSqlServer`.

## Gap Analysis (what's missing today)

| Gap | Where |
|---|---|
| `stats` command reports no `storageMode` or cache stats | `AdvGenNoSqlServer.Server/NoSqlServer.cs` `HandleStatsCommand` (~line 775) |
| `ServerStats` model has no storage-mode / cache fields | `AdvGenNoSqlServer.AdminClient/Services/TcpAdminService.cs` |
| `TcpAdminService` has no cache operations | same file |
| Dashboard shows no storage mode or cache health | `AdvGenNoSqlServer.AdminClient/Pages/Index.razor` |
| No cache browser page at all | `AdvGenNoSqlServer.AdminClient/Pages/` |
| No nav entry for cache | `AdvGenNoSqlServer.AdminClient/Shared/NavMenu.razor` |

The older Blazor WASM admin (`AdvGenNoSqlServer.Admin`, REST-based) is intentionally **out of scope** — the KV cache is TCP-only per the approved spec (HTTP endpoints are a listed non-goal), and the AdminClient is the actively developed UI.

The AdminClient has no test infrastructure (no bUnit); Razor pages are verified by running the app (Task 5). The server-side stats change gets a real xUnit test.

---

### Task 1: Server — report storageMode and cache stats in `stats`

**Files:**
- Modify: `AdvGenNoSqlServer.Server/NoSqlServer.cs` (`HandleStatsCommand`, ~line 775; `StartAsync`)
- Test: `AdvGenNoSqlServer.Tests/StatsCommandCacheTests.cs` (new)

- [ ] **Step 1.1: Write failing test** (reuse the reflection helper pattern from `ClusterCommandTests` / the main plan's `CacheOperationHandlerTests` to invoke the private `HandleMessageAsync`)

```csharp
// AdvGenNoSqlServer.Tests/StatsCommandCacheTests.cs
using AdvGenNoSqlServer.Core.Configuration;
using AdvGenNoSqlServer.Network;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;
using ServerNoSql = AdvGenNoSqlServer.Server.NoSqlServer;

namespace AdvGenNoSqlServer.Tests;

public class StatsCommandCacheTests
{
    private static ServerNoSql CreateServer(string storageMode, int port)
        => CreateServer(storageMode, port, out _);

    private static ServerNoSql CreateServer(string storageMode, int port, out string storagePath)
    {
        storagePath = Path.Combine(Path.GetTempPath(), "advgen-stats-test-" + Guid.NewGuid().ToString("N"));
        var config = new ServerConfiguration
        {
            Host = "127.0.0.1", Port = port,
            StorageMode = storageMode,
            StoragePath = storagePath,
            RequireAuthentication = false,
            MemoryManagement = new() { Plan = "Managed", MaxMemoryMB = 64, MaxMemoryPercent = 0 }
        };
        var configManager = new Mock<IConfigurationManager>();
        configManager.Setup(c => c.Configuration).Returns(config);
        return new ServerNoSql(new Mock<ILogger<ServerNoSql>>().Object, configManager.Object,
            new AdvGenNoSqlServer.Server.ApiDataService());
    }

    [Fact]
    public async Task Stats_ReportsStorageModeAndCacheStats()
    {
        var server = CreateServer("CacheOnly", 19298);
        await server.StartAsync(CancellationToken.None);
        try
        {
            // Put something in the cache so stats are non-trivial
            var setPayload = CacheProtocol.EncodeRequest(CacheOp.Set, "k", [1], -1, CacheRequestFlags.None);
            var setMsg = new NoSqlMessage { MessageType = MessageType.CacheOperation, Payload = setPayload, PayloadLength = setPayload.Length };
            await server.HandleMessageForTestsAsync(setMsg, "t"); // same helper as CacheOperationHandlerTests

            var stats = NoSqlMessage.Create(MessageType.Command, """{"command":"stats"}""");
            var response = await server.HandleMessageForTestsAsync(stats, "t");

            using var doc = JsonDocument.Parse(response.GetPayloadAsString());
            var data = doc.RootElement.GetProperty("data");
            Assert.Equal("CacheOnly", data.GetProperty("storageMode").GetString());
            var cache = data.GetProperty("cache");
            Assert.Equal("Managed", cache.GetProperty("plan").GetString());
            Assert.Equal(1, cache.GetProperty("entryCount").GetInt64());
            Assert.True(cache.GetProperty("limitBytes").GetInt64() > 0);
            // hitCount/missCount/evictionCount/usedBytes must be present
            Assert.True(cache.TryGetProperty("hitCount", out _));
            Assert.True(cache.TryGetProperty("missCount", out _));
            Assert.True(cache.TryGetProperty("evictionCount", out _));
            Assert.True(cache.TryGetProperty("usedBytes", out _));
        }
        finally { await server.DisposeAsync(); }
    }

    [Fact]
    public async Task Stats_HybridMode_ReportsHybrid()
    {
        var server = CreateServer("Hybrid", 19299, out var storagePath);
        await server.StartAsync(CancellationToken.None);
        try
        {
            var stats = NoSqlMessage.Create(MessageType.Command, """{"command":"stats"}""");
            var response = await server.HandleMessageForTestsAsync(stats, "t");
            using var doc = JsonDocument.Parse(response.GetPayloadAsString());
            Assert.Equal("Hybrid", doc.RootElement.GetProperty("data").GetProperty("storageMode").GetString());
        }
        finally
        {
            await server.DisposeAsync();
            if (Directory.Exists(storagePath)) Directory.Delete(storagePath, recursive: true);
        }
    }
}
```

- [ ] **Step 1.2: Run, verify failure** (`storageMode` property missing from stats response)

Run: `dotnet test AdvGenNoSqlServer.Tests/AdvGenNoSqlServer.Tests.csproj -c Release --filter "FullyQualifiedName~StatsCommandCacheTests"`

- [ ] **Step 1.3: Implement**

In `NoSqlServer`, store the resolved mode: add a field `private string _storageMode = "Hybrid";` and in `StartAsync` set `_storageMode = storageMode;` right after the mode is resolved (the main plan's Task 6 already computes a local `storageMode` variable).

In `HandleStatsCommand`, extend the anonymous response object:

```csharp
            var cacheStats = _cacheStore?.GetStats();

            return NoSqlMessage.CreateSuccess(new
            {
                version = ServerVersion,
                uptimeSeconds = (long)uptime.TotalSeconds,
                memoryUsageMB = memoryMB,
                totalDocuments,
                totalCollections,
                activeConnections,
                storageMode = _storageMode,
                cache = cacheStats == null ? null : new
                {
                    plan = cacheStats.Plan,
                    entryCount = cacheStats.EntryCount,
                    usedBytes = cacheStats.UsedBytes,
                    limitBytes = cacheStats.LimitBytes,
                    hitCount = cacheStats.HitCount,
                    missCount = cacheStats.MissCount,
                    evictionCount = cacheStats.EvictionCount
                }
            });
```

(Check `MemoryEngineStats` member names in `AdvGenNoSqlServer.Core/MemoryManagement/MemoryEngineStats.cs` and match them exactly — the main plan uses `Plan/EntryCount/UsedBytes/LimitBytes/HitCount/MissCount/EvictionCount`.)

- [ ] **Step 1.4: Run tests, verify pass** — including the existing stats-related tests (`--filter "FullyQualifiedName~Stats"`) since the response gained fields (additive, should not break consumers that read named properties).

- [ ] **Step 1.5: Commit**

```bash
git add AdvGenNoSqlServer.Server/NoSqlServer.cs AdvGenNoSqlServer.Tests/StatsCommandCacheTests.cs
git commit -m "feat: report storageMode and cache stats in stats command"
```

---

### Task 2: TcpAdminService — cache operations + extended ServerStats

**Files:**
- Modify: `AdvGenNoSqlServer.AdminClient/Services/TcpAdminService.cs`

No test project covers AdminClient; correctness rides on the main plan's `CacheClientTests` (these wrappers are one-liners over `client.Cache`). Keep the wrappers thin so that stays true.

- [ ] **Step 2.1: Extend `ServerStats`** (the model class lives at the bottom of `TcpAdminService.cs` — verify location first). Add:

```csharp
    public string StorageMode { get; set; } = "Hybrid";
    public CacheStatsInfo? Cache { get; set; }
```

and a new model:

```csharp
public class CacheStatsInfo
{
    public string Plan { get; set; } = "";
    public long EntryCount { get; set; }
    public long UsedBytes { get; set; }
    public long LimitBytes { get; set; }
    public long HitCount { get; set; }
    public long MissCount { get; set; }
    public long EvictionCount { get; set; }
    public double HitRatePercent => HitCount + MissCount == 0 ? 0
        : 100.0 * HitCount / (HitCount + MissCount);
}

public class CacheKeyInfo
{
    public string Key { get; set; } = "";
    public TimeSpan? Ttl { get; set; }     // null = no expiry
    public int SizeBytes { get; set; }
}
```

- [ ] **Step 2.2: Parse the new stats fields in `GetStatsAsync`** (defensive — old servers won't send them):

```csharp
        if (data.TryGetProperty("storageMode", out var modeProp))
            stats.StorageMode = modeProp.GetString() ?? "Hybrid";
        if (data.TryGetProperty("cache", out var cacheProp) && cacheProp.ValueKind == JsonValueKind.Object)
        {
            stats.Cache = new CacheStatsInfo
            {
                Plan = cacheProp.GetProperty("plan").GetString() ?? "",
                EntryCount = cacheProp.GetProperty("entryCount").GetInt64(),
                UsedBytes = cacheProp.GetProperty("usedBytes").GetInt64(),
                LimitBytes = cacheProp.GetProperty("limitBytes").GetInt64(),
                HitCount = cacheProp.GetProperty("hitCount").GetInt64(),
                MissCount = cacheProp.GetProperty("missCount").GetInt64(),
                EvictionCount = cacheProp.GetProperty("evictionCount").GetInt64()
            };
        }
```

(Refactor `GetStatsAsync` to build a `stats` local first if it currently uses an object initializer.)

- [ ] **Step 2.3: Add cache methods** to `TcpAdminService` (all follow the existing `EnsureConnected()` pattern):

```csharp
    private const int MaxBrowseKeys = 500;

    public async Task<List<CacheKeyInfo>> SearchCacheKeysAsync(string pattern)
    {
        EnsureConnected();
        if (string.IsNullOrWhiteSpace(pattern)) pattern = "*";
        var keys = await _client!.Cache.ScanAsync(pattern, pageSize: 100);
        var result = new List<CacheKeyInfo>(Math.Min(keys.Count, MaxBrowseKeys));
        foreach (var key in keys.Take(MaxBrowseKeys))
        {
            TimeSpan? ttl = null;
            int size = 0;
            try
            {
                ttl = await _client.Cache.TtlAsync(key);
                size = (await _client.Cache.GetAsync(key))?.Length ?? 0;
            }
            catch (CacheKeyNotFoundException) { continue; } // expired between scan and read
            result.Add(new CacheKeyInfo { Key = key, Ttl = ttl, SizeBytes = size });
        }
        return result;
    }

    public async Task<(byte[]? Value, TimeSpan? Ttl)> GetCacheValueAsync(string key)
    {
        EnsureConnected();
        var value = await _client!.Cache.GetAsync(key);
        TimeSpan? ttl = null;
        if (value != null)
        {
            try { ttl = await _client.Cache.TtlAsync(key); }
            catch (CacheKeyNotFoundException) { }
        }
        return (value, ttl);
    }

    public async Task SetCacheValueAsync(string key, string utf8Value, TimeSpan? ttl)
    {
        EnsureConnected();
        await _client!.Cache.SetStringAsync(key, utf8Value, ttl);
    }

    public async Task<bool> DeleteCacheKeyAsync(string key)
    {
        EnsureConnected();
        return await _client!.Cache.DeleteAsync(key);
    }

    public async Task<bool> ExpireCacheKeyAsync(string key, TimeSpan ttl)
    {
        EnsureConnected();
        return await _client!.Cache.ExpireAsync(key, ttl);
    }

    public async Task FlushCacheAsync()
    {
        EnsureConnected();
        await _client!.Cache.FlushAsync();
    }
```

Add `using AdvGenNoSqlServer.Client;` if `CacheKeyNotFoundException` isn't resolved.

- [ ] **Step 2.4: Build**

Run: `dotnet build AdvGenNoSqlServer.AdminClient/AdvGenNoSqlServer.AdminClient.csproj -c Release`
Expected: clean build.

- [ ] **Step 2.5: Commit**

```bash
git add AdvGenNoSqlServer.AdminClient/Services/TcpAdminService.cs
git commit -m "feat: add cache operations and cache stats to TcpAdminService"
```

---

### Task 3: Dashboard — storage mode + cache tiles

**Files:**
- Modify: `AdvGenNoSqlServer.AdminClient/Pages/Index.razor`

- [ ] **Step 3.1: Add a CacheOnly warning banner** right under the `<MudText Typo="Typo.h4">` heading (renders only when stats are loaded):

```razor
@if (_stats is { StorageMode: "CacheOnly" })
{
    <MudAlert Severity="Severity.Warning" Class="mb-4">
        Server is running in <b>Cache-Only mode</b> — all data is held in memory and will be lost on restart.
    </MudAlert>
}
```

- [ ] **Step 3.2: Add tiles to the existing `<MudGrid>`** (same `MudItem`/`MudPaper` pattern as the six existing tiles):

```razor
        <MudItem xs="12" sm="6" md="4">
            <MudPaper Elevation="2" Class="pa-4">
                <MudText Typo="Typo.subtitle2" Color="Color.Secondary">Storage Mode</MudText>
                <MudText Typo="Typo.h5">
                    <MudChip T="string" Color="@(_stats.StorageMode == "CacheOnly" ? Color.Warning : Color.Success)" Size="Size.Medium">
                        @_stats.StorageMode
                    </MudChip>
                </MudText>
            </MudPaper>
        </MudItem>
        @if (_stats.Cache != null)
        {
            <MudItem xs="12" sm="6" md="4">
                <MudPaper Elevation="2" Class="pa-4">
                    <MudText Typo="Typo.subtitle2" Color="Color.Secondary">Cache Entries (@_stats.Cache.Plan)</MudText>
                    <MudText Typo="Typo.h5">@_stats.Cache.EntryCount.ToString("N0")</MudText>
                </MudPaper>
            </MudItem>
            <MudItem xs="12" sm="6" md="4">
                <MudPaper Elevation="2" Class="pa-4">
                    <MudText Typo="Typo.subtitle2" Color="Color.Secondary">Cache Memory</MudText>
                    <MudText Typo="Typo.h5">@FormatBytes(_stats.Cache.UsedBytes) / @FormatBytes(_stats.Cache.LimitBytes)</MudText>
                    <MudProgressLinear Value="@(_stats.Cache.LimitBytes > 0 ? 100.0 * _stats.Cache.UsedBytes / _stats.Cache.LimitBytes : 0)" Color="Color.Info" Class="mt-2" />
                </MudPaper>
            </MudItem>
            <MudItem xs="12" sm="6" md="4">
                <MudPaper Elevation="2" Class="pa-4">
                    <MudText Typo="Typo.subtitle2" Color="Color.Secondary">Cache Hit Rate</MudText>
                    <MudText Typo="Typo.h5">@_stats.Cache.HitRatePercent.ToString("F1")%</MudText>
                    <MudText Typo="Typo.caption">@_stats.Cache.EvictionCount.ToString("N0") evictions</MudText>
                </MudPaper>
            </MudItem>
        }
```

Add the helper to the `@code` block:

```csharp
    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F1} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }
```

- [ ] **Step 3.3: Build** (`dotnet build AdvGenNoSqlServer.AdminClient -c Release`), expected clean.

- [ ] **Step 3.4: Commit**

```bash
git add AdvGenNoSqlServer.AdminClient/Pages/Index.razor
git commit -m "feat: show storage mode and cache stats on admin dashboard"
```

---

### Task 4: Cache browser page + nav entry

**Files:**
- Create: `AdvGenNoSqlServer.AdminClient/Pages/Cache.razor`
- Modify: `AdvGenNoSqlServer.AdminClient/Shared/NavMenu.razor`

- [ ] **Step 4.1: Add nav entry** after the Query link in `NavMenu.razor` (same markup shape as the existing links):

```razor
    <MudNavLink Href="cache" Match="NavLinkMatch.Prefix" Icon="@Icons.Material.Filled.Memory">
        Cache
    </MudNavLink>
```

- [ ] **Step 4.2: Create `Cache.razor`** — follow the structure of `Documents.razor` for dialogs/snackbar conventions (read it first and mirror its `IDialogService`/`ISnackbar` usage):

```razor
@page "/cache"
@inject TcpAdminService AdminService
@inject NavigationManager Navigation
@inject IDialogService DialogService
@inject ISnackbar Snackbar

<PageTitle>Cache - AdvGenNoSQL Admin</PageTitle>

<MudText Typo="Typo.h4" Class="mb-4">Cache Browser</MudText>

<MudPaper Elevation="2" Class="pa-4 mb-4">
    <MudGrid AlignItems="Center">
        <MudItem xs="12" sm="6">
            <MudTextField @bind-Value="_pattern" Label="Key pattern (glob: * ? [..])" Immediate="true"
                          OnKeyDown="@(async e => { if (e.Key == "Enter") await SearchAsync(); })" />
        </MudItem>
        <MudItem xs="12" sm="6" Class="d-flex gap-2">
            <MudButton Variant="Variant.Filled" Color="Color.Primary" OnClick="SearchAsync"
                       StartIcon="@Icons.Material.Filled.Search" Disabled="_loading">Search</MudButton>
            <MudButton Variant="Variant.Outlined" Color="Color.Primary" OnClick="OpenSetDialogForNewKey"
                       StartIcon="@Icons.Material.Filled.Add">Set Key</MudButton>
            <MudButton Variant="Variant.Outlined" Color="Color.Error" OnClick="FlushAsync"
                       StartIcon="@Icons.Material.Filled.DeleteSweep">Flush All</MudButton>
        </MudItem>
    </MudGrid>
</MudPaper>

@if (_loading)
{
    <MudProgressCircular Indeterminate="true" />
}
else if (_keys != null)
{
    <MudTable Items="_keys" Hover="true" Dense="true">
        <HeaderContent>
            <MudTh>Key</MudTh>
            <MudTh>Size</MudTh>
            <MudTh>TTL</MudTh>
            <MudTh>Actions</MudTh>
        </HeaderContent>
        <RowTemplate>
            <MudTd DataLabel="Key"><code>@context.Key</code></MudTd>
            <MudTd DataLabel="Size">@context.SizeBytes B</MudTd>
            <MudTd DataLabel="TTL">@FormatTtl(context.Ttl)</MudTd>
            <MudTd DataLabel="Actions">
                <MudIconButton Icon="@Icons.Material.Filled.Visibility" Size="Size.Small"
                               OnClick="@(() => ViewValueAsync(context.Key))" title="View value" />
                <MudIconButton Icon="@Icons.Material.Filled.Edit" Size="Size.Small"
                               OnClick="@(() => OpenSetDialogAsync(context.Key))" title="Edit value" />
                <MudIconButton Icon="@Icons.Material.Filled.Timer" Size="Size.Small"
                               OnClick="@(() => SetTtlAsync(context.Key))" title="Set TTL" />
                <MudIconButton Icon="@Icons.Material.Filled.Delete" Size="Size.Small" Color="Color.Error"
                               OnClick="@(() => DeleteAsync(context.Key))" title="Delete" />
            </MudTd>
        </RowTemplate>
        <NoRecordsContent><MudText>No keys match the pattern.</MudText></NoRecordsContent>
    </MudTable>
    @if (_keys.Count >= 500)
    {
        <MudAlert Severity="Severity.Info" Class="mt-2">Showing the first 500 matches — narrow the pattern to see more specific keys.</MudAlert>
    }
}

@if (_error != null)
{
    <MudAlert Severity="Severity.Error" Class="mt-4">@_error</MudAlert>
}

@code {
    private string _pattern = "*";
    private List<CacheKeyInfo>? _keys;
    private bool _loading;
    private string? _error;

    protected override void OnInitialized()
    {
        if (!AdminService.IsConnected)
            Navigation.NavigateTo("/login");
    }

    private async Task SearchAsync()
    {
        _loading = true; _error = null;
        try { _keys = await AdminService.SearchCacheKeysAsync(_pattern); }
        catch (Exception ex) { _error = ex.Message; }
        finally { _loading = false; }
    }

    private async Task ViewValueAsync(string key)
    {
        try
        {
            var (value, ttl) = await AdminService.GetCacheValueAsync(key);
            if (value == null) { Snackbar.Add("Key no longer exists.", Severity.Warning); return; }
            string preview = ToDisplayString(value);
            await DialogService.ShowMessageBox($"Value of '{key}'  (TTL: {FormatTtl(ttl)})", preview);
        }
        catch (Exception ex) { Snackbar.Add(ex.Message, Severity.Error); }
    }

    private Task OpenSetDialogForNewKey() => OpenSetDialogAsync(null);

    private async Task OpenSetDialogAsync(string? key)
    {
        // Uses the existing TextInputDialog pattern twice (key + value) or a small inline dialog;
        // follow whatever Documents.razor does for its create/edit dialogs.
        // Flow: prompt for key (skip if provided) -> prompt for UTF-8 value -> prompt for optional TTL seconds
        // -> AdminService.SetCacheValueAsync(key, value, ttlSeconds > 0 ? TimeSpan.FromSeconds(ttlSeconds) : null)
        // -> Snackbar success -> await SearchAsync()
    }

    private async Task SetTtlAsync(string key)
    {
        // TextInputDialog asking for TTL in seconds; on confirm:
        // await AdminService.ExpireCacheKeyAsync(key, TimeSpan.FromSeconds(seconds)); await SearchAsync();
    }

    private async Task DeleteAsync(string key)
    {
        // ConfirmDialog ("Delete cache key 'x'?"); on confirm:
        // await AdminService.DeleteCacheKeyAsync(key); Snackbar; await SearchAsync();
    }

    private async Task FlushAsync()
    {
        // ConfirmDialog with strong wording ("Delete ALL cache entries? This cannot be undone.");
        // on confirm: await AdminService.FlushCacheAsync(); await SearchAsync();
    }

    private static string FormatTtl(TimeSpan? ttl)
        => ttl == null ? "—" : ttl.Value.TotalHours >= 1
            ? $"{(int)ttl.Value.TotalHours}h {ttl.Value.Minutes}m"
            : ttl.Value.TotalMinutes >= 1 ? $"{(int)ttl.Value.TotalMinutes}m {ttl.Value.Seconds}s"
            : $"{ttl.Value.Seconds}s";

    private static string ToDisplayString(byte[] value)
    {
        // UTF-8 preview when printable; otherwise hex dump. Truncate at 4 KB with a note.
        try
        {
            var s = System.Text.Encoding.UTF8.GetString(value);
            if (!s.Any(c => char.IsControl(c) && c != '\n' && c != '\r' && c != '\t'))
                return s.Length > 4096 ? s[..4096] + $"\n… ({value.Length} bytes total)" : s;
        }
        catch { }
        var hex = Convert.ToHexString(value.AsSpan(0, Math.Min(value.Length, 1024)));
        return $"(binary, {value.Length} bytes)\n{hex}";
    }
}
```

**Implementation notes (not placeholders — do these):** the four `// ...` method bodies must be written by reading `Documents.razor` and reusing its exact `ConfirmDialog`/`TextInputDialog` invocation pattern (`DialogService.ShowAsync<ConfirmDialog>(...)` etc.), so the dialogs look and behave like the rest of the app. Each body is 5–15 lines. The comments above specify the required flow precisely.

- [ ] **Step 4.3: Build** (`dotnet build AdvGenNoSqlServer.AdminClient -c Release`), expected clean.

- [ ] **Step 4.4: Commit**

```bash
git add AdvGenNoSqlServer.AdminClient/Pages/Cache.razor AdvGenNoSqlServer.AdminClient/Shared/NavMenu.razor
git commit -m "feat: add cache browser page to admin client"
```

---

### Task 5: End-to-end verification

No automated UI tests exist for AdminClient; verify by driving the real app (see the AdminClient README for SSL prerequisites — dev cert must be exported to `AdvGenNoSqlServer.Host/certs/advgen.pfx`).

- [ ] **Step 5.1: Start the server in CacheOnly mode**

Temporarily set `"storageMode": "CacheOnly"` in `AdvGenNoSqlServer.Host` appsettings (or the Server appsettings the Host loads), then:

```bash
dotnet run --project AdvGenNoSqlServer.Host/AdvGenNoSqlServer.Host.csproj -c Release
```

Expected log line: `StorageMode=CacheOnly: all data is held in memory only...`

- [ ] **Step 5.2: Start the AdminClient and verify each surface**

```bash
dotnet run --project AdvGenNoSqlServer.AdminClient/AdvGenNoSqlServer.AdminClient.csproj
```

Checklist (login first):
1. Dashboard shows the **CacheOnly** warning banner, Storage Mode chip = CacheOnly (warning color), and three cache tiles with live numbers.
2. Cache page: Set Key → create `test:1` with value `hello` and TTL 300 → appears in search for `test:*` with a ticking TTL.
3. View shows `hello`; Edit changes it; Set TTL updates the TTL column; Delete removes it after confirm.
4. Flush All (confirm dialog) empties the list; dashboard cache entries drop to 0 on next refresh.
5. Restart the Host; documents/collections created earlier are gone (CacheOnly volatility confirmed).
6. Set `storageMode` back to `Hybrid`, restart both; dashboard chip shows Hybrid (success color), cache page still works.

- [ ] **Step 5.3: Revert any appsettings experiment**, run the full test suite one more time, then commit any doc touch-ups:

```bash
dotnet test AdvGenNoSqlServer.Tests/AdvGenNoSqlServer.Tests.csproj -c Release
```

- [ ] **Step 5.4: Use @superpowers:verification-before-completion and @superpowers:finishing-a-development-branch**

---

## Execution notes

- Task 1 is server-side and independent; Tasks 2–4 must run in order (service → dashboard → page).
- Ports 19298–19299 are used by the new test class; the main plan uses 19291–19297.
- Do not add cache endpoints to the old WASM admin (`AdvGenNoSqlServer.Admin`) or the REST API — out of scope per spec non-goals.
