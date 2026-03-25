// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Storage;
using AdvGenNoSqlServer.Storage.Revisions;
using Xunit;

namespace AdvGenNoSqlServer.Tests
{
    public class DocumentRevisionTests
    {
        private readonly DocumentStore _innerStore;
        private readonly RevisionManager _revisionManager;
        private readonly RevisionDocumentStore _store;

        public DocumentRevisionTests()
        {
            _innerStore = new DocumentStore();
            _revisionManager = new RevisionManager();
            _store = new RevisionDocumentStore(_innerStore, _revisionManager);

            // Enable revisions for test collection
            _revisionManager.EnableForCollection("test");
        }

        #region DocumentRevision Tests

        [Fact]
        public void DocumentRevision_Create_ShouldCreateValidRevision()
        {
            var doc = CreateTestDocument("doc1", "John", 30);
            var revision = DocumentRevision.Create("doc1", "users", 1, doc);

            Assert.NotNull(revision);
            Assert.Equal("doc1", revision.DocumentId);
            Assert.Equal("users", revision.CollectionName);
            Assert.Equal(1, revision.Version);
            Assert.True(revision.IsInitialRevision);
            Assert.NotNull(revision.RevisionId);
            Assert.True(revision.CreatedAt <= DateTime.UtcNow);
        }

        [Fact]
        public void DocumentRevision_Create_WithMetadata_ShouldIncludeMetadata()
        {
            var doc = CreateTestDocument("doc1", "John", 30);
            var revision = DocumentRevision.Create("doc1", "users", 1, doc, "user123", "Initial creation");

            Assert.Equal("user123", revision.ModifiedBy);
            Assert.Equal("Initial creation", revision.ChangeReason);
        }

        [Fact]
        public void DocumentRevision_WithMetadata_ShouldUpdateMetadata()
        {
            var doc = CreateTestDocument("doc1", "John", 30);
            var revision = DocumentRevision.Create("doc1", "users", 1, doc);
            var updated = revision.WithMetadata("user456", "Updated reason");

            Assert.Equal("user456", updated.ModifiedBy);
            Assert.Equal("Updated reason", updated.ChangeReason);
            Assert.Equal(revision.RevisionId, updated.RevisionId); // ID should not change
        }

        [Fact]
        public void DocumentRevision_ToString_ShouldReturnReadableFormat()
        {
            var doc = CreateTestDocument("doc1", "John", 30);
            var revision = DocumentRevision.Create("doc1", "users", 1, doc);
            var str = revision.ToString();

            Assert.Contains("Revision 1", str);
            Assert.Contains("users:doc1", str);
        }

        #endregion

        #region RevisionOptions Tests

        [Fact]
        public void RevisionOptions_Default_ShouldHaveReasonableDefaults()
        {
            var options = RevisionOptions.Default;

            Assert.True(options.Enabled);
            Assert.Equal(10, options.MaxRevisionsPerDocument);
            Assert.Null(options.MaxRevisionAge);
            Assert.True(options.CreateRevisionOnInsert);
            Assert.True(options.CreateRevisionOnUpdate);
            Assert.False(options.CreateRevisionOnDelete);
            Assert.True(options.SkipUnchangedRevisions);
            Assert.True(options.EnableAutomaticCleanup);
            Assert.Equal(TimeSpan.FromHours(1), options.CleanupInterval);
        }

        [Fact]
        public void RevisionOptions_Unlimited_ShouldHaveZeroMaxRevisions()
        {
            var options = RevisionOptions.Unlimited;

            Assert.Equal(0, options.MaxRevisionsPerDocument);
        }

        [Fact]
        public void RevisionOptions_Disabled_ShouldBeDisabled()
        {
            var options = RevisionOptions.Disabled;

            Assert.False(options.Enabled);
        }

        [Fact]
        public void RevisionOptions_Clone_ShouldCreateIndependentCopy()
        {
            var original = new RevisionOptions { MaxRevisionsPerDocument = 5 };
            var clone = original.Clone();

            clone.MaxRevisionsPerDocument = 10;

            Assert.Equal(5, original.MaxRevisionsPerDocument);
            Assert.Equal(10, clone.MaxRevisionsPerDocument);
        }

