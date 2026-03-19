// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.ChangeStreams;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Storage;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for Change Streams functionality
/// </summary>
public class ChangeStreamTests
{
    private static Document CreateDocument(string id, Dictionary<string, object>? data = null)
    {
        return new Document { Id = id, Data = data ?? new Dictionary<string, object>() };
    }

    #region ChangeStreamEvent Tests

    [Fact]
    public void ChangeStreamEvent_CreateInsert_SetsCorrectProperties()
    {
        // Arrange
        var document = CreateDocument("doc1", new Dictionary<string, object> { { "name", "Test" } });

        // Act
        var evt = ChangeStreamEvent.CreateInsert("users", document, "txn123");

        // Assert
        Assert.Equal(ChangeOperationType.Insert, evt.OperationType);
        Assert.Equal("users", evt.CollectionName);
        Assert.Equal("doc1", evt.DocumentId);
        Assert.Equal(document, evt.FullDocument);
        Assert.Equal("txn123", evt.TransactionId);
        Assert.NotNull(evt.EventId);
        Assert.True(evt.ClusterTime > 0);
    }

    [Fact]
    public void ChangeStreamEvent_CreateUpdate_SetsCorrectProperties()
    {
        // Arrange
        var beforeDoc = CreateDocument("doc1", new Dictionary<string, object> { { "name", "Before" } });
        var afterDoc = CreateDocument("doc1", new Dictionary<string, object> { { "name", "After" } });

        // Act
        var evt = ChangeStreamEvent.CreateUpdate("users", "doc1", afterDoc, beforeDoc, "txn456");

        // Assert
        Assert.Equal(ChangeOperationType.Update, evt.OperationType);
        Assert.Equal("users", evt.CollectionName);
        Assert.Equal("doc1", evt.DocumentId);
        Assert.Equal(afterDoc, evt.FullDocument);
        Assert.Equal(beforeDoc, evt.DocumentBeforeChange);
        Assert.Equal("txn456", evt.TransactionId);
    }

    [Fact]
    public void ChangeStreamEvent_CreateDelete_SetsCorrectProperties()
    {
        // Arrange
        var deletedDoc = CreateDocument("doc1", new Dictionary<string, object> { { "name", "Deleted" } });

        // Act
        var evt = ChangeStreamEvent.CreateDelete("users", "doc1", deletedDoc, "txn789");

        // Assert
        Assert.Equal(ChangeOperationType.Delete, evt.OperationType);
        Assert.Equal("users", evt.CollectionName);
        Assert.Equal("doc1", evt.DocumentId);
        Assert.Null(evt.FullDocument);
        Assert.Equal(deletedDoc, evt.DocumentBeforeChange);
        Assert.Equal("txn789", evt.TransactionId);
    }

    [Fact]
    public void ChangeStreamEvent_CreateDropCollection_SetsCorrectProperties()
    {
        // Act
        var evt = ChangeStreamEvent.CreateDropCollection("users", "txn000");

        // Assert
        Assert.Equal(ChangeOperationType.Drop, evt.OperationType);
        Assert.Equal("users", evt.CollectionName);
        Assert.Empty(evt.DocumentId);
        Assert.Equal("txn000", evt.TransactionId);
    }

    [Fact]
    public void ChangeStreamEventArgs_Constructor_SetsEventProperty()
    {
        // Arrange
        var evt = ChangeStreamEvent.CreateInsert("users", CreateDocument("doc1"));

        // Act
        var args = new ChangeStreamEventArgs(evt);

        // Assert
        Assert.Equal(evt, args.Event);
    }

