// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using System.Runtime.CompilerServices;

namespace AdvGenNoSqlServer.Core.Transactions;

/// <summary>
/// Represents the type of operation recorded in the WAL
/// </summary>
public enum WalOperationType
{
    /// <summary>Transaction begin marker</summary>
    BeginTransaction = 1,

    /// <summary>Transaction commit marker</summary>
    Commit = 2,

    /// <summary>Transaction rollback marker</summary>
    Rollback = 3,

    /// <summary>Document insert operation</summary>
    Insert = 4,

    /// <summary>Document update operation</summary>
    Update = 5,

    /// <summary>Document delete operation</summary>
    Delete = 6,

    /// <summary>Checkpoint marker for log truncation</summary>
    Checkpoint = 7
}

/// <summary>
/// Represents a single entry in the Write-Ahead Log
/// </summary>
public class WalLogEntry
{
    /// <summary>
    /// Unique Log Sequence Number (monotonically increasing)
    /// </summary>
    public long Lsn { get; set; }

    /// <summary>
    /// The transaction ID this entry belongs to
    /// </summary>
    public string TransactionId { get; set; } = string.Empty;

    /// <summary>
    /// The type of operation recorded
    /// </summary>
    public WalOperationType OperationType { get; set; }

    /// <summary>
    /// The collection name (for data operations)
    /// </summary>
    public string? CollectionName { get; set; }

    /// <summary>
    /// The document ID (for data operations)
    /// </summary>
    public string? DocumentId { get; set; }

    /// <summary>
    /// The document data before the operation (for rollback)
    /// </summary>
    public Document? BeforeImage { get; set; }

    /// <summary>
    /// The document data after the operation
    /// </summary>
    public Document? AfterImage { get; set; }

    /// <summary>
    /// Timestamp when the entry was created
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// CRC32 checksum for data integrity
    /// </summary>
    public uint Checksum { get; set; }

    /// <summary>
    /// Entry size in bytes (for log truncation)
    /// </summary>
    public int EntrySize { get; set; }
}

/// <summary>
/// Represents a checkpoint record for log truncation
/// </summary>
public class CheckpointInfo
{
    /// <summary>
    /// The LSN of the checkpoint
    /// </summary>
    public long CheckpointLsn { get; set; }

    /// <summary>
    /// Timestamp when checkpoint was created
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Active transactions at the time of checkpoint
    /// </summary>
    public List<string> ActiveTransactions { get; set; } = new();
}

/// <summary>
/// Result of a log recovery operation
/// </summary>
public class RecoveryResult
{
    /// <summary>
    /// Whether recovery was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Number of transactions recovered
    /// </summary>
    public int RecoveredTransactions { get; set; }

    /// <summary>
    /// Number of entries replayed
    /// </summary>
    public int ReplayedEntries { get; set; }

    /// <summary>
    /// Error message if recovery failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// List of committed transactions found during recovery
    /// </summary>
    public List<string> CommittedTransactions { get; set; } = new();

    /// <summary>
    /// List of transactions that need rollback (incomplete)
    /// </summary>
    public List<string> IncompleteTransactions { get; set; } = new();
}

/// <summary>
/// Configuration options for the Write-Ahead Log
/// </summary>
public class WalOptions
{
    /// <summary>
    /// Directory where WAL files are stored
    /// </summary>
    public string LogDirectory { get; set; } = "./wal";

    /// <summary>
    /// Maximum size of a single WAL file before rotation (default: 64MB)
    /// </summary>
    public long MaxFileSize { get; set; } = 64 * 1024 * 1024;

    /// <summary>
    /// Whether to force flush to disk after each write (fsync)
    /// </summary>
    public bool ForceSync { get; set; } = true;

    /// <summary>
    /// Buffer size for writing to disk
    /// </summary>
    public int BufferSize { get; set; } = 8192;

    /// <summary>
    /// Interval for automatic checkpointing (0 = disabled)
    /// </summary>
    public TimeSpan CheckpointInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum number of WAL files to retain
    /// </summary>
    public int MaxRetainedFiles { get; set; } = 10;
}

/// <summary>
/// Interface for Write-Ahead Logging operations
/// </summary>
public interface IWriteAheadLog : IDisposable
{
    /// <summary>
    /// Gets the current Log Sequence Number
    /// </summary>
    long CurrentLsn { get; }

    /// <summary>
    /// Gets the last checkpoint information
    /// </summary>
    CheckpointInfo? LastCheckpoint { get; }

    /// <summary>
    /// Appends a begin transaction entry to the log
    /// </summary>
    /// <param name="transactionId">The transaction ID</param>
    /// <returns>The assigned LSN</returns>
    Task<long> AppendBeginTransactionAsync(string transactionId);

    /// <summary>
    /// Appends a commit entry to the log
    /// </summary>
    /// <param name="transactionId">The transaction ID</param>
    /// <returns>The assigned LSN</returns>
    Task<long> AppendCommitAsync(string transactionId);

