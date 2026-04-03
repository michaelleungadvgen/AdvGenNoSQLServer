// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
using AdvGenNoSqlServer.Core.MemoryManagement;

namespace AdvGenNoSqlServer.Tests;

public class EvictionManagerTests
{
    [Fact]
    public void SelectVictims_LRU_ReturnsOldestFirst()
    {
        var mgr = new EvictionManager(EvictionPolicy.LRU, 1024);
        mgr.RecordSet("a", 100, null);
        Thread.Sleep(5);
        mgr.RecordSet("b", 100, null);
        Thread.Sleep(5);
        mgr.RecordAccess("b"); // b is now most-recently-used

        var victims = mgr.SelectVictims(100); // need 100 bytes
        Assert.Contains("a", victims);
        Assert.DoesNotContain("b", victims);
    }

    [Fact]
    public void SelectVictims_LFU_ReturnsLowestHitCountFirst()
    {
        var mgr = new EvictionManager(EvictionPolicy.LFU, 1024);
        mgr.RecordSet("a", 100, null);
        mgr.RecordSet("b", 100, null);
        mgr.RecordAccess("b");
        mgr.RecordAccess("b");

        var victims = mgr.SelectVictims(100);
        Assert.Contains("a", victims);
        Assert.DoesNotContain("b", victims);
    }

    [Fact]
    public void SelectVictims_TTL_ReturnsExpiredFirst()
    {
        var mgr = new EvictionManager(EvictionPolicy.TTL, 1024);
        mgr.RecordSet("a", 100, TimeSpan.FromMilliseconds(1)); // expires immediately
        mgr.RecordSet("b", 100, TimeSpan.FromHours(1));
        Thread.Sleep(10); // let "a" expire

        var victims = mgr.SelectVictims(0); // bytesNeeded=0 returns only expired
        Assert.Contains("a", victims);
        Assert.DoesNotContain("b", victims);
    }

    [Fact]
    public void SelectVictims_BytesNeededZero_ReturnsOnlyExpired()
    {
        var mgr = new EvictionManager(EvictionPolicy.LRU, 1024);
        mgr.RecordSet("a", 100, TimeSpan.FromMilliseconds(1));
        mgr.RecordSet("b", 100, null);
        Thread.Sleep(10);

        var victims = mgr.SelectVictims(0);
        Assert.Contains("a", victims);
        Assert.DoesNotContain("b", victims);
    }

    [Fact]
    public void SelectVictims_AccumulatesUntilBytesNeededMet()
    {
        var mgr = new EvictionManager(EvictionPolicy.LRU, 10_000);
        mgr.RecordSet("a", 50, null);
        mgr.RecordSet("b", 50, null);
        mgr.RecordSet("c", 50, null);

        // Only need 60 bytes; should stop after evicting 2 x 50 = 100 >= 60
        var victims = mgr.SelectVictims(60);
        Assert.True(victims.Count >= 2 && victims.Count <= 3);
    }

    [Fact]
    public void Remove_DecreasesUsedBytes()
    {
        var mgr = new EvictionManager(EvictionPolicy.LRU, 1024);
        mgr.RecordSet("a", 100, null);
        Assert.Equal(100, mgr.UsedBytes);
        mgr.Remove("a");
        Assert.Equal(0, mgr.UsedBytes);
    }

    [Fact]
    public void RecordAccess_UnknownKey_DoesNotThrow()
    {
        var mgr = new EvictionManager(EvictionPolicy.LRU, 1024);
        mgr.RecordAccess("unknown"); // must not throw
    }
}
