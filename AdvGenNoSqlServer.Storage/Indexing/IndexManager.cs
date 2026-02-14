// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Storage.Indexing;

/// <summary>
/// Manages indexes for document collections
/// Provides centralized index creation, deletion, and query capabilities
/// Supports both single-field and compound (multi-field) indexes
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
    /// Creates a new compound (multi-field) B-tree index for a collection
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="fieldNames">The fields to index (in order of significance)</param>
    /// <param name="isUnique">Whether the index enforces uniqueness</param>
    /// <param name="keySelector">Function to extract compound key values from a document</param>
    /// <param name="minDegree">The B-tree minimum degree</param>
    /// <returns>The created compound index</returns>
    public IBTreeIndex<CompoundIndexKey, string> CreateCompoundIndex(
        string collectionName,
        string[] fieldNames,
        bool isUnique,
        Func<Document, object?[]> keySelector,
        int minDegree = 4)
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionName, nameof(collectionName));
        ArgumentNullException.ThrowIfNull(fieldNames, nameof(fieldNames));
        if (fieldNames.Length < 2)
            throw new ArgumentException("Compound index must have at least 2 fields", nameof(fieldNames));
        if (fieldNames.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("All field names must be non-empty", nameof(fieldNames));
        ArgumentNullException.ThrowIfNull(keySelector, nameof(keySelector));

        lock (_lock)
        {
            var collectionIndex = _collectionIndexes.GetOrAdd(collectionName, _ => new CollectionIndexes(collectionName));
            
            string indexName = $"{collectionName}_{string.Join("_", fieldNames)}_idx";
            string indexKey = string.Join("+", fieldNames);
            
            if (collectionIndex.HasIndex(indexKey))
            {
                throw new InvalidOperationException($"Compound index already exists for fields '{indexKey}' in collection '{collectionName}'");
            }

            var index = new BTreeIndex<CompoundIndexKey, string>(indexName, collectionName, indexKey, isUnique, minDegree);
            collectionIndex.AddCompoundIndex(indexKey, index, keySelector, fieldNames);
            
            return index;
        }
    }

    /// <summary>
    /// Creates a compound index with two fields (convenience method)
    /// </summary>
    /// <typeparam name="T1">Type of the first field</typeparam>
    /// <typeparam name="T2">Type of the second field</typeparam>
    /// <param name="collectionName">The collection name</param>
    /// <param name="field1">The first field name</param>
    /// <param name="field2">The second field name</param>
    /// <param name="isUnique">Whether the index enforces uniqueness</param>
    /// <param name="selector1">Function to extract the first field value</param>
    /// <param name="selector2">Function to extract the second field value</param>
    /// <param name="minDegree">The B-tree minimum degree</param>
    /// <returns>The created compound index</returns>
    public IBTreeIndex<CompoundIndexKey, string> CreateCompoundIndex<T1, T2>(
        string collectionName,
        string field1,
        string field2,
        bool isUnique,
        Func<Document, T1> selector1,
        Func<Document, T2> selector2,
        int minDegree = 4) where T1 : IComparable<T1> where T2 : IComparable<T2>
    {
        return CreateCompoundIndex(
            collectionName,
            new[] { field1, field2 },
            isUnique,
            doc => new object?[] { selector1(doc), selector2(doc) },
            minDegree);
    }

    /// <summary>
    /// Creates a compound index with three fields (convenience method)
    /// </summary>
    /// <typeparam name="T1">Type of the first field</typeparam>
    /// <typeparam name="T2">Type of the second field</typeparam>
    /// <typeparam name="T3">Type of the third field</typeparam>
    /// <param name="collectionName">The collection name</param>
    /// <param name="field1">The first field name</param>
    /// <param name="field2">The second field name</param>
    /// <param name="field3">The third field name</param>
    /// <param name="isUnique">Whether the index enforces uniqueness</param>
    /// <param name="selector1">Function to extract the first field value</param>
    /// <param name="selector2">Function to extract the second field value</param>
    /// <param name="selector3">Function to extract the third field value</param>
    /// <param name="minDegree">The B-tree minimum degree</param>
    /// <returns>The created compound index</returns>
    public IBTreeIndex<CompoundIndexKey, string> CreateCompoundIndex<T1, T2, T3>(
        string collectionName,
        string field1,
        string field2,
        string field3,
        bool isUnique,
        Func<Document, T1> selector1,
        Func<Document, T2> selector2,
        Func<Document, T3> selector3,
        int minDegree = 4) 
        where T1 : IComparable<T1> 
        where T2 : IComparable<T2>
        where T3 : IComparable<T3>
    {
        return CreateCompoundIndex(
            collectionName,
            new[] { field1, field2, field3 },
            isUnique,
            doc => new object?[] { selector1(doc), selector2(doc), selector3(doc) },
            minDegree);
    }

    /// <summary>
    /// Gets a compound index for a collection
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="fieldNames">The field names that make up the compound index</param>
    /// <returns>The compound index if found, null otherwise</returns>
    public IBTreeIndex<CompoundIndexKey, string>? GetCompoundIndex(string collectionName, params string[] fieldNames)
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionName, nameof(collectionName));
        ArgumentNullException.ThrowIfNull(fieldNames, nameof(fieldNames));
        
        string indexKey = string.Join("+", fieldNames);
        
        if (_collectionIndexes.TryGetValue(collectionName, out var collectionIndex))
        {
            return collectionIndex.GetCompoundIndex(indexKey);
        }

        return null;
    }

    /// <summary>
    /// Checks if a compound index exists for the specified fields
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="fieldNames">The field names that make up the compound index</param>
    /// <returns>True if compound index exists, false otherwise</returns>
    public bool HasCompoundIndex(string collectionName, params string[] fieldNames)
    {
        return GetCompoundIndex(collectionName, fieldNames) != null;
    }

    /// <summary>
    /// Creates a sparse index that only includes documents with the indexed field
    /// </summary>
    /// <typeparam name="TKey">The type of the index key</typeparam>
    /// <param name="collectionName">The collection name</param>
    /// <param name="fieldName">The field to index</param>
    /// <param name="isUnique">Whether the index enforces uniqueness</param>
    /// <param name="keySelector">Function to extract the key from a document</param>
    /// <param name="minDegree">The B-tree minimum degree</param>
    /// <returns>The created sparse index</returns>
    public IBTreeIndex<TKey, string> CreateSparseIndex<TKey>(
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
            
            string indexName = $"{collectionName}_{fieldName}_sparse_idx";
            
            if (collectionIndex.HasIndex(fieldName))
            {
                throw new InvalidOperationException($"Index already exists for field '{fieldName}' in collection '{collectionName}'");
            }

            var index = new SparseBTreeIndex<TKey>(indexName, collectionName, fieldName, isUnique, minDegree);
            
            collectionIndex.AddSparseIndex(fieldName, index, keySelector);
            
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

        public void AddCompoundIndex(string indexKey, IBTreeIndex<CompoundIndexKey, string> index, Func<Document, object?[]> keySelector, string[] fieldNames)
        {
            _indexes[indexKey] = new CompoundIndexWrapper(index, keySelector, fieldNames);
        }

        public void AddSparseIndex<TKey>(string fieldName, SparseBTreeIndex<TKey> index, Func<Document, TKey> keySelector) where TKey : IComparable<TKey>
        {
            _indexes[fieldName] = new SparseIndexWrapper<TKey>(index, keySelector);
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

        public IBTreeIndex<CompoundIndexKey, string>? GetCompoundIndex(string indexKey)
        {
            if (_indexes.TryGetValue(indexKey, out var wrapper) && wrapper is CompoundIndexWrapper compoundWrapper)
            {
                return compoundWrapper.Index;
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
            return _indexes.Select(kvp =>
            {
                string baseType = kvp.Value.IsUnique ? "Unique " : "";
                
                string indexType = kvp.Value switch
                {
                    CompoundIndexWrapper => baseType + "Compound B-Tree",
                    _ when kvp.Value.GetType().Name.StartsWith("SparseIndexWrapper") => baseType + "Sparse B-Tree",
                    _ => baseType + "B-Tree"
                };
                
                return new IndexStats
                {
                    FieldName = kvp.Key,
                    EntryCount = kvp.Value.Count,
                    Height = kvp.Value.Height,
                    IndexType = indexType
                };
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

    /// <summary>
    /// Wrapper for compound (multi-field) indexes
    /// </summary>
    private class CompoundIndexWrapper : IIndexWrapper
    {
        private readonly Func<Document, object?[]> _keySelector;
        private readonly string[] _fieldNames;

        public IBTreeIndex<CompoundIndexKey, string> Index { get; }
        public int Count => Index.Count;
        public int Height => Index.Height;
        public bool IsUnique => Index.IsUnique;

        public CompoundIndexWrapper(IBTreeIndex<CompoundIndexKey, string> index, Func<Document, object?[]> keySelector, string[] fieldNames)
        {
            Index = index;
            _keySelector = keySelector;
            _fieldNames = fieldNames;
        }

        public void IndexDocument(Document document)
        {
            try
            {
                var values = _keySelector(document);
                if (values != null && values.Length > 0)
                {
                    var key = new CompoundIndexKey(values);
                    Index.Insert(key, document.Id);
                }
            }
            catch (DuplicateKeyException)
            {
                throw; // Re-throw duplicate key exceptions for unique compound indexes
            }
            catch
            {
                // Ignore documents that don't have all indexed fields
            }
        }

        public void RemoveDocument(Document document)
        {
            try
            {
                var values = _keySelector(document);
                if (values != null && values.Length > 0)
                {
                    var key = new CompoundIndexKey(values);
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

    /// <summary>
    /// Wrapper for sparse indexes - only indexes documents that have the field
    /// </summary>
    private class SparseIndexWrapper<TKey> : IIndexWrapper where TKey : IComparable<TKey>
    {
        private readonly Func<Document, TKey> _keySelector;

        public SparseBTreeIndex<TKey> Index { get; }
        public int Count => Index.Count;
        public int Height => Index.Height;
        public bool IsUnique => Index.IsUnique;

        public SparseIndexWrapper(SparseBTreeIndex<TKey> index, Func<Document, TKey> keySelector)
        {
            Index = index;
            _keySelector = keySelector;
        }

        public void IndexDocument(Document document)
        {
            try
            {
                // For sparse indexes, only index if the document has the field
                if (!document.Data.ContainsKey(Index.FieldName))
                    return;

                var key = _keySelector(document);
                if (key != null)
                {
                    Index.Insert(key, document.Id);
                }
            }
            catch (DuplicateKeyException)
            {
                throw; // Re-throw duplicate key exceptions
            }
            catch
            {
                // Ignore other errors
            }
        }

        public void RemoveDocument(Document document)
        {
            try
            {
                // Only try to remove if document has the field
                if (!document.Data.ContainsKey(Index.FieldName))
                    return;

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
