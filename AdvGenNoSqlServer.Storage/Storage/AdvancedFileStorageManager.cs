using AdvGenNoSqlServer.Core.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AdvGenNoSqlServer.Storage.Storage;

public class AdvancedFileStorageManager : IStorageManager, IDisposable
{
    private readonly string _basePath;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _collectionSemaphores;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Document>> _documentCache;
    private readonly TimeSpan _cacheExpiration;
    private readonly Timer _cacheCleanupTimer;
    private bool _disposed = false;

    public AdvancedFileStorageManager(string basePath = "data", TimeSpan? cacheExpiration = null)
    {
        _basePath = basePath;
        _collectionSemaphores = new ConcurrentDictionary<string, SemaphoreSlim>();
        _documentCache = new ConcurrentDictionary<string, ConcurrentDictionary<string, Document>>();
        _cacheExpiration = cacheExpiration ?? TimeSpan.FromMinutes(5);

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // Ensure base directory exists
        if (!Directory.Exists(_basePath))
        {
            Directory.CreateDirectory(_basePath);
        }

        // Start cache cleanup timer
        _cacheCleanupTimer = new Timer(CleanupCache, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public async Task SaveDocumentAsync(string collectionName, Document document)
    {
        await EnsureCollectionAsync(collectionName);

        var semaphore = _collectionSemaphores.GetOrAdd(collectionName, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync();
        try
        {
            var collectionPath = Path.Combine(_basePath, collectionName);
            var documentPath = Path.Combine(collectionPath, $"{document.Id}.json");

            // Serialize the document to JSON
            var json = JsonSerializer.Serialize(document, _jsonOptions);

            // Write to file
            await File.WriteAllTextAsync(documentPath, json);

            // Update cache
            var collectionCache = _documentCache.GetOrAdd(collectionName, _ => new ConcurrentDictionary<string, Document>());
            collectionCache[document.Id] = new Document
            {
                Id = document.Id,
                Data = document.Data,
                CreatedAt = document.CreatedAt,
                UpdatedAt = DateTime.UtcNow,
                Version = document.Version
            };
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task<Document?> LoadDocumentAsync(string collectionName, string documentId)
    {
        await EnsureCollectionAsync(collectionName);

        // Check cache first
        if (_documentCache.TryGetValue(collectionName, out var collectionCache) &&
            collectionCache.TryGetValue(documentId, out var cachedDocument))
        {
            // Check if cache is still valid
            if (DateTime.UtcNow - cachedDocument.UpdatedAt < _cacheExpiration)
            {
                return cachedDocument;
            }
            else
            {
                // Remove expired cache entry
                collectionCache.TryRemove(documentId, out _);
            }
        }

        var semaphore = _collectionSemaphores.GetOrAdd(collectionName, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync();
        try
        {
            var collectionPath = Path.Combine(_basePath, collectionName);
            var documentPath = Path.Combine(collectionPath, $"{documentId}.json");

            if (!File.Exists(documentPath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(documentPath);
            var document = JsonSerializer.Deserialize<Document>(json, _jsonOptions);

            if (document != null)
            {
                // Update cache
                var cache = _documentCache.GetOrAdd(collectionName, _ => new ConcurrentDictionary<string, Document>());
                cache[documentId] = document;
            }

            return document;
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task DeleteDocumentAsync(string collectionName, string documentId)
    {
        await EnsureCollectionAsync(collectionName);

        var semaphore = _collectionSemaphores.GetOrAdd(collectionName, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync();
        try
        {
            var collectionPath = Path.Combine(_basePath, collectionName);
            var documentPath = Path.Combine(collectionPath, $"{documentId}.json");

            if (File.Exists(documentPath))
            {
                File.Delete(documentPath);
            }

            // Remove from cache
            if (_documentCache.TryGetValue(collectionName, out var collectionCache))
            {
                collectionCache.TryRemove(documentId, out _);
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task<bool> DocumentExistsAsync(string collectionName, string documentId)
    {
        await EnsureCollectionAsync(collectionName);

        // Check cache first
        if (_documentCache.TryGetValue(collectionName, out var collectionCache) &&
            collectionCache.ContainsKey(documentId))
        {
            return true;
        }

        var semaphore = _collectionSemaphores.GetOrAdd(collectionName, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync();
        try
        {
            var collectionPath = Path.Combine(_basePath, collectionName);
            var documentPath = Path.Combine(collectionPath, $"{documentId}.json");
            return File.Exists(documentPath);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task<IEnumerable<string>> ListDocumentsAsync(string collectionName)
    {
        await EnsureCollectionAsync(collectionName);

        var semaphore = _collectionSemaphores.GetOrAdd(collectionName, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync();
        try
        {
            var collectionPath = Path.Combine(_basePath, collectionName);
            var files = Directory.GetFiles(collectionPath, "*.json");
            var documentIds = new List<string>();

            foreach (var file in files)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                documentIds.Add(fileName);
            }

            return documentIds.AsEnumerable();
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task EnsureCollectionAsync(string collectionName)
    {
        var semaphore = _collectionSemaphores.GetOrAdd(collectionName, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync();
        try
        {
            var collectionPath = Path.Combine(_basePath, collectionName);
            if (!Directory.Exists(collectionPath))
            {
                Directory.CreateDirectory(collectionPath);
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    private void CleanupCache(object? state)
    {
        var now = DateTime.UtcNow;
        foreach (var collectionEntry in _documentCache)
        {
            var collectionName = collectionEntry.Key;
            var collectionCache = collectionEntry.Value;

            foreach (var documentEntry in collectionCache)
            {
                var documentId = documentEntry.Key;
                var document = documentEntry.Value;

                if (now - document.UpdatedAt > _cacheExpiration)
                {
                    collectionCache.TryRemove(documentId, out _);
                }
            }
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _cacheCleanupTimer?.Dispose();

                foreach (var semaphore in _collectionSemaphores.Values)
                {
                    semaphore.Dispose();
                }
            }

            _disposed = true;
        }
    }
}