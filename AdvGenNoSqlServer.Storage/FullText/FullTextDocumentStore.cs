// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Abstractions;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Storage.FullText;

/// <summary>
/// Document store wrapper that automatically maintains full-text indexes
/// Wraps any IDocumentStore and updates indexes on insert/update/delete operations
/// </summary>
public class FullTextDocumentStore : IDocumentStore
{
    private readonly IDocumentStore _innerStore;
    private readonly FullTextIndexManager _indexManager;

    /// <summary>
    /// Gets the full-text index manager for creating and managing indexes
    /// </summary>
    public FullTextIndexManager IndexManager => _indexManager;

    /// <summary>
    /// Creates a new FullTextDocumentStore
    /// </summary>
    /// <param name="innerStore">The underlying document store</param>
    public FullTextDocumentStore(IDocumentStore innerStore)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
        _indexManager = new FullTextIndexManager();
    }

    /// <inheritdoc />
    public Task<Document> InsertAsync(string collectionName, Document document, CancellationToken cancellationToken = default)
    {
        // Insert into underlying store first
        var result = _innerStore.InsertAsync(collectionName, document, cancellationToken);
        
        // Update full-text indexes
        _indexManager.IndexDocument(collectionName, document);
        
        return result;
    }

    /// <inheritdoc />
    public Task<Document?> GetAsync(string collectionName, string documentId, CancellationToken cancellationToken = default)
    {
        return _innerStore.GetAsync(collectionName, documentId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IEnumerable<Document>> GetManyAsync(string collectionName, IEnumerable<string> documentIds, CancellationToken cancellationToken = default)
    {
        return _innerStore.GetManyAsync(collectionName, documentIds, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IEnumerable<Document>> GetAllAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        return _innerStore.GetAllAsync(collectionName, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Document> UpdateAsync(string collectionName, Document document, CancellationToken cancellationToken = default)
    {
        // Update underlying store first
        var result = _innerStore.UpdateAsync(collectionName, document, cancellationToken);
        
        // Update full-text indexes
        _indexManager.UpdateDocument(collectionName, document);
        
        return result;
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string collectionName, string documentId, CancellationToken cancellationToken = default)
    {
        // Remove from full-text indexes first
        _indexManager.RemoveDocument(collectionName, documentId);
        
        // Delete from underlying store
        return _innerStore.DeleteAsync(collectionName, documentId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string collectionName, string documentId, CancellationToken cancellationToken = default)
    {
        return _innerStore.ExistsAsync(collectionName, documentId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<long> CountAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        return _innerStore.CountAsync(collectionName, cancellationToken);
    }

    /// <inheritdoc />
    public Task CreateCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        return _innerStore.CreateCollectionAsync(collectionName, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> DropCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        // Drop full-text indexes for this collection
        _indexManager.DropCollectionIndexes(collectionName);
        
        // Drop collection from underlying store
        return _innerStore.DropCollectionAsync(collectionName, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IEnumerable<string>> GetCollectionsAsync(CancellationToken cancellationToken = default)
    {
        return _innerStore.GetCollectionsAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task ClearCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        // Clear full-text indexes for this collection
        _indexManager.DropCollectionIndexes(collectionName);
        
        // Clear collection from underlying store
        return _innerStore.ClearCollectionAsync(collectionName, cancellationToken);
    }

    /// <summary>
    /// Creates a full-text index on a field
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="fieldName">The field to index</param>
    /// <param name="analyzerType">The analyzer type</param>
    /// <returns>The created full-text index</returns>
    public IFullTextIndex CreateFullTextIndex(string collectionName, string fieldName, AnalyzerType analyzerType = AnalyzerType.Standard)
    {
        var index = _indexManager.CreateIndex(collectionName, fieldName, analyzerType);
        
        // Index existing documents
        var documents = _innerStore.GetAllAsync(collectionName).Result;
        foreach (var doc in documents)
        {
            _indexManager.IndexDocument(collectionName, doc);
        }
        
        return index;
    }

    /// <summary>
    /// Searches the full-text index
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="query">The search query</param>
    /// <param name="options">Search options</param>
    /// <returns>Search results with document IDs</returns>
    public FullTextSearchResult Search(string collectionName, string query, FullTextSearchOptions? options = null)
    {
        return _indexManager.Search(collectionName, query, options);
    }

    /// <summary>
    /// Searches and retrieves full documents
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="query">The search query</param>
    /// <param name="options">Search options</param>
    /// <returns>Matching documents with search scores</returns>
    public async Task<IEnumerable<(Document Document, double Score)>> SearchDocumentsAsync(
        string collectionName, 
        string query, 
        FullTextSearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var searchResult = _indexManager.Search(collectionName, query, options);
        
        if (!searchResult.Success || searchResult.Results.Count == 0)
        {
            return Enumerable.Empty<(Document, double)>();
        }

        var documentIds = searchResult.Results.Select(r => r.DocumentId);
        var documents = await _innerStore.GetManyAsync(collectionName, documentIds, cancellationToken);
        
        var docDictionary = documents.ToDictionary(d => d.Id);
        
        return searchResult.Results
            .Where(r => docDictionary.ContainsKey(r.DocumentId))
            .Select(r => (docDictionary[r.DocumentId], r.Score));
    }
}

/// <summary>
/// Extension methods for document store full-text search
/// </summary>
public static class FullTextDocumentStoreExtensions
{
    /// <summary>
    /// Wraps an IDocumentStore with full-text search capabilities
    /// </summary>
    public static FullTextDocumentStore WithFullTextSearch(this IDocumentStore store)
    {
        return new FullTextDocumentStore(store);
    }
}
