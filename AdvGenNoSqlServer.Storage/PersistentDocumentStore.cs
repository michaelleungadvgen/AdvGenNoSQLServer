// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;
using System.Text.Json;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Storage;

/// <summary>
/// File-based persistent document store implementation
/// Stores documents as JSON files on disk with in-memory caching
/// </summary>
public class PersistentDocumentStore : IPersistentDocumentStore
{
    private readonly ConcurrentDictionary<string, InMemoryDocumentCollection> _collections;
    private readonly string _dataPath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly SemaphoreSlim _saveLock;
    private bool _isInitialized;

    /// <summary>
    /// Gets the base path where collection data is stored
    /// </summary>
    public string DataPath => _dataPath;

    /// <summary>
    /// Creates a new PersistentDocumentStore instance
    /// </summary>
    /// <param name="dataPath">The base directory for storing collection files</param>
    public PersistentDocumentStore(string dataPath)
    {
        if (string.IsNullOrWhiteSpace(dataPath))
            throw new ArgumentException("Data path cannot be empty", nameof(dataPath));

        _dataPath = dataPath;
        _collections = new ConcurrentDictionary<string, InMemoryDocumentCollection>();
        _saveLock = new SemaphoreSlim(1, 1);
        _isInitialized = false;

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        if (_isInitialized)
            return;

        // Ensure data directory exists
        if (!Directory.Exists(_dataPath))
        {
            Directory.CreateDirectory(_dataPath);
        }

        // Load existing collections from disk
        var collectionDirectories = Directory.GetDirectories(_dataPath);
        foreach (var collectionDir in collectionDirectories)
        {
            var collectionName = Path.GetFileName(collectionDir);
            if (!string.IsNullOrWhiteSpace(collectionName))
            {
                await LoadCollectionAsync(collectionName);
            }
        }

        _isInitialized = true;
    }

    /// <inheritdoc />
    public async Task<Document> InsertAsync(string collectionName, Document document)
    {
        await EnsureInitializedAsync();

        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        if (document == null)
            throw new ArgumentNullException(nameof(document));

        // Ensure collection exists
        var collection = GetOrCreateCollection(collectionName);

        // Insert the document
        var result = collection.Insert(document);

        // Persist to disk
        await SaveDocumentToDiskAsync(collectionName, result);

        return result;
    }

