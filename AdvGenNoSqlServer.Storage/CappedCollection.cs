// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Storage;

/// <summary>
/// Configuration options for a capped collection
/// </summary>
public class CappedCollectionOptions
{
    /// <summary>
    /// Maximum number of documents in the collection
    /// When exceeded, oldest documents are removed
    /// </summary>
    public long MaxDocuments { get; set; } = 10000;

    /// <summary>
    /// Maximum size of the collection in bytes (approximate)
    /// When exceeded, oldest documents are removed
    /// </summary>
    public long MaxSizeBytes { get; set; } = 100 * 1024 * 1024; // 100MB default

    /// <summary>
    /// Whether to enforce document count limit
    /// </summary>
    public bool EnforceMaxDocuments { get; set; } = true;

    /// <summary>
    /// Whether to enforce size limit
    /// </summary>
    public bool EnforceMaxSize { get; set; } = true;
}

/// <summary>
/// A capped collection that maintains a fixed size by removing oldest documents
/// when limits are exceeded. Documents are stored in insertion order.
/// Similar to MongoDB's capped collections.
/// </summary>
public class CappedCollection
{
    private readonly ConcurrentDictionary<string, Document> _documents;
    private readonly ConcurrentQueue<string> _insertionOrder;
    private long _documentCount;
    private long _totalSizeBytes;
    private readonly object _trimLock = new();

    /// <summary>
    /// Gets the name of the collection
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the configuration options for this capped collection
    /// </summary>
    public CappedCollectionOptions Options { get; }

    /// <summary>
    /// Gets the number of documents in the collection
    /// </summary>
    public long Count => Interlocked.Read(ref _documentCount);

    /// <summary>
    /// Gets the approximate total size of documents in bytes
    /// </summary>
    public long TotalSizeBytes => Interlocked.Read(ref _totalSizeBytes);

    /// <summary>
    /// Gets the timestamp when the collection was created
    /// </summary>
    public DateTime CreatedAt { get; }

    /// <summary>
    /// Event raised when documents are automatically removed due to size constraints
    /// </summary>
    public event EventHandler<CappedCollectionTrimmedEventArgs>? CollectionTrimmed;

    /// <summary>
    /// Creates a new capped collection with the specified options
    /// </summary>
    /// <param name="name">The name of the collection</param>
    /// <param name="options">The capped collection options</param>
    public CappedCollection(string name, CappedCollectionOptions options)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Collection name cannot be empty", nameof(name));

        Name = name;
        Options = options ?? throw new ArgumentNullException(nameof(options));
        _documents = new ConcurrentDictionary<string, Document>();
        _insertionOrder = new ConcurrentQueue<string>();
        _documentCount = 0;
        _totalSizeBytes = 0;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Inserts a new document into the capped collection
    /// If limits are exceeded, oldest documents are removed
    /// </summary>
    /// <param name="document">The document to insert</param>
    /// <returns>The inserted document</returns>
    public Document Insert(Document document)
    {
        if (document == null)
            throw new ArgumentNullException(nameof(document));

        if (string.IsNullOrWhiteSpace(document.Id))
            throw new ArgumentException("Document ID cannot be empty", nameof(document));

        // Set metadata
        var now = DateTime.UtcNow;
        var documentToStore = new Document
        {
            Id = document.Id,
            Data = document.Data ?? new Dictionary<string, object>(),
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1
        };

        // Calculate approximate document size
        var docSize = EstimateDocumentSize(documentToStore);

        // Add the document
        if (!_documents.TryAdd(document.Id, documentToStore))
        {
            throw new DocumentAlreadyExistsException(Name, document.Id);
        }

        _insertionOrder.Enqueue(document.Id);
        Interlocked.Increment(ref _documentCount);
        Interlocked.Add(ref _totalSizeBytes, docSize);

        // Trim if necessary
        TrimIfNeeded();

        return documentToStore;
    }

    /// <summary>
    /// Retrieves a document by ID
    /// </summary>
    /// <param name="documentId">The ID of the document to retrieve</param>
    /// <returns>The document if found, null otherwise</returns>
    public Document? Get(string documentId)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            return null;

