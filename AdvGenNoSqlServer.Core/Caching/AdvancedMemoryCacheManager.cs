// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Core.Caching;

/// <summary>
/// Advanced memory cache manager with LRU eviction and TTL support.
/// Provides thread-safe caching with configurable limits and statistics.
/// </summary>
public class AdvancedMemoryCacheManager : ICacheManager, IDisposable
{
    private readonly LruCache<Document> _cache;
    private bool _disposed;

    /// <summary>
    /// Gets the maximum number of items the cache can hold.
    /// </summary>
    public int MaxItemCount => _cache.MaxItemCount;

    /// <summary>
    /// Gets the maximum size in bytes the cache can use.
    /// </summary>
    public long MaxSizeInBytes => _cache.MaxSizeInBytes;

    /// <summary>
    /// Gets the default TTL for cache entries in milliseconds.
    /// </summary>
    public long DefaultTtlMilliseconds => _cache.DefaultTtlMilliseconds;

    /// <summary>
    /// Gets the current number of items in the cache.
    /// </summary>
    public int CurrentItemCount => _cache.Count;

    /// <summary>
    /// Gets the current size of the cache in bytes.
    /// </summary>
    public long CurrentSizeInBytes => _cache.CurrentSizeInBytes;

    /// <summary>
    /// Gets the total number of cache hits.
    /// </summary>
    public long TotalHits => _cache.TotalHits;

    /// <summary>
    /// Gets the total number of cache misses.
    /// </summary>
    public long TotalMisses => _cache.TotalMisses;

    /// <summary>
    /// Gets the total number of evictions.
    /// </summary>
    public long TotalEvictions => _cache.TotalEvictions;

    /// <summary>
    /// Gets the cache hit ratio (0.0 to 1.0).
    /// </summary>
    public double HitRatio => _cache.HitRatio;

    /// <summary>
    /// Event raised when an entry is evicted from the cache.
    /// </summary>
    public event EventHandler<CacheEvictedEventArgs>? ItemEvicted
    {
        add => _cache.ItemEvicted += value;
        remove => _cache.ItemEvicted -= value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AdvancedMemoryCacheManager"/> class.
    /// </summary>
    /// <param name="maxItemCount">Maximum number of items. Default is 10000.</param>
    /// <param name="maxSizeInBytes">Maximum size in bytes. Default is 100MB.</param>
    /// <param name="defaultTtlMilliseconds">Default TTL in milliseconds. Default is 30 minutes.</param>
    public AdvancedMemoryCacheManager(
        int maxItemCount = 10000,
        long maxSizeInBytes = 104857600, // 100MB
        long defaultTtlMilliseconds = 1800000) // 30 minutes
    {
        _cache = new LruCache<Document>(
            maxItemCount,
            maxSizeInBytes,
            defaultTtlMilliseconds,
            enableBackgroundCleanup: true);
    }

    /// <summary>
    /// Gets a document from the cache if it exists and is not expired.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <returns>The cached document or null if not found or expired.</returns>
    /// <exception cref="ArgumentException">Thrown when key is null or empty.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the cache has been disposed.</exception>
    public Document? Get(string key)
    {
        ThrowIfDisposed();
        return _cache.Get(key);
    }

    /// <summary>
    /// Adds or updates a document in the cache.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="document">The document to cache.</param>
    /// <param name="expirationMinutes">Expiration time in minutes. Default is 30.</param>
    /// <exception cref="ArgumentException">Thrown when key is null or empty.</exception>
    /// <exception cref="ArgumentNullException">Thrown when document is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the cache has been disposed.</exception>
    public void Set(string key, Document document, int expirationMinutes = 30)
    {
        ThrowIfDisposed();
        var ttlMilliseconds = expirationMinutes * 60L * 1000L;
        _cache.Set(key, document, ttlMilliseconds);
    }

    /// <summary>
    /// Adds or updates a document in the cache with specific TTL.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="document">The document to cache.</param>
    /// <param name="ttl">Time-to-live for the entry.</param>
    /// <exception cref="ArgumentException">Thrown when key is null or empty.</exception>
    /// <exception cref="ArgumentNullException">Thrown when document is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the cache has been disposed.</exception>
    public void Set(string key, Document document, TimeSpan ttl)
    {
        ThrowIfDisposed();
        _cache.Set(key, document, (long)ttl.TotalMilliseconds);
    }

    /// <summary>
    /// Removes a document from the cache.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <exception cref="ArgumentException">Thrown when key is null or empty.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the cache has been disposed.</exception>
    public void Remove(string key)
    {
        ThrowIfDisposed();
        _cache.Remove(key);
    }

    /// <summary>
    /// Clears all entries from the cache.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown when the cache has been disposed.</exception>
    public void Clear()
    {
        ThrowIfDisposed();
        _cache.Clear();
    }

    /// <summary>
    /// Checks if a key exists in the cache and is not expired.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <returns>True if the key exists and is not expired.</returns>
    /// <exception cref="ArgumentException">Thrown when key is null or empty.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the cache has been disposed.</exception>
    public bool ContainsKey(string key)
    {
        ThrowIfDisposed();
        return _cache.ContainsKey(key);
    }

    /// <summary>
    /// Tries to get a document from the cache.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="document">When this method returns, contains the document if found; otherwise, null.</param>
    /// <returns>True if the document was found and is not expired; otherwise, false.</returns>
    /// <exception cref="ArgumentException">Thrown when key is null or empty.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the cache has been disposed.</exception>
    public bool TryGet(string key, out Document? document)
    {
        ThrowIfDisposed();
        document = _cache.Get(key);
        return document != null;
    }

    /// <summary>
    /// Gets cache statistics.
    /// </summary>
    /// <returns>A snapshot of current cache statistics.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the cache has been disposed.</exception>
    public CacheStatistics GetStatistics()
    {
        ThrowIfDisposed();
        return _cache.GetStatistics();
    }

    /// <summary>
    /// Resets cache statistics.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown when the cache has been disposed.</exception>
    public void ResetStatistics()
    {
        ThrowIfDisposed();
        _cache.ResetStatistics();
    }

    /// <summary>
    /// Disposes the cache and releases all resources.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _cache.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AdvancedMemoryCacheManager));
        }
    }
}
