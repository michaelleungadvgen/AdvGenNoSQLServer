// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Embedded;
using AdvGenNoSqlServer.Query.Execution;
using AdvGenNoSqlServer.Query.Filtering;
using AdvGenNoSqlServer.Query.Models;
using Xunit;

namespace AdvGenNoSqlServer.Embedded.Tests;

public class EmbeddedQueryTests
{
    private sealed record Seed(string Id, long Price, string Category, bool Active);

    private static (EmbeddedDocumentStore Store, EmbeddedCollection Col, List<Seed> Data) Build(int n = 300)
    {
        var store = EmbeddedDocumentStore.CreateInMemory();
        var executor = new QueryExecutor(store, new FilterEngine(), store.Indexes.Manager);
        var col = new EmbeddedCollection("items", store, executor);
        var data = new List<Seed>();
        var cats = new[] { "A", "B", "C" };
        for (int i = 0; i < n; i++)
        {
            var s = new Seed($"id-{i:D3}", i, cats[i % 3], i % 2 == 0);
            data.Add(s);
            col.InsertAsync(new Document
            {
                Id = s.Id,
                Data = new() { ["price"] = s.Price, ["category"] = s.Category, ["active"] = s.Active }
            }).GetAwaiter().GetResult();
        }
        return (store, col, data);
    }

    private static HashSet<string> Ids(IEnumerable<Document> docs) => docs.Select(d => d.Id).ToHashSet();
    private static HashSet<string> Ids(IEnumerable<Seed> seeds) => seeds.Select(s => s.Id).ToHashSet();

    [Fact]
    public async Task Gt_Filter_MatchesLinq()
    {
        var (store, col, data) = Build();
        using var _ = store;
        var got = await col.FindAsync(QueryFilter.Gt("price", 100L));
        Assert.Equal(Ids(data.Where(s => s.Price > 100)), Ids(got));
    }

    [Fact]
    public async Task Lte_Filter_MatchesLinq()
    {
        var (store, col, data) = Build();
        using var _ = store;
        var got = await col.FindAsync(QueryFilter.Lte("price", 50L));
        Assert.Equal(Ids(data.Where(s => s.Price <= 50)), Ids(got));
    }

    [Fact]
    public async Task In_Filter_MatchesLinq()
    {
        var (store, col, data) = Build();
        using var _ = store;
        var got = await col.FindAsync(QueryFilter.In("category", new object[] { "A", "B" }));
        Assert.Equal(Ids(data.Where(s => s.Category is "A" or "B")), Ids(got));
    }

    [Fact]
    public async Task And_Filter_MatchesLinq()
    {
        var (store, col, data) = Build();
        using var _ = store;
        var filter = QueryFilter.Gt("price", 100L).And(QueryFilter.Eq("category", "A"));
        var got = await col.FindAsync(filter);
        Assert.Equal(Ids(data.Where(s => s.Price > 100 && s.Category == "A")), Ids(got));
    }

    [Fact]
    public async Task Or_Filter_MatchesLinq()
    {
        var (store, col, data) = Build();
        using var _ = store;
        var filter = QueryFilter.Eq("category", "A").Or(QueryFilter.Eq("category", "C"));
        var got = await col.FindAsync(filter);
        Assert.Equal(Ids(data.Where(s => s.Category is "A" or "C")), Ids(got));
    }

    [Fact]
    public async Task Sort_Skip_Limit_Paging()
    {
        var (store, col, data) = Build();
        using var _ = store;
        var sort = new List<SortField> { SortField.Descending("price") };
        var got = await col.FindAsync(sort: sort, skip: 10, limit: 5);
        var expected = data.OrderByDescending(s => s.Price).Skip(10).Take(5).Select(s => s.Id).ToList();
        Assert.Equal(expected, got.Select(d => d.Id).ToList());
    }

    [Fact]
    public async Task Count_WithFilter_MatchesLinq()
    {
        var (store, col, data) = Build();
        using var _ = store;
        long count = await col.CountAsync(QueryFilter.Gte("price", 200L));
        Assert.Equal(data.Count(s => s.Price >= 200), count);
    }

    [Fact]
    public async Task IndexedField_Query_EqualsUnindexed()
    {
        var (store, col, data) = Build();
        using var _ = store;
        var unindexed = Ids(await col.FindAsync(QueryFilter.Eq("category", "B")));
        await col.EnsureIndexAsync("category");
        var indexed = Ids(await col.FindAsync(QueryFilter.Eq("category", "B")));
        Assert.Equal(unindexed, indexed);
        Assert.Equal(Ids(data.Where(s => s.Category == "B")), indexed);
    }
}
