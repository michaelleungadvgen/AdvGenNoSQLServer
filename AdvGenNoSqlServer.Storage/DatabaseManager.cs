// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Storage;

using AdvGenNoSqlServer.Core.Abstractions;
using System.Collections.Concurrent;

/// <summary>
/// Manages multiple database instances, each with its own storage directory
/// </summary>
public class DatabaseManager : IDatabaseManager
{
    private readonly string _baseStoragePath;
    private readonly bool _cacheMode;
    private readonly ConcurrentDictionary<string, IDocumentStore> _databases;
    private readonly string _defaultDatabaseName;

    /// <summary>
    /// Creates a new database manager. When <paramref name="cacheMode"/> is true, databases
    /// are memory-only and no storage directories are created or read.
    /// </summary>
    public DatabaseManager(string baseStoragePath, string defaultDatabaseName = "default", bool cacheMode = false)
    {
        _baseStoragePath = baseStoragePath;
        _defaultDatabaseName = defaultDatabaseName;
        _cacheMode = cacheMode;
        _databases = new ConcurrentDictionary<string, IDocumentStore>();

        if (_cacheMode)
        {
            _databases[_defaultDatabaseName] = new DocumentStore();
            return;
        }

        // Ensure base directory exists
        if (!Directory.Exists(_baseStoragePath))
        {
            Directory.CreateDirectory(_baseStoragePath);
        }

        // Load existing databases
        LoadExistingDatabases();
    }

    // Directories that live next to the per-database folders but are never databases:
    // ext4 volume noise plus the server's own auxiliary storage (audit logs, WAL, attachments).
    private static readonly HashSet<string> ReservedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "lost+found", "logs", "wal", "attachments"
    };

    private void LoadExistingDatabases()
    {
        foreach (var dbDir in Directory.GetDirectories(_baseStoragePath))
        {
            var dbName = Path.GetFileName(dbDir);

            if (ReservedDirectoryNames.Contains(dbName))
                continue;

            var store = new HybridDocumentStore(dbDir);
            store.InitializeAsync().GetAwaiter().GetResult();
            _databases[dbName] = store;
        }

        // Create default database if it doesn't exist yet (regardless of what other
        // directories the volume contains)
        if (!_databases.ContainsKey(_defaultDatabaseName))
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

        if (_databases.ContainsKey(name))
        {
            return false; // Already exists
        }

        if (_cacheMode)
        {
            _databases[name] = new DocumentStore();
            return true;
        }

        var dbPath = Path.Combine(_baseStoragePath, name);
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
            if (store is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync();
            }

            if (_cacheMode)
            {
                return true;
            }

            var dbPath = Path.Combine(_baseStoragePath, name);
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

    /// <inheritdoc />
    public async Task FlushAsync()
    {
        foreach (var store in _databases.Values)
        {
            if (store is HybridDocumentStore hybrid)
            {
                await hybrid.FlushAsync();
            }
        }
    }

    /// <inheritdoc />
    public async Task DisposeDatabasesAsync()
    {
        foreach (var (name, store) in _databases.ToArray())
        {
            if (_databases.TryRemove(name, out var removed) && removed is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync();
            }
        }
    }
}
