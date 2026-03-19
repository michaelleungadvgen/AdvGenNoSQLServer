// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Abstractions;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Core.WriteConcern;

namespace AdvGenNoSqlServer.Storage;

/// <summary>
/// Document store wrapper that applies write concern to all write operations.
/// Provides configurable durability guarantees for write operations.
/// </summary>
public class WriteConcernDocumentStore : IDocumentStore
{
    private readonly IDocumentStore _innerStore;
    private readonly IWriteConcernManager _writeConcernManager;
    private readonly bool _isPersistentStore;

    /// <summary>
    /// Creates a new WriteConcernDocumentStore wrapping the specified document store.
    /// </summary>
    /// <param name="innerStore">The underlying document store.</param>
    /// <param name="writeConcernManager">The write concern manager.</param>
    public WriteConcernDocumentStore(IDocumentStore innerStore, IWriteConcernManager writeConcernManager)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
        _writeConcernManager = writeConcernManager ?? throw new ArgumentNullException(nameof(writeConcernManager));
        _isPersistentStore = innerStore is IPersistentDocumentStore;
    }

    /// <summary>
    /// Gets the inner document store.
    /// </summary>
    public IDocumentStore InnerStore => _innerStore;

    /// <summary>
    /// Gets the write concern manager.
    /// </summary>
    public IWriteConcernManager WriteConcernManager => _writeConcernManager;

    /// <inheritdoc />
    public async Task<Document> InsertAsync(string collectionName, Document document, CancellationToken cancellationToken = default)
    {
        var concern = _writeConcernManager.GetWriteConcernForCollection(collectionName);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Perform the insert
            var result = await _innerStore.InsertAsync(collectionName, document, cancellationToken);

            // Handle journal requirement for persistent stores
            if (concern.IsJournaled && _isPersistentStore)
            {
                await FlushToJournalAsync(cancellationToken);
            }

            stopwatch.Stop();
            RecordOperation(concern, stopwatch.Elapsed);

            return result;
        }
        catch (OperationCanceledException)
        {
            _writeConcernManager.GetStatistics();
            throw;
        }
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
    public async Task<Document> UpdateAsync(string collectionName, Document document, CancellationToken cancellationToken = default)
    {
        var concern = _writeConcernManager.GetWriteConcernForCollection(collectionName);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Perform the update
            var result = await _innerStore.UpdateAsync(collectionName, document, cancellationToken);

            // Handle journal requirement for persistent stores
            if (concern.IsJournaled && _isPersistentStore)
            {
                await FlushToJournalAsync(cancellationToken);
            }

            stopwatch.Stop();
            RecordOperation(concern, stopwatch.Elapsed);

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string collectionName, string documentId, CancellationToken cancellationToken = default)
    {
        var concern = _writeConcernManager.GetWriteConcernForCollection(collectionName);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Perform the delete
            var result = await _innerStore.DeleteAsync(collectionName, documentId, cancellationToken);

            // Handle journal requirement for persistent stores
            if (concern.IsJournaled && _isPersistentStore)
            {
                await FlushToJournalAsync(cancellationToken);
            }

            stopwatch.Stop();
            RecordOperation(concern, stopwatch.Elapsed);

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
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
    public async Task<bool> DropCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        var concern = _writeConcernManager.GetWriteConcernForCollection(collectionName);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var result = await _innerStore.DropCollectionAsync(collectionName, cancellationToken);

            if (concern.IsJournaled && _isPersistentStore)
            {
                await FlushToJournalAsync(cancellationToken);
            }

            stopwatch.Stop();
            RecordOperation(concern, stopwatch.Elapsed);

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }

    /// <inheritdoc />
    public Task<IEnumerable<string>> GetCollectionsAsync(CancellationToken cancellationToken = default)
    {
        return _innerStore.GetCollectionsAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task ClearCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        var concern = _writeConcernManager.GetWriteConcernForCollection(collectionName);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            await _innerStore.ClearCollectionAsync(collectionName, cancellationToken);

            if (concern.IsJournaled && _isPersistentStore)
            {
                await FlushToJournalAsync(cancellationToken);
            }

            stopwatch.Stop();
            RecordOperation(concern, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }

    /// <summary>
    /// Performs a write operation with a specific write concern.
    /// </summary>
    /// <param name="collectionName">The collection name.</param>
    /// <param name="operation">The operation to perform.</param>
    /// <param name="writeConcern">The write concern to use.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<WriteConcernResult> ExecuteWithWriteConcernAsync(
        string collectionName,
        Func<IDocumentStore, CancellationToken, Task<Document>> operation,
        WriteConcern writeConcern,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var document = await operation(_innerStore, cancellationToken);

            if (writeConcern.IsJournaled && _isPersistentStore)
            {
                await FlushToJournalAsync(cancellationToken);
            }

            stopwatch.Stop();
            RecordOperation(writeConcern, stopwatch.Elapsed);

            return WriteConcernResult.SuccessResult(document, writeConcern);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return WriteConcernResult.FailureResult(writeConcern, ex.Message, ex);
        }
    }

    /// <summary>
    /// Performs a batch of write operations with the specified write concern.
    /// </summary>
    /// <param name="collectionName">The collection name.</param>
    /// <param name="documents">The documents to insert.</param>
    /// <param name="writeConcern">The write concern to use.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The batch result.</returns>
    public async Task<WriteConcernBatchResult> BatchInsertAsync(
        string collectionName,
        IEnumerable<Document> documents,
        WriteConcern writeConcern,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var results = new List<WriteConcernResult>();

        foreach (var document in documents)
        {
            try
            {
                var result = await InsertAsync(collectionName, document, cancellationToken);
                results.Add(WriteConcernResult.SuccessResult(result, writeConcern));
            }
            catch (Exception ex)
            {
                results.Add(WriteConcernResult.FailureResult(writeConcern, ex.Message, ex));
            }
        }

        if (writeConcern.IsJournaled && _isPersistentStore)
        {
            await FlushToJournalAsync(cancellationToken);
        }

        stopwatch.Stop();

        var batchResult = WriteConcernBatchResult.FromResults(results, writeConcern);
        batchResult.ExecutionTime = stopwatch.Elapsed;
        return batchResult;
    }

    /// <summary>
    /// Flushes data to the journal/disk for durability.
    /// </summary>
    private async Task FlushToJournalAsync(CancellationToken cancellationToken)
    {
        if (_innerStore is IPersistentDocumentStore persistentStore)
        {
            await persistentStore.SaveChangesAsync();
        }
        else if (_innerStore is HybridDocumentStore hybridStore)
        {
            await hybridStore.FlushAsync();
        }
        // For other store types, we can't force a flush
    }

    private void RecordOperation(WriteConcern concern, TimeSpan elapsed)
    {
        // Statistics recording is handled internally by the manager
        // This is a placeholder for future metrics integration
    }
}

