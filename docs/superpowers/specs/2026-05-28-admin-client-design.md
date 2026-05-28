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

The admin client always connects to the NoSQL TCP port (default 9191) with SSL. If the server is not configured for SSL the handshake fails and the login page shows a clear error.

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
    AuthGuard.razor                   ← redirects to /login if not authenticated
    ConfirmDialog.razor
    TextInputDialog.razor
```

## TcpAdminService

**Lifetime:** Scoped (one instance per Blazor Server circuit — one per browser tab).

This is the only service. It owns the `AdvGenNoSqlClient` instance and exposes all server operations.

### State
| Property | Type | Description |
|---|---|---|
| `IsConnected` | `bool` | TCP socket is open and authenticated |
| `CurrentUser` | `string?` | Authenticated username |
| `Host` | `string` | Connected server host |
| `Port` | `int` | Connected server TCP port |

### Key methods
| Method | Description |
|---|---|
| `ConnectAndAuthenticateAsync(host, port, username, password)` | Creates `AdvGenNoSqlClient` with `UseSsl=true`, connects, handshakes, authenticates in one call |
| `DisconnectAsync()` | Closes the TCP connection and resets state |
| `GetStatsAsync()` | Returns server statistics |
| `GetCollectionsAsync()` | Lists collections |
| `CreateCollectionAsync(name)` | Creates a collection |
| `DeleteCollectionAsync(name)` | Drops a collection |
| `GetDocumentsAsync(collection, skip, take)` | Paginated document fetch |
| `GetDocumentAsync(collection, id)` | Single document fetch |
| `InsertDocumentAsync(collection, document)` | Insert with returned ID |
| `UpdateDocumentAsync(collection, document)` | Update by ID |
| `DeleteDocumentAsync(collection, id)` | Delete by ID |
| `ExecuteQueryAsync(query)` | Run a query string |

Implements `IAsyncDisposable` — the TCP socket is closed cleanly when the circuit disposes (browser tab closes).

## Pages

### Login (`/login`)
Fields: Host (default `localhost`), Port (default `9191`), Username, Password.  
On submit: calls `TcpAdminService.ConnectAndAuthenticateAsync`. On success, navigates to `/`. On failure, shows the exception message as a snackbar error.  
Redirects to `/` if already authenticated.

### Dashboard (`/`)
Shows server stats: version, uptime, active connections, total documents, total collections, memory usage. Auto-refreshes every 30 seconds via a timer.

### Collections (`/collections`)
Lists all collections with document counts. Buttons: Create (TextInputDialog for name), Delete (ConfirmDialog). Clicking a collection name navigates to `/documents?collection=X`.

### Documents (`/documents?collection=X`)
Paginated table (50 per page). Buttons per row: View (DocumentViewDialog), Edit (inline JSON editor), Delete (ConfirmDialog). Plus an Insert button.

### Query (`/query`)
Text area for query input, Run button, results rendered as a JSON table.

## AuthGuard

A Blazor component that wraps page content. If `TcpAdminService.IsConnected` is false, it immediately redirects to `/login`. Applied in `MainLayout.razor` so all pages except Login are guarded.

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

This is documented in the README. The `certs/` directory is added to `.gitignore`.

## What Is Not In Scope

- User management (create/delete users) — not supported by the TCP protocol
- Multi-database switching — the TCP protocol targets a single store; database selection is a Host HTTP API concept
- Replacing `AdvGenNoSqlServer.Admin` — both projects coexist

## Dependencies

| Package | Version |
|---|---|
| MudBlazor | 7.x (match existing admin) |
| Microsoft.AspNetCore.Components | net9.0 (framework) |

No new NuGet packages beyond MudBlazor — `AdvGenNoSqlServer.Client` and `AdvGenNoSqlServer.Core` are project references.
