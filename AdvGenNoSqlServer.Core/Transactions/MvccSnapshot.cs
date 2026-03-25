// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.Transactions;

/// <summary>
/// Represents a read snapshot for MVCC transactions.
/// Each transaction gets a snapshot at its start time.
/// </summary>
public sealed class MvccSnapshot
{
    /// <summary>
    /// The read timestamp for this snapshot
    /// </summary>
    public long ReadTimestamp { get; }

    /// <summary>
    /// The transaction ID that owns this snapshot
    /// </summary>
    public string TransactionId { get; }

    /// <summary>
    /// When this snapshot was created
    /// </summary>
    public DateTime CreatedAt { get; }

    /// <summary>
    /// Active transaction IDs at the time of snapshot creation
    /// (transactions that started but not yet committed/aborted)
    /// </summary>
    public IReadOnlySet<string> ActiveTransactionsAtStart { get; }

    /// <summary>
    /// Creates a new MVCC snapshot
    /// </summary>
    public MvccSnapshot(
        long readTimestamp,
        string transactionId,
        IEnumerable<string> activeTransactions)
    {
        ReadTimestamp = readTimestamp;
        TransactionId = transactionId ?? throw new ArgumentNullException(nameof(transactionId));
        CreatedAt = DateTime.UtcNow;
        ActiveTransactionsAtStart = new HashSet<string>(activeTransactions);
    }

    /// <summary>
    /// Checks if a version created by the given transaction is visible in this snapshot
    /// </summary>
    public bool IsVersionVisible(string createdByTransactionId, DateTime versionCreatedAt)
    {
        // Own transaction's writes are always visible
        if (createdByTransactionId == TransactionId)
            return true;

        // Version was created after our read timestamp - not visible
        if (versionCreatedAt.Ticks > ReadTimestamp)
            return false;

        // Version was created by a transaction that was active at our start
        // and is not our own transaction - not visible (uncommitted)
        if (ActiveTransactionsAtStart.Contains(createdByTransactionId))
            return false;

        return true;
    }
}
