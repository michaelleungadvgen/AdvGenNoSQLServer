using AdvGenNoSqlServer.Core.Models;
using Microsoft.Extensions.Caching.Memory;

namespace AdvGenNoSqlServer.Core.Caching;

public class MemoryCacheManager : ICacheManager
{
    private readonly IMemoryCache _cache;

    public MemoryCacheManager(IMemoryCache cache)
    {
        _cache = cache;
    }

    public Document? Get(string key)
    {
        return _cache.Get<Document>(key);
    }

    public void Set(string key, Document document, int expirationMinutes = 30)
    {
        var cacheEntryOptions = new MemoryCacheEntryOptions()
            .SetSlidingExpiration(TimeSpan.FromMinutes(expirationMinutes));

        _cache.Set(key, document, cacheEntryOptions);
    }

    public void Remove(string key)
    {
        _cache.Remove(key);
    }

    public void Clear()
    {
        // IMemoryCache doesn't have a clear method, so we would need to create a new instance
        // This is typically handled by the DI container when needed
        throw new NotImplementedException("Clear operation is not supported with IMemoryCache. " +
            "Consider using a different caching implementation or restarting the service.");
    }
}