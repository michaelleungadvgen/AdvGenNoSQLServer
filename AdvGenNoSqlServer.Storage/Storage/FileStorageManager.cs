// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.IO;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Core.Security;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace AdvGenNoSqlServer.Storage.Storage;

/// <summary>
/// File-based storage manager that persists each document as a JSON file per collection,
/// using per-collection locks, atomic writes, and path validation for safety.
/// </summary>
public class FileStorageManager : IStorageManager
{
    private readonly string _basePath;
    private readonly ConcurrentDictionary<string, object> _collectionLocks;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Creates a storage manager rooted at the specified directory, creating it if needed
    /// and cleaning up orphaned temp files from prior crashes.
    /// </summary>
    /// <param name="basePath">The root directory for collection data (defaults to "data").</param>
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

        // Sweep orphaned temp files left by a crash between write and rename.
        AtomicFileWriter.CleanupTempFiles(_basePath);
    }

    /// <summary>
    /// Serializes a document to JSON and writes it atomically under the collection lock.
    /// </summary>
    /// <param name="collectionName">The name of the collection.</param>
    /// <param name="document">The document to save.</param>
    /// <returns>Task representing the asynchronous operation.</returns>
    public async Task SaveDocumentAsync(string collectionName, Document document)
    {
        await EnsureCollectionAsync(collectionName);

        var collectionLock = _collectionLocks.GetOrAdd(collectionName, _ => new object());
        await Task.Run(() =>
        {
            lock (collectionLock)
            {
                var collectionPath = PathValidator.GetSafePath(_basePath, Path.Combine(_basePath, collectionName));
                var documentPath = PathValidator.GetSafePath(collectionPath, Path.Combine(collectionPath, $"{document.Id}.json"));

                // Serialize the document to JSON and write atomically
                var json = JsonSerializer.Serialize(document, _jsonOptions);
                AtomicFileWriter.WriteAllText(documentPath, json);
            }
        });
    }

    /// <summary>
    /// Loads and deserializes a document's JSON file; returns null when the file is missing or corrupt.
    /// </summary>
    /// <param name="collectionName">The name of the collection.</param>
    /// <param name="documentId">The ID of the document to load.</param>
    /// <returns>The loaded document, or null if not found or corrupt.</returns>
    public async Task<Document?> LoadDocumentAsync(string collectionName, string documentId)
    {
        await EnsureCollectionAsync(collectionName);

        var collectionLock = _collectionLocks.GetOrAdd(collectionName, _ => new object());
        return await Task.Run(() =>
        {
            lock (collectionLock)
            {
                var collectionPath = PathValidator.GetSafePath(_basePath, Path.Combine(_basePath, collectionName));
                var documentPath = PathValidator.GetSafePath(collectionPath, Path.Combine(collectionPath, $"{documentId}.json"));

                if (!File.Exists(documentPath))
                {
                    return null;
                }

                try
                {
                    var json = File.ReadAllText(documentPath);
                    return JsonSerializer.Deserialize<Document>(json, _jsonOptions);
                }
                catch (JsonException)
                {
                    // Corrupt document content; IO/permission errors must propagate.
                    return null;
                }
            }
        });
    }

    /// <summary>
    /// Deletes a document's JSON file if it exists, under the collection lock.
    /// </summary>
    /// <param name="collectionName">The name of the collection.</param>
    /// <param name="documentId">The ID of the document to delete.</param>
    /// <returns>Task representing the asynchronous operation.</returns>
    public async Task DeleteDocumentAsync(string collectionName, string documentId)
    {
        await EnsureCollectionAsync(collectionName);

        var collectionLock = _collectionLocks.GetOrAdd(collectionName, _ => new object());
        await Task.Run(() =>
        {
            lock (collectionLock)
            {
                var collectionPath = PathValidator.GetSafePath(_basePath, Path.Combine(_basePath, collectionName));
                var documentPath = PathValidator.GetSafePath(collectionPath, Path.Combine(collectionPath, $"{documentId}.json"));

                if (File.Exists(documentPath))
                {
                    File.Delete(documentPath);
                }
            }
        });
    }

    /// <summary>
    /// Checks whether a document's JSON file exists on disk.
    /// </summary>
    /// <param name="collectionName">The name of the collection.</param>
    /// <param name="documentId">The ID of the document to check.</param>
    /// <returns>True if the document file exists; otherwise false.</returns>
    public async Task<bool> DocumentExistsAsync(string collectionName, string documentId)
    {
        await EnsureCollectionAsync(collectionName);

        var collectionLock = _collectionLocks.GetOrAdd(collectionName, _ => new object());
        return await Task.Run(() =>
        {
            lock (collectionLock)
            {
                var collectionPath = PathValidator.GetSafePath(_basePath, Path.Combine(_basePath, collectionName));
                var documentPath = PathValidator.GetSafePath(collectionPath, Path.Combine(collectionPath, $"{documentId}.json"));
                return File.Exists(documentPath);
            }
        });
    }

    /// <summary>
    /// Lists the IDs of all documents in a collection by enumerating its JSON files.
    /// </summary>
    /// <param name="collectionName">The name of the collection.</param>
    /// <returns>The document IDs derived from file names.</returns>
    public async Task<IEnumerable<string>> ListDocumentsAsync(string collectionName)
    {
        await EnsureCollectionAsync(collectionName);

        var collectionLock = _collectionLocks.GetOrAdd(collectionName, _ => new object());
        return await Task.Run(() =>
        {
            lock (collectionLock)
            {
                var collectionPath = PathValidator.GetSafePath(_basePath, Path.Combine(_basePath, collectionName));
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

    /// <summary>
    /// Creates the collection's directory if it does not already exist.
    /// </summary>
    /// <param name="collectionName">The name of the collection.</param>
    /// <returns>Task representing the asynchronous operation.</returns>
    public async Task EnsureCollectionAsync(string collectionName)
    {
        var collectionLock = _collectionLocks.GetOrAdd(collectionName, _ => new object());
        await Task.Run(() =>
        {
            lock (collectionLock)
            {
                var collectionPath = PathValidator.GetSafePath(_basePath, Path.Combine(_basePath, collectionName));
                if (!Directory.Exists(collectionPath))
                {
                    Directory.CreateDirectory(collectionPath);
                }
            }
        });
    }
}