// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;
using AdvGenNoSqlServer.Core.Abstractions;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Storage;

/// <summary>
/// Document store that supports both regular and capped collections
/// Capped collections maintain a fixed size by automatically removing oldest documents
/// </summary>
public class CappedDocumentStore : IDocumentStore
{
    private readonly IDocumentStore _underlyingStore;
    private readonly ConcurrentDictionary<string, CappedCollection> _cappedCollections;
    private readonly ConcurrentDictionary<string, CappedCollectionOptions> _cappedCollectionOptions;

    /// <summary>
    /// Event raised when a capped collection is trimmed
    /// </summary>
    public event EventHandler<CappedCollectionTrimmedEventArgs>? CappedCollectionTrimmed;

    /// <summary>
    /// Creates a new CappedDocumentStore wrapping an existing document store
    /// </summary>
    /// <param name="underlyingStore">The underlying document store for non-capped collections</param>
    public CappedDocumentStore(IDocumentStore underlyingStore)
    {
        _underlyingStore = underlyingStore ?? throw new ArgumentNullException(nameof(underlyingStore));
        _cappedCollections = new ConcurrentDictionary<string, CappedCollection>();
        _cappedCollectionOptions = new ConcurrentDictionary<string, CappedCollectionOptions>();
    }

    /// <summary>
    /// Creates a new capped collection with the specified options
    /// </summary>
    /// <param name="collectionName">The name of the collection</param>
    /// <param name="options">The capped collection options</param>
    public async Task CreateCappedCollectionAsync(string collectionName, CappedCollectionOptions options)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        if (options == null)
            throw new ArgumentNullException(nameof(options));

        // Validate options
        if (options.EnforceMaxDocuments && options.MaxDocuments <= 0)
            throw new ArgumentException("MaxDocuments must be greater than 0", nameof(options));

        if (options.EnforceMaxSize && options.MaxSizeBytes <= 0)
            throw new ArgumentException("MaxSizeBytes must be greater than 0", nameof(options));

        // Create underlying collection first
        await _underlyingStore.CreateCollectionAsync(collectionName);

        // Register as capped collection
        var cappedCollection = new CappedCollection(collectionName, options);
        cappedCollection.CollectionTrimmed += (s, e) => CappedCollectionTrimmed?.Invoke(s, e);

