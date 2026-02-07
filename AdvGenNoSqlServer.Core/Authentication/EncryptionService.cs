// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Security.Cryptography;
using System.Text;
using AdvGenNoSqlServer.Core.Configuration;

namespace AdvGenNoSqlServer.Core.Authentication;

/// <summary>
/// Encryption service implementation using AES-256-GCM
/// Provides authenticated encryption for sensitive data at rest
/// </summary>
public class EncryptionService : IEncryptionService
{
    private const int AesGcmNonceSize = 12; // 96 bits as per NIST recommendation
    private const int AesGcmTagSize = 16;   // 128 bits authentication tag
    private const int DefaultKeySize = 32;  // 256 bits
    private const int DefaultSaltSize = 32; // 256 bits
    private const int DefaultPbkdf2Iterations = 100000;

    private readonly byte[] _masterKey;
    private readonly IKeyStore? _keyStore;
    private readonly string _keyId;

    /// <inheritdoc />
    public string? CurrentKeyId => _keyId;

    /// <summary>
    /// Creates a new EncryptionService with the specified configuration
    /// </summary>
    /// <param name="configuration">Server configuration containing encryption settings</param>
    /// <param name="keyStore">Optional key store for key management</param>
    public EncryptionService(ServerConfiguration configuration, IKeyStore? keyStore = null)
    {
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        _keyStore = keyStore;

        // Use configured encryption key or generate a new one
        if (!string.IsNullOrEmpty(configuration.EncryptionKey))
        {
            _masterKey = Convert.FromBase64String(configuration.EncryptionKey);
            if (_masterKey.Length != DefaultKeySize)
                throw new ArgumentException($"Encryption key must be {DefaultKeySize} bytes (256 bits)", nameof(configuration));
        }
        else
        {
            // Generate a secure random key
            _masterKey = GenerateKey();
            // TODO: Log warning that a new key was generated
        }

        _keyId = configuration.EncryptionKeyId ?? GenerateKeyId();
    }

