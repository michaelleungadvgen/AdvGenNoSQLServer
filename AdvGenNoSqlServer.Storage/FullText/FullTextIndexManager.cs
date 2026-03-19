// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Storage.FullText;

/// <summary>
/// Manages full-text indexes for document collections
/// Provides centralized index creation, deletion, and search capabilities
/// </summary>
public class FullTextIndexManager
{
    private readonly ConcurrentDictionary<string, CollectionFullTextIndexes> _collectionIndexes;
    private readonly object _lock = new();

    /// <summary>
    /// Creates a new FullTextIndexManager
    /// </summary>
    public FullTextIndexManager()
    {
        _collectionIndexes = new ConcurrentDictionary<string, CollectionFullTextIndexes>();
    }

    /// <summary>
    /// Creates a new full-text index for a collection field
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="fieldName">The field to index</param>
    /// <param name="analyzerType">The type of analyzer to use</param>
    /// <returns>The created index</returns>
    public IFullTextIndex CreateIndex(
        string collectionName,
        string fieldName,
        AnalyzerType analyzerType = AnalyzerType.Standard)
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionName, nameof(collectionName));
        ArgumentException.ThrowIfNullOrEmpty(fieldName, nameof(fieldName));

        lock (_lock)
        {
            var collectionIndex = _collectionIndexes.GetOrAdd(collectionName, _ => new CollectionFullTextIndexes(collectionName));

            if (collectionIndex.HasIndex(fieldName))
            {
                throw new InvalidOperationException($"Full-text index already exists for field '{fieldName}' in collection '{collectionName}'");
            }

            var analyzer = TextAnalyzerFactory.Create(analyzerType);
            var index = new FullTextIndex(collectionName, fieldName, analyzer);
            collectionIndex.AddIndex(fieldName, index);

            return index;
        }
    }

    /// <summary>
    /// Creates a new full-text index with a custom analyzer
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="fieldName">The field to index</param>
    /// <param name="analyzer">The custom analyzer</param>
    /// <returns>The created index</returns>
    public IFullTextIndex CreateIndex(
        string collectionName,
        string fieldName,
        ITextAnalyzer analyzer)
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionName, nameof(collectionName));
        ArgumentException.ThrowIfNullOrEmpty(fieldName, nameof(fieldName));
        ArgumentNullException.ThrowIfNull(analyzer, nameof(analyzer));

        lock (_lock)
        {
            var collectionIndex = _collectionIndexes.GetOrAdd(collectionName, _ => new CollectionFullTextIndexes(collectionName));

            if (collectionIndex.HasIndex(fieldName))
            {
                throw new InvalidOperationException($"Full-text index already exists for field '{fieldName}' in collection '{collectionName}'");
            }

            var index = new FullTextIndex(collectionName, fieldName, analyzer);
            collectionIndex.AddIndex(fieldName, index);

            return index;
        }
    }

    /// <summary>
    /// Gets a full-text index by collection and field name
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="fieldName">The field name</param>
    /// <returns>The index, or null if not found</returns>
    public IFullTextIndex? GetIndex(string collectionName, string fieldName)
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionName, nameof(collectionName));
        ArgumentException.ThrowIfNullOrEmpty(fieldName, nameof(fieldName));

        if (_collectionIndexes.TryGetValue(collectionName, out var collectionIndex))
        {
            return collectionIndex.GetIndex(fieldName);
        }

        return null;
    }

    /// <summary>
    /// Checks if a full-text index exists
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="fieldName">The field name</param>
    /// <returns>True if index exists, false otherwise</returns>
    public bool HasIndex(string collectionName, string fieldName)
    {
        return GetIndex(collectionName, fieldName) != null;
    }

    /// <summary>
    /// Drops a full-text index
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="fieldName">The field name</param>
    /// <returns>True if index was found and removed, false otherwise</returns>
    public bool DropIndex(string collectionName, string fieldName)
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionName, nameof(collectionName));
        ArgumentException.ThrowIfNullOrEmpty(fieldName, nameof(fieldName));

        lock (_lock)
        {
            if (_collectionIndexes.TryGetValue(collectionName, out var collectionIndex))
            {
                bool removed = collectionIndex.RemoveIndex(fieldName);
                
                // Clean up empty collection entry
                if (collectionIndex.IndexCount == 0)
                {
                    _collectionIndexes.TryRemove(collectionName, out _);
                }

                return removed;
            }

            return false;
        }
    }

    /// <summary>
    /// Drops all full-text indexes for a collection
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <returns>True if any indexes were removed, false otherwise</returns>
    public bool DropCollectionIndexes(string collectionName)
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionName, nameof(collectionName));

        lock (_lock)
        {
            return _collectionIndexes.TryRemove(collectionName, out _);
        }
    }

    /// <summary>
    /// Indexes a document in all applicable full-text indexes for a collection
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="document">The document to index</param>
    public void IndexDocument(string collectionName, Document document)
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionName, nameof(collectionName));
        ArgumentNullException.ThrowIfNull(document, nameof(document));

        if (_collectionIndexes.TryGetValue(collectionName, out var collectionIndex))
        {
            foreach (var (fieldName, index) in collectionIndex.GetAllIndexes())
            {
                string? text = ExtractFieldValue(document, fieldName);
                if (text != null)
                {
                    index.IndexDocument(document.Id, text);
                }
            }
        }
    }

    /// <summary>
    /// Removes a document from all full-text indexes for a collection
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="documentId">The document ID</param>
    public void RemoveDocument(string collectionName, string documentId)
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionName, nameof(collectionName));
        ArgumentException.ThrowIfNullOrEmpty(documentId, nameof(documentId));

        if (_collectionIndexes.TryGetValue(collectionName, out var collectionIndex))
        {
            foreach (var (_, index) in collectionIndex.GetAllIndexes())
            {
                index.RemoveDocument(documentId);
            }
        }
    }

    /// <summary>
    /// Updates a document in all full-text indexes for a collection
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="document">The updated document</param>
    public void UpdateDocument(string collectionName, Document document)
    {
        // Full-text index update is equivalent to re-indexing
        IndexDocument(collectionName, document);
    }

    /// <summary>
    /// Searches all full-text indexes in a collection
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="query">The search query</param>
    /// <param name="options">Search options</param>
    /// <returns>Combined search results from all indexes</returns>
    public FullTextSearchResult Search(string collectionName, string query, FullTextSearchOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionName, nameof(collectionName));
        ArgumentException.ThrowIfNullOrEmpty(query, nameof(query));

        var opts = options ?? FullTextSearchOptions.Default;
        
        // If a specific field is specified, search only that field
        if (!string.IsNullOrEmpty(opts.SearchField))
        {
            var index = GetIndex(collectionName, opts.SearchField);
            if (index != null)
            {
                return index.Search(query, opts);
            }
            return FullTextSearchResult.Empty(query);
        }

        // Otherwise search all fields and combine results
        if (_collectionIndexes.TryGetValue(collectionName, out var collectionIndex))
        {
            var allResults = new Dictionary<string, SearchResult>();
            int totalMatches = 0;
            double totalTime = 0;

            foreach (var (fieldName, index) in collectionIndex.GetAllIndexes())
            {
                var result = index.Search(query, opts);
                totalMatches += result.TotalMatches;
                totalTime += result.ExecutionTimeMs;

                foreach (var docResult in result.Results)
                {
                    if (allResults.TryGetValue(docResult.DocumentId, out var existing))
                    {
                        // Merge results for same document across multiple fields
                        var mergedScore = existing.Score + docResult.Score;
                        var mergedTermFreqs = new Dictionary<string, int>(existing.TermFrequencies);
                        foreach (var (term, freq) in docResult.TermFrequencies)
                        {
                            mergedTermFreqs[term] = mergedTermFreqs.GetValueOrDefault(term) + freq;
                        }
                        
                        allResults[docResult.DocumentId] = new SearchResult(
                            docResult.DocumentId,
                            mergedScore,
                            existing.FieldName + "," + docResult.FieldName,
                            existing.Highlights.Concat(docResult.Highlights).ToList(),
                            mergedTermFreqs
                        );
                    }
                    else
                    {
                        allResults[docResult.DocumentId] = docResult;
                    }
                }
            }

            var sortedResults = allResults.Values
                .OrderByDescending(r => r.Score)
                .Take(opts.MaxResults)
                .ToList();

            return new FullTextSearchResult(sortedResults, totalMatches, query, totalTime);
        }

        return FullTextSearchResult.Empty(query);
    }

    /// <summary>
    /// Gets all full-text indexes for a collection
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <returns>List of index names (field names)</returns>
    public IReadOnlyList<string> GetIndexedFields(string collectionName)
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionName, nameof(collectionName));

        if (_collectionIndexes.TryGetValue(collectionName, out var collectionIndex))
        {
            return collectionIndex.GetAllFields();
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// Gets statistics for all indexes in a collection
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <returns>Statistics for each index</returns>
    public IReadOnlyList<FullTextIndexStats> GetCollectionStats(string collectionName)
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionName, nameof(collectionName));

        if (_collectionIndexes.TryGetValue(collectionName, out var collectionIndex))
        {
            return collectionIndex.GetAllIndexes()
                .Select(kvp => kvp.Value.GetStats())
                .ToList();
        }

        return Array.Empty<FullTextIndexStats>();
    }

    /// <summary>
    /// Clears all full-text indexes
    /// </summary>
    public void ClearAllIndexes()
    {
        lock (_lock)
        {
            foreach (var (_, collectionIndex) in _collectionIndexes)
            {
                foreach (var (_, index) in collectionIndex.GetAllIndexes())
                {
                    index.Clear();
                }
            }
            _collectionIndexes.Clear();
        }
    }

    private static string? ExtractFieldValue(Document document, string fieldName)
    {
        // Handle nested fields with dot notation
        var parts = fieldName.Split('.');
        var current = document.Data;

        foreach (var part in parts)
        {
            if (current == null)
                return null;

            if (current.TryGetValue(part, out var value))
            {
                if (value is Dictionary<string, object?> dict)
                {
                    current = dict;
                }
                else if (value is string str)
                {
                    return str;
                }
                else if (value != null)
                {
                    return value.ToString();
                }
                else
                {
                    return null;
                }
            }
            else
            {
                return null;
            }
        }

        // If we ended on a dictionary, it wasn't a text field
        return current?.ToString();
    }

    // Internal helper class
    private class CollectionFullTextIndexes
    {
        private readonly ConcurrentDictionary<string, IFullTextIndex> _indexes;

        public string CollectionName { get; }
        public int IndexCount => _indexes.Count;

        public CollectionFullTextIndexes(string collectionName)
        {
            CollectionName = collectionName;
            _indexes = new ConcurrentDictionary<string, IFullTextIndex>();
        }

        public void AddIndex(string fieldName, IFullTextIndex index)
        {
            _indexes[fieldName] = index;
        }

        public bool HasIndex(string fieldName)
        {
            return _indexes.ContainsKey(fieldName);
        }

        public IFullTextIndex? GetIndex(string fieldName)
        {
            _indexes.TryGetValue(fieldName, out var index);
            return index;
        }

        public bool RemoveIndex(string fieldName)
        {
            return _indexes.TryRemove(fieldName, out _);
        }

        public IEnumerable<KeyValuePair<string, IFullTextIndex>> GetAllIndexes()
        {
            return _indexes;
        }

        public IReadOnlyList<string> GetAllFields()
        {
            return _indexes.Keys.ToList();
        }
    }
}
