// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Core.Transactions;
using System.IO;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for MVCC (Multi-Version Concurrency Control) implementation
/// </summary>
public class MvccTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
            catch { }
        }
    }

    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mvcc_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private WalOptions CreateWalOptions()
    {
        return new WalOptions { LogDirectory = CreateTempDir() };
    }

    #region DocumentVersion Tests

    [Fact]
    public void DocumentVersion_Constructor_CreatesVersionWithCorrectProperties()
    {
        var doc = new Document { Id = "doc1", Data = new() { ["name"] = "Test" } };
        var version = new DocumentVersion(doc, "txn_1");

        Assert.NotEqual(Guid.Empty, version.VersionId);
        Assert.Equal(doc, version.Document);
        Assert.Equal("txn_1", version.CreatedByTransactionId);
        Assert.True(version.CreatedAt <= DateTime.UtcNow);
        Assert.Null(version.DeletedAt);
        Assert.Null(version.DeletedByTransactionId);
        Assert.False(version.IsDeleted);
        Assert.Null(version.PreviousVersion);
    }

    [Fact]
    public void DocumentVersion_Constructor_WithPreviousVersion_CreatesChain()
    {
        var doc1 = new Document { Id = "doc1", Data = new() { ["version"] = 1 } };
        var doc2 = new Document { Id = "doc1", Data = new() { ["version"] = 2 } };

        var v1 = new DocumentVersion(doc1, "txn_1");
        var v2 = new DocumentVersion(doc2, "txn_2", v1);

        Assert.Equal(v1, v2.PreviousVersion);
    }

    [Fact]
    public void DocumentVersion_MarkDeleted_SetsDeletedProperties()
    {
        var doc = new Document { Id = "doc1" };
        var version = new DocumentVersion(doc, "txn_1");

        version.MarkDeleted("txn_2");

        Assert.True(version.IsDeleted);
        Assert.NotNull(version.DeletedAt);
        Assert.Equal("txn_2", version.DeletedByTransactionId);
    }

    [Fact]
    public void DocumentVersion_MarkDeleted_AlreadyDeleted_Throws()
    {
        var doc = new Document { Id = "doc1" };
        var version = new DocumentVersion(doc, "txn_1");
        version.MarkDeleted("txn_2");

        Assert.Throws<InvalidOperationException>(() => version.MarkDeleted("txn_3"));
    }

    [Fact]
    public void DocumentVersion_IsVisibleTo_OwnTransaction_AlwaysVisible()
    {
        var doc = new Document { Id = "doc1" };
        var version = new DocumentVersion(doc, "txn_1");
        var futureTimestamp = DateTime.UtcNow.AddHours(1).Ticks;

        Assert.True(version.IsVisibleTo(futureTimestamp, "txn_1"));
    }

    [Fact]
    public void DocumentVersion_IsVisibleTo_CreatedAfterSnapshot_NotVisible()
    {
        var doc = new Document { Id = "doc1" };
        var pastTimestamp = DateTime.UtcNow.AddHours(-1).Ticks;
        var version = new DocumentVersion(doc, "txn_1");

        Assert.False(version.IsVisibleTo(pastTimestamp, "txn_2"));
    }

    [Fact]
    public void DocumentVersion_IsVisibleTo_DeletedBeforeSnapshot_NotVisible()
    {
        var doc = new Document { Id = "doc1" };
        var version = new DocumentVersion(doc, "txn_1");
        version.MarkDeleted("txn_2");

        var futureTimestamp = DateTime.UtcNow.AddHours(1).Ticks;

        Assert.False(version.IsVisibleTo(futureTimestamp, "txn_3"));
    }

    [Fact]
    public void DocumentVersion_IsVisibleTo_ValidVersion_Visible()
    {
        var doc = new Document { Id = "doc1" };
        var pastTimestamp = DateTime.UtcNow.AddHours(-1).Ticks;
        var version = new DocumentVersion(doc, "txn_1");
        var futureTimestamp = DateTime.UtcNow.AddHours(1).Ticks;

        Assert.True(version.IsVisibleTo(futureTimestamp, "txn_2"));
    }

    #endregion

    #region VersionChain Tests

    [Fact]
    public void VersionChain_Constructor_SetsProperties()
    {
        var chain = new VersionChain("collection1", "doc1");

        Assert.Equal("collection1", chain.CollectionName);
        Assert.Equal("doc1", chain.DocumentId);
        Assert.Null(chain.LatestVersion);
        Assert.Equal(0, chain.VersionCount);
    }

    [Fact]
    public void VersionChain_AddVersion_SetsLatestVersion()
    {
        var chain = new VersionChain("collection1", "doc1");
        var doc = new Document { Id = "doc1" };
        var version = new DocumentVersion(doc, "txn_1");

        chain.AddVersion(version);

        Assert.Equal(version, chain.LatestVersion);
        Assert.Equal(1, chain.VersionCount);
    }

    [Fact]
    public void VersionChain_AddVersion_MultipleVersions_CreatesChain()
    {
        var chain = new VersionChain("collection1", "doc1");
        var doc1 = new Document { Id = "doc1", Data = new() { ["v"] = 1 } };
        var doc2 = new Document { Id = "doc1", Data = new() { ["v"] = 2 } };

        var v1 = new DocumentVersion(doc1, "txn_1");
        chain.AddVersion(v1);

        var v2 = new DocumentVersion(doc2, "txn_2", v1);
        chain.AddVersion(v2);

        Assert.Equal(v2, chain.LatestVersion);
        Assert.Equal(2, chain.VersionCount);
        Assert.Equal(v1, v2.PreviousVersion);
    }

    [Fact]
    public void VersionChain_GetVisibleVersion_ReturnsCorrectVersion()
    {
        var chain = new VersionChain("collection1", "doc1");
        var doc1 = new Document { Id = "doc1", Data = new() { ["v"] = 1 } };
        var doc2 = new Document { Id = "doc1", Data = new() { ["v"] = 2 } };

        // Create first version
        var v1 = new DocumentVersion(doc1, "txn_1");
        var timestamp1 = v1.CreatedAt.Ticks;
        chain.AddVersion(v1);

        // Create second version
        var v2 = new DocumentVersion(doc2, "txn_2", v1);
        var timestamp2 = v2.CreatedAt.Ticks;
        chain.AddVersion(v2);

        // Reading with timestamp1 should see v1
        var result1 = chain.GetVisibleVersion(timestamp1, "txn_3");
        Assert.Equal(v1, result1);

        // Reading with timestamp2 should see v2
        var result2 = chain.GetVisibleVersion(timestamp2, "txn_3");
        Assert.Equal(v2, result2);
    }

    [Fact]
    public void VersionChain_GetVisibleVersion_DeletedVersion_NotVisible()
    {
        var chain = new VersionChain("collection1", "doc1");
        var doc = new Document { Id = "doc1" };
        var version = new DocumentVersion(doc, "txn_1");
        chain.AddVersion(version);
        version.MarkDeleted("txn_2");

        // Use a timestamp after the deletion
        var futureTimestamp = version.DeletedAt.Value.Ticks + 1;
        var result = chain.GetVisibleVersion(futureTimestamp, "txn_3");

        Assert.Null(result);
    }

    [Fact]
    public void VersionChain_MarkLatestDeleted_SetsDeleted()
    {
        var chain = new VersionChain("collection1", "doc1");
        var doc = new Document { Id = "doc1" };
        var version = new DocumentVersion(doc, "txn_1");
        chain.AddVersion(version);

        var result = chain.MarkLatestDeleted("txn_2");

        Assert.True(result);
        Assert.True(version.IsDeleted);
    }

    [Fact]
    public void VersionChain_GetAllVersions_ReturnsAllVersions()
    {
        var chain = new VersionChain("collection1", "doc1");
        var doc1 = new Document { Id = "doc1" };
        var doc2 = new Document { Id = "doc1" };

        var v1 = new DocumentVersion(doc1, "txn_1");
        chain.AddVersion(v1);
        var v2 = new DocumentVersion(doc2, "txn_2", v1);
        chain.AddVersion(v2);

        var allVersions = chain.GetAllVersions();

        Assert.Equal(2, allVersions.Count);
        Assert.Contains(v1, allVersions);
        Assert.Contains(v2, allVersions);
    }

    #endregion

    #region MvccSnapshot Tests

    [Fact]
    public void MvccSnapshot_Constructor_SetsProperties()
    {
        var activeTxns = new[] { "txn_1", "txn_2" };
        var snapshot = new MvccSnapshot(12345, "txn_3", activeTxns);

        Assert.Equal(12345, snapshot.ReadTimestamp);
        Assert.Equal("txn_3", snapshot.TransactionId);
        Assert.True(snapshot.ActiveTransactionsAtStart.SetEquals(new HashSet<string>(activeTxns)));
        Assert.True(snapshot.CreatedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void MvccSnapshot_IsVersionVisible_OwnTransaction_ReturnsTrue()
    {
        var snapshot = new MvccSnapshot(12345, "txn_1", Array.Empty<string>());

        Assert.True(snapshot.IsVersionVisible("txn_1", DateTime.UtcNow));
    }

    [Fact]
    public void MvccSnapshot_IsVersionVisible_CreatedAfterSnapshot_ReturnsFalse()
    {
        var pastTime = DateTime.UtcNow.AddHours(-1);
        var snapshot = new MvccSnapshot(pastTime.Ticks, "txn_1", Array.Empty<string>());

        Assert.False(snapshot.IsVersionVisible("txn_2", DateTime.UtcNow));
    }

    [Fact]
    public void MvccSnapshot_IsVersionVisible_ActiveTransaction_ReturnsFalse()
    {
        var pastTime = DateTime.UtcNow.AddHours(-1);
        var snapshot = new MvccSnapshot(DateTime.UtcNow.Ticks, "txn_1", new[] { "txn_2" });

        Assert.False(snapshot.IsVersionVisible("txn_2", pastTime));
    }

    [Fact]
    public void MvccSnapshot_IsVersionVisible_CommittedTransaction_ReturnsTrue()
    {
        var pastTime = DateTime.UtcNow.AddHours(-1);
        var snapshot = new MvccSnapshot(DateTime.UtcNow.Ticks, "txn_1", Array.Empty<string>());

        Assert.True(snapshot.IsVersionVisible("txn_2", pastTime));
    }

    #endregion

    #region MvccTimestamp Tests

    [Fact]
    public void MvccTimestamp_Next_ReturnsIncrementingValues()
    {
        var t1 = MvccTimestamp.Next();
        var t2 = MvccTimestamp.Next();
        var t3 = MvccTimestamp.Next();

        Assert.True(t1 < t2);
        Assert.True(t2 < t3);
    }

    [Fact]
    public void MvccTimestamp_Current_ReturnsCurrentValue()
    {
        var before = MvccTimestamp.Current();
        MvccTimestamp.Next();
        var after = MvccTimestamp.Current();

        Assert.True(before < after);
    }

    #endregion

    #region MvccDocumentStore Tests

    [Fact]
    public async Task MvccDocumentStore_CreateCollection_CreatesNewCollection()
    {
        var store = new MvccDocumentStore();

        await store.CreateCollectionAsync("test");

        var collections = await store.GetCollectionsAsync();
        Assert.Contains("test", collections);
    }

    [Fact]
    public async Task MvccDocumentStore_CreateCollection_Duplicate_Throws()
    {
        var store = new MvccDocumentStore();
        await store.CreateCollectionAsync("test");

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateCollectionAsync("test"));
    }

    [Fact]
    public async Task MvccDocumentStore_DropCollection_RemovesCollection()
    {
        var store = new MvccDocumentStore();
        await store.CreateCollectionAsync("test");

        var result = await store.DropCollectionAsync("test");

        Assert.True(result);
        var collections = await store.GetCollectionsAsync();
        Assert.DoesNotContain("test", collections);
    }

    [Fact]
    public async Task MvccDocumentStore_InsertVersion_CreatesNewDocument()
    {
        var store = new MvccDocumentStore();
        await store.CreateCollectionAsync("test");
        var doc = new Document { Id = "doc1", Data = new() { ["name"] = "Test" } };
        var snapshot = new MvccSnapshot(MvccTimestamp.Next(), "txn_1", Array.Empty<string>());

        var version = await store.InsertVersionAsync("test", doc, "txn_1");

        Assert.NotNull(version);
        Assert.Equal("doc1", version.Document.Id);

        var retrieved = await store.GetVisibleVersionAsync("test", "doc1", snapshot);
        Assert.NotNull(retrieved);
        Assert.Equal("doc1", retrieved.Document.Id);
    }

    [Fact]
    public async Task MvccDocumentStore_InsertVersion_Duplicate_Throws()
    {
        var store = new MvccDocumentStore();
        await store.CreateCollectionAsync("test");
        var doc = new Document { Id = "doc1" };
        await store.InsertVersionAsync("test", doc, "txn_1");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.InsertVersionAsync("test", doc, "txn_2"));
    }

    [Fact]
    public async Task MvccDocumentStore_UpdateVersion_CreatesNewVersion()
    {
        var store = new MvccDocumentStore();
        await store.CreateCollectionAsync("test");
        var doc1 = new Document { Id = "doc1", Data = new() { ["v"] = 1 } };
        var doc2 = new Document { Id = "doc1", Data = new() { ["v"] = 2 } };

        var v1 = await store.InsertVersionAsync("test", doc1, "txn_1");
        var timestamp1 = v1.CreatedAt.Ticks;

        var v2 = await store.UpdateVersionAsync("test", "doc1", doc2, "txn_2");
        var timestamp2 = v2!.CreatedAt.Ticks;

        Assert.NotNull(v2);

        // Old snapshot sees old version
        var oldSnapshot = new MvccSnapshot(timestamp1, "txn_read", Array.Empty<string>());
        var oldVersion = await store.GetVisibleVersionAsync("test", "doc1", oldSnapshot);
        Assert.Equal(1, oldVersion?.Document.Data["v"]);

        // New snapshot sees new version
        var newSnapshot = new MvccSnapshot(timestamp2, "txn_read", Array.Empty<string>());
        var newVersion = await store.GetVisibleVersionAsync("test", "doc1", newSnapshot);
        Assert.Equal(2, newVersion?.Document.Data["v"]);
    }

    [Fact]
    public async Task MvccDocumentStore_DeleteVersion_MarksAsDeleted()
    {
        var store = new MvccDocumentStore();
        await store.CreateCollectionAsync("test");
        var doc = new Document { Id = "doc1" };
        var v1 = await store.InsertVersionAsync("test", doc, "txn_1");

        // Get timestamp after insert but before delete
        var afterInsertTimestamp = MvccTimestamp.Next();
        
        var result = await store.DeleteVersionAsync("test", "doc1", "txn_2");

        Assert.True(result);

        // Use a future timestamp that is definitely after deletion
        var futureTimestamp = MvccTimestamp.Next();
        var snapshot = new MvccSnapshot(futureTimestamp, "txn_read", Array.Empty<string>());
        var version = await store.GetVisibleVersionAsync("test", "doc1", snapshot);
        Assert.Null(version); // Deleted document not visible
    }

    [Fact]
    public async Task MvccDocumentStore_Exists_ReturnsCorrectValue()
    {
        var store = new MvccDocumentStore();
        await store.CreateCollectionAsync("test");
        var doc = new Document { Id = "doc1" };
        await store.InsertVersionAsync("test", doc, "txn_1");

        var snapshot = new MvccSnapshot(MvccTimestamp.Next(), "txn_read", Array.Empty<string>());

        Assert.True(await store.ExistsAsync("test", "doc1", snapshot));
        Assert.False(await store.ExistsAsync("test", "nonexistent", snapshot));
    }

    [Fact]
    public async Task MvccDocumentStore_ClearCollection_RemovesAllDocuments()
    {
        var store = new MvccDocumentStore();
        await store.CreateCollectionAsync("test");
        await store.InsertVersionAsync("test", new Document { Id = "doc1" }, "txn_1");
        await store.InsertVersionAsync("test", new Document { Id = "doc2" }, "txn_1");

        await store.ClearCollectionAsync("test");

        var snapshot = new MvccSnapshot(MvccTimestamp.Next(), "txn_read", Array.Empty<string>());
        var versions = await store.GetVisibleVersionsAsync("test", snapshot);
        Assert.Empty(versions);
    }

    [Fact]
    public async Task MvccDocumentStore_GetCollectionStats_ReturnsCorrectStats()
    {
        var store = new MvccDocumentStore();
        await store.CreateCollectionAsync("test");
        await store.InsertVersionAsync("test", new Document { Id = "doc1" }, "txn_1");
        await store.InsertVersionAsync("test", new Document { Id = "doc2" }, "txn_1");

        // Update one document to create multiple versions
        await store.UpdateVersionAsync("test", "doc1", new Document { Id = "doc1", Data = new() { ["v"] = 2 } }, "txn_2");

        var stats = store.GetCollectionStats("test");

        Assert.Equal("test", stats.CollectionName);
        Assert.Equal(2, stats.DocumentCount);
        Assert.Equal(3, stats.TotalVersionCount);
    }

    #endregion

    #region MvccTransactionCoordinator Tests

    [Fact]
    public async Task MvccCoordinator_BeginTransaction_CreatesTransaction()
    {
        var store = new MvccDocumentStore();
        var walOptions = CreateWalOptions();
        using var wal = new WriteAheadLog(walOptions);
        using var coordinator = new MvccTransactionCoordinator(store, wal);

        var txn = await coordinator.BeginTransactionAsync();

        Assert.NotNull(txn);
        Assert.StartsWith("mvcc_txn_", txn.TransactionId);
        Assert.Equal(TransactionState.Active, txn.State);
        Assert.Equal(1, coordinator.ActiveTransactionCount);
    }

    [Fact]
    public async Task MvccCoordinator_CommitTransaction_CommitsSuccessfully()
    {
        var store = new MvccDocumentStore();
        var walOptions = CreateWalOptions();
        using var wal = new WriteAheadLog(walOptions);
        using var coordinator = new MvccTransactionCoordinator(store, wal);

        var txn = await coordinator.BeginTransactionAsync();
        var committed = await coordinator.CommitAsync(txn.TransactionId);

        Assert.True(committed);
        Assert.Equal(TransactionState.Committed, txn.State);
        Assert.Equal(0, coordinator.ActiveTransactionCount);
    }

    [Fact]
    public async Task MvccCoordinator_RollbackTransaction_RollsBackSuccessfully()
    {
        var store = new MvccDocumentStore();
        var walOptions = CreateWalOptions();
        using var wal = new WriteAheadLog(walOptions);
        using var coordinator = new MvccTransactionCoordinator(store, wal);

        var txn = await coordinator.BeginTransactionAsync();
        var rolledBack = await coordinator.RollbackAsync(txn.TransactionId);

        Assert.True(rolledBack);
        Assert.Equal(TransactionState.RolledBack, txn.State);
        Assert.Equal(0, coordinator.ActiveTransactionCount);
    }

    [Fact]
    public async Task MvccCoordinator_GetTransactionInfo_ReturnsInfo()
    {
        var store = new MvccDocumentStore();
        var walOptions = CreateWalOptions();
        using var wal = new WriteAheadLog(walOptions);
        using var coordinator = new MvccTransactionCoordinator(store, wal);

        var txn = await coordinator.BeginTransactionAsync();
        var info = coordinator.GetTransactionInfo(txn.TransactionId);

        Assert.NotNull(info);
        Assert.Equal(txn.TransactionId, info.TransactionId);
        Assert.Equal(TransactionState.Active, info.State);
    }

    [Fact]
    public async Task MvccCoordinator_GetActiveTransactions_ReturnsAllActive()
    {
        var store = new MvccDocumentStore();
        var walOptions = CreateWalOptions();
        using var wal = new WriteAheadLog(walOptions);
        using var coordinator = new MvccTransactionCoordinator(store, wal);

        var txn1 = await coordinator.BeginTransactionAsync();
        var txn2 = await coordinator.BeginTransactionAsync();

        var active = coordinator.GetActiveTransactions();

        Assert.Equal(2, active.Count);
    }

    #endregion

    #region MvccTransactionContext Tests

    [Fact]
    public async Task MvccContext_GetAsync_ReturnsDocument()
    {
        var store = new MvccDocumentStore();
        await store.CreateCollectionAsync("test");
        await store.InsertVersionAsync("test", new Document { Id = "doc1", Data = new() { ["name"] = "Test" } }, "txn_setup");

        var walOptions = CreateWalOptions();
        using var wal = new WriteAheadLog(walOptions);
        var context = new MvccTransactionContext("txn_1", new TransactionOptions(), store, wal, Array.Empty<string>());

        var doc = await context.GetAsync("test", "doc1");

        Assert.NotNull(doc);
        Assert.Equal("doc1", doc.Id);
        Assert.Equal("Test", doc.Data["name"]);
    }

    [Fact]
    public async Task MvccContext_GetAsync_NonExistent_ReturnsNull()
    {
        var store = new MvccDocumentStore();
        await store.CreateCollectionAsync("test");

        var walOptions = CreateWalOptions();
        using var wal = new WriteAheadLog(walOptions);
        var context = new MvccTransactionContext("txn_1", new TransactionOptions(), store, wal, Array.Empty<string>());

        var doc = await context.GetAsync("test", "nonexistent");

        Assert.Null(doc);
    }

    [Fact]
    public async Task MvccContext_InsertAsync_CreatesDocument()
    {
        var store = new MvccDocumentStore();
        await store.CreateCollectionAsync("test");

        var walOptions = CreateWalOptions();
        using var wal = new WriteAheadLog(walOptions);
        var context = new MvccTransactionContext("txn_1", new TransactionOptions(), store, wal, Array.Empty<string>());

        var doc = new Document { Id = "doc1", Data = new() { ["name"] = "Test" } };
        var inserted = await context.InsertAsync("test", doc);

        Assert.NotNull(inserted);
        Assert.Equal(1, context.OperationCount);
    }

    [Fact]
    public async Task MvccContext_InsertAsync_Duplicate_Throws()
    {
        var store = new MvccDocumentStore();
        await store.CreateCollectionAsync("test");
        await store.InsertVersionAsync("test", new Document { Id = "doc1" }, "txn_setup");

        var walOptions = CreateWalOptions();
        using var wal = new WriteAheadLog(walOptions);
        var context = new MvccTransactionContext("txn_1", new TransactionOptions(), store, wal, Array.Empty<string>());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.InsertAsync("test", new Document { Id = "doc1" }));
    }

    [Fact]
    public async Task MvccContext_UpdateAsync_UpdatesDocument()
    {
        var store = new MvccDocumentStore();
        await store.CreateCollectionAsync("test");
        await store.InsertVersionAsync("test", new Document { Id = "doc1", Data = new() { ["v"] = 1 } }, "txn_setup");

        var walOptions = CreateWalOptions();
        using var wal = new WriteAheadLog(walOptions);
        var context = new MvccTransactionContext("txn_1", new TransactionOptions(), store, wal, Array.Empty<string>());

        var updated = await context.UpdateAsync("test", new Document { Id = "doc1", Data = new() { ["v"] = 2 } });

        Assert.NotNull(updated);
        Assert.Equal(1, context.OperationCount);
    }

    [Fact]
    public async Task MvccContext_DeleteAsync_DeletesDocument()
    {
        var store = new MvccDocumentStore();
        await store.CreateCollectionAsync("test");
        await store.InsertVersionAsync("test", new Document { Id = "doc1" }, "txn_setup");

        var walOptions = CreateWalOptions();
        using var wal = new WriteAheadLog(walOptions);
        var context = new MvccTransactionContext("txn_1", new TransactionOptions(), store, wal, Array.Empty<string>());

        var deleted = await context.DeleteAsync("test", "doc1");

        Assert.True(deleted);
        Assert.Equal(1, context.OperationCount);
    }

    [Fact]
    public void MvccContext_IsTimedOut_NotExpired_ReturnsFalse()
    {
        var store = new MvccDocumentStore();
        var walOptions = CreateWalOptions();
        using var wal = new WriteAheadLog(walOptions);
        var options = new TransactionOptions { Timeout = TimeSpan.FromMinutes(1) };
        var context = new MvccTransactionContext("txn_1", options, store, wal, Array.Empty<string>());

        Assert.False(context.IsTimedOut());
    }

    [Fact]
    public async Task MvccContext_IsTimedOut_Expired_ReturnsTrue()
    {
        var store = new MvccDocumentStore();
        var walOptions = CreateWalOptions();
        using var wal = new WriteAheadLog(walOptions);
        var options = new TransactionOptions { Timeout = TimeSpan.FromMilliseconds(1) };
        var context = new MvccTransactionContext("txn_1", options, store, wal, Array.Empty<string>());

        await Task.Delay(10);

        Assert.True(context.IsTimedOut());
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task Mvcc_ReadYourOwnWrites_SeesOwnChanges()
    {
        var store = new MvccDocumentStore();
        await store.CreateCollectionAsync("test");

        var walOptions = CreateWalOptions();
        using var wal = new WriteAheadLog(walOptions);
        using var coordinator = new MvccTransactionCoordinator(store, wal);

        var txn = await coordinator.BeginTransactionAsync();
        var mvccTxn = (MvccTransactionContext)txn;

        // Insert
        await mvccTxn.InsertAsync("test", new Document { Id = "doc1", Data = new() { ["v"] = 1 } });

        // Should see own write
        var doc = await mvccTxn.GetAsync("test", "doc1");
        Assert.NotNull(doc);
        Assert.Equal(1, doc.Data["v"]);

        // Update
        await mvccTxn.UpdateAsync("test", new Document { Id = "doc1", Data = new() { ["v"] = 2 } });

        // Should see updated value
        doc = await mvccTxn.GetAsync("test", "doc1");
        Assert.Equal(2, doc.Data["v"]);

        await coordinator.CommitAsync(txn.TransactionId);
    }

    [Fact]
    public async Task Mvcc_SnapshotIsolation_OtherTransactionChangesNotVisible()
    {
        var store = new MvccDocumentStore();
        await store.CreateCollectionAsync("test");

        var walOptions = CreateWalOptions();
        using var wal = new WriteAheadLog(walOptions);
        using var coordinator = new MvccTransactionCoordinator(store, wal);

        // Setup: insert initial document
        var setupTxn = (MvccTransactionContext)await coordinator.BeginTransactionAsync();
        await setupTxn.InsertAsync("test", new Document { Id = "doc1", Data = new() { ["v"] = 1 } });
        await coordinator.CommitAsync(setupTxn.TransactionId);

        // Start two new transactions
        var txn1 = (MvccTransactionContext)await coordinator.BeginTransactionAsync();
        var txn2 = (MvccTransactionContext)await coordinator.BeginTransactionAsync();

        // txn1 updates the document
        await txn1.UpdateAsync("test", new Document { Id = "doc1", Data = new() { ["v"] = 2 } });
        await coordinator.CommitAsync(txn1.TransactionId);

        // txn2 should still see the old value (snapshot isolation)
        var doc = await txn2.GetAsync("test", "doc1");
        Assert.Equal(1, doc?.Data["v"]);

        await coordinator.RollbackAsync(txn2.TransactionId);
    }

    [Fact(Skip = "Write-write conflict detection requires implementing visibility check in UpdateAsync")]
    public async Task Mvcc_Serializable_WriteWriteConflict_Detected()
    {
        var store = new MvccDocumentStore();
        await store.CreateCollectionAsync("test");

        var walOptions = CreateWalOptions();
        using var wal = new WriteAheadLog(walOptions);
        using var coordinator = new MvccTransactionCoordinator(store, wal);

        // Setup: insert initial document
        var setupTxn = (MvccTransactionContext)await coordinator.BeginTransactionAsync();
        await setupTxn.InsertAsync("test", new Document { Id = "doc1", Data = new() { ["v"] = 1 } });
        await coordinator.CommitAsync(setupTxn.TransactionId);

        // Start two serializable transactions
        var options = new TransactionOptions { IsolationLevel = IsolationLevel.Serializable };

        var txn1 = (MvccTransactionContext)await coordinator.BeginTransactionAsync(options);
        var txn2 = (MvccTransactionContext)await coordinator.BeginTransactionAsync(options);

        // txn1 updates the document
        await txn1.UpdateAsync("test", new Document { Id = "doc1", Data = new() { ["v"] = 2 } });
        await coordinator.CommitAsync(txn1.TransactionId);

        // txn2 tries to update the same document - should detect conflict
        await Assert.ThrowsAsync<TransactionConflictException>(
            () => txn2.UpdateAsync("test", new Document { Id = "doc1", Data = new() { ["v"] = 3 } }));

        await coordinator.RollbackAsync(txn2.TransactionId);
    }

    #endregion
}
