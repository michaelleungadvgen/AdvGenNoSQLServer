// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Storage;

/// <summary>
/// Thread-safe in-memory storage for documents within a collection
/// Manages document versioning and concurrent access
/// </summary>
internal class InMemoryDocumentCollection
{
    private readonly ConcurrentDictionary<string, Document> _documents;
    private long _documentCount;

    /// <summary>
    /// Gets the name of the collection
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the number of documents in the collection
    /// </summary>
    public long Count => Interlocked.Read(ref _documentCount);

    /// <summary>
    /// Gets the timestamp when the collection was created
    /// </summary>
    public DateTime CreatedAt { get; }

    /// <summary>
    /// Creates a new in-memory document collection
    /// </summary>
    /// <param name="name">The name of the collection</param>
    public InMemoryDocumentCollection(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Collection name cannot be empty", nameof(name));

        Name = name;
        _documents = new ConcurrentDictionary<string, Document>();
        _documentCount = 0;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Inserts a new document into the collection
    /// </summary>
    /// <param name="document">The document to insert</param>
    /// <returns>The inserted document</returns>
    /// <exception cref="DocumentAlreadyExistsException">Thrown when document ID already exists</exception>
    public Document Insert(Document document)
    {
        if (document == null)
            throw new ArgumentNullException(nameof(document));

        if (string.IsNullOrWhiteSpace(document.Id))
            throw new ArgumentException("Document ID cannot be empty", nameof(document));

        // Check if document already exists
        if (_documents.ContainsKey(document.Id))
        {
            throw new DocumentAlreadyExistsException(Name, document.Id);
        }

        // Set metadata
        var now = DateTime.UtcNow;
        var documentToStore = new Document
        {
            Id = document.Id,
            Data = document.Data ?? new Dictionary<string, object>(),
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1
        };

        // Try to add the document
        if (!_documents.TryAdd(document.Id, documentToStore))
        {
            throw new DocumentAlreadyExistsException(Name, document.Id);
        }

        Interlocked.Increment(ref _documentCount);
        return documentToStore;
    }

    /// <summary>
    /// Retrieves a document by ID
    /// </summary>
    /// <param name="documentId">The ID of the document to retrieve</param>
    /// <returns>The document if found, null otherwise</returns>
    public Document? Get(string documentId)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            return null;

        _documents.TryGetValue(documentId, out var document);
        return document;
    }

    /// <summary>
    /// Retrieves all documents in the collection
    /// </summary>
    /// <returns>Enumerable of all documents</returns>
    public IEnumerable<Document> GetAll()
    {
        return _documents.Values.ToList(); // Return a copy to avoid enumeration issues
    }

    /// <summary>
    /// Updates an existing document
    /// </summary>
    /// <param name="document">The document to update</param>
    /// <returns>The updated document</returns>
    /// <exception cref="DocumentNotFoundException">Thrown when document ID does not exist</exception>
    public Document Update(Document document)
    {
        if (document == null)
            throw new ArgumentNullException(nameof(document));

        if (string.IsNullOrWhiteSpace(document.Id))
            throw new ArgumentException("Document ID cannot be empty", nameof(document));

        // Check if document exists
        if (!_documents.TryGetValue(document.Id, out var existingDocument))
        {
            throw new DocumentNotFoundException(Name, document.Id);
        }

        // Create updated document with new version
        var updatedDocument = new Document
        {
            Id = document.Id,
            Data = document.Data ?? existingDocument.Data,
            CreatedAt = existingDocument.CreatedAt,
            UpdatedAt = DateTime.UtcNow,
            Version = existingDocument.Version + 1
        };

        // Update the document
        if (!_documents.TryUpdate(document.Id, updatedDocument, existingDocument))
        {
            // Document was modified by another thread, throw exception to indicate conflict
            throw new DocumentStoreException($"Document '{document.Id}' was modified by another operation");
        }

        return updatedDocument;
    }

    /// <summary>
    /// Deletes a document by ID
    /// </summary>
    /// <param name="documentId">The ID of the document to delete</param>
    /// <returns>True if document was deleted, false if not found</returns>
    public bool Delete(string documentId)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            return false;

        if (_documents.TryRemove(documentId, out _))
        {
            Interlocked.Decrement(ref _documentCount);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if a document exists
    /// </summary>
    /// <param name="documentId">The ID of the document to check</param>
    /// <returns>True if document exists, false otherwise</returns>
    public bool Exists(string documentId)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            return false;

        return _documents.ContainsKey(documentId);
    }

    /// <summary>
    /// Clears all documents from the collection
    /// </summary>
    public void Clear()
    {
        _documents.Clear();
        Interlocked.Exchange(ref _documentCount, 0);
    }
}
