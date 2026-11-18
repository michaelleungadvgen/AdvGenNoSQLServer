// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
using System.Buffers;
using System.Collections.Concurrent;

namespace AdvGenNoSqlServer.Core.MemoryManagement;

/// <summary>
/// Stores all values in unmanaged heap memory — zero GC pressure.
/// Uses 16 key-hashed shards for concurrency. Each shard has its own ReaderWriterLockSlim.
/// Lock ordering: acquire shard lock FIRST, then call EvictionManager (which never locks shards).
/// </summary>
public sealed class NativeMemoryStorageEngine : IEvictingMemoryStorageEngine
{
    private const int ShardCount = 16;

    private readonly MemoryManagementConfiguration _config;
    private readonly long _limitBytes;
    private readonly EvictionPolicy _policy;
    private readonly EvictionManager _eviction;
    private readonly ConcurrentDictionary<string, NativeCacheEntryHolder>[] _shards;
    private readonly ReaderWriterLockSlim[] _locks;
    private readonly CancellationTokenSource _cts = new();
    private readonly Timer? _ttlTimer;
    private long _hitCount;
    private long _missCount;
    private int _disposed;

    /// <summary>
    /// Raised synchronously when an entry is evicted, inside the shard write lock;
    /// handlers receive (key, evictedBytes, remainingTtl) and must be fast
    /// </summary>
    public event Action<string, byte[], TimeSpan?>? EntryEvicted;

    /// <summary>
    /// Initializes a new instance of the <see cref="NativeMemoryStorageEngine"/> class, creating the shards and optionally a TTL cleanup timer
    /// </summary>
    /// <param name="config">Memory management settings supplying the eviction policy and default TTL</param>
    /// <param name="limitBytes">The maximum total entry size in bytes before eviction kicks in</param>
    public NativeMemoryStorageEngine(MemoryManagementConfiguration config, long limitBytes)
    {
        _config = config;
        _limitBytes = limitBytes;
        _policy = Enum.TryParse<EvictionPolicy>(config.EvictionPolicy, out var p) ? p : EvictionPolicy.LRU;
        _eviction = new EvictionManager(_policy, limitBytes);

        _shards = new ConcurrentDictionary<string, NativeCacheEntryHolder>[ShardCount];
        _locks = new ReaderWriterLockSlim[ShardCount];
        for (int i = 0; i < ShardCount; i++)
        {
            _shards[i] = new ConcurrentDictionary<string, NativeCacheEntryHolder>();
            _locks[i] = new ReaderWriterLockSlim();
        }

        if (config.DefaultTtlSeconds > 0)
        {
            long intervalMs = Math.Max(config.DefaultTtlSeconds / 4 * 1000L, 30_000L);
            _ttlTimer = new Timer(_ => ScanExpired(), null, intervalMs, intervalMs);
        }
    }

    private int ShardFor(string key) => Math.Abs(key.GetHashCode()) % ShardCount;

    /// <summary>
    /// Attempts to read an entry, copying the bytes to a managed array before releasing the shard read lock
    /// </summary>
    /// <param name="key">The entry key</param>
    /// <param name="value">Receives a copy of the entry bytes on success</param>
    /// <returns>True if the entry exists; otherwise false</returns>
    public bool TryGet(string key, out ReadOnlySpan<byte> value)
    {
        int shard = ShardFor(key);
        _locks[shard].EnterReadLock();
        try
        {
            if (!_shards[shard].TryGetValue(key, out var holder))
            {
                Interlocked.Increment(ref _missCount);
                value = default;
                return false;
            }
            _eviction.RecordAccess(key);
            Interlocked.Increment(ref _hitCount);
            // Copy bytes to managed array before releasing the read lock so the span is stable.
            var result = holder.AsSpan().ToArray();
            value = result;
            return true;
        }
        finally
        {
            _locks[shard].ExitReadLock();
        }
    }

    /// <summary>
    /// Inserts or replaces an entry in unmanaged memory, evicting victims when the memory limit would be exceeded
    /// </summary>
    /// <param name="key">The entry key</param>
    /// <param name="value">The bytes to store (copied to unmanaged memory)</param>
    /// <param name="ttl">Optional time-to-live; null means no expiration</param>
    public void Set(string key, ReadOnlySpan<byte> value, TimeSpan? ttl = null)
    {
        int shard = ShardFor(key);
        int newSize = value.Length;

        _locks[shard].EnterWriteLock();
        try
        {
            // Remove existing entry for this key if present
            if (_shards[shard].TryRemove(key, out var old))
            {
                old.Dispose();
                _eviction.Remove(key);
            }

            // Evict victims if over limit
            if (_eviction.UsedBytes + newSize > _limitBytes)
                EvictVictims(newSize, key, shard);

            var holder = new NativeCacheEntryHolder(value);
            _shards[shard][key] = holder;
            _eviction.RecordSet(key, newSize, ttl);
        }
        finally
        {
            _locks[shard].ExitWriteLock();
        }
    }

