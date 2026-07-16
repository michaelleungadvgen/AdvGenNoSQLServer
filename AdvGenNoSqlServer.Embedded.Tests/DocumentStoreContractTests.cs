// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Abstractions;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Embedded;
using AdvGenNoSqlServer.Embedded.Storage;
using Xunit;

namespace AdvGenNoSqlServer.Embedded.Tests;

/// <summary>
/// Behavioral contract for <see cref="IDocumentStore"/>. Run against the existing in-memory
/// store (oracle) to validate the tests, then against the embedded backends. Uses explicit
/// ids throughout (empty-id handling is backend-specific and not part of the shared contract).
/// </summary>
public abstract class DocumentStoreContractTests
{
    protected abstract IDocumentStore CreateStore();

    private static Document Doc(string id, params (string Key, object Value)[] fields)
    {
        var data = new Dictionary<string, object>();
        foreach (var (k, v) in fields) data[k] = v;
        return new Document { Id = id, Data = data };
    }

    [Fact]
    public async Task Insert_ThenGet_RoundTrips()
    {
        var store = CreateStore();
        await store.InsertAsync("c", Doc("a", ("name", "Alice")));
        var got = await store.GetAsync("c", "a");
        Assert.NotNull(got);
        Assert.Equal("Alice", got!.Data!["name"]);
    }

    [Fact]
    public async Task Insert_SetsMetadata()
    {
        var store = CreateStore();
        var inserted = await store.InsertAsync("c", Doc("a", ("x", 1L)));
        Assert.Equal(1, inserted.Version);
        Assert.NotEqual(default, inserted.CreatedAt);
        Assert.NotEqual(default, inserted.UpdatedAt);
    }

    [Fact]
    public async Task Insert_Duplicate_Throws()
    {
        var store = CreateStore();
        await store.InsertAsync("c", Doc("a"));
        await Assert.ThrowsAsync<DocumentAlreadyExistsException>(() => store.InsertAsync("c", Doc("a")));
    }

    [Fact]
    public async Task Get_Missing_ReturnsNull()
    {
        var store = CreateStore();
        Assert.Null(await store.GetAsync("c", "nope"));
    }

    [Fact]
    public async Task Get_MissingCollection_ReturnsNull()
    {
        var store = CreateStore();
        Assert.Null(await store.GetAsync("ghost", "x"));
    }

    [Fact]
    public async Task GetMany_ReturnsExisting_SkipsMissing()
    {
        var store = CreateStore();
        await store.InsertAsync("c", Doc("a"));
        await store.InsertAsync("c", Doc("b"));
        var many = (await store.GetManyAsync("c", new[] { "a", "missing", "b" })).ToList();
        Assert.Equal(2, many.Count);
        Assert.Contains(many, d => d.Id == "a");
        Assert.Contains(many, d => d.Id == "b");
    }

    [Fact]
    public async Task GetAll_ReturnsAll()
    {
        var store = CreateStore();
        await store.InsertAsync("c", Doc("a"));
        await store.InsertAsync("c", Doc("b"));
        await store.InsertAsync("c", Doc("c"));
        var all = (await store.GetAllAsync("c")).Select(d => d.Id).ToHashSet();
        Assert.Equal(new HashSet<string> { "a", "b", "c" }, all);
    }

    [Fact]
    public async Task Update_ChangesData_BumpsVersion_KeepsCreatedAt()
    {
        var store = CreateStore();
        var inserted = await store.InsertAsync("c", Doc("a", ("v", 1L)));
        var updated = await store.UpdateAsync("c", Doc("a", ("v", 2L)));
        Assert.Equal(2, updated.Version);
        Assert.Equal(inserted.CreatedAt, updated.CreatedAt);
        Assert.Equal(2L, (await store.GetAsync("c", "a"))!.Data!["v"]);
    }

    [Fact]
    public async Task Update_Missing_Throws()
    {
        var store = CreateStore();
        await store.InsertAsync("c", Doc("a"));
        await Assert.ThrowsAsync<DocumentNotFoundException>(() => store.UpdateAsync("c", Doc("missing")));
    }

