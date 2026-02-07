// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Query.Models;

namespace AdvGenNoSqlServer.Query.Filtering;

/// <summary>
/// Interface for filtering documents based on query criteria
/// </summary>
public interface IFilterEngine
{
    /// <summary>
    /// Checks if a document matches the filter criteria
    /// </summary>
    /// <param name="document">The document to check</param>
    /// <param name="filter">The filter criteria</param>
    /// <returns>True if the document matches, false otherwise</returns>
    bool Matches(Document document, QueryFilter? filter);

    /// <summary>
    /// Filters a collection of documents
    /// </summary>
    /// <param name="documents">The documents to filter</param>
    /// <param name="filter">The filter criteria</param>
    /// <returns>Documents matching the filter</returns>
    IEnumerable<Document> Filter(IEnumerable<Document> documents, QueryFilter? filter);

    /// <summary>
    /// Extracts a field value from a document using dot notation (e.g., "address.city")
    /// </summary>
    /// <param name="document">The document to extract from</param>
    /// <param name="fieldPath">The field path using dot notation</param>
    /// <returns>The field value if found, null otherwise</returns>
    object? GetFieldValue(Document document, string fieldPath);
}

/// <summary>
/// Exception thrown when filter evaluation fails
/// </summary>
public class FilterEvaluationException : Exception
{
    public FilterEvaluationException(string message) : base(message) { }
    public FilterEvaluationException(string message, Exception innerException) : base(message, innerException) { }
}
