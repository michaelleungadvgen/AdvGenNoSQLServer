// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Storage.FullText;

/// <summary>
/// Defines the contract for a full-text index
/// Supports adding, removing, and searching documents by text content
/// </summary>
public interface IFullTextIndex
{
    /// <summary>
    /// Gets the name of the index
    /// </summary>
    string IndexName { get; }

    /// <summary>
    /// Gets the collection name being indexed
    /// </summary>
    string CollectionName { get; }

    /// <summary>
    /// Gets the field name being indexed
    /// </summary>
    string FieldName { get; }

    /// <summary>
    /// Gets the analyzer used for text processing
    /// </summary>
    ITextAnalyzer Analyzer { get; }

    /// <summary>
    /// Gets the number of documents in the index
    /// </summary>
    int DocumentCount { get; }

    /// <summary>
    /// Gets the number of unique terms in the index
    /// </summary>
    int TermCount { get; }

    /// <summary>
    /// Adds or updates a document in the index
    /// </summary>
    /// <param name="documentId">The document ID</param>
    /// <param name="text">The text content to index</param>
    void IndexDocument(string documentId, string text);

    /// <summary>
    /// Removes a document from the index
    /// </summary>
    /// <param name="documentId">The document ID to remove</param>
    /// <returns>True if document was found and removed, false otherwise</returns>
    bool RemoveDocument(string documentId);

    /// <summary>
    /// Searches the index for documents matching the query
    /// </summary>
    /// <param name="query">The search query</param>
    /// <param name="options">Search options</param>
    /// <returns>Search results ordered by relevance</returns>
    FullTextSearchResult Search(string query, FullTextSearchOptions? options = null);

    /// <summary>
    /// Clears all documents from the index
    /// </summary>
    void Clear();

    /// <summary>
    /// Gets statistics about the index
    /// </summary>
    FullTextIndexStats GetStats();
}

/// <summary>
/// Statistics for a full-text index
/// </summary>
public class FullTextIndexStats
{
    /// <summary>
    /// The name of the index
    /// </summary>
    public string IndexName { get; }

    /// <summary>
    /// The collection name
    /// </summary>
    public string CollectionName { get; }

    /// <summary>
    /// The field name
    /// </summary>
    public string FieldName { get; }

    /// <summary>
    /// Number of documents in the index
    /// </summary>
    public int DocumentCount { get; }

    /// <summary>
    /// Number of unique terms
    /// </summary>
    public int TermCount { get; }

    /// <summary>
    /// Average document length in tokens
    /// </summary>
    public double AverageDocumentLength { get; }

    /// <summary>
    /// Total number of tokens across all documents
    /// </summary>
    public long TotalTokens { get; }

    /// <summary>
    /// Creates new index statistics
    /// </summary>
    public FullTextIndexStats(
        string indexName,
        string collectionName,
        string fieldName,
        int documentCount,
        int termCount,
        double averageDocumentLength,
        long totalTokens)
    {
        IndexName = indexName ?? throw new ArgumentNullException(nameof(indexName));
        CollectionName = collectionName ?? throw new ArgumentNullException(nameof(collectionName));
        FieldName = fieldName ?? throw new ArgumentNullException(nameof(fieldName));
        DocumentCount = documentCount;
        TermCount = termCount;
        AverageDocumentLength = averageDocumentLength;
        TotalTokens = totalTokens;
    }
}
