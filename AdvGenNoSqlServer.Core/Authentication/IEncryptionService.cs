// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.Authentication;

/// <summary>
/// Interface for encryption and decryption operations
/// Provides field-level encryption for sensitive data at rest
/// </summary>
public interface IEncryptionService
{
    /// <summary>
    /// Encrypts plaintext data using AES-256-GCM
    /// </summary>
    /// <param name="plaintext">The data to encrypt</param>
    /// <returns>The encrypted data with nonce and tag, encoded as Base64</returns>
    string Encrypt(string plaintext);

    /// <summary>
    /// Encrypts plaintext data using AES-256-GCM with a specific key
    /// </summary>
    /// <param name="plaintext">The data to encrypt</param>
    /// <param name="key">The encryption key</param>
    /// <returns>The encrypted data with nonce and tag, encoded as Base64</returns>
    string Encrypt(string plaintext, byte[] key);

    /// <summary>
    /// Encrypts binary data using AES-256-GCM
    /// </summary>
    /// <param name="plaintext">The binary data to encrypt</param>
    /// <returns>The encrypted data with nonce and tag</returns>
    byte[] Encrypt(byte[] plaintext);

    /// <summary>
    /// Encrypts binary data using AES-256-GCM with a specific key
    /// </summary>
    /// <param name="plaintext">The binary data to encrypt</param>
    /// <param name="key">The encryption key</param>
    /// <returns>The encrypted data with nonce and tag</returns>
    byte[] Encrypt(byte[] plaintext, byte[] key);

    /// <summary>
    /// Decrypts encrypted data using AES-256-GCM
    /// </summary>
    /// <param name="ciphertext">The encrypted data (Base64 encoded with nonce and tag)</param>
    /// <returns>The decrypted plaintext</returns>
    /// <exception cref="CryptographicException">Thrown when decryption fails (wrong key, tampered data, etc.)</exception>
    string Decrypt(string ciphertext);

    /// <summary>
    /// Decrypts encrypted data using AES-256-GCM with a specific key
    /// </summary>
    /// <param name="ciphertext">The encrypted data (Base64 encoded with nonce and tag)</param>
    /// <param name="key">The decryption key</param>
    /// <returns>The decrypted plaintext</returns>
    /// <exception cref="CryptographicException">Thrown when decryption fails</exception>
    string Decrypt(string ciphertext, byte[] key);

    /// <summary>
    /// Decrypts binary encrypted data using AES-256-GCM
    /// </summary>
    /// <param name="ciphertext">The encrypted binary data (includes nonce and tag)</param>
    /// <returns>The decrypted binary data</returns>
    /// <exception cref="CryptographicException">Thrown when decryption fails</exception>
    byte[] Decrypt(byte[] ciphertext);

    /// <summary>
    /// Decrypts binary encrypted data using AES-256-GCM with a specific key
    /// </summary>
    /// <param name="ciphertext">The encrypted binary data (includes nonce and tag)</param>
    /// <param name="key">The decryption key</param>
    /// <returns>The decrypted binary data</returns>
    /// <exception cref="CryptographicException">Thrown when decryption fails</exception>
    byte[] Decrypt(byte[] ciphertext, byte[] key);

    /// <summary>
    /// Generates a new random encryption key
    /// </summary>
    /// <param name="keySize">The key size in bits (must be 128, 192, or 256)</param>
    /// <returns>A cryptographically secure random key</returns>
    byte[] GenerateKey(int keySize = 256);

    /// <summary>
    /// Derives an encryption key from a password using PBKDF2
    /// </summary>
    /// <param name="password">The password to derive from</param>
    /// <param name="salt">The salt (if null, a random salt will be generated)</param>
    /// <param name="iterations">Number of PBKDF2 iterations</param>
    /// <param name="keySize">The desired key size in bits</param>
    /// <returns>The derived key with salt prefix</returns>
    byte[] DeriveKeyFromPassword(string password, byte[]? salt = null, int iterations = 100000, int keySize = 256);

    /// <summary>
    /// Rotates the encryption key and re-encrypts data
    /// </summary>
    /// <param name="ciphertext">The encrypted data with the old key</param>
    /// <param name="oldKey">The old encryption key</param>
    /// <param name="newKey">The new encryption key</param>
    /// <returns>The re-encrypted data with the new key</returns>
    string RotateKey(string ciphertext, byte[] oldKey, byte[] newKey);

    /// <summary>
    /// Verifies if the ciphertext format is valid (without decrypting)
    /// </summary>
    /// <param name="ciphertext">The encrypted data to verify</param>
    /// <returns>True if the format appears valid; otherwise, false</returns>
    bool IsValidCiphertext(string ciphertext);

    /// <summary>
    /// Gets the current master key identifier
    /// </summary>
    string? CurrentKeyId { get; }
}

/// <summary>
/// Result of a key derivation operation
/// </summary>
public class KeyDerivationResult
{
    /// <summary>
    /// The derived key
    /// </summary>
    public byte[] Key { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// The salt used during derivation
    /// </summary>
    public byte[] Salt { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Number of iterations used
    /// </summary>
    public int Iterations { get; set; }
}

/// <summary>
/// Exception thrown when encryption or decryption operations fail
/// </summary>
public class EncryptionException : Exception
{
    /// <summary>
    /// Creates a new EncryptionException
    /// </summary>
    public EncryptionException(string message) : base(message) { }

    /// <summary>
    /// Creates a new EncryptionException with an inner exception
    /// </summary>
    public EncryptionException(string message, Exception innerException) : base(message, innerException) { }
}
