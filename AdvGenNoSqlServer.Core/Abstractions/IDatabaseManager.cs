// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.Abstractions;

/// <summary>
/// Interface for managing multiple databases
/// </summary>
public interface IDatabaseManager
{
    /// <summary>
    /// Gets the names of all available databases
    /// </summary>
    IEnumerable<string> GetDatabaseNames();

    /// <summary>
    /// Gets a document store for the specified database
    /// </summary>
    IDocumentStore GetDatabase(string name);

    /// <summary>
    /// Creates a new database
    /// </summary>
    Task<bool> CreateDatabaseAsync(string name);

    /// <summary>
    /// Deletes a database and all its data
    /// </summary>
    Task<bool> DeleteDatabaseAsync(string name);

    /// <summary>
    /// Checks if a database exists
    /// </summary>
    bool DatabaseExists(string name);

    /// <summary>
    /// Flushes all pending write-behind operations in every database to disk.
    /// </summary>
    Task FlushAsync();

    /// <summary>
    /// Flushes and disposes all database stores. Must be called on server shutdown
    /// so queued writes are not lost.
    /// </summary>
    Task DisposeDatabasesAsync();

    /// <summary>
    /// Gets the default database name
    /// </summary>
    string DefaultDatabaseName { get; }
}
