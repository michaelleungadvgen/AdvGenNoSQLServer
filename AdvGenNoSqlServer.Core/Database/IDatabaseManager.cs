// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.Database
{
    /// <summary>
    /// Interface for managing databases in the NoSQL server.
    /// </summary>
    public interface IDatabaseManager
    {
        /// <summary>
        /// Creates a new database.
        /// </summary>
        /// <param name="name">The database name.</param>
        /// <param name="options">Optional database configuration.</param>
        /// <param name="security">Optional security settings.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The created database.</returns>
        /// <exception cref="ArgumentException">Thrown when name is invalid.</exception>
        /// <exception cref="InvalidOperationException">Thrown when database already exists.</exception>
        Task<Database> CreateDatabaseAsync(
            string name,
            DatabaseOptions? options = null,
            DatabaseSecurity? security = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Drops (deletes) a database.
        /// </summary>
        /// <param name="name">The database name.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if the database was dropped; false if it didn't exist.</returns>
        /// <exception cref="InvalidOperationException">Thrown when trying to drop a system database.</exception>
        Task<bool> DropDatabaseAsync(string name, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a database by name.
        /// </summary>
        /// <param name="name">The database name.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The database, or null if not found.</returns>
        Task<Database?> GetDatabaseAsync(string name, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if a database exists.
        /// </summary>
        /// <param name="name">The database name.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if the database exists; otherwise, false.</returns>
        Task<bool> DatabaseExistsAsync(string name, CancellationToken cancellationToken = default);

        /// <summary>
        /// Lists all databases.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A collection of all databases.</returns>
        Task<IReadOnlyCollection<Database>> ListDatabasesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets statistics for all databases.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A collection of database statistics.</returns>
        Task<IReadOnlyCollection<DatabaseStatistics>> GetDatabaseStatisticsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the storage path for a database.
        /// </summary>
        /// <param name="name">The database name.</param>
        /// <returns>The full path to the database storage directory.</returns>
        string GetDatabasePath(string name);

        /// <summary>
        /// Gets the default database name.
        /// </summary>
        string DefaultDatabaseName { get; }

        /// <summary>
        /// Gets the system database name.
        /// </summary>
        string SystemDatabaseName { get; }

        /// <summary>
        /// Event raised when a database is created.
        /// </summary>
        event EventHandler<DatabaseEventArgs>? DatabaseCreated;

        /// <summary>
        /// Event raised when a database is dropped.
        /// </summary>
        event EventHandler<DatabaseEventArgs>? DatabaseDropped;
    }

    /// <summary>
    /// Event arguments for database events.
    /// </summary>
    public class DatabaseEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the database name.
        /// </summary>
        public string DatabaseName { get; }

        /// <summary>
        /// Gets the timestamp of the event.
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DatabaseEventArgs"/> class.
        /// </summary>
        /// <param name="databaseName">The database name.</param>
        public DatabaseEventArgs(string databaseName)
        {
            DatabaseName = databaseName ?? throw new ArgumentNullException(nameof(databaseName));
            Timestamp = DateTime.UtcNow;
        }
    }
}