        _documents.TryGetValue(documentId, out var document);
        return document;
    }

    /// <summary>
    /// Retrieves multiple documents by their IDs
    /// </summary>
    /// <param name="documentIds">The IDs of the documents to retrieve</param>
    /// <returns>Enumerable of found documents</returns>
    public IEnumerable<Document> GetMany(IEnumerable<string> documentIds)
    {
        if (documentIds == null)
            yield break;

        foreach (var id in documentIds)
        {
            if (string.IsNullOrWhiteSpace(id))
                continue;

            if (_documents.TryGetValue(id, out var document))
            {
                yield return document;
            }
        }
    }

    /// <summary>
    /// Retrieves all documents in insertion order (oldest first)
    /// </summary>
    /// <returns>Enumerable of all documents in insertion order</returns>
    public IEnumerable<Document> GetAll()
    {
        foreach (var id in _insertionOrder)
        {
            if (_documents.TryGetValue(id, out var document))
            {
                yield return document;
            }
        }
    }

    /// <summary>
    /// Retrieves documents in natural order (insertion order) with optional limit
    /// </summary>
    /// <param name="limit">Maximum number of documents to return</param>
    /// <returns>Enumerable of documents</returns>
    public IEnumerable<Document> GetNaturalOrder(int? limit = null)
    {
        int count = 0;
        foreach (var id in _insertionOrder)
        {
            if (limit.HasValue && count >= limit.Value)
                break;

            if (_documents.TryGetValue(id, out var document))
            {
                yield return document;
                count++;
            }
        }
    }

    /// <summary>
    /// Retrieves the most recent documents (newest first)
    /// </summary>
    /// <param name="limit">Maximum number of documents to return</param>
    /// <returns>Enumerable of documents in reverse insertion order</returns>
    public IEnumerable<Document> GetRecent(int? limit = null)
    {
        // Convert to list to be able to iterate in reverse
        var ids = _insertionOrder.ToArray();
        int count = 0;
        
        for (int i = ids.Length - 1; i >= 0; i--)
        {
            if (limit.HasValue && count >= limit.Value)
                break;

            if (_documents.TryGetValue(ids[i], out var document))
            {
                yield return document;
                count++;
            }
        }
    }

    /// <summary>
    /// Deletes a document by ID
    /// Note: In capped collections, documents are typically only removed automatically
    /// </summary>
    /// <param name="documentId">The ID of the document to delete</param>
    /// <returns>True if document was deleted, false if not found</returns>
    public bool Delete(string documentId)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            return false;

        if (_documents.TryRemove(documentId, out var removedDoc))
        {
            var docSize = EstimateDocumentSize(removedDoc);
            Interlocked.Decrement(ref _documentCount);
            Interlocked.Add(ref _totalSizeBytes, -docSize);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if a document exists
    /// </summary>
    /// <param name="documentId">The ID of the document to check</param>
    /// <returns>True if document exists, false otherwise</returns>
    public bool Exists(string documentId)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            return false;

        return _documents.ContainsKey(documentId);
    }

    /// <summary>
    /// Clears all documents from the collection
    /// </summary>
    public void Clear()
    {
        _documents.Clear();
        while (_insertionOrder.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _documentCount, 0);
        Interlocked.Exchange(ref _totalSizeBytes, 0);
    }

    /// <summary>
    /// Gets statistics about the capped collection
    /// </summary>
    /// <returns>Collection statistics</returns>
    public CappedCollectionStats GetStats()
    {
        return new CappedCollectionStats
        {
            Name = Name,
            DocumentCount = Count,
            TotalSizeBytes = TotalSizeBytes,
            MaxDocuments = Options.EnforceMaxDocuments ? Options.MaxDocuments : null,
            MaxSizeBytes = Options.EnforceMaxSize ? Options.MaxSizeBytes : null,
            CreatedAt = CreatedAt,
            IsCapped = true
        };
    }

    /// <summary>
    /// Trims the collection if it exceeds size or count limits
    /// </summary>
    private void TrimIfNeeded()
    {
        // Only one thread should trim at a time
        if (!Monitor.TryEnter(_trimLock))
            return;

        try
        {
            var removedDocs = new List<string>();
            long removedSize = 0;

            // Trim by document count
            if (Options.EnforceMaxDocuments)
            {
                while (Count > Options.MaxDocuments && _insertionOrder.TryDequeue(out var oldestId))
                {
                    if (_documents.TryRemove(oldestId, out var removedDoc))
                    {
                        removedDocs.Add(oldestId);
                        removedSize += EstimateDocumentSize(removedDoc);
                        Interlocked.Decrement(ref _documentCount);
                    }
                }
            }

            // Trim by size
            if (Options.EnforceMaxSize)
            {
                while (TotalSizeBytes > Options.MaxSizeBytes && _insertionOrder.TryDequeue(out var oldestId))
                {
                    if (_documents.TryRemove(oldestId, out var removedDoc))
                    {
                        removedDocs.Add(oldestId);
                        removedSize += EstimateDocumentSize(removedDoc);
                        Interlocked.Decrement(ref _documentCount);
                    }
                }
            }

            if (removedDocs.Count > 0)
            {
                Interlocked.Add(ref _totalSizeBytes, -removedSize);
                CollectionTrimmed?.Invoke(this, new CappedCollectionTrimmedEventArgs
                {
                    RemovedDocumentIds = removedDocs,
                    RemovedCount = removedDocs.Count,
                    RemovedSizeBytes = removedSize,
                    Timestamp = DateTime.UtcNow
                });
            }
        }
        finally
        {
            Monitor.Exit(_trimLock);
        }
    }

    /// <summary>
    /// Estimates the size of a document in bytes
    /// </summary>
    private long EstimateDocumentSize(Document document)
    {
        if (document?.Data == null)
            return 0;

        // Rough estimation: count characters in JSON-like representation
        long size = document.Id?.Length ?? 0;
        
        foreach (var kvp in document.Data)
        {
            size += (kvp.Key?.Length ?? 0) + EstimateValueSize(kvp.Value);
        }

        // Add overhead for metadata
        size += 100; // Version, timestamps, etc.

        return size;
    }

    private long EstimateValueSize(object? value)
    {
        if (value == null)
            return 4; // "null"

        return value switch
        {
            string s => s.Length,
            int => 4,
            long => 8,
            double => 8,
            decimal => 16,
            bool => 4,
            DateTime => 20,
            DateTimeOffset => 25,
            System.Text.Json.JsonElement json => json.ToString()?.Length ?? 0,
            Dictionary<string, object> dict => dict.Sum(kvp => kvp.Key.Length + EstimateValueSize(kvp.Value)),
            System.Collections.IEnumerable enumerable => enumerable.Cast<object>().Sum(EstimateValueSize),
            _ => value.ToString()?.Length ?? 8
        };
    }
}