    /// <summary>
    /// Appends a rollback entry to the log
    /// </summary>
    /// <param name="transactionId">The transaction ID</param>
    /// <returns>The assigned LSN</returns>
    Task<long> AppendRollbackAsync(string transactionId);

    /// <summary>
    /// Appends an insert operation to the log
    /// </summary>
    /// <param name="transactionId">The transaction ID</param>
    /// <param name="collectionName">The collection name</param>
    /// <param name="document">The document being inserted</param>
    /// <returns>The assigned LSN</returns>
    Task<long> AppendInsertAsync(string transactionId, string collectionName, Document document);

    /// <summary>
    /// Appends an update operation to the log
    /// </summary>
    /// <param name="transactionId">The transaction ID</param>
    /// <param name="collectionName">The collection name</param>
    /// <param name="beforeImage">The document before update</param>
    /// <param name="afterImage">The document after update</param>
    /// <returns>The assigned LSN</returns>
    Task<long> AppendUpdateAsync(string transactionId, string collectionName, Document beforeImage, Document afterImage);

    /// <summary>
    /// Appends a delete operation to the log
    /// </summary>
    /// <param name="transactionId">The transaction ID</param>
    /// <param name="collectionName">The collection name</param>
    /// <param name="document">The document being deleted</param>
    /// <returns>The assigned LSN</returns>
    Task<long> AppendDeleteAsync(string transactionId, string collectionName, Document document);

    /// <summary>
    /// Creates a checkpoint for log truncation
    /// </summary>
    /// <param name="activeTransactions">List of currently active transactions</param>
    /// <returns>The checkpoint LSN</returns>
    Task<long> CreateCheckpointAsync(IEnumerable<string> activeTransactions);

    /// <summary>
    /// Recovers from the WAL after a crash
    /// </summary>
    /// <returns>Recovery result with committed and incomplete transactions</returns>
    Task<RecoveryResult> RecoverAsync();

    /// <summary>
    /// Replays log entries from a specific LSN
    /// </summary>
    /// <param name="startLsn">The starting LSN</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Async enumerable of log entries</returns>
    IAsyncEnumerable<WalLogEntry> ReplayEntriesAsync(long startLsn, CancellationToken cancellationToken = default);

    /// <summary>
    /// Truncates the log up to the last checkpoint
    /// </summary>
    /// <returns>True if truncation was successful</returns>
    Task<bool> TruncateAsync();

    /// <summary>
    /// Forces all pending writes to disk
    /// </summary>
    Task FlushAsync();

    /// <summary>
    /// Gets statistics about the WAL
    /// </summary>
    WalStatistics GetStatistics();

    /// <summary>
    /// Event raised when a checkpoint is created
    /// </summary>
    event EventHandler<CheckpointEventArgs>? CheckpointCreated;

    /// <summary>
    /// Event raised when log rotation occurs
    /// </summary>
    event EventHandler<LogRotationEventArgs>? LogRotated;
}

/// <summary>
/// Statistics for the Write-Ahead Log
/// </summary>
public class WalStatistics
{
    /// <summary>
    /// Current LSN
    /// </summary>
    public long CurrentLsn { get; set; }

    /// <summary>
    /// Total number of entries written
    /// </summary>
    public long TotalEntries { get; set; }

    /// <summary>
    /// Total bytes written
    /// </summary>
    public long TotalBytes { get; set; }

    /// <summary>
    /// Current log file size
    /// </summary>
    public long CurrentFileSize { get; set; }

    /// <summary>
    /// Number of log files
    /// </summary>
    public int FileCount { get; set; }

    /// <summary>
    /// Last checkpoint LSN (0 if no checkpoint)
    /// </summary>
    public long LastCheckpointLsn { get; set; }

    /// <summary>
    /// When the last checkpoint was created
    /// </summary>
    public DateTime? LastCheckpointTime { get; set; }
}

/// <summary>
/// Event args for checkpoint creation
/// </summary>
public class CheckpointEventArgs : EventArgs
{
    /// <summary>
    /// The checkpoint LSN
    /// </summary>
    public long CheckpointLsn { get; set; }

    /// <summary>
    /// Active transactions at checkpoint time
    /// </summary>
    public IReadOnlyList<string> ActiveTransactions { get; set; } = new List<string>();

    /// <summary>
    /// Timestamp of the checkpoint
    /// </summary>
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Event args for log rotation
/// </summary>
public class LogRotationEventArgs : EventArgs
{
    /// <summary>
    /// Path of the old log file
    /// </summary>
    public string OldFilePath { get; set; } = string.Empty;

    /// <summary>
    /// Path of the new log file
    /// </summary>
    public string NewFilePath { get; set; } = string.Empty;

    /// <summary>
    /// The LSN at which rotation occurred
    /// </summary>
    public long RotationLsn { get; set; }

    /// <summary>
    /// Timestamp of rotation
    /// </summary>
    public DateTime Timestamp { get; set; }
}
