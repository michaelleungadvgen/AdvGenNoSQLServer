// Copyright (c) 2026 [Your Organization]
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Storage;

/// <summary>
/// Interface for document-based storage operations
/// Provides CRUD operations for documents organized in collections
/// </summary>
public interface IDocumentStore
{
    /// <summary>
    /// Inserts a new document into the specified collection
    /// </summary>
    /// <param name="collectionName">The name of the collection</param>
    /// <param name="document">The document to insert</param>
    /// <returns>The inserted document with generated ID and timestamps</returns>
    /// <exception cref="ArgumentException">Thrown when document ID already exists</exception>
    Task<Document> InsertAsync(string collectionName, Document document);

    /// <summary>
    /// Retrieves a document by ID from the specified collection
    /// </summary>
    /// <param name="collectionName">The name of the collection</param>
    /// <param name="documentId">The ID of the document to retrieve</param>
    /// <returns>The document if found, null otherwise</returns>
    Task<Document?> GetAsync(string collectionName, string documentId);

    /// <summary>
    /// Retrieves all documents from the specified collection
    /// </summary>
    /// <param name="collectionName">The name of the collection</param>
    /// <returns>Enumerable of all documents in the collection</returns>
    Task<IEnumerable<Document>> GetAllAsync(string collectionName);

    /// <summary>
    /// Updates an existing document in the specified collection
    /// </summary>
    /// <param name="collectionName">The name of the collection</param>
    /// <param name="document">The document to update</param>
    /// <returns>The updated document with new version and timestamp</returns>
    /// <exception cref="ArgumentException">Thrown when document ID does not exist</exception>
    Task<Document> UpdateAsync(string collectionName, Document document);

    /// <summary>
    /// Deletes a document by ID from the specified collection
    /// </summary>
    /// <param name="collectionName">The name of the collection</param>
    /// <param name="documentId">The ID of the document to delete</param>
    /// <returns>True if document was deleted, false if not found</returns>
    Task<bool> DeleteAsync(string collectionName, string documentId);

    /// <summary>
    /// Checks if a document exists in the specified collection
    /// </summary>
    /// <param name="collectionName">The name of the collection</param>
    /// <param name="documentId">The ID of the document to check</param>
    /// <returns>True if document exists, false otherwise</returns>
    Task<bool> ExistsAsync(string collectionName, string documentId);

    /// <summary>
    /// Gets the count of documents in the specified collection
    /// </summary>
    /// <param name="collectionName">The name of the collection</param>
    /// <returns>Number of documents in the collection</returns>
    Task<long> CountAsync(string collectionName);

    /// <summary>
    /// Creates a new collection if it doesn't exist
    /// </summary>
    /// <param name="collectionName">The name of the collection to create</param>
    Task CreateCollectionAsync(string collectionName);

    /// <summary>
    /// Drops a collection and all its documents
    /// </summary>
    /// <param name="collectionName">The name of the collection to drop</param>
    /// <returns>True if collection was dropped, false if not found</returns>
    Task<bool> DropCollectionAsync(string collectionName);

    /// <summary>
    /// Gets a list of all collection names
    /// </summary>
    /// <returns>List of collection names</returns>
    Task<IEnumerable<string>> GetCollectionsAsync();

    /// <summary>
    /// Clears all documents from a collection without removing the collection
    /// </summary>
    /// <param name="collectionName">The name of the collection to clear</param>
    Task ClearCollectionAsync(string collectionName);
}

/// <summary>
/// Exception thrown when a document operation fails
/// </summary>
public class DocumentStoreException : Exception
{
    public DocumentStoreException(string message) : base(message) { }
    public DocumentStoreException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when a document is not found
/// </summary>
public class DocumentNotFoundException : DocumentStoreException
{
    public string CollectionName { get; }
    public string DocumentId { get; }

    public DocumentNotFoundException(string collectionName, string documentId)
        : base($"Document '{documentId}' not found in collection '{collectionName}'")
    {
        CollectionName = collectionName;
        DocumentId = documentId;
    }
}

/// <summary>
/// Exception thrown when a document already exists
/// </summary>
public class DocumentAlreadyExistsException : DocumentStoreException
{
    public string CollectionName { get; }
    public string DocumentId { get; }

    public DocumentAlreadyExistsException(string collectionName, string documentId)
        : base($"Document '{documentId}' already exists in collection '{collectionName}'")
    {
        CollectionName = collectionName;
        DocumentId = documentId;
    }
}

/// <summary>
/// Exception thrown when a collection is not found
/// </summary>
public class CollectionNotFoundException : DocumentStoreException
{
    public string CollectionName { get; }

    public CollectionNotFoundException(string collectionName)
        : base($"Collection '{collectionName}' not found")
    {
        CollectionName = collectionName;
    }
}