/// <summary>
/// Statistics for a capped collection
/// </summary>
public class CappedCollectionStats
{
    /// <summary>
    /// Name of the collection
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Number of documents in the collection
    /// </summary>
    public long DocumentCount { get; set; }

    /// <summary>
    /// Total size of documents in bytes
    /// </summary>
    public long TotalSizeBytes { get; set; }

    /// <summary>
    /// Maximum number of documents allowed (null if not enforced)
    /// </summary>
    public long? MaxDocuments { get; set; }

    /// <summary>
    /// Maximum size in bytes allowed (null if not enforced)
    /// </summary>
    public long? MaxSizeBytes { get; set; }

    /// <summary>
    /// When the collection was created
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Whether this is a capped collection
    /// </summary>
    public bool IsCapped { get; set; }
}

/// <summary>
/// Event arguments for when a capped collection is trimmed
/// </summary>
public class CappedCollectionTrimmedEventArgs : EventArgs
{
    /// <summary>
    /// IDs of documents that were removed
    /// </summary>
    public IReadOnlyList<string> RemovedDocumentIds { get; set; } = new List<string>();

    /// <summary>
    /// Number of documents removed
    /// </summary>
    public int RemovedCount { get; set; }

    /// <summary>
    /// Size of removed documents in bytes
    /// </summary>
    public long RemovedSizeBytes { get; set; }

    /// <summary>
    /// When the trim occurred
    /// </summary>
    public DateTime Timestamp { get; set; }
}
