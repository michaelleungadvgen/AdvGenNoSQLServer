// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Abstractions;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Storage.Geospatial;

/// <summary>
/// A document store wrapper that maintains geospatial indexes.
/// Automatically indexes documents on insert/update and removes from index on delete.
/// </summary>
public sealed class GeospatialDocumentStore : IDocumentStore
{
    private readonly IDocumentStore _innerStore;
    private readonly GeospatialIndexManager _indexManager;

    /// <summary>
    /// Wraps an existing document store with geospatial indexing support.
    /// </summary>
    /// <param name="innerStore">The underlying document store to delegate to.</param>
    /// <param name="indexManager">The index manager to use; a new one is created when null.</param>
    public GeospatialDocumentStore(IDocumentStore innerStore, GeospatialIndexManager? indexManager = null)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
        _indexManager = indexManager ?? new GeospatialIndexManager();
    }

    /// <summary>
    /// The underlying document store.
    /// </summary>
    public IDocumentStore InnerStore => _innerStore;

    /// <summary>
    /// The geospatial index manager.
    /// </summary>
    public GeospatialIndexManager IndexManager => _indexManager;

    /// <summary>
    /// Creates a geospatial index on the specified field.
    /// </summary>
    public IGeospatialIndex CreateGeospatialIndex(string collectionName, string fieldName, double? cellSizeDegrees = null)
    {
        return _indexManager.CreateIndex(collectionName, fieldName, cellSizeDegrees);
    }

    /// <summary>
    /// Finds documents near a point.
    /// </summary>
    public IEnumerable<GeospatialQueryResult> FindNear(
        string collectionName, 
        string fieldName, 
        GeoPoint center, 
        double maxDistance, 
        GeospatialQueryOptions? options = null)
    {
        var index = _indexManager.GetIndex(collectionName, fieldName);
        if (index == null)
            throw new InvalidOperationException($"No geospatial index exists for {collectionName}.{fieldName}");

        return index.FindNear(center, maxDistance, options);
    }

    /// <summary>
    /// Finds documents within a bounding box.
    /// </summary>
    public IEnumerable<Document> FindWithinBox(
        string collectionName,
        string fieldName,
        GeoBoundingBox box,
        GeospatialQueryOptions? options = null)
    {
        var index = _indexManager.GetIndex(collectionName, fieldName);
        if (index == null)
            throw new InvalidOperationException($"No geospatial index exists for {collectionName}.{fieldName}");

        var entries = index.FindWithinBox(box, options);
        return entries.Select(e => _innerStore.GetAsync(collectionName, e.DocumentId).Result).Where(d => d != null)!;
    }

    /// <summary>
    /// Finds documents within a circle.
    /// </summary>
    public IEnumerable<GeospatialQueryResult> FindWithinCircle(
        string collectionName,
        string fieldName,
        GeoCircle circle,
        GeospatialQueryOptions? options = null)
    {
        var index = _indexManager.GetIndex(collectionName, fieldName);
        if (index == null)
            throw new InvalidOperationException($"No geospatial index exists for {collectionName}.{fieldName}");

        return index.FindWithinCircle(circle, options);
    }

    /// <summary>
    /// Finds documents within a polygon.
    /// </summary>
    public IEnumerable<Document> FindWithinPolygon(
        string collectionName,
        string fieldName,
        GeoPolygon polygon,
        GeospatialQueryOptions? options = null)
    {
        var index = _indexManager.GetIndex(collectionName, fieldName);
        if (index == null)
            throw new InvalidOperationException($"No geospatial index exists for {collectionName}.{fieldName}");

        var entries = index.FindWithinPolygon(polygon, options);
        return entries.Select(e => _innerStore.GetAsync(collectionName, e.DocumentId).Result).Where(d => d != null)!;
    }

    #region IDocumentStore Implementation

    /// <summary>
    /// Inserts a document into the underlying store and adds it to all geospatial indexes for the collection.
    /// </summary>
    /// <param name="collectionName">The target collection.</param>
    /// <param name="document">The document to insert.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The inserted document.</returns>
    public Task<Document> InsertAsync(string collectionName, Document document, CancellationToken cancellationToken = default)
    {
        var result = _innerStore.InsertAsync(collectionName, document, cancellationToken);
        
        // Index in all relevant geospatial indexes
        foreach (var index in _indexManager.GetCollectionIndexes(collectionName))
        {
            _indexManager.IndexDocument(collectionName, index.FieldName, document);
        }
        
        return result;
    }

    /// <summary>
    /// Retrieves a document by ID from the underlying store.
    /// </summary>
    /// <param name="collectionName">The collection to read from.</param>
    /// <param name="documentId">The document identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The document, or null if not found.</returns>
    public Task<Document?> GetAsync(string collectionName, string documentId, CancellationToken cancellationToken = default)
    {
        return _innerStore.GetAsync(collectionName, documentId, cancellationToken);
    }

    /// <summary>
    /// Retrieves multiple documents by ID from the underlying store.
    /// </summary>
    /// <param name="collectionName">The collection to read from.</param>
    /// <param name="documentIds">The document identifiers to fetch.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The documents that were found.</returns>
    public Task<IEnumerable<Document>> GetManyAsync(string collectionName, IEnumerable<string> documentIds, CancellationToken cancellationToken = default)
    {
        return _innerStore.GetManyAsync(collectionName, documentIds, cancellationToken);
    }

    /// <summary>
    /// Retrieves all documents in a collection from the underlying store.
    /// </summary>
    /// <param name="collectionName">The collection to read from.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>All documents in the collection.</returns>
    public Task<IEnumerable<Document>> GetAllAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        return _innerStore.GetAllAsync(collectionName, cancellationToken);
    }

    /// <summary>
    /// Updates a document in the underlying store and re-indexes it in all geospatial indexes for the collection.
    /// </summary>
    /// <param name="collectionName">The collection containing the document.</param>
    /// <param name="document">The document with updated data.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The updated document.</returns>
    public Task<Document> UpdateAsync(string collectionName, Document document, CancellationToken cancellationToken = default)
    {
        var result = _innerStore.UpdateAsync(collectionName, document, cancellationToken);
        
        // Re-index in all relevant geospatial indexes
        foreach (var index in _indexManager.GetCollectionIndexes(collectionName))
        {
            _indexManager.IndexDocument(collectionName, index.FieldName, document);
        }
        
        return result;
    }

    /// <summary>
    /// Deletes a document from the underlying store and removes it from all geospatial indexes for the collection.
    /// </summary>
    /// <param name="collectionName">The collection containing the document.</param>
    /// <param name="documentId">The document identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>True if the document was deleted; false if it did not exist.</returns>
    public Task<bool> DeleteAsync(string collectionName, string documentId, CancellationToken cancellationToken = default)
    {
        var result = _innerStore.DeleteAsync(collectionName, documentId, cancellationToken);
        
        // Remove from all geospatial indexes
        foreach (var index in _indexManager.GetCollectionIndexes(collectionName))
        {
            _indexManager.RemoveDocument(collectionName, index.FieldName, documentId);
        }
        
        return result;
    }

    /// <summary>
    /// Checks whether a document exists in the underlying store.
    /// </summary>
    /// <param name="collectionName">The collection to check.</param>
    /// <param name="documentId">The document identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>True if the document exists; otherwise false.</returns>
    public Task<bool> ExistsAsync(string collectionName, string documentId, CancellationToken cancellationToken = default)
    {
        return _innerStore.ExistsAsync(collectionName, documentId, cancellationToken);
    }

    /// <summary>
    /// Gets the number of documents in a collection from the underlying store.
    /// </summary>
    /// <param name="collectionName">The collection to count.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The document count.</returns>
    public Task<long> CountAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        return _innerStore.CountAsync(collectionName, cancellationToken);
    }

    /// <summary>
    /// Creates a collection in the underlying store.
    /// </summary>
    /// <param name="collectionName">The collection to create.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task that completes when the collection is created.</returns>
    public Task CreateCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        return _innerStore.CreateCollectionAsync(collectionName, cancellationToken);
    }

    /// <summary>
    /// Drops all geospatial indexes for the collection, then drops the collection from the underlying store.
    /// </summary>
    /// <param name="collectionName">The collection to drop.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>True if the collection was dropped; false if it did not exist.</returns>
    public Task<bool> DropCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        // Remove all indexes for this collection
        foreach (var index in _indexManager.GetCollectionIndexes(collectionName).ToList())
        {
            _indexManager.DropIndex(collectionName, index.FieldName);
        }
        
        return _innerStore.DropCollectionAsync(collectionName, cancellationToken);
    }

    /// <summary>
    /// Gets the names of all collections in the underlying store.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The collection names.</returns>
    public Task<IEnumerable<string>> GetCollectionsAsync(CancellationToken cancellationToken = default)
    {
        return _innerStore.GetCollectionsAsync(cancellationToken);
    }

    /// <summary>
    /// Clears all geospatial indexes for the collection, then removes all documents from the underlying store.
    /// </summary>
    /// <param name="collectionName">The collection to clear.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task that completes when the collection is cleared.</returns>
    public Task ClearCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        // Clear all indexes for this collection
        foreach (var index in _indexManager.GetCollectionIndexes(collectionName))
        {
            index.Clear();
        }
        
        return _innerStore.ClearCollectionAsync(collectionName, cancellationToken);
    }

    #endregion
}

/// <summary>
/// Extension methods for adding geospatial support to document stores.
/// </summary>
public static class GeospatialDocumentStoreExtensions
{
    /// <summary>
    /// Wraps the document store with geospatial indexing support.
    /// </summary>
    public static GeospatialDocumentStore WithGeospatialSupport(this IDocumentStore store)
    {
        return new GeospatialDocumentStore(store);
    }
}
