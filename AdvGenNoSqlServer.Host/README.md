# AdvGenNoSQL Server Host

A standalone console application for running the AdvGenNoSQL Server.

## Overview

The Host application provides a simple way to run the NoSQL server without complex setup. It integrates all server components:
- TCP server for client connections
- Document store with file persistence
- Authentication and authorization
- Caching layer
- Transaction management
- Audit logging

## Quick Start

### Run with Default Configuration

```bash
dotnet run
```

### Run with Custom Configuration

```bash
dotnet run -- --config /path/to/appsettings.json
```

### Run in Production Mode

```bash
dotnet run -- --environment Production
```

## Command Line Options

| Option | Short | Description |
|--------|-------|-------------|
| `--config` | `-c` | Path to configuration file |
| `--environment` | `-e` | Environment name (Development, Production, Testing) |
| `--help` | `-h` | Show help message |
| `--version` | `-v` | Show version information |

## Configuration

The host application uses JSON configuration files:

- `appsettings.json` - Default configuration
- `appsettings.Development.json` - Development overrides
- `appsettings.Production.json` - Production settings

### Example Configuration

```json
{
  "Server": {
    "Host": "0.0.0.0",
    "Port": 9090,
    "MaxConnections": 1000
  },
  "Security": {
    "RequireAuthentication": true,
    "MasterPassword": "your-secure-password"
  },
  "Storage": {
    "DataPath": "./data"
  },
  "Logging": {
    "EnableConsoleLogging": true,
    "EnableAuditLogging": true
  }
}
```

## Building

### Build for Development

```bash
dotnet build
```

### Build for Release

```bash
dotnet build -c Release
```

### Publish Self-Contained

```bash
dotnet publish -c Release --self-contained -r win-x64
```

## Running

### Development

```bash
dotnet run
```

### Production

```bash
dotnet run --environment Production
```

### With Custom Data Directory

```bash
dotnet run -- --config /etc/nosql-server/appsettings.json
```

## Logging

The host outputs server status to the console:

```
╔════════════════════════════════════════════════════════════════╗
║           AdvGenNoSQL Server - Host Application                ║
║                   MIT Licensed - Version 1.0.0                 ║
╚════════════════════════════════════════════════════════════════╝

Loading configuration from: appsettings.json
Server Configuration:
  Host: 0.0.0.0:9090
  Max Connections: 1000
  Storage Path: ./storage
  Data Path: ./data

Cache manager initialized.
Document store initialized.
Transaction coordinator initialized.

Starting TCP server...

═══════════════════════════════════════════════════════════════
  Server is running on 0.0.0.0:9090
  Press Ctrl+C to stop the server
═══════════════════════════════════════════════════════════════

[+] Connection established: conn-123 from 127.0.0.1:54321
```

## Graceful Shutdown

Press `Ctrl+C` to initiate graceful shutdown:

```
Shutting down server...
Server stopped.
```

## License

MIT License - Copyright (c) 2026 AdvanGeneration Pty. Ltd.