        [Fact]
        public void RevisionOptions_Validate_NegativeMaxRevisions_ShouldThrow()
        {
            var options = new RevisionOptions { MaxRevisionsPerDocument = -1 };

            Assert.Throws<ArgumentException>(() => options.Validate());
        }

        [Fact]
        public void RevisionOptions_Validate_ShortCleanupInterval_ShouldThrow()
        {
            var options = new RevisionOptions { CleanupInterval = TimeSpan.FromSeconds(30) };

            Assert.Throws<ArgumentException>(() => options.Validate());
        }

        #endregion

        #region DocumentComparer Tests

        [Fact]
        public void DocumentComparer_AreEqual_SameDocuments_ShouldReturnTrue()
        {
            var doc1 = CreateTestDocument("doc1", "John", 30);
            var doc2 = CreateTestDocument("doc1", "John", 30);
            var comparer = new DocumentComparer();

            Assert.True(comparer.AreEqual(doc1, doc2));
        }

        [Fact]
        public void DocumentComparer_AreEqual_DifferentDocuments_ShouldReturnFalse()
        {
            var doc1 = CreateTestDocument("doc1", "John", 30);
            var doc2 = CreateTestDocument("doc1", "Jane", 25);
            var comparer = new DocumentComparer();

            Assert.False(comparer.AreEqual(doc1, doc2));
        }

        [Fact]
        public void DocumentComparer_AreEqual_BothNull_ShouldReturnTrue()
        {
            var comparer = new DocumentComparer();

            Assert.True(comparer.AreEqual(null, null));
        }

        [Fact]
        public void DocumentComparer_AreEqual_OneNull_ShouldReturnFalse()
        {
            var doc = CreateTestDocument("doc1", "John", 30);
            var comparer = new DocumentComparer();

            Assert.False(comparer.AreEqual(doc, null));
            Assert.False(comparer.AreEqual(null, doc));
        }

        [Fact]
        public void DocumentComparer_Compare_EqualDocuments_ShouldReturnEqualResult()
        {
            var doc1 = CreateTestDocument("doc1", "John", 30);
            var doc2 = CreateTestDocument("doc1", "John", 30);
            var comparer = new DocumentComparer();

            var result = comparer.Compare(doc1, doc2);

            Assert.True(result.AreEqual);
            Assert.Empty(result.ChangedFields);
            Assert.Empty(result.AddedFields);
            Assert.Empty(result.RemovedFields);
        }

        [Fact]
        public void DocumentComparer_Compare_NewDocument_ShouldReturnAddedFields()
        {
            var doc = CreateTestDocument("doc1", "John", 30);
            var comparer = new DocumentComparer();

            var result = comparer.Compare(null, doc);

            Assert.False(result.AreEqual);
            Assert.NotEmpty(result.AddedFields);
            Assert.Contains("name", result.AddedFields);
            Assert.Contains("age", result.AddedFields);
        }

        [Fact]
        public void DocumentComparer_Compare_DeletedDocument_ShouldReturnRemovedFields()
        {
            var doc = CreateTestDocument("doc1", "John", 30);
            var comparer = new DocumentComparer();

            var result = comparer.Compare(doc, null);

            Assert.False(result.AreEqual);
            Assert.NotEmpty(result.RemovedFields);
            Assert.Contains("name", result.RemovedFields);
            Assert.Contains("age", result.RemovedFields);
        }

        [Fact]
        public void DocumentComparer_Compare_ChangedField_ShouldReturnChangedFields()
        {
            var doc1 = CreateTestDocument("doc1", "John", 30);
            var doc2 = CreateTestDocument("doc1", "Jane", 30);
            var comparer = new DocumentComparer();

            var result = comparer.Compare(doc1, doc2);

            Assert.False(result.AreEqual);
            Assert.Contains("name", result.ChangedFields);
        }

        #endregion

        #region RevisionManager Tests

