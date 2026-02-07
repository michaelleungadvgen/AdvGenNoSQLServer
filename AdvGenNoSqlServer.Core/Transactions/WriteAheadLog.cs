// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AdvGenNoSqlServer.Core.Transactions;

/// <summary>
/// Implementation of Write-Ahead Logging for transaction durability
/// </summary>
public class WriteAheadLog : IWriteAheadLog
{
    // WAL File format constants
    private const ulong WAL_MAGIC = 0x57414C5F44422020; // "WAL_DB "
    private const ushort WAL_VERSION = 1;
    private const int HEADER_SIZE = 32;
    private const int ENTRY_HEADER_SIZE = 48;

    private readonly WalOptions _options;
    private readonly string _logFilePath;
    private readonly string _checkpointFilePath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _lsnLock = new();

    private FileStream? _logFile;
    private long _currentLsn;
    private long _currentFileSize;
    private long _entriesWritten;
    private long _bytesWritten;
    private bool _disposed;
    private Timer? _checkpointTimer;

    /// <inheritdoc />
    public long CurrentLsn => Interlocked.Read(ref _currentLsn);

    /// <inheritdoc />
    public CheckpointInfo? LastCheckpoint { get; private set; }

    /// <summary>
    /// Creates a new WriteAheadLog instance
    /// </summary>
    /// <param name="options">Configuration options</param>
    public WriteAheadLog(WalOptions? options = null)
    {
        _options = options ?? new WalOptions();
        _logFilePath = Path.Combine(_options.LogDirectory, "wal.current");
        _checkpointFilePath = Path.Combine(_options.LogDirectory, "wal.checkpoint");

        // Ensure log directory exists
        Directory.CreateDirectory(_options.LogDirectory);

        // Initialize or recover
        InitializeAsync().GetAwaiter().GetResult();

        // Start checkpoint timer if enabled
        if (_options.CheckpointInterval > TimeSpan.Zero)
        {
            _checkpointTimer = new Timer(
                async _ => await CreateCheckpointAsync(Array.Empty<string>()),
                null,
                _options.CheckpointInterval,
                _options.CheckpointInterval);
        }
    }

    private async Task InitializeAsync()
    {
        // Load last checkpoint if exists
        if (File.Exists(_checkpointFilePath))
        {
            await LoadCheckpointAsync();
        }

        // Open or create log file
        if (File.Exists(_logFilePath))
        {
            await OpenExistingLogAsync();
        }
        else
        {
            await CreateNewLogAsync();
        }
    }

    private async Task LoadCheckpointAsync()
    {
        try
        {
            var json = await File.ReadAllTextAsync(_checkpointFilePath);
            LastCheckpoint = JsonSerializer.Deserialize<CheckpointInfo>(json);
        }
        catch
        {
            LastCheckpoint = null;
        }
    }

    private async Task OpenExistingLogAsync()
    {
        _logFile = new FileStream(
            _logFilePath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.Read,
            _options.BufferSize,
            FileOptions.SequentialScan);

        // Read header
        var header = new byte[HEADER_SIZE];
        _logFile.Position = 0;
        await _logFile.ReadExactlyAsync(header);

        // Verify magic
        var magic = BinaryPrimitives.ReadUInt64LittleEndian(header);
        if (magic != WAL_MAGIC)
        {
            throw new InvalidDataException("Invalid WAL file format");
        }

        // Read version
        var version = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(8));
        if (version != WAL_VERSION)
        {
            throw new InvalidDataException($"Unsupported WAL version: {version}");
        }

