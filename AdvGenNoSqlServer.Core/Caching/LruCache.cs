// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;
using System.Diagnostics;

namespace AdvGenNoSqlServer.Core.Caching;

/// <summary>
/// Represents an entry in the LRU cache with TTL support.
/// </summary>
/// <typeparam name="TValue">The type of the cached value.</typeparam>
internal class LruCacheEntry<TValue>
{
    public string Key { get; set; } = string.Empty;
    public TValue Value { get; set; } = default!;
    public long ExpirationTicks { get; set; }
    public long SizeInBytes { get; set; }
    public LinkedListNode<LruCacheEntry<TValue>>? Node { get; set; }
}

/// <summary>
/// A thread-safe LRU (Least Recently Used) cache implementation with TTL support.
/// Provides O(1) insertion, deletion, and access operations.
/// </summary>
/// <typeparam name="TValue">The type of values to store in the cache.</typeparam>
public class LruCache<TValue> : IDisposable
{
    private readonly LinkedList<LruCacheEntry<TValue>> _lruList;
    private readonly ConcurrentDictionary<string, LruCacheEntry<TValue>> _cache;
    private readonly ReaderWriterLockSlim _lock;
    private readonly Timer? _cleanupTimer;
    
    private long _currentSizeInBytes;
    private long _totalHits;
    private long _totalMisses;
    private long _totalEvictions;

    /// <summary>
    /// Gets the maximum number of items the cache can hold.
    /// </summary>
    public int MaxItemCount { get; }

    /// <summary>
    /// Gets the maximum size in bytes the cache can use.
    /// </summary>
    public long MaxSizeInBytes { get; }

    /// <summary>
    /// Gets the default TTL for cache entries in milliseconds.
    /// </summary>
    public long DefaultTtlMilliseconds { get; }

    /// <summary>
    /// Gets the current number of items in the cache.
    /// </summary>
    public int Count => _cache.Count;

    /// <summary>
    /// Gets the current size of the cache in bytes.
    /// </summary>
    public long CurrentSizeInBytes => Interlocked.Read(ref _currentSizeInBytes);

    /// <summary>
    /// Gets the total number of cache hits.
    /// </summary>
    public long TotalHits => Interlocked.Read(ref _totalHits);

    /// <summary>
    /// Gets the total number of cache misses.
    /// </summary>
    public long TotalMisses => Interlocked.Read(ref _totalMisses);

    /// <summary>
    /// Gets the total number of evictions.
    /// </summary>
    public long TotalEvictions => Interlocked.Read(ref _totalEvictions);

    /// <summary>
    /// Gets the cache hit ratio (0.0 to 1.0).
    /// </summary>
    public double HitRatio
    {
        get
        {
            var hits = Interlocked.Read(ref _totalHits);
            var misses = Interlocked.Read(ref _totalMisses);
            var total = hits + misses;
            return total == 0 ? 0.0 : (double)hits / total;
        }
    }

    /// <summary>
    /// Event raised when an entry is evicted from the cache.
    /// </summary>
    public event EventHandler<CacheEvictedEventArgs>? ItemEvicted;

