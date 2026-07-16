// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Embedded;
using Xunit;

namespace AdvGenNoSqlServer.Embedded.Tests;

public class IndexRegistryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "agdb-tests", Guid.NewGuid().ToString("N"));

    public IndexRegistryTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static Document Doc(string id, string barcode) =>
        new() { Id = id, Data = new() { ["Barcode"] = barcode } };

    private List<string> LookupByBarcode(EmbeddedDocumentStore store, string barcode)
    {
        var idx = store.Indexes.Manager.GetIndex<string>("items", "Barcode");
        Assert.NotNull(idx);
        return idx!.GetValues(barcode).ToList();
    }

    [Fact]
    public async Task EnsureIndex_Empty_ThenInsert_LookupWorks()
    {
        using var store = EmbeddedDocumentStore.CreateInMemory();
        Assert.True(await store.EnsureIndexAsync("items", "Barcode"));
        await store.InsertAsync("items", Doc("a", "111"));
        await store.InsertAsync("items", Doc("b", "222"));
        Assert.Equal(new[] { "a" }, LookupByBarcode(store, "111"));
    }

    [Fact]
    public async Task EnsureIndex_Populated_Backfills()
    {
        using var store = EmbeddedDocumentStore.CreateInMemory();
        await store.InsertAsync("items", Doc("a", "111"));
        await store.InsertAsync("items", Doc("b", "222"));
        Assert.True(await store.EnsureIndexAsync("items", "Barcode"));
        Assert.Equal(new[] { "b" }, LookupByBarcode(store, "222"));
    }

    [Fact]
    public async Task Update_MovesIndexEntry()
    {
        using var store = EmbeddedDocumentStore.CreateInMemory();
        await store.EnsureIndexAsync("items", "Barcode");
        await store.InsertAsync("items", Doc("a", "111"));
        await store.UpdateAsync("items", Doc("a", "999"));
        Assert.Empty(LookupByBarcode(store, "111"));
        Assert.Equal(new[] { "a" }, LookupByBarcode(store, "999"));
    }

    [Fact]
    public async Task Delete_RemovesIndexEntry()
    {
        using var store = EmbeddedDocumentStore.CreateInMemory();
        await store.EnsureIndexAsync("items", "Barcode");
        await store.InsertAsync("items", Doc("a", "111"));
        await store.DeleteAsync("items", "a");
        Assert.Empty(LookupByBarcode(store, "111"));
    }

    [Fact]
    public async Task UniqueIndex_DuplicateInsert_Throws_AndNotStored()
    {
        using var store = EmbeddedDocumentStore.CreateInMemory();
        await store.EnsureIndexAsync("items", "Barcode", unique: true);
        await store.InsertAsync("items", Doc("a", "111"));
        await Assert.ThrowsAsync<DuplicateKeyException>(() => store.InsertAsync("items", Doc("b", "111")));
        Assert.Equal(1, await store.CountAsync("items"));
        Assert.Null(await store.GetAsync("items", "b"));
    }

    [Fact]
    public async Task IndexDefinitions_SurviveReopen_AndRebuild()
    {
        var path = Path.Combine(_dir, "idx.agdb");
        using (var store = EmbeddedDocumentStore.OpenFile(path))
        {
            await store.EnsureIndexAsync("items", "Barcode", unique: true);
            await store.InsertAsync("items", Doc("a", "111"));
        }
        using (var reopened = EmbeddedDocumentStore.OpenFile(path))
        {
            // Indexed lookup works after rebuild.
            var idx = reopened.Indexes.Manager.GetIndex<string>("items", "Barcode");
            Assert.NotNull(idx);
            Assert.Equal(new[] { "a" }, idx!.GetValues("111").ToList());
            // Unique constraint still enforced after reopen.
            await Assert.ThrowsAsync<DuplicateKeyException>(() => reopened.InsertAsync("items", Doc("c", "111")));
        }
    }
}