    [Fact]
    public async Task Update_MissingCollection_Throws()
    {
        var store = CreateStore();
        await Assert.ThrowsAsync<CollectionNotFoundException>(() => store.UpdateAsync("ghost", Doc("a")));
    }

    [Fact]
    public async Task Delete_Removes_ReturnsTrue()
    {
        var store = CreateStore();
        await store.InsertAsync("c", Doc("a"));
        Assert.True(await store.DeleteAsync("c", "a"));
        Assert.Null(await store.GetAsync("c", "a"));
    }

    [Fact]
    public async Task Delete_Missing_ReturnsFalse()
    {
        var store = CreateStore();
        Assert.False(await store.DeleteAsync("c", "nope"));
    }

    [Fact]
    public async Task Exists_Works()
    {
        var store = CreateStore();
        await store.InsertAsync("c", Doc("a"));
        Assert.True(await store.ExistsAsync("c", "a"));
        Assert.False(await store.ExistsAsync("c", "b"));
    }

    [Fact]
    public async Task Count_ReflectsInsertsAndDeletes()
    {
        var store = CreateStore();
        await store.InsertAsync("c", Doc("a"));
        await store.InsertAsync("c", Doc("b"));
        Assert.Equal(2, await store.CountAsync("c"));
        await store.DeleteAsync("c", "a");
        Assert.Equal(1, await store.CountAsync("c"));
    }

    [Fact]
    public async Task CreateCollection_ThenListed()
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("things");
        Assert.Contains("things", await store.GetCollectionsAsync());
    }

    [Fact]
    public async Task DropCollection_Removes()
    {
        var store = CreateStore();
        await store.InsertAsync("c", Doc("a"));
        Assert.True(await store.DropCollectionAsync("c"));
        Assert.DoesNotContain("c", await store.GetCollectionsAsync());
        Assert.False(await store.DropCollectionAsync("c"));
    }

    [Fact]
    public async Task ClearCollection_EmptiesButKeeps()
    {
        var store = CreateStore();
        await store.InsertAsync("c", Doc("a"));
        await store.InsertAsync("c", Doc("b"));
        await store.ClearCollectionAsync("c");
        Assert.Equal(0, await store.CountAsync("c"));
        Assert.Contains("c", await store.GetCollectionsAsync());
    }

    [Fact]
    public async Task DataValueTypes_RoundTrip()
    {
        var store = CreateStore();
        await store.InsertAsync("c", Doc("a",
            ("s", "text"), ("n", 42L), ("d", 3.5), ("b", true)));
        var got = await store.GetAsync("c", "a");
        Assert.Equal("text", got!.Data!["s"]);
        Assert.Equal(42L, got.Data["n"]);
        Assert.Equal(3.5, got.Data["d"]);
        Assert.Equal(true, got.Data["b"]);
    }
}

/// <summary>Validates the contract tests against the known-good in-memory store.</summary>
public class InMemoryOracleTests : DocumentStoreContractTests
{
    protected override IDocumentStore CreateStore() => new AdvGenNoSqlServer.Storage.DocumentStore();
}

/// <summary>Runs the contract against the embedded in-memory backend.</summary>
public class EmbeddedMemoryStoreTests : DocumentStoreContractTests
{
    protected override IDocumentStore CreateStore() => EmbeddedDocumentStore.CreateInMemory();
}

/// <summary>Runs the contract against the embedded file backend.</summary>
public class EmbeddedFileStoreTests : DocumentStoreContractTests, IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "agdb-tests", Guid.NewGuid().ToString("N"));
    private readonly List<EmbeddedDocumentStore> _stores = new();

    public EmbeddedFileStoreTests() => Directory.CreateDirectory(_dir);

    protected override IDocumentStore CreateStore()
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".agdb");
        var store = new EmbeddedDocumentStore(new FilePageStore(path));
        store.InitializeAsync().GetAwaiter().GetResult();
        _stores.Add(store);
        return store;
    }

    public void Dispose()
    {
        foreach (var s in _stores) s.Dispose();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }
}
