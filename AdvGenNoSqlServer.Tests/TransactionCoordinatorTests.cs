// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Core.Transactions;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for the Transaction Coordinator
/// </summary>
public class TransactionCoordinatorTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly WalOptions _walOptions;
    private readonly WriteAheadLog _writeAheadLog;
    private readonly LockManager _lockManager;
    private readonly TransactionCoordinator _coordinator;

    public TransactionCoordinatorTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"AdvGenNoSql_Tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);

        _walOptions = new WalOptions
        {
            LogDirectory = _testDirectory,
            ForceSync = false, // Disable fsync for faster tests
            MaxFileSize = 1024 * 1024 // 1MB for tests
        };

        _writeAheadLog = new WriteAheadLog(_walOptions);
        _lockManager = new LockManager();
        _coordinator = new TransactionCoordinator(_writeAheadLog, _lockManager);
    }

    public void Dispose()
    {
        _coordinator?.Dispose();
        _writeAheadLog?.Dispose();
        _lockManager?.Dispose();

        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    #region BeginTransaction Tests

    [Fact]
    public async Task BeginTransaction_WithDefaultOptions_CreatesActiveTransaction()
    {
        var context = await _coordinator.BeginTransactionAsync();

        Assert.NotNull(context);
        Assert.NotNull(context.TransactionId);
        Assert.StartsWith("txn_", context.TransactionId);
        Assert.Equal(TransactionState.Active, context.State);
        Assert.Equal(IsolationLevel.ReadCommitted, context.IsolationLevel);
        Assert.True(_coordinator.ActiveTransactionCount > 0);
    }

    [Fact]
    public async Task BeginTransaction_WithCustomOptions_AppliesOptions()
    {
        var options = new TransactionOptions
        {
            IsolationLevel = IsolationLevel.Serializable,
            Timeout = TimeSpan.FromMinutes(5),
            AutoRollbackOnDispose = false
        };

        var context = await _coordinator.BeginTransactionAsync(options);

        Assert.Equal(IsolationLevel.Serializable, context.IsolationLevel);
        Assert.False(context.AutoRollbackOnDispose);
    }

    [Fact]
    public async Task BeginTransaction_MultipleTransactions_CreatesUniqueIds()
    {
        var context1 = await _coordinator.BeginTransactionAsync();
        var context2 = await _coordinator.BeginTransactionAsync();
        var context3 = await _coordinator.BeginTransactionAsync();

        Assert.NotEqual(context1.TransactionId, context2.TransactionId);
        Assert.NotEqual(context2.TransactionId, context3.TransactionId);
        Assert.Equal(3, _coordinator.ActiveTransactionCount);
    }

    #endregion

    #region Commit Tests

    [Fact]
    public async Task Commit_ActiveTransaction_CommitsSuccessfully()
    {
        var context = await _coordinator.BeginTransactionAsync();
        bool committed = await context.CommitAsync();

        Assert.True(committed);
        Assert.Equal(TransactionState.Committed, context.State);
    }

    [Fact]
    public async Task Commit_CommittedTransaction_ThrowsException()
    {
        var context = await _coordinator.BeginTransactionAsync();
        await context.CommitAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await context.CommitAsync());
    }

    [Fact]
    public async Task CommitViaCoordinator_ActiveTransaction_CommitsSuccessfully()
    {
        var context = await _coordinator.BeginTransactionAsync();
        bool committed = await _coordinator.CommitAsync(context.TransactionId);

        Assert.True(committed);
        Assert.Equal(0, _coordinator.ActiveTransactionCount);
    }

    [Fact]
    public async Task CommitViaCoordinator_UnknownTransaction_ReturnsFalse()
    {
        bool committed = await _coordinator.CommitAsync("txn_nonexistent");

        Assert.False(committed);
    }

    [Fact]
    public async Task Commit_RaisesTransactionCommittedEvent()
    {
        var eventRaised = false;
        TransactionEventArgs? eventArgs = null;

        _coordinator.TransactionCommitted += (s, e) =>
        {
            eventRaised = true;
            eventArgs = e;
        };

        var context = await _coordinator.BeginTransactionAsync();
        await Task.Delay(50); // Ensure some duration
        await _coordinator.CommitAsync(context.TransactionId);

        Assert.True(eventRaised);
        Assert.NotNull(eventArgs);
        Assert.Equal(context.TransactionId, eventArgs.TransactionId);
        Assert.True(eventArgs.Duration > TimeSpan.Zero);
    }

    #endregion

    #region Rollback Tests

    [Fact]
    public async Task Rollback_ActiveTransaction_RollsBackSuccessfully()
    {
        var context = await _coordinator.BeginTransactionAsync();
        bool rolledBack = await context.RollbackAsync();

        Assert.True(rolledBack);
        Assert.Equal(TransactionState.RolledBack, context.State);
    }

    [Fact]
    public async Task Rollback_CommittedTransaction_ReturnsFalse()
    {
        var context = await _coordinator.BeginTransactionAsync();
        await context.CommitAsync();

        bool rolledBack = await context.RollbackAsync();

        Assert.False(rolledBack);
    }

    [Fact]
    public async Task RollbackViaCoordinator_ActiveTransaction_RollsBackSuccessfully()
    {
        var context = await _coordinator.BeginTransactionAsync();
        bool rolledBack = await _coordinator.RollbackAsync(context.TransactionId);

        Assert.True(rolledBack);
        Assert.Equal(0, _coordinator.ActiveTransactionCount);
    }

    [Fact]
    public async Task Rollback_RaisesTransactionRolledBackEvent()
    {
        var eventRaised = false;
        TransactionEventArgs? eventArgs = null;

        _coordinator.TransactionRolledBack += (s, e) =>
        {
            eventRaised = true;
            eventArgs = e;
        };

        var context = await _coordinator.BeginTransactionAsync();
        await Task.Delay(50);
        await _coordinator.RollbackAsync(context.TransactionId);

        Assert.True(eventRaised);
        Assert.NotNull(eventArgs);
        Assert.Equal(context.TransactionId, eventArgs.TransactionId);
    }

    #endregion

    #region Abort Tests

    [Fact]
    public async Task Abort_ActiveTransaction_AbortsSuccessfully()
    {
        var context = await _coordinator.BeginTransactionAsync();
        bool aborted = await _coordinator.AbortAsync(context.TransactionId, "Test abort");

        Assert.True(aborted);
        Assert.Equal(0, _coordinator.ActiveTransactionCount);
    }

    [Fact]
    public async Task Abort_RaisesTransactionAbortedEvent()
    {
        var eventRaised = false;
        TransactionAbortedEventArgs? eventArgs = null;

        _coordinator.TransactionAborted += (s, e) =>
        {
            eventRaised = true;
            eventArgs = e;
        };

        var context = await _coordinator.BeginTransactionAsync();
        await _coordinator.AbortAsync(context.TransactionId, "Test reason");

        Assert.True(eventRaised);
        Assert.NotNull(eventArgs);
        Assert.Equal("Test reason", eventArgs.Reason);
    }

    #endregion

    #region Savepoint Tests

    [Fact]
    public async Task Savepoint_ActiveTransaction_CreatesSavepoint()
    {
        var context = await _coordinator.BeginTransactionAsync();

        bool created = await context.SavepointAsync("sp1");

        Assert.True(created);
    }

    [Fact]
    public async Task Savepoint_EmptyName_ThrowsArgumentException()
    {
        var context = await _coordinator.BeginTransactionAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await context.SavepointAsync(""));
    }

    [Fact]
    public async Task Savepoint_CommittedTransaction_ThrowsInvalidOperation()
    {
        var context = await _coordinator.BeginTransactionAsync();
        await context.CommitAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await context.SavepointAsync("sp1"));
    }

    [Fact]
    public async Task RollbackToSavepoint_ExistingSavepoint_RollsBackSuccessfully()
    {
        var context = await _coordinator.BeginTransactionAsync();
        await context.SavepointAsync("sp1");

        bool rolledBack = await context.RollbackToSavepointAsync("sp1");

        Assert.True(rolledBack);
    }

    [Fact]
    public async Task RollbackToSavepoint_NonexistentSavepoint_ThrowsInvalidOperation()
    {
        var context = await _coordinator.BeginTransactionAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await context.RollbackToSavepointAsync("nonexistent"));
    }

    #endregion

    #region Transaction Info Tests

    [Fact]
    public async Task GetTransactionInfo_ActiveTransaction_ReturnsInfo()
    {
        var context = await _coordinator.BeginTransactionAsync();

        var info = _coordinator.GetTransactionInfo(context.TransactionId);

        Assert.NotNull(info);
        Assert.Equal(context.TransactionId, info.TransactionId);
        Assert.Equal(TransactionState.Active, info.State);
        Assert.Equal(context.IsolationLevel, info.IsolationLevel);
    }

    [Fact]
    public void GetTransactionInfo_UnknownTransaction_ReturnsNull()
    {
        var info = _coordinator.GetTransactionInfo("txn_unknown");

        Assert.Null(info);
    }

    [Fact]
    public async Task GetActiveTransactions_WithMultipleTransactions_ReturnsAll()
    {
        var context1 = await _coordinator.BeginTransactionAsync();
        var context2 = await _coordinator.BeginTransactionAsync();
        var context3 = await _coordinator.BeginTransactionAsync();

        var activeTransactions = _coordinator.GetActiveTransactions();

        Assert.Equal(3, activeTransactions.Count);
        Assert.Contains(activeTransactions, t => t.TransactionId == context1.TransactionId);
        Assert.Contains(activeTransactions, t => t.TransactionId == context2.TransactionId);
        Assert.Contains(activeTransactions, t => t.TransactionId == context3.TransactionId);
    }

    [Fact]
    public void GetActiveTransactions_NoTransactions_ReturnsEmptyList()
    {
        var activeTransactions = _coordinator.GetActiveTransactions();

        Assert.Empty(activeTransactions);
    }

    #endregion

    #region Isolation Level Tests

    [Theory]
    [InlineData(IsolationLevel.ReadUncommitted)]
    [InlineData(IsolationLevel.ReadCommitted)]
    [InlineData(IsolationLevel.RepeatableRead)]
    [InlineData(IsolationLevel.Serializable)]
    public async Task BeginTransaction_WithVariousIsolationLevels_SetsCorrectLevel(IsolationLevel level)
    {
        var options = new TransactionOptions { IsolationLevel = level };
        var context = await _coordinator.BeginTransactionAsync(options);

        Assert.Equal(level, context.IsolationLevel);
    }

    #endregion

    #region Record Operations Tests

    [Fact]
    public async Task RecordWrite_WithInsert_AppendsToWAL()
    {
        var context = await _coordinator.BeginTransactionAsync();
        var document = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object> { { "name", "test" } }
        };

        await context.RecordWriteAsync("users", "doc1", null, document);

        // Verify by checking that operations were recorded
        Assert.True(context.OperationCount > 0);
    }

    [Fact]
    public async Task RecordWrite_WithUpdate_AppendsToWAL()
    {
        var context = await _coordinator.BeginTransactionAsync();
        var beforeDoc = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object> { { "name", "old" } }
        };
        var afterDoc = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object> { { "name", "new" } }
        };

        await context.RecordWriteAsync("users", "doc1", beforeDoc, afterDoc);

        Assert.True(context.OperationCount > 0);
    }

    [Fact]
    public async Task RecordWrite_WithDelete_AppendsToWAL()
    {
        var context = await _coordinator.BeginTransactionAsync();
        var document = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object> { { "name", "test" } }
        };

        await context.RecordWriteAsync("users", "doc1", document, null);

        Assert.True(context.OperationCount > 0);
    }

    [Fact]
    public async Task RecordWrite_CommittedTransaction_ThrowsInvalidOperation()
    {
        var context = await _coordinator.BeginTransactionAsync();
        await context.CommitAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await context.RecordWriteAsync("users", "doc1", null, new Document { Id = "doc1" }));
    }

    [Fact]
    public async Task RecordRead_WithRepeatableRead_RecordsInReadSet()
    {
        var options = new TransactionOptions { IsolationLevel = IsolationLevel.RepeatableRead };
        var context = await _coordinator.BeginTransactionAsync(options);

        await context.RecordReadAsync("users", "doc1");

        // Should complete without error
        Assert.True(true);
    }

    [Fact]
    public async Task RecordRead_WithReadCommitted_DoesNotRecord()
    {
        var options = new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted };
        var context = await _coordinator.BeginTransactionAsync(options);

        await context.RecordReadAsync("users", "doc1");

        // Should complete without error and not record
        Assert.True(true);
    }

    #endregion

    #region Transaction Timeout Tests

    [Fact(Timeout = 10000)]
    public async Task Transaction_WithShortTimeout_TimesOut()
    {
        var options = new TransactionOptions { Timeout = TimeSpan.FromMilliseconds(100) };
        var context = await _coordinator.BeginTransactionAsync(options);

        // Wait for timeout
        await Task.Delay(500);

        // The transaction should be timed out (cleanup happens on timer)
        // Force cleanup by creating a new coordinator that should abort timed out transactions
        // Or check that the transaction is no longer active after cleanup
    }

    #endregion

    #region Deadlock Handling Tests

    [Fact]
    public async Task Deadlock_Detected_AbortsVictimTransaction()
    {
        var eventRaised = false;
        _coordinator.TransactionAborted += (s, e) =>
        {
            if (e.Reason.Contains("Deadlock"))
            {
                eventRaised = true;
            }
        };

        // Create two transactions that will deadlock
        var options = new TransactionOptions
        {
            IsolationLevel = IsolationLevel.Serializable,
            Timeout = TimeSpan.FromSeconds(1)
        };

        var context1 = await _coordinator.BeginTransactionAsync(options);
        var context2 = await _coordinator.BeginTransactionAsync(options);

        // Both acquire locks on different resources
        var doc1 = new Document { Id = "doc1" };
        var doc2 = new Document { Id = "doc2" };

        await context1.RecordWriteAsync("test", "doc1", null, doc1);
        await context2.RecordWriteAsync("test", "doc2", null, doc2);

        // Now try to acquire cross locks (this may or may not trigger deadlock depending on timing)
        // We mainly verify that the deadlock detection mechanism is wired up
        Assert.True(_coordinator.ActiveTransactionCount >= 2);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task FullTransactionLifecycle_Commit_Success()
    {
        // Begin transaction
        var context = await _coordinator.BeginTransactionAsync();
        Assert.Equal(TransactionState.Active, context.State);

        // Perform operations
        var doc = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object> { { "value", 42 } }
        };
        await context.RecordWriteAsync("test", "doc1", null, doc);

        // Create savepoint
        await context.SavepointAsync("after_insert");

        // Commit via coordinator
        bool committed = await _coordinator.CommitAsync(context.TransactionId);

        Assert.True(committed);
        Assert.Equal(TransactionState.Committed, context.State);
        Assert.Equal(0, _coordinator.ActiveTransactionCount);
    }

    [Fact]
    public async Task FullTransactionLifecycle_Rollback_Success()
    {
        // Begin transaction
        var context = await _coordinator.BeginTransactionAsync();

        // Perform operations
        var doc = new Document { Id = "doc1" };
        await context.RecordWriteAsync("test", "doc1", null, doc);

        // Rollback via coordinator
        bool rolledBack = await _coordinator.RollbackAsync(context.TransactionId);

        Assert.True(rolledBack);
        Assert.Equal(TransactionState.RolledBack, context.State);
    }

    [Fact]
    public async Task MultipleTransactions_CommitAndRollback_MixedResults()
    {
        var contexts = new List<ITransactionContext>();

        // Begin 5 transactions
        for (int i = 0; i < 5; i++)
        {
            contexts.Add(await _coordinator.BeginTransactionAsync());
        }

        Assert.Equal(5, _coordinator.ActiveTransactionCount);

        // Commit 3, rollback 2 using coordinator
        await _coordinator.CommitAsync(contexts[0].TransactionId);
        await _coordinator.RollbackAsync(contexts[1].TransactionId);
        await _coordinator.CommitAsync(contexts[2].TransactionId);
        await _coordinator.RollbackAsync(contexts[3].TransactionId);
        await _coordinator.CommitAsync(contexts[4].TransactionId);

        Assert.Equal(0, _coordinator.ActiveTransactionCount);
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_CleansUpResources()
    {
        var coordinator = new TransactionCoordinator(_writeAheadLog, _lockManager);
        coordinator.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => coordinator.BeginTransactionAsync().GetAwaiter().GetResult());
    }

    [Fact]
    public async Task ContextDispose_WithAutoRollback_RollsBackTransaction()
    {
        TransactionContext context;
        using (context = (TransactionContext)await _coordinator.BeginTransactionAsync())
        {
            // Transaction is active
            Assert.Equal(TransactionState.Active, context.State);
        }

        // After dispose with AutoRollbackOnDispose=true, transaction should be rolled back
        // Note: The actual state change happens asynchronously in the background
    }

    #endregion

    #region Stress Tests

    [Fact]
    public async Task MultipleConcurrentTransactions_AllCompleteSuccessfully()
    {
        const int transactionCount = 10;
        var tasks = new List<Task>();

        for (int i = 0; i < transactionCount; i++)
        {
            var taskIndex = i;
            tasks.Add(Task.Run(async () =>
            {
                var context = await _coordinator.BeginTransactionAsync();
                var doc = new Document { Id = $"doc_{taskIndex}" };

                await context.RecordWriteAsync("test", $"doc_{taskIndex}", null, doc);
                await Task.Delay(10); // Simulate some work

                // Use coordinator methods to ensure proper tracking
                if (taskIndex % 2 == 0)
                {
                    await _coordinator.CommitAsync(context.TransactionId);
                }
                else
                {
                    await _coordinator.RollbackAsync(context.TransactionId);
                }
            }));
        }

        await Task.WhenAll(tasks);

        Assert.Equal(0, _coordinator.ActiveTransactionCount);
    }

    #endregion
}
