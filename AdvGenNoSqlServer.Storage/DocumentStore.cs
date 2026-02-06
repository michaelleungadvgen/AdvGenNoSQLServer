// Copyright (c) 2026 [Your Organization]
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Storage;

/// <summary>
/// Thread-safe document store implementation with in-memory storage
/// Organizes documents into collections with versioning support
/// </summary>
public class DocumentStore : IDocumentStore
{
    private readonly ConcurrentDictionary<string, InMemoryDocumentCollection> _collections;
    private readonly ReaderWriterLockSlim _collectionsLock;

    /// <summary>
    /// Creates a new DocumentStore instance
    /// </summary>
    public DocumentStore()
    {
        _collections = new ConcurrentDictionary<string, InMemoryDocumentCollection>();
        _collectionsLock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
    }

    /// <inheritdoc />
    public Task<Document> InsertAsync(string collectionName, Document document)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        if (document == null)
            throw new ArgumentNullException(nameof(document));

        // Ensure collection exists
        var collection = GetOrCreateCollection(collectionName);

        // Insert the document
        var result = collection.Insert(document);
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<Document?> GetAsync(string collectionName, string documentId)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        if (string.IsNullOrWhiteSpace(documentId))
            return Task.FromResult<Document?>(null);

        if (!_collections.TryGetValue(collectionName, out var collection))
        {
            return Task.FromResult<Document?>(null);
        }

        var result = collection.Get(documentId);
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<IEnumerable<Document>> GetAllAsync(string collectionName)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        if (!_collections.TryGetValue(collectionName, out var collection))
        {
            return Task.FromResult<IEnumerable<Document>>(Array.Empty<Document>());
        }

        var result = collection.GetAll();
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<Document> UpdateAsync(string collectionName, Document document)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        if (document == null)
            throw new ArgumentNullException(nameof(document));

        if (!_collections.TryGetValue(collectionName, out var collection))
        {
            throw new CollectionNotFoundException(collectionName);
        }

        var result = collection.Update(document);
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string collectionName, string documentId)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        if (string.IsNullOrWhiteSpace(documentId))
            return Task.FromResult(false);

        if (!_collections.TryGetValue(collectionName, out var collection))
        {
            return Task.FromResult(false);
        }

        var result = collection.Delete(documentId);
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string collectionName, string documentId)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        if (string.IsNullOrWhiteSpace(documentId))
            return Task.FromResult(false);

        if (!_collections.TryGetValue(collectionName, out var collection))
        {
            return Task.FromResult(false);
        }

        var result = collection.Exists(documentId);
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<long> CountAsync(string collectionName)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        if (!_collections.TryGetValue(collectionName, out var collection))
        {
            return Task.FromResult(0L);
        }

        return Task.FromResult(collection.Count);
    }

    /// <inheritdoc />
    public Task CreateCollectionAsync(string collectionName)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        GetOrCreateCollection(collectionName);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> DropCollectionAsync(string collectionName)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        var removed = _collections.TryRemove(collectionName, out _);
        return Task.FromResult(removed);
    }

    /// <inheritdoc />
    public Task<IEnumerable<string>> GetCollectionsAsync()
    {
        var collectionNames = _collections.Keys.ToList();
        return Task.FromResult<IEnumerable<string>>(collectionNames);
    }

    /// <inheritdoc />
    public Task ClearCollectionAsync(string collectionName)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        if (_collections.TryGetValue(collectionName, out var collection))
        {
            collection.Clear();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets an existing collection or creates a new one
    /// </summary>
    /// <param name="collectionName">The name of the collection</param>
    /// <returns>The collection</returns>
    private InMemoryDocumentCollection GetOrCreateCollection(string collectionName)
    {
        // Try to get existing collection first
        if (_collections.TryGetValue(collectionName, out var existingCollection))
        {
            return existingCollection;
        }

        // Create new collection
        var newCollection = new InMemoryDocumentCollection(collectionName);

        // Try to add, or return existing if another thread created it
        return _collections.GetOrAdd(collectionName, newCollection);
    }
}
