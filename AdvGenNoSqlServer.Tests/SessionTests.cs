// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Abstractions;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Core.Sessions;
using AdvGenNoSqlServer.Core.Transactions;
using AdvGenNoSqlServer.Storage;
using Moq;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for the Session/Unit of Work pattern implementation
/// </summary>
public class SessionTests : IDisposable
{
    private readonly Mock<IDocumentStore> _mockDocumentStore;
    private readonly Mock<ITransactionCoordinator> _mockTransactionCoordinator;
    private readonly Mock<ITransactionContext> _mockTransactionContext;

    public SessionTests()
    {
        _mockDocumentStore = new Mock<IDocumentStore>();
        _mockTransactionCoordinator = new Mock<ITransactionCoordinator>();
        _mockTransactionContext = new Mock<ITransactionContext>();

        _mockTransactionContext.Setup(x => x.TransactionId).Returns("txn_test_123");
        _mockTransactionCoordinator
            .Setup(x => x.BeginTransactionAsync(It.IsAny<TransactionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_mockTransactionContext.Object);
    }

    public void Dispose()
    {
        // Cleanup
    }

    #region Session Creation and Lifecycle

    [Fact]
    public void Session_Constructor_WithAutoBeginTransaction_ShouldBeginTransaction()
    {
        // Arrange
        var options = new SessionOptions { AutoBeginTransaction = true };

        // Act
        var session = new Session(_mockDocumentStore.Object, _mockTransactionCoordinator.Object, options);

        // Assert
        Assert.NotNull(session.SessionId);
        Assert.Equal(SessionState.Active, session.State);
        Assert.NotNull(session.CurrentTransactionId);
        _mockTransactionCoordinator.Verify(x => x.BeginTransactionAsync(It.Is<TransactionOptions>(o => o.IsolationLevel == options.IsolationLevel), default), Times.Once);
    }

    [Fact]
    public void Session_Constructor_WithoutAutoBeginTransaction_ShouldBeOpen()
    {
        // Arrange
        var options = new SessionOptions { AutoBeginTransaction = false };

        // Act
        var session = new Session(_mockDocumentStore.Object, _mockTransactionCoordinator.Object, options);

        // Assert
        Assert.Equal(SessionState.Open, session.State);
        Assert.Null(session.CurrentTransactionId);
        _mockTransactionCoordinator.Verify(x => x.BeginTransactionAsync(It.IsAny<TransactionOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Session_Dispose_ShouldDisposeResources()
    {
        // Arrange
        var session = new Session(_mockDocumentStore.Object, _mockTransactionCoordinator.Object);

        // Act
        session.Dispose();

        // Assert
        Assert.Equal(SessionState.Disposed, session.State);
    }

    [Fact]
    public async Task Session_DisposeAsync_ShouldDisposeResources()
    {
        // Arrange
        var session = new Session(_mockDocumentStore.Object, _mockTransactionCoordinator.Object);

        // Act
        await session.DisposeAsync();

        // Assert
        Assert.Equal(SessionState.Disposed, session.State);
    }

    [Fact]
    public void Session_Operations_AfterDispose_ShouldThrow()
    {
        // Arrange
        var session = new Session(_mockDocumentStore.Object, _mockTransactionCoordinator.Object);
        session.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => session.GetAsync("test", "id").GetAwaiter().GetResult());
    }

    #endregion

    #region Transaction Management

    [Fact]
    public async Task BeginTransactionAsync_WithNoActiveTransaction_ShouldBeginTransaction()
    {
        // Arrange
        var options = new SessionOptions { AutoBeginTransaction = false };
        var session = new Session(_mockDocumentStore.Object, _mockTransactionCoordinator.Object, options);

        // Act
        var txnId = await session.BeginTransactionAsync(IsolationLevel.Serializable);

        // Assert
        Assert.NotNull(txnId);
        Assert.Equal(SessionState.Active, session.State);
        _mockTransactionCoordinator.Verify(x => x.BeginTransactionAsync(It.Is<TransactionOptions>(o => o.IsolationLevel == IsolationLevel.Serializable), default), Times.Once);
    }

    [Fact]
    public async Task BeginTransactionAsync_WithActiveTransaction_ShouldThrow()
    {
        // Arrange
        var session = new Session(_mockDocumentStore.Object, _mockTransactionCoordinator.Object);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => session.BeginTransactionAsync());
        Assert.Contains("already active", ex.Message);
    }

    [Fact]
    public async Task CommitAsync_WithActiveTransaction_ShouldCommit()
    {
        // Arrange
        var session = new Session(_mockDocumentStore.Object, _mockTransactionCoordinator.Object);
        var transactionId = session.CurrentTransactionId;

        // Act
        await session.CommitAsync();

        // Assert
        _mockTransactionCoordinator.Verify(x => x.CommitAsync(transactionId!, default), Times.Once);
        Assert.Equal(SessionState.Committed, session.State);
        Assert.Null(session.CurrentTransactionId);
    }

    [Fact]
    public async Task CommitAsync_WithoutTransaction_ShouldThrow()
    {
        // Arrange
        var options = new SessionOptions { AutoBeginTransaction = false };
        var session = new Session(_mockDocumentStore.Object, _mockTransactionCoordinator.Object, options);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => session.CommitAsync());
        Assert.Contains("No active transaction", ex.Message);
    }

    [Fact]
    public async Task RollbackAsync_WithActiveTransaction_ShouldRollback()
    {
        // Arrange
        var session = new Session(_mockDocumentStore.Object, _mockTransactionCoordinator.Object);
        var transactionId = session.CurrentTransactionId;

        // Act
        await session.RollbackAsync();

        // Assert
        _mockTransactionCoordinator.Verify(x => x.RollbackAsync(transactionId!, default), Times.Once);
        Assert.Equal(SessionState.RolledBack, session.State);
        Assert.Null(session.CurrentTransactionId);
    }

    [Fact]
    public async Task RollbackAsync_WithoutTransaction_ShouldThrow()
    {
        // Arrange
        var options = new SessionOptions { AutoBeginTransaction = false };
        var session = new Session(_mockDocumentStore.Object, _mockTransactionCoordinator.Object, options);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => session.RollbackAsync());
        Assert.Contains("No active transaction", ex.Message);
    }

