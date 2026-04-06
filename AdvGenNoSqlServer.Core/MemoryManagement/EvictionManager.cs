// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
using System.Collections.Concurrent;

namespace AdvGenNoSqlServer.Core.MemoryManagement;

/// <summary>
/// Shared victim-selection logic used by all three engine implementations.
/// Thread-safe via ConcurrentDictionary + Interlocked — no additional locks.
/// Never acquires engine shard locks (see spec lock ordering).
/// </summary>
public sealed class EvictionManager
{
    private readonly EvictionPolicy _policy;
    private readonly long _limitBytes;
    private readonly ConcurrentDictionary<string, CacheEntryMetadata> _metadata = new();
    private long _usedBytes;
    private long _evictionCount;

    public long UsedBytes => Volatile.Read(ref _usedBytes);
    public long EvictionCount => Volatile.Read(ref _evictionCount);
    public int EntryCount => _metadata.Count;

    public EvictionManager(EvictionPolicy policy, long limitBytes)
    {
        _policy = policy;
        _limitBytes = limitBytes;
    }

    public void RecordSet(string key, int sizeBytes, TimeSpan? ttl)
    {
        long expireAtMs = ttl.HasValue && ttl.Value > TimeSpan.Zero
            ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (long)ttl.Value.TotalMilliseconds
            : 0;

        _metadata.AddOrUpdate(key,
            _ =>
            {
                Interlocked.Add(ref _usedBytes, sizeBytes);
                return new CacheEntryMetadata
                {
                    LastAccessedTicks = Environment.TickCount64,
                    HitCount = 0,
                    ExpireAtMs = expireAtMs,
                    SizeBytes = sizeBytes
                };
            },
            (_, existing) =>
            {
                Interlocked.Add(ref _usedBytes, sizeBytes - existing.SizeBytes);
                existing.ExpireAtMs = expireAtMs;
                existing.SizeBytes = sizeBytes;
                existing.LastAccessedTicks = Environment.TickCount64;
                return existing;
            });
    }

    public void RecordAccess(string key)
    {
        if (_metadata.TryGetValue(key, out var meta))
        {
            meta.LastAccessedTicks = Environment.TickCount64;
            // HitCount is a property; increment is best-effort (slight races acceptable
            // for victim selection — the alternative would require a separate long field).
            meta.HitCount++;
        }
    }

    public void Remove(string key)
    {
        if (_metadata.TryRemove(key, out var meta))
            Interlocked.Add(ref _usedBytes, -meta.SizeBytes);
    }

    /// <summary>
    /// Called by the engine after an entry is physically evicted (not just selected).
    /// Increments _evictionCount at actual removal time, avoiding phantom counts.
    /// </summary>
    public void RecordEviction() => Interlocked.Increment(ref _evictionCount);

    /// <summary>
    /// Returns keys to evict. bytesNeeded=0 returns only expired entries.
    /// For bytesNeeded&gt;0: returns expired entries plus non-expired victims sorted
    /// by policy until cumulative SizeBytes &gt;= bytesNeeded.
    /// </summary>
    public IReadOnlyList<string> SelectVictims(long bytesNeeded)
    {
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var snapshot = _metadata.ToArray();

        var expired = snapshot
            .Where(kv => kv.Value.ExpireAtMs > 0 && kv.Value.ExpireAtMs <= nowMs)
            .Select(kv => kv.Key)
            .ToList();

        if (bytesNeeded <= 0)
            return expired;

        long expiredBytes = expired
            .Select(k => _metadata.TryGetValue(k, out var m) ? m.SizeBytes : 0)
            .Sum();

        long remaining = bytesNeeded - expiredBytes;
        if (remaining <= 0)
            return expired;

        var nonExpired = snapshot
            .Where(kv => !(kv.Value.ExpireAtMs > 0 && kv.Value.ExpireAtMs <= nowMs));

        IOrderedEnumerable<KeyValuePair<string, CacheEntryMetadata>> sorted = _policy switch
        {
            EvictionPolicy.LFU => nonExpired.OrderBy(kv => kv.Value.HitCount),
            _ => nonExpired.OrderBy(kv => kv.Value.LastAccessedTicks) // LRU and TTL fallback
        };

        var result = new List<string>(expired);
        long accumulated = 0;
        foreach (var kv in sorted)
        {
            if (accumulated >= remaining) break;
            result.Add(kv.Key);
            accumulated += kv.Value.SizeBytes;
            // Note: _evictionCount is NOT incremented here. It is incremented via
            // RecordEviction() called by the engine after the entry is physically removed,
            // preventing phantom counts if a victim was already removed by another thread.
        }

        return result;
    }
}
