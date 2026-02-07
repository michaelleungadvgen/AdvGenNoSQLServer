// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Text.Json;
using AdvGenNoSqlServer.Query.Models;

namespace AdvGenNoSqlServer.Query.Parsing;

/// <summary>
/// Interface for parsing queries from various formats
/// </summary>
public interface IQueryParser
{
    /// <summary>
    /// Parses a query from a JSON string
    /// </summary>
    /// <param name="json">The JSON query string</param>
    /// <returns>The parsed Query object</returns>
    /// <exception cref="QueryParseException">Thrown when the query cannot be parsed</exception>
    Models.Query Parse(string json);

    /// <summary>
    /// Parses a query from a JSON element
    /// </summary>
    /// <param name="jsonElement">The JSON element to parse</param>
    /// <returns>The parsed Query object</returns>
    Models.Query Parse(JsonElement jsonElement);

    /// <summary>
    /// Parses a filter from a JSON string
    /// </summary>
    /// <param name="json">The JSON filter string</param>
    /// <returns>The parsed QueryFilter</returns>
    QueryFilter ParseFilter(string json);

    /// <summary>
    /// Tries to parse a query from a JSON string
    /// </summary>
    /// <param name="json">The JSON query string</param>
    /// <param name="query">The parsed query if successful</param>
    /// <param name="error">Error message if parsing failed</param>
    /// <returns>True if parsing succeeded, false otherwise</returns>
    bool TryParse(string json, out Models.Query? query, out string? error);
}

/// <summary>
/// Exception thrown when a query cannot be parsed
/// </summary>
public class QueryParseException : Exception
{
    public QueryParseException(string message) : base(message) { }
    public QueryParseException(string message, Exception innerException) : base(message, innerException) { }
}
