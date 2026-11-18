# Quick Start Guide

Get AdvGenNoSQL Server running, connect a client, and try the example apps in a few minutes.

> **Security warning:** this project is prototype code intended for development, experimentation,
> and demonstration. Do **not** store sensitive data in it. Review and harden authentication,
> authorization, input validation, and secrets handling before any production use. The demo
> credentials below (`admin` / `admin123`) are for local development only.

## What's in the box

| Component | Project | What it does |
|-----------|---------|--------------|
| Server host | `AdvGenNoSqlServer.Host` | TCP binary protocol + HTTPS REST API, storage, auth/RBAC |
| Client library | `AdvGenNoSqlServer.Client` | .NET TCP client (`AdvGenNoSqlClient`) |
| Embedded DB | `AdvGenNoSqlServer.Embedded` | In-process, single-file LiteDB-style database (no server) |
| Web Admin (WASM) | `AdvGenNoSqlServer.Admin` | Blazor WebAssembly dashboard over REST |
| Admin Client | `AdvGenNoSqlServer.AdminClient` | Blazor Server dashboard over TCP (attachments, command console) |
| Examples | `AdvGenNoSqlServer.Examples.*`, `Example.ConsoleApp` | Console, WPF, and Blazor demo apps |

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Windows for the WPF examples (`net9.0-windows`); everything else runs cross-platform
- Optional: Docker for containerized deployment, Playwright browsers for the E2E UI tests

## 1. Build and test

```powershell
cd "E:\Projects\AdvGenNoSQLServer"
dotnet build AdvGenNoSqlServer.sln -c Release
dotnet test AdvGenNoSqlServer.Tests/AdvGenNoSqlServer.Tests.csproj -c Release --no-build
```

## 2. Run the server

### Option A — one-click launcher (Windows)

```powershell
.\run-server.bat
```

Builds and starts the Host in Development mode with TCP SSL off, `admin` / `admin123`.
Close the spawned console window to stop it.

### Option B — dotnet run

```powershell
# Terminal 1 — server (SSL off for a plain-TCP quick start)
$env:NOSQL_ENABLE_SSL = "false"
dotnet run --project AdvGenNoSqlServer.Host/AdvGenNoSqlServer.Host.csproj -c Release
```

`dotnet run` uses the Development profile, which seeds the `admin` user from
`MasterPassword` (`admin123` in `appsettings.Development.json`).

> The base `appsettings.json` enables TCP SSL with the bundled dev certificate
> (`AdvGenNoSqlServer.Host/certs/advgen.pfx`, password `devpassword`). If you keep SSL on,
> set `UseSsl = true` in the client options (see below).

### Ports

| Service | Default port | Protocol |
|---------|--------------|----------|
| TCP server | **9191** (`Port`) | Binary protocol — for `AdvGenNoSqlServer.Client` |
| Admin REST API | **9192** (`AdminApiPort`) | HTTPS — for the WASM Admin and curl |

The REST API port is configured independently (`AdminApiPort` / `NOSQL_ADMIN_HTTP_PORT`);
it defaults to 9192. It serves HTTPS using the ASP.NET Core dev certificate outside
Production. Trust it once per machine if browsers/curl complain:

```powershell
dotnet dev-certs https --trust
```

### Verify it's up

```powershell
curl.exe -k https://localhost:9192/health
```

## 3. Connect a .NET client (TCP)

Add the client library (NuGet, or a project reference to `AdvGenNoSqlServer.Client`):

```powershell
dotnet add package AdvGenNoSqlServer.Client
```

```csharp
using AdvGenNoSqlServer.Client;

// Always include the port — the client defaults to 9090 when it is omitted.
await using var client = new AdvGenNoSqlClient("localhost:9191");
await client.ConnectAsync();
await client.AuthenticateAsync("admin", "admin123");

await client.ExecuteCommandAsync("createcollection", "products");

var id = await client.SetAsync("products", new { name = "Milk 2L", price = 3.5 });
var doc = await client.GetAsync("products", id);
Console.WriteLine(doc?["name"]);

await client.DeleteAsync("products", id);
```

If the server runs with TCP SSL enabled (the base config default):

```csharp
var client = new AdvGenNoSqlClient("localhost:9191",
    new AdvGenNoSqlClientOptions { UseSsl = true });
```

Other useful members: `ExistsAsync`, `ExecuteQueryAsync`, bulk/batch operations, user
management (`createuser`, `setrole`, ... via `ExecuteCommandAsync`), and attachment
upload/download. See [API.md](API.md) and the client README.

