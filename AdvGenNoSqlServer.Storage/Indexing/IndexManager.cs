// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Storage.Indexing;

/// <summary>
/// Manages indexes for document collections
/// Provides centralized index creation, deletion, and query capabilities
/// </summary>
public class IndexManager
{
    private readonly ConcurrentDictionary<string, CollectionIndexes> _collectionIndexes = new();
    private readonly object _lock = new();

    /// <summary>
    /// Creates a new B-tree index for a collection field
    /// </summary>
    /// <typeparam name="TKey">The type of the index key</typeparam>
    /// <param name="collectionName">The collection name</param>
    /// <param name="fieldName">The field to index</param>
    /// <param name="isUnique">Whether the index enforces uniqueness</param>
    /// <param name="keySelector">Function to extract the key from a document</param>
    /// <param name="minDegree">The B-tree minimum degree</param>
    /// <returns>The created index</returns>
    public IBTreeIndex<TKey, string> CreateIndex<TKey>(
        string collectionName,
        string fieldName,
        bool isUnique,
        Func<Document, TKey> keySelector,
        int minDegree = 4) where TKey : IComparable<TKey>
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionName, nameof(collectionName));
        ArgumentException.ThrowIfNullOrEmpty(fieldName, nameof(fieldName));
        ArgumentNullException.ThrowIfNull(keySelector, nameof(keySelector));

        lock (_lock)
        {
            var collectionIndex = _collectionIndexes.GetOrAdd(collectionName, _ => new CollectionIndexes(collectionName));
            
            string indexName = $"{collectionName}_{fieldName}_idx";
            
            if (collectionIndex.HasIndex(fieldName))
            {
                throw new InvalidOperationException($"Index already exists for field '{fieldName}' in collection '{collectionName}'");
            }

            var index = new BTreeIndex<TKey, string>(indexName, collectionName, fieldName, isUnique, minDegree);
            collectionIndex.AddIndex(fieldName, index, keySelector);
            
            return index;
        }
    }

    /// <summary>
    /// Drops an index from a collection
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="fieldName">The field whose index to drop</param>
    /// <returns>True if index was dropped, false if not found</returns>
    public bool DropIndex(string collectionName, string fieldName)
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionName, nameof(collectionName));
        ArgumentException.ThrowIfNullOrEmpty(fieldName, nameof(fieldName));

        lock (_lock)
        {
            if (!_collectionIndexes.TryGetValue(collectionName, out var collectionIndex))
            {
                return false;
            }

            return collectionIndex.RemoveIndex(fieldName);
        }
    }

    /// <summary>
    /// Gets an index for a collection field
    /// </summary>
    /// <typeparam name="TKey">The key type</typeparam>
    /// <param name="collectionName">The collection name</param>
    /// <param name="fieldName">The field name</param>
    /// <returns>The index if found, null otherwise</returns>
    public IBTreeIndex<TKey, string>? GetIndex<TKey>(string collectionName, string fieldName) where TKey : IComparable<TKey>
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionName, nameof(collectionName));
        ArgumentException.ThrowIfNullOrEmpty(fieldName, nameof(fieldName));

        if (_collectionIndexes.TryGetValue(collectionName, out var collectionIndex))
        {
            return collectionIndex.GetIndex<TKey>(fieldName);
        }

        return null;
    }

    /// <summary>
    /// Checks if an index exists for a field
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="fieldName">The field name</param>
    /// <returns>True if index exists, false otherwise</returns>
    public bool HasIndex(string collectionName, string fieldName)
    {
        if (_collectionIndexes.TryGetValue(collectionName, out var collectionIndex))
        {
            return collectionIndex.HasIndex(fieldName);
        }
        return false;
    }

    /// <summary>
    /// Gets all indexed fields for a collection
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <returns>List of indexed field names</returns>
    public IEnumerable<string> GetIndexedFields(string collectionName)
    {
        if (_collectionIndexes.TryGetValue(collectionName, out var collectionIndex))
        {
            return collectionIndex.GetIndexedFields();
        }
        return Enumerable.Empty<string>();
    }

    /// <summary>
    /// Indexes a document in all indexes for a collection
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="document">The document to index</param>
    public void IndexDocument(string collectionName, Document document)
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionName, nameof(collectionName));
        ArgumentNullException.ThrowIfNull(document, nameof(document));

        if (_collectionIndexes.TryGetValue(collectionName, out var collectionIndex))
        {
            collectionIndex.IndexDocument(document);
        }
    }

    /// <summary>
    /// Removes a document from all indexes for a collection
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="document">The document to remove</param>
    public void RemoveDocument(string collectionName, Document document)
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionName, nameof(collectionName));
        ArgumentNullException.ThrowIfNull(document, nameof(document));

        if (_collectionIndexes.TryGetValue(collectionName, out var collectionIndex))
        {
            collectionIndex.RemoveDocument(document);
        }
    }

    /// <summary>
    /// Updates a document in all indexes for a collection
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="oldDocument">The old document state</param>
    /// <param name="newDocument">The new document state</param>
    public void UpdateDocument(string collectionName, Document oldDocument, Document newDocument)
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionName, nameof(collectionName));
        ArgumentNullException.ThrowIfNull(oldDocument, nameof(oldDocument));
        ArgumentNullException.ThrowIfNull(newDocument, nameof(newDocument));

        if (_collectionIndexes.TryGetValue(collectionName, out var collectionIndex))
        {
            collectionIndex.UpdateDocument(oldDocument, newDocument);
        }
    }

    /// <summary>
    /// Drops all indexes for a collection
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    public void DropCollectionIndexes(string collectionName)
    {
        _collectionIndexes.TryRemove(collectionName, out _);
    }

    /// <summary>
    /// Clears all indexes
    /// </summary>
    public void ClearAllIndexes()
    {
        _collectionIndexes.Clear();
    }

    /// <summary>
    /// Gets index statistics for a collection
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <returns>Collection of index statistics</returns>
    public IEnumerable<IndexStats> GetIndexStats(string collectionName)
    {
        if (_collectionIndexes.TryGetValue(collectionName, out var collectionIndex))
        {
            return collectionIndex.GetStats();
        }
        return Enumerable.Empty<IndexStats>();
    }

    /// <summary>
    /// Internal class to manage indexes for a single collection
    /// </summary>
    private class CollectionIndexes
    {
        private readonly ConcurrentDictionary<string, IIndexWrapper> _indexes = new();
        
        public string CollectionName { get; }

        public CollectionIndexes(string collectionName)
        {
            CollectionName = collectionName;
        }

        public void AddIndex<TKey>(string fieldName, IBTreeIndex<TKey, string> index, Func<Document, TKey> keySelector) where TKey : IComparable<TKey>
        {
            _indexes[fieldName] = new IndexWrapper<TKey>(index, keySelector);
        }

        public bool RemoveIndex(string fieldName)
        {
            return _indexes.TryRemove(fieldName, out _);
        }

        public bool HasIndex(string fieldName)
        {
            return _indexes.ContainsKey(fieldName);
        }

        public IBTreeIndex<TKey, string>? GetIndex<TKey>(string fieldName) where TKey : IComparable<TKey>
        {
            if (_indexes.TryGetValue(fieldName, out var wrapper) && wrapper is IndexWrapper<TKey> typedWrapper)
            {
                return typedWrapper.Index;
            }
            return null;
        }

        public IEnumerable<string> GetIndexedFields()
        {
            return _indexes.Keys;
        }

        public void IndexDocument(Document document)
        {
            foreach (var wrapper in _indexes.Values)
            {
                wrapper.IndexDocument(document);
            }
        }

        public void RemoveDocument(Document document)
        {
            foreach (var wrapper in _indexes.Values)
            {
                wrapper.RemoveDocument(document);
            }
        }

        public void UpdateDocument(Document oldDocument, Document newDocument)
        {
            foreach (var wrapper in _indexes.Values)
            {
                wrapper.UpdateDocument(oldDocument, newDocument);
            }
        }

        public IEnumerable<IndexStats> GetStats()
        {
            return _indexes.Select(kvp => new IndexStats
            {
                FieldName = kvp.Key,
                EntryCount = kvp.Value.Count,
                Height = kvp.Value.Height,
                IndexType = kvp.Value.IsUnique ? "Unique B-Tree" : "B-Tree"
            });
        }
    }

    /// <summary>
    /// Interface to allow storing different generic index types in the same dictionary
    /// </summary>
    private interface IIndexWrapper
    {
        int Count { get; }
        int Height { get; }
        bool IsUnique { get; }
        void IndexDocument(Document document);
        void RemoveDocument(Document document);
        void UpdateDocument(Document oldDocument, Document newDocument);
    }

    /// <summary>
    /// Wrapper for typed indexes
    /// </summary>
    private class IndexWrapper<TKey> : IIndexWrapper where TKey : IComparable<TKey>
    {
        private readonly Func<Document, TKey> _keySelector;

        public IBTreeIndex<TKey, string> Index { get; }
        public int Count => Index.Count;
        public int Height => Index.Height;
        public bool IsUnique => Index.IsUnique;

        public IndexWrapper(IBTreeIndex<TKey, string> index, Func<Document, TKey> keySelector)
        {
            Index = index;
            _keySelector = keySelector;
        }

        public void IndexDocument(Document document)
        {
            try
            {
                var key = _keySelector(document);
                if (key != null)
                {
                    Index.Insert(key, document.Id);
                }
            }
            catch
            {
                // Ignore documents that don't have the indexed field
            }
        }

        public void RemoveDocument(Document document)
        {
            try
            {
                var key = _keySelector(document);
                if (key != null)
                {
                    Index.Delete(key, document.Id);
                }
            }
            catch
            {
                // Ignore errors during removal
            }
        }

        public void UpdateDocument(Document oldDocument, Document newDocument)
        {
            RemoveDocument(oldDocument);
            IndexDocument(newDocument);
        }
    }
}

/// <summary>
/// Statistics for an index
/// </summary>
public class IndexStats
{
    /// <summary>
    /// The field name being indexed
    /// </summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// The type of index
    /// </summary>
    public string IndexType { get; set; } = string.Empty;

    /// <summary>
    /// Number of entries in the index
    /// </summary>
    public int EntryCount { get; set; }

    /// <summary>
    /// Height of the B-tree
    /// </summary>
    public int Height { get; set; }
}
