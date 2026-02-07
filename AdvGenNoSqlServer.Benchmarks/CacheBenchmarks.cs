// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Caching;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace AdvGenNoSqlServer.Benchmarks;

[SimpleJob(RuntimeMoniker.Net90, launchCount: 1, warmupCount: 3, iterationCount: 5)]
[MemoryDiagnoser]
[RankColumn]
public class CacheBenchmarks
{
    private LruCache<object> _cache = null!;
    private readonly List<string> _keys = new();
    private readonly Random _random = new(42);

    [Params(100, 1000, 10000)]
    public int CacheSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _cache = new LruCache<object>(CacheSize);

        // Pre-populate cache
        for (int i = 0; i < CacheSize; i++)
        {
            string key = $"key_{i}";
            _keys.Add(key);
            _cache.Set(key, new { Index = i, Data = $"Data{i}" });
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _cache.Dispose();
    }

    [Benchmark]
    public void CacheGetHit()
    {
        string key = _keys[_random.Next(_keys.Count)];
        _cache.Get(key);
    }

    [Benchmark]
    public void CacheGetMiss()
    {
        _cache.Get($"missing_key_{_random.Next(1000000)}");
    }

    [Benchmark]
    public void CacheSet()
    {
        string key = $"new_key_{_random.Next(1000000)}";
        _cache.Set(key, new { Value = _random.Next() });
    }

    [Benchmark]
    public void CacheSetExisting()
    {
        string key = _keys[_random.Next(_keys.Count)];
        _cache.Set(key, new { Updated = DateTime.UtcNow });
    }

    [Benchmark]
    public void CacheRemove()
    {
        // Use separate cache to avoid affecting other benchmarks
        var cache = new LruCache<object>(100);
        string key = "temp_key";
        cache.Set(key, "value");
        cache.Remove(key);
        cache.Dispose();
    }

    [Benchmark]
    public void CacheEvictionUnderPressure()
    {
        // Create a small cache and fill it beyond capacity
        var cache = new LruCache<object>(100);
        for (int i = 0; i < 200; i++)
        {
            cache.Set($"pressure_key_{i}", new { Data = i });
        }
        cache.Dispose();
    }

    [Benchmark]
    public void CacheGetStatistics()
    {
        _cache.GetStatistics();
    }
}
