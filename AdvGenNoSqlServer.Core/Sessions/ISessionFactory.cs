// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Abstractions;
using AdvGenNoSqlServer.Core.Transactions;

namespace AdvGenNoSqlServer.Core.Sessions;

/// <summary>
/// Factory interface for creating database sessions
/// </summary>
public interface ISessionFactory
{
    /// <summary>
    /// The document store used by sessions created from this factory
    /// </summary>
    IDocumentStore DocumentStore { get; }

    /// <summary>
    /// The transaction coordinator used by sessions created from this factory
    /// </summary>
    ITransactionCoordinator TransactionCoordinator { get; }

    /// <summary>
    /// The default options for sessions created from this factory
    /// </summary>
    SessionOptions DefaultOptions { get; }

    /// <summary>
    /// Creates a new session with default options
    /// </summary>
    /// <returns>A new session instance</returns>
    ISession CreateSession();

    /// <summary>
    /// Creates a new session with the specified options
    /// </summary>
    /// <param name="options">The session options</param>
    /// <returns>A new session instance</returns>
    ISession CreateSession(SessionOptions options);

    /// <summary>
    /// Creates a new session asynchronously with default options
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A new session instance</returns>
    Task<ISession> CreateSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new session asynchronously with the specified options
    /// </summary>
    /// <param name="options">The session options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A new session instance</returns>
    Task<ISession> CreateSessionAsync(SessionOptions options, CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for configuring the session factory
/// </summary>
public class SessionFactoryOptions
{
    /// <summary>
    /// The default isolation level for sessions
    /// </summary>
    public IsolationLevel DefaultIsolationLevel { get; set; } = IsolationLevel.ReadCommitted;

    /// <summary>
    /// Default transaction timeout in milliseconds
    /// </summary>
    public int DefaultTransactionTimeoutMs { get; set; } = 30000;

    /// <summary>
    /// Automatically begin transactions when sessions are created
    /// </summary>
    public bool AutoBeginTransaction { get; set; } = true;

    /// <summary>
    /// Enable change tracking by default
    /// </summary>
    public bool EnableChangeTracking { get; set; } = true;

    /// <summary>
    /// Maximum number of sessions that can be created (-1 for unlimited)
    /// </summary>
    public int MaxConcurrentSessions { get; set; } = -1;

    /// <summary>
    /// Enable session pooling for better performance
    /// </summary>
    public bool EnableSessionPooling { get; set; } = false;
}
