# AdvGenNoSQLServer Admin Client — Design Spec

**Date:** 2026-05-28  
**Status:** Approved  

## Overview

A new `AdvGenNoSqlServer.AdminClient` project — a Blazor Server web application that provides an admin UI for the AdvGenNoSQL server. It lives alongside the existing `AdvGenNoSqlServer.Admin` (Blazor WASM) without replacing it.

The key motivation: Blazor WASM cannot open TCP sockets (browser sandbox), forcing the existing admin through HTTPS REST API with associated complexity (dev cert, JWT validation, DI singleton workarounds). Blazor Server runs as a .NET process; it can use `AdvGenNoSqlServer.Client` (TCP binary protocol) directly, eliminating all of that complexity.

## Architecture

**Project type:** ASP.NET Core Blazor Server (net9.0)  
**Solution:** Added to `AdvGenNoSqlServer.sln`  
**References:** `AdvGenNoSqlServer.Client`, `AdvGenNoSqlServer.Core`, MudBlazor 7.x  

### Two HTTPS surfaces

| Surface | How |
|---|---|
| Browser → Admin app | Kestrel HTTPS, ASP.NET Core dev cert (`dotnet dev-certs https --trust`), fixed port `https://localhost:7210` |
| Admin app → NoSQL server (TCP) | `AdvGenNoSqlClient` with `UseSsl = true` — always required, no toggle |

The admin client always connects to the NoSQL TCP port (default 9191) with SSL. If the server is not configured for SSL the handshake fails and the login page shows the exception message.

## Server Changes Required (Host)

The Host's TCP command handler currently supports: `get`, `set`, `delete`, `exists`, `count`, `listcollections`. Four new commands must be added to `AdvGenNoSqlServer.Host/Program.cs` (`HandleCommandAsync` switch) before the admin client can be fully functional:

| New command | Payload | Server action |
|---|---|---|
| `createcollection` | `{ "collection": "name" }` | `DocumentStore.CreateCollectionAsync(name)` |
| `dropcollection` | `{ "collection": "name" }` | `DocumentStore.DropCollectionAsync(name)` |
| `listdocuments` | `{ "collection": "name", "skip": 0, "take": 50 }` | `DocumentStore.GetAllAsync(collection).Skip(skip).Take(take)` |
| `stats` | `{}` | Returns version, uptimeSeconds, memoryUsageMB, totalDocuments, totalCollections, activeConnections |

These are additions to the existing switch statement only — no changes to other layers.

## Project Structure

```
AdvGenNoSqlServer.AdminClient/
  AdvGenNoSqlServer.AdminClient.csproj
  Program.cs
  appsettings.json
  Properties/
    launchSettings.json               ← https://localhost:7210
  Services/
    TcpAdminService.cs                ← Scoped; owns AdvGenNoSqlClient
  Pages/
    Login.razor                       ← /login
    Index.razor                       ← / (dashboard)
    Collections.razor                 ← /collections
    Documents.razor                   ← /documents?collection=X
    Query.razor                       ← /query
  Shared/
    MainLayout.razor
    NavMenu.razor
    ConfirmDialog.razor
    TextInputDialog.razor
```

## TcpAdminService

**Lifetime:** Scoped (one instance per Blazor Server circuit — one per browser tab).

This is the only service. It owns the `AdvGenNoSqlClient` instance and exposes all server operations.

### Construction

`ConnectAndAuthenticateAsync` creates the client as follows:

```csharp
var options = new AdvGenNoSqlClientOptions
{
    UseSsl = true,
    SslTargetHost = host,           // matches the cert CN; defaults to entered hostname
    CheckCertificateRevocation = false  // dev certs are self-signed, no CRL
};
var client = new AdvGenNoSqlClient($"{host}:{port}", options);
await client.ConnectAsync();        // TCP connect + SSL handshake + protocol handshake
await client.AuthenticateAsync(username, password);
```

`AdvGenNoSqlClientOptions.ServerAddress` defaults to `localhost:9090` but is unused here because the address is passed directly to the constructor as a `"host:port"` string.

### State

| Property | Type | Description |
|---|---|---|
| `IsConnected` | `bool` | TCP socket is open and authenticated |
| `CurrentUser` | `string?` | Authenticated username |
| `Host` | `string` | Connected server host |
| `Port` | `int` | Connected server TCP port |

### Methods and TCP command mapping

