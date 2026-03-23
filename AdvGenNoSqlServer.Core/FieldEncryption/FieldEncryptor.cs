// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Text;
using System.Text.Json;
using AdvGenNoSqlServer.Core.Authentication;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Core.FieldEncryption;

/// <summary>
/// Implementation of field-level encryption using AES-256-GCM
/// Provides transparent encryption/decryption of sensitive document fields
/// </summary>
public class FieldEncryptor : IFieldEncryptor
{
    private readonly IEncryptionService _encryptionService;
    private readonly IKeyVault _keyVault;
    private readonly Dictionary<string, FieldEncryptionConfig> _collectionConfigs;

    /// <summary>
    /// Creates a new FieldEncryptor
    /// </summary>
    /// <param name="encryptionService">The encryption service to use</param>
    /// <param name="keyVault">The key vault for key management</param>
    public FieldEncryptor(IEncryptionService encryptionService, IKeyVault? keyVault = null)
    {
        _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));
        _keyVault = keyVault ?? new InMemoryKeyVault(encryptionService);
        _collectionConfigs = new Dictionary<string, FieldEncryptionConfig>(StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public string DefaultKeyId => _keyVault.DefaultKeyId;

    /// <summary>
    /// Configures field encryption for a collection
    /// </summary>
    /// <param name="config">The configuration</param>
    public void ConfigureCollection(FieldEncryptionConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrEmpty(config.CollectionName);

        _collectionConfigs[config.CollectionName] = config;
    }

    /// <summary>
    /// Gets the configuration for a collection
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <returns>The configuration if found; otherwise, null</returns>
    public FieldEncryptionConfig? GetConfiguration(string collectionName)
    {
        _collectionConfigs.TryGetValue(collectionName, out var config);
        return config;
    }

    /// <inheritdoc />
    public Task<Document> EncryptFieldsAsync(Document document, IEnumerable<string> fieldPaths, string? keyId = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(fieldPaths);

        var paths = fieldPaths.ToList();
        if (paths.Count == 0 || document.Data == null)
            return Task.FromResult(document);

        // Deep clone the data dictionary
        var mutableDict = DeepCloneDictionary(document.Data);

        foreach (var fieldPath in paths)
        {
            EncryptField(mutableDict, fieldPath, keyId ?? DefaultKeyId);
        }

        return Task.FromResult(new Document
        {
            Id = document.Id,
            Data = mutableDict,
            CreatedAt = document.CreatedAt,
            UpdatedAt = document.UpdatedAt,
            Version = document.Version
        });
    }

    /// <inheritdoc />
    public Task<Document> DecryptFieldsAsync(Document document, IEnumerable<string> fieldPaths, string? keyId = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(fieldPaths);

        var paths = fieldPaths.ToList();
        if (paths.Count == 0 || document.Data == null)
            return Task.FromResult(document);

        // Deep clone the data dictionary
        var mutableDict = DeepCloneDictionary(document.Data);

        foreach (var fieldPath in paths)
        {
            DecryptField(mutableDict, fieldPath, keyId);
        }

        return Task.FromResult(new Document
        {
            Id = document.Id,
            Data = mutableDict,
            CreatedAt = document.CreatedAt,
            UpdatedAt = document.UpdatedAt,
            Version = document.Version
        });
    }

    /// <inheritdoc />
    public Task<string> EncryptValueAsync(object? fieldValue, string? keyId = null, CancellationToken ct = default)
    {
        if (fieldValue == null)
            return Task.FromResult(string.Empty);

        var effectiveKeyId = keyId ?? DefaultKeyId;
        var serializedValue = SerializeValue(fieldValue);
        var encrypted = _encryptionService.Encrypt(serializedValue);

        // Prefix with key ID for later decryption
        var prefixed = $"__enc__:{effectiveKeyId}:{encrypted}";
        return Task.FromResult(Convert.ToBase64String(Encoding.UTF8.GetBytes(prefixed)));
    }

    /// <inheritdoc />
    public Task<object?> DecryptValueAsync(string encryptedValue, Type targetType, string? keyId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(encryptedValue))
            return Task.FromResult<object?>(null);

        try
        {
            var prefixed = Encoding.UTF8.GetString(Convert.FromBase64String(encryptedValue));

            // Check if it's an encrypted value
            if (!prefixed.StartsWith("__enc__:"))
                return Task.FromResult<object?>(Convert.ChangeType(prefixed, targetType));

            var parts = prefixed.Split(':', 3);
            if (parts.Length != 3)
                throw new FieldEncryptionException("Invalid encrypted value format");

            var effectiveKeyId = keyId ?? parts[1];
            var ciphertext = parts[2];

            var decrypted = _encryptionService.Decrypt(ciphertext);
            var result = DeserializeValue(decrypted, targetType);

            return Task.FromResult(result);
        }
        catch (Exception ex) when (ex is not FieldEncryptionException)
        {
            throw new FieldEncryptionException("Failed to decrypt value", ex);
        }
    }

    /// <inheritdoc />
    public bool IsEncryptedValue(object? fieldValue)
    {
        if (fieldValue is not string str)
            return false;

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(str));
            return decoded.StartsWith("__enc__:");
        }
        catch
        {
            return false;
        }
    }

    private void EncryptField(Dictionary<string, object?> dict, string fieldPath, string keyId)
    {
        var parts = fieldPath.Split('.');
        var current = dict;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (!current.TryGetValue(parts[i], out var next) || next is not Dictionary<string, object?> nextDict)
                return; // Path doesn't exist, skip

            current = nextDict;
        }

        var finalField = parts[^1];
        if (current.TryGetValue(finalField, out var value) && value != null)
        {
            var encrypted = EncryptValueAsync(value, keyId).GetAwaiter().GetResult();
            current[finalField] = encrypted;
        }
    }

    private void DecryptField(Dictionary<string, object?> dict, string fieldPath, string? keyId)
    {
        var parts = fieldPath.Split('.');
        var current = dict;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (!current.TryGetValue(parts[i], out var next) || next is not Dictionary<string, object?> nextDict)
                return; // Path doesn't exist, skip

            current = nextDict;
        }

        var finalField = parts[^1];
        if (current.TryGetValue(finalField, out var value) && value is string strValue && IsEncryptedValue(strValue))
        {
            var decrypted = DecryptValueAsync(strValue, typeof(string), keyId).GetAwaiter().GetResult();
            current[finalField] = decrypted;
        }
    }

    private static Dictionary<string, object?> DeepCloneDictionary(Dictionary<string, object?> original)
    {
        var clone = new Dictionary<string, object?>();
        foreach (var kvp in original)
        {
            clone[kvp.Key] = DeepCloneValue(kvp.Value);
        }
        return clone;
    }

    private static object? DeepCloneValue(object? value)
    {
        return value switch
        {
            null => null,
            Dictionary<string, object?> dict => DeepCloneDictionary(dict),
            System.Collections.IEnumerable enumerable and not string => enumerable.Cast<object?>().Select(DeepCloneValue).ToList(),
            JsonElement element => JsonToObject(element),
            _ => value
        };
    }

    private static object? JsonToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => JsonToDictionary(element),
            JsonValueKind.Array => element.EnumerateArray().Select(JsonToObject).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }

    private static Dictionary<string, object?> JsonToDictionary(JsonElement element)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var property in element.EnumerateObject())
        {
            dict[property.Name] = JsonToObject(property.Value);
        }
        return dict;
    }

    private static string SerializeValue(object value)
    {
        return value switch
        {
            string s => $"S:{s}",
            int i => $"I:{i}",
            long l => $"L:{l}",
            double d => $"D:{d}",
            bool b => $"B:{b}",
            DateTime dt => $"T:{dt:o}",
            DateTimeOffset dto => $"O:{dto:o}",
            _ => $"J:{JsonSerializer.Serialize(value)}"
        };
    }

    private static object? DeserializeValue(string serialized, Type targetType)
    {
        if (serialized.Length < 2 || serialized[1] != ':')
        {
            // Legacy format or plain string
            return Convert.ChangeType(serialized, targetType);
        }

        var typeCode = serialized[0];
        var value = serialized[2..];

        return typeCode switch
        {
            'S' => value,
            'I' => int.Parse(value),
            'L' => long.Parse(value),
            'D' => double.Parse(value),
            'B' => bool.Parse(value),
            'T' => DateTime.Parse(value),
            'O' => DateTimeOffset.Parse(value),
            'J' => JsonSerializer.Deserialize(value, targetType),
            _ => Convert.ChangeType(value, targetType)
        };
    }
}

/// <summary>
/// Exception thrown when field encryption operations fail
/// </summary>
public class FieldEncryptionException : Exception
{
    public FieldEncryptionException(string message) : base(message) { }
    public FieldEncryptionException(string message, Exception innerException) : base(message, innerException) { }
}