        [Fact]
        public void RevisionManager_Constructor_ShouldInitializeDefaults()
        {
            var manager = new RevisionManager();

            Assert.NotNull(manager.Options);
            Assert.NotNull(manager.Statistics);
            Assert.True(manager.Options.Enabled);
        }

        [Fact]
        public async Task RevisionManager_CreateRevision_ShouldCreateRevision()
        {
            var manager = new RevisionManager();
            manager.EnableForCollection("test");

            var doc = CreateTestDocument("doc1", "John", 30);
            var revision = await manager.CreateRevisionAsync("test", "doc1", doc, RevisionTrigger.Insert);

            Assert.NotNull(revision);
            Assert.Equal(1, revision.Version);
        }

        [Fact]
        public async Task RevisionManager_CreateRevision_Multiple_ShouldIncrementVersion()
        {
            var manager = new RevisionManager();
            manager.EnableForCollection("test");

            var doc1 = CreateTestDocument("doc1", "John", 30);
            var doc2 = CreateTestDocument("doc1", "Jane", 25);

            var revision1 = await manager.CreateRevisionAsync("test", "doc1", doc1, RevisionTrigger.Insert);
            var revision2 = await manager.CreateRevisionAsync("test", "doc1", doc2, RevisionTrigger.Update);

            Assert.Equal(1, revision1.Version);
            Assert.Equal(2, revision2.Version);
        }

        [Fact]
        public async Task RevisionManager_CreateRevision_Disabled_ShouldThrow()
        {
            var manager = new RevisionManager(RevisionOptions.Disabled);
            manager.EnableForCollection("test");

            var doc = CreateTestDocument("doc1", "John", 30);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                manager.CreateRevisionAsync("test", "doc1", doc, RevisionTrigger.Insert));
        }

