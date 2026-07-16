// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Embedded.Storage;
using Xunit;

namespace AdvGenNoSqlServer.Embedded.Tests;

public class CatalogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "agdb-tests", Guid.NewGuid().ToString("N"));

    public CatalogTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private string DbPath() => Path.Combine(_dir, "cat.agdb");

    [Fact]
    public void AddCollectionsAndIndexes_RoundTripAcrossReopen()
    {
        var path = DbPath();
        using (var store = new FilePageStore(path))
        {
            var catalog = new Catalog(store, store.CatalogRootPageId, store.WritePage, id => store.CatalogRootPageId = id);
            catalog.Load();
            catalog.AddCollection("items");
            catalog.AddCollection("alerts");
            catalog.AddIndex(new IndexDef { Collection = "items", Field = "Barcode", Name = "idx_items_Barcode", Unique = true });
            catalog.AddIndex(new IndexDef { Collection = "items", Field = "Brand", Name = "idx_items_Brand", Unique = false });
        }

        using (var store = new FilePageStore(path))
        {
            var catalog = new Catalog(store, store.CatalogRootPageId, store.WritePage, id => store.CatalogRootPageId = id);
            catalog.Load();
            Assert.Contains(catalog.Collections, c => c.Name == "items");
            Assert.Contains(catalog.Collections, c => c.Name == "alerts");
            Assert.Equal(2, catalog.Indexes.Count(i => i.Collection == "items"));
            var barcode = catalog.Indexes.Single(i => i.Name == "idx_items_Barcode");
            Assert.True(barcode.Unique);
            Assert.Equal("Barcode", barcode.Field);
        }
    }

    [Fact]
    public void RemoveCollection_RemovesItsIndexes()
    {
        var path = DbPath();
        using var store = new FilePageStore(path);
        var catalog = new Catalog(store, store.CatalogRootPageId, store.WritePage, id => store.CatalogRootPageId = id);
        catalog.Load();
        catalog.AddCollection("items");
        catalog.AddIndex(new IndexDef { Collection = "items", Field = "Barcode", Name = "idx_items_Barcode", Unique = false });
        Assert.Single(catalog.Indexes);

        Assert.True(catalog.RemoveCollection("items"));
        Assert.Empty(catalog.Collections);
        Assert.Empty(catalog.Indexes);
    }

    [Fact]
    public void UpdateFirstPage_Persists()
    {
        var path = DbPath();
        using (var store = new FilePageStore(path))
        {
            var catalog = new Catalog(store, store.CatalogRootPageId, store.WritePage, id => store.CatalogRootPageId = id);
            catalog.Load();
            catalog.AddCollection("items");
            catalog.UpdateCollectionFirstPage("items", 42);
        }
        using (var store = new FilePageStore(path))
        {
            var catalog = new Catalog(store, store.CatalogRootPageId, store.WritePage, id => store.CatalogRootPageId = id);
            catalog.Load();
            Assert.Equal(42u, catalog.Collections.Single(c => c.Name == "items").FirstPage);
        }
    }
}
