// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.Attachments;

/// <summary>
/// Represents attachment metadata without binary data
/// </summary>
public class AttachmentInfo
{
    /// <summary>
    /// The attachment name (unique within document)
    /// </summary>
    public required string Name { get; set; }
    
    /// <summary>
    /// The content type (MIME type)
    /// </summary>
    public required string ContentType { get; set; }
    
    /// <summary>
    /// The size in bytes
    /// </summary>
    public long Size { get; set; }
    
    /// <summary>
    /// SHA-256 hash of the content for integrity verification
    /// </summary>
    public required string Hash { get; set; }
    
    /// <summary>
    /// When the attachment was created
    /// </summary>
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// When the attachment was last modified
    /// </summary>
    public DateTime UpdatedAt { get; set; }
    
    /// <summary>
    /// Optional metadata dictionary
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }
}

/// <summary>
/// Represents an attachment with its binary data
/// </summary>
public class Attachment : AttachmentInfo
{
    /// <summary>
    /// The binary content of the attachment
    /// </summary>
    public required byte[] Content { get; set; }
}

/// <summary>
/// Result of an attachment operation
/// </summary>
public class AttachmentResult
{
    /// <summary>
    /// Whether the operation was successful
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// The attachment info (if applicable)
    /// </summary>
    public AttachmentInfo? Info { get; set; }
    
    /// <summary>
    /// Error message if operation failed
    /// </summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// Creates a successful result
    /// </summary>
    public static AttachmentResult SuccessResult(AttachmentInfo info)
    {
        return new AttachmentResult { Success = true, Info = info };
    }
    
    /// <summary>
    /// Creates a failure result
    /// </summary>
    public static AttachmentResult FailureResult(string errorMessage)
    {
        return new AttachmentResult { Success = false, ErrorMessage = errorMessage };
    }
}

/// <summary>
/// Configuration options for attachment storage
/// </summary>
public class AttachmentStoreOptions
{
    /// <summary>
    /// Base directory for attachment storage
    /// </summary>
    public required string BasePath { get; set; }
    
    /// <summary>
    /// Maximum attachment size in bytes (default: 100MB)
    /// </summary>
    public long MaxAttachmentSize { get; set; } = 100 * 1024 * 1024;
    
    /// <summary>
    /// Maximum total storage size in bytes (default: 10GB, 0 = unlimited)
    /// </summary>
    public long MaxTotalStorage { get; set; } = 10L * 1024 * 1024 * 1024;
    
    /// <summary>
    /// Allowed content types (empty = allow all)
    /// </summary>
    public List<string>? AllowedContentTypes { get; set; }
    
    /// <summary>
    /// Blocked content types (e.g., dangerous file types)
    /// </summary>
    public List<string> BlockedContentTypes { get; set; } = new()
    {
        "application/x-msdownload",
        "application/x-executable",
        "application/x-dosexec"
    };
    
    /// <summary>
    /// Enable hash-based deduplication
    /// </summary>
    public bool EnableDeduplication { get; set; } = false;
    
    /// <summary>
    /// Validate the options
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(BasePath))
            throw new ArgumentException("BasePath cannot be empty", nameof(BasePath));
            
        if (MaxAttachmentSize <= 0)
            throw new ArgumentException("MaxAttachmentSize must be positive", nameof(MaxAttachmentSize));
            
        if (MaxTotalStorage < 0)
            throw new ArgumentException("MaxTotalStorage cannot be negative", nameof(MaxTotalStorage));
    }
}

/// <summary>
/// Interface for document attachment storage operations
/// Provides CRUD operations for binary attachments associated with documents
/// </summary>
public interface IAttachmentStore
{
    /// <summary>
    /// Stores an attachment for a document
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="documentId">The document ID</param>
    /// <param name="name">The attachment name</param>
    /// <param name="contentType">The content type (MIME type)</param>
    /// <param name="content">The binary content</param>
    /// <param name="metadata">Optional metadata</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result with attachment info</returns>
    Task<AttachmentResult> StoreAsync(
        string collectionName, 
        string documentId, 
        string name, 
        string contentType, 
        byte[] content,
        Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Retrieves an attachment
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="documentId">The document ID</param>
    /// <param name="name">The attachment name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The attachment with binary data, or null if not found</returns>
    Task<Attachment?> GetAsync(
        string collectionName, 
        string documentId, 
        string name,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Retrieves attachment metadata without loading binary data
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="documentId">The document ID</param>
    /// <param name="name">The attachment name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Attachment info, or null if not found</returns>
    Task<AttachmentInfo?> GetInfoAsync(
        string collectionName, 
        string documentId, 
        string name,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Lists all attachments for a document
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="documentId">The document ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of attachment info</returns>
    Task<IReadOnlyList<AttachmentInfo>> ListAsync(
        string collectionName, 
        string documentId,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Deletes an attachment
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="documentId">The document ID</param>
    /// <param name="name">The attachment name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if deleted, false if not found</returns>
    Task<bool> DeleteAsync(
        string collectionName, 
        string documentId, 
        string name,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Deletes all attachments for a document
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="documentId">The document ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of attachments deleted</returns>
    Task<int> DeleteAllAsync(
        string collectionName, 
        string documentId,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Checks if an attachment exists
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="documentId">The document ID</param>
    /// <param name="name">The attachment name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if exists</returns>
    Task<bool> ExistsAsync(
        string collectionName, 
        string documentId, 
        string name,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the total storage size used
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Size in bytes</returns>
    Task<long> GetTotalStorageSizeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Exception thrown when an attachment operation fails
/// </summary>
public class AttachmentStoreException : Exception
{
    public AttachmentStoreException(string message) : base(message) { }
    public AttachmentStoreException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when an attachment is not found
/// </summary>
public class AttachmentNotFoundException : AttachmentStoreException
{
    public string CollectionName { get; }
    public string DocumentId { get; }
    public string AttachmentName { get; }

    public AttachmentNotFoundException(string collectionName, string documentId, string attachmentName)
        : base($"Attachment '{attachmentName}' not found for document '{documentId}' in collection '{collectionName}'")
    {
        CollectionName = collectionName;
        DocumentId = documentId;
        AttachmentName = attachmentName;
    }
}

/// <summary>
/// Exception thrown when an attachment is too large
/// </summary>
public class AttachmentTooLargeException : AttachmentStoreException
{
    public long MaxSize { get; }
    public long ActualSize { get; }

    public AttachmentTooLargeException(long maxSize, long actualSize)
        : base($"Attachment size ({actualSize} bytes) exceeds maximum allowed ({maxSize} bytes)")
    {
        MaxSize = maxSize;
        ActualSize = actualSize;
    }
}

/// <summary>
/// Exception thrown when content type is not allowed
/// </summary>
public class AttachmentContentTypeException : AttachmentStoreException
{
    public string ContentType { get; }

    public AttachmentContentTypeException(string contentType)
        : base($"Content type '{contentType}' is not allowed")
    {
        ContentType = contentType;
    }
}
