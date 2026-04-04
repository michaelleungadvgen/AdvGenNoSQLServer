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

    public AdvancedMemoryCacheManager(IMemoryStorageEngine engine, IMetricsCollector metrics)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _statsTimer = new Timer(_ => ForwardStats(), null,
                                TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public Document? Get(string key)
    {
        ThrowIfDisposed();
        if (!_engine.TryGet(key, out var span)) return null;

        try
        {
            // Use standard deserialization for correctness with Dictionary<string, object?>
            return JsonSerializer.Deserialize<Document>(span);
        }
        catch (JsonException)
        {
            _engine.Remove(key);
            return null;
        }
    }

    public void Set(string key, Document document, int expirationMinutes = 30)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(document);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document);
        TimeSpan? ttl = expirationMinutes > 0 ? TimeSpan.FromMinutes(expirationMinutes) : null;
        _engine.Set(key, bytes, ttl);
    }

    public void Remove(string key)
    {
        ThrowIfDisposed();
        _engine.Remove(key);
    }

    public void Clear()
    {
        ThrowIfDisposed();
        _engine.Clear();
    }

    public bool TryGet(string key, out Document? document)
    {
        document = Get(key);
        return document != null;
    }

    public bool ContainsKey(string key)
    {
        ThrowIfDisposed();
        return _engine.TryGet(key, out _);
    }

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

    public void ResetStatistics() { /* Stats are cumulative in the engine */ }

    private void ForwardStats()
    {
        try { _metrics.RecordCacheStats(_engine.GetStats()); }
        catch { /* never crash the timer */ }
    }

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