/// <summary>
/// Extension methods for write concern support.
/// </summary>
public static class WriteConcernDocumentStoreExtensions
{
    /// <summary>
    /// Wraps the document store with write concern support.
    /// </summary>
    /// <param name="store">The document store to wrap.</param>
    /// <param name="writeConcernManager">The write concern manager. If null, a default one is created.</param>
    /// <returns>A WriteConcernDocumentStore wrapper.</returns>
    public static WriteConcernDocumentStore WithWriteConcern(
        this IDocumentStore store,
        IWriteConcernManager? writeConcernManager = null)
    {
        return new WriteConcernDocumentStore(store, writeConcernManager ?? new WriteConcernManager());
    }

    /// <summary>
    /// Wraps the document store with write concern support using specific options.
    /// </summary>
    /// <param name="store">The document store to wrap.</param>
    /// <param name="options">The write concern options.</param>
    /// <returns>A WriteConcernDocumentStore wrapper.</returns>
    public static WriteConcernDocumentStore WithWriteConcern(
        this IDocumentStore store,
        WriteConcernOptions options)
    {
        var manager = new WriteConcernManager(options);
        return new WriteConcernDocumentStore(store, manager);
    }

    /// <summary>
    /// Wraps the document store with a specific default write concern.
    /// </summary>
    /// <param name="store">The document store to wrap.</param>
    /// <param name="defaultWriteConcern">The default write concern.</param>
    /// <returns>A WriteConcernDocumentStore wrapper.</returns>
    public static WriteConcernDocumentStore WithWriteConcern(
        this IDocumentStore store,
        WriteConcern defaultWriteConcern)
    {
        var options = new WriteConcernOptions { DefaultWriteConcern = defaultWriteConcern };
        return store.WithWriteConcern(options);
    }
}
