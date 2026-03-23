// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Abstractions;
using AdvGenNoSqlServer.Core.ETags;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Core.ETags;

/// <summary>
/// Document store wrapper that adds ETag-based optimistic concurrency control
/// Wraps an existing IDocumentStore and provides conditional update/delete operations
/// </summary>
public class ETagDocumentStore : IDocumentStore, IDisposable
{
    private readonly IDocumentStore _innerStore;
    private readonly IETagGenerator _eTagGenerator;
    private bool _isDisposed;

    /// <summary>
    /// Gets the underlying document store
    /// </summary>
    public IDocumentStore InnerStore => _innerStore;

    /// <summary>
    /// Gets the ETag generator
    /// </summary>
    public IETagGenerator ETagGenerator => _eTagGenerator;

    /// <summary>
    /// Creates a new ETag-enabled document store
    /// </summary>
    /// <param name="innerStore">The underlying document store to wrap</param>
    /// <param name="eTagGenerator">The ETag generator (optional, will create default if not provided)</param>
    public ETagDocumentStore(IDocumentStore innerStore, IETagGenerator? eTagGenerator = null)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
        _eTagGenerator = eTagGenerator ?? new ETagGenerator();
    }

    /// <summary>
    /// Gets a document with its ETag
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="documentId">The document ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tuple of document and ETag, or null if not found</returns>
    public async Task<(Document? Document, string? ETag)> GetWithETagAsync(
        string collectionName,
        string documentId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var document = await _innerStore.GetAsync(collectionName, documentId, cancellationToken);

        if (document == null)
            return (null, null);

        var eTag = _eTagGenerator.GenerateETag(document);
        return (document, eTag);
    }

    /// <summary>
    /// Gets multiple documents with their ETags
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="documentIds">The document IDs</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of tuples containing document and ETag</returns>
    public async Task<IEnumerable<(Document Document, string ETag)>> GetManyWithETagsAsync(
        string collectionName,
        IEnumerable<string> documentIds,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var documents = await _innerStore.GetManyAsync(collectionName, documentIds, cancellationToken);
        var result = new List<(Document Document, string ETag)>();

        foreach (var document in documents)
        {
            var eTag = _eTagGenerator.GenerateETag(document);
            result.Add((document, eTag));
        }

        return result;
    }

    /// <summary>
    /// Conditionally updates a document only if the ETag matches (optimistic concurrency)
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="document">The document to update</param>
    /// <param name="eTag">The expected ETag</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The updated document</returns>
    /// <exception cref="DocumentNotFoundException">If document doesn't exist</exception>
    /// <exception cref="ConcurrencyException">If ETag doesn't match</exception>
    public async Task<Document> UpdateIfMatchAsync(
        string collectionName,
        Document document,
        string eTag,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (document == null)
            throw new ArgumentNullException(nameof(document));

        if (string.IsNullOrWhiteSpace(eTag))
            throw new ArgumentException("ETag cannot be empty", nameof(eTag));

        // Get current document
        var currentDocument = await _innerStore.GetAsync(collectionName, document.Id, cancellationToken);

        if (currentDocument == null)
        {
            throw new DocumentNotFoundException(collectionName, document.Id);
        }

        // Validate ETag
        if (!_eTagGenerator.ValidateETag(currentDocument, eTag))
        {
            var currentETag = _eTagGenerator.GenerateETag(currentDocument);
            throw new ConcurrencyException(collectionName, document.Id, currentETag, eTag);
        }

        // ETag matches, proceed with update
        return await _innerStore.UpdateAsync(collectionName, document, cancellationToken);
    }

    /// <summary>
    /// Conditionally deletes a document only if the ETag matches (optimistic concurrency)
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="documentId">The document ID</param>
    /// <param name="eTag">The expected ETag</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if deleted, false if not found</returns>
    /// <exception cref="ConcurrencyException">If ETag doesn't match</exception>
    public async Task<bool> DeleteIfMatchAsync(
        string collectionName,
        string documentId,
        string eTag,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Document ID cannot be empty", nameof(documentId));

        if (string.IsNullOrWhiteSpace(eTag))
            throw new ArgumentException("ETag cannot be empty", nameof(eTag));

        // Get current document
        var currentDocument = await _innerStore.GetAsync(collectionName, documentId, cancellationToken);

        if (currentDocument == null)
        {
            return false;
        }

        // Validate ETag
        if (!_eTagGenerator.ValidateETag(currentDocument, eTag))
        {
            var currentETag = _eTagGenerator.GenerateETag(currentDocument);
            throw new ConcurrencyException(collectionName, documentId, currentETag, eTag);
        }

        // ETag matches, proceed with delete
        return await _innerStore.DeleteAsync(collectionName, documentId, cancellationToken);
    }

    /// <summary>
    /// Validates an ETag for a document without modifying it
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="documentId">The document ID</param>
    /// <param name="eTag">The ETag to validate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Validation result</returns>
    public async Task<ETagValidationResponse> ValidateETagAsync(
        string collectionName,
        string documentId,
        string? eTag,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(eTag))
            return ETagValidationResponse.ETagNotProvided();

        var document = await _innerStore.GetAsync(collectionName, documentId, cancellationToken);

        if (document == null)
            return ETagValidationResponse.DocumentNotFound(documentId);

        if (_eTagGenerator.ValidateETag(document, eTag))
            return ETagValidationResponse.Success();

        var currentETag = _eTagGenerator.GenerateETag(document);
        return ETagValidationResponse.ETagMismatch(currentETag);
    }

    /// <summary>
    /// Performs a conditional GET operation - returns document only if ETag doesn't match (for caching)
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="documentId">The document ID</param>
    /// <param name="ifNoneMatch">The ETag to check (If-None-Match header semantics)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Document if ETag doesn't match, null if it matches (304 Not Modified semantics)</returns>
    public async Task<(Document? Document, string? ETag, bool NotModified)> GetIfNoneMatchAsync(
        string collectionName,
        string documentId,
        string? ifNoneMatch,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var document = await _innerStore.GetAsync(collectionName, documentId, cancellationToken);

        if (document == null)
            return (null, null, false);

        var currentETag = _eTagGenerator.GenerateETag(document);

        // If no If-None-Match provided, return document
        if (string.IsNullOrWhiteSpace(ifNoneMatch))
            return (document, currentETag, false);

        // Check if ETag matches (indicating not modified)
        if (_eTagGenerator.ValidateETag(document, ifNoneMatch))
            return (null, currentETag, true);

        return (document, currentETag, false);
    }

    #region IDocumentStore Implementation

    /// <inheritdoc />
    public Task<Document> InsertAsync(string collectionName, Document document, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _innerStore.InsertAsync(collectionName, document, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Document?> GetAsync(string collectionName, string documentId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _innerStore.GetAsync(collectionName, documentId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IEnumerable<Document>> GetManyAsync(string collectionName, IEnumerable<string> documentIds, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _innerStore.GetManyAsync(collectionName, documentIds, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IEnumerable<Document>> GetAllAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _innerStore.GetAllAsync(collectionName, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Document> UpdateAsync(string collectionName, Document document, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _innerStore.UpdateAsync(collectionName, document, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string collectionName, string documentId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _innerStore.DeleteAsync(collectionName, documentId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string collectionName, string documentId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _innerStore.ExistsAsync(collectionName, documentId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<long> CountAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _innerStore.CountAsync(collectionName, cancellationToken);
    }

    /// <inheritdoc />
    public Task CreateCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _innerStore.CreateCollectionAsync(collectionName, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> DropCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _innerStore.DropCollectionAsync(collectionName, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IEnumerable<string>> GetCollectionsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _innerStore.GetCollectionsAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task ClearCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _innerStore.ClearCollectionAsync(collectionName, cancellationToken);
    }

    #endregion

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_isDisposed)
        {
            if (_innerStore is IDisposable disposable)
            {
                disposable.Dispose();
            }
            _isDisposed = true;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(ETagDocumentStore));
    }
}

/// <summary>
/// Extension methods for IDocumentStore to add ETag support
/// </summary>
public static class ETagDocumentStoreExtensions
{
    /// <summary>
    /// Wraps an IDocumentStore with ETag support
    /// </summary>
    /// <param name="store">The document store to wrap</param>
    /// <param name="eTagGenerator">Optional ETag generator</param>
    /// <returns>ETag-enabled document store</returns>
    public static ETagDocumentStore WithETags(this IDocumentStore store, IETagGenerator? eTagGenerator = null)
    {
        return new ETagDocumentStore(store, eTagGenerator);
    }
}
