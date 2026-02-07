// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Storage.Indexing;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace AdvGenNoSqlServer.Benchmarks;

[SimpleJob(RuntimeMoniker.Net90, launchCount: 1, warmupCount: 3, iterationCount: 5)]
[MemoryDiagnoser]
[RankColumn]
public class BTreeIndexBenchmarks
{
    private BTreeIndex<int, string> _intIndex = null!;
    private BTreeIndex<string, string> _stringIndex = null!;
    private readonly List<int> _intKeys = new();
    private readonly List<string> _stringKeys = new();
    private readonly Random _random = new(42);

    [Params(100, 1000, 10000)]
    public int KeyCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _intIndex = new BTreeIndex<int, string>("int_index", "benchmark", "intField");
        _stringIndex = new BTreeIndex<string, string>("string_index", "benchmark", "stringField");

        // Pre-populate indexes
        for (int i = 0; i < KeyCount; i++)
        {
            int intKey = _random.Next(1, KeyCount * 10);
            string stringKey = $"key_{i:D8}";
            string value = $"value_{i}";

            _intIndex.Insert(intKey, value);
            _stringIndex.Insert(stringKey, value);

            _intKeys.Add(intKey);
            _stringKeys.Add(stringKey);
        }
    }

    [Benchmark]
    public void InsertIntKey()
    {
        int key = _random.Next(KeyCount * 10, KeyCount * 20);
        _intIndex.Insert(key, $"value_{key}");
    }

    [Benchmark]
    public void InsertStringKey()
    {
        string key = $"new_key_{_random.Next():D8}";
        _stringIndex.Insert(key, $"value_{key}");
    }

    [Benchmark]
    public void SearchIntKey()
    {
        int key = _intKeys[_random.Next(_intKeys.Count)];
        _intIndex.TryGetValue(key, out _);
    }

    [Benchmark]
    public void SearchStringKey()
    {
        string key = _stringKeys[_random.Next(_stringKeys.Count)];
        _stringIndex.TryGetValue(key, out _);
    }

    [Benchmark]
    public void RangeQueryInt()
    {
        int min = _random.Next(1, KeyCount * 5);
        int max = min + KeyCount;
        _intIndex.RangeQuery(min, max).ToList();
    }

    [Benchmark]
    public void RangeQueryString()
    {
        int start = _random.Next(0, Math.Max(1, _stringKeys.Count - 100));
        string min = $"key_{start:D8}";
        string max = $"key_{(start + 100):D8}";
        _stringIndex.RangeQuery(min, max).ToList();
    }

    [Benchmark]
    public void DeleteIntKey()
    {
        // Use a separate index to not affect other benchmarks
        var index = new BTreeIndex<int, string>("delete_int", "benchmark", "field");
        int key = _random.Next(1000000);
        index.Insert(key, "value");
        index.Delete(key);
    }

    [Benchmark]
    public void IterateAllIntKeys()
    {
        _intIndex.GetAll().ToList();
    }

    [Benchmark]
    public void IterateAllStringKeys()
    {
        _stringIndex.GetAll().ToList();
    }
}
