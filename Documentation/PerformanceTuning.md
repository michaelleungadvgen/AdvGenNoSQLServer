# AdvGenNoSQL Server - Performance Tuning Guide

**Version**: 1.0.0  
**Last Updated**: February 7, 2026

---

## Table of Contents

1. [Introduction](#introduction)
2. [Performance Targets](#performance-targets)
3. [Benchmarking](#benchmarking)
4. [Configuration Tuning](#configuration-tuning)
5. [Caching Strategies](#caching-strategies)
6. [Indexing](#indexing)
7. [Query Optimization](#query-optimization)
8. [Transaction Tuning](#transaction-tuning)
9. [Memory Management](#memory-management)
10. [Network Optimization](#network-optimization)
11. [Storage Optimization](#storage-optimization)
12. [Monitoring](#monitoring)

---

## Introduction

This guide provides recommendations for optimizing AdvGenNoSQL Server performance. It covers configuration tuning, caching strategies, indexing, query optimization, and monitoring.

### Performance Philosophy

- **Measure First**: Always benchmark before optimizing
- **Optimize for Common Cases**: 80% of gains come from 20% of optimizations
- **Trade-offs**: Every optimization has trade-offs (memory vs CPU, consistency vs speed)
- **Scalability**: Design for horizontal scaling where possible

---

## Performance Targets

### Official Targets

| Metric | Target | Measurement |
|--------|--------|-------------|
| Throughput | > 10,000 ops/sec | Sustained for 60 seconds |
| Latency (P99) | < 100ms | Typical operations |
| Memory | < 500MB baseline | Idle server |
| Connections | 10,000+ concurrent | Active connections |
| Cache Hit Ratio | > 80% | For cached workloads |

### Actual Benchmarks

Run benchmarks to verify performance:

```bash
cd AdvGenNoSqlServer.Benchmarks
dotnet run -c Release -- all
```

Sample results on reference hardware (8-core, 16GB RAM):

| Operation | Throughput | Latency (P50) |
|-----------|------------|---------------|
| Document Insert | 15,000/sec | 0.5ms |
| Document Get | 50,000/sec | 0.2ms |
| Query (indexed) | 25,000/sec | 0.4ms |
| Query (no index) | 5,000/sec | 2.0ms |

---

## Benchmarking

### Running Benchmarks

```bash
# All benchmarks
dotnet run -c Release -- all

# Specific component
dotnet run -c Release -- Cache
dotnet run -c Release -- DocumentStore
dotnet run -c Release -- QueryEngine
dotnet run -c Release -- BTreeIndex
dotnet run -c Release -- Serialization

# Quick mode (fewer iterations)
dotnet run -c Release -- --job short
```

### Understanding Results

```
|          Method |     Mean |   Error |  StdDev |   Gen0 | Allocated |
|---------------- |---------:|--------:|--------:|-------:|----------:|
| CacheHit        | 152.3 ns | 2.14 ns | 2.00 ns | 0.0153 |      96 B |
| CacheMiss       | 203.5 ns | 3.21 ns | 3.00 ns | 0.0229 |     144 B |
```

- **Mean**: Average execution time
- **Error/StdDev**: Statistical variance
- **Gen0**: Garbage collections per 1,000 operations
- **Allocated**: Bytes allocated per operation

### Custom Benchmarks

Create custom benchmarks for specific scenarios:

```csharp
[MemoryDiagnoser]
public class MyBenchmark
{
    private DocumentStore _store = null!;
    
    [GlobalSetup]
    public void Setup()
    {
        _store = new DocumentStore();
        // Populate with test data
    }
    
    [Benchmark]
    public async Task QueryWithFilter()
    {
        var filter = new QueryFilter { /* ... */ };
        await _store.QueryAsync("collection", filter);
    }
}
```

---

## Configuration Tuning

### Server Configuration

#### High-Performance Configuration

```json
{
  "Server": {
    "Host": "0.0.0.0",
    "Port": 9090,
    "MaxConnections": 50000,
    "ReceiveBufferSize": 262144,
    "SendBufferSize": 262144,
    "ConnectionTimeout": 60000,
    "KeepAliveInterval": 60000
  },
  "Cache": {
    "Enabled": true,
    "MaxSize": 1073741824,
    "EvictionPolicy": "LRU",
    "DefaultTTL": 3600000
  },
  "Storage": {
    "DataPath": "./data",
    "CompressionEnabled": false,
    "PersistenceMode": "async"
  },
  "Performance": {
    "ThreadPoolMinThreads": 64,
    "ThreadPoolMaxThreads": 512,
    "GCMode": "Server"
  }
}
```

### GC Tuning

For server workloads, use Server GC mode:

```xml
<PropertyGroup>
  <ServerGarbageCollection>true</ServerGarbageCollection>
  <ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>
</PropertyGroup>
```

Or in runtimeconfig.json:

```json
{
  "runtimeOptions": {
    "configProperties": {
      "System.GC.Server": true,
      "System.GC.Concurrent": true,
      "System.GC.HeapCount": 4
    }
  }
}
```

### Thread Pool Tuning

For high-concurrency scenarios:

```csharp
// Set minimum threads to avoid thread starvation
ThreadPool.SetMinThreads(64, 64);
ThreadPool.SetMaxThreads(512, 512);
```

---

## Caching Strategies

### Cache Configuration

```json
{
  "Cache": {
    "Enabled": true,
    "MaxSize": 536870912,
    "MaxItemCount": 100000,
    "DefaultTTL": 3600000
  }
}
```

### Cache Size Guidelines

| Workload | Cache Size | Item Count |
|----------|------------|------------|
| Light | 128 MB | 10,000 |
| Medium | 256 MB | 50,000 |
| Heavy | 512 MB+ | 100,000+ |

### TTL Strategy

Choose TTL based on data volatility:

| Data Type | Recommended TTL |
|-----------|----------------|
| User sessions | 15-30 minutes |
| Configuration | 1-24 hours |
| Reference data | 1-7 days |
| Static content | 7+ days |

### Cache Warming

Pre-populate cache for hot data:

```csharp
public async Task WarmCacheAsync()
{
    var hotData = await store.GetRecentDocumentsAsync(1000);
    foreach (var doc in hotData)
    {
        cache.Set($"doc:{doc.Id}", doc, TimeSpan.FromHours(1));
    }
}
```

### Cache Invalidation

Implement proper invalidation:

```csharp
// After document update
public async Task UpdateDocumentAsync(string id, Dictionary<string, object> updates)
{
    await store.UpdateAsync(id, updates);
    cache.Remove($"doc:{id}");
    cache.Remove("recent_docs");
}
```

---

## Indexing

### When to Index

Create indexes for fields used in:
- Query filters (`$eq`, `$gt`, `$lt`, etc.)
- Sort operations
- Aggregation `$match` stages

### Index Guidelines

| Collection Size | Index Strategy |
|-----------------|----------------|
| < 1,000 docs | No indexes needed |
| 1,000 - 10,000 | Indexes on frequently queried fields |
| 10,000 - 100,000 | Indexes on most query fields |
| 100,000+ | Comprehensive indexing strategy |

### Creating Indexes

```csharp
var indexManager = new IndexManager();

// Single field index
indexManager.CreateIndex("users", "email", unique: true);

// Compound index (simulate with multiple single indexes)
indexManager.CreateIndex("orders", "userId");
indexManager.CreateIndex("orders", "createdAt");
```

### Index Trade-offs

| Benefit | Cost |
|---------|------|
| Faster queries | Slower writes |
| Faster sorts | More memory usage |
| Unique constraints | Insert/update overhead |

### Index Monitoring

Monitor index usage:

```csharp
var stats = indexManager.GetStatistics();
foreach (var index in stats)
{
    Console.WriteLine($"{index.Field}: {index.HitCount} hits, {index.Size} bytes");
}
```

---

## Query Optimization

### Query Best Practices

1. **Use specific filters**: Narrow result set early
2. **Limit results**: Use `Limit` to cap result size
3. **Selective projection**: Only retrieve needed fields
4. **Use indexes**: Ensure filter fields are indexed

### Optimized Query Example

```csharp
// Good - Specific filter with limit
var query = new Query
{
    Collection = "users",
    Filter = new QueryFilter
    {
        { "status", new { $eq = "active" } },
        { "createdAt", new { $gte = DateTime.UtcNow.AddDays(-30) } }
    },
    SortFields = new List<SortField>
    {
        new SortField { Field = "lastLogin", Direction = SortDirection.Descending }
    },
    Options = new QueryOptions
    {
        Limit = 100,
        Projection = new[] { "id", "name", "email" }
    }
};

// Bad - Full scan, no limits
var badQuery = new Query
{
    Collection = "users",
    Filter = null,  // No filter!
    Options = new QueryOptions
    {
        Limit = null  // No limit!
    }
};
```

### Filter Optimization

Order filter conditions by selectivity:

```csharp
// Good - Most selective first
{ "$and": [
    { "userId": { "$eq": "specific-user" } },  // Very selective
    { "status": { "$eq": "active" } }           // Less selective
]}

// Bad - Less selective first
{ "$and": [
    { "status": { "$eq": "active" } },         // Many matching
    { "userId": { "$eq": "specific-user" } }   // Few matching
]}
```

### Aggregation Pipeline Optimization

1. **Match early**: Filter documents early in pipeline
2. **Limit early**: Reduce data set size quickly
3. **Project selectively**: Only carry needed fields

```csharp
// Good - Match and limit early
var pipeline = new AggregationPipelineBuilder()
    .Match(new { status = "active" })      // Filter early
    .Limit(1000)                            // Reduce dataset
    .Group(new { _id = "$category", count = new { $sum = 1 } })
    .Sort(new { count = -1 })
    .Build();

// Bad - Grouping everything first
var badPipeline = new AggregationPipelineBuilder()
    .Group(new { _id = "$category" })      // Processing all docs!
    .Match(new { status = "active" })      // Filter too late
    .Build();
```

---

## Transaction Tuning

### Isolation Level Selection

| Isolation Level | Use Case | Performance |
|-----------------|----------|-------------|
| ReadUncommitted | High throughput, eventual consistency | Fastest |
| ReadCommitted | Default, good balance | Good |
| RepeatableRead | Consistent reporting | Slower |
| Serializable | Financial, strict consistency | Slowest |

### Transaction Best Practices

1. **Keep transactions short**: Minimize transaction duration
2. **Batch operations**: Group related operations
3. **Choose appropriate isolation**: Don't over-isolate

```csharp
// Good - Short transaction with batching
var tx = await coordinator.BeginAsync(IsolationLevel.ReadCommitted);
try
{
    await store.BatchInsertAsync(docs, tx.TransactionId);
    await coordinator.CommitAsync(tx.TransactionId);
}
catch { /* rollback */ }

// Bad - Long transaction with many operations
var tx = await coordinator.BeginAsync(IsolationLevel.Serializable);
foreach (var doc in largeList)  // Too many operations!
{
    await store.InsertAsync(doc, tx.TransactionId);
    await Task.Delay(100);  // Holding locks!
}
```

### Deadlock Prevention

- Access resources in consistent order
- Use timeouts to break deadlocks
- Keep transactions short

---

## Memory Management

### Memory Configuration

```json
{
  "Cache": {
    "MaxSize": 268435456,
    "MaxItemCount": 100000
  },
  "Storage": {
    "IndexCacheSize": 134217728
  }
}
```

### Object Pooling

Use object pooling for frequently created objects:

```csharp
// Rent from pool
using var buffer = BufferPool.Rent(4096);

// Use buffer
data.CopyTo(buffer.Memory);

// Automatically returned on dispose
```

### Large Object Heap (LOH)

Avoid LOH fragmentation:

```csharp
// Bad - Allocates on LOH
var largeArray = new byte[100000];

// Good - Uses ArrayPool
var largeArray = ArrayPool<byte>.Shared.Rent(100000);
try
{
    // Use array
}
finally
{
    ArrayPool<byte>.Shared.Return(largeArray);
}
```

### Memory Diagnostics

Monitor memory usage:

```csharp
var gcInfo = GC.GetGCMemoryInfo();
Console.WriteLine($"Heap size: {gcInfo.TotalAvailableMemoryBytes}");
Console.WriteLine($"Gen0 collections: {GC.CollectionCount(0)}");
Console.WriteLine($"Gen1 collections: {GC.CollectionCount(1)}");
Console.WriteLine($"Gen2 collections: {GC.CollectionCount(2)}");
```

---

## Network Optimization

### TCP Settings

```json
{
  "Server": {
    "ReceiveBufferSize": 65536,
    "SendBufferSize": 65536,
    "ConnectionTimeout": 30000
  }
}
```

### Buffer Sizes

| Workload | Buffer Size |
|----------|-------------|
| Small messages (<1KB) | 8KB |
| Medium messages (1-100KB) | 64KB |
| Large messages (>100KB) | 256KB+ |

### Connection Pooling (Client)

Reuse connections:

```csharp
// Good - Reuse single client
using var client = new AdvGenNoSqlClient("localhost:9090");
await client.ConnectAsync();

for (int i = 0; i < 1000; i++)
{
    await client.InsertAsync("data", doc);
}

// Bad - Creating new connection each time
for (int i = 0; i < 1000; i++)
{
    using var client = new AdvGenNoSqlClient("localhost:9090");
    await client.ConnectAsync();
    await client.InsertAsync("data", doc);
}
```

### Batch Operations

Use batch operations for bulk data:

```csharp
// Good - Batch insert
await client.BatchInsertAsync("collection", documents);

// Bad - Individual inserts
foreach (var doc in documents)
{
    await client.InsertAsync("collection", doc);
}
```

---

## Storage Optimization

### Persistence Mode

| Mode | Durability | Performance |
|------|------------|-------------|
| `fsync` | Highest (fsync every write) | Slowest |
| `async` | High (OS manages flush) | Good |
| `delayed` | Medium (periodic flush) | Fastest |

### Compression

Enable compression for large datasets:

```json
{
  "Storage": {
    "CompressionEnabled": true,
    "CompressionLevel": "optimal"
  }
}
```

Trade-offs:
- **Storage savings**: 50-90% reduction
- **CPU cost**: 10-20% overhead
- **Best for**: Large documents, infrequently accessed data

### File Size Management

```json
{
  "Storage": {
    "MaxFileSize": 1073741824,
    "MaxCollectionFiles": 10
  }
}
```

---

## Monitoring

### Key Metrics

Monitor these metrics for performance:

| Metric | Target | Alert Threshold |
|--------|--------|-----------------|
| Request latency (P99) | < 100ms | > 500ms |
| Cache hit ratio | > 80% | < 60% |
| Active connections | < MaxConnections | > 80% Max |
| Memory usage | < 500MB | > 1GB |
| Disk I/O | < 50MB/s | > 100MB/s |

### Performance Counters

```csharp
// Custom metrics
public class PerformanceMetrics
{
    public long RequestsPerSecond { get; set; }
    public double AverageLatencyMs { get; set; }
    public double CacheHitRatio { get; set; }
    public int ActiveConnections { get; set; }
    public long MemoryUsedBytes { get; set; }
}
```

### Logging Performance Events

```csharp
if (executionTimeMs > 100)
{
    logger.LogWarning("Slow query detected: {QueryTime}ms", executionTimeMs);
}
```

### Health Checks

Implement health checks:

```csharp
public async Task<HealthStatus> CheckHealthAsync()
{
    var checks = new List<HealthCheck>
    {
        CheckConnectionPool(),
        CheckCacheHealth(),
        CheckDiskSpace(),
        CheckMemoryUsage()
    };
    
    return checks.Any(c => c.Status == HealthStatus.Unhealthy)
        ? HealthStatus.Unhealthy
        : HealthStatus.Healthy;
}
```

---

## Summary

### Quick Optimization Checklist

- [ ] Enable Server GC mode
- [ ] Configure appropriate cache size
- [ ] Create indexes for query fields
- [ ] Use batch operations for bulk data
- [ ] Choose appropriate isolation level
- [ ] Set connection buffer sizes
- [ ] Enable compression (if CPU available)
- [ ] Monitor key metrics
- [ ] Use object pooling for hot paths
- [ ] Keep transactions short

### Performance Anti-Patterns

| Anti-Pattern | Solution |
|--------------|----------|
| N+1 queries | Use batch operations |
| Full collection scans | Create indexes |
| Long transactions | Reduce scope, use appropriate isolation |
| Unbounded caching | Set cache size limits |
| Synchronous I/O | Use async/await throughout |
| Creating objects in loops | Use object pooling |

---

## See Also

- [API Documentation](API.md)
- [User Guide](UserGuide.md)
- [Developer Guide](DeveloperGuide.md)
- [.NET Performance Guidelines](https://docs.microsoft.com/en-us/dotnet/standard/performance/)
