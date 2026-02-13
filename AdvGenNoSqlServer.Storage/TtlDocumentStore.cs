// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Storage.Indexing;

namespace AdvGenNoSqlServer.Storage;

/// <summary>
/// Document store wrapper that adds TTL (Time-To-Live) index support for automatic document expiration
/// Wraps an existing IDocumentStore and adds expiration tracking
/// </summary>
public class TtlDocumentStore : IDocumentStore, IDisposable
{
    private readonly IDocumentStore _innerStore;
    private readonly ITtlIndexService _ttlService;
    private bool _isDisposed;

    /// <summary>
    /// Gets the underlying document store
    /// </summary>
    public IDocumentStore InnerStore => _innerStore;

    /// <summary>
    /// Gets the TTL index service
    /// </summary>
    public ITtlIndexService TtlService => _ttlService;

    /// <summary>
    /// Creates a new TTL-enabled document store
    /// </summary>
    /// <param name="innerStore">The underlying document store to wrap</param>
    /// <param name="ttlService">The TTL index service (optional, will create default if not provided)</param>
    public TtlDocumentStore(IDocumentStore innerStore, ITtlIndexService? ttlService = null)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
        _ttlService = ttlService ?? new TtlIndexService(async (collection, docId) =>
        {
            // Default delete callback uses the inner store
            await innerStore.DeleteAsync(collection, docId);
            return true;
        });
    }

    /// <summary>
    /// Creates a TTL index on a collection field
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="expireAfterField">The field containing expiration time</param>
    /// <param name="defaultExpireAfter">Default expiration time if field is not present</param>
    /// <param name="cleanupInterval">Interval for cleanup operations</param>
    public void CreateTtlIndex(string collectionName, string expireAfterField, TimeSpan? defaultExpireAfter = null, TimeSpan? cleanupInterval = null)
    {
        var config = new TtlIndexConfiguration
        {
            CollectionName = collectionName,
            ExpireAfterField = expireAfterField,
            DefaultExpireAfter = defaultExpireAfter,
            CleanupInterval = cleanupInterval ?? TimeSpan.FromMinutes(1)
        };

        _ttlService.CreateTtlIndex(config);
    }

    /// <inheritdoc />
    public Task<Document> InsertAsync(string collectionName, Document document)
    {
        ThrowIfDisposed();

        // Register for TTL tracking before inserting
        _ttlService.RegisterDocument(collectionName, document);

        return _innerStore.InsertAsync(collectionName, document);
    }

    /// <inheritdoc />
    public Task<Document?> GetAsync(string collectionName, string documentId)
    {
        ThrowIfDisposed();
        return _innerStore.GetAsync(collectionName, documentId);
    }

    /// <inheritdoc />
    public Task<IEnumerable<Document>> GetAllAsync(string collectionName)
    {
        ThrowIfDisposed();
        return _innerStore.GetAllAsync(collectionName);
    }

    /// <inheritdoc />
    public Task<Document> UpdateAsync(string collectionName, Document document)
    {
        ThrowIfDisposed();

        // Re-register for TTL tracking with potentially new expiration time
        _ttlService.RegisterDocument(collectionName, document);

        return _innerStore.UpdateAsync(collectionName, document);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string collectionName, string documentId)
    {
        ThrowIfDisposed();

        // Unregister from TTL tracking
        _ttlService.UnregisterDocument(collectionName, documentId);

        return _innerStore.DeleteAsync(collectionName, documentId);
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string collectionName, string documentId)
    {
        ThrowIfDisposed();
        return _innerStore.ExistsAsync(collectionName, documentId);
    }

    /// <inheritdoc />
    public Task<long> CountAsync(string collectionName)
    {
        ThrowIfDisposed();
        return _innerStore.CountAsync(collectionName);
    }

    /// <inheritdoc />
    public Task CreateCollectionAsync(string collectionName)
    {
        ThrowIfDisposed();
        return _innerStore.CreateCollectionAsync(collectionName);
    }

    /// <inheritdoc />
    public Task<bool> DropCollectionAsync(string collectionName)
    {
        ThrowIfDisposed();

        // Drop any TTL index for this collection
        _ttlService.DropTtlIndex(collectionName);

        return _innerStore.DropCollectionAsync(collectionName);
    }

    /// <inheritdoc />
    public Task<IEnumerable<string>> GetCollectionsAsync()
    {
        ThrowIfDisposed();
        return _innerStore.GetCollectionsAsync();
    }

    /// <inheritdoc />
    public Task ClearCollectionAsync(string collectionName)
    {
        ThrowIfDisposed();

        // Clear all TTL tracking for this collection
        if (_ttlService.HasTtlIndex(collectionName))
        {
            _ttlService.DropTtlIndex(collectionName);
            _ttlService.CreateTtlIndex(new TtlIndexConfiguration
            {
                CollectionName = collectionName,
                ExpireAfterField = "expireAt" // Will be recreated when new documents are inserted
            });
        }

        return _innerStore.ClearCollectionAsync(collectionName);
    }

    /// <summary>
    /// Triggers manual cleanup of expired documents
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of documents cleaned up</returns>
    public Task<int> CleanupExpiredDocumentsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _ttlService.CleanupExpiredDocumentsAsync(cancellationToken);
    }

    /// <summary>
    /// Gets TTL index statistics
    /// </summary>
    /// <returns>Statistics for TTL operations</returns>
    public TtlIndexStatistics GetTtlStatistics()
    {
        return _ttlService.GetStatistics();
    }

    /// <summary>
    /// Starts the background cleanup service
    /// </summary>
    public Task StartCleanupServiceAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _ttlService.StartAsync(cancellationToken);
    }

    /// <summary>
    /// Stops the background cleanup service
    /// </summary>
    public Task StopCleanupServiceAsync(CancellationToken cancellationToken = default)
    {
        return _ttlService.StopAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _ttlService.Dispose();

        if (_innerStore is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(TtlDocumentStore));
    }
}
