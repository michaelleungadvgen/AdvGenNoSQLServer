# AdvGenNoSQL Server - Developer Guide

**Version**: 1.0.0  
**Last Updated**: February 7, 2026

---

## Table of Contents

1. [Introduction](#introduction)
2. [Development Environment](#development-environment)
3. [Project Structure](#project-structure)
4. [Coding Standards](#coding-standards)
5. [Building and Testing](#building-and-testing)
6. [Architecture Overview](#architecture-overview)
7. [Adding New Features](#adding-new-features)
8. [Testing Guidelines](#testing-guidelines)
9. [Debugging](#debugging)
10. [Contributing](#contributing)

---

## Introduction

This guide is for developers who want to contribute to AdvGenNoSQL Server or understand its internals. It covers the development environment setup, project structure, coding standards, and contribution guidelines.

---

## Development Environment

### Prerequisites

- **.NET 9.0 SDK** (latest stable)
- **Visual Studio 2022** or **VS Code** with C# Dev Kit
- **Git** for version control
- **PowerShell** or **bash** for scripts

### Setting Up the Environment

1. Clone the repository:
```bash
git clone https://github.com/yourorg/advgen-nosql-server.git
cd advgen-nosql-server
```

2. Verify .NET installation:
```bash
dotnet --version
# Should output: 9.0.x
```

3. Restore dependencies:
```bash
dotnet restore
```

4. Build the solution:
```bash
dotnet build
```

5. Run tests:
```bash
dotnet test
```

### IDE Configuration

#### Visual Studio 2022

1. Open `AdvGenNoSqlServer.sln`
2. Install recommended extensions:
   - EditorConfig
   - xUnit Test Runner

#### VS Code

Recommended extensions:
- C# Dev Kit
- .NET Extension Pack
- xUnit Test Runner
- EditorConfig

Add to `.vscode/settings.json`:
```json
{
    "dotnet.defaultSolution": "AdvGenNoSqlServer.sln",
    "omnisharp.enableRoslynAnalyzers": true,
    "omnisharp.organizeImportsOnFormat": true
}
```

---

## Project Structure

```
AdvGenNoSQLServer/
├── AdvGenNoSqlServer.Core/           # Core functionality
│   ├── Authentication/               # Auth, JWT, RBAC, encryption
│   ├── Caching/                      # LRU cache implementation
│   ├── Configuration/                # Configuration management
│   ├── Models/                       # Core models (Document, etc.)
│   ├── Pooling/                      # Object pooling
│   └── Transactions/                 # Transaction management
│
├── AdvGenNoSqlServer.Network/        # Network layer
│   ├── TcpServer.cs                  # TCP server implementation
│   ├── ConnectionHandler.cs          # Per-connection handling
│   ├── ConnectionPool.cs             # Connection management
│   ├── MessageProtocol.cs            # Binary protocol
│   └── TlsStreamHelper.cs            # SSL/TLS support
│
├── AdvGenNoSqlServer.Storage/        # Storage engine
│   ├── Indexing/                     # B-tree indexes
│   └── Storage/                      # File storage managers
│
├── AdvGenNoSqlServer.Query/          # Query engine
│   ├── Aggregation/                  # Aggregation pipeline
│   ├── Execution/                    # Query execution
│   ├── Filtering/                    # Filter engine
│   ├── Models/                       # Query models
│   └── Parsing/                      # Query parser
│
├── AdvGenNoSqlServer.Client/         # Client library
│   ├── Client.cs                     # Main client
│   └── ClientOptions.cs              # Client configuration
│
├── AdvGenNoSqlServer.Server/         # Server application
│   ├── NoSqlServer.cs                # Server implementation
│   ├── Program.cs                    # Entry point
│   └── appsettings*.json             # Configuration files
│
├── AdvGenNoSqlServer.Tests/          # Unit tests
├── AdvGenNoSqlServer.Benchmarks/     # Performance benchmarks
├── Example.ConsoleApp/               # Example usage
└── Documentation/                    # Documentation
```

### Project Dependencies

```
Core
├── (no internal dependencies)

Network
├── Core

Storage
├── Core

Query
├── Core
├── Storage

Client
├── Core
├── Network

Server
├── Core
├── Network
├── Storage
├── Query

Tests
├── All projects

Benchmarks
├── All projects
```

---

## Coding Standards

### File Header

Every file must include the license header:

```csharp
// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.
```

### Naming Conventions

| Element | Convention | Example |
|---------|------------|---------|
| Classes | PascalCase | `TcpServer` |
| Interfaces | PascalCase with I prefix | `IDocumentStore` |
| Methods | PascalCase | `GetDocumentAsync` |
| Properties | PascalCase | `ConnectionTimeout` |
| Fields (private) | _camelCase | `_listener` |
| Constants | PascalCase | `MaxConnections` |
| Enums | PascalCase | `TransactionStatus` |
| Generic parameters | T prefix | `TKey`, `TValue` |

### Async Patterns

- Use `async/await` throughout
- Method names should end with `Async`
- Return `Task` or `Task<T>` for async methods
- Use `CancellationToken` for cancellable operations

```csharp
// Good
public async Task<Document?> GetAsync(string id, CancellationToken cancellationToken = default)
{
    // Implementation
}

// Bad
public Task<Document> Get(string id)  // Missing Async suffix
{
    return Task.FromResult(document);  // Not truly async
}
```

### Error Handling

- Use specific exception types
- Validate inputs and throw `ArgumentNullException` for null checks
- Include meaningful error messages

```csharp
public async Task<Document?> GetAsync(string collection, string id)
{
    if (string.IsNullOrEmpty(collection))
        throw new ArgumentException("Collection name cannot be empty", nameof(collection));
    
    if (string.IsNullOrEmpty(id))
        throw new ArgumentException("Document ID cannot be empty", nameof(id));
    
    try
    {
        // Implementation
    }
    catch (IOException ex)
    {
        throw new DocumentStoreException($"Failed to retrieve document {id}", ex);
    }
}
```

### Documentation Comments

All public APIs must have XML documentation:

```csharp
/// <summary>
/// Gets a document by its unique identifier.
/// </summary>
/// <param name="collection">The collection name.</param>
/// <param name="id">The document ID.</param>
/// <returns>The document if found, null otherwise.</returns>
/// <exception cref="ArgumentException">Thrown when collection or id is empty.</exception>
/// <exception cref="DocumentStoreException">Thrown when retrieval fails.</exception>
public async Task<Document?> GetAsync(string collection, string id)
{
    // Implementation
}
```

---

## Building and Testing

### Build Commands

```bash
# Debug build
dotnet build

# Release build
dotnet build -c Release

# Clean build
dotnet clean
dotnet build -c Release
```

### Test Commands

```bash
# Run all tests
dotnet test

# Run with verbosity
dotnet test -v n

# Run specific test class
dotnet test --filter "FullyQualifiedName~DocumentStoreTests"

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run tests without build (faster)
dotnet test --no-build
```

### Benchmarking

```bash
cd AdvGenNoSqlServer.Benchmarks

# Run all benchmarks
dotnet run -c Release -- all

# Run specific benchmark
dotnet run -c Release -- Cache
dotnet run -c Release -- DocumentStore

# Quick run (fewer iterations)
dotnet run -c Release -- --job short
```

---

## Architecture Overview

### Layer Architecture

```
┌─────────────────────────────────────┐
│      Client Application             │
├─────────────────────────────────────┤
│      Client Library                 │
├─────────────────────────────────────┤
│      Network/Protocol Layer         │
├─────────────────────────────────────┤
│    Security & Authentication        │
├─────────────────────────────────────┤
│   Query Processing & Commands       │
├─────────────────────────────────────┤
│   Transaction Management            │
├─────────────────────────────────────┤
│     Storage Engine                  │
├─────────────────────────────────────┤
│   Caching Layer                     │
├─────────────────────────────────────┤
│   Persistence & File I/O            │
└─────────────────────────────────────┘
```

### Key Design Patterns

1. **Repository Pattern**: `IDocumentStore` abstracts storage
2. **Factory Pattern**: `ClientFactory` for creating connections
3. **Strategy Pattern**: Isolation levels in transactions
4. **Observer Pattern**: Configuration change events
5. **Object Pooling**: Buffer and object reuse

### Thread Safety

- All public APIs are thread-safe
- Use `ConcurrentDictionary` for shared collections
- Use `SemaphoreSlim` for resource limiting
- Use `ReaderWriterLockSlim` for read-heavy scenarios

### Memory Management

- Use `ArrayPool<byte>` for buffer pooling
- Use `ObjectPool<T>` for expensive objects
- Implement `IDisposable` for resources
- Use `using` statements for disposal

---

## Adding New Features

### Adding a New Query Operator

1. Add operator constant to `FilterEngine.cs`:
```csharp
public const string OperatorRegex = "$regex";
```

2. Implement operator logic in `FilterEngine.EvaluateOperator`:
```csharp
case OperatorRegex:
    return EvaluateRegex(fieldValue, operatorValue);
```

3. Add unit tests in `QueryEngineTests.cs`:
```csharp
[Fact]
public void FilterEngine_Regex_MatchingPattern()
{
    // Test implementation
}
```

### Adding a New Aggregation Stage

1. Create stage class in `AdvGenNoSqlServer.Query/Aggregation/Stages/`:
```csharp
public class UnwindStage : IAggregationStage
{
    public string Field { get; }
    
    public UnwindStage(string field)
    {
        Field = field;
    }
    
    public Task<IEnumerable<Document>> ExecuteAsync(IEnumerable<Document> input)
    {
        // Implementation
    }
}
```

2. Add builder method to `AggregationPipelineBuilder`:
```csharp
public AggregationPipelineBuilder Unwind(string field)
{
    _stages.Add(new UnwindStage(field));
    return this;
}
```

3. Add tests in `AggregationPipelineTests.cs`

### Adding a New Storage Backend

1. Create interface extending `IDocumentStore`:
```csharp
public interface IDistributedDocumentStore : IDocumentStore
{
    Task ReplicateAsync(string nodeId);
    Task<bool> IsNodeHealthyAsync(string nodeId);
}
```

2. Implement the interface
3. Add configuration options
4. Add unit tests
5. Update documentation

---

## Testing Guidelines

### Test Structure

```csharp
// Arrange
var store = new DocumentStore();
var document = new Document { Id = "test", Data = new() };

// Act
var result = await store.InsertAsync("collection", document);

// Assert
Assert.True(result);
Assert.Single(store.GetAllAsync("collection").Result);
```

### Test Categories

| Category | Naming | Example |
|----------|--------|---------|
| Unit | `[Method]_[Scenario]_[Expected]` | `GetDocument_ExistingId_ReturnsDocument` |
| Integration | `Integration_[Feature]` | `Integration_ClientServer_Communication` |
| Stress | `Stress_[Scenario]` | `Stress_HighConcurrentConnections` |

### Mocking

Use Moq for mocking dependencies:

```csharp
[Fact]
public async Task Query_WithCache_HitsCache()
{
    // Arrange
    var mockCache = new Mock<ICacheManager>();
    mockCache.Setup(c => c.Get("key")).Returns(new Document());
    
    var executor = new QueryExecutor(mockCache.Object);
    
    // Act & Assert
    // ...
}
```

### Test Data

Use fixtures for complex test data:

```csharp
public class DocumentFixture
{
    public Document CreateDocument(string id, Dictionary<string, object?>? data = null)
    {
        return new Document
        {
            Id = id,
            Data = data ?? new Dictionary<string, object?>()
        };
    }
}
```

---

## Debugging

### Enabling Debug Logging

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft": "Warning"
    }
  }
}
```

### Using the Debugger

1. Set breakpoints in code
2. Run with debugger attached:
```bash
dotnet run --project AdvGenNoSqlServer.Server
```

### Common Debug Scenarios

#### Debugging Connection Issues

Enable network logging:
```csharp
var server = new TcpServer(config, logger);
server.ConnectionEstablished += (s, e) => 
    logger.LogDebug("Connection established: {ConnectionId}", e.ConnectionId);
```

#### Debugging Query Performance

Enable query logging:
```csharp
var result = await executor.ExecuteAsync(query, store);
logger.LogDebug("Query executed in {ElapsedMs}ms", result.ExecutionTimeMs);
```

### Performance Profiling

Use dotTrace or built-in .NET profiling:

```bash
# Enable CPU profiling
setx DOTNET_EnableDiagnostics 1
dotnet run -c Release

# Analyze with PerfView or dotTrace
```

---

## Contributing

### Before Contributing

1. Read this guide completely
2. Check existing issues and PRs
3. Discuss major changes in an issue first

### Contribution Workflow

1. Fork the repository
2. Create a feature branch: `git checkout -b feature/my-feature`
3. Make your changes
4. Add tests for new functionality
5. Run all tests: `dotnet test`
6. Update documentation
7. Commit with descriptive message
8. Push and create pull request

### Commit Message Format

```
[type]: [short description]

[long description if needed]

Fixes #[issue number]
```

Types:
- `feat`: New feature
- `fix`: Bug fix
- `docs`: Documentation changes
- `test`: Test additions/changes
- `refactor`: Code refactoring
- `perf`: Performance improvements

Example:
```
feat: Add $regex query operator

Implements the $regex operator for pattern matching in queries.
Supports standard .NET regex syntax.

Fixes #123
```

### Code Review Checklist

- [ ] Code follows naming conventions
- [ ] XML documentation added/updated
- [ ] Tests added for new functionality
- [ ] All tests pass
- [ ] No new warnings
- [ ] Performance considered
- [ ] Security implications reviewed

### License Compliance

All contributions must be MIT-licensed:
- No GPL/AGPL code
- No proprietary dependencies
- Include license header in new files

---

## Performance Considerations

### Optimization Guidelines

1. **Minimize allocations** - Use object pooling
2. **Use async I/O** - Never block threads
3. **Cache frequently accessed data** - Use LRU cache
4. **Batch operations** - Reduce network round trips
5. **Use indexes** - Create indexes for query fields

### Performance Anti-Patterns

```csharp
// Bad - Synchronous I/O
public Document Get(string id)
{
    return File.ReadAllText(path);  // Blocking!
}

// Good - Asynchronous I/O
public async Task<Document> GetAsync(string id)
{
    return await File.ReadAllTextAsync(path);
}

// Bad - Excessive allocations
public void Process(List<byte> data)
{
    var buffer = new byte[data.Count];  // Allocates every time
}

// Good - Buffer pooling
public void Process(List<byte> data)
{
    var buffer = ArrayPool<byte>.Shared.Rent(data.Count);
    try { /* use buffer */ }
    finally { ArrayPool<byte>.Shared.Return(buffer); }
}
```

---

## Resources

- [API Documentation](API.md)
- [User Guide](UserGuide.md)
- [Performance Tuning](PerformanceTuning.md)
- [.NET Documentation](https://docs.microsoft.com/dotnet/)
- [xUnit Documentation](https://xunit.net/)

---

## Contact

For questions or support:
- Open an issue on GitHub
- Email: dev@advgen.com
