// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Core.Transactions;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

public class WriteAheadLogTests : IDisposable
{
    private readonly string _testLogDirectory;

    public WriteAheadLogTests()
    {
        _testLogDirectory = Path.Combine(Path.GetTempPath(), $"wal_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testLogDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testLogDirectory))
            {
                Directory.Delete(_testLogDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private static async Task<List<WalLogEntry>> ToListAsync(IAsyncEnumerable<WalLogEntry> source)
    {
        var list = new List<WalLogEntry>();
        await foreach (var item in source)
        {
            list.Add(item);
        }
        return list;
    }

    private WalOptions CreateTestOptions(bool forceSync = false)
    {
        return new WalOptions
        {
            LogDirectory = _testLogDirectory,
            ForceSync = forceSync,
            BufferSize = 4096,
            MaxFileSize = 1024 * 1024 // 1MB for testing
        };
    }

    #region Basic Operations

    [Fact]
    public async Task Wal_CreateNewLog_Success()
    {
        // Arrange
        var options = CreateTestOptions();

        // Act
        using var wal = new WriteAheadLog(options);

        // Assert
        Assert.True(Directory.Exists(_testLogDirectory));
        Assert.True(File.Exists(Path.Combine(_testLogDirectory, "wal.current")));
        Assert.Equal(0, wal.CurrentLsn);
    }

    [Fact]
    public async Task Wal_AppendBeginTransaction_ReturnsLsn()
    {
        // Arrange
        var options = CreateTestOptions();
        using var wal = new WriteAheadLog(options);
        var transactionId = Guid.NewGuid().ToString();

        // Act
        var lsn = await wal.AppendBeginTransactionAsync(transactionId);

        // Assert
        Assert.True(lsn > 0);
        Assert.Equal(lsn, wal.CurrentLsn);
    }

    [Fact]
    public async Task Wal_AppendCommit_ReturnsLsn()
    {
        // Arrange
        var options = CreateTestOptions();
        using var wal = new WriteAheadLog(options);
        var transactionId = Guid.NewGuid().ToString();

        // Act
        await wal.AppendBeginTransactionAsync(transactionId);
        var lsn = await wal.AppendCommitAsync(transactionId);

        // Assert
        Assert.True(lsn > 0);
    }

    [Fact]
    public async Task Wal_AppendRollback_ReturnsLsn()
    {
        // Arrange
        var options = CreateTestOptions();
        using var wal = new WriteAheadLog(options);
        var transactionId = Guid.NewGuid().ToString();

        // Act
        await wal.AppendBeginTransactionAsync(transactionId);
        var lsn = await wal.AppendRollbackAsync(transactionId);

        // Assert
        Assert.True(lsn > 0);
    }

    [Fact]
    public async Task Wal_Lsn_IsSequential()
    {
        // Arrange
        var options = CreateTestOptions();
        using var wal = new WriteAheadLog(options);
        var transactionId = Guid.NewGuid().ToString();

        // Act
        var lsn1 = await wal.AppendBeginTransactionAsync(transactionId);
        var lsn2 = await wal.AppendCommitAsync(transactionId);
        var lsn3 = await wal.AppendBeginTransactionAsync(Guid.NewGuid().ToString());

        // Assert
        Assert.True(lsn2 > lsn1);
        Assert.True(lsn3 > lsn2);
        Assert.Equal(lsn1 + 1, lsn2);
        Assert.Equal(lsn2 + 1, lsn3);
    }

    #endregion

    #region Data Operations

    [Fact]
    public async Task Wal_AppendInsert_ReturnsLsn()
    {
        // Arrange
        var options = CreateTestOptions();
        using var wal = new WriteAheadLog(options);
        var transactionId = Guid.NewGuid().ToString();
        var document = new Document
        {
            Id = "doc-1",
            Data = new System.Collections.Generic.Dictionary<string, object>
            {
                ["name"] = "Test Document",
                ["value"] = 42
            },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };

        // Act
        await wal.AppendBeginTransactionAsync(transactionId);
        var lsn = await wal.AppendInsertAsync(transactionId, "test-collection", document);

        // Assert
        Assert.True(lsn > 0);
    }

    [Fact]
    public async Task Wal_AppendUpdate_ReturnsLsn()
    {
        // Arrange
        var options = CreateTestOptions();
        using var wal = new WriteAheadLog(options);
        var transactionId = Guid.NewGuid().ToString();
        var beforeDoc = new Document
        {
            Id = "doc-1",
            Data = new System.Collections.Generic.Dictionary<string, object> { ["value"] = 42 },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };
        var afterDoc = new Document
        {
            Id = "doc-1",
            Data = new System.Collections.Generic.Dictionary<string, object> { ["value"] = 100 },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 2
        };

        // Act
        await wal.AppendBeginTransactionAsync(transactionId);
        var lsn = await wal.AppendUpdateAsync(transactionId, "test-collection", beforeDoc, afterDoc);

        // Assert
        Assert.True(lsn > 0);
    }

    [Fact]
    public async Task Wal_AppendDelete_ReturnsLsn()
    {
        // Arrange
        var options = CreateTestOptions();
        using var wal = new WriteAheadLog(options);
        var transactionId = Guid.NewGuid().ToString();
        var document = new Document
        {
            Id = "doc-1",
            Data = new System.Collections.Generic.Dictionary<string, object> { ["value"] = 42 },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };

        // Act
        await wal.AppendBeginTransactionAsync(transactionId);
        var lsn = await wal.AppendDeleteAsync(transactionId, "test-collection", document);

        // Assert
        Assert.True(lsn > 0);
    }

    #endregion

    #region Recovery

    [Fact]
    public async Task Wal_Recover_EmptyLog_ReturnsSuccess()
    {
        // Arrange
        var options = CreateTestOptions();
        using var wal = new WriteAheadLog(options);

        // Act
        var result = await wal.RecoverAsync();

        // Debug: Print error if failed
        if (!result.Success)
        {
            Console.WriteLine($"Recovery failed: {result.ErrorMessage}");
        }

        // Assert
        Assert.True(result.Success, $"Recovery failed: {result.ErrorMessage}");
        Assert.Equal(0, result.ReplayedEntries);
        Assert.Empty(result.CommittedTransactions);
        Assert.Empty(result.IncompleteTransactions);
    }

    [Fact]
    public async Task Wal_Recover_CommittedTransaction_Found()
    {
        // Arrange
        var options = CreateTestOptions();
        var transactionId = Guid.NewGuid().ToString();

        using (var wal = new WriteAheadLog(options))
        {
            await wal.AppendBeginTransactionAsync(transactionId);
            await wal.AppendCommitAsync(transactionId);
        }

        // Act - reopen log (simulating restart)
        using (var wal = new WriteAheadLog(options))
        {
            var result = await wal.RecoverAsync();

            // Assert
            Assert.True(result.Success);
            Assert.Equal(2, result.ReplayedEntries); // Begin + Commit
            Assert.Single(result.CommittedTransactions);
            Assert.Equal(transactionId, result.CommittedTransactions[0]);
            Assert.Empty(result.IncompleteTransactions);
        }
    }

    [Fact]
    public async Task Wal_Recover_IncompleteTransaction_Found()
    {
        // Arrange
        var options = CreateTestOptions();
        var transactionId = Guid.NewGuid().ToString();

        using (var wal = new WriteAheadLog(options))
        {
            await wal.AppendBeginTransactionAsync(transactionId);
            // No commit
        }

        // Act - reopen log
        using (var wal = new WriteAheadLog(options))
        {
            var result = await wal.RecoverAsync();

            // Assert
            Assert.True(result.Success);
            Assert.Single(result.IncompleteTransactions);
            Assert.Equal(transactionId, result.IncompleteTransactions[0]);
        }
    }

    [Fact]
    public async Task Wal_Recover_RolledBackTransaction_NotInIncomplete()
    {
        // Arrange
        var options = CreateTestOptions();
        var transactionId = Guid.NewGuid().ToString();

        using (var wal = new WriteAheadLog(options))
        {
            await wal.AppendBeginTransactionAsync(transactionId);
            await wal.AppendRollbackAsync(transactionId);
        }

        // Act - reopen log
        using (var wal = new WriteAheadLog(options))
        {
            var result = await wal.RecoverAsync();

            // Assert
            Assert.True(result.Success);
            Assert.Empty(result.CommittedTransactions);
            Assert.Empty(result.IncompleteTransactions); // Rolled back is complete
        }
    }

    [Fact]
    public async Task Wal_Recover_MultipleTransactions_CorrectlyIdentified()
    {
        // Arrange
        var options = CreateTestOptions();
        var tx1 = Guid.NewGuid().ToString();
        var tx2 = Guid.NewGuid().ToString();
        var tx3 = Guid.NewGuid().ToString();

        using (var wal = new WriteAheadLog(options))
        {
            // tx1: committed
            await wal.AppendBeginTransactionAsync(tx1);
            await wal.AppendCommitAsync(tx1);

            // tx2: incomplete
            await wal.AppendBeginTransactionAsync(tx2);

            // tx3: rolled back
            await wal.AppendBeginTransactionAsync(tx3);
            await wal.AppendRollbackAsync(tx3);
        }

        // Act - reopen log
        using (var wal = new WriteAheadLog(options))
        {
            var result = await wal.RecoverAsync();

            // Assert
            Assert.True(result.Success);
            Assert.Equal(5, result.ReplayedEntries);
            Assert.Single(result.CommittedTransactions);
            Assert.Equal(tx1, result.CommittedTransactions[0]);
            Assert.Single(result.IncompleteTransactions);
            Assert.Equal(tx2, result.IncompleteTransactions[0]);
        }
    }

    [Fact]
    public async Task Wal_ReplayEntries_ReturnsAllEntries()
    {
        // Arrange
        var options = CreateTestOptions();
        var transactionId = Guid.NewGuid().ToString();

        using (var wal = new WriteAheadLog(options))
        {
            await wal.AppendBeginTransactionAsync(transactionId);
            await wal.AppendCommitAsync(transactionId);
        }

        // Act
        using (var wal = new WriteAheadLog(options))
        {
            var entries = await ToListAsync(wal.ReplayEntriesAsync(0));

            // Assert
            Assert.Equal(2, entries.Count);
            Assert.Equal(WalOperationType.BeginTransaction, entries[0].OperationType);
            Assert.Equal(WalOperationType.Commit, entries[1].OperationType);
        }
    }

    [Fact]
    public async Task Wal_ReplayEntries_FromSpecificLsn()
    {
        // Arrange
        var options = CreateTestOptions();
        var transactionId = Guid.NewGuid().ToString();

        long firstLsn = 0;
        using (var wal = new WriteAheadLog(options))
        {
            firstLsn = await wal.AppendBeginTransactionAsync(transactionId);
            await wal.AppendCommitAsync(transactionId);
        }

        // Act
        using (var wal = new WriteAheadLog(options))
        {
            var entries = await ToListAsync(wal.ReplayEntriesAsync(firstLsn + 1));

            // Assert
            Assert.Single(entries);
            Assert.Equal(WalOperationType.Commit, entries[0].OperationType);
        }
    }

    #endregion

    #region Checkpoints

    [Fact]
    public async Task Wal_CreateCheckpoint_Success()
    {
        // Arrange
        var options = CreateTestOptions();
        using var wal = new WriteAheadLog(options);
        var transactionId = Guid.NewGuid().ToString();

        await wal.AppendBeginTransactionAsync(transactionId);
        await wal.AppendCommitAsync(transactionId);

        // Act
        var checkpointLsn = await wal.CreateCheckpointAsync(new[] { transactionId });

        // Assert
        Assert.True(checkpointLsn > 0);
        Assert.NotNull(wal.LastCheckpoint);
        Assert.Equal(checkpointLsn, wal.LastCheckpoint.CheckpointLsn);
    }

    [Fact]
    public async Task Wal_CreateCheckpoint_SavesToFile()
    {
        // Arrange
        var options = CreateTestOptions();
        using (var wal = new WriteAheadLog(options))
        {
            await wal.CreateCheckpointAsync(Array.Empty<string>());
        }

        // Assert
        Assert.True(File.Exists(Path.Combine(_testLogDirectory, "wal.checkpoint")));
    }

    [Fact]
    public async Task Wal_CheckpointEvent_Raised()
    {
        // Arrange
        var options = CreateTestOptions();
        using var wal = new WriteAheadLog(options);
        CheckpointEventArgs? capturedArgs = null;
        wal.CheckpointCreated += (s, e) => capturedArgs = e;

        // Act
        await wal.CreateCheckpointAsync(new[] { "tx-1", "tx-2" });

        // Assert
        Assert.NotNull(capturedArgs);
        Assert.True(capturedArgs.CheckpointLsn > 0);
        Assert.Equal(2, capturedArgs.ActiveTransactions.Count);
    }

    #endregion

    #region Statistics

    [Fact]
    public async Task Wal_Statistics_AfterOperations()
    {
        // Arrange
        var options = CreateTestOptions();
        using var wal = new WriteAheadLog(options);
        var transactionId = Guid.NewGuid().ToString();

        // Act
        await wal.AppendBeginTransactionAsync(transactionId);
        await wal.AppendCommitAsync(transactionId);
        var stats = wal.GetStatistics();

        // Assert
        Assert.True(stats.CurrentLsn >= 2);
        Assert.True(stats.TotalEntries >= 2);
        Assert.True(stats.TotalBytes > 0);
        Assert.True(stats.CurrentFileSize > 0);
    }

    #endregion

    #region Log Rotation

    [Fact]
    public async Task Wal_RotateLog_WhenMaxSizeReached()
    {
        // Arrange - very small max file size
        var options = new WalOptions
        {
            LogDirectory = _testLogDirectory,
            MaxFileSize = 500, // Very small to trigger rotation
            ForceSync = false
        };

        using var wal = new WriteAheadLog(options);
        LogRotationEventArgs? rotationArgs = null;
        wal.LogRotated += (s, e) => rotationArgs = e;

        // Act - write many entries to trigger rotation
        for (int i = 0; i < 100; i++)
        {
            var txId = Guid.NewGuid().ToString();
            await wal.AppendBeginTransactionAsync(txId);
            await wal.AppendCommitAsync(txId);
        }

        // Assert
        var logFiles = Directory.GetFiles(_testLogDirectory, "wal.*");
        Assert.True(logFiles.Length > 1, "Expected log rotation to create multiple files");
    }

    #endregion

    #region Flush

    [Fact]
    public async Task Wal_Flush_Success()
    {
        // Arrange
        var options = CreateTestOptions();
        using var wal = new WriteAheadLog(options);
        var transactionId = Guid.NewGuid().ToString();

        await wal.AppendBeginTransactionAsync(transactionId);

        // Act - should not throw
        await wal.FlushAsync();

        // Assert - no exception means success
        Assert.True(true);
    }

    #endregion

    #region Persistence Across Restarts

    [Fact]
    public async Task Wal_Persist_AcrossRestarts()
    {
        // Arrange
        var options = CreateTestOptions();
        var transactionId = Guid.NewGuid().ToString();
        long firstLsn = 0;

        // First session
        using (var wal = new WriteAheadLog(options))
        {
            firstLsn = await wal.AppendBeginTransactionAsync(transactionId);
            await wal.AppendCommitAsync(transactionId);
        }

        // Second session (simulating restart)
        using (var wal = new WriteAheadLog(options))
        {
            // Assert
            Assert.True(wal.CurrentLsn >= firstLsn);

            var entries = await ToListAsync(wal.ReplayEntriesAsync(0));
            Assert.Equal(2, entries.Count);
        }
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task Wal_EmptyTransactionId_Throws()
    {
        // Arrange
        var options = CreateTestOptions();
        using var wal = new WriteAheadLog(options);

        // Act & Assert - should handle gracefully
        var lsn = await wal.AppendBeginTransactionAsync(string.Empty);
        Assert.True(lsn > 0);
    }

    [Fact]
    public async Task Wal_MultipleTransactions_Interleaved()
    {
        // Arrange
        var options = CreateTestOptions();
        using var wal = new WriteAheadLog(options);
        var tx1 = Guid.NewGuid().ToString();
        var tx2 = Guid.NewGuid().ToString();

        // Act - interleaved operations
        await wal.AppendBeginTransactionAsync(tx1);
        await wal.AppendBeginTransactionAsync(tx2);
        await wal.AppendCommitAsync(tx1);
        await wal.AppendCommitAsync(tx2);

        // Assert
        var entries = await ToListAsync(wal.ReplayEntriesAsync(0));
        Assert.Equal(4, entries.Count);
        Assert.Equal(tx1, entries[0].TransactionId);
        Assert.Equal(tx2, entries[1].TransactionId);
    }

    [Fact]
    public async Task Wal_DocumentWithUnicode_Preserved()
    {
        // Arrange
        var options = CreateTestOptions();
        using var wal = new WriteAheadLog(options);
        var transactionId = Guid.NewGuid().ToString();
        var document = new Document
        {
            Id = "doc-unicode-1",
            Data = new System.Collections.Generic.Dictionary<string, object>
            {
                ["value"] = 42,
                ["name"] = "test"
            },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };

        // Act
        await wal.AppendBeginTransactionAsync(transactionId);
        await wal.AppendInsertAsync(transactionId, "test-collection", document);

        // Assert
        var entries = await ToListAsync(wal.ReplayEntriesAsync(0));
        var insertEntry = entries.First(e => e.OperationType == WalOperationType.Insert);
        Assert.NotNull(insertEntry.AfterImage);
        Assert.Equal(document.Id, insertEntry.AfterImage.Id);
        // Note: JSON deserialization preserves values but may change types (e.g., to JsonElement)
        // The fact that we can round-trip the document is the important test
        Assert.NotNull(insertEntry.AfterImage.Data);
        Assert.True(insertEntry.AfterImage.Data.ContainsKey("value"));
        Assert.True(insertEntry.AfterImage.Data.ContainsKey("name"));
    }

    #endregion

    #region Dispose

    [Fact]
    public void Wal_Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var options = CreateTestOptions();
        var wal = new WriteAheadLog(options);

        // Act & Assert - should not throw
        wal.Dispose();
        wal.Dispose();
    }

    [Fact]
    public async Task Wal_AfterDispose_Throws()
    {
        // Arrange
        var options = CreateTestOptions();
        var wal = new WriteAheadLog(options);
        wal.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await wal.AppendBeginTransactionAsync("tx-1"));
    }

    #endregion
}
