using AdvGenNoSqlServer.Core.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace AdvGenNoSqlServer.Storage.Storage;

public class FileStorageManager : IStorageManager
{
    private readonly string _basePath;
    private readonly ConcurrentDictionary<string, object> _collectionLocks;
    private readonly JsonSerializerOptions _jsonOptions;

    public FileStorageManager(string basePath = "data")
    {
        _basePath = basePath;
        _collectionLocks = new ConcurrentDictionary<string, object>();
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
    }

    public async Task SaveDocumentAsync(string collectionName, Document document)
    {
        await EnsureCollectionAsync(collectionName);
        
        var collectionLock = _collectionLocks.GetOrAdd(collectionName, _ => new object());
        await Task.Run(() =>
        {
            lock (collectionLock)
            {
                var collectionPath = Path.Combine(_basePath, collectionName);
                var documentPath = Path.Combine(collectionPath, $"{document.Id}.json");
                
                // Serialize the document to JSON
                var json = JsonSerializer.Serialize(document, _jsonOptions);
                
                // Write to file
                File.WriteAllText(documentPath, json);
            }
        });
    }

    public async Task<Document?> LoadDocumentAsync(string collectionName, string documentId)
    {
        await EnsureCollectionAsync(collectionName);
        
        var collectionLock = _collectionLocks.GetOrAdd(collectionName, _ => new object());
        return await Task.Run(() =>
        {
            lock (collectionLock)
            {
                var collectionPath = Path.Combine(_basePath, collectionName);
                var documentPath = Path.Combine(collectionPath, $"{documentId}.json");

                if (!File.Exists(documentPath))
                {
                    return null;
                }

                try
                {
                    var json = File.ReadAllText(documentPath);
                    return JsonSerializer.Deserialize<Document>(json, _jsonOptions);
                }
                catch (Exception)
                {
                    // Log the exception in a real implementation
                    return null;
                }
            }
        });
    }

    public async Task DeleteDocumentAsync(string collectionName, string documentId)
    {
        await EnsureCollectionAsync(collectionName);
        
        var collectionLock = _collectionLocks.GetOrAdd(collectionName, _ => new object());
        await Task.Run(() =>
        {
            lock (collectionLock)
            {
                var collectionPath = Path.Combine(_basePath, collectionName);
                var documentPath = Path.Combine(collectionPath, $"{documentId}.json");

                if (File.Exists(documentPath))
                {
                    File.Delete(documentPath);
                }
            }
        });
    }

    public async Task<bool> DocumentExistsAsync(string collectionName, string documentId)
    {
        await EnsureCollectionAsync(collectionName);
        
        var collectionLock = _collectionLocks.GetOrAdd(collectionName, _ => new object());
        return await Task.Run(() =>
        {
            lock (collectionLock)
            {
                var collectionPath = Path.Combine(_basePath, collectionName);
                var documentPath = Path.Combine(collectionPath, $"{documentId}.json");
                return File.Exists(documentPath);
            }
        });
    }

    public async Task<IEnumerable<string>> ListDocumentsAsync(string collectionName)
    {
        await EnsureCollectionAsync(collectionName);
        
        var collectionLock = _collectionLocks.GetOrAdd(collectionName, _ => new object());
        return await Task.Run(() =>
        {
            lock (collectionLock)
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
        });
    }

    public async Task EnsureCollectionAsync(string collectionName)
    {
        var collectionLock = _collectionLocks.GetOrAdd(collectionName, _ => new object());
        await Task.Run(() =>
        {
            lock (collectionLock)
            {
                var collectionPath = Path.Combine(_basePath, collectionName);
                if (!Directory.Exists(collectionPath))
                {
                    Directory.CreateDirectory(collectionPath);
                }
            }
        });
    }
}