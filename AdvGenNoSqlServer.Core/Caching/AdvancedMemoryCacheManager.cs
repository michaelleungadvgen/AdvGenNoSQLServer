using AdvGenNoSqlServer.Core.Models;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;

namespace AdvGenNoSqlServer.Core.Caching;

public class AdvancedMemoryCacheManager : ICacheManager
{
    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<string, byte> _keys;
    private readonly int _maxCacheSize;

    public AdvancedMemoryCacheManager(IMemoryCache cache, int maxCacheSize = 1000)
    {
        _cache = cache;
        _keys = new ConcurrentDictionary<string, byte>();
        _maxCacheSize = maxCacheSize;
    }

    public Document? Get(string key)
    {
        return _cache.Get<Document>(key);
    }

    public void Set(string key, Document document, int expirationMinutes = 30)
    {
        // Check if we're at the maximum cache size
        if (_keys.Count >= _maxCacheSize)
        {
            // Remove the oldest entry
            var oldestKey = _keys.Keys.FirstOrDefault();
            if (!string.IsNullOrEmpty(oldestKey))
            {
                _cache.Remove(oldestKey);
                _keys.TryRemove(oldestKey, out _);
            }
        }

        var cacheEntryOptions = new MemoryCacheEntryOptions()
            .SetSlidingExpiration(TimeSpan.FromMinutes(expirationMinutes))
            .RegisterPostEvictionCallback((key, value, reason, state) =>
            {
                if (key is string stringKey)
                {
                    _keys.TryRemove(stringKey, out _);
                }
            });

        _cache.Set(key, document, cacheEntryOptions);
        _keys.TryAdd(key, 0);
    }

    public void Remove(string key)
    {
        _cache.Remove(key);
        _keys.TryRemove(key, out _);
    }

    public void Clear()
    {
        foreach (var key in _keys.Keys)
        {
            _cache.Remove(key);
        }
        _keys.Clear();
    }
}