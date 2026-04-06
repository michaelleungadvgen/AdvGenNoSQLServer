// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
namespace AdvGenNoSqlServer.Core.MemoryManagement;

/// <summary>Per-key metadata used by EvictionManager to select victims.</summary>
public sealed class CacheEntryMetadata
{
    /// <summary>Environment.TickCount64 at the time of the last TryGet hit.</summary>
    public long LastAccessedTicks { get; set; }

    /// <summary>Number of TryGet hits for this key.</summary>
    public long HitCount { get; set; }

    /// <summary>
    /// Absolute expiry as Unix milliseconds (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + ttlMs).
    /// 0 means no expiry.
    /// </summary>
    public long ExpireAtMs { get; set; }

    /// <summary>Size of the stored value in bytes.</summary>
    public int SizeBytes { get; set; }
}
