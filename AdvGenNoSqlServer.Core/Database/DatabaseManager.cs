// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;

namespace AdvGenNoSqlServer.Core.Database
{
    /// <summary>
    /// Manages databases in the NoSQL server.
    /// </summary>
    public class DatabaseManager : IDatabaseManager, IDisposable
    {
        private readonly ConcurrentDictionary<string, Database> _databases;
        private readonly string _basePath;
        private readonly SemaphoreSlim _semaphore;
        private bool _disposed;

        /// <summary>
        /// Gets the default database name.
        /// </summary>
        public string DefaultDatabaseName => "default";

        /// <summary>
        /// Gets the system database name.
        /// </summary>
        public string SystemDatabaseName => "_system";

        /// <summary>
        /// Event raised when a database is created.
        /// </summary>
        public event EventHandler<DatabaseEventArgs>? DatabaseCreated;

        /// <summary>
        /// Event raised when a database is dropped.
        /// </summary>
        public event EventHandler<DatabaseEventArgs>? DatabaseDropped;

        /// <summary>
        /// Initializes a new instance of the <see cref="DatabaseManager"/> class.
        /// </summary>
        /// <param name="basePath">The base path for all database storage.</param>
        /// <exception cref="ArgumentException">Thrown when basePath is null or empty.</exception>
        /// <exception cref="DirectoryNotFoundException">Thrown when basePath doesn't exist and can't be created.</exception>
        public DatabaseManager(string basePath)
        {
            if (string.IsNullOrWhiteSpace(basePath))
                throw new ArgumentException("Base path cannot be null or empty", nameof(basePath));

            // Ensure the base directory exists
            if (!Directory.Exists(basePath))
            {
                try
                {
                    Directory.CreateDirectory(basePath);
                }
                catch (Exception ex)
                {
                    throw new DirectoryNotFoundException($"Could not create base directory: {basePath}", ex);
                }
            }

            _basePath = basePath;
            _databases = new ConcurrentDictionary<string, Database>(StringComparer.OrdinalIgnoreCase);
            _semaphore = new SemaphoreSlim(1, 1);

            // Initialize system databases
            InitializeSystemDatabasesAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Creates a new database.
        /// </summary>
        public async Task<Database> CreateDatabaseAsync(
            string name,
            DatabaseOptions? options = null,
            DatabaseSecurity? security = null,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (!Database.IsValidDatabaseName(name))
                throw new ArgumentException($"Invalid database name: '{name}'", nameof(name));

            // Validate options before acquiring lock
            options ??= new DatabaseOptions();
            var validationErrors = options.Validate();
            if (validationErrors.Count > 0)
                throw new ArgumentException($"Invalid database options: {string.Join(", ", validationErrors)}");

            // Create database path
            var dbPath = GetDatabasePath(name);

            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                // Check if database already exists
                if (_databases.ContainsKey(name))
                    throw new InvalidOperationException($"Database '{name}' already exists");

                // Create directory
                if (!Directory.Exists(dbPath))
                {
                    Directory.CreateDirectory(dbPath);
                }

                // Create database object
                var database = new Database(name, dbPath, options, security);

                // Store in collection
                if (!_databases.TryAdd(name, database))
                    throw new InvalidOperationException($"Failed to add database '{name}' to collection");

                // Persist database metadata
                await SaveDatabaseMetadataAsync(database, cancellationToken);

                // Raise event
                DatabaseCreated?.Invoke(this, new DatabaseEventArgs(name));

                return database;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Drops (deletes) a database.
        /// </summary>
        public async Task<bool> DropDatabaseAsync(string name, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(name))
                return false;

            // Cannot drop system databases
            if (name.Equals(SystemDatabaseName, StringComparison.OrdinalIgnoreCase) ||
                name.Equals(DefaultDatabaseName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Cannot drop system database '{name}'");
            }

            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                if (!_databases.TryRemove(name, out var database))
                    return false;

                // Delete the database directory
                try
                {
                    if (Directory.Exists(database.Path))
                    {
                        Directory.Delete(database.Path, recursive: true);
                    }
                }
                catch (Exception ex)
                {
                    // Log the error but don't fail the operation
                    // The database entry is already removed from memory
                    Console.WriteLine($"Warning: Could not delete database directory '{database.Path}': {ex.Message}");
                }

                // Delete metadata file if it exists
                var metadataPath = GetDatabaseMetadataPath(name);
                try
                {
                    if (File.Exists(metadataPath))
                    {
                        File.Delete(metadataPath);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Could not delete database metadata '{metadataPath}': {ex.Message}");
                }

                // Raise event
                DatabaseDropped?.Invoke(this, new DatabaseEventArgs(name));

                return true;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Gets a database by name.
        /// </summary>
        public async Task<Database?> GetDatabaseAsync(string name, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(name))
                return null;

            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                if (_databases.TryGetValue(name, out var database))
                {
                    database.Touch();
                    return database;
                }

                return null;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Checks if a database exists.
        /// </summary>
        public async Task<bool> DatabaseExistsAsync(string name, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(name))
                return false;

            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                return _databases.ContainsKey(name);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Lists all databases.
        /// </summary>
        public async Task<IReadOnlyCollection<Database>> ListDatabasesAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                return _databases.Values.ToList();
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Gets statistics for all databases.
        /// </summary>
        public async Task<IReadOnlyCollection<DatabaseStatistics>> GetDatabaseStatisticsAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            var databases = await ListDatabasesAsync(cancellationToken);
            return databases.Select(d => d.GetStatistics()).ToList();
        }

        /// <summary>
        /// Gets the storage path for a database.
        /// </summary>
        public string GetDatabasePath(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Database name cannot be null or empty", nameof(name));

            return System.IO.Path.Combine(_basePath, name);
        }

        /// <summary>
        /// Disposes the database manager.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _semaphore.Dispose();
            _disposed = true;
        }

        private async Task InitializeSystemDatabasesAsync()
        {
            // Create system database if it doesn't exist
            if (!_databases.ContainsKey(SystemDatabaseName))
            {
                var systemOptions = new DatabaseOptions
                {
                    MaxCollections = 100,
                    MaxSizeBytes = 1024 * 1024 * 1024, // 1GB for system DB
                    RequireAuthentication = true
                };

                var systemSecurity = new DatabaseSecurity();

                var systemPath = GetDatabasePath(SystemDatabaseName);
                if (!Directory.Exists(systemPath))
                {
                    Directory.CreateDirectory(systemPath);
                }

                var systemDb = new Database(SystemDatabaseName, systemPath, systemOptions, systemSecurity);
                _databases[SystemDatabaseName] = systemDb;
                await SaveDatabaseMetadataAsync(systemDb, CancellationToken.None);
            }

            // Create default database if it doesn't exist
            if (!_databases.ContainsKey(DefaultDatabaseName))
            {
                var defaultOptions = new DatabaseOptions
                {
                    RequireAuthentication = false // Default DB can be accessed without auth
                };
                var defaultSecurity = new DatabaseSecurity
                {
                    AllowAnonymousRead = true
                };

                var defaultPath = GetDatabasePath(DefaultDatabaseName);
                if (!Directory.Exists(defaultPath))
                {
                    Directory.CreateDirectory(defaultPath);
                }

                var defaultDb = new Database(DefaultDatabaseName, defaultPath, defaultOptions, defaultSecurity);
                _databases[DefaultDatabaseName] = defaultDb;
                await SaveDatabaseMetadataAsync(defaultDb, CancellationToken.None);
            }
        }

        private async Task SaveDatabaseMetadataAsync(Database database, CancellationToken cancellationToken)
        {
            var metadataPath = GetDatabaseMetadataPath(database.Name);
            var metadata = new DatabaseMetadata
            {
                Name = database.Name,
                CreatedAt = database.CreatedAt,
                Options = database.Options,
                Security = database.Security
            };

            var json = System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(metadataPath, json, cancellationToken);
        }

        private string GetDatabaseMetadataPath(string name)
        {
            return System.IO.Path.Combine(GetDatabasePath(name), "_database.json");
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(DatabaseManager));
        }

        /// <summary>
        /// Internal class for serializing database metadata.
        /// </summary>
        private class DatabaseMetadata
        {
            public string Name { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
            public DatabaseOptions Options { get; set; } = new();
            public DatabaseSecurity Security { get; set; } = new();
        }
    }
}
