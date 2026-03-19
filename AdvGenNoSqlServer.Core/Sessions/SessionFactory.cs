// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;
using AdvGenNoSqlServer.Core.Transactions;
using AdvGenNoSqlServer.Core.Abstractions;

namespace AdvGenNoSqlServer.Core.Sessions;

/// <summary>
/// Default implementation of the session factory
/// </summary>
public class SessionFactory : ISessionFactory, IDisposable
{
    private readonly IDocumentStore _documentStore;
    private readonly ITransactionCoordinator _transactionCoordinator;
    private readonly SessionFactoryOptions _factoryOptions;
    private readonly ConcurrentDictionary<string, ISession> _activeSessions = new();
    private readonly SemaphoreSlim _sessionLimitSemaphore;
    private bool _isDisposed;

    /// <inheritdoc />
    public IDocumentStore DocumentStore => _documentStore;

    /// <inheritdoc />
    public ITransactionCoordinator TransactionCoordinator => _transactionCoordinator;

    /// <inheritdoc />
    public SessionOptions DefaultOptions { get; }

    /// <summary>
    /// Gets the number of active sessions
    /// </summary>
    public int ActiveSessionCount => _activeSessions.Count;

    /// <summary>
    /// Event raised when a session is created
    /// </summary>
    public event EventHandler<SessionCreatedEventArgs>? SessionCreated;

    /// <summary>
    /// Event raised when a session is disposed
    /// </summary>
    public event EventHandler<SessionDisposedEventArgs>? SessionDisposed;

    /// <summary>
    /// Creates a new session factory
    /// </summary>
    public SessionFactory(
        IDocumentStore documentStore,
        ITransactionCoordinator transactionCoordinator,
        SessionFactoryOptions? factoryOptions = null,
        SessionOptions? defaultOptions = null)
    {
        _documentStore = documentStore ?? throw new ArgumentNullException(nameof(documentStore));
        _transactionCoordinator = transactionCoordinator ?? throw new ArgumentNullException(nameof(transactionCoordinator));
        _factoryOptions = factoryOptions ?? new SessionFactoryOptions();
        DefaultOptions = defaultOptions ?? SessionOptions.Default;

        _sessionLimitSemaphore = _factoryOptions.MaxConcurrentSessions > 0
            ? new SemaphoreSlim(_factoryOptions.MaxConcurrentSessions, _factoryOptions.MaxConcurrentSessions)
            : new SemaphoreSlim(1, 1);
    }

    /// <inheritdoc />
    public ISession CreateSession()
    {
        return CreateSession(DefaultOptions);
    }

    /// <inheritdoc />
    public ISession CreateSession(SessionOptions options)
    {
        EnsureNotDisposed();

        if (_factoryOptions.MaxConcurrentSessions > 0)
        {
            if (!_sessionLimitSemaphore.Wait(0))
            {
                throw new InvalidOperationException($"Maximum number of concurrent sessions ({_factoryOptions.MaxConcurrentSessions}) has been reached");
            }
        }

        try
        {
            var session = new Session(_documentStore, _transactionCoordinator, options);
            session.StateChanged += OnSessionStateChanged;
            _activeSessions[session.SessionId] = session;

            OnSessionCreated(session);
            return session;
        }
        catch
        {
            if (_factoryOptions.MaxConcurrentSessions > 0)
            {
                _sessionLimitSemaphore.Release();
            }
            throw;
        }
    }

    /// <inheritdoc />
    public Task<ISession> CreateSessionAsync(CancellationToken cancellationToken = default)
    {
        return CreateSessionAsync(DefaultOptions, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ISession> CreateSessionAsync(SessionOptions options, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();

        if (_factoryOptions.MaxConcurrentSessions > 0)
        {
            if (!await _sessionLimitSemaphore.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken))
            {
                throw new InvalidOperationException($"Maximum number of concurrent sessions ({_factoryOptions.MaxConcurrentSessions}) has been reached");
            }
        }

        try
        {
            var session = new Session(_documentStore, _transactionCoordinator, options);
            session.StateChanged += OnSessionStateChanged;
            _activeSessions[session.SessionId] = session;

            OnSessionCreated(session);
            return session;
        }
        catch
        {
            if (_factoryOptions.MaxConcurrentSessions > 0)
            {
                _sessionLimitSemaphore.Release();
            }
            throw;
        }
    }

    /// <summary>
    /// Gets all active sessions
    /// </summary>
    public IReadOnlyCollection<ISession> GetActiveSessions()
    {
        return _activeSessions.Values.ToList().AsReadOnly();
    }

    /// <summary>
    /// Gets a session by ID
    /// </summary>
    public ISession? GetSession(string sessionId)
    {
        _activeSessions.TryGetValue(sessionId, out var session);
        return session;
    }

    /// <summary>
    /// Disposes the session factory and all active sessions
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        // Dispose all active sessions
        foreach (var session in _activeSessions.Values)
        {
            try
            {
                session.StateChanged -= OnSessionStateChanged;
                session.Dispose();
            }
            catch (Exception)
            {
                // Best effort - continue disposing other sessions
            }
        }

        _activeSessions.Clear();
        _sessionLimitSemaphore.Dispose();
        _isDisposed = true;
    }

    /// <summary>
    /// Handles session state changes
    /// </summary>
    private void OnSessionStateChanged(object? sender, SessionStateChangedEventArgs e)
    {
        if (sender is ISession session && e.NewState == SessionState.Disposed)
        {
            session.StateChanged -= OnSessionStateChanged;
            _activeSessions.TryRemove(session.SessionId, out _);

            if (_factoryOptions.MaxConcurrentSessions > 0)
            {
                _sessionLimitSemaphore.Release();
            }

            OnSessionDisposed(session);
        }
    }

    /// <summary>
    /// Raises the SessionCreated event
    /// </summary>
    protected virtual void OnSessionCreated(ISession session)
    {
        SessionCreated?.Invoke(this, new SessionCreatedEventArgs(session));
    }

    /// <summary>
    /// Raises the SessionDisposed event
    /// </summary>
    protected virtual void OnSessionDisposed(ISession session)
    {
        SessionDisposed?.Invoke(this, new SessionDisposedEventArgs(session));
    }

    /// <summary>
    /// Ensures the factory is not disposed
    /// </summary>
    private void EnsureNotDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(SessionFactory), "The session factory has been disposed");
        }
    }
}

/// <summary>
/// Event arguments for session created events
/// </summary>
public class SessionCreatedEventArgs : EventArgs
{
    /// <summary>
    /// The session that was created
    /// </summary>
    public ISession Session { get; }

    /// <summary>
    /// When the session was created
    /// </summary>
    public DateTime CreatedAt { get; }

    /// <summary>
    /// Creates new session created event args
    /// </summary>
    public SessionCreatedEventArgs(ISession session)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        CreatedAt = DateTime.UtcNow;
    }
}

/// <summary>
/// Event arguments for session disposed events
/// </summary>
public class SessionDisposedEventArgs : EventArgs
{
    /// <summary>
    /// The session that was disposed
    /// </summary>
    public ISession Session { get; }

    /// <summary>
    /// When the session was disposed
    /// </summary>
    public DateTime DisposedAt { get; }

    /// <summary>
    /// Creates new session disposed event args
    /// </summary>
    public SessionDisposedEventArgs(ISession session)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        DisposedAt = DateTime.UtcNow;
    }
}