        [Fact]
        public async Task RevisionManager_CreateRevision_CollectionNotEnabled_ShouldThrow()
        {
            var manager = new RevisionManager();
            // Not enabling the collection

            var doc = CreateTestDocument("doc1", "John", 30);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                manager.CreateRevisionAsync("test", "doc1", doc, RevisionTrigger.Insert));
        }

        [Fact]
        public async Task RevisionManager_GetRevision_ShouldReturnCorrectRevision()
        {
            var manager = new RevisionManager();
            manager.EnableForCollection("test");

            var doc = CreateTestDocument("doc1", "John", 30);
            var created = await manager.CreateRevisionAsync("test", "doc1", doc, RevisionTrigger.Insert);

            var retrieved = await manager.GetRevisionAsync("test", "doc1", 1);

            Assert.NotNull(retrieved);
            Assert.Equal(created.RevisionId, retrieved.RevisionId);
        }

        [Fact]
        public async Task RevisionManager_GetRevision_NonExistent_ShouldReturnNull()
        {
            var manager = new RevisionManager();
            manager.EnableForCollection("test");

            var retrieved = await manager.GetRevisionAsync("test", "doc1", 999);

            Assert.Null(retrieved);
        }

        [Fact]
        public async Task RevisionManager_GetAllRevisions_ShouldReturnAllRevisions()
        {
            var manager = new RevisionManager();
            manager.EnableForCollection("test");

            var doc1 = CreateTestDocument("doc1", "John", 30);
            var doc2 = CreateTestDocument("doc1", "Jane", 25);

            await manager.CreateRevisionAsync("test", "doc1", doc1, RevisionTrigger.Insert);
            await manager.CreateRevisionAsync("test", "doc1", doc2, RevisionTrigger.Update);

            var revisions = await manager.GetAllRevisionsAsync("test", "doc1");

            Assert.Equal(2, revisions.Count);
            Assert.Contains(revisions, r => r.Version == 1);
            Assert.Contains(revisions, r => r.Version == 2);
        }

        [Fact]
        public async Task RevisionManager_GetLatestRevision_ShouldReturnMostRecent()
        {
            var manager = new RevisionManager();
            manager.EnableForCollection("test");

            var doc1 = CreateTestDocument("doc1", "John", 30);
            var doc2 = CreateTestDocument("doc1", "Jane", 25);

            await manager.CreateRevisionAsync("test", "doc1", doc1, RevisionTrigger.Insert);
            await manager.CreateRevisionAsync("test", "doc1", doc2, RevisionTrigger.Update);

            var latest = await manager.GetLatestRevisionAsync("test", "doc1");

            Assert.NotNull(latest);
            Assert.Equal(2, latest.Version);
        }

        [Fact]
        public async Task RevisionManager_RestoreRevision_ShouldReturnDocument()
        {
            var manager = new RevisionManager();
            manager.EnableForCollection("test");

            var doc = CreateTestDocument("doc1", "John", 30);
            await manager.CreateRevisionAsync("test", "doc1", doc, RevisionTrigger.Insert);

            var restored = await manager.RestoreRevisionAsync("test", "doc1", 1);

            Assert.NotNull(restored);
            Assert.Equal("doc1", restored.Id);
        }

        [Fact]
        public async Task RevisionManager_RestoreRevision_NonExistent_ShouldThrow()
        {
            var manager = new RevisionManager();
            manager.EnableForCollection("test");

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                manager.RestoreRevisionAsync("test", "doc1", 999));
        }

        [Fact]
        public async Task RevisionManager_DeleteRevisions_ShouldRemoveAllRevisions()
        {
            var manager = new RevisionManager();
            manager.EnableForCollection("test");

            var doc = CreateTestDocument("doc1", "John", 30);
            await manager.CreateRevisionAsync("test", "doc1", doc, RevisionTrigger.Insert);

            var result = await manager.DeleteRevisionsAsync("test", "doc1");
            var revisions = await manager.GetAllRevisionsAsync("test", "doc1");

            Assert.True(result);
            Assert.Empty(revisions);
        }

        [Fact]
        public async Task RevisionManager_EnableDisableCollection_ShouldToggleTracking()
        {
            var manager = new RevisionManager();

            Assert.False(manager.IsEnabledForCollection("test"));

            manager.EnableForCollection("test");
            Assert.True(manager.IsEnabledForCollection("test"));

            manager.DisableForCollection("test");
            Assert.False(manager.IsEnabledForCollection("test"));
        }

        [Fact]
        public async Task RevisionManager_UpdateOptions_ShouldUpdateSettings()
        {
            var manager = new RevisionManager();
            var newOptions = new RevisionOptions { MaxRevisionsPerDocument = 20 };

            manager.UpdateOptions(newOptions);

            Assert.Equal(20, manager.Options.MaxRevisionsPerDocument);
        }

        [Fact]
        public async Task RevisionManager_Cleanup_ShouldRemoveOldRevisions()
        {
            var options = new RevisionOptions
            {
                MaxRevisionsPerDocument = 2,
                EnableAutomaticCleanup = false // Manual cleanup for testing
            };
            var manager = new RevisionManager(options);
            manager.EnableForCollection("test");

            var doc1 = CreateTestDocument("doc1", "V1", 1);
            var doc2 = CreateTestDocument("doc1", "V2", 2);
            var doc3 = CreateTestDocument("doc1", "V3", 3);

            await manager.CreateRevisionAsync("test", "doc1", doc1, RevisionTrigger.Insert);
            await Task.Delay(10); // Ensure different timestamps
            await manager.CreateRevisionAsync("test", "doc1", doc2, RevisionTrigger.Update);
            await Task.Delay(10);
            await manager.CreateRevisionAsync("test", "doc1", doc3, RevisionTrigger.Update);

            var result = await manager.CleanupAsync();
            var revisions = await manager.GetAllRevisionsAsync("test", "doc1");

            Assert.Equal(1, result.RevisionsRemoved);
            Assert.Equal(2, revisions.Count); // Should keep only 2
        }

        [Fact]
        public void RevisionManager_RevisionCreated_Event_ShouldFire()
        {
            var manager = new RevisionManager();
            manager.EnableForCollection("test");

            DocumentRevision? capturedRevision = null;
            manager.RevisionCreated += (sender, args) =>
            {
                capturedRevision = args.Revision;
            };

            var doc = CreateTestDocument("doc1", "John", 30);
            manager.CreateRevisionAsync("test", "doc1", doc, RevisionTrigger.Insert).Wait();

            Assert.NotNull(capturedRevision);
            Assert.Equal("doc1", capturedRevision.DocumentId);
        }

        #endregion

        #region RevisionDocumentStore Tests

        [Fact]
        public async Task RevisionDocumentStore_Insert_ShouldCreateRevision()
        {
            var doc = CreateTestDocument("doc1", "John", 30);
            await _store.InsertAsync("test", doc);

            var revisions = await _revisionManager.GetAllRevisionsAsync("test", "doc1");

            Assert.Single(revisions);
            Assert.Equal(1, revisions[0].Version);
        }

        [Fact]
        public async Task RevisionDocumentStore_Update_ShouldCreateRevision()
        {
            var doc = CreateTestDocument("doc1", "John", 30);
            await _store.InsertAsync("test", doc);

            doc.Data = new Dictionary<string, object> { { "name", "Jane" }, { "age", 25 } };
            await _store.UpdateAsync("test", doc);

            var revisions = await _revisionManager.GetAllRevisionsAsync("test", "doc1");

            Assert.Equal(2, revisions.Count);
        }

        [Fact]
        public async Task RevisionDocumentStore_Update_NoChanges_ShouldNotCreateRevision()
        {
            var doc = CreateTestDocument("doc1", "John", 30);
            await _store.InsertAsync("test", doc);

            // Update with same data
            await _store.UpdateAsync("test", doc);

            var revisions = await _revisionManager.GetAllRevisionsAsync("test", "doc1");

            Assert.Equal(1, revisions.Count); // Should still be 1
        }

        [Fact]
        public async Task RevisionDocumentStore_Delete_ShouldNotCreateRevisionByDefault()
        {
            var doc = CreateTestDocument("doc1", "John", 30);
            await _store.InsertAsync("test", doc);
            await _store.DeleteAsync("test", "doc1");

            var revisions = await _revisionManager.GetAllRevisionsAsync("test", "doc1");

            Assert.Single(revisions); // Only insert revision
        }

        [Fact]
        public async Task RevisionDocumentStore_RestoreRevision_ShouldRestoreDocument()
        {
            var doc = CreateTestDocument("doc1", "John", 30);
            await _store.InsertAsync("test", doc);

            doc.Data = new Dictionary<string, object> { { "name", "Jane" }, { "age", 25 } };
            await _store.UpdateAsync("test", doc);

            var restored = await _store.RestoreRevisionAsync("test", "doc1", 1);

            Assert.Equal("John", restored.Data?["name"]);
            Assert.Equal(30, restored.Data?["age"]);
        }

        [Fact]
        public async Task RevisionDocumentStore_GetRevisionsAsync_ShouldReturnRevisions()
        {
            var doc = CreateTestDocument("doc1", "John", 30);
            await _store.InsertAsync("test", doc);

            var revisions = await _store.GetRevisionsAsync("test", "doc1");

            Assert.Single(revisions);
        }

        [Fact]
        public async Task RevisionDocumentStore_InnerStore_ShouldReturnInnerStore()
        {
            Assert.Equal(_innerStore, _store.InnerStore);
        }

        [Fact]
        public async Task RevisionDocumentStore_GetAndExists_ShouldWork()
        {
            var doc = CreateTestDocument("doc1", "John", 30);
            await _store.InsertAsync("test", doc);

            var retrieved = await _store.GetAsync("test", "doc1");
            var exists = await _store.ExistsAsync("test", "doc1");

            Assert.NotNull(retrieved);
            Assert.True(exists);
            Assert.Equal("John", retrieved.Data?["name"]);
        }

        [Fact]
        public async Task RevisionDocumentStore_CountAndCollections_ShouldWork()
        {
            var doc = CreateTestDocument("doc1", "John", 30);
            await _store.InsertAsync("test", doc);

            var count = await _store.CountAsync("test");
            var collections = await _store.GetCollectionsAsync();

            Assert.Equal(1, count);
            Assert.Contains("test", collections);
        }

        [Fact]
        public async Task RevisionDocumentStore_CreateCollection_ShouldEnableRevisions()
        {
            await _store.CreateCollectionAsync("newcollection");

            Assert.True(_revisionManager.IsEnabledForCollection("newcollection"));
        }

        [Fact]
        public async Task RevisionDocumentStore_DropCollection_ShouldCleanUpRevisions()
        {
            var doc = CreateTestDocument("doc1", "John", 30);
            await _store.InsertAsync("test", doc);
            await _store.DropCollectionAsync("test");

            var revisions = await _revisionManager.GetAllRevisionsAsync("test", "doc1");
            Assert.Empty(revisions);
        }

        [Fact]
        public async Task RevisionDocumentStore_GetManyAsync_ShouldReturnDocuments()
        {
            var doc1 = CreateTestDocument("doc1", "John", 30);
            var doc2 = CreateTestDocument("doc2", "Jane", 25);
            await _store.InsertAsync("test", doc1);
            await _store.InsertAsync("test", doc2);

            var docs = await _store.GetManyAsync("test", new[] { "doc1", "doc2" });

            Assert.Equal(2, docs.Count());
        }

        [Fact]
        public async Task RevisionDocumentStore_GetAllAsync_ShouldReturnAllDocuments()
        {
            var doc1 = CreateTestDocument("doc1", "John", 30);
            var doc2 = CreateTestDocument("doc2", "Jane", 25);
            await _store.InsertAsync("test", doc1);
            await _store.InsertAsync("test", doc2);

            var docs = await _store.GetAllAsync("test");

            Assert.Equal(2, docs.Count());
        }

        #endregion

        #region Extension Method Tests

        [Fact]
        public void DocumentStore_WithRevisions_ShouldReturnRevisionDocumentStore()
        {
            var innerStore = new DocumentStore();
            var revisionStore = innerStore.WithRevisions();

            Assert.IsType<RevisionDocumentStore>(revisionStore);
            Assert.Equal(innerStore, revisionStore.InnerStore);
        }

        [Fact]
        public void DocumentStore_WithRevisions_WithOptions_ShouldApplyOptions()
        {
            var innerStore = new DocumentStore();
            var options = new RevisionOptions { MaxRevisionsPerDocument = 5 };
            var revisionStore = innerStore.WithRevisions(options);

            Assert.IsType<RevisionDocumentStore>(revisionStore);
        }

        #endregion

        #region CleanupResult Tests

        [Fact]
        public void CleanupResult_Success_ShouldCreateResult()
        {
            var result = CleanupResult.Success(5, 3, TimeSpan.FromSeconds(1));

            Assert.Equal(5, result.RevisionsRemoved);
            Assert.Equal(3, result.DocumentsAffected);
            Assert.Equal(TimeSpan.FromSeconds(1), result.Duration);
        }

        [Fact]
        public void CleanupResult_Empty_ShouldCreateEmptyResult()
        {
            var result = CleanupResult.Empty();

            Assert.Equal(0, result.RevisionsRemoved);
            Assert.Equal(0, result.DocumentsAffected);
            Assert.Equal(TimeSpan.Zero, result.Duration);
        }

        #endregion

        #region RevisionStatistics Tests

        [Fact]
        public void RevisionStatistics_Snapshot_ShouldCreateCopy()
        {
            var stats = new RevisionStatistics
            {
                TotalRevisions = 10,
                RevisionsCreated = 15,
                RevisionsRemoved = 5
            };

            var snapshot = stats.Snapshot();

            Assert.Equal(stats.TotalRevisions, snapshot.TotalRevisions);
            Assert.Equal(stats.RevisionsCreated, snapshot.RevisionsCreated);
            Assert.Equal(stats.RevisionsRemoved, snapshot.RevisionsRemoved);
        }

        #endregion

        #region DocumentComparisonResult Tests

        [Fact]
        public void DocumentComparisonResult_Equal_ShouldCreateEqualResult()
        {
            var result = DocumentComparisonResult.Equal();

            Assert.True(result.AreEqual);
            Assert.Empty(result.ChangedFields);
            Assert.Empty(result.AddedFields);
            Assert.Empty(result.RemovedFields);
        }

        [Fact]
        public void DocumentComparisonResult_Different_ShouldCreateDifferentResult()
        {
            var result = DocumentComparisonResult.Different(
                new[] { "field1" },
                new[] { "field2" },
                new[] { "field3" });

            Assert.False(result.AreEqual);
            Assert.Contains("field1", result.ChangedFields);
            Assert.Contains("field2", result.AddedFields);
            Assert.Contains("field3", result.RemovedFields);
        }

        #endregion

        #region RevisionEventArgs Tests

        [Fact]
        public void RevisionEventArgs_Constructor_ShouldSetProperties()
        {
            var doc = CreateTestDocument("doc1", "John", 30);
            var revision = DocumentRevision.Create("doc1", "test", 1, doc);
            var args = new RevisionEventArgs(revision, RevisionTrigger.Insert);

            Assert.Equal(revision, args.Revision);
            Assert.Equal(RevisionTrigger.Insert, args.Operation);
        }

        #endregion

        #region RevisionHistoryStats Tests

        [Fact]
        public void RevisionHistoryStats_Default_ShouldHaveZeroValues()
        {
            var stats = new RevisionHistoryStats();

            Assert.Equal(0, stats.TotalDocumentsWithRevisions);
            Assert.Equal(0, stats.TotalRevisions);
            Assert.Equal(0, stats.AverageRevisionsPerDocument);
        }

        #endregion

        #region Integration Tests

        [Fact]
        public async Task RevisionManager_Integration_MultipleCollections_ShouldIsolateRevisions()
        {
            var manager = new RevisionManager();
            manager.EnableForCollection("collection1");
            manager.EnableForCollection("collection2");

            var doc1 = CreateTestDocument("doc1", "John", 30);
            var doc2 = CreateTestDocument("doc1", "Jane", 25);

            await manager.CreateRevisionAsync("collection1", "doc1", doc1, RevisionTrigger.Insert);
            await manager.CreateRevisionAsync("collection2", "doc1", doc2, RevisionTrigger.Insert);

            var revisions1 = await manager.GetAllRevisionsAsync("collection1", "doc1");
            var revisions2 = await manager.GetAllRevisionsAsync("collection2", "doc1");

            Assert.Single(revisions1);
            Assert.Single(revisions2);
            Assert.Equal("John", revisions1[0].Document.Data?["name"]);
            Assert.Equal("Jane", revisions2[0].Document.Data?["name"]);
        }

        [Fact]
        public async Task RevisionManager_Integration_MultipleDocuments_ShouldIsolateRevisions()
        {
            var manager = new RevisionManager();
            manager.EnableForCollection("test");

            var doc1 = CreateTestDocument("doc1", "John", 30);
            var doc2 = CreateTestDocument("doc2", "Jane", 25);

            await manager.CreateRevisionAsync("test", "doc1", doc1, RevisionTrigger.Insert);
            await manager.CreateRevisionAsync("test", "doc2", doc2, RevisionTrigger.Insert);

            var revisions1 = await manager.GetAllRevisionsAsync("test", "doc1");
            var revisions2 = await manager.GetAllRevisionsAsync("test", "doc2");

            Assert.Single(revisions1);
            Assert.Single(revisions2);
            Assert.Equal("doc1", revisions1[0].DocumentId);
            Assert.Equal("doc2", revisions2[0].DocumentId);
        }

        #endregion

        #region Helper Methods

        private Document CreateTestDocument(string id, string name, int age)
        {
            return new Document
            {
                Id = id,
                Data = new Dictionary<string, object> { { "name", name }, { "age", age } },
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Version = 1
            };
        }

        private Document CreateTestDocument(string id, Dictionary<string, object> data)
        {
            return new Document
            {
                Id = id,
                Data = data,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Version = 1
            };
        }

        #endregion
    }
}
