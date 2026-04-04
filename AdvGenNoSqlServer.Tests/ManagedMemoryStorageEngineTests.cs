// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
using AdvGenNoSqlServer.Core.MemoryManagement;

namespace AdvGenNoSqlServer.Tests;

public class ManagedMemoryStorageEngineTests : IDisposable
{
    private readonly ManagedMemoryStorageEngine _engine = new(
        new MemoryManagementConfiguration { DefaultTtlSeconds = 0 }, 64 * 1024 * 1024);

    public void Dispose() => _engine.Dispose();

    [Fact]
    public void Set_ThenTryGet_ReturnsSameBytes()
    {
        byte[] data = [5, 6, 7];
        _engine.Set("k1", data);
        bool found = _engine.TryGet("k1", out var result);
        Assert.True(found);
        Assert.Equal(data, result.ToArray());
    }

    [Fact]
    public void TryGet_ReturnsACopy_NotThePooledBuffer()
    {
        byte[] data = new byte[128];
        data[0] = 42;
        _engine.Set("safe", data);

        _engine.TryGet("safe", out var span1);
        _engine.TryGet("safe", out var span2);

        // Both reads should independently return the same data — not racing
        Assert.Equal(42, span1[0]);
        Assert.Equal(42, span2[0]);
    }

    [Fact]
    public void Remove_ExistingKey_ReturnsTrueAndKeyGone()
    {
        _engine.Set("k2", new byte[] { 1 });
        Assert.True(_engine.Remove("k2"));
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
    public void GetStats_ReturnsCorrectPlan()
    {
        var stats = _engine.GetStats();
        Assert.Equal("Managed", stats.Plan);
    }
}
