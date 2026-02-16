# AdvGen NoSQL Server

WARNING: This project contains "app created vibe" prototype code. It was built as part of a supermarket price-comparing app and is intended for development, experimentation, and demonstration only. Security was NOT a focus for this codebase — do NOT store any sensitive information (passwords, API keys, personal data, payment details, etc.) anywhere in this repository or in runtime configuration.

If you intend to use or adapt this project, review and harden authentication, authorization, input validation, storage encryption, and secrets handling before any production use.

## Purpose

This repository contains the AdvGen NoSQL Server — a prototype NoSQL-like server and related client, storage, network, and query engine components used for experimenting and building a supermarket price comparison app.

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

## Where to look

- Network layer: `AdvGenNoSqlServer.Network` — `MessageProtocol.cs`, `TcpServer.cs`, `ConnectionHandler.cs`
- Core models & interfaces: `AdvGenNoSqlServer.Core`
- Storage engine: `AdvGenNoSqlServer.Storage`
- Server host: `AdvGenNoSqlServer.Host`
- Tests: `AdvGenNoSqlServer.Tests`

## Development notes & coding standards

This project uses .NET 9, xUnit for tests, and follows the repo's internal coding standards (async method names ending with `Async`, PascalCase for types, underscore-prefixed private fields, etc.). See AGENTS.md for more details on build/test conventions, protocol spec, and testing guidelines.

## License

This project is MIT-licensed. See LICENSE.md for details.

## Important — Security Reminder

This repository was created quickly for an app prototype. Repeated for emphasis: do NOT store any sensitive information here. Treat this code as untrusted until a security review and remediation pass is completed.
