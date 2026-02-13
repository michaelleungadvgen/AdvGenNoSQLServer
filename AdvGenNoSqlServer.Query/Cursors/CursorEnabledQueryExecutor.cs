// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Diagnostics;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Query.Execution;
using AdvGenNoSqlServer.Query.Filtering;
using AdvGenNoSqlServer.Query.Models;
using AdvGenNoSqlServer.Storage;
using AdvGenNoSqlServer.Storage.Indexing;

namespace AdvGenNoSqlServer.Query.Cursors;

/// <summary>
/// Query executor that supports both traditional and cursor-based pagination
/// </summary>
public class CursorEnabledQueryExecutor : ICursorQueryExecutor
{
    private readonly QueryExecutor _baseExecutor;
    private readonly ICursorManager _cursorManager;

    /// <summary>
    /// Creates a new cursor-enabled query executor
    /// </summary>
    public CursorEnabledQueryExecutor(
        IDocumentStore documentStore,
        IFilterEngine filterEngine,
        IndexManager? indexManager = null)
    {
        _baseExecutor = new QueryExecutor(documentStore, filterEngine, indexManager);
        _cursorManager = new CursorManager(documentStore, filterEngine);
    }

    /// <summary>
    /// Creates a new cursor-enabled query executor with an existing cursor manager
    /// </summary>
    public CursorEnabledQueryExecutor(
        IDocumentStore documentStore,
        IFilterEngine filterEngine,
        ICursorManager cursorManager,
        IndexManager? indexManager = null)
    {
        _baseExecutor = new QueryExecutor(documentStore, filterEngine, indexManager);
        _cursorManager = cursorManager;
    }

    /// <inheritdoc />
    public ICursorManager CursorManager => _cursorManager;

    /// <inheritdoc />
    public Task<QueryResult> ExecuteAsync(Models.Query query, CancellationToken cancellationToken = default)
    {
        // Check if cursor-based pagination is requested via query options
        if (query.Options?.Skip == null && query.Options?.Limit != null)
        {
            // Use cursor-based pagination for limit-only queries
            var options = new CursorOptions
            {
                BatchSize = query.Options.Limit.Value,
                IncludeTotalCount = query.Options.IncludeTotalCount
            };
            return ExecuteWithCursorInternalAsync(query, options, cancellationToken);
        }

        // Fall back to base executor for traditional skip/limit pagination
        return _baseExecutor.ExecuteAsync(query, cancellationToken);
    }

    /// <inheritdoc />
    public Task<long> CountAsync(Models.Query query, CancellationToken cancellationToken = default)
    {
        return _baseExecutor.CountAsync(query, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(Models.Query query, CancellationToken cancellationToken = default)
    {
        return _baseExecutor.ExistsAsync(query, cancellationToken);
    }

    /// <inheritdoc />
    public Task<QueryStats> ExplainAsync(Models.Query query)
    {
        return _baseExecutor.ExplainAsync(query);
    }

    /// <inheritdoc />
    public Task<CursorResult> ExecuteWithCursorAsync(
        Models.Query query,
        CursorOptions options,
        CancellationToken cancellationToken = default)
    {
        return _cursorManager.CreateCursorAsync(
            query.CollectionName,
            query.Filter,
            query.Sort,
            options,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<CursorBatchResult> GetMoreAsync(
        string cursorId,
        int? batchSize = null,
        CancellationToken cancellationToken = default)
    {
        return _cursorManager.GetMoreAsync(cursorId, batchSize, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> KillCursorAsync(string cursorId)
    {
        return _cursorManager.KillCursorAsync(cursorId);
    }

    private async Task<QueryResult> ExecuteWithCursorInternalAsync(
        Models.Query query,
        CursorOptions options,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var cursorResult = await ExecuteWithCursorAsync(query, options, cancellationToken);

            if (!cursorResult.Success)
            {
                return QueryResult.FailureResult(cursorResult.ErrorMessage ?? "Unknown error");
            }

            stopwatch.Stop();

            // Convert CursorResult to QueryResult for backwards compatibility
            return new QueryResult
            {
                Documents = cursorResult.Documents,
                TotalCount = cursorResult.TotalCount ?? cursorResult.Documents.Count,
                Skipped = 0,
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds,
                Success = true
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return QueryResult.FailureResult(ex.Message);
        }
    }
}
