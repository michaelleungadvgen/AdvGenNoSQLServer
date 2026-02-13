// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;

namespace AdvGenNoSqlServer.Storage;

/// <summary>
/// Hybrid document store that combines in-memory cache with disk persistence.
/// - Writes go to cache immediately, then async to disk via background thread
/// - Reads check cache first, then disk (read-through caching)
/// - Provides fast access with durability guarantees
/// </summary>
public class HybridDocumentStore : IDocumentStore, IAsyncDisposable
{
    private readonly string _basePath;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Document>> _cache;
    private readonly Channel<WriteOperation> _writeQueue;
    private readonly CancellationTokenSource _cts;
    private readonly Task _backgroundWriter;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;
    private int _pendingWrites;
    private bool _initialized;
    private bool _disposed;

    /// <summary>
    /// Gets the number of pending write operations
    /// </summary>
    public int PendingWrites => _pendingWrites;

    /// <summary>
    /// Gets whether the store has been initialized
    /// </summary>
    public bool IsInitialized => _initialized;

    /// <summary>
    /// Creates a new hybrid document store
    /// </summary>
    /// <param name="basePath">Base path for disk storage</param>
    public HybridDocumentStore(string basePath)
    {
        _basePath = basePath;
        _cache = new ConcurrentDictionary<string, ConcurrentDictionary<string, Document>>();
        _writeQueue = Channel.CreateUnbounded<WriteOperation>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _cts = new CancellationTokenSource();
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        // Start background writer
        _backgroundWriter = Task.Run(ProcessWriteQueueAsync);
    }

