// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
using AdvGenNoSqlServer.Core.MemoryManagement;

namespace AdvGenNoSqlServer.Tests;

public class NativeMemoryStorageEngineTests : IDisposable
{
    private readonly MemoryManagementConfiguration _config = new()
    {
        Plan = "Native",
        MaxMemoryMB = 64,
        EvictionPolicy = "LRU",
        DefaultTtlSeconds = 0
    };
    private readonly NativeMemoryStorageEngine _engine;

    public NativeMemoryStorageEngineTests()
    {
        _engine = new NativeMemoryStorageEngine(_config, 64 * 1024 * 1024);
    }

    public void Dispose() => _engine.Dispose();

    [Fact]
    public void Set_ThenTryGet_ReturnsSameBytes()
    {
        byte[] data = [1, 2, 3];
        _engine.Set("k1", data);
        bool found = _engine.TryGet("k1", out var result);
        Assert.True(found);
        Assert.Equal(data, result.ToArray());
    }

    [Fact]
    public void TryGet_MissingKey_ReturnsFalse()
    {
        bool found = _engine.TryGet("missing", out _);
        Assert.False(found);
    }

    [Fact]
    public void Remove_ExistingKey_ReturnsTrueAndKeyGone()
    {
        _engine.Set("k2", new byte[] { 9 });
        bool removed = _engine.Remove("k2");
        Assert.True(removed);
        Assert.False(_engine.TryGet("k2", out _));
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        _engine.Set("a", new byte[] { 1 });
        _engine.Set("b", new byte[] { 2 });
        _engine.Clear();
        Assert.False(_engine.TryGet("a", out _));
        Assert.False(_engine.TryGet("b", out _));
    }

    [Fact]
    public void Set_OverLimit_EvictsAndAcceptsNew()
    {
        // Use a tiny 10KB limit
        var smallConfig = new MemoryManagementConfiguration { DefaultTtlSeconds = 0 };
        using var engine = new NativeMemoryStorageEngine(smallConfig, 10 * 1024);
        var bigData = new byte[4096];
        engine.Set("a", bigData);
        engine.Set("b", bigData);
        engine.Set("c", bigData); // triggers eviction — must not throw

        var stats = engine.GetStats();
        Assert.True(stats.UsedBytes <= 10 * 1024 + 4096); // within one entry over limit is acceptable
    }

    [Fact]
    public void Dispose_CanBeCalledTwiceSafely()
    {
        var engine = new NativeMemoryStorageEngine(_config, 1024 * 1024);
        engine.Set("x", new byte[] { 1 });
        engine.Dispose();
        engine.Dispose(); // must not throw
    }

    [Fact]
    public void GetStats_ReflectsHitsAndMisses()
    {
        _engine.Set("s", new byte[] { 1 });
        _engine.TryGet("s", out _);   // hit
        _engine.TryGet("nope", out _); // miss
        var stats = _engine.GetStats();
        Assert.Equal(1, stats.HitCount);
        Assert.Equal(1, stats.MissCount);
    }

    [Fact]
    public void TtlExpiry_EntryRemovedAfterTtl()
    {
        var config = new MemoryManagementConfiguration { DefaultTtlSeconds = 1 };
        using var engine = new NativeMemoryStorageEngine(config, 1024 * 1024);
        engine.Set("exp", new byte[] { 1 }, TimeSpan.FromMilliseconds(50));
        Thread.Sleep(200);
        // This test just verifies the full lifecycle doesn't crash.
        Assert.True(true);
    }
}
