// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Embedded;
using Xunit;
using Xunit.Abstractions;

namespace AdvGenNoSqlServer.Embedded.Tests;

/// <summary>
/// Randomized operations mirrored against a Dictionary oracle. The seed is logged and can be
/// pinned via the AGDB_TEST_SEED env var for reproduction. Runs file-backed and in-memory.
/// </summary>
public class PropertyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "agdb-tests", Guid.NewGuid().ToString("N"));
    private readonly ITestOutputHelper _out;

    public PropertyTests(ITestOutputHelper output) { _out = output; Directory.CreateDirectory(_dir); }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    public sealed class Item
    {
        public string Id { get; set; } = "";
        public string Bucket { get; set; } = "";
        public long Value { get; set; }
        public bool Flag { get; set; }
    }

    private static int Seed()
    {
        var env = Environment.GetEnvironmentVariable("AGDB_TEST_SEED");
        return int.TryParse(env, out var s) ? s : Environment.TickCount;
    }

    [Theory]
    [InlineData(true)]   // file-backed (with reopen)
    [InlineData(false)]  // :memory:
    public void RandomOps_MatchOracle(bool fileBacked)
    {
        int seed = Seed();
        _out.WriteLine($"seed={seed} fileBacked={fileBacked}");
        var rng = new Random(seed);
        var oracle = new Dictionary<string, Item>();
        var path = Path.Combine(_dir, $"prop-{fileBacked}-{seed}.agdb");

        AdvGenDatabase db = fileBacked ? new AdvGenDatabase(path) : new AdvGenDatabase(":memory:");
        var col = db.GetCollection<Item>("items");
        col.EnsureIndex(x => x.Bucket);

        try
        {
            int nextId = 0;
            for (int op = 0; op < 2000; op++)
            {
                int roll = rng.Next(100);
                if (roll < 40) // insert
                {
                    var item = new Item
                    {
                        Id = $"k{nextId++}",
                        Bucket = $"b{rng.Next(8)}",
                        Value = rng.Next(1000),
                        Flag = rng.Next(2) == 0
                    };
                    col.Insert(item);
                    oracle[item.Id] = Clone(item);
                }
                else if (roll < 65) // update
                {
                    var key = RandomKey(oracle, rng);
                    if (key != null)
                    {
                        var item = new Item { Id = key, Bucket = $"b{rng.Next(8)}", Value = rng.Next(1000), Flag = rng.Next(2) == 0 };
                        col.Update(item);
                        oracle[key] = Clone(item);
                    }
                }
                else if (roll < 80) // delete
                {
                    var key = RandomKey(oracle, rng);
                    if (key != null)
                    {
                        col.Delete(key);
                        oracle.Remove(key);
                    }
                }
                else if (roll < 95) // find by predicate
                {
                    string bucket = $"b{rng.Next(8)}";
                    var got = col.Find(x => x.Bucket == bucket).Select(x => x.Id).ToHashSet();
                    var expected = oracle.Values.Where(x => x.Bucket == bucket).Select(x => x.Id).ToHashSet();
                    Assert.True(expected.SetEquals(got), $"predicate mismatch (seed={seed}) bucket={bucket}: expected {expected.Count}, got {got.Count}");
                }
                else if (fileBacked) // reopen
                {
                    db.Dispose();
                    db = new AdvGenDatabase(path);
                    col = db.GetCollection<Item>("items");
                    AssertFullEquality(col, oracle, seed);
                }

                // Spot probe after each op.
                if (oracle.Count > 0)
                {
                    var probeKey = RandomKey(oracle, rng)!;
                    var got = col.FindById(probeKey);
                    Assert.NotNull(got);
                    Assert.Equal(oracle[probeKey].Value, got!.Value);
                    Assert.Equal(oracle[probeKey].Bucket, got.Bucket);
                }
            }

            AssertFullEquality(col, oracle, seed);
        }
        finally
        {
            db.Dispose();
        }
    }

    private static void AssertFullEquality(AdvGenNoSqlServer.Embedded.Typed.IEmbeddedCollection<Item> col, Dictionary<string, Item> oracle, int seed)
    {
        Assert.Equal(oracle.Count, col.Count());
        foreach (var kv in oracle)
        {
            var got = col.FindById(kv.Key);
            Assert.True(got != null, $"missing {kv.Key} (seed={seed})");
            Assert.Equal(kv.Value.Value, got!.Value);
            Assert.Equal(kv.Value.Bucket, got.Bucket);
            Assert.Equal(kv.Value.Flag, got.Flag);
        }
    }

    private static string? RandomKey(Dictionary<string, Item> oracle, Random rng)
    {
        if (oracle.Count == 0) return null;
        int idx = rng.Next(oracle.Count);
        return oracle.Keys.ElementAt(idx);
    }

    private static Item Clone(Item i) => new() { Id = i.Id, Bucket = i.Bucket, Value = i.Value, Flag = i.Flag };
}
