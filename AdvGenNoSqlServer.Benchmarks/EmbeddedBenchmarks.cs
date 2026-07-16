// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Embedded;
using AdvGenNoSqlServer.Query.Models;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using LiteDB;

namespace AdvGenNoSqlServer.Benchmarks;

/// <summary>
/// Benchmarks the embedded database against LiteDB. Validates AD-3's assumption that a
/// cold open (index rebuild) stays acceptable. Run: <c>dotnet run -c Release -- embedded</c>.
/// </summary>
[SimpleJob(RuntimeMoniker.Net90, launchCount: 1, warmupCount: 2, iterationCount: 3)]
[MemoryDiagnoser]
public class EmbeddedBenchmarks
{
    public sealed class Item
    {
        public string Id { get; set; } = "";
        public string Bucket { get; set; } = "";
        public long Value { get; set; }
        public string Name { get; set; } = "";
    }

    [Params(100_000)]
    public int N { get; set; }

    private string _agPath = null!;
    private string _litePath = null!;
    private Item[] _data = null!;

    [GlobalSetup]
    public void Setup()
    {
        var dir = Path.Combine(Path.GetTempPath(), "agdb-bench", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _agPath = Path.Combine(dir, "bench.agdb");
        _litePath = Path.Combine(dir, "bench.litedb");

        _data = new Item[N];
        for (int i = 0; i < N; i++)
            _data[i] = new Item { Id = $"k{i}", Bucket = $"b{i % 100}", Value = i, Name = $"name{i}" };

        // Pre-populate a shared copy for lookup/query benchmarks (AdvGen).
        using var db = new AdvGenDatabase(_agPath);
        var col = db.GetCollection<Item>("items");
        col.EnsureIndex(x => x.Bucket);
        foreach (var it in _data) col.Insert(it);
    }

    [Benchmark]
    public void AdvGen_Insert()
    {
        var path = _agPath + ".ins" + Guid.NewGuid().ToString("N");
        using var db = new AdvGenDatabase(path);
        var col = db.GetCollection<Item>("items");
        foreach (var it in _data) col.Insert(it);
        CleanupFile(path);
    }

    [Benchmark]
    public void LiteDb_Insert()
    {
        var path = _litePath + ".ins" + Guid.NewGuid().ToString("N");
        using (var db = new LiteDatabase(path))
        {
            var col = db.GetCollection<Item>("items");
            col.InsertBulk(_data);
        }
        CleanupFile(path);
    }

    [Benchmark]
    public Item? AdvGen_PointLookup()
    {
        using var db = new AdvGenDatabase(_agPath);
        var col = db.GetCollection<Item>("items");
        return col.FindById($"k{N / 2}");
    }

    [Benchmark]
    public int AdvGen_IndexedQuery()
    {
        using var db = new AdvGenDatabase(_agPath);
        var col = db.GetCollection("items");
        return col.FindAsync(QueryFilter.Eq("Bucket", "b50")).GetAwaiter().GetResult().Count;
    }

    [Benchmark]
    public int AdvGen_ColdOpen()
    {
        // Open + rebuild all in-memory indexes from the file, then count.
        using var db = new AdvGenDatabase(_agPath);
        var col = db.GetCollection<Item>("items");
        return (int)col.Count();
    }

    private static void CleanupFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
        try { if (File.Exists(path + ".wal")) File.Delete(path + ".wal"); } catch { }
    }
}