    /// <summary>
    /// Initializes a new instance of the <see cref="LruCache{TValue}"/> class.
    /// </summary>
    /// <param name="maxItemCount">Maximum number of items. Default is 10000.</param>
    /// <param name="maxSizeInBytes">Maximum size in bytes. Default is 100MB.</param>
    /// <param name="defaultTtlMilliseconds">Default TTL in milliseconds. Default is 1 hour.</param>
    /// <param name="enableBackgroundCleanup">Whether to enable background cleanup of expired items. Default is true.</param>
    public LruCache(
        int maxItemCount = 10000,
        long maxSizeInBytes = 104857600, // 100MB
        long defaultTtlMilliseconds = 3600000, // 1 hour
        bool enableBackgroundCleanup = true)
    {
        MaxItemCount = maxItemCount > 0 ? maxItemCount : throw new ArgumentException("Max item count must be greater than 0", nameof(maxItemCount));
        MaxSizeInBytes = maxSizeInBytes > 0 ? maxSizeInBytes : throw new ArgumentException("Max size must be greater than 0", nameof(maxSizeInBytes));
        DefaultTtlMilliseconds = defaultTtlMilliseconds > 0 ? defaultTtlMilliseconds : throw new ArgumentException("Default TTL must be greater than 0", nameof(defaultTtlMilliseconds));

        _lruList = new LinkedList<LruCacheEntry<TValue>>();
        _cache = new ConcurrentDictionary<string, LruCacheEntry<TValue>>();
        _lock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
        _currentSizeInBytes = 0;

        if (enableBackgroundCleanup)
        {
            // Run cleanup every 60 seconds
            _cleanupTimer = new Timer(CleanupExpiredItems, null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
        }
    }

    /// <summary>
    /// Gets a value from the cache.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <returns>The cached value, or default if not found or expired.</returns>
    public TValue? Get(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        _lock.EnterUpgradeableReadLock();
        try
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                // Check if expired using high-precision timer
                if (Stopwatch.GetTimestamp() > entry.ExpirationTicks)
                {
                    _lock.EnterWriteLock();
                    try
                    {
                        RemoveEntry(entry);
                    }
                    finally
                    {
                        _lock.ExitWriteLock();
                    }
                    Interlocked.Increment(ref _totalMisses);
                    return default;
                }

                // Move to front (most recently used)
                _lock.EnterWriteLock();
                try
                {
                    _lruList.Remove(entry.Node!);
                    entry.Node = _lruList.AddFirst(entry);
                }
                finally
                {
                    _lock.ExitWriteLock();
                }

                Interlocked.Increment(ref _totalHits);
                return entry.Value;
            }

            Interlocked.Increment(ref _totalMisses);
            return default;
        }
        finally
        {
            _lock.ExitUpgradeableReadLock();
        }
    }

    /// <summary>
    /// Adds or updates a value in the cache.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="value">The value to cache.</param>
    /// <param name="ttlMilliseconds">TTL in milliseconds. If null, uses default.</param>
    /// <param name="sizeInBytes">Size of the entry in bytes for memory tracking.</param>
    public void Set(string key, TValue value, long? ttlMilliseconds = null, long sizeInBytes = 0)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));
        if (value == null)
            throw new ArgumentNullException(nameof(value));

        var ttlMs = ttlMilliseconds ?? DefaultTtlMilliseconds;
        // Convert milliseconds to ticks using Stopwatch.Frequency for high precision
        var expirationTicks = Stopwatch.GetTimestamp() + (ttlMs * Stopwatch.Frequency / 1000);

        _lock.EnterWriteLock();
        try
        {
            // Remove existing entry if present
            if (_cache.TryGetValue(key, out var existingEntry))
            {
                RemoveEntry(existingEntry);
            }

            // Evict entries if necessary
            EvictIfNecessary(sizeInBytes);

            // Create new entry
            var entry = new LruCacheEntry<TValue>
            {
                Key = key,
                Value = value,
                ExpirationTicks = expirationTicks,
                SizeInBytes = sizeInBytes
            };

            // Add to front of LRU list
            entry.Node = _lruList.AddFirst(entry);
            _cache[key] = entry;

            Interlocked.Add(ref _currentSizeInBytes, sizeInBytes);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Removes a value from the cache.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <returns>True if the key was found and removed, false otherwise.</returns>
    public bool Remove(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        _lock.EnterWriteLock();
        try
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                RemoveEntry(entry);
                return true;
            }
            return false;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Clears all entries from the cache.
    /// </summary>
    public void Clear()
    {
        _lock.EnterWriteLock();
        try
        {
            foreach (var entry in _cache.Values)
            {
                OnItemEvicted(entry.Key, entry.Value, EvictionReason.Cleared);
            }

            _lruList.Clear();
            _cache.Clear();
            Interlocked.Exchange(ref _currentSizeInBytes, 0);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Checks if a key exists in the cache and is not expired.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <returns>True if the key exists and is not expired.</returns>
    public bool ContainsKey(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        _lock.EnterUpgradeableReadLock();
        try
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                if (Stopwatch.GetTimestamp() > entry.ExpirationTicks)
                {
                    _lock.EnterWriteLock();
                    try
                    {
                        RemoveEntry(entry);
                    }
                    finally
                    {
                        _lock.ExitWriteLock();
                    }
                    return false;
                }
                return true;
            }
            return false;
        }
        finally
        {
            _lock.ExitUpgradeableReadLock();
        }
    }

    /// <summary>
    /// Gets cache statistics.
    /// </summary>
    /// <returns>A snapshot of current cache statistics.</returns>
    public CacheStatistics GetStatistics()
    {
        return new CacheStatistics
        {
            ItemCount = Count,
            CurrentSizeInBytes = CurrentSizeInBytes,
            MaxItemCount = MaxItemCount,
            MaxSizeInBytes = MaxSizeInBytes,
            TotalHits = TotalHits,
            TotalMisses = TotalMisses,
            TotalEvictions = TotalEvictions,
            HitRatio = HitRatio
        };
    }

    /// <summary>
    /// Resets cache statistics.
    /// </summary>
    public void ResetStatistics()
    {
        Interlocked.Exchange(ref _totalHits, 0);
        Interlocked.Exchange(ref _totalMisses, 0);
        Interlocked.Exchange(ref _totalEvictions, 0);
    }

    private void EvictIfNecessary(long newEntrySize)
    {
        // Check if we need to evict based on count
        while (_cache.Count >= MaxItemCount || (_currentSizeInBytes + newEntrySize) > MaxSizeInBytes)
        {
            if (_lruList.Count == 0)
                break;

            // Remove least recently used (last in list)
            var lruEntry = _lruList.Last;
            if (lruEntry != null)
            {
                RemoveEntry(lruEntry.Value);
                Interlocked.Increment(ref _totalEvictions);
            }
        }
    }

    private void RemoveEntry(LruCacheEntry<TValue> entry)
    {
        if (entry.Node != null)
        {
            _lruList.Remove(entry.Node);
        }
        _cache.TryRemove(entry.Key, out _);
        Interlocked.Add(ref _currentSizeInBytes, -entry.SizeInBytes);
        OnItemEvicted(entry.Key, entry.Value, EvictionReason.Removed);
    }

    private void OnItemEvicted(string key, TValue value, EvictionReason reason)
    {
        ItemEvicted?.Invoke(this, new CacheEvictedEventArgs(key, value, reason));
    }

    private void CleanupExpiredItems(object? state)
    {
        _lock.EnterWriteLock();
        try
        {
            var nowTicks = Stopwatch.GetTimestamp();
            var expiredEntries = _cache.Values.Where(e => nowTicks > e.ExpirationTicks).ToList();

            foreach (var entry in expiredEntries)
            {
                RemoveEntry(entry);
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Disposes the cache and releases all resources.
    /// </summary>
    public void Dispose()
    {
        _cleanupTimer?.Dispose();
        Clear();
        _lock.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Cache statistics snapshot.
/// </summary>
public class CacheStatistics
{
    public int ItemCount { get; set; }
    public long CurrentSizeInBytes { get; set; }
    public int MaxItemCount { get; set; }
    public long MaxSizeInBytes { get; set; }
    public long TotalHits { get; set; }
    public long TotalMisses { get; set; }
    public long TotalEvictions { get; set; }
    public double HitRatio { get; set; }
}

/// <summary>
/// Reasons for cache eviction.
/// </summary>
public enum EvictionReason
{
    /// <summary>
    /// Item was removed due to expiration.
    /// </summary>
    Expired,

    /// <summary>
    /// Item was removed to make room for new entries.
    /// </summary>
    Capacity,

    /// <summary>
    /// Item was explicitly removed.
    /// </summary>
    Removed,

    /// <summary>
    /// Cache was cleared.
    /// </summary>
    Cleared
}

/// <summary>
/// Event arguments for cache eviction events.
/// </summary>
public class CacheEvictedEventArgs : EventArgs
{
    /// <summary>
    /// The key of the evicted item.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// The evicted value.
    /// </summary>
    public object? Value { get; }

    /// <summary>
    /// The reason for eviction.
    /// </summary>
    public EvictionReason Reason { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CacheEvictedEventArgs"/> class.
    /// </summary>
    public CacheEvictedEventArgs(string key, object? value, EvictionReason reason)
    {
        Key = key;
        Value = value;
        Reason = reason;
    }
}
