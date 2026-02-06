using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Storage.Storage;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AdvGenNoSqlServer.Storage;

/// <summary>
/// Example service demonstrating the use of the file storage manager
/// </summary>
public class DocumentStorageService
{
    private readonly IStorageManager _storageManager;

    public DocumentStorageService(IStorageManager storageManager)
    {
        _storageManager = storageManager;
    }

    public async Task<Document> CreateDocumentAsync(string collectionName, string id, Dictionary<string, object> data)
    {
        var document = new Document
        {
            Id = id,
            Data = data,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };

        await _storageManager.SaveDocumentAsync(collectionName, document);
        return document;
    }

    public async Task<Document?> GetDocumentAsync(string collectionName, string id)
    {
        return await _storageManager.LoadDocumentAsync(collectionName, id);
    }

    public async Task UpdateDocumentAsync(string collectionName, Document document)
    {
        document.UpdatedAt = DateTime.UtcNow;
        document.Version++;
        await _storageManager.SaveDocumentAsync(collectionName, document);
    }

    public async Task DeleteDocumentAsync(string collectionName, string id)
    {
        await _storageManager.DeleteDocumentAsync(collectionName, id);
    }

    public async Task<bool> DocumentExistsAsync(string collectionName, string id)
    {
        return await _storageManager.DocumentExistsAsync(collectionName, id);
    }

    public async Task<IEnumerable<string>> ListDocumentsAsync(string collectionName)
    {
        return await _storageManager.ListDocumentsAsync(collectionName);
    }
}