    [Fact]
    public void ChangeStreamEventArgs_Constructor_ThrowsOnNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ChangeStreamEventArgs(null!));
    }

    #endregion

    #region Filter Tests

    [Fact]
    public void OperationTypeFilter_Matches_ReturnsTrueForMatchingTypes()
    {
        // Arrange
        var filter = new OperationTypeFilter(ChangeOperationType.Insert, ChangeOperationType.Update);
        var insertEvent = ChangeStreamEvent.CreateInsert("users", CreateDocument("doc1"));
        var deleteEvent = ChangeStreamEvent.CreateDelete("users", "doc1");

        // Act & Assert
        Assert.True(filter.Matches(insertEvent));
        Assert.False(filter.Matches(deleteEvent));
    }

    [Fact]
    public void DocumentIdFilter_Matches_ReturnsTrueForMatchingIds()
    {
        // Arrange
        var filter = new DocumentIdFilter("doc1", "doc2");
        var event1 = ChangeStreamEvent.CreateInsert("users", CreateDocument("doc1"));
        var event3 = ChangeStreamEvent.CreateInsert("users", CreateDocument("doc3"));

        // Act & Assert
        Assert.True(filter.Matches(event1));
        Assert.False(filter.Matches(event3));
    }

    [Fact]
    public void TimeRangeFilter_Matches_ReturnsTrueForEventsInRange()
    {
        // Arrange
        var startTime = DateTime.UtcNow.AddMinutes(-5);
        var endTime = DateTime.UtcNow.AddMinutes(5);
        var filter = new TimeRangeFilter(startTime, endTime);

        var evt = new ChangeStreamEvent
        {
            OperationType = ChangeOperationType.Insert,
            Timestamp = DateTime.UtcNow
        };

        var oldEvent = new ChangeStreamEvent
        {
            OperationType = ChangeOperationType.Insert,
            Timestamp = DateTime.UtcNow.AddMinutes(-10)
        };

        // Act & Assert
        Assert.True(filter.Matches(evt));
        Assert.False(filter.Matches(oldEvent));
    }

    [Fact]
    public void CompositeFilter_Matches_ReturnsTrueWhenAllFiltersMatch()
    {
        // Arrange
        var typeFilter = new OperationTypeFilter(ChangeOperationType.Insert);
        var idFilter = new DocumentIdFilter("doc1");
        var composite = new CompositeFilter(typeFilter, idFilter);

        var matchingEvent = ChangeStreamEvent.CreateInsert("users", CreateDocument("doc1"));
        var nonMatchingEvent = ChangeStreamEvent.CreateUpdate("users", "doc1", CreateDocument("doc1"));

        // Act & Assert
        Assert.True(composite.Matches(matchingEvent));
        Assert.False(composite.Matches(nonMatchingEvent));
    }

    [Fact]
    public void MatchAllFilter_Matches_ReturnsTrueForAllEvents()
    {
        // Arrange
        var filter = new MatchAllFilter();
        var insertEvent = ChangeStreamEvent.CreateInsert("users", CreateDocument("doc1"));
        var deleteEvent = ChangeStreamEvent.CreateDelete("users", "doc1");

        // Act & Assert
        Assert.True(filter.Matches(insertEvent));
        Assert.True(filter.Matches(deleteEvent));
    }

    #endregion

    #region ChangeStreamManager Tests

    [Fact]
    public void ChangeStreamManager_Subscribe_CreatesActiveSubscription()
    {
        // Arrange
        var manager = new ChangeStreamManager();

        // Act
        var subscription = manager.Subscribe("users");

        // Assert
        Assert.NotNull(subscription);
        Assert.True(subscription.IsActive);
        Assert.Equal("users", subscription.CollectionName);
        Assert.Single(manager.GetActiveSubscriptions());
    }

    [Fact]
    public void ChangeStreamManager_SubscribeToAll_CreatesSubscriptionForAllCollections()
    {
        // Arrange
        var manager = new ChangeStreamManager();

        // Act
        var subscription = manager.SubscribeToAll();

        // Assert
        Assert.NotNull(subscription);
        Assert.Empty(subscription.CollectionName);
    }

    [Fact]
    public void ChangeStreamManager_Unsubscribe_RemovesSubscription()
    {
        // Arrange
        var manager = new ChangeStreamManager();
        var subscription = manager.Subscribe("users");

        // Act
        var result = manager.Unsubscribe(subscription.SubscriptionId);

        // Assert
        Assert.True(result);
        Assert.Empty(manager.GetActiveSubscriptions());
    }

    [Fact]
    public void ChangeStreamManager_Unsubscribe_InvalidId_ReturnsFalse()
    {
        // Arrange
        var manager = new ChangeStreamManager();

        // Act
        var result = manager.Unsubscribe("invalid-id");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ChangeStreamManager_PublishEvent_DeliversToMatchingSubscriptions()
    {
        // Arrange
        var manager = new ChangeStreamManager();
        var receivedEvents = new List<IChangeStreamEvent>();
        var subscription = manager.Subscribe("users", onChange: (sender, args) =>
        {
            receivedEvents.Add(args.Event);
        });

        var evt = ChangeStreamEvent.CreateInsert("users", CreateDocument("doc1"));

        // Act
        manager.PublishEvent(evt);

        // Allow time for async delivery
        Thread.Sleep(100);

        // Assert
        Assert.Single(receivedEvents);
        Assert.Equal(evt.DocumentId, receivedEvents[0].DocumentId);
    }

    [Fact]
    public void ChangeStreamManager_PublishEvent_DoesNotDeliverToDifferentCollection()
    {
        // Arrange
        var manager = new ChangeStreamManager();
        var receivedEvents = new List<IChangeStreamEvent>();
        var subscription = manager.Subscribe("users", onChange: (sender, args) =>
        {
            receivedEvents.Add(args.Event);
        });

        var evt = ChangeStreamEvent.CreateInsert("products", CreateDocument("doc1"));

        // Act
        manager.PublishEvent(evt);

        // Allow time for async delivery
        Thread.Sleep(100);

        // Assert
        Assert.Empty(receivedEvents);
    }

    [Fact]
    public void ChangeStreamManager_PublishEvent_RespectsFilter()
    {
        // Arrange
        var manager = new ChangeStreamManager();
        var receivedEvents = new List<IChangeStreamEvent>();
        var filter = new OperationTypeFilter(ChangeOperationType.Insert);
        var subscription = manager.Subscribe("users", filter, onChange: (sender, args) =>
        {
            receivedEvents.Add(args.Event);
        });

        var insertEvent = ChangeStreamEvent.CreateInsert("users", CreateDocument("doc1"));
        var deleteEvent = ChangeStreamEvent.CreateDelete("users", "doc2");

        // Act
        manager.PublishEvent(insertEvent);
        manager.PublishEvent(deleteEvent);

        // Allow time for async delivery
        Thread.Sleep(100);

        // Assert
        Assert.Single(receivedEvents);
        Assert.Equal(ChangeOperationType.Insert, receivedEvents[0].OperationType);
    }

    [Fact]
    public void ChangeStreamManager_PublishEvent_DeliversToAllSubscriptions()
    {
        // Arrange
        var manager = new ChangeStreamManager();
        var receivedEvents1 = new List<IChangeStreamEvent>();
        var receivedEvents2 = new List<IChangeStreamEvent>();

        var subscription1 = manager.Subscribe("users", onChange: (sender, args) => receivedEvents1.Add(args.Event));
        var subscription2 = manager.Subscribe("users", onChange: (sender, args) => receivedEvents2.Add(args.Event));

        var evt = ChangeStreamEvent.CreateInsert("users", CreateDocument("doc1"));

        // Act
        manager.PublishEvent(evt);

        // Allow time for async delivery
        Thread.Sleep(100);

        // Assert
        Assert.Single(receivedEvents1);
        Assert.Single(receivedEvents2);
    }

    [Fact]
    public void ChangeStreamManager_GetStatistics_ReturnsCorrectCounts()
    {
        // Arrange
        var manager = new ChangeStreamManager();
        var subscription = manager.Subscribe("users");

        // Act
        var stats = manager.GetStatistics();

        // Assert
        Assert.Equal(1, stats.ActiveSubscriptionCount);
        Assert.Equal(0, stats.TotalEventsPublished);
        Assert.True(stats.Uptime.TotalSeconds >= 0);
    }

    [Fact]
    public void ChangeStreamManager_GetSubscriptionsForCollection_ReturnsMatchingSubscriptions()
    {
        // Arrange
        var manager = new ChangeStreamManager();
        var sub1 = manager.Subscribe("users");
        var sub2 = manager.Subscribe("products");
        var sub3 = manager.SubscribeToAll();

        // Act
        var userSubs = manager.GetSubscriptionsForCollection("users");

        // Assert
        Assert.Equal(2, userSubs.Count); // sub1 + sub3 (all collections)
    }

    [Fact]
    public void ChangeStreamManager_ChangePublished_EventRaised()
    {
        // Arrange
        var manager = new ChangeStreamManager();
        IChangeStreamEvent? receivedEvent = null;
        manager.ChangePublished += (sender, args) =>
        {
            receivedEvent = args.Event;
        };

        var evt = ChangeStreamEvent.CreateInsert("users", CreateDocument("doc1"));

        // Act
        manager.PublishEvent(evt);

        // Assert
        Assert.NotNull(receivedEvent);
        Assert.Equal(evt.DocumentId, receivedEvent.DocumentId);
    }

    [Fact]
    public async Task ChangeStreamManager_PublishEventAsync_PublishesEvent()
    {
        // Arrange
        var manager = new ChangeStreamManager();
        var receivedEvents = new List<IChangeStreamEvent>();
        var subscription = manager.Subscribe("users", onChange: (sender, args) => receivedEvents.Add(args.Event));

        var evt = ChangeStreamEvent.CreateInsert("users", CreateDocument("doc1"));

        // Act
        await manager.PublishEventAsync(evt);

        // Allow time for async delivery
        await Task.Delay(100);

        // Assert
        Assert.Single(receivedEvents);
    }

    [Fact]
    public void ChangeStreamManager_Dispose_CleansUpSubscriptions()
    {
        // Arrange
        var manager = new ChangeStreamManager();
        var subscription = manager.Subscribe("users");

        // Act
        manager.Dispose();

        // Assert
        Assert.Throws<ObjectDisposedException>(() => manager.Subscribe("products"));
    }

    [Fact]
    public void ChangeStreamManager_Disposed_ThrowsOnPublish()
    {
        // Arrange
        var manager = new ChangeStreamManager();
        manager.Dispose();

        var evt = ChangeStreamEvent.CreateInsert("users", CreateDocument("doc1"));

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => manager.PublishEvent(evt));
    }

    [Fact]
    public void ChangeStreamManager_PublishNullEvent_ThrowsArgumentNullException()
    {
        // Arrange
        var manager = new ChangeStreamManager();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => manager.PublishEvent(null!));
    }

    #endregion

    #region ChangeStreamSubscription Tests

    [Fact]
    public void ChangeStreamSubscription_Constructor_SetsProperties()
    {
        // Arrange & Act
        var subscription = new ChangeStreamSubscription("sub1", "users");

        // Assert
        Assert.Equal("sub1", subscription.SubscriptionId);
        Assert.Equal("users", subscription.CollectionName);
        Assert.True(subscription.IsActive);
        Assert.True(subscription.CreatedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void ChangeStreamSubscription_TryProcessEvent_ReturnsTrueForMatchingCollection()
    {
        // Arrange
        var subscription = new ChangeStreamSubscription("sub1", "users");
        var evt = ChangeStreamEvent.CreateInsert("users", CreateDocument("doc1"));

        // Act
        var result = subscription.TryProcessEvent(evt);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ChangeStreamSubscription_TryProcessEvent_ReturnsFalseForDifferentCollection()
    {
        // Arrange
        var subscription = new ChangeStreamSubscription("sub1", "users");
        var evt = ChangeStreamEvent.CreateInsert("products", CreateDocument("doc1"));

        // Act
        var result = subscription.TryProcessEvent(evt);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ChangeStreamSubscription_TryProcessEvent_InactiveReturnsFalse()
    {
        // Arrange
        var subscription = new ChangeStreamSubscription("sub1", "users");
        subscription.Deactivate();
        var evt = ChangeStreamEvent.CreateInsert("users", CreateDocument("doc1"));

        // Act
        var result = subscription.TryProcessEvent(evt);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ChangeStreamSubscription_ResumeFrom_SetsSequenceNumber()
    {
        // Arrange
        var subscription = new ChangeStreamSubscription("sub1", "users");

        // Act
        subscription.ResumeFrom(100);

        // Assert
        Assert.Equal(100, subscription.LastEventSequence);
    }

    [Fact]
    public void ChangeStreamSubscription_Deactivate_SetsIsActiveFalse()
    {
        // Arrange
        var subscription = new ChangeStreamSubscription("sub1", "users");

        // Act
        subscription.Deactivate();

        // Assert
        Assert.False(subscription.IsActive);
    }

    #endregion

    #region ChangeStreamEnabledDocumentStore Tests

    [Fact]
    public async Task ChangeStreamEnabledDocumentStore_InsertAsync_PublishesEvent()
    {
        // Arrange
        var innerStore = new DocumentStore();
        var manager = new ChangeStreamManager();
        var store = new ChangeStreamEnabledDocumentStore(innerStore, manager);

        IChangeStreamEvent? capturedEvent = null;
        manager.Subscribe("users", onChange: (sender, args) => capturedEvent = args.Event);

        // Create a document with an ID
        var docId = Guid.NewGuid().ToString();
        var document = CreateDocument(docId, new Dictionary<string, object> { { "name", "Test" } });

        // Act
        var result = await store.InsertAsync("users", document);

        // Allow time for async delivery
        await Task.Delay(100);

        // Assert
        Assert.NotNull(capturedEvent);
        Assert.Equal(ChangeOperationType.Insert, capturedEvent.OperationType);
        Assert.Equal("users", capturedEvent.CollectionName);
        Assert.Equal(result.Id, capturedEvent.DocumentId);
    }

    [Fact]
    public async Task ChangeStreamEnabledDocumentStore_DeleteAsync_PublishesEvent()
    {
        // Arrange
        var innerStore = new DocumentStore();
        var manager = new ChangeStreamManager();
        var store = new ChangeStreamEnabledDocumentStore(innerStore, manager);

        // Insert a document first
        var docId = Guid.NewGuid().ToString();
        var document = CreateDocument(docId, new Dictionary<string, object> { { "name", "Test" } });
        await store.InsertAsync("users", document);

        IChangeStreamEvent? capturedEvent = null;
        manager.Subscribe("users", onChange: (sender, args) => capturedEvent = args.Event);

        // Act
        await store.DeleteAsync("users", docId);

        // Allow time for async delivery
        await Task.Delay(100);

        // Assert
        Assert.NotNull(capturedEvent);
        Assert.Equal(ChangeOperationType.Delete, capturedEvent.OperationType);
        Assert.Equal(docId, capturedEvent.DocumentId);
    }

    [Fact]
    public async Task ChangeStreamEnabledDocumentStore_DropCollectionAsync_PublishesEvent()
    {
        // Arrange
        var innerStore = new DocumentStore();
        var manager = new ChangeStreamManager();
        var store = new ChangeStreamEnabledDocumentStore(innerStore, manager);

        // Create a collection first
        await innerStore.InsertAsync("users", CreateDocument(Guid.NewGuid().ToString()));

        IChangeStreamEvent? capturedEvent = null;
        manager.Subscribe("users", onChange: (sender, args) => capturedEvent = args.Event);

        // Act
        await store.DropCollectionAsync("users");

        // Allow time for async delivery
        await Task.Delay(100);

        // Assert
        Assert.NotNull(capturedEvent);
        Assert.Equal(ChangeOperationType.Drop, capturedEvent.OperationType);
        Assert.Equal("users", capturedEvent.CollectionName);
    }

    [Fact]
    public async Task ChangeStreamEnabledDocumentStore_GetAsync_ReturnsDocument()
    {
        // Arrange
        var innerStore = new DocumentStore();
        var manager = new ChangeStreamManager();
        var store = new ChangeStreamEnabledDocumentStore(innerStore, manager);

        var docId = Guid.NewGuid().ToString();
        var document = CreateDocument(docId, new Dictionary<string, object> { { "name", "Test" } });
        await innerStore.InsertAsync("users", document);

        // Act
        var result = await store.GetAsync("users", docId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(docId, result!.Id);
    }

    [Fact]
    public async Task ChangeStreamEnabledDocumentStore_ExistsAsync_ReturnsCorrectResult()
    {
        // Arrange
        var innerStore = new DocumentStore();
        var manager = new ChangeStreamManager();
        var store = new ChangeStreamEnabledDocumentStore(innerStore, manager);

        var docId = Guid.NewGuid().ToString();
        await innerStore.InsertAsync("users", CreateDocument(docId));

        // Act & Assert
        Assert.True(await store.ExistsAsync("users", docId));
        Assert.False(await store.ExistsAsync("users", "nonexistent-id"));
    }

    [Fact]
    public async Task ChangeStreamEnabledDocumentStore_CountAsync_ReturnsCorrectCount()
    {
        // Arrange
        var innerStore = new DocumentStore();
        var manager = new ChangeStreamManager();
        var store = new ChangeStreamEnabledDocumentStore(innerStore, manager);

        await innerStore.InsertAsync("users", CreateDocument(Guid.NewGuid().ToString()));
        await innerStore.InsertAsync("users", CreateDocument(Guid.NewGuid().ToString()));

        // Act
        var count = await store.CountAsync("users");

        // Assert
        Assert.Equal(2, count);
    }

    [Fact]
    public void ChangeStreamEnabledDocumentStore_Constructor_NullInnerStore_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ChangeStreamEnabledDocumentStore(null!, new ChangeStreamManager()));
    }

    [Fact]
    public void ChangeStreamEnabledDocumentStore_Constructor_NullManager_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ChangeStreamEnabledDocumentStore(new DocumentStore(), null!));
    }

    [Fact]
    public void ChangeStreamEnabledDocumentStore_Dispose_DisposesInnerStore()
    {
        // Arrange
        var innerStore = new DocumentStore();
        var manager = new ChangeStreamManager();
        var store = new ChangeStreamEnabledDocumentStore(innerStore, manager);

        // Act
        store.Dispose();

        // Assert - no exception means success
        Assert.True(true);
    }

    #endregion

    #region Statistics Tests

    [Fact]
    public void ChangeStreamStatistics_Uptime_CalculatesCorrectly()
    {
        // Arrange
        var stats = new ChangeStreamStatistics
        {
            StartTime = DateTime.UtcNow.AddMinutes(-5)
        };

        // Act
        var uptime = stats.Uptime;

        // Assert
        Assert.True(uptime.TotalMinutes >= 4.9 && uptime.TotalMinutes <= 5.1);
    }

    #endregion
}
