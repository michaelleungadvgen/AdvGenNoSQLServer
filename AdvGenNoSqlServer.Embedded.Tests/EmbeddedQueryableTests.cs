// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Embedded;
using Xunit;

namespace AdvGenNoSqlServer.Embedded.Tests;

public class EmbeddedQueryableTests
{
    public sealed class Item
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int Price { get; set; }
        public string Category { get; set; } = "";
        public bool Active { get; set; }
    }

    private static (AdvGenDatabase Db, IEmbeddedCollectionShim Col, List<Item> Data) Seed(int n = 60)
    {
        var db = new AdvGenDatabase(":memory:");
        var col = db.GetCollection<Item>("items");
        var data = new List<Item>();
        var cats = new[] { "A", "B", "C" };
        for (int i = 0; i < n; i++)
        {
            var it = new Item { Id = $"i{i:D2}", Name = $"n{i}", Price = i, Category = cats[i % 3], Active = i % 2 == 0 };
            data.Add(it);
            col.Insert(it);
        }
        return (db, new ShimImpl(col), data);
    }

    // Small shim so the test reads cleanly regardless of interface namespace.
    public interface IEmbeddedCollectionShim { AdvGenNoSqlServer.Embedded.Typed.IEmbeddedQueryable<Item> Query(); }
    private sealed class ShimImpl : IEmbeddedCollectionShim
    {
        private readonly AdvGenNoSqlServer.Embedded.Typed.IEmbeddedCollection<Item> _c;
        public ShimImpl(AdvGenNoSqlServer.Embedded.Typed.IEmbeddedCollection<Item> c) => _c = c;
        public AdvGenNoSqlServer.Embedded.Typed.IEmbeddedQueryable<Item> Query() => _c.Query();
    }

    [Fact]
    public void ChainedWhere_AndsCorrectly()
    {
        var (db, col, data) = Seed();
        using var _ = db;
        var got = col.Query().Where(x => x.Price > 10).Where(x => x.Category == "A").ToList();
        var expected = data.Where(x => x.Price > 10 && x.Category == "A").Select(x => x.Id).ToHashSet();
        Assert.Equal(expected, got.Select(x => x.Id).ToHashSet());
    }

    [Fact]
    public void MultiKeySort_MatchesLinq()
    {
        var (db, col, data) = Seed();
        using var _ = db;
        var got = col.Query().OrderBy(x => x.Category).OrderByDescending(x => x.Price).ToList();
        var expected = data.OrderBy(x => x.Category).ThenByDescending(x => x.Price).Select(x => x.Id).ToList();
        Assert.Equal(expected, got.Select(x => x.Id).ToList());
    }

    [Fact]
    public void Skip_Limit_Paging()
    {
        var (db, col, data) = Seed();
        using var _ = db;
        var got = col.Query().OrderBy(x => x.Price).Skip(5).Limit(10).ToList();
        var expected = data.OrderBy(x => x.Price).Skip(5).Take(10).Select(x => x.Id).ToList();
        Assert.Equal(expected, got.Select(x => x.Id).ToList());
    }

    [Fact]
    public void MixedTranslatableAndFallback_StillCorrect()
    {
        var (db, col, data) = Seed();
        using var _ = db;
        // Second predicate (EndsWith) is untranslatable -> whole thing falls back, still correct.
        var got = col.Query()
            .Where(x => x.Price > 5)
            .Where(x => x.Name.EndsWith("7"))
            .OrderBy(x => x.Price)
            .ToList();
        var expected = data.Where(x => x.Price > 5 && x.Name.EndsWith("7"))
            .OrderBy(x => x.Price).Select(x => x.Id).ToList();
        Assert.Equal(expected, got.Select(x => x.Id).ToList());
    }

    [Fact]
    public void FirstAndCount()
    {
        var (db, col, data) = Seed();
        using var _ = db;
        var q = col.Query().Where(x => x.Active).OrderBy(x => x.Price);
        Assert.Equal(data.Count(x => x.Active), q.Count());
        Assert.Equal(data.Where(x => x.Active).OrderBy(x => x.Price).First().Id, col.Query().Where(x => x.Active).OrderBy(x => x.Price).First().Id);
        Assert.Null(col.Query().Where(x => x.Price > 100000).FirstOrDefault());
    }
}
