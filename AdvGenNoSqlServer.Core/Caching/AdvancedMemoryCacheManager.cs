// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
using System.Text.Json;
using AdvGenNoSqlServer.Core.MemoryManagement;
using AdvGenNoSqlServer.Core.Metrics;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Core.Caching;

/// <summary>
/// Cache manager backed by a pluggable IMemoryStorageEngine.
/// Serialises Document objects to byte[] for storage. Deserialisation failures
/// are treated as cache misses (key removed).
/// </summary>
public class AdvancedMemoryCacheManager : ICacheManager, IDisposable
{
    private readonly IMemoryStorageEngine _engine;
    private readonly IMetricsCollector _metrics;
    private readonly Timer _statsTimer;
    private int _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="AdvancedMemoryCacheManager"/> class and starts periodic statistics forwarding
    /// </summary>
    /// <param name="engine">The memory storage engine backing the cache</param>
    /// <param name="metrics">The metrics collector that receives cache statistics</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="engine"/> or <paramref name="metrics"/> is null</exception>
    public AdvancedMemoryCacheManager(IMemoryStorageEngine engine, IMetricsCollector metrics)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _statsTimer = new Timer(_ => ForwardStats(), null,
                                TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// Gets a cached document, treating deserialization failures as a miss and removing the corrupt entry
    /// </summary>
    /// <param name="key">The cache key</param>
    /// <returns>The cached document, or null if not found or corrupt</returns>
    public Document? Get(string key)
    {
        ThrowIfDisposed();
        if (!_engine.TryGet(key, out var span)) return null;

        try
        {
            // Use source-gen context for AOT-safe deserialization
            return JsonSerializer.Deserialize(span, CacheJsonContext.Default.Document);
        }
        catch (JsonException)
        {
            _engine.Remove(key);
            return null;
        }
    }

    /// <summary>
    /// Serializes and stores a document in the cache with an optional time-to-live
    /// </summary>
    /// <param name="key">The cache key</param>
    /// <param name="document">The document to cache</param>
    /// <param name="expirationMinutes">Expiration in minutes; values of zero or less mean no expiration</param>
    public void Set(string key, Document document, int expirationMinutes = 30)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(document);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, CacheJsonContext.Default.Document);
        TimeSpan? ttl = expirationMinutes > 0 ? TimeSpan.FromMinutes(expirationMinutes) : null;
        _engine.Set(key, bytes, ttl);
    }

    /// <summary>
    /// Removes an entry from the cache
    /// </summary>
    /// <param name="key">The cache key</param>
    public void Remove(string key)
    {
        ThrowIfDisposed();
        _engine.Remove(key);
    }

    /// <summary>
    /// Clears all entries from the cache
    /// </summary>
    public void Clear()
    {
        ThrowIfDisposed();
        _engine.Clear();
    }

    /// <summary>
    /// Attempts to get a cached document
    /// </summary>
    /// <param name="key">The cache key</param>
    /// <param name="document">Receives the cached document, or null on a miss</param>
    /// <returns>True if the document was found; otherwise false</returns>
    public bool TryGet(string key, out Document? document)
    {
        document = Get(key);
        return document != null;
    }

    /// <summary>
    /// Checks whether an entry exists in the cache without deserializing it
    /// </summary>
    /// <param name="key">The cache key</param>
    /// <returns>True if the key is present; otherwise false</returns>
    public bool ContainsKey(string key)
    {
        ThrowIfDisposed();
        return _engine.TryGet(key, out _);
    }

    /// <summary>
    /// Gets a snapshot of cache statistics from the underlying storage engine
    /// </summary>
    /// <returns>The current cache statistics</returns>
    public CacheStatistics GetStatistics()
    {
        ThrowIfDisposed();
        var s = _engine.GetStats();
        return new CacheStatistics
        {
            TotalHits = s.HitCount,
            TotalMisses = s.MissCount,
            TotalEvictions = s.EvictionCount,
            HitRatio = s.HitRatio,
            ItemCount = (int)s.EntryCount,
            CurrentSizeInBytes = s.UsedBytes
        };
    }

    /// <summary>
    /// Resets cache statistics; a no-op because engine statistics are cumulative
    /// </summary>
    public void ResetStatistics() { /* Stats are cumulative in the engine */ }

    private void ForwardStats()
    {
        try { _metrics.RecordCacheStats(_engine.GetStats()); }
        catch { /* never crash the timer */ }
    }

    /// <summary>
    /// Stops the statistics timer and disposes the underlying storage engine
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _statsTimer.Dispose();
        _engine.Dispose();
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed != 0)
            throw new ObjectDisposedException(nameof(AdvancedMemoryCacheManager));
    }
}
