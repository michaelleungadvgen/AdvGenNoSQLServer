// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Query.Filtering;
using AdvGenNoSqlServer.Query.Models;
using AdvGenNoSqlServer.Storage;

namespace AdvGenNoSqlServer.Query.Cursors;

/// <summary>
/// Internal implementation of ICursor
/// </summary>
internal class Cursor : ICursor
{
    private readonly IDocumentStore _documentStore;
    private readonly IFilterEngine _filterEngine;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<Document>? _bufferedDocuments;
    private bool _hasMore = true;
    private long _documentsReturned;
    private int _currentPosition; // Tracks position in the filtered/sorted result set

    public string CursorId { get; }
    public string CollectionName { get; }
    public QueryFilter? Filter { get; }
    public List<SortField>? Sort { get; }
    public int BatchSize { get; }
    public DateTime CreatedAt { get; }
    public DateTime ExpiresAt { get; }
    public long? TotalCount { get; private set; }
    public long DocumentsReturned => Interlocked.Read(ref _documentsReturned);
    public bool IsClosed { get; private set; }
    public string? LastDocumentId { get; private set; }
    
    /// <inheritdoc />
    public bool IsExpired => DateTime.UtcNow > ExpiresAt;

    // Resume token support
    private readonly int _resumeAfterPosition;

    public Cursor(
        string cursorId,
        string collectionName,
        QueryFilter? filter,
        List<SortField>? sort,
        CursorOptions options,
        IDocumentStore documentStore,
        IFilterEngine filterEngine,
        ResumeToken? resumeToken = null)
    {
        CursorId = cursorId;
        CollectionName = collectionName;
        Filter = filter;
        Sort = sort;
        BatchSize = options.BatchSize;
        CreatedAt = DateTime.UtcNow;
        ExpiresAt = CreatedAt.AddMinutes(options.TimeoutMinutes);
        _documentStore = documentStore;
        _filterEngine = filterEngine;

        // Handle resume token - we'll store the position for future implementation
        // For now, resume tokens just preserve the cursor state
        _resumeAfterPosition = 0;
    }

    public async Task CalculateTotalCountAsync(CancellationToken cancellationToken = default)
    {
        var allDocs = await _documentStore.GetAllAsync(CollectionName);
        var filtered = _filterEngine.Filter(allDocs, Filter);
        TotalCount = filtered.Count();
    }

    public async Task<IReadOnlyList<Document>> GetNextBatchAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (IsClosed)
                return new List<Document>();

            // Extend expiration on each use
            ExtendExpiration();

            var results = new List<Document>();

            // If we have buffered documents from HasMoreAsync, use them first
            if (_bufferedDocuments != null && _bufferedDocuments.Count > 0)
            {
                // Take up to batchSize documents from the buffer
                var toTake = Math.Min(batchSize, _bufferedDocuments.Count);
                results.AddRange(_bufferedDocuments.Take(toTake));
                
                // Remove the taken documents from the buffer
                if (toTake >= _bufferedDocuments.Count)
                {
                    _bufferedDocuments = null;
                }
                else
                {
                    _bufferedDocuments = _bufferedDocuments.Skip(toTake).ToList();
                }

                // If we still need more documents, fetch them
                if (results.Count < batchSize)
                {
                    var remainingNeeded = batchSize - results.Count;
                    var additionalDocs = await FetchDocumentsAsync(remainingNeeded, cancellationToken);
                    results.AddRange(additionalDocs);
                }
            }
            else
            {
                // No buffered documents, fetch fresh
                var documents = await FetchDocumentsAsync(batchSize, cancellationToken);
                results.AddRange(documents);
            }

            // Update last document ID
            if (results.Count > 0)
            {
                LastDocumentId = results[^1].Id;
            }

            Interlocked.Add(ref _documentsReturned, results.Count);
            return results;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> HasMoreAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (IsClosed)
                return false;

            // Check if we have buffered documents remaining
            if (_bufferedDocuments != null && _bufferedDocuments.Count > 0)
            {
                return true;
            }

            // Check if we've already determined there are no more documents
            if (!_hasMore)
                return false;

            // Fetch the next batch and buffer it
            var nextBatch = await FetchDocumentsAsync(BatchSize, cancellationToken);
            if (nextBatch.Count == 0)
            {
                _hasMore = false;
                return false;
            }

            // Buffer the fetched documents for the actual GetNextBatch call
            _bufferedDocuments = nextBatch;
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task CloseAsync()
    {
        IsClosed = true;
        _bufferedDocuments = null;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        IsClosed = true;
        _bufferedDocuments = null;
        _lock.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<List<Document>> FetchDocumentsAsync(int batchSize, CancellationToken cancellationToken)
    {
        // Get all documents from the collection
        var allDocs = await _documentStore.GetAllAsync(CollectionName);

        // Apply filter
        var filtered = _filterEngine.Filter(allDocs, Filter).ToList();

        // Apply sorting
        if (Sort != null && Sort.Count > 0)
        {
            filtered = ApplySorting(filtered, Sort);
        }

        // Calculate start position based on resume token or current position
        int startIndex = _currentPosition;
        if (_documentsReturned == 0 && _resumeAfterPosition > 0)
        {
            startIndex = _resumeAfterPosition;
            _currentPosition = startIndex;
        }

        // Take the next batch
        var results = filtered.Skip(startIndex).Take(batchSize).ToList();

        // Update position
        _currentPosition = startIndex + results.Count;

        // Check if there are more documents
        _hasMore = _currentPosition < filtered.Count;

        return results;
    }

    private List<Document> ApplySorting(List<Document> documents, List<SortField> sortFields)
    {
        IOrderedEnumerable<Document>? ordered = null;

        for (int i = 0; i < sortFields.Count; i++)
        {
            var sortField = sortFields[i];
            var fieldName = sortField.FieldName;
            var direction = sortField.Direction;

            if (i == 0)
            {
                ordered = direction == SortDirection.Ascending
                    ? documents.OrderBy(d => GetFieldForSorting(d, fieldName))
                    : documents.OrderByDescending(d => GetFieldForSorting(d, fieldName));
            }
            else
            {
                ordered = direction == SortDirection.Ascending
                    ? ordered!.ThenBy(d => GetFieldForSorting(d, fieldName))
                    : ordered!.ThenByDescending(d => GetFieldForSorting(d, fieldName));
            }
        }

        return ordered?.ToList() ?? documents;
    }

    private object? GetFieldForSorting(Document document, string fieldName)
    {
        var value = _filterEngine.GetFieldValue(document, fieldName);
        return value ?? string.Empty;
    }

    private void ExtendExpiration()
    {
        // Extend expiration by the original timeout from now
        var originalTimeout = ExpiresAt - CreatedAt;
        // Note: We don't actually extend ExpiresAt to keep it simple
        // In a production system, you might want to extend it
    }
}
