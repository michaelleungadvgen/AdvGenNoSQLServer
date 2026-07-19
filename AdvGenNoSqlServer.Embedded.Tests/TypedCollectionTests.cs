// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Embedded;
using Xunit;

namespace AdvGenNoSqlServer.Embedded.Tests;

public class TypedCollectionTests
{
    public sealed class Item
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Brand { get; set; } = "";
        public string Barcode { get; set; } = "";
        public decimal Price { get; set; }
        public bool IsActive { get; set; }
    }

    private static AdvGenDatabase Db() => new(":memory:");

    [Fact]
    public void Insert_AssignsId_SetsOnEntity()
    {
        using var db = Db();
        var col = db.GetCollection<Item>("items");
        var item = new Item { Name = "Milk", Barcode = "111" };
        var id = col.Insert(item);
        Assert.False(string.IsNullOrEmpty(id));
        Assert.Equal(id, item.Id);
        Assert.Equal("Milk", col.FindById(id)!.Name);
    }

    [Fact]
    public void Upsert_InsertsThenUpdates()
    {
        using var db = Db();
        var col = db.GetCollection<Item>("items");
        var item = new Item { Id = "a", Name = "One" };
        Assert.True(col.Upsert(item));   // inserted
        item.Name = "Two";
        Assert.False(col.Upsert(item));  // updated
        Assert.Equal("Two", col.FindById("a")!.Name);
        Assert.Equal(1, col.Count());
    }

    [Fact]
    public void CrudRoundTrip()
    {
        using var db = Db();
        var col = db.GetCollection<Item>("items");
        col.Insert(new Item { Id = "a", Name = "A", Price = 1.5m });
        col.Insert(new Item { Id = "b", Name = "B", Price = 2.5m });

        Assert.Equal(2, col.Count());
        Assert.Equal("A", col.FindById("a")!.Name);
        Assert.NotNull(col.FindOne(x => x.Name == "B"));

        var a = col.FindById("a")!;
        a.Name = "A-updated";
        Assert.True(col.Update(a));
        Assert.Equal("A-updated", col.FindById("a")!.Name);

        Assert.True(col.Delete("b"));
        Assert.Equal(1, col.Count());
    }

    [Fact]
    public void Find_Translatable_And_Fallback_MatchLinq()
    {
        using var db = Db();
        var col = db.GetCollection<Item>("items");
        var data = new List<Item>();
        for (int i = 0; i < 50; i++)
        {
            var it = new Item { Id = $"i{i}", Name = $"name{i}", Price = i, IsActive = i % 2 == 0 };
            data.Add(it);
            col.Insert(it);
        }

        // Translatable: Price < 10 && IsActive
        var translated = col.Find(x => x.Price < 10m && x.IsActive).Select(x => x.Id).ToHashSet();
        var expected1 = data.Where(x => x.Price < 10m && x.IsActive).Select(x => x.Id).ToHashSet();
        Assert.Equal(expected1, translated);

        // Untranslatable: string method forces fallback
        long before = db.Diagnostics.FallbackQueryCount;
        var fb = col.Find(x => x.Name.EndsWith("5")).Select(x => x.Id).ToHashSet();
        var expected2 = data.Where(x => x.Name.EndsWith("5")).Select(x => x.Id).ToHashSet();
        Assert.Equal(expected2, fb);
        Assert.True(db.Diagnostics.FallbackQueryCount > before);
    }

    [Fact]
    public void IndexedFind_EqualsUnindexed()
    {
        using var db = Db();
        var col = db.GetCollection<Item>("items");
        for (int i = 0; i < 100; i++)
            col.Insert(new Item { Id = $"i{i}", Barcode = $"bc{i % 10}" });

        var unindexed = col.Find(x => x.Barcode == "bc3").Select(x => x.Id).ToHashSet();
        col.EnsureIndex(x => x.Barcode);
        var indexed = col.Find(x => x.Barcode == "bc3").Select(x => x.Id).ToHashSet();
        Assert.Equal(unindexed, indexed);
        Assert.Equal(10, indexed.Count);
    }

    [Fact]
    public void UniqueIndex_Violation_Throws()
    {
        using var db = Db();
        var col = db.GetCollection<Item>("items");
        col.EnsureIndex(x => x.Barcode, unique: true);
        col.Insert(new Item { Id = "a", Barcode = "X" });
        Assert.Throws<DuplicateKeyException>(() => col.Insert(new Item { Id = "b", Barcode = "X" }));
    }

    [Fact]
    public void DeleteMany_Works()
    {
        using var db = Db();
        var col = db.GetCollection<Item>("items");
        for (int i = 0; i < 20; i++)
            col.Insert(new Item { Id = $"i{i}", Price = i });
        int deleted = col.DeleteMany(x => x.Price < 5m);
        Assert.Equal(5, deleted);
        Assert.Equal(15, col.Count());
    }

    [Fact]
    public async Task CountAsync_NoPredicate_CountsAll()
    {
        using var db = Db();
        var col = db.GetCollection<Item>("items");
        for (int i = 0; i < 7; i++)
            await col.InsertAsync(new Item { Id = $"i{i}" });
        Assert.Equal(7, await col.CountAsync());
    }

    [Fact]
    public async Task CountAsync_WithPredicate_CountsMatches()
    {
        using var db = Db();
        var col = db.GetCollection<Item>("items");
        for (int i = 0; i < 20; i++)
            await col.InsertAsync(new Item { Id = $"i{i}", Price = i, IsActive = i % 2 == 0 });

        Assert.Equal(5, await col.CountAsync(x => x.Price < 10m && x.IsActive));
        Assert.Equal(0, await col.CountAsync(x => x.Price > 1000m));
    }

    [Fact]
    public async Task DeleteManyAsync_Works()
    {
        using var db = Db();
        var col = db.GetCollection<Item>("items");
        for (int i = 0; i < 20; i++)
            await col.InsertAsync(new Item { Id = $"i{i}", Price = i });

        int deleted = await col.DeleteManyAsync(x => x.Price < 5m);
        Assert.Equal(5, deleted);
        Assert.Equal(15, await col.CountAsync());
    }

    [Fact]
    public async Task DeleteManyAsync_MatchAllAndNoMatch()
    {
        using var db = Db();
        var col = db.GetCollection<Item>("items");
        for (int i = 0; i < 5; i++)
            await col.InsertAsync(new Item { Id = $"i{i}", Price = i });

        Assert.Equal(0, await col.DeleteManyAsync(x => x.Price > 100m));
        Assert.Equal(5, await col.CountAsync());

        Assert.Equal(5, await col.DeleteManyAsync(x => true));
        Assert.Equal(0, await col.CountAsync());
    }
}
