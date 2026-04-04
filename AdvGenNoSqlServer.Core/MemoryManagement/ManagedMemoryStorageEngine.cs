// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
using System.Buffers;
using System.Collections.Concurrent;

namespace AdvGenNoSqlServer.Core.MemoryManagement;

/// <summary>
/// Managed-heap engine using ArrayPool for reduced allocation pressure.
/// Same 16-shard structure as NativeMemoryStorageEngine.
/// TryGet always returns a copy — never exposes the pooled buffer.
/// Stores (Buffer, Size) tuples to track actual data size vs rented buffer size.
/// </summary>
public sealed class ManagedMemoryStorageEngine : IMemoryStorageEngine
{
    private const int ShardCount = 16;

    private readonly long _limitBytes;
    private readonly EvictionManager _eviction;
    private readonly ConcurrentDictionary<string, (byte[] Buffer, int Size)>[] _shards;
    private readonly ReaderWriterLockSlim[] _locks;
    private readonly CancellationTokenSource _cts = new();
    private readonly Timer? _ttlTimer;
    private long _hitCount;
    private long _missCount;
    private int _disposed;

    public ManagedMemoryStorageEngine(MemoryManagementConfiguration config, long limitBytes)
    {
        _limitBytes = limitBytes;
        var policy = Enum.TryParse<EvictionPolicy>(config.EvictionPolicy, out var p) ? p : EvictionPolicy.LRU;
        _eviction = new EvictionManager(policy, limitBytes);

        _shards = new ConcurrentDictionary<string, (byte[] Buffer, int Size)>[ShardCount];
        _locks = new ReaderWriterLockSlim[ShardCount];
        for (int i = 0; i < ShardCount; i++)
        {
            _shards[i] = new();
            _locks[i] = new ReaderWriterLockSlim();
        }

        if (config.DefaultTtlSeconds > 0)
        {
            long intervalMs = Math.Max(config.DefaultTtlSeconds / 4 * 1000L, 30_000L);
            _ttlTimer = new Timer(_ => ScanExpired(), null, intervalMs, intervalMs);
        }
    }

    private int ShardFor(string key) => Math.Abs(key.GetHashCode()) % ShardCount;

    public bool TryGet(string key, out ReadOnlySpan<byte> value)
    {
        int shard = ShardFor(key);
        _locks[shard].EnterReadLock();
        try
        {
            if (!_shards[shard].TryGetValue(key, out var entry))
            {
                Interlocked.Increment(ref _missCount);
                value = default;
                return false;
            }
            _eviction.RecordAccess(key);
            Interlocked.Increment(ref _hitCount);
            // Return a copy of exactly Size bytes — never expose the pooled buffer
            value = entry.Buffer.AsSpan(0, entry.Size).ToArray();
            return true;
        }
        finally
        {
            _locks[shard].ExitReadLock();
        }
    }

    public void Set(string key, ReadOnlySpan<byte> value, TimeSpan? ttl = null)
    {
        int shard = ShardFor(key);
        int size = value.Length;

        _locks[shard].EnterWriteLock();
        try
        {
            if (_shards[shard].TryRemove(key, out var old))
            {
                ArrayPool<byte>.Shared.Return(old.Buffer);
                _eviction.Remove(key);
            }

            if (_eviction.UsedBytes + size > _limitBytes)
                EvictVictims(size, key);

            var buf = ArrayPool<byte>.Shared.Rent(size);
            value.CopyTo(buf);
            _shards[shard][key] = (buf, size);
            _eviction.RecordSet(key, size, ttl);
        }
        finally
        {
            _locks[shard].ExitWriteLock();
        }
    }

    private void EvictVictims(long bytesNeeded, string excludeKey)
    {
        var victims = _eviction.SelectVictims(bytesNeeded);
        foreach (var victimKey in victims)
        {
            if (victimKey == excludeKey) continue;
            int victimShard = ShardFor(victimKey);

            void RemoveFromShard()
            {
                if (_shards[victimShard].TryRemove(victimKey, out var entry))
                {
                    _eviction.Remove(victimKey);
                    _eviction.RecordEviction();
                    ArrayPool<byte>.Shared.Return(entry.Buffer);
                }
            }

            if (victimShard == ShardFor(excludeKey))
            {
                RemoveFromShard();
            }
            else
            {
                _locks[victimShard].EnterWriteLock();
                try { RemoveFromShard(); }
                finally { _locks[victimShard].ExitWriteLock(); }
            }
        }
    }

    public bool Remove(string key)
    {
        int shard = ShardFor(key);
        _locks[shard].EnterWriteLock();
        (byte[] Buffer, int Size) entry = default;
        bool removed = false;
        try
        {
            if (_shards[shard].TryRemove(key, out entry))
            {
                _eviction.Remove(key);
                removed = true;
            }
            return removed;
        }
        finally
        {
            _locks[shard].ExitWriteLock();
            if (removed) ArrayPool<byte>.Shared.Return(entry.Buffer);
        }
    }

    public void Clear()
    {
        for (int i = 0; i < ShardCount; i++)
        {
            _locks[i].EnterWriteLock();
            try
            {
                foreach (var kv in _shards[i])
                {
                    _eviction.Remove(kv.Key);
                    ArrayPool<byte>.Shared.Return(kv.Value.Buffer);
                }
                _shards[i].Clear();
            }
            finally { _locks[i].ExitWriteLock(); }
        }
    }

    public MemoryEngineStats GetStats() => new()
    {
        Plan = "Managed",
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
        foreach (var key in _eviction.SelectVictims(0))
        {
            int shard = ShardFor(key);
            _locks[shard].EnterWriteLock();
            (byte[] Buffer, int Size) entry = default;
            bool removed = false;
            try
            {
                if (_shards[shard].TryRemove(key, out entry))
                {
                    _eviction.Remove(key);
                    removed = true;
                }
            }
            finally
            {
                _locks[shard].ExitWriteLock();
                if (removed) ArrayPool<byte>.Shared.Return(entry.Buffer);
            }
        }
    }

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
