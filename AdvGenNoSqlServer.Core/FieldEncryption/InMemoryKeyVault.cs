// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;
using System.Security.Cryptography;
using AdvGenNoSqlServer.Core.Authentication;

namespace AdvGenNoSqlServer.Core.FieldEncryption;

/// <summary>
/// In-memory implementation of the key vault
/// Suitable for development and testing; for production, use a secure key vault
/// </summary>
public class InMemoryKeyVault : IKeyVault
{
    private readonly ConcurrentDictionary<string, DataKey> _keys;
    private readonly ConcurrentDictionary<string, byte[]> _keyMaterials;
    private readonly ConcurrentDictionary<string, string> _altNameToKeyId;
    private readonly IEncryptionService _encryptionService;
    private readonly string _masterKeyId;

    /// <summary>
    /// Creates a new InMemoryKeyVault with a default key
    /// </summary>
    /// <param name="encryptionService">The encryption service for key material encryption</param>
    public InMemoryKeyVault(IEncryptionService encryptionService)
    {
        _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));
        _keys = new ConcurrentDictionary<string, DataKey>();
        _keyMaterials = new ConcurrentDictionary<string, byte[]>();
        _altNameToKeyId = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Create a default key
        _masterKeyId = CreateDefaultKeyAsync().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public string DefaultKeyId => _masterKeyId;

    /// <inheritdoc />
    public Task<DataKey> CreateKeyAsync(string keyAltName, KeyOptions? options = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyAltName);

        var keyId = Guid.NewGuid().ToString("N");
        var keySize = options?.KeySize ?? 256;
        var algorithm = options?.Algorithm ?? EncryptionAlgorithm.AES256GCM;

        // Generate random key material
        var keyBytes = new byte[keySize / 8];
        RandomNumberGenerator.Fill(keyBytes);

        // Optionally encrypt the key material
        byte[]? encryptedKeyMaterial = null;
        if (options?.EncryptKeyMaterial ?? true)
        {
            encryptedKeyMaterial = _encryptionService.Encrypt(keyBytes);
        }

        var dataKey = new DataKey
        {
            KeyId = keyId,
            KeyAltName = keyAltName,
            EncryptedKeyMaterial = encryptedKeyMaterial,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = options?.ExpiresIn.HasValue ?? false
                ? DateTime.UtcNow.Add(options.ExpiresIn.Value)
                : null,
            Status = KeyStatus.Active,
            Version = 1,
            Algorithm = algorithm
        };

        _keys[keyId] = dataKey;
        _keyMaterials[keyId] = keyBytes;
        _altNameToKeyId[keyAltName] = keyId;

        return Task.FromResult(dataKey);
    }

    /// <inheritdoc />
    public Task<DataKey?> GetKeyAsync(string keyId, CancellationToken ct = default)
    {
        _keys.TryGetValue(keyId, out var key);
        return Task.FromResult(key);
    }

    /// <inheritdoc />
    public Task<DataKey?> GetKeyByAltNameAsync(string keyAltName, CancellationToken ct = default)
    {
        if (_altNameToKeyId.TryGetValue(keyAltName, out var keyId))
        {
            _keys.TryGetValue(keyId, out var key);
            return Task.FromResult(key);
        }
        return Task.FromResult<DataKey?>(null);
    }

    /// <inheritdoc />
    public Task<byte[]?> GetKeyMaterialAsync(string keyId, CancellationToken ct = default)
    {
        if (_keyMaterials.TryGetValue(keyId, out var material))
        {
            // Return a copy to prevent external modification
            return Task.FromResult<byte[]?>(material.ToArray());
        }

        // Try to decrypt if we have encrypted material
        if (_keys.TryGetValue(keyId, out var key) && key.EncryptedKeyMaterial != null)
        {
            try
            {
                var decrypted = _encryptionService.Decrypt(key.EncryptedKeyMaterial);
                return Task.FromResult<byte[]?>(decrypted);
            }
            catch
            {
                return Task.FromResult<byte[]?>(null);
            }
        }

        return Task.FromResult<byte[]?>(null);
    }

    /// <inheritdoc />
    public async Task<DataKey> RotateKeyAsync(string keyId, CancellationToken ct = default)
    {
        if (!_keys.TryGetValue(keyId, out var existingKey))
        {
            throw new KeyNotFoundException($"Key '{keyId}' not found");
        }

        // Create a new key with the same alternative name
        var newKey = await CreateKeyAsync(
            existingKey.KeyAltName ?? $"rotated_{keyId}",
            new KeyOptions
            {
                KeySize = existingKey.Algorithm == EncryptionAlgorithm.AES256GCM ? 256 : 256,
                Algorithm = existingKey.Algorithm
            },
            ct);

        // Update the existing key
        existingKey.Status = KeyStatus.Inactive;
        existingKey.RotatedAt = DateTime.UtcNow;
        existingKey.PreviousKeyId = newKey.KeyId;

        return newKey;
    }

    /// <inheritdoc />
    public Task<bool> DeleteKeyAsync(string keyId, CancellationToken ct = default)
    {
        if (_keys.TryRemove(keyId, out var key))
        {
            _keyMaterials.TryRemove(keyId, out _);
            if (!string.IsNullOrEmpty(key.KeyAltName))
            {
                _altNameToKeyId.TryRemove(key.KeyAltName, out _);
            }
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DataKey>> ListKeysAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<DataKey>>(_keys.Values.ToList());
    }

    private async Task<string> CreateDefaultKeyAsync()
    {
        var key = await CreateKeyAsync("default", new KeyOptions { KeySize = 256 });
        return key.KeyId;
    }
}