    /// <inheritdoc />
    public Task<Document?> GetAsync(string collectionName, string documentId)
    {
        EnsureInitialized();

        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        if (string.IsNullOrWhiteSpace(documentId))
            return Task.FromResult<Document?>(null);

        if (!_collections.TryGetValue(collectionName, out var collection))
        {
            return Task.FromResult<Document?>(null);
        }

        var result = collection.Get(documentId);
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<IEnumerable<Document>> GetAllAsync(string collectionName)
    {
        EnsureInitialized();

        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        if (!_collections.TryGetValue(collectionName, out var collection))
        {
            return Task.FromResult<IEnumerable<Document>>(Array.Empty<Document>());
        }

        var result = collection.GetAll();
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public async Task<Document> UpdateAsync(string collectionName, Document document)
    {
        await EnsureInitializedAsync();

        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        if (document == null)
            throw new ArgumentNullException(nameof(document));

        if (!_collections.TryGetValue(collectionName, out var collection))
        {
            throw new CollectionNotFoundException(collectionName);
        }

        var result = collection.Update(document);

        // Persist to disk
        await SaveDocumentToDiskAsync(collectionName, result);

        return result;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string collectionName, string documentId)
    {
        await EnsureInitializedAsync();

        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        if (string.IsNullOrWhiteSpace(documentId))
            return false;

        if (!_collections.TryGetValue(collectionName, out var collection))
        {
            return false;
        }

        var result = collection.Delete(documentId);

        if (result)
        {
            // Remove from disk
            await DeleteDocumentFromDiskAsync(collectionName, documentId);
        }

        return result;
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string collectionName, string documentId)
    {
        EnsureInitialized();

        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        if (string.IsNullOrWhiteSpace(documentId))
            return Task.FromResult(false);

        if (!_collections.TryGetValue(collectionName, out var collection))
        {
            return Task.FromResult(false);
        }

        var result = collection.Exists(documentId);
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<long> CountAsync(string collectionName)
    {
        EnsureInitialized();

        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        if (!_collections.TryGetValue(collectionName, out var collection))
        {
            return Task.FromResult(0L);
        }

        return Task.FromResult(collection.Count);
    }

    /// <inheritdoc />
    public async Task CreateCollectionAsync(string collectionName)
    {
        await EnsureInitializedAsync();

        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        GetOrCreateCollection(collectionName);

        // Create directory on disk
        var collectionPath = GetCollectionPath(collectionName);
        if (!Directory.Exists(collectionPath))
        {
            Directory.CreateDirectory(collectionPath);
        }
    }

    /// <inheritdoc />
    public async Task<bool> DropCollectionAsync(string collectionName)
    {
        await EnsureInitializedAsync();

        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        var removed = _collections.TryRemove(collectionName, out _);

        if (removed)
        {
            // Remove from disk
            var collectionPath = GetCollectionPath(collectionName);
            if (Directory.Exists(collectionPath))
            {
                Directory.Delete(collectionPath, recursive: true);
            }
        }

        return removed;
    }

    /// <inheritdoc />
    public Task<IEnumerable<string>> GetCollectionsAsync()
    {
        EnsureInitialized();

        var collectionNames = _collections.Keys.ToList();
        return Task.FromResult<IEnumerable<string>>(collectionNames);
    }

    /// <inheritdoc />
    public async Task ClearCollectionAsync(string collectionName)
    {
        await EnsureInitializedAsync();

        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        if (_collections.TryGetValue(collectionName, out var collection))
        {
            collection.Clear();

            // Clear from disk
            var collectionPath = GetCollectionPath(collectionName);
            if (Directory.Exists(collectionPath))
            {
                var files = Directory.GetFiles(collectionPath, "*.json");
                foreach (var file in files)
                {
                    File.Delete(file);
                }
            }
        }
    }

    /// <inheritdoc />
    public async Task SaveChangesAsync()
    {
        await EnsureInitializedAsync();

        await _saveLock.WaitAsync();
        try
        {
            foreach (var collectionName in _collections.Keys)
            {
                await SaveCollectionAsync(collectionName);
            }
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveCollectionAsync(string collectionName)
    {
        await EnsureInitializedAsync();

        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        if (!_collections.TryGetValue(collectionName, out var collection))
        {
            throw new CollectionNotFoundException(collectionName);
        }

        var collectionPath = GetCollectionPath(collectionName);
        if (!Directory.Exists(collectionPath))
        {
            Directory.CreateDirectory(collectionPath);
        }

        // Save all documents in the collection
        var documents = collection.GetAll();
        foreach (var document in documents)
        {
            await SaveDocumentToDiskAsync(collectionName, document);
        }
    }

    /// <inheritdoc />
    public async Task LoadCollectionAsync(string collectionName)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        var collectionPath = GetCollectionPath(collectionName);
        if (!Directory.Exists(collectionPath))
        {
            return;
        }

        // Create in-memory collection
        var collection = new InMemoryDocumentCollection(collectionName);

        // Load all JSON files from the collection directory
        var files = Directory.GetFiles(collectionPath, "*.json");
        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var document = JsonSerializer.Deserialize<Document>(json, _jsonOptions);
                if (document != null && !string.IsNullOrWhiteSpace(document.Id))
                {
                    // Use reflection to bypass the normal Insert method since we're loading from disk
                    LoadDocumentIntoCollection(collection, document);
                }
            }
            catch (JsonException)
            {
                // Skip invalid files
                continue;
            }
        }

        // Add to collections dictionary
        _collections.TryAdd(collectionName, collection);
    }

    /// <inheritdoc />
    public Task<bool> CollectionExistsOnDiskAsync(string collectionName)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        var collectionPath = GetCollectionPath(collectionName);
        return Task.FromResult(Directory.Exists(collectionPath));
    }

    #region Private Methods

    private void EnsureInitialized()
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("PersistentDocumentStore has not been initialized. Call InitializeAsync() first.");
        }
    }

    private async Task EnsureInitializedAsync()
    {
        if (!_isInitialized)
        {
            await InitializeAsync();
        }
    }

    private InMemoryDocumentCollection GetOrCreateCollection(string collectionName)
    {
        // Try to get existing collection first
        if (_collections.TryGetValue(collectionName, out var existingCollection))
        {
            return existingCollection;
        }

        // Create directory on disk
        var collectionPath = GetCollectionPath(collectionName);
        if (!Directory.Exists(collectionPath))
        {
            Directory.CreateDirectory(collectionPath);
        }

        // Create new collection
        var newCollection = new InMemoryDocumentCollection(collectionName);

        // Try to add, or return existing if another thread created it
        return _collections.GetOrAdd(collectionName, newCollection);
    }

    private string GetCollectionPath(string collectionName)
    {
        return Path.Combine(_dataPath, SanitizeFileName(collectionName));
    }

    private string GetDocumentPath(string collectionName, string documentId)
    {
        var collectionPath = GetCollectionPath(collectionName);
        var sanitizedId = SanitizeFileName(documentId);
        return Path.Combine(collectionPath, $"{sanitizedId}.json");
    }

    private async Task SaveDocumentToDiskAsync(string collectionName, Document document)
    {
        var documentPath = GetDocumentPath(collectionName, document.Id);
        var json = JsonSerializer.Serialize(document, _jsonOptions);
        await File.WriteAllTextAsync(documentPath, json);
    }

    private async Task DeleteDocumentFromDiskAsync(string collectionName, string documentId)
    {
        var documentPath = GetDocumentPath(collectionName, documentId);
        if (File.Exists(documentPath))
        {
            await Task.Run(() => File.Delete(documentPath));
        }
    }

    private void LoadDocumentIntoCollection(InMemoryDocumentCollection collection, Document document)
    {
        // Use reflection to access the private dictionary directly
        // This is necessary because we want to load documents with their original timestamps and versions
        var collectionType = typeof(InMemoryDocumentCollection);
        var documentsField = collectionType.GetField("_documents", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        if (documentsField != null)
        {
            var documents = (ConcurrentDictionary<string, Document>)documentsField.GetValue(collection)!;
            documents.TryAdd(document.Id, document);

            // Update document count
            var countField = collectionType.GetField("_documentCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (countField != null)
            {
                var currentCount = (long)countField.GetValue(collection)!;
                Interlocked.Increment(ref currentCount);
                countField.SetValue(collection, currentCount);
            }
        }
    }

    private string SanitizeFileName(string name)
    {
        // Remove invalid characters from file name
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Where(c => !invalidChars.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "_" : sanitized;
    }

    #endregion
}
