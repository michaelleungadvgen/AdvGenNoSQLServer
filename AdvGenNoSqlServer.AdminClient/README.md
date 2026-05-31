# AdvGenNoSQL Admin Client

A Blazor Server web application for administering the AdvGenNoSQL server over a direct TCP connection.

## Overview

The Admin Client connects to the NoSQL server using the same binary TCP protocol as application clients (`AdvGenNoSqlServer.Client`). Unlike the existing Blazor WASM admin (`AdvGenNoSqlServer.Admin`), it runs as a .NET process rather than in the browser sandbox, so it can open TCP sockets directly — no REST API or JWT middleware required.

Both admin applications coexist in the solution; this one is not a replacement.

**Browser → Admin app:** Kestrel HTTPS on `https://localhost:7210`  
**Admin app → NoSQL server:** `AdvGenNoSqlClient` TCP with SSL (always required)

## Prerequisites

### 1. Trust the ASP.NET Core dev cert (once per machine)

```bash
dotnet dev-certs https --trust
```

### 2. Export the dev cert for the Host TCP server (once per repo checkout)

```bash
mkdir -p AdvGenNoSqlServer.Host/certs
dotnet dev-certs https -ep AdvGenNoSqlServer.Host/certs/advgen.pfx -p devpassword
```

The `certs/` directory is excluded from git — never commit `.pfx` files.

### 3. Enable SSL in the Host

`AdvGenNoSqlServer.Host/appsettings.json` must have:

```json
"EnableSsl": true,
"SslCertificatePath": "./certs/advgen.pfx",
"SslCertificatePassword": "devpassword",
```

Restart the Host after making this change. If `EnableSsl` is `false`, the TCP SSL handshake will fail and the login page will show the exception.

## Quick Start

**Terminal 1 — start the NoSQL server:**

```bash
cd AdvGenNoSqlServer.Host
dotnet run
```

Expected: `TCP server listening on 0.0.0.0:9191`

**Terminal 2 — start the admin client:**

```bash
cd AdvGenNoSqlServer.AdminClient
dotnet run --launch-profile https
```

Expected: `Now listening on: https://localhost:7210`

Open `https://localhost:7210/login` and enter your server credentials.

## Login

| Field | Default | Description |
|---|---|---|
| Host | `localhost` | TCP hostname of the NoSQL server |
| Port | `9191` | TCP port of the NoSQL server |
| Username | — | Server username (default: `admin`) |
| Password | — | Server password (default: `admin123`) |

Each browser tab maintains its own TCP connection (Blazor Server circuit = Scoped service lifetime). Closing the tab disconnects automatically.

## Pages

### Dashboard (`/`)

Live server stats refreshed every 30 seconds:

- Version
- Uptime
- Active TCP connections
- Total documents across all collections
- Total collections
- Memory usage (MB)

### Collections (`/collections`)

- Lists all collections with their document count
- **New Collection** — prompts for a name and creates the collection
- **Delete** — confirms and drops the collection with all its documents
- Click a collection name to browse its documents

### Documents (`/documents?collection=<name>`)

Paginated view (50 documents per page):

- **View** — shows the full document JSON
- **Edit** — opens a JSON editor pre-populated with the document; saves via upsert (preserves `_id`)
- **Insert** — opens a blank JSON editor; the server assigns a GUID `_id`
- **Delete** — confirms and removes the document

After any write operation the current page reloads automatically.

### Query (`/query`)

Free-form query execution. Enter a query string, click **Run**, and see the JSON result. Error details appear as a notification.

## Architecture

```
Browser
  │  HTTPS (port 7210)
  ▼
AdvGenNoSqlServer.AdminClient   ← Blazor Server, net9.0
  │
  │  TcpAdminService (Scoped)
  │  AdvGenNoSqlClient + SSL
  │
  │  TCP + SSL (port 9191)
  ▼
AdvGenNoSqlServer.Host
```

`TcpAdminService` is the only service. It is Scoped (one instance per SignalR circuit / browser tab), owns the `AdvGenNoSqlClient` instance, and implements `IAsyncDisposable` to close the TCP socket cleanly when the circuit disposes.

## TCP Commands Used

| Operation | Command |
|---|---|
| Server stats | `stats` |
| List collections | `listcollections` |
| Create collection | `createcollection` |
| Drop collection | `dropcollection` |
| List documents (paginated) | `listdocuments` |
| Get single document | `get` |
| Upsert document | `set` |
| Delete document | `delete` |
| Execute query | (query protocol) |

## Configuration

`appsettings.json` contains only standard ASP.NET Core logging config. All connection details (host, port, credentials) are entered at login time and held in the Scoped `TcpAdminService`.

`Properties/launchSettings.json` sets HTTPS on port 7210 and HTTP on port 5210.

## Building

```bash
dotnet build AdvGenNoSqlServer.AdminClient/AdvGenNoSqlServer.AdminClient.csproj
```

## What Is Not Supported

- **User management** — no TCP command exists for creating or deleting users
- **Multi-database switching** — all operations target the default database; no `selectdatabase` TCP command exists
- **Non-SSL connections** — SSL is always required; the client always sets `UseSsl = true`

## License

MIT License — Copyright (c) 2026 AdvanGeneration Pty. Ltd.
