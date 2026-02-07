// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Query.Models;

namespace AdvGenNoSqlServer.Query.Execution;

/// <summary>
/// Interface for executing queries against the document store
/// </summary>
public interface IQueryExecutor
{
    /// <summary>
    /// Executes a query and returns the matching documents
    /// </summary>
    /// <param name="query">The query to execute</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Query result containing matching documents</returns>
    Task<QueryResult> ExecuteAsync(Models.Query query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a query and returns the count of matching documents
    /// </summary>
    /// <param name="query">The query to execute (without options/sort)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Count of matching documents</returns>
    Task<long> CountAsync(Models.Query query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if any documents match the query
    /// </summary>
    /// <param name="query">The query to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if at least one document matches, false otherwise</returns>
    Task<bool> ExistsAsync(Models.Query query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets query statistics without executing the full query
    /// </summary>
    /// <param name="query">The query to analyze</param>
    /// <returns>Query statistics including execution plan</returns>
    Task<QueryStats> ExplainAsync(Models.Query query);
}

/// <summary>
/// Exception thrown when query execution fails
/// </summary>
public class QueryExecutionException : Exception
{
    public QueryExecutionException(string message) : base(message) { }
    public QueryExecutionException(string message, Exception innerException) : base(message, innerException) { }
}
