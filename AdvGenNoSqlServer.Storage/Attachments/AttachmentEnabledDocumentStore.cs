// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Abstractions;
using AdvGenNoSqlServer.Core.Attachments;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Storage.Attachments;

/// <summary>
/// Document store wrapper that adds attachment support
/// Automatically manages attachments when documents are deleted
/// </summary>
public class AttachmentEnabledDocumentStore : IDocumentStore, IDisposable
{
    private readonly IDocumentStore _documentStore;
    private readonly IAttachmentStore _attachmentStore;
    private readonly bool _cascadeDelete;
    private bool _disposed;

    /// <summary>
    /// Creates a new attachment-enabled document store
    /// </summary>
    /// <param name="documentStore">The underlying document store</param>
    /// <param name="attachmentStore">The attachment store</param>
    /// <param name="cascadeDelete">Whether to delete attachments when document is deleted</param>
    public AttachmentEnabledDocumentStore(
        IDocumentStore documentStore, 
        IAttachmentStore attachmentStore,
        bool cascadeDelete = true)
    {
        _documentStore = documentStore ?? throw new ArgumentNullException(nameof(documentStore));
        _attachmentStore = attachmentStore ?? throw new ArgumentNullException(nameof(attachmentStore));
        _cascadeDelete = cascadeDelete;
    }

    /// <summary>
    /// Gets the attachment store for direct attachment operations
    /// </summary>
    public IAttachmentStore AttachmentStore => _attachmentStore;

    /// <inheritdoc />
    public Task<Document> InsertAsync(string collectionName, Document document, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _documentStore.InsertAsync(collectionName, document, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Document?> GetAsync(string collectionName, string documentId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _documentStore.GetAsync(collectionName, documentId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IEnumerable<Document>> GetManyAsync(string collectionName, IEnumerable<string> documentIds, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _documentStore.GetManyAsync(collectionName, documentIds, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IEnumerable<Document>> GetAllAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _documentStore.GetAllAsync(collectionName, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Document> UpdateAsync(string collectionName, Document document, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _documentStore.UpdateAsync(collectionName, document, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string collectionName, string documentId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        var result = await _documentStore.DeleteAsync(collectionName, documentId, cancellationToken);
        
        if (result && _cascadeDelete)
        {
            // Delete all attachments for this document
            await _attachmentStore.DeleteAllAsync(collectionName, documentId, cancellationToken);
        }
        
        return result;
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string collectionName, string documentId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _documentStore.ExistsAsync(collectionName, documentId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<long> CountAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _documentStore.CountAsync(collectionName, cancellationToken);
    }

    /// <inheritdoc />
    public Task CreateCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _documentStore.CreateCollectionAsync(collectionName, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> DropCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        // Get all documents to delete their attachments
        if (_cascadeDelete)
        {
            var documents = await _documentStore.GetAllAsync(collectionName, cancellationToken);
            foreach (var doc in documents)
            {
                await _attachmentStore.DeleteAllAsync(collectionName, doc.Id, cancellationToken);
            }
        }
        
        return await _documentStore.DropCollectionAsync(collectionName, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IEnumerable<string>> GetCollectionsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _documentStore.GetCollectionsAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task ClearCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _documentStore.ClearCollectionAsync(collectionName, cancellationToken);
    }

    /// <summary>
    /// Stores an attachment for a document
    /// </summary>
    public Task<AttachmentResult> StoreAttachmentAsync(
        string collectionName, 
        string documentId, 
        string name, 
        string contentType, 
        byte[] content,
        Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _attachmentStore.StoreAsync(collectionName, documentId, name, contentType, content, metadata, cancellationToken);
    }

    /// <summary>
    /// Gets an attachment
    /// </summary>
    public Task<Attachment?> GetAttachmentAsync(
        string collectionName, 
        string documentId, 
        string name,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _attachmentStore.GetAsync(collectionName, documentId, name, cancellationToken);
    }

    /// <summary>
    /// Lists all attachments for a document
    /// </summary>
    public Task<IReadOnlyList<AttachmentInfo>> ListAttachmentsAsync(
        string collectionName, 
        string documentId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _attachmentStore.ListAsync(collectionName, documentId, cancellationToken);
    }

    /// <summary>
    /// Deletes an attachment
    /// </summary>
    public Task<bool> DeleteAttachmentAsync(
        string collectionName, 
        string documentId, 
        string name,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _attachmentStore.DeleteAsync(collectionName, documentId, name, cancellationToken);
    }

    /// <summary>
    /// Checks if a document has attachments
    /// </summary>
    public async Task<bool> HasAttachmentsAsync(
        string collectionName, 
        string documentId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var attachments = await _attachmentStore.ListAsync(collectionName, documentId, cancellationToken);
        return attachments.Count > 0;
    }

    /// <summary>
    /// Gets attachment count for a document
    /// </summary>
    public async Task<int> GetAttachmentCountAsync(
        string collectionName, 
        string documentId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var attachments = await _attachmentStore.ListAsync(collectionName, documentId, cancellationToken);
        return attachments.Count;
    }

    /// <summary>
    /// Gets total storage size used by attachments
    /// </summary>
    public Task<long> GetAttachmentStorageSizeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _attachmentStore.GetTotalStorageSizeAsync(cancellationToken);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AttachmentEnabledDocumentStore));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            if (_documentStore is IDisposable docDisposable)
            {
                docDisposable.Dispose();
            }
            
            if (_attachmentStore is IDisposable attDisposable)
            {
                attDisposable.Dispose();
            }
            
            _disposed = true;
        }
    }
}

/// <summary>
/// Extension methods for document store attachment support
/// </summary>
public static class AttachmentEnabledDocumentStoreExtensions
{
    /// <summary>
    /// Wraps the document store with attachment support
    /// </summary>
    public static AttachmentEnabledDocumentStore WithAttachments(
        this IDocumentStore documentStore, 
        AttachmentStoreOptions options,
        bool cascadeDelete = true)
    {
        var attachmentStore = new AttachmentStore(options);
        return new AttachmentEnabledDocumentStore(documentStore, attachmentStore, cascadeDelete);
    }

    /// <summary>
    /// Wraps the document store with attachment support using existing attachment store
    /// </summary>
    public static AttachmentEnabledDocumentStore WithAttachments(
        this IDocumentStore documentStore, 
        IAttachmentStore attachmentStore,
        bool cascadeDelete = true)
    {
        return new AttachmentEnabledDocumentStore(documentStore, attachmentStore, cascadeDelete);
    }
}