    #endregion

    #region Document Operations with Change Tracking

    [Fact]
    public async Task GetAsync_WithChangeTracking_ShouldTrackEntity()
    {
        // Arrange
        var doc = new Document { Id = "doc1", Data = new Dictionary<string, object?> { ["name"] = "Test" } };
        _mockDocumentStore.Setup(x => x.GetAsync("collection", "doc1", default)).ReturnsAsync(doc);

        var session = new Session(_mockDocumentStore.Object, _mockTransactionCoordinator.Object);

        // Act
        var result = await session.GetAsync("collection", "doc1");

        // Assert
        Assert.NotNull(result);
        Assert.True(session.ChangeTracker.IsTracked("collection", "doc1"));
        Assert.Equal(EntityState.Unchanged, session.ChangeTracker.GetEntityState("collection", "doc1"));
    }

    [Fact]
    public async Task GetAsync_WithoutChangeTracking_ShouldNotTrackEntity()
    {
        // Arrange
        var doc = new Document { Id = "doc1", Data = new Dictionary<string, object?> { ["name"] = "Test" } };
        _mockDocumentStore.Setup(x => x.GetAsync("collection", "doc1", default)).ReturnsAsync(doc);

        var options = new SessionOptions { EnableChangeTracking = false, AutoBeginTransaction = false };
        var session = new Session(_mockDocumentStore.Object, _mockTransactionCoordinator.Object, options);
        await session.BeginTransactionAsync();

        // Act
        var result = await session.GetAsync("collection", "doc1");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0, session.ChangeTracker.Count);
    }

    [Fact]
    public async Task InsertAsync_WithChangeTracking_ShouldTrackAsAdded()
    {
        // Arrange
        var doc = new Document { Id = "doc1", Data = new Dictionary<string, object?> { ["name"] = "Test" } };
        var session = new Session(_mockDocumentStore.Object, _mockTransactionCoordinator.Object);

        // Act
        var result = await session.InsertAsync("collection", doc);

        // Assert
        Assert.True(session.ChangeTracker.IsTracked("collection", "doc1"));
        Assert.Equal(EntityState.Added, session.ChangeTracker.GetEntityState("collection", "doc1"));
        _mockDocumentStore.Verify(x => x.InsertAsync(It.IsAny<string>(), It.IsAny<Document>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InsertAsync_WithoutChangeTracking_ShouldInsertImmediately()
    {
        // Arrange
        var doc = new Document { Id = "doc1", Data = new Dictionary<string, object?> { ["name"] = "Test" } };
        var options = new SessionOptions { EnableChangeTracking = false, AutoBeginTransaction = false };
        var session = new Session(_mockDocumentStore.Object, _mockTransactionCoordinator.Object, options);
        await session.BeginTransactionAsync();

        // Act
        await session.InsertAsync("collection", doc);

        // Assert
        _mockDocumentStore.Verify(x => x.InsertAsync("collection", doc, default), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_WithChangeTracking_ShouldTrackAsModified()
    {
        // Arrange
        var doc = new Document { Id = "doc1", Data = new Dictionary<string, object?> { ["name"] = "Updated" } };
        var session = new Session(_mockDocumentStore.Object, _mockTransactionCoordinator.Object);

        // Act
        var result = await session.UpdateAsync("collection", doc);

        // Assert
        Assert.True(session.ChangeTracker.IsTracked("collection", "doc1"));
        Assert.Equal(EntityState.Modified, session.ChangeTracker.GetEntityState("collection", "doc1"));
    }

    [Fact]
    public async Task DeleteAsync_WithChangeTracking_ShouldTrackAsDeleted()
    {
        // Arrange
        var session = new Session(_mockDocumentStore.Object, _mockTransactionCoordinator.Object);

        // Act
        var result = await session.DeleteAsync("collection", "doc1");

        // Assert
        Assert.True(result);
        Assert.True(session.ChangeTracker.IsTracked("collection", "doc1"));
        Assert.Equal(EntityState.Deleted, session.ChangeTracker.GetEntityState("collection", "doc1"));
    }

    [Fact]
    public async Task DeleteAsync_WithoutChangeTracking_ShouldDeleteImmediately()
    {
        // Arrange
        _mockDocumentStore.Setup(x => x.DeleteAsync("collection", "doc1", default)).ReturnsAsync(true);
        var options = new SessionOptions { EnableChangeTracking = false, AutoBeginTransaction = false };
        var session = new Session(_mockDocumentStore.Object, _mockTransactionCoordinator.Object, options);
        await session.BeginTransactionAsync();

        // Act
        var result = await session.DeleteAsync("collection", "doc1");

        // Assert
        Assert.True(result);
        _mockDocumentStore.Verify(x => x.DeleteAsync("collection", "doc1", default), Times.Once);
    }

    #endregion

    #region SaveChanges

    [Fact]
    public async Task SaveChangesAsync_WithAddedEntities_ShouldInsertToStore()
    {
        // Arrange
        var doc = new Document { Id = "doc1", Data = new Dictionary<string, object?> { ["name"] = "Test" } };
        var session = new Session(_mockDocumentStore.Object, _mockTransactionCoordinator.Object);
        await session.InsertAsync("collection", doc);

        // Act
        var affected = await session.SaveChangesAsync();

        // Assert
        Assert.Equal(1, affected);
        _mockDocumentStore.Verify(x => x.InsertAsync("collection", doc, default), Times.Once);
        Assert.Equal(EntityState.Unchanged, session.ChangeTracker.GetEntityState("collection", "doc1"));
    }

    [Fact]
    public async Task SaveChangesAsync_WithModifiedEntities_ShouldUpdateStore()
    {
        // Arrange
        var doc = new Document { Id = "doc1", Data = new Dictionary<string, object?> { ["name"] = "Updated" } };
        var session = new Session(_mockDocumentStore.Object, _mockTransactionCoordinator.Object);
        await session.UpdateAsync("collection", doc);

        // Act
        var affected = await session.SaveChangesAsync();

        // Assert
        Assert.Equal(1, affected);
        _mockDocumentStore.Verify(x => x.UpdateAsync("collection", doc, default), Times.Once);
        Assert.Equal(EntityState.Unchanged, session.ChangeTracker.GetEntityState("collection", "doc1"));
    }

    [Fact]
    public async Task SaveChangesAsync_WithDeletedEntities_ShouldDeleteFromStore()
    {
        // Arrange
        var session = new Session(_mockDocumentStore.Object, _mockTransactionCoordinator.Object);
        await session.DeleteAsync("collection", "doc1");

        // Act
        var affected = await session.SaveChangesAsync();

        // Assert
        Assert.Equal(1, affected);
        _mockDocumentStore.Verify(x => x.DeleteAsync("collection", "doc1", default), Times.Once);
    }

    [Fact]
    public async Task SaveChangesAsync_WithMultipleChanges_ShouldApplyAll()
    {
        // Arrange
        var doc1 = new Document { Id = "doc1", Data = new Dictionary<string, object?> { ["name"] = "New" } };
        var doc2 = new Document { Id = "doc2", Data = new Dictionary<string, object?> { ["name"] = "Updated" } };
        var session = new Session(_mockDocumentStore.Object, _mockTransactionCoordinator.Object);

        await session.InsertAsync("collection", doc1);
        await session.UpdateAsync("collection", doc2);
        await session.DeleteAsync("collection", "doc3");

        // Act
        var affected = await session.SaveChangesAsync();

        // Assert
        Assert.Equal(3, affected);
        _mockDocumentStore.Verify(x => x.InsertAsync("collection", doc1, default), Times.Once);
        _mockDocumentStore.Verify(x => x.UpdateAsync("collection", doc2, default), Times.Once);
        _mockDocumentStore.Verify(x => x.DeleteAsync("collection", "doc3", default), Times.Once);
    }

    [Fact]
    public async Task SaveChangesAsync_WithoutTransaction_ShouldThrow()
    {
        // Arrange
        var options = new SessionOptions { AutoBeginTransaction = false };
        var session = new Session(_mockDocumentStore.Object, _mockTransactionCoordinator.Object, options);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => session.SaveChangesAsync());
        Assert.Contains("No active transaction", ex.Message);
    }

    #endregion

    #region Change Tracker Events

    [Fact]
    public void ChangeTracker_EntityTracked_ShouldRaiseEvent()
    {
        // Arrange
        var tracker = new ChangeTracker();
        var eventRaised = false;
        TrackedEntity? trackedEntity = null;

        tracker.EntityTracked += (s, e) =>
        {
            eventRaised = true;
            trackedEntity = e.TrackedEntity;
        };

        var doc = new Document { Id = "doc1", Data = new Dictionary<string, object?> { ["name"] = "Test" } };

        // Act
        tracker.TrackAdded("collection", doc);

        // Assert
        Assert.True(eventRaised);
        Assert.NotNull(trackedEntity);
        Assert.Equal("doc1", trackedEntity.Entity.Id);
        Assert.Equal("collection", trackedEntity.CollectionName);
    }

    [Fact]
    public void ChangeTracker_EntityStateChanged_ShouldRaiseEvent()
    {
        // Arrange
        var tracker = new ChangeTracker();
        var doc = new Document { Id = "doc1", Data = new Dictionary<string, object?> { ["name"] = "Test" } };
        tracker.TrackUnchanged("collection", doc);

        var eventRaised = false;
        EntityState? oldState = null;
        EntityState? newState = null;

        tracker.EntityStateChanged += (s, e) =>
        {
            eventRaised = true;
            oldState = e.OldState;
            newState = e.NewState;
        };

        // Act - modify the document and track as modified
        tracker.TrackModified("collection", doc);

        // Assert
        Assert.True(eventRaised);
        Assert.Equal(EntityState.Unchanged, oldState);
        Assert.Equal(EntityState.Modified, newState);
    }

    #endregion

    #region Session Events

    [Fact]
    public void Session_StateChanged_ShouldRaiseEvent()
    {
        // Arrange
        var options = new SessionOptions { AutoBeginTransaction = false };
        var session = new Session(_mockDocumentStore.Object, _mockTransactionCoordinator.Object, options);

        var eventRaised = false;
        SessionState? oldState = null;
        SessionState? newState = null;

        session.StateChanged += (s, e) =>
        {
            eventRaised = true;
            oldState = e.OldState;
            newState = e.NewState;
        };

        // Act
        session.BeginTransactionAsync().GetAwaiter().GetResult();

        // Assert
        Assert.True(eventRaised);
        Assert.Equal(SessionState.Open, oldState);
        Assert.Equal(SessionState.Active, newState);
    }

    #endregion

    #region Session Factory

    [Fact]
    public void SessionFactory_CreateSession_ShouldCreateSession()
    {
        // Arrange
        var factory = new SessionFactory(_mockDocumentStore.Object, _mockTransactionCoordinator.Object);

        // Act
        var session = factory.CreateSession();

        // Assert
        Assert.NotNull(session);
        Assert.Single(factory.GetActiveSessions());
    }

    [Fact]
    public void SessionFactory_CreateSession_WithOptions_ShouldApplyOptions()
    {
        // Arrange
        var factory = new SessionFactory(_mockDocumentStore.Object, _mockTransactionCoordinator.Object);
        var options = new SessionOptions { AutoBeginTransaction = false };

        // Act
        var session = factory.CreateSession(options);

        // Assert
        Assert.Equal(SessionState.Open, session.State);
    }

    [Fact]
    public async Task SessionFactory_CreateSessionAsync_ShouldCreateSession()
    {
        // Arrange
        var factory = new SessionFactory(_mockDocumentStore.Object, _mockTransactionCoordinator.Object);

        // Act
        var session = await factory.CreateSessionAsync();

        // Assert
        Assert.NotNull(session);
        Assert.Single(factory.GetActiveSessions());
    }

    [Fact]
    public void SessionFactory_SessionDisposed_ShouldRemoveFromActiveSessions()
    {
        // Arrange
        var factory = new SessionFactory(_mockDocumentStore.Object, _mockTransactionCoordinator.Object);
        var session = factory.CreateSession();
        var sessionId = session.SessionId;

        // Act
        session.Dispose();

        // Assert
        Assert.Empty(factory.GetActiveSessions());
    }

    [Fact]
    public void SessionFactory_MaxConcurrentSessions_ShouldLimitSessions()
    {
        // Arrange
        var factoryOptions = new SessionFactoryOptions { MaxConcurrentSessions = 1 };
        var factory = new SessionFactory(_mockDocumentStore.Object, _mockTransactionCoordinator.Object, factoryOptions);

        // Create first session (should succeed)
        var session1 = factory.CreateSession();
        Assert.NotNull(session1);

        // Try to create second session (should fail)
        Assert.Throws<InvalidOperationException>(() => factory.CreateSession());
    }

    [Fact]
    public void SessionFactory_Dispose_ShouldDisposeAllActiveSessions()
    {
        // Arrange
        var factory = new SessionFactory(_mockDocumentStore.Object, _mockTransactionCoordinator.Object);
        var session1 = factory.CreateSession();
        var session2 = factory.CreateSession();

        // Act
        factory.Dispose();

        // Assert
        Assert.Equal(SessionState.Disposed, session1.State);
        Assert.Equal(SessionState.Disposed, session2.State);
    }

    [Fact]
    public void SessionFactory_SessionCreated_ShouldRaiseEvent()
    {
        // Arrange
        var factory = new SessionFactory(_mockDocumentStore.Object, _mockTransactionCoordinator.Object);
        var eventRaised = false;
        ISession? createdSession = null;

        factory.SessionCreated += (s, e) =>
        {
            eventRaised = true;
            createdSession = e.Session;
        };

        // Act
        var session = factory.CreateSession();

        // Assert
        Assert.True(eventRaised);
        Assert.NotNull(createdSession);
        Assert.Equal(session.SessionId, createdSession.SessionId);
    }

    [Fact]
    public void SessionFactory_SessionDisposed_ShouldRaiseEvent()
    {
        // Arrange
        var factory = new SessionFactory(_mockDocumentStore.Object, _mockTransactionCoordinator.Object);
        var session = factory.CreateSession();

        var eventRaised = false;
        ISession? disposedSession = null;

        factory.SessionDisposed += (s, e) =>
        {
            eventRaised = true;
            disposedSession = e.Session;
        };

        // Act
        session.Dispose();

        // Assert
        Assert.True(eventRaised);
        Assert.NotNull(disposedSession);
        Assert.Equal(session.SessionId, disposedSession.SessionId);
    }

    #endregion

    #region Session Options

    [Fact]
    public void SessionOptions_Default_ShouldHaveConservativeSettings()
    {
        // Act
        var options = SessionOptions.Default;

        // Assert
        Assert.Equal(IsolationLevel.ReadCommitted, options.IsolationLevel);
        Assert.Equal(30000, options.TransactionTimeoutMs);
        Assert.True(options.AutoBeginTransaction);
        Assert.False(options.AutoCommitOnDispose);
        Assert.True(options.EnableChangeTracking);
    }

    [Fact]
    public void SessionOptions_ReadOnly_ShouldDisableTrackingAndTransactions()
    {
        // Act
        var options = SessionOptions.ReadOnly;

        // Assert
        Assert.False(options.AutoBeginTransaction);
        Assert.False(options.EnableChangeTracking);
    }

    [Fact]
    public void SessionOptions_AutoCommit_ShouldEnableAutoCommit()
    {
        // Act
        var options = SessionOptions.AutoCommit;

        // Assert
        Assert.True(options.AutoCommitOnDispose);
    }

    #endregion

    #region ChangeTracker Detection

    [Fact]
    public void ChangeTracker_DetectChanges_WithModifiedEntity_ShouldDetectChange()
    {
        // Arrange
        var tracker = new ChangeTracker();
        var doc = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object?> { ["name"] = "Original" }
        };
        tracker.TrackUnchanged("collection", doc);

        // Act - modify the document data
        doc.Data!["name"] = "Modified";
        var changesDetected = tracker.DetectChanges();

        // Assert
        Assert.Equal(1, changesDetected);
        Assert.Equal(EntityState.Modified, tracker.GetEntityState("collection", "doc1"));
        Assert.True(tracker.HasChanges());
    }

    [Fact]
    public void ChangeTracker_DetectChanges_WithNoChanges_ShouldReturnZero()
    {
        // Arrange
        var tracker = new ChangeTracker();
        var doc = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object?> { ["name"] = "Original" }
        };
        tracker.TrackUnchanged("collection", doc);

        // Act
        var changesDetected = tracker.DetectChanges();

        // Assert
        Assert.Equal(0, changesDetected);
        Assert.False(tracker.HasChanges());
    }

    [Fact]
    public void ChangeTracker_HasChanges_WithOnlyUnchanged_ShouldReturnFalse()
    {
        // Arrange
        var tracker = new ChangeTracker();
        var doc = new Document { Id = "doc1", Data = new Dictionary<string, object?> { ["name"] = "Test" } };
        tracker.TrackUnchanged("collection", doc);

        // Act & Assert
        Assert.False(tracker.HasChanges());
    }

    [Fact]
    public void ChangeTracker_HasChanges_WithAdded_ShouldReturnTrue()
    {
        // Arrange
        var tracker = new ChangeTracker();
        var doc = new Document { Id = "doc1", Data = new Dictionary<string, object?> { ["name"] = "Test" } };
        tracker.TrackAdded("collection", doc);

        // Act & Assert
        Assert.True(tracker.HasChanges());
    }

    [Fact]
    public void ChangeTracker_Clear_ShouldRemoveAllTrackedEntities()
    {
        // Arrange
        var tracker = new ChangeTracker();
        tracker.TrackAdded("collection", new Document { Id = "doc1" });
        tracker.TrackAdded("collection", new Document { Id = "doc2" });

        // Act
        tracker.Clear();

        // Assert
        Assert.Equal(0, tracker.Count);
        Assert.False(tracker.IsTracked("collection", "doc1"));
        Assert.False(tracker.IsTracked("collection", "doc2"));
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task GetAsync_DeletedEntity_ShouldReturnNull()
    {
        // Arrange
        var doc = new Document { Id = "doc1", Data = new Dictionary<string, object?> { ["name"] = "Test" } };
        _mockDocumentStore.Setup(x => x.GetAsync("collection", "doc1", default)).ReturnsAsync(doc);

        var session = new Session(_mockDocumentStore.Object, _mockTransactionCoordinator.Object);
        await session.DeleteAsync("collection", "doc1");

        // Act
        var result = await session.GetAsync("collection", "doc1");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ChangeTracker_TrackDuplicate_WithThrowOnDuplicate_ShouldThrow()
    {
        // Arrange
        var tracker = new ChangeTracker(throwOnDuplicateTracking: true);
        var doc = new Document { Id = "doc1", Data = new Dictionary<string, object?> { ["name"] = "Test" } };
        tracker.TrackAdded("collection", doc);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => tracker.TrackAdded("collection", doc));
    }

    [Fact]
    public async Task InsertAsync_DuplicateTracking_WithoutThrow_ShouldNotThrow()
    {
        // Arrange
        var doc = new Document { Id = "doc1", Data = new Dictionary<string, object?> { ["name"] = "Test" } };
        var options = new SessionOptions { ThrowOnDuplicateTracking = false };
        var session = new Session(_mockDocumentStore.Object, _mockTransactionCoordinator.Object, options);

        // Act
        await session.InsertAsync("collection", doc);
        await session.InsertAsync("collection", doc); // Should not throw

        // Assert
        Assert.Single(session.ChangeTracker.TrackedEntities);
    }

    [Fact]
    public void ChangeTracker_TrackDeleted_AddedEntity_ShouldRemoveFromTracking()
    {
        // Arrange
        var tracker = new ChangeTracker();
        var doc = new Document { Id = "doc1", Data = new Dictionary<string, object?> { ["name"] = "Test" } };
        tracker.TrackAdded("collection", doc);

        // Act
        tracker.TrackDeleted("collection", "doc1");

        // Assert
        Assert.Equal(0, tracker.Count);
        Assert.False(tracker.IsTracked("collection", "doc1"));
    }

    [Fact]
    public void ChangeTracker_TrackModified_DeletedEntity_ShouldThrow()
    {
        // Arrange
        var tracker = new ChangeTracker();
        tracker.TrackDeleted("collection", "doc1");

        // Act & Assert
        var doc = new Document { Id = "doc1", Data = new Dictionary<string, object?> { ["name"] = "Test" } };
        Assert.Throws<InvalidOperationException>(() => tracker.TrackModified("collection", doc));
    }

    [Fact]
    public async Task ExistsAsync_DeletedEntity_ShouldReturnFalse()
    {
        // Arrange
        _mockDocumentStore.Setup(x => x.ExistsAsync("collection", "doc1", default)).ReturnsAsync(true);
        var session = new Session(_mockDocumentStore.Object, _mockTransactionCoordinator.Object);
        await session.DeleteAsync("collection", "doc1");

        // Act
        var exists = await session.ExistsAsync("collection", "doc1");

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public async Task ExistsAsync_AddedEntity_ShouldReturnTrue()
    {
        // Arrange
        _mockDocumentStore.Setup(x => x.ExistsAsync("collection", "doc1", default)).ReturnsAsync(false);
        var session = new Session(_mockDocumentStore.Object, _mockTransactionCoordinator.Object);
        await session.InsertAsync("collection", new Document { Id = "doc1", Data = new Dictionary<string, object?>() });

        // Act
        var exists = await session.ExistsAsync("collection", "doc1");

        // Assert
        Assert.True(exists);
    }

    #endregion
}
