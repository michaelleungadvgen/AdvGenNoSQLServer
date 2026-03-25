// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;
using AdvGenNoSqlServer.Core.Models;
using Microsoft.Extensions.Caching.Memory;

namespace AdvGenNoSqlServer.Core.Caching;

/// <summary>
/// Implementation of ICacheManager using IMemoryCache with key tracking for Clear() support.
/// </summary>
public class MemoryCacheManager : ICacheManager
{
    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<string, byte> _keys;

    /// <summary>
    /// Initializes a new instance of the MemoryCacheManager class.
    /// </summary>
    /// <param name="cache">The memory cache instance</param>
    public MemoryCacheManager(IMemoryCache cache)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _keys = new ConcurrentDictionary<string, byte>();
    }

    /// <inheritdoc />
    public Document? Get(string key)
    {
        if (string.IsNullOrEmpty(key))
            return null;

        if (_cache.TryGetValue(key, out Document? document))
        {
            return document;
        }

        // Key no longer in cache (expired or removed), remove from tracking
        _keys.TryRemove(key, out _);
        return null;
    }

    /// <inheritdoc />
    public void Set(string key, Document document, int expirationMinutes = 30)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Cache key cannot be null or empty", nameof(key));
        
        if (document == null)
            throw new ArgumentNullException(nameof(document));

        // Track the key first
        _keys.TryAdd(key, 1);

        // Create entry with eviction callback
        using var entry = _cache.CreateEntry(key);
        entry.Value = document;
        entry.SetSlidingExpiration(TimeSpan.FromMinutes(expirationMinutes));
        entry.RegisterPostEvictionCallback((key, value, reason, state) =>
        {
            if (key is string keyString)
            {
                _keys.TryRemove(keyString, out _);
            }
        });
    }

    /// <inheritdoc />
    public void Remove(string key)
    {
        if (string.IsNullOrEmpty(key))
            return;

        _cache.Remove(key);
        _keys.TryRemove(key, out _);
    }

    /// <inheritdoc />
    public void Clear()
    {
        // Remove all tracked keys from the cache
        foreach (var key in _keys.Keys)
        {
            _cache.Remove(key);
        }
        _keys.Clear();
    }

    /// <summary>
    /// Gets the count of tracked keys in the cache.
    /// Note: This may include keys that have expired but not yet been evicted.
    /// </summary>
    public int Count => _keys.Count;

    /// <summary>
    /// Gets a snapshot of all keys currently tracked by the cache manager.
    /// </summary>
    public IReadOnlyCollection<string> GetKeys() => _keys.Keys.ToList().AsReadOnly();
}