| Method | TCP command / client method | Notes |
|---|---|---|
| `ConnectAndAuthenticateAsync(host, port, username, password)` | `ConnectAsync()` + `AuthenticateAsync()` | See Construction above |
| `DisconnectAsync()` | `DisconnectAsync()` | Resets state |
| `GetStatsAsync()` | `ExecuteCommandAsync("stats", "")` | New server command required |
| `GetCollectionsAsync()` | `ExecuteCommandAsync("listcollections", "")` | Returns collection names |
| `CreateCollectionAsync(name)` | `ExecuteCommandAsync("createcollection", name)` | New server command required |
| `DeleteCollectionAsync(name)` | `ExecuteCommandAsync("dropcollection", name)` | New server command required |
| `GetDocumentsAsync(collection, skip, take)` | `ExecuteCommandAsync("listdocuments", collection, new { skip, take })` | New server command required |
| `GetDocumentAsync(collection, id)` | `GetAsync(collection, id)` | Existing typed wrapper |
| `UpsertDocumentAsync(collection, document)` | `SetAsync(collection, document)` | Upsert — insert and update both call this |
| `DeleteDocumentAsync(collection, id)` | `DeleteAsync(collection, id)` | Existing typed wrapper |
| `ExecuteQueryAsync(query)` | `ExecuteQueryAsync(query)` | Raw query passthrough |

**Insert vs Update:** The TCP `set` command is always upsert. The UI presents separate "Insert" (new document, no ID pre-filled) and "Edit" (existing document, ID locked) buttons, but both call `UpsertDocumentAsync`. This is accurate to the server's behaviour.

Implements `IAsyncDisposable` — the TCP socket is closed cleanly when the circuit disposes (browser tab closes).

## Pages

### Login (`/login`)
Fields: Host (default `localhost`), Port (default `9191`), Username, Password.  
On submit: calls `TcpAdminService.ConnectAndAuthenticateAsync`. On success, navigates to `/`. On failure, shows the exception message as a snackbar error (e.g. "SSL handshake failed" if the server has `EnableSsl: false`).  
Redirects to `/` immediately in `OnInitializedAsync` if already connected.

### Dashboard (`/`)
Calls `GetStatsAsync()` on load. Shows: version, uptime, active connections, total documents, total collections, memory usage.  
Auto-refreshes every 30 seconds using a `PeriodicTimer` in an async loop, calling `InvokeAsync(StateHasChanged)` after each tick.  
Auth guard: `OnInitializedAsync` calls `NavigationManager.NavigateTo("/login")` if `!TcpAdminService.IsConnected`.

### Collections (`/collections`)
Lists all collections with document counts (one `count` command per collection). Create button → `TextInputDialog` → `CreateCollectionAsync`. Delete button → `ConfirmDialog` → `DeleteCollectionAsync`. Collection name links to `/documents?collection=X`.  
Auth guard applied in `OnInitializedAsync`.

### Documents (`/documents?collection=X`)
Paginated table (50 per page). Shows document ID and a JSON preview. Per-row buttons: View (JSON in a MudDialog), Edit (JSON editor in a MudDialog, calls `UpsertDocumentAsync`), Delete (`ConfirmDialog`). Plus an Insert button (JSON editor with blank document).  
Auth guard applied in `OnInitializedAsync`.

### Query (`/query`)
Text area for query input, Run button, results rendered as formatted JSON.  
Auth guard applied in `OnInitializedAsync`.

## Auth Guard Pattern

Each page (except Login) guards itself in `OnInitializedAsync`:

```csharp
protected override async Task OnInitializedAsync()
{
    if (!TcpAdminService.IsConnected)
    {
        Navigation.NavigateTo("/login");
        return;
    }
    // ... load page data
}
```

`NavigationManager.NavigateTo` in `OnInitializedAsync` is the correct Blazor Server pattern. Redirecting from `MainLayout.razor` render logic causes a "Cannot redirect while rendering" exception and is not used.

## Server-Side SSL Setup (Host)

For the TCP connection SSL to work, `AdvGenNoSqlServer.Host/appsettings.json` must have:

```json
"EnableSsl": true,
"SslCertificatePath": "./certs/advgen.pfx",
"SslCertificatePassword": "devpassword"
```

Export the dev cert once:
```bash
dotnet dev-certs https -ep AdvGenNoSqlServer.Host/certs/advgen.pfx -p devpassword
```

The server must be **restarted** after adding these keys. If `EnableSsl` remains `false`, the server accepts plain TCP connections and the admin client's SSL handshake will fail with a clear exception on the login page. The `certs/` directory must be added to `.gitignore`.

## What Is Not In Scope

- **User management** — no TCP command exists for creating/deleting users
- **Multi-database switching** — no `selectdatabase` TCP command exists in the Host; adding one is out of scope for this project. All operations target the default database.
- **Replacing `AdvGenNoSqlServer.Admin`** — both projects coexist

## Dependencies

| Package | Version |
|---|---|
| MudBlazor | 7.x (match existing admin) |
| Microsoft.AspNetCore.Components | net9.0 (framework) |

No new NuGet packages beyond MudBlazor — `AdvGenNoSqlServer.Client` and `AdvGenNoSqlServer.Core` are project references.
