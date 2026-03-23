// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;
using AdvGenNoSqlServer.Core.Abstractions;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Core.Transactions;

namespace AdvGenNoSqlServer.Core.Sessions;

/// <summary>
/// Default implementation of ISession following the Unit of Work pattern
/// </summary>
public class Session : ISession
{
    private readonly IDocumentStore _documentStore;
    private readonly ITransactionCoordinator _transactionCoordinator;
    private readonly ChangeTracker _changeTracker;
    private readonly ConcurrentDictionary<string, Document> _deletedEntities = new();
    private SessionState _state;
    private string? _currentTransactionId;
    private bool _isDisposed;

    /// <inheritdoc />
    public string SessionId { get; }

    /// <inheritdoc />
    public SessionState State
    {
        get => _state;
        private set
        {
            if (_state != value)
            {
                var oldState = _state;
                _state = value;
                OnStateChanged(oldState, value);
            }
        }
    }

    /// <inheritdoc />
    public SessionOptions Options { get; }

    /// <inheritdoc />
    public IChangeTracker ChangeTracker => _changeTracker;

    /// <inheritdoc />
    public string? CurrentTransactionId => _currentTransactionId;

    /// <inheritdoc />
    public event EventHandler<SessionStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Creates a new session
    /// </summary>
    public Session(IDocumentStore documentStore, ITransactionCoordinator transactionCoordinator, SessionOptions? options = null)
    {
        _documentStore = documentStore ?? throw new ArgumentNullException(nameof(documentStore));
        _transactionCoordinator = transactionCoordinator ?? throw new ArgumentNullException(nameof(transactionCoordinator));
        Options = options ?? SessionOptions.Default;

        SessionId = Guid.NewGuid().ToString("N")[0..16];
        _changeTracker = new ChangeTracker(Options.ThrowOnDuplicateTracking);
        _state = SessionState.Open;

        // Auto-begin transaction if configured
        if (Options.AutoBeginTransaction)
        {
            BeginTransactionAsync(Options.IsolationLevel).GetAwaiter().GetResult();
        }
    }

    /// <inheritdoc />
    public async Task<string> BeginTransactionAsync(IsolationLevel? isolationLevel = null, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();

        if (_currentTransactionId != null)
        {
            throw new InvalidOperationException("A transaction is already active in this session");
        }

        var options = new TransactionOptions
        {
            IsolationLevel = isolationLevel ?? Options.IsolationLevel,
            Timeout = TimeSpan.FromMilliseconds(Options.TransactionTimeoutMs)
        };

        var context = await _transactionCoordinator.BeginTransactionAsync(options, cancellationToken);
        _currentTransactionId = context.TransactionId;
        State = SessionState.Active;

        return _currentTransactionId;
    }

    /// <inheritdoc />
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        EnsureTransactionActive();

        // Save any pending changes first
        await SaveChangesAsync(cancellationToken);

        await _transactionCoordinator.CommitAsync(_currentTransactionId!, cancellationToken);

        // Clear change tracker after successful commit
        _changeTracker.Clear();
        _deletedEntities.Clear();

