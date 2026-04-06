// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
using AdvGenNoSqlServer.Core.MemoryManagement;
using AdvGenNoSqlServer.Core.Metrics;

namespace AdvGenNoSqlServer.Tests;

public class MetricsCollectorCacheStatsTests
{
    [Fact]
    public void RecordCacheStats_WritesExpectedGaugesAndCounters()
    {
        var collector = new MetricsCollector();
        var stats = new MemoryEngineStats
        {
            Plan = "Native",
            UsedBytes = 1024,
            LimitBytes = 65536,
            EntryCount = 5,
            HitCount = 10,
            MissCount = 2,
            EvictionCount = 1
        };

        collector.RecordCacheStats(stats);

        var planLabel = MetricLabel.Create("plan", "Native");
        Assert.Equal(1024, collector.GetValue("cache_used_bytes", planLabel));
        Assert.Equal(5, collector.GetValue("cache_entry_count", planLabel));
        Assert.Equal(10, collector.GetValue("cache_hit_total", planLabel));
        Assert.Equal(2, collector.GetValue("cache_miss_total", planLabel));
        Assert.Equal(1, collector.GetValue("cache_eviction_total", planLabel));
    }
}
