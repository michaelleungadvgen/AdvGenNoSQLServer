// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
using AdvGenNoSqlServer.Core.Caching;
using AdvGenNoSqlServer.Core.MemoryManagement;
using AdvGenNoSqlServer.Core.Metrics;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Tests;

public class AdvancedMemoryCacheManagerV2Tests : IDisposable
{
    private readonly ManagedMemoryStorageEngine _engine;
    private readonly AdvancedMemoryCacheManager _manager;

    public AdvancedMemoryCacheManagerV2Tests()
    {
        _engine = new ManagedMemoryStorageEngine(
            new MemoryManagementConfiguration { DefaultTtlSeconds = 0 }, 64 * 1024 * 1024);
        _manager = new AdvancedMemoryCacheManager(_engine, new NoOpMetricsCollector());
    }

    public void Dispose()
    {
        _manager.Dispose();
        // engine is disposed by manager
    }

    [Fact]
    public void Set_ThenGet_ReturnsSameDocument()
    {
        var doc = new Document
        {
            Id = "test-1",
            Data = new Dictionary<string, object?> { ["name"] = "hello" }
        };
        _manager.Set("test-1", doc);
        var result = _manager.Get("test-1");
        Assert.NotNull(result);
        Assert.Equal("test-1", result.Id);
    }

    [Fact]
    public void Get_MissingKey_ReturnsNull()
    {
        Assert.Null(_manager.Get("nonexistent"));
    }

    [Fact]
    public void Remove_ExistingKey_KeyGone()
    {
        var doc = new Document { Id = "r1", Data = new() };
        _manager.Set("r1", doc);
        _manager.Remove("r1");
        Assert.Null(_manager.Get("r1"));
    }

    [Fact]
    public void GetStatistics_ReflectsHitsAndMisses()
    {
        var doc = new Document { Id = "s1", Data = new() };
        _manager.Set("s1", doc);
        _manager.Get("s1");  // hit
        _manager.Get("s1");  // hit
        _manager.Get("nope"); // miss

        var stats = _manager.GetStatistics();
        Assert.Equal(2, stats.TotalHits);
        Assert.Equal(1, stats.TotalMisses);
    }

    [Fact]
    public void Dispose_CanBeCalledTwice()
    {
        var engine2 = new ManagedMemoryStorageEngine(
            new MemoryManagementConfiguration { DefaultTtlSeconds = 0 }, 1024 * 1024);
        var manager2 = new AdvancedMemoryCacheManager(engine2, new NoOpMetricsCollector());
        manager2.Dispose();
        manager2.Dispose(); // must not throw
    }
}
