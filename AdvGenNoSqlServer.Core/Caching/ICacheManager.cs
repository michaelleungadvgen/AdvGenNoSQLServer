using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Core.Caching;

public interface ICacheManager
{
    /// <summary>
    /// Gets a document from the cache if it exists
    /// </summary>
    /// <param name="key">The cache key</param>
    /// <returns>The cached document or null if not found</returns>
    Document? Get(string key);

    /// <summary>
    /// Adds or updates a document in the cache
    /// </summary>
    /// <param name="key">The cache key</param>
    /// <param name="document">The document to cache</param>
    /// <param name="expirationMinutes">Expiration time in minutes</param>
    void Set(string key, Document document, int expirationMinutes = 30);

    /// <summary>
    /// Removes a document from the cache
    /// </summary>
    /// <param name="key">The cache key</param>
    void Remove(string key);

    /// <summary>
    /// Clears all entries from the cache
    /// </summary>
    void Clear();
}