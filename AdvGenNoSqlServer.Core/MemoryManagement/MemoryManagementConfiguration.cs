// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
namespace AdvGenNoSqlServer.Core.MemoryManagement;

public class MemoryManagementConfiguration
{
    /// <summary>Native | Managed | Mixed. Unknown values fall back to Managed.</summary>
    public string Plan { get; set; } = "Managed";

    /// <summary>Hard cap in MB. Must be > 0.</summary>
    public int MaxMemoryMB { get; set; } = 512;

    /// <summary>
    /// Soft cap as % of system RAM. 0 = use MaxMemoryMB only.
    /// Effective limit = Min(MaxMemoryMB times 1MB, RAM times MaxMemoryPercent/100).
    /// </summary>
    public int MaxMemoryPercent { get; set; } = 75;

    /// <summary>LRU | LFU | TTL (TTL evicts expired first then falls back to LRU).</summary>
    public string EvictionPolicy { get; set; } = "LRU";

    /// <summary>Default entry TTL in seconds. 0 = no expiry.</summary>
    public int DefaultTtlSeconds { get; set; } = 3600;

    public MixedTierConfiguration Mixed { get; set; } = new();
}

public class MixedTierConfiguration
{
    /// <summary>Max MB kept in native hot memory. Must be &gt; 0 and &lt; MaxMemoryMB.</summary>
    public int HotTierMaxMB { get; set; } = 256;

    /// <summary>IDocumentStore collection used as cold tier.</summary>
    public string SpillCollection { get; set; } = "_cache_cold";

    /// <summary>Warning threshold: cold collection size that triggers an operator warning.</summary>
    public int MaxColdEntries { get; set; } = 10_000;
}
