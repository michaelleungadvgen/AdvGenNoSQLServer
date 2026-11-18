// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.Transactions;

/// <summary>
/// Commit-state record for a registered MVCC transaction. Visibility of a version
/// depends on its creator's commit state and commit timestamp, not just timestamps.
/// </summary>
public sealed class MvccTransactionRecord
{
    /// <summary>Gets the current commit state of the transaction.</summary>
    public TransactionState State { get; internal set; }

    /// <summary>Gets the timestamp assigned when the transaction committed.</summary>
    public long CommitTimestamp { get; internal set; }
}

/// <summary>
/// The kind of write being undone during rollback.
/// </summary>
public enum MvccUndoKind
{
    /// <summary>Undo an insert by removing the created version.</summary>
    Insert,

    /// <summary>Undo an update by restoring the previous version.</summary>
    Update,

    /// <summary>Undo a delete by clearing the deletion mark.</summary>
    Delete
}