## 4. Use the REST API (HTTPS)

Every endpoint except `/health` and `/api/auth/login` requires a JWT Bearer token.

```powershell
# Login — returns a JWT token
curl.exe -k -X POST https://localhost:9192/api/auth/login `
  -H "Content-Type: application/json" `
  -d '{\"username\":\"admin\",\"password\":\"admin123\"}'

# Then, with the token:
curl.exe -k https://localhost:9192/api/databases -H "Authorization: Bearer <token>"
curl.exe -k -X POST https://localhost:9192/api/collections/products -H "Authorization: Bearer <token>"
curl.exe -k -X POST https://localhost:9192/api/collections/products/documents `
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" `
  -d '{\"name\":\"Milk 2L\",\"price\":3.5}'
curl.exe -k "https://localhost:9192/api/collections/products/documents?skip=0&take=50" `
  -H "Authorization: Bearer <token>"
```

Main endpoint groups: `/api/auth/login`, `/api/stats`, `/api/databases`,
`/api/collections` (+ documents CRUD), `/api/query`, `/api/users` (Admin role),
`/metrics` (Prometheus). Full list in [API.md](API.md).

## 5. Embedded mode — no server needed

For local/desktop apps, `AdvGenNoSqlServer.Embedded` is an in-process, single-file document
database (`.agdb`, WAL durability, secondary indexes, LINQ-style typed API):

```csharp
using AdvGenNoSqlServer.Embedded;

using var db = new AdvGenDatabase("prices.agdb");   // or ":memory:"
var items = db.GetCollection<Item>("items");
items.EnsureIndex(x => x.Barcode);
items.Insert(new Item { Name = "Milk 2L", Barcode = "93052001" });
var cheap = items.Find(x => x.Price < 3.0m && x.IsActive);
```

Single-process file lock; no multi-operation transactions. Full guide and LiteDB migration
notes: [AdvGenNoSqlServer.Embedded/README.md](../AdvGenNoSqlServer.Embedded/README.md).
Runnable WPF demo: `AdvGenNoSqlServer.Examples.Wpf` (below).

## 6. Admin UIs

Start the Host first, then either UI:

| UI | Launch | URL | Login |
|----|--------|-----|-------|
| WASM Admin (REST) | `.\run-admin-web.bat` | http://localhost:62960 | Server URL `localhost:9191`, `admin` / `admin123` |
| AdminClient (TCP + SSL) | `.\run-admin-client.bat` | https://localhost:7210 | host `localhost`, port `9191`, `admin` / `admin123` |

- **WASM Admin**: databases, collections, document CRUD, query executor, users, statistics.
  Runs in the browser, so it talks to the HTTPS REST API only.
- **AdminClient**: Blazor Server app talking TCP directly; adds document **attachments**
  (paperclip on each Documents row) and a raw command console. The launcher auto-exports the
  dev SSL certificate the Host needs.

## 7. Example apps

| Example | What it shows | Mode | Run |
|---------|---------------|------|-----|
| `Example.ConsoleApp` | Menu-driven feature tour: CRUD, transactions, full-text search, geospatial (basic demos are simulated, in-process) | Core/Storage, no server | `dotnet run --project Example.ConsoleApp` |
| `AdvGenNoSqlServer.Examples` | Storage/index demos + real client/server walkthrough | TCP client | `dotnet run --project AdvGenNoSqlServer.Examples -- --server=localhost:9191` |
| `AdvGenNoSqlServer.Examples/TodoList` | Console todo CRUD | TCP client | Server running; set port to 9191 in its `appsettings.json` (defaults to 9090), then `dotnet run --project AdvGenNoSqlServer.Examples/TodoList` |
| `AdvGenNoSqlServer.Examples.Wpf` | WPF todo app on the Embedded DB (typed collections, WAL checkpoint) | Embedded, no server | `dotnet run --project AdvGenNoSqlServer.Examples.Wpf -c Release` |
| `AdvGenNoSqlServer.Examples.TodoApp` | Blazor Server todo app, auto-login | TCP client | `.\run-todo-demo.bat` → http://localhost:5090 |
| `AdvGenNoSqlServer.Examples.Messages` | Console message board | TCP client, **cache mode** | `.\run-messages-demo.bat` (or `dotnet run --project AdvGenNoSqlServer.Examples.Messages -- --demo` against a cache-mode server) |
| `AdvGenNoSqlServer.Examples.Messages.Wpf` | WPF chat client, auto-connect | TCP client, **cache mode** | `.\run-messages-wpf.bat` |
| `AdvGenNoSqlServer.Examples.Messages.Web` | Blazor Server (MudBlazor) web chat | TCP client, **cache mode** | `.\run-messages-web.bat` → http://localhost:5290 |