    /// <summary>
    /// Selects and removes victims to free bytesNeeded bytes.
    /// callerShard is already write-locked; victims on other shards acquire their own write lock.
    /// </summary>
    private void EvictVictims(long bytesNeeded, string excludeKey, int callerShard)
    {
        var victims = _eviction.SelectVictims(bytesNeeded);
        foreach (var victimKey in victims)
        {
            if (victimKey == excludeKey) continue;
            int victimShard = ShardFor(victimKey);
            if (victimShard == callerShard)
            {
                // Already holding write lock on this shard — evict directly.
                if (_shards[victimShard].TryRemove(victimKey, out var vh))
                {
                    byte[]? evictedBytes = EntryEvicted != null ? vh.AsSpan().ToArray() : null;
                    vh.Dispose();
                    _eviction.Remove(victimKey);
                    _eviction.RecordEviction();
                    if (evictedBytes != null)
                        EntryEvicted?.Invoke(victimKey, evictedBytes, null);
                }
            }
            else
            {
                _locks[victimShard].EnterWriteLock();
                try
                {
                    if (_shards[victimShard].TryRemove(victimKey, out var vh))
                    {
                        byte[]? evictedBytes = EntryEvicted != null ? vh.AsSpan().ToArray() : null;
                        vh.Dispose();
                        _eviction.Remove(victimKey);
                        _eviction.RecordEviction();
                        if (evictedBytes != null)
                            EntryEvicted?.Invoke(victimKey, evictedBytes, null);
                    }
                }
                finally
                {
                    _locks[victimShard].ExitWriteLock();
                }
            }
        }
    }

    /// <summary>
    /// Removes an entry and frees its unmanaged memory
    /// </summary>
    /// <param name="key">The entry key</param>
    /// <returns>True if the entry was removed; false if it did not exist</returns>
    public bool Remove(string key)
    {
        int shard = ShardFor(key);
        _locks[shard].EnterWriteLock();
        try
        {
            if (_shards[shard].TryRemove(key, out var holder))
            {
                holder.Dispose();
                _eviction.Remove(key);
                return true;
            }
            return false;
        }
        finally
        {
            _locks[shard].ExitWriteLock();
        }
    }

    /// <summary>
    /// Removes all entries from every shard, freeing their unmanaged memory
    /// </summary>
    public void Clear()
    {
        for (int i = 0; i < ShardCount; i++)
        {
            _locks[i].EnterWriteLock();
            try
            {
                foreach (var kv in _shards[i])
                {
                    kv.Value.Dispose();
                    _eviction.Remove(kv.Key);
                }
                _shards[i].Clear();
            }
            finally
            {
                _locks[i].ExitWriteLock();
            }
        }
    }

    /// <summary>
    /// Gets a snapshot of usage and hit/miss statistics for the native engine
    /// </summary>
    /// <returns>The current engine statistics with plan "Native"</returns>
    public MemoryEngineStats GetStats() => new()
    {
        Plan = "Native",
        UsedBytes = _eviction.UsedBytes,
        LimitBytes = _limitBytes,
        EntryCount = _eviction.EntryCount,
        HitCount = Volatile.Read(ref _hitCount),
        MissCount = Volatile.Read(ref _missCount),
        EvictionCount = _eviction.EvictionCount
    };

    private void ScanExpired()
    {
        if (_cts.IsCancellationRequested) return;
        var expired = _eviction.SelectVictims(0);
        foreach (var key in expired)
        {
            int shard = ShardFor(key);
            _locks[shard].EnterWriteLock();
            try
            {
                if (_shards[shard].TryRemove(key, out var holder))
                {
                    holder.Dispose();
                    _eviction.Remove(key);
                }
            }
            finally
            {
                _locks[shard].ExitWriteLock();
            }
        }
    }

    /// <summary>
    /// Stops the TTL timer, frees all entries and disposes the shard locks
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cts.Cancel();
        _ttlTimer?.Dispose();
        Clear();
        for (int i = 0; i < ShardCount; i++)
            _locks[i].Dispose();
        _cts.Dispose();
    }
}