        _cappedCollections[collectionName] = cappedCollection;
        _cappedCollectionOptions[collectionName] = options;
    }

    /// <summary>
    /// Checks if a collection is capped
    /// </summary>
    /// <param name="collectionName">The name of the collection</param>
    /// <returns>True if the collection is capped, false otherwise</returns>
    public bool IsCappedCollection(string collectionName)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            return false;

        return _cappedCollections.ContainsKey(collectionName);
    }

    /// <summary>
    /// Gets the options for a capped collection
    /// </summary>
    /// <param name="collectionName">The name of the collection</param>
    /// <returns>The options if the collection is capped, null otherwise</returns>
    public CappedCollectionOptions? GetCappedCollectionOptions(string collectionName)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            return null;

        _cappedCollectionOptions.TryGetValue(collectionName, out var options);
        return options;
    }

    /// <summary>
    /// Gets statistics for a capped collection
    /// </summary>
    /// <param name="collectionName">The name of the collection</param>
    /// <returns>Statistics if the collection is capped, null otherwise</returns>
    public CappedCollectionStats? GetCappedCollectionStats(string collectionName)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            return null;

        if (_cappedCollections.TryGetValue(collectionName, out var collection))
        {
            return collection.GetStats();
        }

        return null;
    }

    /// <summary>
    /// Gets all documents from a capped collection in natural (insertion) order
    /// </summary>
    /// <param name="collectionName">The name of the capped collection</param>
    /// <param name="limit">Optional limit on number of documents</param>
    /// <returns>Documents in insertion order</returns>
    public Task<IEnumerable<Document>> GetAllInNaturalOrderAsync(string collectionName, int? limit = null)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        if (_cappedCollections.TryGetValue(collectionName, out var cappedCollection))
        {
            return Task.FromResult(cappedCollection.GetNaturalOrder(limit));
        }

        throw new InvalidOperationException($"Collection '{collectionName}' is not a capped collection");
    }

    /// <summary>
    /// Gets the most recent documents from a capped collection
    /// </summary>
    /// <param name="collectionName">The name of the capped collection</param>
    /// <param name="limit">Optional limit on number of documents</param>
    /// <returns>Most recent documents</returns>
    public Task<IEnumerable<Document>> GetRecentAsync(string collectionName, int? limit = null)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        if (_cappedCollections.TryGetValue(collectionName, out var cappedCollection))
        {
            return Task.FromResult(cappedCollection.GetRecent(limit));
        }

        throw new InvalidOperationException($"Collection '{collectionName}' is not a capped collection");
    }

    #region IDocumentStore Implementation

    /// <inheritdoc />
    public Task<Document> InsertAsync(string collectionName, Document document, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        if (document == null)
            throw new ArgumentNullException(nameof(document));

        // Route to capped collection if it exists
        if (_cappedCollections.TryGetValue(collectionName, out var cappedCollection))
        {
            return Task.FromResult(cappedCollection.Insert(document));
        }

        return _underlyingStore.InsertAsync(collectionName, document, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Document?> GetAsync(string collectionName, string documentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        if (_cappedCollections.TryGetValue(collectionName, out var cappedCollection))
        {
            return Task.FromResult(cappedCollection.Get(documentId));
        }

        return _underlyingStore.GetAsync(collectionName, documentId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IEnumerable<Document>> GetManyAsync(string collectionName, IEnumerable<string> documentIds, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        if (_cappedCollections.TryGetValue(collectionName, out var cappedCollection))
        {
            return Task.FromResult(cappedCollection.GetMany(documentIds));
        }

        return _underlyingStore.GetManyAsync(collectionName, documentIds, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IEnumerable<Document>> GetAllAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        if (_cappedCollections.TryGetValue(collectionName, out var cappedCollection))
        {
            return Task.FromResult(cappedCollection.GetAll());
        }

        return _underlyingStore.GetAllAsync(collectionName, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Document> UpdateAsync(string collectionName, Document document, CancellationToken cancellationToken = default)
    {
        // Capped collections typically don't support updates
        // but we'll delegate to underlying store for non-capped collections
        if (_cappedCollections.ContainsKey(collectionName))
        {
            throw new NotSupportedException("Capped collections do not support document updates. Insert new documents instead.");
        }

        return _underlyingStore.UpdateAsync(collectionName, document, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string collectionName, string documentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        if (_cappedCollections.TryGetValue(collectionName, out var cappedCollection))
        {
            return Task.FromResult(cappedCollection.Delete(documentId));
        }

        return _underlyingStore.DeleteAsync(collectionName, documentId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string collectionName, string documentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        if (_cappedCollections.TryGetValue(collectionName, out var cappedCollection))
        {
            return Task.FromResult(cappedCollection.Exists(documentId));
        }

        return _underlyingStore.ExistsAsync(collectionName, documentId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<long> CountAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        if (_cappedCollections.TryGetValue(collectionName, out var cappedCollection))
        {
            return Task.FromResult(cappedCollection.Count);
        }

        return _underlyingStore.CountAsync(collectionName, cancellationToken);
    }

    /// <inheritdoc />
    public Task CreateCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        return _underlyingStore.CreateCollectionAsync(collectionName, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> DropCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        // Remove from capped collections if present
        _cappedCollections.TryRemove(collectionName, out _);
        _cappedCollectionOptions.TryRemove(collectionName, out _);

        return await _underlyingStore.DropCollectionAsync(collectionName, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<string>> GetCollectionsAsync(CancellationToken cancellationToken = default)
    {
        var collections = (await _underlyingStore.GetCollectionsAsync(cancellationToken)).ToList();
        
        // Add any capped collections that might not be in the underlying store
        foreach (var cappedCollectionName in _cappedCollections.Keys)
        {
            if (!collections.Contains(cappedCollectionName))
            {
                collections.Add(cappedCollectionName);
            }
        }

        return collections;
    }

    /// <inheritdoc />
    public async Task ClearCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        if (_cappedCollections.TryGetValue(collectionName, out var cappedCollection))
        {
            cappedCollection.Clear();
        }

        await _underlyingStore.ClearCollectionAsync(collectionName, cancellationToken);
    }

    #endregion
}

/// <summary>
/// Exception thrown when a capped collection operation fails
/// </summary>
public class CappedCollectionException : DocumentStoreException
{
    public CappedCollectionException(string message) : base(message) { }
    public CappedCollectionException(string message, Exception innerException) : base(message, innerException) { }
}
