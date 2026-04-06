// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
using AdvGenNoSqlServer.Core.Abstractions;
using AdvGenNoSqlServer.Core.Models;
using Microsoft.Extensions.Logging;

namespace AdvGenNoSqlServer.Core.MemoryManagement;

/// <summary>
/// Hot tier = NativeMemoryStorageEngine (HotTierMaxMB).
/// Cold tier = IDocumentStore (SpillCollection).
///
/// Write: always goes to hot. On hot eviction (EntryEvicted event), entry is spilled to cold.
/// Read: hot first, then cold. Cold hit triggers async fire-and-forget promotion to hot.
/// Cold docs store: _value (base64 bytes), _expiry (unix ms, 0=none).
/// </summary>
public sealed class MixedMemoryStorageEngine : IMemoryStorageEngine
{
    private readonly MemoryManagementConfiguration _config;
    private readonly IEvictingMemoryStorageEngine _hot;
    private readonly IDocumentStore _store;
    private readonly ILogger? _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly Timer? _coldCleanupTimer;
    private long _coldHitCount;
    private long _coldMissCount;
    private long _evictionCount;
    private int _disposed;

    public MixedMemoryStorageEngine(
        MemoryManagementConfiguration config,
        long totalLimitBytes,
        IDocumentStore store,
        ILogger? logger = null)
    {
        _config = config;
        _store = store;
        _logger = logger;

        long hotLimit = (long)config.Mixed.HotTierMaxMB * 1_048_576;
        var hotConfig = new MemoryManagementConfiguration
        {
            Plan = "Native",
            MaxMemoryMB = config.Mixed.HotTierMaxMB,
            MaxMemoryPercent = 0,
            EvictionPolicy = config.EvictionPolicy,
            DefaultTtlSeconds = config.DefaultTtlSeconds
        };
        _hot = new NativeMemoryStorageEngine(hotConfig, hotLimit);
        _hot.EntryEvicted += OnHotEviction;

        if (config.DefaultTtlSeconds > 0)
        {
            long intervalMs = Math.Max(config.DefaultTtlSeconds / 4 * 1000L, 30_000L);
            _coldCleanupTimer = new Timer(_ => CleanColdTier(), null, intervalMs, intervalMs);
        }
    }

    public bool TryGet(string key, out ReadOnlySpan<byte> value)
    {
        if (_hot.TryGet(key, out value)) return true;

        var coldDoc = _store.GetAsync(_config.Mixed.SpillCollection, key, _cts.Token)
            .GetAwaiter().GetResult();

        if (coldDoc == null)
        {
            Interlocked.Increment(ref _coldMissCount);
            value = default;
            return false;
        }

        static long ExtractLong(object? v) =>
            v is System.Text.Json.JsonElement je ? je.GetInt64() : v != null ? Convert.ToInt64(v) : 0L;

        long expireAtMs = coldDoc.Data.TryGetValue("_expiry", out var expObj) ? ExtractLong(expObj) : 0;
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (expireAtMs > 0 && expireAtMs <= nowMs)
        {
            _store.DeleteAsync(_config.Mixed.SpillCollection, key, _cts.Token).GetAwaiter().GetResult();
            Interlocked.Increment(ref _coldMissCount);
            value = default;
            return false;
        }

        byte[] bytes = coldDoc.Data.TryGetValue("_value", out var valObj) && valObj is string b64
            ? Convert.FromBase64String(b64)
            : [];

        TimeSpan? remainingTtl = expireAtMs > 0
            ? TimeSpan.FromMilliseconds(expireAtMs - nowMs)
            : null;

        if (remainingTtl.HasValue && remainingTtl.Value <= TimeSpan.Zero)
        {
            _store.DeleteAsync(_config.Mixed.SpillCollection, key, _cts.Token).GetAwaiter().GetResult();
            Interlocked.Increment(ref _coldMissCount);
            value = default;
            return false;
        }

        // Fire-and-forget promotion to hot tier
        var capturedKey = key;
        var capturedBytes = bytes;
        var capturedTtl = remainingTtl;
        var capturedExpireAtMs = expireAtMs;
        _ = Task.Run(() =>
        {
            if (_cts.IsCancellationRequested) return;
            if (_hot.TryGet(capturedKey, out _)) return;
            long now2 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (capturedExpireAtMs > 0 && capturedExpireAtMs <= now2) return;
            _hot.Set(capturedKey, capturedBytes, capturedTtl);
        }, _cts.Token).ContinueWith(t =>
        {
            if (t.IsFaulted)
                _logger?.LogWarning(t.Exception, "Cold→hot promotion failed for key {Key}", capturedKey);
        }, TaskContinuationOptions.OnlyOnFaulted);

        Interlocked.Increment(ref _coldHitCount);
        value = bytes;
        return true;
    }

