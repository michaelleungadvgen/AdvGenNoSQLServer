// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Text;
using AdvGenNoSqlServer.Embedded.Storage;
using Xunit;

namespace AdvGenNoSqlServer.Embedded.Tests;

public class FilePageStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "agdb-tests", Guid.NewGuid().ToString("N"));

    public FilePageStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private string DbPath() => Path.Combine(_dir, "test.agdb");

    [Fact]
    public void CreateFresh_FileExists_HeaderValid()
    {
        var path = DbPath();
        using (var store = new FilePageStore(path))
        {
            Assert.Equal(1u, store.PageCount);
        }
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void WriteRead_SurvivesReopen()
    {
        var path = DbPath();
        uint id;
        using (var store = new FilePageStore(path))
        {
            id = store.AllocatePage(PageType.Data);
            var page = store.ReadPage(id);
            page.TryAddRecord(Encoding.UTF8.GetBytes("persisted"));
            store.WritePage(page);
        }
        using (var reopened = new FilePageStore(path))
        {
            var page = reopened.ReadPage(id);
            Assert.Equal("persisted", Encoding.UTF8.GetString(page.ReadRecord(0).ToArray()));
        }
    }

    [Fact]
    public void FreeList_PersistsAcrossReopen()
    {
        var path = DbPath();
        uint a;
        using (var store = new FilePageStore(path))
        {
            a = store.AllocatePage(PageType.Data);
            store.AllocatePage(PageType.Data);
            store.FreePage(a);
        }
        using (var reopened = new FilePageStore(path))
        {
            uint reused = reopened.AllocatePage(PageType.Data);
            Assert.Equal(a, reused);
        }
    }

    [Fact]
    public void CatalogRoot_PersistsAcrossReopen()
    {
        var path = DbPath();
        using (var store = new FilePageStore(path))
        {
            store.CatalogRootPageId = 5;
        }
        using (var reopened = new FilePageStore(path))
        {
            Assert.Equal(5u, reopened.CatalogRootPageId);
        }
    }

    [Fact]
    public void SecondOpen_Throws_DatabaseLocked()
    {
        var path = DbPath();
        using var first = new FilePageStore(path);
        Assert.Throws<EmbeddedDatabaseLockedException>(() => new FilePageStore(path));
    }

    [Fact]
    public void CorruptDataPage_Throws_Corruption()
    {
        var path = DbPath();
        uint id;
        using (var store = new FilePageStore(path))
        {
            id = store.AllocatePage(PageType.Data);
            var page = store.ReadPage(id);
            page.TryAddRecord(Encoding.UTF8.GetBytes("data"));
            store.WritePage(page);
        }

        // Flip a byte in the middle of page `id`'s body on disk.
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            long pos = (long)id * Page.PageSize + 100;
            fs.Seek(pos, SeekOrigin.Begin);
            int b = fs.ReadByte();
            fs.Seek(pos, SeekOrigin.Begin);
            fs.WriteByte((byte)(b ^ 0xFF));
        }

        using var reopened = new FilePageStore(path);
        var ex = Assert.Throws<EmbeddedDataCorruptionException>(() => reopened.ReadPage(id));
        Assert.Contains(id.ToString(), ex.Message);
    }
}
