using AdvGenNoSqlServer.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AdvGenNoSqlServer.Storage.Storage;

public interface IStorageManager
{
    /// <summary>
    /// Saves a document to storage
    /// </summary>
    /// <param name="collectionName">The name of the collection</param>
    /// <param name="document">The document to save</param>
    /// <returns>Task representing the asynchronous operation</returns>
    Task SaveDocumentAsync(string collectionName, Document document);

    /// <summary>
    /// Loads a document from storage
    /// </summary>
    /// <param name="collectionName">The name of the collection</param>
    /// <param name="documentId">The ID of the document to load</param>
    /// <returns>The loaded document or null if not found</returns>
    Task<Document?> LoadDocumentAsync(string collectionName, string documentId);

    /// <summary>
    /// Deletes a document from storage
    /// </summary>
    /// <param name="collectionName">The name of the collection</param>
    /// <param name="documentId">The ID of the document to delete</param>
    /// <returns>Task representing the asynchronous operation</returns>
    Task DeleteDocumentAsync(string collectionName, string documentId);

    /// <summary>
    /// Checks if a document exists in storage
    /// </summary>
    /// <param name="collectionName">The name of the collection</param>
    /// <param name="documentId">The ID of the document to check</param>
    /// <returns>True if the document exists, false otherwise</returns>
    Task<bool> DocumentExistsAsync(string collectionName, string documentId);

    /// <summary>
    /// Lists all document IDs in a collection
    /// </summary>
    /// <param name="collectionName">The name of the collection</param>
    /// <returns>List of document IDs</returns>
    Task<IEnumerable<string>> ListDocumentsAsync(string collectionName);

    /// <summary>
    /// Ensures the storage directory for a collection exists
    /// </summary>
    /// <param name="collectionName">The name of the collection</param>
    /// <returns>Task representing the asynchronous operation</returns>
    Task EnsureCollectionAsync(string collectionName);
}