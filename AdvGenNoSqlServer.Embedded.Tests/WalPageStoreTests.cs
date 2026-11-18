// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Embedded.Storage;
using Xunit;

namespace AdvGenNoSqlServer.Embedded.Tests;

public class WalPageStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "agdb-tests", Guid.NewGuid().ToString("N"));

    public WalPageStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private string DbPath() => Path.Combine(_dir, "walpage.agdb");

    [Fact]
    public void Rollback_RevertsFreeListAndAllocation()
    {
        var path = DbPath();
        using var store = new WalPageStore(new FilePageStore(path), path + ".wal", long.MaxValue);

        uint p1 = store.AllocatePage(PageType.Data);
        store.WritePage(Page.CreateNew(p1, PageType.Data));
        store.CommitTransaction();

        // Uncommitted free + reuse: rollback must restore the committed state
        // (p1 allocated, free list empty).
        store.FreePage(p1);
        uint reused = store.AllocatePage(PageType.Data);
        Assert.Equal(p1, reused);
        store.RollbackTransaction();

        uint fresh = store.AllocatePage(PageType.Data);
        Assert.NotEqual(p1, fresh);
        store.RollbackTransaction();

        // Committed free: rolling back a subsequent allocation must keep the
        // freed page reusable.
        store.FreePage(p1);
        store.CommitTransaction();
        uint a = store.AllocatePage(PageType.Data);
        Assert.Equal(p1, a);
        store.RollbackTransaction();
        uint b = store.AllocatePage(PageType.Data);
        Assert.Equal(p1, b);
        store.RollbackTransaction();
    }

    [Fact]
    public void Crash_CommittedMetadataReplayedWithPages()
    {
        var path = DbPath();
        var store = new WalPageStore(new FilePageStore(path), path + ".wal", long.MaxValue);
        uint p1 = store.AllocatePage(PageType.Data);
        store.WritePage(Page.CreateNew(p1, PageType.Data));
        store.CatalogRootPageId = p1;
        store.CommitTransaction();
        store.Abort(); // crash before any checkpoint

        using var reopened = new WalPageStore(new FilePageStore(path), path + ".wal", long.MaxValue);
        Assert.Equal(p1, reopened.CatalogRootPageId);
        Assert.True(reopened.PageCount > p1);
        Assert.Equal(p1, reopened.ReadPage(p1).PageId);
    }

    [Fact]
    public void LostWal_MainFileNeverReferencesUncheckpointedPages()
    {
        var path = DbPath();
        var store = new WalPageStore(new FilePageStore(path), path + ".wal", long.MaxValue);
        uint p1 = store.AllocatePage(PageType.Data);
        store.WritePage(Page.CreateNew(p1, PageType.Data));
        store.CatalogRootPageId = p1;
        store.CommitTransaction();
        store.Abort();

        // Total WAL loss: the main file must still open at its last checkpoint
        // state, with no dangling catalog-root or free-list references.
        File.Delete(path + ".wal");
        using var reopened = new WalPageStore(new FilePageStore(path), path + ".wal", long.MaxValue);
        Assert.Equal(0u, reopened.CatalogRootPageId);
    }
}