// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Storage;

/// <summary>
/// Extended persistent document store with garbage collection support for deleted documents
/// </summary>
public class GarbageCollectedDocumentStore : PersistentDocumentStore, IDisposable
{
    private readonly IGarbageCollector _garbageCollector;
    private readonly bool _ownsGarbageCollector;
    private bool _disposed;

    /// <summary>
    /// Gets the garbage collector associated with this store
    /// </summary>
    public IGarbageCollector GarbageCollector => _garbageCollector;

    /// <summary>
    /// Creates a new GarbageCollectedDocumentStore instance
    /// </summary>
    /// <param name="dataPath">The base directory for storing collection files</param>
    /// <param name="garbageCollectorOptions">Options for the garbage collector (null for defaults)</param>
    public GarbageCollectedDocumentStore(string dataPath, GarbageCollectorOptions? garbageCollectorOptions = null)
        : base(dataPath)
    {
        _garbageCollector = new GarbageCollector(garbageCollectorOptions);
        _ownsGarbageCollector = true;
        _disposed = false;
    }

    /// <summary>
    /// Creates a new GarbageCollectedDocumentStore with an existing garbage collector
    /// </summary>
    /// <param name="dataPath">The base directory for storing collection files</param>
    /// <param name="garbageCollector">The garbage collector to use</param>
    public GarbageCollectedDocumentStore(string dataPath, IGarbageCollector garbageCollector)
        : base(dataPath)
    {
        _garbageCollector = garbageCollector ?? throw new ArgumentNullException(nameof(garbageCollector));
        _ownsGarbageCollector = false;
        _disposed = false;
    }

    /// <inheritdoc />
    public override async Task<bool> DeleteAsync(string collectionName, string documentId)
    {
        // Get document info before deletion for the tombstone
        var document = await GetAsync(collectionName, documentId);
        var documentVersion = document?.Version ?? 0;

        // Perform the actual deletion
        var result = await base.DeleteAsync(collectionName, documentId);

        // Record the deletion in the garbage collector
        if (result)
        {
            var filePath = GetDocumentPathInternal(collectionName, documentId);
            _garbageCollector.RecordDeletion(collectionName, documentId, documentVersion, filePath);
        }

        return result;
    }

    /// <inheritdoc />
    public override async Task<bool> DropCollectionAsync(string collectionName)
    {
        // Get all document IDs in the collection before dropping
        var documents = await GetAllAsync(collectionName);
        var documentIds = documents.Select(d => d.Id).ToList();

        // Perform the actual drop
        var result = await base.DropCollectionAsync(collectionName);

        // Record deletions for all documents in the collection
        if (result)
        {
            foreach (var docId in documentIds)
            {
                var filePath = GetDocumentPathInternal(collectionName, docId);
                _garbageCollector.RecordDeletion(collectionName, docId, 0, filePath);
            }
        }

        return result;
    }

    /// <summary>
    /// Runs garbage collection to clean up deleted documents
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The number of documents cleaned up</returns>
    public Task<int> CollectGarbageAsync(CancellationToken cancellationToken = default)
    {
        return _garbageCollector.CollectAsync(cancellationToken);
    }

    /// <summary>
    /// Gets garbage collection statistics
    /// </summary>
    /// <returns>Statistics snapshot</returns>
    public GarbageCollectorStats GetGarbageCollectionStats()
    {
        return _garbageCollector.GetStatistics();
    }

    /// <summary>
    /// Gets the path where a document would be stored
    /// </summary>
    private string GetDocumentPathInternal(string collectionName, string documentId)
    {
        var collectionPath = Path.Combine(DataPath, SanitizeFileName(collectionName));
        var sanitizedId = SanitizeFileName(documentId);
        return Path.Combine(collectionPath, $"{sanitizedId}.json");
    }

    private string SanitizeFileName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Where(c => !invalidChars.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "_" : sanitized;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        if (_ownsGarbageCollector && _garbageCollector is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _disposed = true;
    }
}