All `run-*.bat` launchers build Release, start the Host plus the app in separate console
windows, and use Development settings with `admin` / `admin123` (demo only). Close the
windows to stop. If a port is in use, close the previous run's window and retry.

The three Messages apps share one `MessageService` and demonstrate **cache mode**
(`StorageMode=Cache`): the launchers start the Host with `NOSQL_STORAGE_MODE=Cache`, so all
data lives in RAM only — nothing is written to disk, and everything is lost on restart
(Redis-style). Run the console and WPF/web clients against the same server to see a shared
in-memory message board.

## 8. Configuration cheatsheet

Config lives in `AdvGenNoSqlServer.Host/appsettings.json`, overlaid by
`appsettings.{Environment}.json`, then overridden by `NOSQL_*` environment variables.

| Key | Env override | Default | Notes |
|-----|--------------|---------|-------|
| `Port` | `NOSQL_PORT` | 9191 | TCP binary protocol |
| `AdminApiPort` | `NOSQL_ADMIN_HTTP_PORT` | 9192 | REST API |
| `StoragePath` | `NOSQL_STORAGE_PATH` | `./storage` | Data directory (Persistent mode) |
| `StorageMode` | `NOSQL_STORAGE_MODE` | `Persistent` | `Persistent` (memory + disk) or `Cache` (memory-only, zero persistence) |
| `MasterPassword` | `NOSQL_MASTER_PASSWORD` | — | Seeds the `admin` user; `admin123` in Development |
| `RequireAuthentication` | `NOSQL_REQUIRE_AUTHENTICATION` | true | RBAC per TCP command (`admin`/`readwrite`/`readonly`) |
| `EnableSsl` | `NOSQL_ENABLE_SSL` | true | TCP TLS; dev PFX bundled in `Host/certs` |
| `JwtSecretKey` | `NOSQL_JWT_SECRET_KEY` | random per run | Set a fixed value or tokens die on restart |

Production fails fast on missing/invalid config (master password, JWT secret, certificate).
Full reference: [Deployment.md](Deployment.md).

## 9. Docker

```powershell
$env:NOSQL_MASTER_PASSWORD = "<strong-password>"
$env:NOSQL_JWT_SECRET_KEY  = "<long-random-secret>"
docker compose up --build
```

Exposes 9191 (TCP) and 9192 (REST); data volume at `/data`, TLS PFX mounted at `/certs`.
The container runs as non-root in Production mode. Details, reverse-proxy setup, and backup
guidance: [Deployment.md](Deployment.md).

## 10. Tests

```powershell
# Unit / integration suite (Core, Storage, Query, Network, Client)
dotnet test AdvGenNoSqlServer.Tests -c Release

# Embedded engine (workflow, persistence, crash recovery)
dotnet test AdvGenNoSqlServer.Embedded.Tests -c Release

# Whole-system E2E — self-hosting, isolated ports, fresh temp data
dotnet test AdvGenNoSqlServer.Host.E2ETests -c Release            # TCP + REST + cache perf
dotnet test AdvGenNoSqlServer.Admin.PlaywrightTests -c Release    # WASM Admin (headless)
dotnet test AdvGenNoSqlServer.AdminClient.PlaywrightTests -c Release
dotnet test AdvGenNoSqlServer.Examples.TodoApp.PlaywrightTests -c Release
dotnet test AdvGenNoSqlServer.Examples.Messages.Web.PlaywrightTests -c Release
dotnet test AdvGenNoSqlServer.Examples.Messages.Wpf.E2ETests -c Release  # needs interactive desktop
```

The E2E suites build and launch the real Host themselves — no manual setup. Playwright
suites need browsers installed (`playwright install` via the test project's build output).

## Next steps

- [UserGuide.md](UserGuide.md) — day-to-day usage
- [API.md](API.md) — full API reference (TCP commands, REST endpoints, client classes)
- [DeveloperGuide.md](DeveloperGuide.md) — architecture and contributing
- [PerformanceTuning.md](PerformanceTuning.md) — tuning and cache-mode benchmarks
- [Deployment.md](Deployment.md) / [ProductionReadiness.md](ProductionReadiness.md) — production deployment and hardening record