        _currentTransactionId = null;
        State = SessionState.Committed;
    }

    /// <summary>
    /// Commits the current transaction and returns whether it succeeded
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if committed successfully</returns>
    public async Task<bool> TryCommitAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        EnsureTransactionActive();

        await _transactionCoordinator.RollbackAsync(_currentTransactionId!, cancellationToken);

        // Clear change tracker after rollback
        _changeTracker.Clear();
        _deletedEntities.Clear();

        _currentTransactionId = null;
        State = SessionState.RolledBack;
    }

    /// <inheritdoc />
    public async Task<Document?> GetAsync(string collectionName, string documentId, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();

        // Check if the entity is already tracked
        var tracked = _changeTracker.GetTrackedEntity(collectionName, documentId);
        if (tracked != null)
        {
            if (tracked.State == EntityState.Deleted)
            {
                return null;
            }
            return tracked.Entity;
        }

        // Load from store
        var document = await _documentStore.GetAsync(collectionName, documentId);

        if (document != null && Options.EnableChangeTracking)
        {
            _changeTracker.TrackUnchanged(collectionName, document);
        }

        return document;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Document>> GetManyAsync(string collectionName, IEnumerable<string> documentIds, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();

        var ids = documentIds.ToList();
        var results = new List<Document>();
        var idsToLoad = new List<string>();

        // Check tracked entities first
        foreach (var id in ids)
        {
            var tracked = _changeTracker.GetTrackedEntity(collectionName, id);
            if (tracked != null)
            {
                if (tracked.State != EntityState.Deleted)
                {
                    results.Add(tracked.Entity);
                }
            }
            else
            {
                idsToLoad.Add(id);
            }
        }

        // Load remaining from store
        if (idsToLoad.Count > 0)
        {
            var documents = await _documentStore.GetManyAsync(collectionName, idsToLoad);
            foreach (var doc in documents)
            {
                if (Options.EnableChangeTracking)
                {
                    _changeTracker.TrackUnchanged(collectionName, doc);
                }
                results.Add(doc);
            }
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Document>> GetAllAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();

        // Note: This loads all documents fresh from the store
        // Tracked entities are not merged for GetAll operations
        var documents = await _documentStore.GetAllAsync(collectionName);

        if (Options.EnableChangeTracking)
        {
            foreach (var doc in documents)
            {
                if (!_changeTracker.IsTracked(collectionName, doc.Id))
                {
                    _changeTracker.TrackUnchanged(collectionName, doc);
                }
            }
        }

        return documents;
    }

    /// <inheritdoc />
    public Task<Document> InsertAsync(string collectionName, Document document, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        EnsureTransactionActive();

        if (Options.EnableChangeTracking)
        {
            _changeTracker.TrackAdded(collectionName, document);
        }
        else
        {
            // If change tracking is disabled, insert immediately
            return _documentStore.InsertAsync(collectionName, document);
        }

        return Task.FromResult(document);
    }

    /// <inheritdoc />
    public Task<Document> UpdateAsync(string collectionName, Document document, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        EnsureTransactionActive();

        if (Options.EnableChangeTracking)
        {
            _changeTracker.TrackModified(collectionName, document);
        }
        else
        {
            // If change tracking is disabled, update immediately
            return _documentStore.UpdateAsync(collectionName, document);
        }

        return Task.FromResult(document);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string collectionName, string documentId, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        EnsureTransactionActive();

        if (Options.EnableChangeTracking)
        {
            _changeTracker.TrackDeleted(collectionName, documentId);
            _deletedEntities[documentId] = new Document { Id = documentId };
            return Task.FromResult(true);
        }
        else
        {
            // If change tracking is disabled, delete immediately
            return _documentStore.DeleteAsync(collectionName, documentId);
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string collectionName, string documentId, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();

        // Check change tracker first
        var tracked = _changeTracker.GetTrackedEntity(collectionName, documentId);
        if (tracked != null)
        {
            return tracked.State != EntityState.Deleted;
        }

        return await _documentStore.ExistsAsync(collectionName, documentId);
    }

    /// <inheritdoc />
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        EnsureTransactionActive();

        if (!Options.EnableChangeTracking)
        {
            return 0;
        }

        // Detect any changes in unchanged entities
        _changeTracker.DetectChanges();

        int affectedCount = 0;

        // Process deletions first
        foreach (var deleted in _changeTracker.GetEntities(EntityState.Deleted))
        {
            await _documentStore.DeleteAsync(deleted.CollectionName, deleted.Entity.Id);
            affectedCount++;
        }

        // Process inserts
        foreach (var added in _changeTracker.GetEntities(EntityState.Added))
        {
            await _documentStore.InsertAsync(added.CollectionName, added.Entity);
            added.State = EntityState.Unchanged;
            affectedCount++;
        }

        // Process updates
        foreach (var modified in _changeTracker.GetEntities(EntityState.Modified))
        {
            await _documentStore.UpdateAsync(modified.CollectionName, modified.Entity);
            modified.State = EntityState.Unchanged;
            affectedCount++;
        }

        return affectedCount;
    }

    /// <inheritdoc />
    public void ClearChangeTracker()
    {
        _changeTracker.Clear();
        _deletedEntities.Clear();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            if (_currentTransactionId != null)
            {
                if (Options.AutoCommitOnDispose && _changeTracker.HasChanges())
                {
                    await CommitAsync();
                }
                else
                {
                    await RollbackAsync();
                }
            }
        }
        catch (Exception)
        {
            // Best effort - don't throw from dispose
        }
        finally
        {
            _changeTracker.Clear();
            _deletedEntities.Clear();
            State = SessionState.Disposed;
            _isDisposed = true;
        }
    }

    /// <summary>
    /// Ensures the session is not disposed
    /// </summary>
    private void EnsureNotDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(Session), "The session has been disposed");
        }
    }

    /// <summary>
    /// Ensures a transaction is active
    /// </summary>
    private void EnsureTransactionActive()
    {
        if (_currentTransactionId == null)
        {
            throw new InvalidOperationException("No active transaction. Call BeginTransactionAsync first.");
        }
    }

    /// <summary>
    /// Raises the StateChanged event
    /// </summary>
    protected virtual void OnStateChanged(SessionState oldState, SessionState newState)
    {
        StateChanged?.Invoke(this, new SessionStateChangedEventArgs(oldState, newState));
    }
}

/// <summary>
/// Exception thrown when a session operation fails
/// </summary>
public class SessionException : Exception
{
    public SessionException(string message) : base(message) { }
    public SessionException(string message, Exception innerException) : base(message, innerException) { }
}
