# AdvGen NoSQL Server

WARNING: This project contains "app created vibe" prototype code. It was built as part of a supermarket price-comparing app and is intended for development, experimentation, and demonstration only. Security was NOT a focus for this codebase — do NOT store any sensitive information (passwords, API keys, personal data, payment details, etc.) anywhere in this repository or in runtime configuration.

If you intend to use or adapt this project, review and harden authentication, authorization, input validation, storage encryption, and secrets handling before any production use.

## Purpose

This repository contains the AdvGen NoSQL Server — a prototype NoSQL-like server and related client, storage, network, and query engine components used for experimenting and building a supermarket price comparison app.

## Features

- **TCP Protocol**: Binary protocol for client-server communication
- **HTTP API**: REST API for web-based administration
- **Database Management**: Multiple database support with selectable databases
- **JWT Authentication**: Secure API access with JWT tokens
- **HTTPS/SSL**: Optional TLS encryption for HTTP API
- **Web Admin Dashboard**: Blazor WebAssembly-based management UI
- **Full-Text Search**: Built-in text indexing and search capabilities
- **User Management + RBAC**: Persistent user accounts (PBKDF2-hashed in `users.json`) with three built-in roles — `admin`, `readwrite`, `readonly` — enforced per TCP command when `RequireAuthentication` is enabled. Managed from the Admin UI Users page or the `createuser`/`setrole`/`setpassword`/`deleteuser`/`listusers`/`changepassword` commands.

## Installation

Install the client library via NuGet:

```bash
dotnet add package AdvGenNoSqlServer.Client
```

Or via the Package Manager Console:

```powershell
Install-Package AdvGenNoSqlServer.Client
```

This will automatically install the required dependencies (`AdvGenNoSqlServer.Core` and `AdvGenNoSqlServer.Network`).

## Quick Start

- Build the solution (Windows / PowerShell):

```powershell
cd "E:\Projects\AdvGenNoSQLServer"
dotnet build AdvGenNoSqlServer.sln -c Release
```

- Run tests:

```powershell
cd "E:\Projects\AdvGenNoSQLServer"
dotnet test AdvGenNoSqlServer.Tests/AdvGenNoSqlServer.Tests.csproj -c Release
```

- Run the server:

```powershell
cd "E:\Projects\AdvGenNoSQLServer"
dotnet run --project AdvGenNoSqlServer.Host/AdvGenNoSqlServer.Host.csproj -c Release
```

- Run the Admin web UI (separate terminal):

```powershell
cd "E:\Projects\AdvGenNoSQLServer"
dotnet run --project AdvGenNoSqlServer.Admin/AdvGenNoSqlServer.Admin.csproj
```

The Admin app will print its local URL (e.g. `https://localhost:62959`). Open that in a browser and log in with `admin` and the `MasterPassword` from `appsettings.json`.

## Configuration

### Server Configuration (`AdvGenNoSqlServer.Host/appsettings.json`)

All keys are flat (no nested sections):

```json
{
  "Host": "0.0.0.0",
  "Port": 9191,
  "MaxConcurrentConnections": 1000,
  "StoragePath": "./storage",
  "MasterPassword": "admin123",
  "RequireAuthentication": true,
  "JwtSecretKey": "your-secret-key-change-in-production",
  "JwtIssuer": "AdvGenNoSqlServer",
  "JwtAudience": "AdvGenNoSqlClient",
  "TokenExpirationHours": 24,
  "EnableSsl": false
}
```

`JwtSecretKey` must be set to the same value on every restart. If omitted, a random key is generated at startup and all existing tokens are invalidated.

### Port Mapping

| Service | Default Port | Protocol | Notes |
|---------|-------------|----------|-------|
| TCP Server | 9191 | Binary TCP | Client library connections |
| HTTPS API | 9192 | HTTPS REST | Always TCP port + 1 |