        // Read current LSN from header
        _currentLsn = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(16));
        _currentFileSize = _logFile.Length;

        // Position at end for appending
        _logFile.Position = _currentFileSize;
    }

    private async Task CreateNewLogAsync()
    {
        _logFile = new FileStream(
            _logFilePath,
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.Read,
            _options.BufferSize,
            FileOptions.SequentialScan);

        // Write header
        var header = new byte[HEADER_SIZE];
        BinaryPrimitives.WriteUInt64LittleEndian(header, WAL_MAGIC);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), WAL_VERSION);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(16), _currentLsn);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(24), DateTime.UtcNow.Ticks);

        await _logFile.WriteAsync(header);
        await _logFile.FlushAsync();

        _currentFileSize = HEADER_SIZE;
        _bytesWritten = HEADER_SIZE;
    }

    /// <inheritdoc />
    public async Task<long> AppendBeginTransactionAsync(string transactionId)
    {
        return await AppendEntryAsync(new WalLogEntry
        {
            TransactionId = transactionId,
            OperationType = WalOperationType.BeginTransaction,
            Timestamp = DateTime.UtcNow
        });
    }

    /// <inheritdoc />
    public async Task<long> AppendCommitAsync(string transactionId)
    {
        return await AppendEntryAsync(new WalLogEntry
        {
            TransactionId = transactionId,
            OperationType = WalOperationType.Commit,
            Timestamp = DateTime.UtcNow
        });
    }

    /// <inheritdoc />
    public async Task<long> AppendRollbackAsync(string transactionId)
    {
        return await AppendEntryAsync(new WalLogEntry
        {
            TransactionId = transactionId,
            OperationType = WalOperationType.Rollback,
            Timestamp = DateTime.UtcNow
        });
    }

    /// <inheritdoc />
    public async Task<long> AppendInsertAsync(string transactionId, string collectionName, Document document)
    {
        return await AppendEntryAsync(new WalLogEntry
        {
            TransactionId = transactionId,
            OperationType = WalOperationType.Insert,
            CollectionName = collectionName,
            DocumentId = document.Id,
            AfterImage = document,
            Timestamp = DateTime.UtcNow
        });
    }

    /// <inheritdoc />
    public async Task<long> AppendUpdateAsync(
        string transactionId,
        string collectionName,
        Document beforeImage,
        Document afterImage)
    {
        return await AppendEntryAsync(new WalLogEntry
        {
            TransactionId = transactionId,
            OperationType = WalOperationType.Update,
            CollectionName = collectionName,
            DocumentId = afterImage.Id,
            BeforeImage = beforeImage,
            AfterImage = afterImage,
            Timestamp = DateTime.UtcNow
        });
    }

    /// <inheritdoc />
    public async Task<long> AppendDeleteAsync(string transactionId, string collectionName, Document document)
    {
        return await AppendEntryAsync(new WalLogEntry
        {
            TransactionId = transactionId,
            OperationType = WalOperationType.Delete,
            CollectionName = collectionName,
            DocumentId = document.Id,
            BeforeImage = document,
            Timestamp = DateTime.UtcNow
        });
    }

    private async Task<long> AppendEntryAsync(WalLogEntry entry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _writeLock.WaitAsync();
        try
        {
            return await AppendEntryInternalAsync(entry);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<long> AppendEntryInternalAsync(WalLogEntry entry)
    {
        // Check if we need to rotate
        if (_currentFileSize >= _options.MaxFileSize)
        {
            await RotateLogAsync();
        }

        // Assign LSN
        entry.Lsn = Interlocked.Increment(ref _currentLsn);

        // Serialize entry
        var serializedEntry = SerializeEntry(entry);
        entry.EntrySize = serializedEntry.Length;

        // Calculate checksum
        entry.Checksum = CalculateCrc32(serializedEntry);

        // Build complete entry with header
        var completeEntry = BuildEntryWithHeader(entry, serializedEntry);

        // Write to file
        await _logFile!.WriteAsync(completeEntry);

        if (_options.ForceSync)
        {
            await _logFile.FlushAsync();
            _logFile.Flush(true); // Flush to disk
        }

        // Update stats
        _currentFileSize += completeEntry.Length;
        _bytesWritten += completeEntry.Length;
        Interlocked.Increment(ref _entriesWritten);

        // Update header with current LSN
        await UpdateHeaderLsnAsync();

        return entry.Lsn;
    }

    private byte[] SerializeEntry(WalLogEntry entry)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8);

        // Write transaction ID
        writer.Write(entry.TransactionId);

        // Write operation type
        writer.Write((byte)entry.OperationType);

        // Write collection name (nullable)
        writer.Write(entry.CollectionName ?? string.Empty);

        // Write document ID (nullable)
        writer.Write(entry.DocumentId ?? string.Empty);

        // Write before image (nullable)
        if (entry.BeforeImage != null)
        {
            writer.Write(true);
            var beforeJson = JsonSerializer.Serialize(entry.BeforeImage);
            writer.Write(beforeJson);
        }
        else
        {
            writer.Write(false);
        }

        // Write after image (nullable)
        if (entry.AfterImage != null)
        {
            writer.Write(true);
            var afterJson = JsonSerializer.Serialize(entry.AfterImage);
            writer.Write(afterJson);
        }
        else
        {
            writer.Write(false);
        }

        // Write timestamp
        writer.Write(entry.Timestamp.Ticks);

        writer.Flush();
        return ms.ToArray();
    }

    private byte[] BuildEntryWithHeader(WalLogEntry entry, byte[] serializedData)
    {
        var result = new byte[ENTRY_HEADER_SIZE + serializedData.Length];

        // Entry header
        BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan(0), entry.Lsn);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(8), serializedData.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), entry.Checksum);
        BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan(16), entry.Timestamp.Ticks);
        result[24] = (byte)entry.OperationType;
        // 23 bytes reserved for future use

        // Copy serialized data
        Buffer.BlockCopy(serializedData, 0, result, ENTRY_HEADER_SIZE, serializedData.Length);

        return result;
    }

    private async Task UpdateHeaderLsnAsync()
    {
        var buffer = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, _currentLsn);

        _logFile!.Position = 16; // LSN offset in header
        await _logFile.WriteAsync(buffer);
        await _logFile.FlushAsync();

        _logFile.Position = _currentFileSize; // Return to end
    }

    private async Task RotateLogAsync()
    {
        var oldPath = _logFilePath;
        var newPath = Path.Combine(
            _options.LogDirectory,
            $"wal.{DateTime.UtcNow:yyyyMMddHHmmss}.{_currentLsn}");

        // Close current file
        await _logFile!.FlushAsync();
        _logFile.Close();

        // Rename to archived name
        File.Move(oldPath, newPath);

        // Create new log file
        _currentFileSize = 0;
        await CreateNewLogAsync();

        // Clean up old files if needed
        CleanupOldLogFiles();

        // Raise event
        LogRotated?.Invoke(this, new LogRotationEventArgs
        {
            OldFilePath = newPath,
            NewFilePath = oldPath,
            RotationLsn = _currentLsn,
            Timestamp = DateTime.UtcNow
        });
    }

    private void CleanupOldLogFiles()
    {
        var logFiles = Directory.GetFiles(_options.LogDirectory, "wal.*.*")
            .Where(f => !f.EndsWith(".current") && !f.EndsWith(".checkpoint"))
            .OrderByDescending(f => f)
            .Skip(_options.MaxRetainedFiles);

        foreach (var file in logFiles)
        {
            try
            {
                File.Delete(file);
            }
            catch
            {
                // Ignore deletion errors
            }
        }
    }

    /// <inheritdoc />
    public async Task<long> CreateCheckpointAsync(IEnumerable<string> activeTransactions)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _writeLock.WaitAsync();
        try
        {
            // Flush directly without calling FlushAsync (which would deadlock)
            if (_logFile != null)
            {
                await _logFile.FlushAsync();
                _logFile.Flush(true);
            }

            // Write checkpoint entry to log (internal version without lock)
            var checkpointLsn = await AppendEntryInternalAsync(new WalLogEntry
            {
                TransactionId = "CHECKPOINT",
                OperationType = WalOperationType.Checkpoint,
                Timestamp = DateTime.UtcNow
            });

            var checkpoint = new CheckpointInfo
            {
                CheckpointLsn = checkpointLsn,
                Timestamp = DateTime.UtcNow,
                ActiveTransactions = activeTransactions.ToList()
            };

            // Save checkpoint metadata
            var json = JsonSerializer.Serialize(checkpoint);
            await File.WriteAllTextAsync(_checkpointFilePath, json);

            LastCheckpoint = checkpoint;

            CheckpointCreated?.Invoke(this, new CheckpointEventArgs
            {
                CheckpointLsn = checkpoint.CheckpointLsn,
                ActiveTransactions = checkpoint.ActiveTransactions.AsReadOnly(),
                Timestamp = checkpoint.Timestamp
            });

            return checkpoint.CheckpointLsn;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<RecoveryResult> RecoverAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var result = new RecoveryResult
        {
            Success = true,
            CommittedTransactions = new List<string>(),
            IncompleteTransactions = new List<string>()
        };

        var transactions = new Dictionary<string, TransactionRecoveryState>();

        try
        {
            await foreach (var entry in ReplayEntriesAsync(0))
            {
                result.ReplayedEntries++;

                switch (entry.OperationType)
                {
                    case WalOperationType.BeginTransaction:
                        transactions[entry.TransactionId] = new TransactionRecoveryState
                        {
                            TransactionId = entry.TransactionId,
                            Status = TransactionStatus.Active,
                            Operations = new List<WalLogEntry>()
                        };
                        break;

                    case WalOperationType.Commit:
                        if (transactions.TryGetValue(entry.TransactionId, out var commitState))
                        {
                            commitState.Status = TransactionStatus.Committed;
                        }
                        break;

                    case WalOperationType.Rollback:
                        if (transactions.TryGetValue(entry.TransactionId, out var rollbackState))
                        {
                            rollbackState.Status = TransactionStatus.RolledBack;
                        }
                        break;

                    case WalOperationType.Insert:
                    case WalOperationType.Update:
                    case WalOperationType.Delete:
                        if (transactions.TryGetValue(entry.TransactionId, out var opState))
                        {
                            opState.Operations.Add(entry);
                        }
                        break;
                }
            }

            // Determine which transactions need recovery action
            foreach (var state in transactions.Values)
            {
                if (state.Status == TransactionStatus.Committed)
                {
                    result.CommittedTransactions.Add(state.TransactionId);
                }
                else if (state.Status == TransactionStatus.Active)
                {
                    // These need to be rolled back (no commit or rollback record)
                    result.IncompleteTransactions.Add(state.TransactionId);
                }
                // RolledBack transactions are complete and don't need recovery
            }

            result.RecoveredTransactions = transactions.Count;
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            return result;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<WalLogEntry> ReplayEntriesAsync(
        long startLsn,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_logFile == null) yield break;

        // First, replay from archived files if needed
        var archivedFiles = Directory.GetFiles(_options.LogDirectory, "wal.*.*")
            .Where(f => !f.EndsWith(".current") && !f.EndsWith(".checkpoint"))
            .OrderBy(f => f);

        foreach (var file in archivedFiles)
        {
            await foreach (var entry in ReplayFileAsync(file, startLsn, cancellationToken))
            {
                yield return entry;
            }
        }

        // Then replay from current file using a separate read stream
        // (since the write stream is at the end and we can't easily share position)
        await foreach (var entry in ReplayCurrentFileAsync(startLsn, cancellationToken))
        {
            yield return entry;
        }
    }

    private async IAsyncEnumerable<WalLogEntry> ReplayCurrentFileAsync(
        long startLsn,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Open a separate read stream for the current log file
        // Use FileShare.ReadWrite to allow concurrent access with the write stream
        using var readStream = new FileStream(
            _logFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            _options.BufferSize,
            FileOptions.SequentialScan);

        // Skip header
        readStream.Position = HEADER_SIZE;

        while (readStream.Position < readStream.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entryHeader = new byte[ENTRY_HEADER_SIZE];
            var read = await readStream.ReadAsync(entryHeader.AsMemory(0, ENTRY_HEADER_SIZE), cancellationToken);
            if (read < ENTRY_HEADER_SIZE) break;

            var lsn = BinaryPrimitives.ReadInt64LittleEndian(entryHeader.AsSpan(0));
            var dataLength = BinaryPrimitives.ReadInt32LittleEndian(entryHeader.AsSpan(8));
            var checksum = BinaryPrimitives.ReadUInt32LittleEndian(entryHeader.AsSpan(12));
            var timestamp = new DateTime(BinaryPrimitives.ReadInt64LittleEndian(entryHeader.AsSpan(16)));
            var opType = (WalOperationType)entryHeader[24];

            if (lsn < startLsn)
            {
                // Skip this entry's data
                readStream.Position += dataLength;
                continue;
            }

            var data = new byte[dataLength];
            read = await readStream.ReadAsync(data.AsMemory(0, dataLength), cancellationToken);
            if (read < dataLength) break;

            // Verify checksum
            var calculatedChecksum = CalculateCrc32(data);
            if (calculatedChecksum != checksum)
            {
                throw new InvalidDataException($"CRC mismatch at LSN {lsn}");
            }

            var entry = DeserializeEntry(data);
            entry.Lsn = lsn;
            entry.Checksum = checksum;
            entry.Timestamp = timestamp;
            entry.OperationType = opType;

            yield return entry;
        }
    }

    private async IAsyncEnumerable<WalLogEntry> ReplayFileAsync(
        string filePath,
        long startLsn,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath)) yield break;

        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            _options.BufferSize,
            FileOptions.SequentialScan);

        // Skip header
        stream.Position = HEADER_SIZE;

        while (stream.Position < stream.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entryHeader = new byte[ENTRY_HEADER_SIZE];
            var read = await stream.ReadAsync(entryHeader.AsMemory(0, ENTRY_HEADER_SIZE), cancellationToken);
            if (read < ENTRY_HEADER_SIZE) break;

            var lsn = BinaryPrimitives.ReadInt64LittleEndian(entryHeader.AsSpan(0));
            var dataLength = BinaryPrimitives.ReadInt32LittleEndian(entryHeader.AsSpan(8));
            var checksum = BinaryPrimitives.ReadUInt32LittleEndian(entryHeader.AsSpan(12));
            var timestamp = new DateTime(BinaryPrimitives.ReadInt64LittleEndian(entryHeader.AsSpan(16)));
            var opType = (WalOperationType)entryHeader[24];

            if (lsn < startLsn)
            {
                // Skip this entry's data
                stream.Position += dataLength;
                continue;
            }

            var data = new byte[dataLength];
            read = await stream.ReadAsync(data.AsMemory(0, dataLength), cancellationToken);
            if (read < dataLength) break;

            // Verify checksum
            var calculatedChecksum = CalculateCrc32(data);
            if (calculatedChecksum != checksum)
            {
                // Corrupted entry - skip or throw?
                // For recovery, we'll throw to indicate corruption
                throw new InvalidDataException($"CRC mismatch at LSN {lsn}");
            }

            var entry = DeserializeEntry(data);
            entry.Lsn = lsn;
            entry.Checksum = checksum;
            entry.Timestamp = timestamp;
            entry.OperationType = opType;

            yield return entry;
        }
    }

    private WalLogEntry DeserializeEntry(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms, Encoding.UTF8);

        var entry = new WalLogEntry();

        entry.TransactionId = reader.ReadString();
        entry.OperationType = (WalOperationType)reader.ReadByte();
        entry.CollectionName = reader.ReadString();
        entry.DocumentId = reader.ReadString();

        // Before image
        if (reader.ReadBoolean())
        {
            var beforeJson = reader.ReadString();
            entry.BeforeImage = JsonSerializer.Deserialize<Document>(beforeJson);
        }

        // After image
        if (reader.ReadBoolean())
        {
            var afterJson = reader.ReadString();
            entry.AfterImage = JsonSerializer.Deserialize<Document>(afterJson);
        }

        entry.Timestamp = new DateTime(reader.ReadInt64());

        return entry;
    }

    /// <inheritdoc />
    public async Task<bool> TruncateAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (LastCheckpoint == null) return false;

        await _writeLock.WaitAsync();
        try
        {
            // Archive and truncate log up to checkpoint
            var checkpointLsn = LastCheckpoint.CheckpointLsn;

            // Close current file
            await _logFile!.FlushAsync();
            _logFile.Close();

            // Create truncated log with only entries after checkpoint
            var tempPath = _logFilePath + ".tmp";
            await using (var newLog = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                _options.BufferSize))
            {
                // Write header
                var header = new byte[HEADER_SIZE];
                BinaryPrimitives.WriteUInt64LittleEndian(header, WAL_MAGIC);
                BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), WAL_VERSION);
                BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(16), _currentLsn);
                BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(24), DateTime.UtcNow.Ticks);
                await newLog.WriteAsync(header);

                // Copy entries after checkpoint
                await foreach (var entry in ReplayEntriesAsync(checkpointLsn + 1))
                {
                    var serializedEntry = SerializeEntry(entry);
                    var completeEntry = BuildEntryWithHeader(entry, serializedEntry);
                    await newLog.WriteAsync(completeEntry);
                }

                await newLog.FlushAsync();
            }

            // Replace old log with truncated version
            File.Move(tempPath, _logFilePath, overwrite: true);

            // Reopen log
            await OpenExistingLogAsync();

            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task FlushAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_logFile != null)
        {
            await _writeLock.WaitAsync();
            try
            {
                await _logFile.FlushAsync();
                _logFile.Flush(true); // Flush to disk
            }
            finally
            {
                _writeLock.Release();
            }
        }
    }

    /// <inheritdoc />
    public WalStatistics GetStatistics()
    {
        return new WalStatistics
        {
            CurrentLsn = CurrentLsn,
            TotalEntries = Interlocked.Read(ref _entriesWritten),
            TotalBytes = Interlocked.Read(ref _bytesWritten),
            CurrentFileSize = _currentFileSize,
            FileCount = Directory.GetFiles(_options.LogDirectory, "wal.*").Length,
            LastCheckpointLsn = LastCheckpoint?.CheckpointLsn ?? 0,
            LastCheckpointTime = LastCheckpoint?.Timestamp
        };
    }

    /// <summary>
    /// Calculates CRC32 checksum for data integrity
    /// </summary>
    private static uint CalculateCrc32(byte[] data)
    {
        // Simple CRC32 implementation
        uint crc = 0xFFFFFFFF;

        foreach (var b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                crc = (crc >> 1) ^ (0xEDB88320 & (uint)(-(crc & 1)));
            }
        }

        return ~crc;
    }

    /// <inheritdoc />
    public event EventHandler<CheckpointEventArgs>? CheckpointCreated;

    /// <inheritdoc />
    public event EventHandler<LogRotationEventArgs>? LogRotated;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        _checkpointTimer?.Dispose();
        _writeLock.Dispose();

        if (_logFile != null)
        {
            _logFile.Flush(true);
            _logFile.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private class TransactionRecoveryState
    {
        public string TransactionId { get; set; } = string.Empty;
        public TransactionStatus Status { get; set; }
        public List<WalLogEntry> Operations { get; set; } = new();
    }
}
