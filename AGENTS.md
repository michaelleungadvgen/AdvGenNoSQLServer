# Agent Instructions - AdvGenNoSQL Server

**Project**: AdvGenNoSQL Server  
**License**: MIT License  
**Framework**: .NET 9.0

---

## Quick Start

### Build the Solution
```powershell
cd "E:\Projects\AdvGenNoSQLServer"
dotnet build AdvGenNoSqlServer.sln -c Release
```

### Run Tests
```powershell
cd "E:\Projects\AdvGenNoSQLServer"
dotnet test AdvGenNoSqlServer.Tests/AdvGenNoSqlServer.Tests.csproj -c Release
```

### Build and Test in One Command
```powershell
cd "E:\Projects\AdvGenNoSQLServer"
dotnet build AdvGenNoSqlServer.sln -c Release
if ($?) { dotnet test AdvGenNoSqlServer.Tests/AdvGenNoSqlServer.Tests.csproj -c Release --no-build }
```

---

## Project Structure

```
AdvGenNoSQLServer/
├── AdvGenNoSqlServer.Client/        # Client library
├── AdvGenNoSqlServer.Core/          # Core models and interfaces
│   ├── Authentication/
│   ├── Caching/
│   ├── Configuration/
│   ├── Models/
│   └── Transactions/
├── AdvGenNoSqlServer.Host/          # Server host application
├── AdvGenNoSqlServer.Network/       # TCP/Network layer ✓ IMPLEMENTED
├── AdvGenNoSqlServer.Query/         # Query engine
├── AdvGenNoSqlServer.Server/        # Server implementation
├── AdvGenNoSqlServer.Storage/       # Storage engine
├── AdvGenNoSqlServer.Tests/         # Unit tests
└── Example.ConsoleApp/              # Example application
```

---

## Network Layer Implementation Details

### Binary Protocol Specification
```
[Magic (4 bytes): "NOSQ"]
[Version (2 bytes): 1]
[Message Type (1 byte)]
[Flags (1 byte)]
[Payload Length (4 bytes)]
[Payload (variable)]
[CRC32 Checksum (4 bytes)]
Total Header: 12 bytes
```

### Message Types
- `0x01` - Handshake
- `0x02` - Authentication
- `0x03` - Command
- `0x04` - Response
- `0x05` - Error
- `0x06` - Ping
- `0x07` - Pong
- `0x08` - Transaction
- `0x09` - BulkOperation
- `0x0A` - Notification

### Key Classes
- **TcpServer**: Main TCP server with async/await
- **ConnectionHandler**: Per-connection message handling
- **MessageProtocol**: Binary serialization/deserialization
- **ConnectionPool**: Semaphore-based connection limiting

---

## Coding Standards

### Required File Header
```csharp
// Copyright (c) 2026 [Your Organization]
// Licensed under the MIT License.
// See LICENSE.txt for license information.
```

### Naming Conventions
- **Classes/Interfaces**: PascalCase (e.g., `TcpServer`, `IConnectionHandler`)
- **Methods**: PascalCase (e.g., `StartAsync`, `SendMessage`)
- **Private fields**: _camelCase with underscore prefix (e.g., `_listener`)
- **Constants**: PascalCase (e.g., `MaxConnections`)

### Async Patterns
- Use `async/await` throughout
- Method names should end with `Async` (e.g., `StartAsync`)
- Return `Task` or `Task<T>` for async methods
- Use `ValueTask` for hot paths where applicable

### Error Handling
- Use specific exception types (e.g., `ProtocolException`)
- Validate inputs and throw `ArgumentNullException` for null checks
- Use `CancellationToken` for graceful shutdown

---

## Testing Guidelines

### Test Project Setup
- Tests are in `AdvGenNoSqlServer.Tests`
- Uses xUnit framework
- Run with: `dotnet test AdvGenNoSqlServer.Tests/AdvGenNoSqlServer.Tests.csproj`

### Test Naming
- Format: `[MethodName]_[Scenario]_[ExpectedResult]`
- Example: `MessageProtocol_SerializeDeserialize_RoundTrip`

### Test Categories
- Unit tests for individual components
- Integration tests for end-to-end workflows
- Network tests require available port (default test port: 19090)

---

## Common Tasks

### Add a New Network Component
1. Create file in `AdvGenNoSqlServer.Network/`
2. Add copyright header
3. Implement with async patterns
4. Add unit tests in `AdvGenNoSqlServer.Tests/`
5. Build and verify: `dotnet build && dotnet test`

### Update Protocol
1. Modify `MessageProtocol.cs`
2. Update `MessageType` enum if adding new types
3. Add tests for new functionality
4. Ensure backward compatibility

### Debugging Network Issues
1. Check `TcpServer.IsRunning`
2. Verify `ActiveConnectionCount`
3. Review `ConnectionPoolStatistics`
4. Use `dotnet test` to run network tests

---

## Dependencies

### Allowed (MIT/Apache/BSD)
- System.* namespaces (Microsoft, MIT)
- Serilog (Apache 2.0)
- xUnit (Apache 2.0)
- Moq (BSD 3-Clause)

### NOT Allowed
- GPL/AGPL licensed libraries
- SSPL licensed software
- Proprietary/closed-source libraries

---

## Multi-Agent Coordination

See `multiagents.md` for current task assignments.

### Before Starting Work
1. Read `multiagents.md`
2. Pick an available task
3. Update `multiagents.md` with your assignment
4. Verify no conflicts with other agents

### After Completing Work
1. Run full test suite
2. Update `multiagents.md` to mark task complete
3. Update `PROJECT_STATUS.md` with progress
4. Commit changes (do not push)

---

## Project Status

See `PROJECT_STATUS.md` for detailed status.

### Current Phase: Phase 2 (Network & TCP)
- **Progress**: 75% Complete
- **Status**: Server-side TCP implementation complete
- **Next**: Client library implementation

---

**Last Updated**: February 7, 2026  
**Maintainer**: Agent-1
