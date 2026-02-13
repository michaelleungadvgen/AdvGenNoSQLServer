// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Query.Execution;
using AdvGenNoSqlServer.Query.Models;

namespace AdvGenNoSqlServer.Query.Cursors;

/// <summary>
/// Interface for query executors that support cursor-based pagination
/// </summary>
public interface ICursorQueryExecutor : IQueryExecutor
{
    /// <summary>
    /// Executes a query using cursor-based pagination
    /// </summary>
    /// <param name="query">The query to execute</param>
    /// <param name="options">Cursor options for pagination</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Cursor result containing the first batch and cursor for subsequent batches</returns>
    Task<CursorResult> ExecuteWithCursorAsync(
        Models.Query query,
        CursorOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets more documents from an existing cursor
    /// </summary>
    /// <param name="cursorId">The cursor ID</param>
    /// <param name="batchSize">Optional batch size override</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Batch result with documents</returns>
    Task<CursorBatchResult> GetMoreAsync(
        string cursorId,
        int? batchSize = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Kills (closes) a cursor
    /// </summary>
    /// <param name="cursorId">The cursor ID to kill</param>
    /// <returns>True if cursor was found and killed, false otherwise</returns>
    Task<bool> KillCursorAsync(string cursorId);

    /// <summary>
    /// Gets the cursor manager for advanced operations
    /// </summary>
    ICursorManager CursorManager { get; }
}
