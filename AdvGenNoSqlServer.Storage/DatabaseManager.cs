// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Storage;

using AdvGenNoSqlServer.Core.Abstractions;
using AdvGenNoSqlServer.Core.Security;
using System.Collections.Concurrent;

/// <summary>
/// Manages multiple database instances, each with its own storage directory
/// </summary>
public class DatabaseManager : IDatabaseManager
{
    private readonly string _baseStoragePath;
    private readonly ConcurrentDictionary<string, HybridDocumentStore> _databases;
    private readonly string _defaultDatabaseName;

    /// <summary>
    /// Creates a new database manager
    /// </summary>
    public DatabaseManager(string baseStoragePath, string defaultDatabaseName = "default")
    {
        _baseStoragePath = baseStoragePath;
        _defaultDatabaseName = defaultDatabaseName;
        _databases = new ConcurrentDictionary<string, HybridDocumentStore>();

        // Ensure base directory exists
        if (!Directory.Exists(_baseStoragePath))
        {
            Directory.CreateDirectory(_baseStoragePath);
        }

        // Load existing databases
        LoadExistingDatabases();
    }

    private void LoadExistingDatabases()
    {
        foreach (var dbDir in Directory.GetDirectories(_baseStoragePath))
        {
            var dbName = Path.GetFileName(dbDir);
            var store = new HybridDocumentStore(dbDir);
            store.InitializeAsync().GetAwaiter().GetResult();
            _databases[dbName] = store;
        }

        // Create default database if none exist
        if (!_databases.Any())
        {
            var defaultDbPath = Path.Combine(_baseStoragePath, _defaultDatabaseName);
            var defaultStore = new HybridDocumentStore(defaultDbPath);
            defaultStore.InitializeAsync().GetAwaiter().GetResult();
            _databases[_defaultDatabaseName] = defaultStore;
        }
    }

    /// <inheritdoc />
    public string DefaultDatabaseName => _defaultDatabaseName;

    /// <inheritdoc />
    public IEnumerable<string> GetDatabaseNames()
    {
        return _databases.Keys.OrderBy(n => n);
    }

    /// <inheritdoc />
    public IDocumentStore GetDatabase(string name)
    {
        if (_databases.TryGetValue(name, out var store))
        {
            return store;
        }
        throw new InvalidOperationException($"Database '{name}' not found");
    }

    /// <inheritdoc />
    public async Task<bool> CreateDatabaseAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('/') || name.Contains('\\'))
        {
            return false;
        }

        var dbPath = PathValidator.GetSafePath(_baseStoragePath, Path.Combine(_baseStoragePath, name));
        if (Directory.Exists(dbPath))
        {
            return false; // Already exists
        }

        try
        {
            Directory.CreateDirectory(dbPath);
            var store = new HybridDocumentStore(dbPath);
            await store.InitializeAsync();
            _databases[name] = store;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteDatabaseAsync(string name)
    {
        if (name == _defaultDatabaseName)
        {
            return false; // Can't delete default
        }

        if (_databases.TryRemove(name, out var store))
        {
            await store.DisposeAsync();

            var dbPath = PathValidator.GetSafePath(_baseStoragePath, Path.Combine(_baseStoragePath, name));
            try
            {
                Directory.Delete(dbPath, recursive: true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public bool DatabaseExists(string name)
    {
        return _databases.ContainsKey(name);
    }
}
