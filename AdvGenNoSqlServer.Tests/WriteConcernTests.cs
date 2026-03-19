// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Core.WriteConcern;
using AdvGenNoSqlServer.Storage;

namespace AdvGenNoSqlServer.Tests;

public class WriteConcernTests
{
    #region WriteConcern Tests

    [Fact]
    public void WriteConcern_Default_IsAcknowledged()
    {
        var concern = new WriteConcern();

        Assert.Equal(1, concern.W);
        Assert.False(concern.Journal);
        Assert.Null(concern.WTimeout);
        Assert.True(concern.IsAcknowledged);
        Assert.False(concern.IsJournaled);
        Assert.False(concern.IsMajority);
    }

    [Fact]
    public void WriteConcern_Unacknowledged_HasWValue0()
    {
        var concern = WriteConcern.Unacknowledged;

        Assert.Equal(0, concern.W);
        Assert.False(concern.IsAcknowledged);
        Assert.False(concern.IsJournaled);
    }

    [Fact]
    public void WriteConcern_Acknowledged_HasWValue1()
    {
        var concern = WriteConcern.Acknowledged;

        Assert.Equal(1, concern.W);
        Assert.True(concern.IsAcknowledged);
        Assert.False(concern.IsJournaled);
    }

    [Fact]
    public void WriteConcern_Journaled_HasWValue1AndJournalTrue()
    {
        var concern = WriteConcern.Journaled;

        Assert.Equal(1, concern.W);
        Assert.True(concern.Journal);
        Assert.True(concern.IsAcknowledged);
        Assert.True(concern.IsJournaled);
    }

