// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.FieldEncryption;

/// <summary>
/// Interface for key vault operations
/// Manages encryption keys for field-level encryption
/// </summary>
public interface IKeyVault
{
    /// <summary>
    /// Creates a new data encryption key
    /// </summary>
    /// <param name="keyAltName">Alternative name for the key</param>
    /// <param name="options">Key creation options</param>
    /// <returns>The created data key</returns>
    Task<DataKey> CreateKeyAsync(string keyAltName, KeyOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// Gets a key by its identifier
    /// </summary>
    /// <param name="keyId">The key identifier</param>
    /// <returns>The data key if found; otherwise, null</returns>
    Task<DataKey?> GetKeyAsync(string keyId, CancellationToken ct = default);

    /// <summary>
    /// Gets a key by its alternative name
    /// </summary>
    /// <param name="keyAltName">The alternative name</param>
    /// <returns>The data key if found; otherwise, null</returns>
    Task<DataKey?> GetKeyByAltNameAsync(string keyAltName, CancellationToken ct = default);

    /// <summary>
    /// Gets the raw key material for encryption/decryption
    /// </summary>
    /// <param name="keyId">The key identifier</param>
    /// <returns>The key bytes if found; otherwise, null</returns>
    Task<byte[]?> GetKeyMaterialAsync(string keyId, CancellationToken ct = default);

    /// <summary>
    /// Rotates a key (creates a new version)
    /// </summary>
    /// <param name="keyId">The key identifier to rotate</param>
    /// <returns>The new data key</returns>
    Task<DataKey> RotateKeyAsync(string keyId, CancellationToken ct = default);

    /// <summary>
    /// Deletes a key
    /// </summary>
    /// <param name="keyId">The key identifier to delete</param>
    /// <returns>True if deleted; otherwise, false</returns>
    Task<bool> DeleteKeyAsync(string keyId, CancellationToken ct = default);

    /// <summary>
    /// Lists all keys in the vault
    /// </summary>
    /// <returns>List of all data keys</returns>
    Task<IReadOnlyList<DataKey>> ListKeysAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the default key identifier
    /// </summary>
    string DefaultKeyId { get; }
}

/// <summary>
/// Data encryption key information
/// </summary>
public class DataKey
{
    /// <summary>
    /// Unique identifier for the key
    /// </summary>
    public required string KeyId { get; set; }

    /// <summary>
    /// Alternative name for the key (e.g., "default", "sensitive")
    /// </summary>
    public string? KeyAltName { get; set; }

    /// <summary>
    /// The encrypted key material (if stored encrypted)
    /// </summary>
    public byte[]? EncryptedKeyMaterial { get; set; }

    /// <summary>
    /// Key creation timestamp
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Key last rotation timestamp
    /// </summary>
    public DateTime? RotatedAt { get; set; }

    /// <summary>
    /// Key expiration timestamp
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Key status
    /// </summary>
    public KeyStatus Status { get; set; } = KeyStatus.Active;

    /// <summary>
    /// Key version for tracking rotations
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Algorithm this key is used for
    /// </summary>
    public EncryptionAlgorithm Algorithm { get; set; } = EncryptionAlgorithm.AES256GCM;

    /// <summary>
    /// Previous key ID if this is a rotated key
    /// </summary>
    public string? PreviousKeyId { get; set; }
}

/// <summary>
/// Key status
/// </summary>
public enum KeyStatus
{
    Active,
    Inactive,
    Compromised,
    Expired
}

/// <summary>
/// Options for creating a new key
/// </summary>
public class KeyOptions
{
    /// <summary>
    /// Key size in bits (128, 192, or 256)
    /// </summary>
    public int KeySize { get; set; } = 256;

    /// <summary>
    /// Key expiration time from now
    /// </summary>
    public TimeSpan? ExpiresIn { get; set; }

    /// <summary>
    /// Algorithm for the key
    /// </summary>
    public EncryptionAlgorithm Algorithm { get; set; } = EncryptionAlgorithm.AES256GCM;

    /// <summary>
    /// Whether to store the key material encrypted
    /// </summary>
    public bool EncryptKeyMaterial { get; set; } = true;
}
