// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
namespace AdvGenNoSqlServer.Core.MemoryManagement;

/// <summary>Point-in-time snapshot of engine health, fed into MetricsCollector.</summary>
public sealed class MemoryEngineStats
{
    /// <summary>Gets the engine plan name (Native, Managed or Mixed).</summary>
    public string Plan { get; init; } = "";

    /// <summary>Gets the total size in bytes of stored entries.</summary>
    public long UsedBytes { get; init; }

    /// <summary>Gets the configured memory limit in bytes.</summary>
    public long LimitBytes { get; init; }

    /// <summary>Gets the number of entries currently stored.</summary>
    public long EntryCount { get; init; }

    /// <summary>Gets the cumulative number of cache hits.</summary>
    public long HitCount { get; init; }

    /// <summary>Gets the cumulative number of cache misses.</summary>
    public long MissCount { get; init; }

    /// <summary>Gets the cumulative number of evicted entries.</summary>
    public long EvictionCount { get; init; }

    /// <summary>Gets the hit ratio, or zero when no lookups have occurred.</summary>
    public double HitRatio => (HitCount + MissCount) == 0 ? 0
        : (double)HitCount / (HitCount + MissCount);
}
