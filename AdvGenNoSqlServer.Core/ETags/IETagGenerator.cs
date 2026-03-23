// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Abstractions;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Core.ETags;

/// <summary>
/// Interface for generating and validating ETags for optimistic concurrency control
/// </summary>
public interface IETagGenerator
{
    /// <summary>
    /// Generates an ETag for a document based on its content and version
    /// </summary>
    /// <param name="document">The document to generate ETag for</param>
    /// <returns>The generated ETag string</returns>
    string GenerateETag(Document document);

    /// <summary>
    /// Generates a weak ETag for a document (semantic equivalence)
    /// </summary>
    /// <param name="document">The document to generate ETag for</param>
    /// <returns>The generated weak ETag string (prefixed with W/)</returns>
    string GenerateWeakETag(Document document);

    /// <summary>
    /// Validates if the provided ETag matches the document's current state
    /// </summary>
    /// <param name="document">The document to validate against</param>
    /// <param name="eTag">The ETag to validate</param>
    /// <returns>True if ETag matches, false otherwise</returns>
    bool ValidateETag(Document document, string? eTag);

    /// <summary>
    /// Validates if the provided ETag matches using weak comparison
    /// </summary>
    /// <param name="document">The document to validate against</param>
    /// <param name="eTag">The ETag to validate (may be weak or strong)</param>
    /// <returns>True if ETag matches semantically, false otherwise</returns>
    bool ValidateWeakETag(Document document, string? eTag);

    /// <summary>
    /// Checks if an ETag is a weak ETag (starts with W/)
    /// </summary>
    /// <param name="eTag">The ETag to check</param>
    /// <returns>True if weak ETag, false otherwise</returns>
    bool IsWeakETag(string? eTag);

    /// <summary>
    /// Normalizes an ETag by removing quotes and weak prefix if present
    /// </summary>
    /// <param name="eTag">The ETag to normalize</param>
    /// <returns>Normalized ETag</returns>
    string NormalizeETag(string? eTag);
}

/// <summary>
/// Configuration options for ETag generation
/// </summary>
public class ETagOptions
{
    /// <summary>
    /// Gets or sets the hash algorithm to use for ETag generation
    /// </summary>
    public ETagHashAlgorithm HashAlgorithm { get; set; } = ETagHashAlgorithm.SHA256;

    /// <summary>
    /// Gets or sets whether to generate weak ETags by default
    /// </summary>
    public bool UseWeakETagsByDefault { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to include the document's Version property in ETag calculation
    /// </summary>
    public bool IncludeVersion { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to include the document's UpdatedAt timestamp in ETag calculation
    /// </summary>
    public bool IncludeUpdatedAt { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to include document data content in ETag calculation
    /// </summary>
    public bool IncludeContent { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum length of ETag to generate (for truncation)
    /// </summary>
    public int MaxETagLength { get; set; } = 64;

    /// <summary>
    /// Creates default ETag options
    /// </summary>
    public static ETagOptions Default => new();

    /// <summary>
    /// Creates options for strong ETags (content-based)
    /// </summary>
    public static ETagOptions Strong => new() { UseWeakETagsByDefault = false };

    /// <summary>
    /// Creates options for weak ETags (version-based only)
    /// </summary>
    public static ETagOptions Weak => new()
    {
        UseWeakETagsByDefault = true,
        IncludeContent = false,
        IncludeVersion = true
    };
}

/// <summary>
/// Hash algorithms supported for ETag generation
/// </summary>
public enum ETagHashAlgorithm
{
    /// <summary>
    /// SHA-256 algorithm (default, 64 hex chars)
    /// </summary>
    SHA256,

    /// <summary>
    /// SHA-512 algorithm (128 hex chars)
    /// </summary>
    SHA512,

    /// <summary>
    /// MD5 algorithm (32 hex chars) - faster but less secure, suitable for ETags only
    /// </summary>
    MD5,

    /// <summary>
    /// CRC32 algorithm (8 hex chars) - fastest, suitable for simple change detection
    /// </summary>
    CRC32
}

/// <summary>
/// Result of ETag validation
/// </summary>
public enum ETagValidationResult
{
    /// <summary>
    /// ETag validation succeeded
    /// </summary>
    Success,

    /// <summary>
    /// Document not found
    /// </summary>
    DocumentNotFound,

    /// <summary>
    /// ETag does not match (concurrency conflict)
    /// </summary>
    ETagMismatch,

    /// <summary>
    /// ETag format is invalid
    /// </summary>
    InvalidETag,

    /// <summary>
    /// No ETag was provided for validation
    /// </summary>
    ETagNotProvided
}

/// <summary>
/// Represents an ETag validation response with details
/// </summary>
public class ETagValidationResponse
{
    /// <summary>
    /// Gets the validation result
    /// </summary>
    public ETagValidationResult Result { get; }

    /// <summary>
    /// Gets the current ETag of the document (for mismatch responses)
    /// </summary>
    public string? CurrentETag { get; }

    /// <summary>
    /// Gets the error message if validation failed
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Gets whether validation succeeded
    /// </summary>
    public bool IsSuccess => Result == ETagValidationResult.Success;

    private ETagValidationResponse(ETagValidationResult result, string? currentETag = null, string? errorMessage = null)
    {
        Result = result;
        CurrentETag = currentETag;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Creates a success response
    /// </summary>
    public static ETagValidationResponse Success() => new(ETagValidationResult.Success);

    /// <summary>
    /// Creates a document not found response
    /// </summary>
    public static ETagValidationResponse DocumentNotFound(string documentId) =>
        new(ETagValidationResult.DocumentNotFound, errorMessage: $"Document '{documentId}' not found");

    /// <summary>
    /// Creates an ETag mismatch response with current ETag
    /// </summary>
    public static ETagValidationResponse ETagMismatch(string currentETag) =>
        new(ETagValidationResult.ETagMismatch, currentETag, "ETag does not match - document has been modified");

    /// <summary>
    /// Creates an invalid ETag response
    /// </summary>
    public static ETagValidationResponse InvalidETag(string reason) =>
        new(ETagValidationResult.InvalidETag, errorMessage: $"Invalid ETag: {reason}");

    /// <summary>
    /// Creates an ETag not provided response
    /// </summary>
    public static ETagValidationResponse ETagNotProvided() =>
        new(ETagValidationResult.ETagNotProvided, errorMessage: "ETag not provided for conditional operation");
}

/// <summary>
/// Extension methods for ETag validation results
/// </summary>
public static class ETagValidationResultExtensions
{
    /// <summary>
    /// Throws appropriate exception if validation failed
    /// </summary>
    public static void ThrowIfFailed(this ETagValidationResponse response, string collectionName, string documentId)
    {
        if (response.IsSuccess) return;

        throw response.Result switch
        {
            ETagValidationResult.DocumentNotFound => new DocumentNotFoundException(collectionName, documentId),
            ETagValidationResult.ETagMismatch => new ConcurrencyException(collectionName, documentId, response.CurrentETag!),
            ETagValidationResult.InvalidETag => new ArgumentException(response.ErrorMessage, "eTag"),
            ETagValidationResult.ETagNotProvided => new ArgumentException(response.ErrorMessage, "eTag"),
            _ => new InvalidOperationException(response.ErrorMessage)
        };
    }
}
