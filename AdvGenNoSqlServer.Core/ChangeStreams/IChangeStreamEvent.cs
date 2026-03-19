// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Core.ChangeStreams;

/// <summary>
/// Represents the type of change operation
/// </summary>
public enum ChangeOperationType
{
    /// <summary>
    /// A new document was inserted
    /// </summary>
    Insert,

    /// <summary>
    /// An existing document was updated
    /// </summary>
    Update,

    /// <summary>
    /// A document was replaced entirely
    /// </summary>
    Replace,

    /// <summary>
    /// A document was deleted
    /// </summary>
    Delete,

    /// <summary>
    /// A collection was dropped
    /// </summary>
    Drop,

    /// <summary>
    /// A collection was renamed
    /// </summary>
    Rename,

    /// <summary>
    /// An index was created
    /// </summary>
    CreateIndex,

    /// <summary>
    /// An index was dropped
    /// </summary>
    DropIndex
}

/// <summary>
/// Represents a change stream event that captures data modifications
/// </summary>
public interface IChangeStreamEvent
{
    /// <summary>
    /// Gets the unique identifier for this change event
    /// </summary>
    string EventId { get; }

    /// <summary>
    /// Gets the type of operation that occurred
    /// </summary>
    ChangeOperationType OperationType { get; }

    /// <summary>
    /// Gets the name of the collection where the change occurred
    /// </summary>
    string CollectionName { get; }

    /// <summary>
    /// Gets the ID of the document that was changed
    /// </summary>
    string DocumentId { get; }

    /// <summary>
    /// Gets the full document after the change (for insert, update, replace)
    /// </summary>
    Document? FullDocument { get; }

    /// <summary>
    /// Gets the document before the change (if available)
    /// </summary>
    Document? DocumentBeforeChange { get; }

    /// <summary>
    /// Gets the timestamp when the change occurred
    /// </summary>
    DateTime Timestamp { get; }

    /// <summary>
    /// Gets the transaction ID if the change was part of a transaction
    /// </summary>
    string? TransactionId { get; }

    /// <summary>
    /// Gets the wall clock time as a high-resolution timestamp
    /// </summary>
    long ClusterTime { get; }
}

/// <summary>
/// Represents a change stream event with full type information
/// </summary>
public class ChangeStreamEvent : IChangeStreamEvent
{
    /// <inheritdoc />
    public string EventId { get; set; } = Guid.NewGuid().ToString("N");

    /// <inheritdoc />
    public ChangeOperationType OperationType { get; set; }

    /// <inheritdoc />
    public string CollectionName { get; set; } = string.Empty;

    /// <inheritdoc />
    public string DocumentId { get; set; } = string.Empty;

    /// <inheritdoc />
    public Document? FullDocument { get; set; }

    /// <inheritdoc />
    public Document? DocumentBeforeChange { get; set; }

    /// <inheritdoc />
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <inheritdoc />
    public string? TransactionId { get; set; }

    /// <inheritdoc />
    public long ClusterTime { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>
    /// Creates a new change stream event for an insert operation
    /// </summary>
    public static ChangeStreamEvent CreateInsert(string collectionName, Document document, string? transactionId = null)
    {
        return new ChangeStreamEvent
        {
            OperationType = ChangeOperationType.Insert,
            CollectionName = collectionName,
            DocumentId = document.Id,
            FullDocument = document,
            TransactionId = transactionId
        };
    }

    /// <summary>
    /// Creates a new change stream event for an update operation
    /// </summary>
    public static ChangeStreamEvent CreateUpdate(
        string collectionName, 
        string documentId, 
        Document updatedDocument, 
        Document? documentBeforeChange = null,
        string? transactionId = null)
    {
        return new ChangeStreamEvent
        {
            OperationType = ChangeOperationType.Update,
            CollectionName = collectionName,
            DocumentId = documentId,
            FullDocument = updatedDocument,
            DocumentBeforeChange = documentBeforeChange,
            TransactionId = transactionId
        };
    }

    /// <summary>
    /// Creates a new change stream event for a replace operation
    /// </summary>
    public static ChangeStreamEvent CreateReplace(
        string collectionName,
        string documentId,
        Document newDocument,
        Document? documentBeforeChange = null,
        string? transactionId = null)
    {
        return new ChangeStreamEvent
        {
            OperationType = ChangeOperationType.Replace,
            CollectionName = collectionName,
            DocumentId = documentId,
            FullDocument = newDocument,
            DocumentBeforeChange = documentBeforeChange,
            TransactionId = transactionId
        };
    }

    /// <summary>
    /// Creates a new change stream event for a delete operation
    /// </summary>
    public static ChangeStreamEvent CreateDelete(
        string collectionName, 
        string documentId, 
        Document? deletedDocument = null,
        string? transactionId = null)
    {
        return new ChangeStreamEvent
        {
            OperationType = ChangeOperationType.Delete,
            CollectionName = collectionName,
            DocumentId = documentId,
            FullDocument = null,
            DocumentBeforeChange = deletedDocument,
            TransactionId = transactionId
        };
    }

    /// <summary>
    /// Creates a new change stream event for a collection drop
    /// </summary>
    public static ChangeStreamEvent CreateDropCollection(string collectionName, string? transactionId = null)
    {
        return new ChangeStreamEvent
        {
            OperationType = ChangeOperationType.Drop,
            CollectionName = collectionName,
            DocumentId = string.Empty,
            TransactionId = transactionId
        };
    }
}

/// <summary>
/// Event arguments for change stream events
/// </summary>
public class ChangeStreamEventArgs : EventArgs
{
    /// <summary>
    /// Gets the change stream event
    /// </summary>
    public IChangeStreamEvent Event { get; }

    /// <summary>
    /// Creates a new change stream event arguments
    /// </summary>
    public ChangeStreamEventArgs(IChangeStreamEvent @event)
    {
        Event = @event ?? throw new ArgumentNullException(nameof(@event));
    }
}
