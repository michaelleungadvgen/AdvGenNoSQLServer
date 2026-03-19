// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Storage.Indexing;

/// <summary>
/// Specifies the type of partial/sparse index
/// </summary>
public enum PartialIndexType
{
    /// <summary>
    /// Standard index that includes all documents
    /// </summary>
    None,

    /// <summary>
    /// Sparse index - only includes documents that have the indexed field
    /// </summary>
    Sparse,

    /// <summary>
    /// Partial index - only includes documents matching a filter expression
    /// </summary>
    Partial
}

/// <summary>
/// Interface for indexes that support partial/sparse filtering
/// </summary>
public interface IPartialIndex
{
    /// <summary>
    /// Gets the type of partial index
    /// </summary>
    PartialIndexType PartialType { get; }

    /// <summary>
    /// Checks if a document should be included in this index
    /// </summary>
    /// <param name="document">The document to check</param>
    /// <returns>True if the document should be indexed, false otherwise</returns>
    bool ShouldIncludeDocument(Document document);

    /// <summary>
    /// Gets the field name being indexed
    /// </summary>
    string FieldName { get; }
}

/// <summary>
/// A sparse B-tree index that only includes documents with the indexed field
/// </summary>
/// <typeparam name="TKey">The type of the index key</typeparam>
public class SparseBTreeIndex<TKey> : BTreeIndex<TKey, string>, IPartialIndex where TKey : IComparable<TKey>
{
    /// <summary>
    /// Creates a new sparse B-tree index
    /// </summary>
    /// <param name="name">The index name</param>
    /// <param name="collectionName">The collection name</param>
    /// <param name="fieldName">The field being indexed</param>
    /// <param name="isUnique">Whether the index enforces uniqueness</param>
    /// <param name="minDegree">The B-tree minimum degree</param>
    public SparseBTreeIndex(
        string name,
        string collectionName,
        string fieldName,
        bool isUnique,
        int minDegree = 4) : base(name, collectionName, fieldName, isUnique, minDegree)
    {
    }

    /// <inheritdoc />
    public PartialIndexType PartialType => PartialIndexType.Sparse;

    /// <inheritdoc />
    public bool ShouldIncludeDocument(Document document)
    {
        return document.Data.ContainsKey(FieldName);
    }
}

/// <summary>
/// A partial B-tree index that only includes documents matching a filter expression
/// </summary>
/// <typeparam name="TKey">The type of the index key</typeparam>
public class PartialBTreeIndex<TKey> : BTreeIndex<TKey, string>, IPartialIndex where TKey : IComparable<TKey>
{
    private readonly Func<Document, bool> _filterExpression;

    /// <summary>
    /// Creates a new partial B-tree index
    /// </summary>
    /// <param name="name">The index name</param>
    /// <param name="collectionName">The collection name</param>
    /// <param name="fieldName">The field being indexed</param>
    /// <param name="isUnique">Whether the index enforces uniqueness</param>
    /// <param name="filterExpression">The filter expression to determine which documents to include</param>
    /// <param name="minDegree">The B-tree minimum degree</param>
    public PartialBTreeIndex(
        string name,
        string collectionName,
        string fieldName,
        bool isUnique,
        Func<Document, bool> filterExpression,
        int minDegree = 4) : base(name, collectionName, fieldName, isUnique, minDegree)
    {
        _filterExpression = filterExpression ?? throw new ArgumentNullException(nameof(filterExpression));
    }

    /// <inheritdoc />
    public PartialIndexType PartialType => PartialIndexType.Partial;

    /// <summary>
    /// Gets the filter expression used by this partial index
    /// </summary>
    public Func<Document, bool> FilterExpression => _filterExpression;

    /// <inheritdoc />
    public bool ShouldIncludeDocument(Document document)
    {
        // Must have the field AND match the filter expression
        if (!document.Data.ContainsKey(FieldName))
            return false;

        return _filterExpression(document);
    }
}