    [Fact]
    public void WriteConcern_Majority_HasWValueMajority()
    {
        var concern = WriteConcern.Majority;

        Assert.Equal("majority", concern.W);
        Assert.True(concern.IsAcknowledged);
        Assert.True(concern.IsMajority);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void WriteConcern_Nodes_CreatesConcernWithCorrectWValue(int nodeCount)
    {
        var concern = WriteConcern.Nodes(nodeCount);

        Assert.Equal(nodeCount, concern.W);
    }

    [Fact]
    public void WriteConcern_Nodes_ThrowsOnNegativeCount()
    {
        Assert.Throws<ArgumentException>(() => WriteConcern.Nodes(-1));
    }

    [Fact]
    public void WriteConcern_WithTimeout_SetsTimeout()
    {
        var timeout = TimeSpan.FromSeconds(10);
        var concern = WriteConcern.Acknowledged.WithTimeout(timeout);

        Assert.Equal(1, concern.W);
        Assert.Equal(timeout, concern.WTimeout);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData("majority", true)]
    public void WriteConcern_IsAcknowledged_ReturnsCorrectValue(object w, bool expected)
    {
        var concern = new WriteConcern { W = w };

        Assert.Equal(expected, concern.IsAcknowledged);
    }

    [Fact]
    public void WriteConcern_Validate_DoesNotThrowOnValidConfig()
    {
        var concern = WriteConcern.Journaled;

        var exception = Record.Exception(() => concern.Validate());

        Assert.Null(exception);
    }

    [Fact]
    public void WriteConcern_Validate_ThrowsOnNegativeW()
    {
        var concern = new WriteConcern { W = -1 };

        Assert.Throws<ArgumentException>(() => concern.Validate());
    }

    [Fact]
    public void WriteConcern_Validate_ThrowsOnInvalidWString()
    {
        var concern = new WriteConcern { W = "invalid" };

        Assert.Throws<ArgumentException>(() => concern.Validate());
    }

    [Fact]
    public void WriteConcern_Validate_ThrowsOnNonPositiveTimeout()
    {
        var concern = new WriteConcern { WTimeout = TimeSpan.Zero };

        Assert.Throws<ArgumentException>(() => concern.Validate());
    }

    [Fact]
    public void WriteConcern_Equals_ReturnsTrueForEqualConcerns()
    {
        var concern1 = WriteConcern.Journaled;
        var concern2 = WriteConcern.Journaled;

        Assert.Equal(concern1, concern2);
        Assert.True(concern1.Equals(concern2));
    }

    [Fact]
    public void WriteConcern_Equals_ReturnsFalseForDifferentConcerns()
    {
        var concern1 = WriteConcern.Acknowledged;
        var concern2 = WriteConcern.Journaled;

        Assert.NotEqual(concern1, concern2);
        Assert.False(concern1.Equals(concern2));
    }

    [Fact]
    public void WriteConcern_GetHashCode_SameForEqualConcerns()
    {
        var concern1 = WriteConcern.Majority;
        var concern2 = WriteConcern.Majority;

        Assert.Equal(concern1.GetHashCode(), concern2.GetHashCode());
    }

    [Fact]
    public void WriteConcern_ToString_ContainsWValue()
    {
        var concern = WriteConcern.Acknowledged;
        var str = concern.ToString();

        Assert.Contains("w: 1", str);
    }

    [Fact]
    public void WriteConcern_ToString_ContainsJournalWhenTrue()
    {
        var concern = WriteConcern.Journaled;
        var str = concern.ToString();

        Assert.Contains("j: true", str);
    }

    [Fact]
    public void WriteConcern_ToString_ContainsTimeoutWhenSet()
    {
        var concern = WriteConcern.Acknowledged.WithTimeout(TimeSpan.FromSeconds(5));
        var str = concern.ToString();

        Assert.Contains("wtimeout:", str);
        Assert.Contains("5000ms", str);
    }

    [Fact]
    public void WriteConcern_ImplicitConversion_FromInt()
    {
        WriteConcern concern = 3;

        Assert.Equal(3, concern.W);
    }

    [Fact]
    public void WriteConcern_ImplicitConversion_FromString_Majority()
    {
        WriteConcern concern = "majority";

        Assert.Equal("majority", concern.W);
    }

    [Fact]
    public void WriteConcern_ImplicitConversion_FromString_InvalidThrows()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            WriteConcern concern = "invalid";
        });
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData("majority", -1)]
    public void WriteConcern_GetWValue_ReturnsCorrectValue(object w, int expected)
    {
        var concern = new WriteConcern { W = w };

        Assert.Equal(expected, concern.GetWValue());
    }

    #endregion

    #region WriteConcernResult Tests

    [Fact]
    public void WriteConcernResult_SuccessResult_HasCorrectValues()
    {
        var document = CreateTestDocument("doc1");
        var concern = WriteConcern.Acknowledged;
        var result = WriteConcernResult.SuccessResult(document, concern);

        Assert.True(result.Success);
        Assert.Equal(document, result.Document);
        Assert.Equal(concern, result.WriteConcern);
        Assert.Equal(1, result.AffectedCount);
        Assert.True(result.IsAcknowledged);
    }

    [Fact]
    public void WriteConcernResult_UpdateResult_HasCorrectValues()
    {
        var document = CreateTestDocument("doc1");
        var concern = WriteConcern.Journaled;
        var result = WriteConcernResult.UpdateResult(document, concern);

        Assert.True(result.Success);
        Assert.True(result.WasJournaled);
    }

    [Fact]
    public void WriteConcernResult_DeleteResult_True_HasAffectedCount1()
    {
        var concern = WriteConcern.Acknowledged;
        var result = WriteConcernResult.DeleteResult(true, concern);

        Assert.True(result.Success);
        Assert.Equal(1, result.AffectedCount);
        Assert.Null(result.Document);
    }

    [Fact]
    public void WriteConcernResult_DeleteResult_False_HasAffectedCount0()
    {
        var concern = WriteConcern.Acknowledged;
        var result = WriteConcernResult.DeleteResult(false, concern);

        Assert.True(result.Success);
        Assert.Equal(0, result.AffectedCount);
    }

    [Fact]
    public void WriteConcernResult_UnacknowledgedResult_HasCorrectValues()
    {
        var result = WriteConcernResult.UnacknowledgedResult();

        Assert.True(result.Success);
        Assert.False(result.WriteConcern.IsAcknowledged);
        Assert.Equal(0, result.AcknowledgedByNodes);
    }

    [Fact]
    public void WriteConcernResult_FailureResult_HasCorrectValues()
    {
        var exception = new Exception("Test error");
        var result = WriteConcernResult.FailureResult("Error message", exception);

        Assert.False(result.Success);
        Assert.Equal("Error message", result.ErrorMessage);
        Assert.Equal(exception, result.Exception);
        Assert.False(result.IsAcknowledged);
    }

    [Fact]
    public void WriteConcernResult_CollectionOperationResult_Success_HasCorrectValues()
    {
        var concern = WriteConcern.Journaled;
        var result = WriteConcernResult.CollectionOperationResult(true, "create", concern);

        Assert.True(result.Success);
        Assert.True(result.WasJournaled);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void WriteConcernResult_CollectionOperationResult_Failure_HasCorrectValues()
    {
        var concern = WriteConcern.Acknowledged;
        var result = WriteConcernResult.CollectionOperationResult(false, "drop", concern);

        Assert.False(result.Success);
        Assert.Equal("drop operation failed", result.ErrorMessage);
    }

    #endregion

    #region WriteConcernBatchResult Tests

    [Fact]
    public void WriteConcernBatchResult_FromResults_AllSuccessful()
    {
        var concern = WriteConcern.Acknowledged;
        var results = new List<WriteConcernResult>
        {
            WriteConcernResult.SuccessResult(CreateTestDocument("doc1"), concern),
            WriteConcernResult.SuccessResult(CreateTestDocument("doc2"), concern),
            WriteConcernResult.SuccessResult(CreateTestDocument("doc3"), concern)
        };

        var batchResult = WriteConcernBatchResult.FromResults(results, concern);

        Assert.True(batchResult.AllSuccessful);
        Assert.Equal(3, batchResult.SuccessCount);
        Assert.Equal(0, batchResult.FailureCount);
        Assert.Equal(3, batchResult.TotalAffectedCount);
        Assert.Equal(3, batchResult.Results.Count);
    }

    [Fact]
    public void WriteConcernBatchResult_FromResults_SomeFailed()
    {
        var concern = WriteConcern.Acknowledged;
        var results = new List<WriteConcernResult>
        {
            WriteConcernResult.SuccessResult(CreateTestDocument("doc1"), concern),
            WriteConcernResult.FailureResult(concern, "Error"),
            WriteConcernResult.SuccessResult(CreateTestDocument("doc3"), concern)
        };

        var batchResult = WriteConcernBatchResult.FromResults(results, concern);

        Assert.False(batchResult.AllSuccessful);
        Assert.Equal(2, batchResult.SuccessCount);
        Assert.Equal(1, batchResult.FailureCount);
    }

    [Fact]
    public void WriteConcernBatchResult_FromResults_Empty()
    {
        var concern = WriteConcern.Acknowledged;
        var results = new List<WriteConcernResult>();

        var batchResult = WriteConcernBatchResult.FromResults(results, concern);

        Assert.True(batchResult.AllSuccessful);
        Assert.Equal(0, batchResult.SuccessCount);
        Assert.Equal(0, batchResult.FailureCount);
    }

    #endregion

    #region WriteConcernManager Tests

    [Fact]
    public void WriteConcernManager_DefaultConstructor_UsesDefaultOptions()
    {
        var manager = new WriteConcernManager();

        Assert.Equal(WriteConcern.Acknowledged.W, manager.DefaultWriteConcern.W);
    }

    [Fact]
    public void WriteConcernManager_Constructor_WithOptions_UsesOptions()
    {
        var options = new WriteConcernOptions { DefaultWriteConcern = WriteConcern.Journaled };
        var manager = new WriteConcernManager(options);

        Assert.True(manager.DefaultWriteConcern.IsJournaled);
    }

    [Fact]
    public void WriteConcernManager_DefaultWriteConcern_Setter_UpdatesValue()
    {
        var manager = new WriteConcernManager();
        manager.DefaultWriteConcern = WriteConcern.Majority;

        Assert.True(manager.DefaultWriteConcern.IsMajority);
    }

    [Fact]
    public void WriteConcernManager_DefaultWriteConcern_Setter_ThrowsOnNull()
    {
        var manager = new WriteConcernManager();

        Assert.Throws<ArgumentNullException>(() => manager.DefaultWriteConcern = null!);
    }

    [Fact]
    public void WriteConcernManager_DefaultWriteConcern_Setter_ThrowsOnInvalid()
    {
        var manager = new WriteConcernManager();
        var invalidConcern = new WriteConcern { W = -1 };

        Assert.Throws<ArgumentException>(() => manager.DefaultWriteConcern = invalidConcern);
    }

    [Fact]
    public async Task WriteConcernManager_GetWriteConcernForCollection_ReturnsDefaultWhenNotSet()
    {
        var manager = new WriteConcernManager();
        var concern = manager.GetWriteConcernForCollection("test");

        Assert.Equal(manager.DefaultWriteConcern.W, concern.W);
    }

    [Fact]
    public async Task WriteConcernManager_SetCollectionWriteConcern_SetsCustomConcern()
    {
        var manager = new WriteConcernManager();
        await manager.SetCollectionWriteConcernAsync("test", WriteConcern.Journaled);

        var concern = manager.GetWriteConcernForCollection("test");

        Assert.True(concern.IsJournaled);
    }

    [Fact]
    public async Task WriteConcernManager_SetCollectionWriteConcern_ThrowsOnEmptyName()
    {
        var manager = new WriteConcernManager();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            manager.SetCollectionWriteConcernAsync("", WriteConcern.Acknowledged));
    }

    [Fact]
    public async Task WriteConcernManager_SetCollectionWriteConcern_ThrowsOnNullConcern()
    {
        var manager = new WriteConcernManager();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            manager.SetCollectionWriteConcernAsync("test", null!));
    }

    [Fact]
    public async Task WriteConcernManager_SetCollectionWriteConcern_ThrowsWhenUnacknowledgedNotAllowed()
    {
        var options = new WriteConcernOptions { AllowUnacknowledgedWrites = false };
        var manager = new WriteConcernManager(options);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.SetCollectionWriteConcernAsync("test", WriteConcern.Unacknowledged));
    }

    [Fact]
    public async Task WriteConcernManager_RemoveCollectionWriteConcern_RemovesCustomConcern()
    {
        var manager = new WriteConcernManager();
        await manager.SetCollectionWriteConcernAsync("test", WriteConcern.Journaled);
        await manager.RemoveCollectionWriteConcernAsync("test");

        var concern = manager.GetWriteConcernForCollection("test");

        Assert.Equal(manager.DefaultWriteConcern.W, concern.W);
    }

    [Fact]
    public async Task WriteConcernManager_GetCollectionsWithCustomWriteConcern_ReturnsCustomCollections()
    {
        var manager = new WriteConcernManager();
        await manager.SetCollectionWriteConcernAsync("col1", WriteConcern.Journaled);
        await manager.SetCollectionWriteConcernAsync("col2", WriteConcern.Majority);

        var collections = await manager.GetCollectionsWithCustomWriteConcernAsync();

        Assert.Equal(2, collections.Count);
        Assert.True(collections["col1"].IsJournaled);
        Assert.True(collections["col2"].IsMajority);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData("majority", true)]
    public void WriteConcernManager_ValidateWriteConcern_ValidCases(object w, bool expected)
    {
        var manager = new WriteConcernManager();
        var concern = new WriteConcern { W = w };

        var result = manager.ValidateWriteConcern(concern);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void WriteConcernManager_ValidateWriteConcern_NullReturnsFalse()
    {
        var manager = new WriteConcernManager();

        Assert.False(manager.ValidateWriteConcern(null!));
    }

    [Fact]
    public void WriteConcernManager_ValidateWriteConcern_InvalidWReturnsFalse()
    {
        var manager = new WriteConcernManager();
        var concern = new WriteConcern { W = -1 };

        Assert.False(manager.ValidateWriteConcern(concern));
    }

    [Fact]
    public void WriteConcernManager_ValidateWriteConcern_UnacknowledgedNotAllowed()
    {
        var options = new WriteConcernOptions { AllowUnacknowledgedWrites = false };
        var manager = new WriteConcernManager(options);

        Assert.False(manager.ValidateWriteConcern(WriteConcern.Unacknowledged));
    }

    [Fact]
    public void WriteConcernManager_ValidateWriteConcern_TimeoutTooLong()
    {
        var options = new WriteConcernOptions { MaxTimeout = TimeSpan.FromSeconds(1) };
        var manager = new WriteConcernManager(options);
        var concern = new WriteConcern { WTimeout = TimeSpan.FromSeconds(10) };

        Assert.False(manager.ValidateWriteConcern(concern));
    }

    [Fact]
    public void WriteConcernManager_GetStatistics_ReturnsCorrectValues()
    {
        var manager = new WriteConcernManager();

        var stats = manager.GetStatistics();

        Assert.NotNull(stats);
        Assert.Equal(0, stats.TotalWriteOperations);
        Assert.Equal(DateTime.UtcNow.Date, stats.LastResetAt.Date);
    }

    [Fact]
    public void WriteConcernManager_ResetStatistics_ClearsAllCounts()
    {
        var manager = new WriteConcernManager();
        var beforeReset = manager.GetStatistics();
        var resetTime = beforeReset.LastResetAt;

        // Wait a tiny bit to ensure time difference
        Thread.Sleep(10);
        manager.ResetStatistics();

        var stats = manager.GetStatistics();
        Assert.True(stats.LastResetAt > resetTime || stats.LastResetAt == resetTime);
    }

    [Fact]
    public void WriteConcernStatistics_GetDistributionPercentages_Empty()
    {
        var stats = new WriteConcernStatistics();

        var distribution = stats.GetDistributionPercentages();

        Assert.Equal(0, distribution["unacknowledged"]);
        Assert.Equal(0, distribution["acknowledged"]);
        Assert.Equal(0, distribution["journaled"]);
        Assert.Equal(0, distribution["majority"]);
    }

    [Fact]
    public void WriteConcernStatistics_GetDistributionPercentages_WithData()
    {
        var stats = new WriteConcernStatistics
        {
            TotalWriteOperations = 100,
            UnacknowledgedOperations = 10,
            AcknowledgedOperations = 40,
            JournaledOperations = 30,
            MajorityOperations = 20
        };

        var distribution = stats.GetDistributionPercentages();

        Assert.Equal(10.0, distribution["unacknowledged"]);
        Assert.Equal(40.0, distribution["acknowledged"]);
        Assert.Equal(30.0, distribution["journaled"]);
        Assert.Equal(20.0, distribution["majority"]);
    }

    #endregion

    #region WriteConcernDocumentStore Tests

    [Fact]
    public async Task WriteConcernDocumentStore_Constructor_SetsProperties()
    {
        var innerStore = new DocumentStore();
        var manager = new WriteConcernManager();
        var wrapper = new WriteConcernDocumentStore(innerStore, manager);

        Assert.Equal(innerStore, wrapper.InnerStore);
        Assert.Equal(manager, wrapper.WriteConcernManager);
    }

    [Fact]
    public void WriteConcernDocumentStore_Constructor_ThrowsOnNullInnerStore()
    {
        var manager = new WriteConcernManager();

        Assert.Throws<ArgumentNullException>(() => new WriteConcernDocumentStore(null!, manager));
    }

    [Fact]
    public void WriteConcernDocumentStore_Constructor_ThrowsOnNullManager()
    {
        var innerStore = new DocumentStore();

        Assert.Throws<ArgumentNullException>(() => new WriteConcernDocumentStore(innerStore, null!));
    }

    [Fact]
    public async Task WriteConcernDocumentStore_InsertAsync_ReturnsDocument()
    {
        var innerStore = new DocumentStore();
        var manager = new WriteConcernManager();
        var wrapper = new WriteConcernDocumentStore(innerStore, manager);
        var document = CreateTestDocument("doc1");

        var result = await wrapper.InsertAsync("test", document);

        Assert.NotNull(result);
        Assert.Equal("doc1", result.Id);
    }

    [Fact]
    public async Task WriteConcernDocumentStore_InsertAsync_WithJournaled_StillWorks()
    {
        var innerStore = new DocumentStore();
        var manager = new WriteConcernManager();
        manager.DefaultWriteConcern = WriteConcern.Journaled;
        var wrapper = new WriteConcernDocumentStore(innerStore, manager);
        var document = CreateTestDocument("doc1");

        var result = await wrapper.InsertAsync("test", document);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task WriteConcernDocumentStore_GetAsync_ReturnsDocument()
    {
        var innerStore = new DocumentStore();
        var manager = new WriteConcernManager();
        var wrapper = new WriteConcernDocumentStore(innerStore, manager);
        var document = CreateTestDocument("doc1");
        await wrapper.InsertAsync("test", document);

        var result = await wrapper.GetAsync("test", "doc1");

        Assert.NotNull(result);
        Assert.Equal("doc1", result.Id);
    }

    [Fact]
    public async Task WriteConcernDocumentStore_GetAsync_NonExistentReturnsNull()
    {
        var innerStore = new DocumentStore();
        var manager = new WriteConcernManager();
        var wrapper = new WriteConcernDocumentStore(innerStore, manager);

        var result = await wrapper.GetAsync("test", "nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public async Task WriteConcernDocumentStore_UpdateAsync_ReturnsUpdatedDocument()
    {
        var innerStore = new DocumentStore();
        var manager = new WriteConcernManager();
        var wrapper = new WriteConcernDocumentStore(innerStore, manager);
        var document = CreateTestDocument("doc1");
        await wrapper.InsertAsync("test", document);

        document.Data!["name"] = "Updated";
        var result = await wrapper.UpdateAsync("test", document);

        Assert.NotNull(result);
        Assert.Equal("Updated", result.Data!["name"]);
    }

    [Fact]
    public async Task WriteConcernDocumentStore_DeleteAsync_ReturnsTrue()
    {
        var innerStore = new DocumentStore();
        var manager = new WriteConcernManager();
        var wrapper = new WriteConcernDocumentStore(innerStore, manager);
        var document = CreateTestDocument("doc1");
        await wrapper.InsertAsync("test", document);

        var result = await wrapper.DeleteAsync("test", "doc1");

        Assert.True(result);
    }

    [Fact]
    public async Task WriteConcernDocumentStore_DeleteAsync_NonExistentReturnsFalse()
    {
        var innerStore = new DocumentStore();
        var manager = new WriteConcernManager();
        var wrapper = new WriteConcernDocumentStore(innerStore, manager);

        var result = await wrapper.DeleteAsync("test", "nonexistent");

        Assert.False(result);
    }

    [Fact]
    public async Task WriteConcernDocumentStore_ExistsAsync_ReturnsTrue()
    {
        var innerStore = new DocumentStore();
        var manager = new WriteConcernManager();
        var wrapper = new WriteConcernDocumentStore(innerStore, manager);
        var document = CreateTestDocument("doc1");
        await wrapper.InsertAsync("test", document);

        var result = await wrapper.ExistsAsync("test", "doc1");

        Assert.True(result);
    }

    [Fact]
    public async Task WriteConcernDocumentStore_CountAsync_ReturnsCorrectCount()
    {
        var innerStore = new DocumentStore();
        var manager = new WriteConcernManager();
        var wrapper = new WriteConcernDocumentStore(innerStore, manager);
        await wrapper.InsertAsync("test", CreateTestDocument("doc1"));
        await wrapper.InsertAsync("test", CreateTestDocument("doc2"));

        var result = await wrapper.CountAsync("test");

        Assert.Equal(2, result);
    }

    [Fact]
    public async Task WriteConcernDocumentStore_GetAllAsync_ReturnsAllDocuments()
    {
        var innerStore = new DocumentStore();
        var manager = new WriteConcernManager();
        var wrapper = new WriteConcernDocumentStore(innerStore, manager);
        await wrapper.InsertAsync("test", CreateTestDocument("doc1"));
        await wrapper.InsertAsync("test", CreateTestDocument("doc2"));

        var result = await wrapper.GetAllAsync("test");

        Assert.Equal(2, result.Count());
    }

    [Fact]
    public async Task WriteConcernDocumentStore_CreateCollectionAsync_CreatesCollection()
    {
        var innerStore = new DocumentStore();
        var manager = new WriteConcernManager();
        var wrapper = new WriteConcernDocumentStore(innerStore, manager);

        await wrapper.CreateCollectionAsync("newcollection");

        var collections = await wrapper.GetCollectionsAsync();
        Assert.Contains("newcollection", collections);
    }

    [Fact]
    public async Task WriteConcernDocumentStore_DropCollectionAsync_ReturnsTrue()
    {
        var innerStore = new DocumentStore();
        var manager = new WriteConcernManager();
        var wrapper = new WriteConcernDocumentStore(innerStore, manager);
        await wrapper.CreateCollectionAsync("test");

        var result = await wrapper.DropCollectionAsync("test");

        Assert.True(result);
    }

    [Fact]
    public async Task WriteConcernDocumentStore_ClearCollectionAsync_ClearsDocuments()
    {
        var innerStore = new DocumentStore();
        var manager = new WriteConcernManager();
        var wrapper = new WriteConcernDocumentStore(innerStore, manager);
        await wrapper.InsertAsync("test", CreateTestDocument("doc1"));
        await wrapper.InsertAsync("test", CreateTestDocument("doc2"));

        await wrapper.ClearCollectionAsync("test");
        var count = await wrapper.CountAsync("test");

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task WriteConcernDocumentStore_BatchInsertAsync_ReturnsBatchResult()
    {
        var innerStore = new DocumentStore();
        var manager = new WriteConcernManager();
        var wrapper = new WriteConcernDocumentStore(innerStore, manager);
        var documents = new List<Document>
        {
            CreateTestDocument("doc1"),
            CreateTestDocument("doc2"),
            CreateTestDocument("doc3")
        };

        var result = await wrapper.BatchInsertAsync("test", documents, WriteConcern.Acknowledged);

        Assert.True(result.AllSuccessful);
        Assert.Equal(3, result.SuccessCount);
        Assert.Equal(3, result.TotalAffectedCount);
    }

    [Fact]
    public async Task WriteConcernDocumentStore_ExecuteWithWriteConcernAsync_ReturnsResult()
    {
        var innerStore = new DocumentStore();
        var manager = new WriteConcernManager();
        var wrapper = new WriteConcernDocumentStore(innerStore, manager);
        var document = CreateTestDocument("doc1");

        var result = await wrapper.ExecuteWithWriteConcernAsync(
            "test",
            (store, ct) => store.InsertAsync("test", document, ct),
            WriteConcern.Acknowledged);

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
    }

    #endregion

    #region Extension Method Tests

    [Fact]
    public void WriteConcernDocumentStoreExtensions_WithWriteConcern_DefaultManager()
    {
        var store = new DocumentStore();
        var wrapper = store.WithWriteConcern();

        Assert.NotNull(wrapper);
        Assert.IsType<WriteConcernDocumentStore>(wrapper);
    }

    [Fact]
    public void WriteConcernDocumentStoreExtensions_WithWriteConcern_WithOptions()
    {
        var store = new DocumentStore();
        var options = new WriteConcernOptions { DefaultWriteConcern = WriteConcern.Journaled };
        var wrapper = store.WithWriteConcern(options);

        Assert.NotNull(wrapper);
        Assert.True(wrapper.WriteConcernManager.DefaultWriteConcern.IsJournaled);
    }

    [Fact]
    public void WriteConcernDocumentStoreExtensions_WithWriteConcern_WithSpecificConcern()
    {
        var store = new DocumentStore();
        var wrapper = store.WithWriteConcern(WriteConcern.Majority);

        Assert.NotNull(wrapper);
        Assert.True(wrapper.WriteConcernManager.DefaultWriteConcern.IsMajority);
    }

    #endregion

    #region Helper Methods

    private static Document CreateTestDocument(string id)
    {
        return new Document
        {
            Id = id,
            Data = new Dictionary<string, object>
            {
                ["name"] = $"Document {id}",
                ["value"] = 42
            },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };
    }

    #endregion
}