    /// <summary>
    /// Initializes the store by loading existing data from disk into cache
    /// </summary>
    public async Task InitializeAsync()
    {
        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;

            // Create base directory if it doesn't exist
            if (!Directory.Exists(_basePath))
            {
                Directory.CreateDirectory(_basePath);
            }

            // Load existing collections from disk into cache
            foreach (var collectionDir in Directory.GetDirectories(_basePath))
            {
                var collectionName = Path.GetFileName(collectionDir);
                var collection = _cache.GetOrAdd(collectionName, _ => new ConcurrentDictionary<string, Document>());

                foreach (var file in Directory.GetFiles(collectionDir, "*.json"))
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(file);
                        var document = JsonSerializer.Deserialize<Document>(json, _jsonOptions);
                        if (document != null)
                        {
                            collection[document.Id] = document;
                        }
                    }
                    catch (Exception)
                    {
                        // Skip corrupted files
                    }
                }
            }

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<Document> InsertAsync(string collectionName, Document document)
    {
        EnsureInitialized();
        ValidateCollectionName(collectionName);

        var collection = _cache.GetOrAdd(collectionName, _ => new ConcurrentDictionary<string, Document>());

        // Check if document already exists
        if (collection.ContainsKey(document.Id))
        {
            throw new DocumentAlreadyExistsException(collectionName, document.Id);
        }

        // Set timestamps
        document.CreatedAt = DateTime.UtcNow;
        document.UpdatedAt = document.CreatedAt;
        document.Version = 1;

        // Add to cache
        if (!collection.TryAdd(document.Id, document))
        {
            throw new DocumentAlreadyExistsException(collectionName, document.Id);
        }

        // Queue for disk write
        await QueueWriteAsync(WriteOperationType.Insert, collectionName, document);

        return document;
    }

    /// <inheritdoc />
    public Task<Document?> GetAsync(string collectionName, string documentId)
    {
        EnsureInitialized();
        ValidateCollectionName(collectionName);

        // Check cache first
        if (_cache.TryGetValue(collectionName, out var collection))
        {
            if (collection.TryGetValue(documentId, out var document))
            {
                return Task.FromResult<Document?>(document);
            }
        }

        // Cache miss - try to load from disk (read-through)
        return LoadFromDiskAsync(collectionName, documentId);
    }

    /// <inheritdoc />
    public Task<IEnumerable<Document>> GetAllAsync(string collectionName)
    {
        EnsureInitialized();
        ValidateCollectionName(collectionName);

        if (_cache.TryGetValue(collectionName, out var collection))
        {
            return Task.FromResult<IEnumerable<Document>>(collection.Values.ToList());
        }

        return Task.FromResult<IEnumerable<Document>>(Array.Empty<Document>());
    }

    /// <inheritdoc />
    public async Task<Document> UpdateAsync(string collectionName, Document document)
    {
        EnsureInitialized();
        ValidateCollectionName(collectionName);

        if (!_cache.TryGetValue(collectionName, out var collection))
        {
            throw new DocumentNotFoundException(collectionName, document.Id);
        }

        if (!collection.TryGetValue(document.Id, out var existing))
        {
            throw new DocumentNotFoundException(collectionName, document.Id);
        }

        // Update timestamps and version
        document.CreatedAt = existing.CreatedAt;
        document.UpdatedAt = DateTime.UtcNow;
        document.Version = existing.Version + 1;

        // Update cache
        collection[document.Id] = document;

        // Queue for disk write
        await QueueWriteAsync(WriteOperationType.Update, collectionName, document);

        return document;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string collectionName, string documentId)
    {
        EnsureInitialized();
        ValidateCollectionName(collectionName);

        if (!_cache.TryGetValue(collectionName, out var collection))
        {
            return false;
        }

        if (!collection.TryRemove(documentId, out var removed))
        {
            return false;
        }

        // Queue for disk delete
        await QueueWriteAsync(WriteOperationType.Delete, collectionName, removed);

        return true;
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string collectionName, string documentId)
    {
        EnsureInitialized();
        ValidateCollectionName(collectionName);

        if (_cache.TryGetValue(collectionName, out var collection))
        {
            return Task.FromResult(collection.ContainsKey(documentId));
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<long> CountAsync(string collectionName)
    {
        EnsureInitialized();
        ValidateCollectionName(collectionName);

        if (_cache.TryGetValue(collectionName, out var collection))
        {
            return Task.FromResult((long)collection.Count);
        }

        return Task.FromResult(0L);
    }

    /// <inheritdoc />
    public Task CreateCollectionAsync(string collectionName)
    {
        EnsureInitialized();
        ValidateCollectionName(collectionName);

        _cache.GetOrAdd(collectionName, _ => new ConcurrentDictionary<string, Document>());

        // Create directory on disk
        var collectionPath = Path.Combine(_basePath, collectionName);
        if (!Directory.Exists(collectionPath))
        {
            Directory.CreateDirectory(collectionPath);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> DropCollectionAsync(string collectionName)
    {
        EnsureInitialized();
        ValidateCollectionName(collectionName);

        var removed = _cache.TryRemove(collectionName, out _);

        // Remove directory on disk
        var collectionPath = Path.Combine(_basePath, collectionName);
        if (Directory.Exists(collectionPath))
        {
            Directory.Delete(collectionPath, true);
            return Task.FromResult(true);
        }

        return Task.FromResult(removed);
    }

    /// <inheritdoc />
    public Task<IEnumerable<string>> GetCollectionsAsync()
    {
        EnsureInitialized();
        return Task.FromResult<IEnumerable<string>>(_cache.Keys.ToList());
    }

    /// <inheritdoc />
    public Task ClearCollectionAsync(string collectionName)
    {
        EnsureInitialized();
        ValidateCollectionName(collectionName);

        if (_cache.TryGetValue(collectionName, out var collection))
        {
            collection.Clear();
        }

        // Clear files on disk
        var collectionPath = Path.Combine(_basePath, collectionName);
        if (Directory.Exists(collectionPath))
        {
            foreach (var file in Directory.GetFiles(collectionPath, "*.json"))
            {
                File.Delete(file);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Forces all pending writes to be flushed to disk
    /// </summary>
    public async Task FlushAsync()
    {
        // Wait until write queue is empty
        while (_pendingWrites > 0)
        {
            await Task.Delay(10);
        }
    }

    /// <summary>
    /// Saves all cached data to disk immediately (synchronous save)
    /// </summary>
    public async Task SaveAllAsync()
    {
        foreach (var (collectionName, collection) in _cache)
        {
            var collectionPath = Path.Combine(_basePath, collectionName);
            if (!Directory.Exists(collectionPath))
            {
                Directory.CreateDirectory(collectionPath);
            }

            foreach (var (_, document) in collection)
            {
                var filePath = Path.Combine(collectionPath, $"{document.Id}.json");
                var json = JsonSerializer.Serialize(document, _jsonOptions);
                await File.WriteAllTextAsync(filePath, json);
            }
        }
    }

    private async Task<Document?> LoadFromDiskAsync(string collectionName, string documentId)
    {
        var filePath = Path.Combine(_basePath, collectionName, $"{documentId}.json");

        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var document = JsonSerializer.Deserialize<Document>(json, _jsonOptions);

            if (document != null)
            {
                // Add to cache (read-through)
                var collection = _cache.GetOrAdd(collectionName, _ => new ConcurrentDictionary<string, Document>());
                collection[document.Id] = document;
            }

            return document;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task QueueWriteAsync(WriteOperationType type, string collectionName, Document document)
    {
        var operation = new WriteOperation
        {
            Type = type,
            CollectionName = collectionName,
            Document = document
        };

        Interlocked.Increment(ref _pendingWrites);
        await _writeQueue.Writer.WriteAsync(operation);
    }

    private async Task ProcessWriteQueueAsync()
    {
        try
        {
            await foreach (var operation in _writeQueue.Reader.ReadAllAsync(_cts.Token))
            {
                try
                {
                    await ProcessWriteOperationAsync(operation);
                }
                catch (Exception)
                {
                    // Log error but continue processing
                }
                finally
                {
                    Interlocked.Decrement(ref _pendingWrites);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    private async Task ProcessWriteOperationAsync(WriteOperation operation)
    {
        var collectionPath = Path.Combine(_basePath, operation.CollectionName);

        if (!Directory.Exists(collectionPath))
        {
            Directory.CreateDirectory(collectionPath);
        }

        var filePath = Path.Combine(collectionPath, $"{operation.Document.Id}.json");

        switch (operation.Type)
        {
            case WriteOperationType.Insert:
            case WriteOperationType.Update:
                var json = JsonSerializer.Serialize(operation.Document, _jsonOptions);
                await File.WriteAllTextAsync(filePath, json);
                break;

            case WriteOperationType.Delete:
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                break;
        }
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("HybridDocumentStore has not been initialized. Call InitializeAsync() first.");
        }
    }

    private static void ValidateCollectionName(string collectionName)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
        {
            throw new ArgumentException("Collection name cannot be null or empty", nameof(collectionName));
        }

        // Prevent path traversal attacks
        if (collectionName.Contains("..") || collectionName.Contains('/') || collectionName.Contains('\\'))
        {
            throw new ArgumentException("Collection name contains invalid characters", nameof(collectionName));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Stop accepting new writes
        _writeQueue.Writer.Complete();

        // Wait for pending writes to complete (with timeout)
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await _backgroundWriter.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Timeout - cancel remaining writes
            _cts.Cancel();
        }

        _cts.Dispose();
        _initLock.Dispose();
    }

    private enum WriteOperationType
    {
        Insert,
        Update,
        Delete
    }

    private class WriteOperation
    {
        public WriteOperationType Type { get; init; }
        public string CollectionName { get; init; } = string.Empty;
        public Document Document { get; init; } = null!;
    }
}
