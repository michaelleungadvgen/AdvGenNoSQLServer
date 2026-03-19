// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Text.Json;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Core.FieldEncryption;

/// <summary>
/// Interface for field-level encryption operations
/// Provides transparent encryption/decryption of sensitive document fields
/// </summary>
public interface IFieldEncryptor
{
    /// <summary>
    /// Encrypts specified fields in a document
    /// </summary>
    /// <param name="document">The document containing fields to encrypt</param>
    /// <param name="fieldPaths">List of field paths to encrypt (supports dot notation for nested fields)</param>
    /// <param name="keyId">Optional key identifier to use for encryption</param>
    /// <returns>A new document with specified fields encrypted</returns>
    Task<Document> EncryptFieldsAsync(Document document, IEnumerable<string> fieldPaths, string? keyId = null, CancellationToken ct = default);

    /// <summary>
    /// Decrypts specified fields in a document
    /// </summary>
    /// <param name="document">The document containing encrypted fields</param>
    /// <param name="fieldPaths">List of field paths to decrypt (supports dot notation for nested fields)</param>
    /// <param name="keyId">Optional key identifier to use for decryption</param>
    /// <returns>A new document with specified fields decrypted</returns>
    Task<Document> DecryptFieldsAsync(Document document, IEnumerable<string> fieldPaths, string? keyId = null, CancellationToken ct = default);

    /// <summary>
    /// Encrypts a single field value
    /// </summary>
    /// <param name="fieldValue">The value to encrypt</param>
    /// <param name="keyId">Optional key identifier</param>
    /// <returns>The encrypted value as a base64 string</returns>
    Task<string> EncryptValueAsync(object? fieldValue, string? keyId = null, CancellationToken ct = default);

    /// <summary>
    /// Decrypts a single field value
    /// </summary>
    /// <param name="encryptedValue">The encrypted value (base64 string)</param>
    /// <param name="targetType">The expected type of the decrypted value</param>
    /// <param name="keyId">Optional key identifier</param>
    /// <returns>The decrypted value</returns>
    Task<object?> DecryptValueAsync(string encryptedValue, Type targetType, string? keyId = null, CancellationToken ct = default);

    /// <summary>
    /// Checks if a field value appears to be encrypted
    /// </summary>
    /// <param name="fieldValue">The value to check</param>
    /// <returns>True if the value appears to be encrypted; otherwise, false</returns>
    bool IsEncryptedValue(object? fieldValue);

    /// <summary>
    /// Gets the default key identifier used when none is specified
    /// </summary>
    string DefaultKeyId { get; }
}

/// <summary>
/// Configuration for field-level encryption on a collection
/// </summary>
public class FieldEncryptionConfig
{
    /// <summary>
    /// The collection name this configuration applies to
    /// </summary>
    public required string CollectionName { get; set; }

    /// <summary>
    /// List of field paths to encrypt (supports dot notation for nested fields)
    /// </summary>
    public List<string> EncryptedFields { get; set; } = new();

    /// <summary>
    /// The key identifier to use for encryption
    /// </summary>
    public string KeyId { get; set; } = "default";

    /// <summary>
    /// Encryption algorithm to use
    /// </summary>
    public EncryptionAlgorithm Algorithm { get; set; } = EncryptionAlgorithm.AES256GCM;

    /// <summary>
    /// Whether to encrypt null values
    /// </summary>
    public bool EncryptNullValues { get; set; } = false;

    /// <summary>
    /// Metadata prefix to identify encrypted fields
    /// </summary>
    public string EncryptionPrefix { get; set; } = "__enc__";
}

/// <summary>
/// Supported encryption algorithms
/// </summary>
public enum EncryptionAlgorithm
{
    AES256GCM,
    AES256CBC
}

/// <summary>
/// Attribute to mark properties for automatic field encryption
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public class EncryptedFieldAttribute : Attribute
{
    /// <summary>
    /// The key identifier to use for this field
    /// </summary>
    public string KeyId { get; set; } = "default";

    /// <summary>
    /// Creates a new EncryptedFieldAttribute
    /// </summary>
    public EncryptedFieldAttribute() { }

    /// <summary>
    /// Creates a new EncryptedFieldAttribute with a specific key ID
    /// </summary>
    /// <param name="keyId">The key identifier</param>
    public EncryptedFieldAttribute(string keyId)
    {
        KeyId = keyId;
    }
}

/// <summary>
/// Context for encryption operations
/// </summary>
public class EncryptionContext
{
    /// <summary>
    /// The collection being operated on
    /// </summary>
    public required string CollectionName { get; set; }

    /// <summary>
    /// The document ID being operated on
    /// </summary>
    public required string DocumentId { get; set; }

    /// <summary>
    /// The field path being encrypted/decrypted
    /// </summary>
    public required string FieldPath { get; set; }

    /// <summary>
    /// The key identifier being used
    /// </summary>
    public string KeyId { get; set; } = "default";

    /// <summary>
    /// The user performing the operation
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Timestamp of the operation
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
