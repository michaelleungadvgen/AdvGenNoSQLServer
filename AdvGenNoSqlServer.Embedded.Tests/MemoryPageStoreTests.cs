// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Text;
using AdvGenNoSqlServer.Embedded.Storage;
using Xunit;

namespace AdvGenNoSqlServer.Embedded.Tests;

public class MemoryPageStoreTests
{
    [Fact]
    public void Allocate_ReturnsIncreasingIds_StartingAtOne()
    {
        using var store = new MemoryPageStore();
        Assert.Equal(1u, store.AllocatePage(PageType.Data));
        Assert.Equal(2u, store.AllocatePage(PageType.Data));
        Assert.Equal(3u, store.AllocatePage(PageType.Data));
    }

    [Fact]
    public void WriteThenRead_RoundTripsContent()
    {
        using var store = new MemoryPageStore();
        uint id = store.AllocatePage(PageType.Data);
        var page = store.ReadPage(id);
        page.TryAddRecord(Encoding.UTF8.GetBytes("payload"));
        store.WritePage(page);

        var reread = store.ReadPage(id);
        Assert.Equal("payload", Encoding.UTF8.GetString(reread.ReadRecord(0).ToArray()));
    }

    [Fact]
    public void FreePage_ThenAllocate_ReusesId()
    {
        using var store = new MemoryPageStore();
        uint a = store.AllocatePage(PageType.Data);
        uint b = store.AllocatePage(PageType.Data);
        store.FreePage(a);
        uint c = store.AllocatePage(PageType.Data);
        Assert.Equal(a, c);
        Assert.NotEqual(b, c);
    }

    [Fact]
    public void ReadUnallocated_Throws()
    {
        using var store = new MemoryPageStore();
        Assert.Throws<ArgumentOutOfRangeException>(() => store.ReadPage(99));
    }

    [Fact]
    public void ReadStored_ReturnsIndependentCopy()
    {
        using var store = new MemoryPageStore();
        uint id = store.AllocatePage(PageType.Data);
        var page = store.ReadPage(id);
        page.TryAddRecord(Encoding.UTF8.GetBytes("x"));
        // did not WritePage -> store must be unaffected
        var reread = store.ReadPage(id);
        Assert.Equal(0, reread.SlotCount);
    }
}