    /// <summary>
    /// Creates a new EncryptionService with an explicit key
    /// </summary>
    /// <param name="key">The master encryption key (must be 32 bytes for AES-256)</param>
    /// <param name="keyStore">Optional key store for key management</param>
    /// <param name="keyId">Optional key identifier</param>
    public EncryptionService(byte[] key, IKeyStore? keyStore = null, string? keyId = null)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));
        if (key.Length != DefaultKeySize)
            throw new ArgumentException($"Key must be {DefaultKeySize} bytes (256 bits)", nameof(key));

        _masterKey = new byte[key.Length];
        Buffer.BlockCopy(key, 0, _masterKey, 0, key.Length);
        _keyStore = keyStore;
        _keyId = keyId ?? GenerateKeyId();
    }

    #region Encryption Methods

    /// <inheritdoc />
    public string Encrypt(string plaintext)
    {
        return Encrypt(plaintext, _masterKey);
    }

    /// <inheritdoc />
    public string Encrypt(string plaintext, byte[] key)
    {
        if (string.IsNullOrEmpty(plaintext))
            return string.Empty;

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = Encrypt(plaintextBytes, key);
        return Convert.ToBase64String(ciphertext);
    }

    /// <inheritdoc />
    public byte[] Encrypt(byte[] plaintext)
    {
        return Encrypt(plaintext, _masterKey);
    }

    /// <inheritdoc />
    public byte[] Encrypt(byte[] plaintext, byte[] key)
    {
        if (plaintext == null)
            throw new ArgumentNullException(nameof(plaintext));
        if (key == null)
            throw new ArgumentNullException(nameof(key));
        if (key.Length != DefaultKeySize)
            throw new ArgumentException($"Key must be {DefaultKeySize} bytes (256 bits)", nameof(key));

        if (plaintext.Length == 0)
            return Array.Empty<byte>();

        // Generate random nonce
        var nonce = new byte[AesGcmNonceSize];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(nonce);
        }

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[AesGcmTagSize];

        // Perform encryption
        using (var aesGcm = new AesGcm(key, AesGcmTagSize))
        {
            aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        // Combine: nonce (12 bytes) + tag (16 bytes) + ciphertext
        var result = new byte[AesGcmNonceSize + AesGcmTagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, AesGcmNonceSize);
        Buffer.BlockCopy(tag, 0, result, AesGcmNonceSize, AesGcmTagSize);
        Buffer.BlockCopy(ciphertext, 0, result, AesGcmNonceSize + AesGcmTagSize, ciphertext.Length);

        return result;
    }

    #endregion

    #region Decryption Methods

    /// <inheritdoc />
    public string Decrypt(string ciphertext)
    {
        return Decrypt(ciphertext, _masterKey);
    }

    /// <inheritdoc />
    public string Decrypt(string ciphertext, byte[] key)
    {
        if (string.IsNullOrEmpty(ciphertext))
            return string.Empty;

        try
        {
            var ciphertextBytes = Convert.FromBase64String(ciphertext);
            var plaintext = Decrypt(ciphertextBytes, key);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (FormatException ex)
        {
            throw new EncryptionException("Invalid ciphertext format: not valid Base64", ex);
        }
        catch (CryptographicException)
        {
            throw; // Re-throw cryptographic exceptions as-is
        }
    }

    /// <inheritdoc />
    public byte[] Decrypt(byte[] ciphertext)
    {
        return Decrypt(ciphertext, _masterKey);
    }

    /// <inheritdoc />
    public byte[] Decrypt(byte[] ciphertext, byte[] key)
    {
        if (ciphertext == null)
            throw new ArgumentNullException(nameof(ciphertext));
        if (key == null)
            throw new ArgumentNullException(nameof(key));
        if (key.Length != DefaultKeySize)
            throw new ArgumentException($"Key must be {DefaultKeySize} bytes (256 bits)", nameof(key));

        if (ciphertext.Length == 0)
            return Array.Empty<byte>();

        // Minimum size: nonce (12) + tag (16) = 28 bytes
        if (ciphertext.Length < AesGcmNonceSize + AesGcmTagSize)
            throw new EncryptionException("Ciphertext is too short to contain valid encrypted data");

        // Extract nonce, tag, and encrypted data
        var nonce = new byte[AesGcmNonceSize];
        var tag = new byte[AesGcmTagSize];
        var encryptedData = new byte[ciphertext.Length - AesGcmNonceSize - AesGcmTagSize];

        Buffer.BlockCopy(ciphertext, 0, nonce, 0, AesGcmNonceSize);
        Buffer.BlockCopy(ciphertext, AesGcmNonceSize, tag, 0, AesGcmTagSize);
        Buffer.BlockCopy(ciphertext, AesGcmNonceSize + AesGcmTagSize, encryptedData, 0, encryptedData.Length);

        var plaintext = new byte[encryptedData.Length];

        // Perform decryption
        try
        {
            using (var aesGcm = new AesGcm(key, AesGcmTagSize))
            {
                aesGcm.Decrypt(nonce, encryptedData, tag, plaintext);
            }
        }
        catch (CryptographicException ex)
        {
            throw new EncryptionException("Decryption failed: the ciphertext may have been tampered with or the key is incorrect", ex);
        }

        return plaintext;
    }

    #endregion

    #region Key Management

    /// <inheritdoc />
    public byte[] GenerateKey(int keySize = 256)
    {
        if (keySize != 128 && keySize != 192 && keySize != 256)
            throw new ArgumentException("Key size must be 128, 192, or 256 bits", nameof(keySize));

        var key = new byte[keySize / 8];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(key);
        }
        return key;
    }

    /// <inheritdoc />
    public byte[] DeriveKeyFromPassword(string password, byte[]? salt = null, int iterations = 100000, int keySize = 256)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password cannot be empty", nameof(password));
        if (iterations < 1000)
            throw new ArgumentException("Iterations must be at least 1000", nameof(iterations));
        if (keySize != 128 && keySize != 192 && keySize != 256)
            throw new ArgumentException("Key size must be 128, 192, or 256 bits", nameof(keySize));

        // Generate salt if not provided
        if (salt == null || salt.Length == 0)
        {
            salt = new byte[DefaultSaltSize];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }
        }

        // Derive key using PBKDF2
        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
        var key = pbkdf2.GetBytes(keySize / 8);

        // Return salt + key for storage
        var result = new byte[salt.Length + key.Length];
        Buffer.BlockCopy(salt, 0, result, 0, salt.Length);
        Buffer.BlockCopy(key, 0, result, salt.Length, key.Length);

        return result;
    }

    /// <summary>
    /// Extracts the salt and key from a derived key result
    /// </summary>
    /// <param name="derivedKey">The combined salt and key from DeriveKeyFromPassword</param>
    /// <returns>Tuple containing the salt and key separately</returns>
    public static (byte[] Salt, byte[] Key) ExtractSaltAndKey(byte[] derivedKey)
    {
        if (derivedKey == null || derivedKey.Length <= DefaultSaltSize)
            throw new ArgumentException("Invalid derived key format", nameof(derivedKey));

        var salt = new byte[DefaultSaltSize];
        var key = new byte[derivedKey.Length - DefaultSaltSize];

        Buffer.BlockCopy(derivedKey, 0, salt, 0, DefaultSaltSize);
        Buffer.BlockCopy(derivedKey, DefaultSaltSize, key, 0, key.Length);

        return (salt, key);
    }

    /// <inheritdoc />
    public string RotateKey(string ciphertext, byte[] oldKey, byte[] newKey)
    {
        if (string.IsNullOrEmpty(ciphertext))
            return string.Empty;

        // Decrypt with old key
        var plaintext = Decrypt(ciphertext, oldKey);

        // Encrypt with new key
        return Encrypt(plaintext, newKey);
    }

    /// <inheritdoc />
    public bool IsValidCiphertext(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
            return false;

        try
        {
            var bytes = Convert.FromBase64String(ciphertext);
            // Must have at least nonce (12) + tag (16) bytes
            return bytes.Length >= AesGcmNonceSize + AesGcmTagSize;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    #endregion

    #region Private Methods

    private static string GenerateKeyId()
    {
        return Guid.NewGuid().ToString("N")[..16];
    }

    #endregion

    #region IDisposable Support

    /// <summary>
    /// Clears the master key from memory
    /// </summary>
    public void Dispose()
    {
        // Clear the master key from memory
        CryptographicOperations.ZeroMemory(_masterKey);
        GC.SuppressFinalize(this);
    }

    #endregion
}

/// <summary>
/// Interface for key storage and management
/// </summary>
public interface IKeyStore
{
    /// <summary>
    /// Stores a key with the specified identifier
    /// </summary>
    /// <param name="keyId">The unique key identifier</param>
    /// <param name="key">The key to store</param>
    Task StoreKeyAsync(string keyId, byte[] key);

    /// <summary>
    /// Retrieves a key by its identifier
    /// </summary>
    /// <param name="keyId">The key identifier</param>
    /// <returns>The key if found; otherwise, null</returns>
    Task<byte[]?> GetKeyAsync(string keyId);

    /// <summary>
    /// Lists all available key identifiers
    /// </summary>
    Task<IReadOnlyList<string>> ListKeyIdsAsync();

    /// <summary>
    /// Deletes a key by its identifier
    /// </summary>
    /// <param name="keyId">The key identifier</param>
    /// <returns>True if the key was deleted; otherwise, false</returns>
    Task<bool> DeleteKeyAsync(string keyId);
}