    public void Set(string key, ReadOnlySpan<byte> value, TimeSpan? ttl = null)
        => _hot.Set(key, value, ttl);

    public bool Remove(string key)
    {
        bool hotRemoved = _hot.Remove(key);
        bool coldRemoved = _store.DeleteAsync(_config.Mixed.SpillCollection, key, _cts.Token)
            .GetAwaiter().GetResult();
        return hotRemoved || coldRemoved;
    }

    public void Clear()
    {
        _hot.Clear();
        _store.ClearCollectionAsync(_config.Mixed.SpillCollection, _cts.Token).GetAwaiter().GetResult();
    }

    public MemoryEngineStats GetStats()
    {
        var hotStats = _hot.GetStats();
        long coldCount = _store.CountAsync(_config.Mixed.SpillCollection, _cts.Token)
            .GetAwaiter().GetResult();
        return new MemoryEngineStats
        {
            Plan = "Mixed",
            UsedBytes = hotStats.UsedBytes,
            LimitBytes = hotStats.LimitBytes,
            EntryCount = hotStats.EntryCount + coldCount,
            HitCount = hotStats.HitCount + Volatile.Read(ref _coldHitCount),
            MissCount = hotStats.MissCount + Volatile.Read(ref _coldMissCount),
            EvictionCount = hotStats.EvictionCount + Volatile.Read(ref _evictionCount)
        };
    }

    private void OnHotEviction(string key, byte[] bytes, TimeSpan? remainingTtl)
    {
        Interlocked.Increment(ref _evictionCount);
        try
        {
            long expireAtMs = remainingTtl.HasValue
                ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (long)remainingTtl.Value.TotalMilliseconds
                : 0;

            var doc = new Document
            {
                Id = key,
                Data = new Dictionary<string, object?>
                {
                    ["_value"] = Convert.ToBase64String(bytes),
                    ["_expiry"] = expireAtMs
                }
            };
            _store.InsertAsync(_config.Mixed.SpillCollection, doc, _cts.Token).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to spill evicted entry {Key} to cold tier; entry dropped.", key);
        }
    }

    private void CleanColdTier()
    {
        if (_cts.IsCancellationRequested) return;
        try
        {
            var allDocs = _store.GetAllAsync(_config.Mixed.SpillCollection, _cts.Token)
                .GetAwaiter().GetResult();
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long count = 0;
            foreach (var doc in allDocs)
            {
                count++;
                if (doc.Data.TryGetValue("_expiry", out var expObj) && expObj != null)
                {
                    long exp = expObj is System.Text.Json.JsonElement je ? je.GetInt64() : Convert.ToInt64(expObj);
                    if (exp > 0 && exp <= nowMs)
                        _store.DeleteAsync(_config.Mixed.SpillCollection, doc.Id, _cts.Token).GetAwaiter().GetResult();
                }
            }
            if (count > _config.Mixed.MaxColdEntries)
                _logger?.LogWarning("Cold tier collection '{Col}' has {Count} entries, exceeding MaxColdEntries={Max}.",
                    _config.Mixed.SpillCollection, count, _config.Mixed.MaxColdEntries);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Cold tier cleanup failed.");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cts.Cancel();
        _coldCleanupTimer?.Dispose();
        _hot.EntryEvicted -= OnHotEviction;
        _hot.Dispose();
        _cts.Dispose();
    }
}
