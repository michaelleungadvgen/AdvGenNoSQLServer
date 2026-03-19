// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Abstractions;
using AdvGenNoSqlServer.Core.ChangeStreams;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Storage;

/// <summary>
/// A document store wrapper that publishes change stream events for all operations
/// </summary>
public class ChangeStreamEnabledDocumentStore : IDocumentStore, IDisposable
{
    private readonly IDocumentStore _innerStore;
    private readonly IChangeStreamManager _changeStreamManager;
    private readonly bool _captureDocumentBeforeChange;
    private bool _disposed;

    /// <summary>
    /// Creates a new change stream enabled document store
    /// </summary>
    /// <param name="innerStore">The underlying document store</param>
    /// <param name="changeStreamManager">The change stream manager for publishing events</param>
    /// <param name="captureDocumentBeforeChange">Whether to capture document state before changes</param>
    public ChangeStreamEnabledDocumentStore(
        IDocumentStore innerStore,
        IChangeStreamManager changeStreamManager,
        bool captureDocumentBeforeChange = true)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
        _changeStreamManager = changeStreamManager ?? throw new ArgumentNullException(nameof(changeStreamManager));
        _captureDocumentBeforeChange = captureDocumentBeforeChange;
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
    public async Task<Document> InsertAsync(string collectionName, Document document, CancellationToken cancellationToken = default)
    {
        var result = await _innerStore.InsertAsync(collectionName, document, cancellationToken);

        // Publish insert event
        var changeEvent = ChangeStreamEvent.CreateInsert(collectionName, result);
        _changeStreamManager.PublishEvent(changeEvent);

        return result;
    }

    /// <inheritdoc />
    public async Task<Document> UpdateAsync(string collectionName, Document document, CancellationToken cancellationToken = default)
    {
        // Capture document before change if needed
        Document? documentBeforeChange = null;
        if (_captureDocumentBeforeChange)
        {
            documentBeforeChange = await _innerStore.GetAsync(collectionName, document.Id, cancellationToken);
        }

        var result = await _innerStore.UpdateAsync(collectionName, document, cancellationToken);

        // Publish update event
        var changeEvent = ChangeStreamEvent.CreateUpdate(
            collectionName, 
            document.Id, 
            result, 
            documentBeforeChange);
        _changeStreamManager.PublishEvent(changeEvent);

        return result;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string collectionName, string documentId, CancellationToken cancellationToken = default)
    {
        // Capture document before deletion if needed
        Document? documentBeforeChange = null;
        if (_captureDocumentBeforeChange)
        {
            documentBeforeChange = await _innerStore.GetAsync(collectionName, documentId, cancellationToken);
        }

        var result = await _innerStore.DeleteAsync(collectionName, documentId, cancellationToken);

        if (result)
        {
            // Publish delete event
            var changeEvent = ChangeStreamEvent.CreateDelete(
                collectionName, 
                documentId, 
                documentBeforeChange);
            _changeStreamManager.PublishEvent(changeEvent);
        }

        return result;
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
    public async Task CreateCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        await _innerStore.CreateCollectionAsync(collectionName, cancellationToken);
        // Note: We don't publish events for collection creation currently
    }

    /// <inheritdoc />
    public async Task<bool> DropCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        var result = await _innerStore.DropCollectionAsync(collectionName, cancellationToken);

        if (result)
        {
            // Publish drop collection event
            var changeEvent = ChangeStreamEvent.CreateDropCollection(collectionName);
            _changeStreamManager.PublishEvent(changeEvent);
        }

        return result;
    }

    /// <inheritdoc />
    public Task<IEnumerable<string>> GetCollectionsAsync(CancellationToken cancellationToken = default)
    {
        return _innerStore.GetCollectionsAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task ClearCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        await _innerStore.ClearCollectionAsync(collectionName, cancellationToken);
        // Note: We could publish individual delete events, but for now we don't
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_innerStore is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