The HTTPS API always runs on `Port + 1` (9191 + 1 = 9192). There is no plain-HTTP API endpoint.

### HTTPS / Dev Certificate

The Host uses the ASP.NET Core developer certificate for HTTPS. Trust it once per machine:

```bash
dotnet dev-certs https --trust
```

For production, set `CertificatePath` and `CertificatePassword` in `appsettings.json` to point at a PFX file.

## Admin Web Dashboard

`AdvGenNoSqlServer.Admin` is a Blazor WebAssembly application served by its own dev server. It connects to the Host's HTTPS API over `https://localhost:9192`.

### Running

Start the Host first, then the Admin in a separate terminal:

```powershell
# Terminal 1 — server
dotnet run --project AdvGenNoSqlServer.Host/AdvGenNoSqlServer.Host.csproj

# Terminal 2 — admin UI
dotnet run --project AdvGenNoSqlServer.Admin/AdvGenNoSqlServer.Admin.csproj
```

### Logging in

1. Open the Admin URL printed by `dotnet run` (e.g. `https://localhost:62959`)
2. Leave the Server URL field as `localhost:9191` (the TCP port — the app internally adds 1 for the HTTPS API)
3. Username: `admin`, Password: value of `MasterPassword` in `appsettings.json` (default `admin123`)

### Features

- **Database Management**: Create, delete, and switch between databases
- **Collection Management**: Create and delete collections
- **Document CRUD**: Browse, insert, edit, and delete documents
- **Query Executor**: Run queries with JSON results
- **Server Statistics**: View uptime, connection count, and memory usage

### Architecture note

Blazor WebAssembly runs in the browser and cannot open TCP sockets, so the Admin communicates exclusively via the HTTPS REST API (port 9192). The TCP binary protocol is for the `AdvGenNoSqlServer.Client` .NET library only.

## HTTP API

All endpoints are on `https://localhost:9192`. Every endpoint except `/api/health` and `/api/auth/login` requires a `Bearer` token.

### Authentication

```bash
# Login (returns JWT token)
POST https://localhost:9192/api/auth/login
Content-Type: application/json

{
  "username": "admin",
  "password": "admin123"
}
```

### Database Management

```bash
GET    https://localhost:9192/api/databases
POST   https://localhost:9192/api/databases/{name}
POST   https://localhost:9192/api/databases/{name}/select
DELETE https://localhost:9192/api/databases/{name}
```

### Collection Operations

```bash
GET    https://localhost:9192/api/collections
POST   https://localhost:9192/api/collections/{name}
DELETE https://localhost:9192/api/collections/{name}
GET    https://localhost:9192/api/collections/{name}/documents?skip=0&take=50
POST   https://localhost:9192/api/collections/{name}/documents
PUT    https://localhost:9192/api/collections/{name}/documents/{id}
DELETE https://localhost:9192/api/collections/{name}/documents/{id}
```

## Where to look

- Network layer: `AdvGenNoSqlServer.Network` — `MessageProtocol.cs`, `TcpServer.cs`, `ConnectionHandler.cs`
- Core models & interfaces: `AdvGenNoSqlServer.Core`
- Storage engine: `AdvGenNoSqlServer.Storage`
- Server host: `AdvGenNoSqlServer.Host`
- Web Admin: `AdvGenNoSqlServer.Admin`
- Tests: `AdvGenNoSqlServer.Tests`

## Development notes & coding standards

This project uses .NET 9, xUnit for tests, and follows the repo's internal coding standards (async method names ending with `Async`, PascalCase for types, underscore-prefixed private fields, etc.). See AGENTS.md for more details on build/test conventions, protocol spec, and testing guidelines.

## License

This project is MIT-licensed. See LICENSE.md for details.

## Important — Security Reminder

This repository was created quickly for an app prototype. Repeated for emphasis: do NOT store any sensitive information here. Treat this code as untrusted until a security review and remediation pass is completed.
