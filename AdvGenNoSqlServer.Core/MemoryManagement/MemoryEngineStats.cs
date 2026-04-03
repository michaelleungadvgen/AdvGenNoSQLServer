// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
namespace AdvGenNoSqlServer.Core.MemoryManagement;

/// <summary>Point-in-time snapshot of engine health, fed into MetricsCollector.</summary>
public sealed class MemoryEngineStats
{
    public string Plan { get; init; } = "";
    public long UsedBytes { get; init; }
    public long LimitBytes { get; init; }
    public long EntryCount { get; init; }
    public long HitCount { get; init; }
    public long MissCount { get; init; }
    public long EvictionCount { get; init; }
    public double HitRatio => (HitCount + MissCount) == 0 ? 0
        : (double)HitCount / (HitCount + MissCount);
}
