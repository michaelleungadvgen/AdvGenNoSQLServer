// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
using AdvGenNoSqlServer.Core.MemoryManagement;

namespace AdvGenNoSqlServer.Tests;

public class NativeCacheEntryHolderTests
{
    [Fact]
    public void AsSpan_ReturnsOriginalBytes()
    {
        byte[] data = [1, 2, 3, 4, 5];
        using var holder = new NativeCacheEntryHolder(data);
        var span = holder.AsSpan();
        Assert.Equal(data, span.ToArray());
    }

    [Fact]
    public void Dispose_CanBeCalledTwiceSafely()
    {
        byte[] data = [10, 20];
        var holder = new NativeCacheEntryHolder(data);
        holder.Dispose();
        holder.Dispose(); // must not throw
    }

    [Fact]
    public void Size_ReflectsDataLength()
    {
        byte[] data = new byte[64];
        using var holder = new NativeCacheEntryHolder(data);
        Assert.Equal(64, holder.Size);
    }
}
