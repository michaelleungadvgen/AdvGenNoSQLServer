// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Text;
using AdvGenNoSqlServer.Embedded.Storage;
using Xunit;

namespace AdvGenNoSqlServer.Embedded.Tests;

public class PageTests
{
    [Fact]
    public void HeaderRoundTrip()
    {
        var page = Page.CreateNew(pageId: 7, PageType.Data);
        page.NextPageId = 42;
        page.Seal();

        var reloaded = Page.FromBuffer(page.Buffer);
        Assert.Equal(7u, reloaded.PageId);
        Assert.Equal(PageType.Data, reloaded.Type);
        Assert.Equal(42u, reloaded.NextPageId);
        Assert.True(reloaded.Validate());
    }

    [Fact]
    public void ChecksumDetectsCorruption()
    {
        var page = Page.CreateNew(1, PageType.Data);
        page.TryAddRecord(Encoding.UTF8.GetBytes("hello"));
        page.Seal();
        Assert.True(page.Validate());

        // flip a body byte
        page.Buffer[100] ^= 0xFF;
        Assert.False(page.Validate());
    }

    [Fact]
    public void AddAndReadRecord()
    {
        var page = Page.CreateNew(1, PageType.Data);
        var data = Encoding.UTF8.GetBytes("the quick brown fox");
        int slot = page.TryAddRecord(data);
        Assert.True(slot >= 0);

        var read = page.ReadRecord(slot);
        Assert.Equal(data, read.ToArray());
    }

    [Fact]
    public void MultipleRecordsPreserveOrder()
    {
        var page = Page.CreateNew(1, PageType.Data);
        int s0 = page.TryAddRecord(Encoding.UTF8.GetBytes("first"));
        int s1 = page.TryAddRecord(Encoding.UTF8.GetBytes("second"));
        int s2 = page.TryAddRecord(Encoding.UTF8.GetBytes("third"));
        Assert.Equal(0, s0);
        Assert.Equal(1, s1);
        Assert.Equal(2, s2);
        Assert.Equal("first", Encoding.UTF8.GetString(page.ReadRecord(s0).ToArray()));
        Assert.Equal("second", Encoding.UTF8.GetString(page.ReadRecord(s1).ToArray()));
        Assert.Equal("third", Encoding.UTF8.GetString(page.ReadRecord(s2).ToArray()));
    }

    [Fact]
    public void RecordsSurviveReload()
    {
        var page = Page.CreateNew(3, PageType.Data);
        page.TryAddRecord(Encoding.UTF8.GetBytes("alpha"));
        page.TryAddRecord(Encoding.UTF8.GetBytes("beta"));
        page.Seal();

        var reloaded = Page.FromBuffer(page.Buffer);
        Assert.Equal(2, reloaded.SlotCount);
        Assert.Equal("alpha", Encoding.UTF8.GetString(reloaded.ReadRecord(0).ToArray()));
        Assert.Equal("beta", Encoding.UTF8.GetString(reloaded.ReadRecord(1).ToArray()));
    }

    [Fact]
    public void TryAddRecord_ReturnsMinusOne_WhenFull()
    {
        var page = Page.CreateNew(1, PageType.Data);
        var big = new byte[7000];
        Assert.True(page.TryAddRecord(big) >= 0);
        // Not enough room for another 7000-byte record + slot
        Assert.Equal(-1, page.TryAddRecord(new byte[7000]));
    }

    [Fact]
    public void DeleteRecord_Tombstones()
    {
        var page = Page.CreateNew(1, PageType.Data);
        int slot = page.TryAddRecord(Encoding.UTF8.GetBytes("gone"));
        page.DeleteRecord(slot);
        Assert.True(page.IsSlotDeleted(slot));
        Assert.Throws<InvalidOperationException>(() => page.ReadRecord(slot));
    }

    [Fact]
    public void FreeBytesShrinksAsRecordsAdded()
    {
        var page = Page.CreateNew(1, PageType.Data);
        int initial = page.FreeBytes;
        page.TryAddRecord(new byte[100]);
        Assert.Equal(initial - 100 - 4, page.FreeBytes); // 100 body + 4 slot
    }
}
