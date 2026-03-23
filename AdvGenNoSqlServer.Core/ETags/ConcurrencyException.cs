// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.ETags;

/// <summary>
/// Exception thrown when an optimistic concurrency conflict is detected
/// This occurs when attempting to update a document using a stale ETag
/// </summary>
public class ConcurrencyException : Exception
{
    /// <summary>
    /// Gets the collection name where the conflict occurred
    /// </summary>
    public string CollectionName { get; }

    /// <summary>
    /// Gets the document ID where the conflict occurred
    /// </summary>
    public string DocumentId { get; }

    /// <summary>
    /// Gets the current ETag of the document at the time of conflict
    /// </summary>
    public string CurrentETag { get; }

    /// <summary>
    /// Gets the ETag that was provided for the operation (may be null)
    /// </summary>
    public string? ProvidedETag { get; }

    /// <summary>
    /// Creates a new ConcurrencyException
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="documentId">The document ID</param>
    /// <param name="currentETag">The current ETag of the document</param>
    /// <param name="providedETag">The ETag that was provided for the operation (optional)</param>
    public ConcurrencyException(string collectionName, string documentId, string currentETag, string? providedETag = null)
        : base(BuildMessage(collectionName, documentId, currentETag, providedETag))
    {
        CollectionName = collectionName;
        DocumentId = documentId;
        CurrentETag = currentETag;
        ProvidedETag = providedETag;
    }

    /// <summary>
    /// Creates a new ConcurrencyException with an inner exception
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="documentId">The document ID</param>
    /// <param name="currentETag">The current ETag of the document</param>
    /// <param name="providedETag">The ETag that was provided for the operation (optional)</param>
    /// <param name="innerException">The inner exception</param>
    public ConcurrencyException(string collectionName, string documentId, string currentETag, string? providedETag, Exception innerException)
        : base(BuildMessage(collectionName, documentId, currentETag, providedETag), innerException)
    {
        CollectionName = collectionName;
        DocumentId = documentId;
        CurrentETag = currentETag;
        ProvidedETag = providedETag;
    }

    private static string BuildMessage(string collectionName, string documentId, string currentETag, string? providedETag)
    {
        var message = $"Concurrency conflict detected in collection '{collectionName}' for document '{documentId}'. " +
                      $"The document has been modified by another operation. ";

        if (!string.IsNullOrEmpty(providedETag))
        {
            message += $"Provided ETag: '{providedETag}', Current ETag: '{currentETag}'. ";
        }
        else
        {
            message += $"Current ETag: '{currentETag}'. ";
        }

        message += "Please fetch the latest version of the document and retry the operation.";

        return message;
    }
}
