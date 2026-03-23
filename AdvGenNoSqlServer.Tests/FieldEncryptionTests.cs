// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Security.Cryptography;
using System.Text.Json;
using AdvGenNoSqlServer.Core.Authentication;
using AdvGenNoSqlServer.Core.Configuration;
using AdvGenNoSqlServer.Core.FieldEncryption;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Storage;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for field-level encryption functionality
/// </summary>
public class FieldEncryptionTests
{
    private readonly EncryptionService _encryptionService;
    private readonly InMemoryKeyVault _keyVault;
    private readonly FieldEncryptor _fieldEncryptor;
    private readonly DocumentStore _documentStore;

    public FieldEncryptionTests()
    {
        var config = new ServerConfiguration { EncryptionKey = GenerateTestKey() };
        _encryptionService = new EncryptionService(config);
        _keyVault = new InMemoryKeyVault(_encryptionService);
        _fieldEncryptor = new FieldEncryptor(_encryptionService, _keyVault);
        _documentStore = new DocumentStore();
    }

    private static string GenerateTestKey()
    {
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        return Convert.ToBase64String(key);
    }

    #region IFieldEncryptor Tests

    [Fact]
    public void Constructor_WithNullEncryptionService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new FieldEncryptor(null!, _keyVault));
    }

    [Fact]
    public void Constructor_WithValidDependencies_CreatesInstance()
    {
        var encryptor = new FieldEncryptor(_encryptionService, _keyVault);
        Assert.NotNull(encryptor);
        Assert.Equal(_keyVault.DefaultKeyId, encryptor.DefaultKeyId);
    }

    [Fact]
    public void ConfigureCollection_WithNullConfig_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _fieldEncryptor.ConfigureCollection(null!));
    }

    [Fact]
    public void ConfigureCollection_WithValidConfig_ConfiguresCollection()
    {
        var config = new FieldEncryptionConfig
        {
            CollectionName = "users",
            EncryptedFields = new List<string> { "ssn", "creditCard" },
            KeyId = "default"
        };

        _fieldEncryptor.ConfigureCollection(config);

        var retrieved = _fieldEncryptor.GetConfiguration("users");
        Assert.NotNull(retrieved);
        Assert.Equal("users", retrieved.CollectionName);
        Assert.Equal(2, retrieved.EncryptedFields.Count);
    }

    [Fact]
    public void GetConfiguration_NonExistentCollection_ReturnsNull()
    {
        var config = _fieldEncryptor.GetConfiguration("nonexistent");
        Assert.Null(config);
    }

    [Fact]
    public async Task EncryptValueAsync_NullValue_ReturnsEmptyString()
    {
        var result = await _fieldEncryptor.EncryptValueAsync(null);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task EncryptValueAsync_StringValue_ReturnsEncryptedBase64()
    {
        var value = "sensitive data";
        var result = await _fieldEncryptor.EncryptValueAsync(value);

        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.True(_fieldEncryptor.IsEncryptedValue(result));
    }

    [Fact]
    public async Task EncryptValueAsync_IntegerValue_ReturnsEncryptedBase64()
    {
        var value = 12345;
        var result = await _fieldEncryptor.EncryptValueAsync(value);

        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.True(_fieldEncryptor.IsEncryptedValue(result));
    }

    [Fact]
    public async Task EncryptDecryptValue_RoundTrip_ReturnsOriginalValue()
    {
        var original = "secret message";
        var encrypted = await _fieldEncryptor.EncryptValueAsync(original);
        var decrypted = await _fieldEncryptor.DecryptValueAsync(encrypted, typeof(string));

        Assert.Equal(original, decrypted);
    }

    [Fact]
    public async Task EncryptDecryptValue_Integer_RoundTrip_ReturnsOriginalValue()
    {
        var original = 42;
        var encrypted = await _fieldEncryptor.EncryptValueAsync(original);
        var decrypted = await _fieldEncryptor.DecryptValueAsync(encrypted, typeof(int));

        Assert.Equal(original, decrypted);
    }

    [Fact]
    public async Task EncryptDecryptValue_Boolean_RoundTrip_ReturnsOriginalValue()
    {
        var original = true;
        var encrypted = await _fieldEncryptor.EncryptValueAsync(original);
        var decrypted = await _fieldEncryptor.DecryptValueAsync(encrypted, typeof(bool));

        Assert.Equal(original, decrypted);
    }

    [Fact]
    public async Task EncryptDecryptValue_DateTime_RoundTrip_ReturnsOriginalValue()
    {
        var original = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        var encrypted = await _fieldEncryptor.EncryptValueAsync(original);
        var decrypted = await _fieldEncryptor.DecryptValueAsync(encrypted, typeof(DateTime));

        // Compare using UTC to handle timezone conversions
        Assert.Equal(original.ToUniversalTime(), ((DateTime)decrypted!).ToUniversalTime());
    }

    [Fact]
    public void IsEncryptedValue_NonEncryptedString_ReturnsFalse()
    {
        var result = _fieldEncryptor.IsEncryptedValue("plain text");
        Assert.False(result);
    }

    [Fact]
    public void IsEncryptedValue_NullValue_ReturnsFalse()
    {
        var result = _fieldEncryptor.IsEncryptedValue(null);
        Assert.False(result);
    }

    [Fact]
    public void IsEncryptedValue_InvalidBase64_ReturnsFalse()
    {
        var result = _fieldEncryptor.IsEncryptedValue("not-valid-base64!!!");
        Assert.False(result);
    }

    #endregion

    #region Document Encryption Tests

    [Fact]
    public async Task EncryptFieldsAsync_NullDocument_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _fieldEncryptor.EncryptFieldsAsync(null!, new[] { "field" }));
    }

    [Fact]
    public async Task EncryptFieldsAsync_EmptyFieldList_ReturnsSameDocument()
    {
        var doc = CreateDocument("doc1", new { name = "John", ssn = "123-45-6789" });
        var result = await _fieldEncryptor.EncryptFieldsAsync(doc, new List<string>());

        Assert.Equal(doc.Id, result.Id);
        Assert.Equal(doc.Data?["ssn"]?.ToString(), result.Data?["ssn"]?.ToString());
    }

    [Fact]
    public async Task EncryptFieldsAsync_SingleField_EncryptsField()
    {
        var doc = CreateDocument("doc1", new { name = "John", ssn = "123-45-6789" });
        var result = await _fieldEncryptor.EncryptFieldsAsync(doc, new[] { "ssn" });

        var ssnValue = result.Data?["ssn"]?.ToString();
        Assert.NotNull(ssnValue);
        Assert.True(_fieldEncryptor.IsEncryptedValue(ssnValue));

        // Name should not be encrypted
        var nameValue = result.Data?["name"]?.ToString();
        Assert.Equal("John", nameValue);
    }

    [Fact]
    public async Task EncryptDecryptFieldsAsync_RoundTrip_ReturnsOriginalData()
    {
        var originalSsn = "123-45-6789";
        var doc = CreateDocument("doc1", new { name = "John", ssn = originalSsn });

        var encrypted = await _fieldEncryptor.EncryptFieldsAsync(doc, new[] { "ssn" });
        var decrypted = await _fieldEncryptor.DecryptFieldsAsync(encrypted, new[] { "ssn" });

        Assert.Equal(originalSsn, decrypted.Data?["ssn"]?.ToString());
        Assert.Equal("John", decrypted.Data?["name"]?.ToString());
    }

    [Fact]
    public async Task EncryptFieldsAsync_NestedField_EncryptsNestedValue()
    {
        var doc = CreateDocument("doc1", new
        {
            name = "John",
            profile = new { ssn = "123-45-6789", age = 30 }
        });

        var result = await _fieldEncryptor.EncryptFieldsAsync(doc, new[] { "profile.ssn" });

        var profile = result.Data?["profile"] as Dictionary<string, object?>;
        var ssnValue = profile?["ssn"]?.ToString();
        Assert.True(_fieldEncryptor.IsEncryptedValue(ssnValue));
        Assert.Equal(30, GetInt32Value(profile, "age"));
    }

    [Fact]
    public async Task EncryptDecryptFieldsAsync_NestedField_RoundTrip_ReturnsOriginalData()
    {
        var originalSsn = "123-45-6789";
        var doc = CreateDocument("doc1", new
        {
            name = "John",
            profile = new { ssn = originalSsn, age = 30 }
        });

        var encrypted = await _fieldEncryptor.EncryptFieldsAsync(doc, new[] { "profile.ssn" });
        var decrypted = await _fieldEncryptor.DecryptFieldsAsync(encrypted, new[] { "profile.ssn" });

        var decryptedProfile = decrypted.Data?["profile"] as Dictionary<string, object?>;
        Assert.Equal(originalSsn, decryptedProfile?["ssn"]?.ToString());
    }

    [Fact]
    public async Task EncryptFieldsAsync_MultipleFields_EncryptsAllSpecifiedFields()
    {
        var doc = CreateDocument("doc1", new
        {
            name = "John",
            ssn = "123-45-6789",
            creditCard = "4111-1111-1111-1111"
        });

        var result = await _fieldEncryptor.EncryptFieldsAsync(doc, new[] { "ssn", "creditCard" });

        Assert.True(_fieldEncryptor.IsEncryptedValue(result.Data?["ssn"]?.ToString()));
        Assert.True(_fieldEncryptor.IsEncryptedValue(result.Data?["creditCard"]?.ToString()));
        Assert.Equal("John", result.Data?["name"]?.ToString());
    }

    [Fact]
    public async Task EncryptFieldsAsync_NonExistentField_DoesNotThrow()
    {
        var doc = CreateDocument("doc1", new { name = "John" });

        var result = await _fieldEncryptor.EncryptFieldsAsync(doc, new[] { "nonexistent" });

        Assert.Equal("John", result.Data?["name"]?.ToString());
    }

    #endregion

    #region InMemoryKeyVault Tests

    [Fact]
    public void InMemoryKeyVault_Constructor_CreatesDefaultKey()
    {
        Assert.NotNull(_keyVault.DefaultKeyId);
        Assert.NotEmpty(_keyVault.DefaultKeyId);
    }

    [Fact]
    public async Task CreateKeyAsync_WithValidName_CreatesKey()
    {
        var key = await _keyVault.CreateKeyAsync("test-key");

        Assert.NotNull(key);
        Assert.NotEmpty(key.KeyId);
        Assert.Equal("test-key", key.KeyAltName);
        Assert.Equal(KeyStatus.Active, key.Status);
    }

    [Fact]
    public async Task CreateKeyAsync_WithDuplicateAltName_UpdatesMapping()
    {
        var key1 = await _keyVault.CreateKeyAsync("shared-name");
        var key2 = await _keyVault.CreateKeyAsync("shared-name");

        // The latest key should be accessible by alt name
        var retrieved = await _keyVault.GetKeyByAltNameAsync("shared-name");
        Assert.Equal(key2.KeyId, retrieved?.KeyId);
    }

    [Fact]
    public async Task GetKeyAsync_ExistingKey_ReturnsKey()
    {
        var created = await _keyVault.CreateKeyAsync("get-test");
        var retrieved = await _keyVault.GetKeyAsync(created.KeyId);

        Assert.NotNull(retrieved);
        Assert.Equal(created.KeyId, retrieved.KeyId);
    }

    [Fact]
    public async Task GetKeyAsync_NonExistentKey_ReturnsNull()
    {
        var retrieved = await _keyVault.GetKeyAsync("non-existent-id");
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task GetKeyByAltNameAsync_ExistingAltName_ReturnsKey()
    {
        var created = await _keyVault.CreateKeyAsync("alt-name-test");
        var retrieved = await _keyVault.GetKeyByAltNameAsync("alt-name-test");

        Assert.NotNull(retrieved);
        Assert.Equal(created.KeyId, retrieved.KeyId);
    }

    [Fact]
    public async Task GetKeyMaterialAsync_ExistingKey_ReturnsKeyBytes()
    {
        var created = await _keyVault.CreateKeyAsync("material-test");
        var material = await _keyVault.GetKeyMaterialAsync(created.KeyId);

        Assert.NotNull(material);
        Assert.Equal(32, material.Length); // 256 bits = 32 bytes
    }

    [Fact]
    public async Task RotateKeyAsync_ExistingKey_CreatesNewKey()
    {
        var original = await _keyVault.CreateKeyAsync("rotate-test");
        var rotated = await _keyVault.RotateKeyAsync(original.KeyId);

        Assert.NotNull(rotated);
        Assert.NotEqual(original.KeyId, rotated.KeyId);
        Assert.Equal("rotate-test", rotated.KeyAltName);

        // Original key should be inactive
        var originalUpdated = await _keyVault.GetKeyAsync(original.KeyId);
        Assert.Equal(KeyStatus.Inactive, originalUpdated?.Status);
        Assert.NotNull(originalUpdated?.RotatedAt);
    }

    [Fact]
    public async Task RotateKeyAsync_NonExistentKey_ThrowsKeyNotFoundException()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _keyVault.RotateKeyAsync("non-existent"));
    }

    [Fact]
    public async Task DeleteKeyAsync_ExistingKey_RemovesKey()
    {
        var created = await _keyVault.CreateKeyAsync("delete-test");
        var deleted = await _keyVault.DeleteKeyAsync(created.KeyId);

        Assert.True(deleted);
        var retrieved = await _keyVault.GetKeyAsync(created.KeyId);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task DeleteKeyAsync_NonExistentKey_ReturnsFalse()
    {
        var result = await _keyVault.DeleteKeyAsync("non-existent");
        Assert.False(result);
    }

    [Fact]
    public async Task ListKeysAsync_ReturnsAllKeys()
    {
        await _keyVault.CreateKeyAsync("key1");
        await _keyVault.CreateKeyAsync("key2");

        var keys = await _keyVault.ListKeysAsync();

        Assert.True(keys.Count >= 2); // Including default key
    }

    #endregion

    #region EncryptedDocumentStore Tests

    [Fact]
    public void EncryptedDocumentStore_Constructor_WithNullInnerStore_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new EncryptedDocumentStore(null!, _fieldEncryptor));
    }

    [Fact]
    public void EncryptedDocumentStore_Constructor_WithNullEncryptor_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new EncryptedDocumentStore(_documentStore, null!));
    }

    [Fact]
    public void EncryptedDocumentStore_Constructor_WithValidArgs_CreatesInstance()
    {
        var store = new EncryptedDocumentStore(_documentStore, _fieldEncryptor);
        Assert.NotNull(store);
    }

    [Fact]
    public void EncryptedDocumentStore_ConfigureCollection_WithValidConfig_ConfiguresCollection()
    {
        var store = new EncryptedDocumentStore(_documentStore, _fieldEncryptor);
        var config = new FieldEncryptionConfig
        {
            CollectionName = "users",
            EncryptedFields = new List<string> { "ssn" }
        };

        store.ConfigureCollection(config);

        Assert.True(store.IsEncryptedCollection("users"));
        Assert.False(store.IsEncryptedCollection("other"));
    }

    [Fact]
    public async Task InsertAsync_EncryptedCollection_EncryptsFields()
    {
        var store = CreateEncryptedStore();
        var doc = CreateDocument("doc1", new { name = "John", ssn = "123-45-6789" });

        var inserted = await store.InsertAsync("users", doc);

        // Get directly from inner store to verify encryption
        var fromInner = await _documentStore.GetAsync("users", "doc1");
        Assert.NotNull(fromInner);

        var ssnValue = fromInner.Data?["ssn"]?.ToString();
        Assert.True(_fieldEncryptor.IsEncryptedValue(ssnValue));
    }

    [Fact]
    public async Task GetAsync_EncryptedCollection_DecryptsFields()
    {
        var store = CreateEncryptedStore();
        var originalSsn = "123-45-6789";
        var doc = CreateDocument("doc1", new { name = "John", ssn = originalSsn });

        await store.InsertAsync("users", doc);
        var retrieved = await store.GetAsync("users", "doc1");

        Assert.NotNull(retrieved);
        Assert.Equal(originalSsn, retrieved.Data?["ssn"]?.ToString());
        Assert.Equal("John", retrieved.Data?["name"]?.ToString());
    }

    [Fact]
    public async Task GetAsync_NonExistentDocument_ReturnsNull()
    {
        var store = CreateEncryptedStore();
        var result = await store.GetAsync("users", "non-existent");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetManyAsync_EncryptedCollection_DecryptsAllDocuments()
    {
        var store = CreateEncryptedStore();
        await store.InsertAsync("users", CreateDocument("doc1", new { name = "John", ssn = "111-11-1111" }));
        await store.InsertAsync("users", CreateDocument("doc2", new { name = "Jane", ssn = "222-22-2222" }));

        var results = await store.GetManyAsync("users", new[] { "doc1", "doc2" });
        var list = results.ToList();

        Assert.Equal(2, list.Count);
        Assert.Contains(list, d => d.Data?["ssn"]?.ToString() == "111-11-1111");
        Assert.Contains(list, d => d.Data?["ssn"]?.ToString() == "222-22-2222");
    }

    [Fact]
    public async Task GetAllAsync_EncryptedCollection_DecryptsAllDocuments()
    {
        var store = CreateEncryptedStore();
        await store.InsertAsync("users", CreateDocument("doc1", new { name = "John", ssn = "111-11-1111" }));
        await store.InsertAsync("users", CreateDocument("doc2", new { name = "Jane", ssn = "222-22-2222" }));

        var results = await store.GetAllAsync("users");
        var list = results.ToList();

        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task UpdateAsync_EncryptedCollection_EncryptsFields()
    {
        var store = CreateEncryptedStore();
        var doc = CreateDocument("doc1", new { name = "John", ssn = "123-45-6789" });
        await store.InsertAsync("users", doc);

        var updatedDoc = CreateDocument("doc1", new { name = "John Updated", ssn = "987-65-4321" });
        await store.UpdateAsync("users", updatedDoc);

        var retrieved = await store.GetAsync("users", "doc1");
        Assert.Equal("987-65-4321", retrieved?.Data?["ssn"]?.ToString());
        Assert.Equal("John Updated", retrieved?.Data?["name"]?.ToString());
    }

    [Fact]
    public async Task DeleteAsync_RemovesDocument()
    {
        var store = CreateEncryptedStore();
        var doc = CreateDocument("doc1", new { name = "John", ssn = "123-45-6789" });
        await store.InsertAsync("users", doc);

        var deleted = await store.DeleteAsync("users", "doc1");
        Assert.True(deleted);

        var retrieved = await store.GetAsync("users", "doc1");
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task ExistsAsync_ReturnsCorrectValue()
    {
        var store = CreateEncryptedStore();
        var doc = CreateDocument("doc1", new { name = "John", ssn = "123-45-6789" });
        await store.InsertAsync("users", doc);

        Assert.True(await store.ExistsAsync("users", "doc1"));
        Assert.False(await store.ExistsAsync("users", "non-existent"));
    }

    [Fact]
    public async Task CountAsync_ReturnsCorrectCount()
    {
        var store = CreateEncryptedStore();
        await store.InsertAsync("users", CreateDocument("doc1", new { name = "John", ssn = "111" }));
        await store.InsertAsync("users", CreateDocument("doc2", new { name = "Jane", ssn = "222" }));

        var count = await store.CountAsync("users");
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task RotateEncryptionKeyAsync_ReEncryptsAllDocuments()
    {
        var store = CreateEncryptedStore();
        await store.InsertAsync("users", CreateDocument("doc1", new { name = "John", ssn = "123-45-6789" }));

        // Create a new key
        var newKey = await _keyVault.CreateKeyAsync("new-key");

        // Rotate encryption key
        var count = await store.RotateEncryptionKeyAsync("users", newKey.KeyId);
        Assert.Equal(1, count);

        // Verify document is still accessible
        var retrieved = await store.GetAsync("users", "doc1");
        Assert.Equal("123-45-6789", retrieved?.Data?["ssn"]?.ToString());
    }

    #endregion

    #region Extension Method Tests

    [Fact]
    public void WithFieldEncryption_Extension_CreatesEncryptedStore()
    {
        var encrypted = _documentStore.WithFieldEncryption(_fieldEncryptor);
        Assert.NotNull(encrypted);
        Assert.IsType<EncryptedDocumentStore>(encrypted);
    }

    [Fact]
    public void WithEncryptedFields_Extension_ConfiguresEncryption()
    {
        var encrypted = _documentStore
            .WithFieldEncryption(_fieldEncryptor)
            .WithEncryptedFields(new FieldEncryptionConfig
            {
                CollectionName = "users",
                EncryptedFields = new List<string> { "ssn" }
            });

        Assert.True(encrypted.IsEncryptedCollection("users"));
    }

    #endregion

    #region Configuration Tests

    [Fact]
    public void FieldEncryptionConfig_DefaultValues_AreCorrect()
    {
        var config = new FieldEncryptionConfig
        {
            CollectionName = "test"
        };

        Assert.Equal("default", config.KeyId);
        Assert.Equal(EncryptionAlgorithm.AES256GCM, config.Algorithm);
        Assert.False(config.EncryptNullValues);
        Assert.Equal("__enc__", config.EncryptionPrefix);
        Assert.Empty(config.EncryptedFields);
    }

    [Fact]
    public void EncryptedFieldAttribute_DefaultConstructor_SetsDefaultKeyId()
    {
        var attr = new EncryptedFieldAttribute();
        Assert.Equal("default", attr.KeyId);
    }

    [Fact]
    public void EncryptedFieldAttribute_WithKeyId_SetsKeyId()
    {
        var attr = new EncryptedFieldAttribute("custom-key");
        Assert.Equal("custom-key", attr.KeyId);
    }

    #endregion

    #region Exception Tests

    [Fact]
    public void FieldEncryptionException_Constructor_WithMessage_SetsMessage()
    {
        var ex = new FieldEncryptionException("test message");
        Assert.Equal("test message", ex.Message);
    }

    [Fact]
    public void FieldEncryptionException_Constructor_WithInnerException_SetsInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new FieldEncryptionException("outer", inner);
        Assert.Equal("outer", ex.Message);
        Assert.Equal(inner, ex.InnerException);
    }

    [Fact]
    public async Task DecryptValueAsync_InvalidFormat_ThrowsFieldEncryptionException()
    {
        await Assert.ThrowsAsync<FieldEncryptionException>(() =>
            _fieldEncryptor.DecryptValueAsync("__enc__:invalid", typeof(string)));
    }

    #endregion

    #region Helper Methods

    private Document CreateDocument(string id, object data)
    {
        var json = JsonSerializer.Serialize(data);
        var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(json);

        return new Document
        {
            Id = id,
            Data = dict ?? new Dictionary<string, object?>(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };
    }

    private static string? GetStringValue(Dictionary<string, object?>? data, string key)
    {
        if (data == null || !data.TryGetValue(key, out var value))
            return null;
        return value?.ToString();
    }

    private static int GetInt32Value(Dictionary<string, object?>? data, string key)
    {
        if (data == null || !data.TryGetValue(key, out var value))
            return 0;
        return value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            string s => int.TryParse(s, out var result) ? result : 0,
            JsonElement je => je.GetInt32(),
            _ => 0
        };
    }

    private static Dictionary<string, object?>? GetNestedDict(Dictionary<string, object?>? data, string key)
    {
        if (data == null || !data.TryGetValue(key, out var value))
            return null;
        return value as Dictionary<string, object?>;
    }

    private EncryptedDocumentStore CreateEncryptedStore()
    {
        var store = new EncryptedDocumentStore(_documentStore, _fieldEncryptor);
        store.ConfigureCollection(new FieldEncryptionConfig
        {
            CollectionName = "users",
            EncryptedFields = new List<string> { "ssn" },
            KeyId = "default"
        });
        return store;
    }

    #endregion
}

