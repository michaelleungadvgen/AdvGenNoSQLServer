// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Abstractions;
using AdvGenNoSqlServer.Core.FieldEncryption;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Storage;

/// <summary>
/// Document store wrapper that provides transparent field-level encryption
/// Automatically encrypts fields on insert/update and decrypts on read
/// </summary>
public class EncryptedDocumentStore : IDocumentStore
{
    private readonly IDocumentStore _innerStore;
    private readonly IFieldEncryptor _fieldEncryptor;
    private readonly Dictionary<string, FieldEncryptionConfig> _collectionConfigs;

    /// <summary>
    /// Creates a new EncryptedDocumentStore
    /// </summary>
    /// <param name="innerStore">The underlying document store</param>
    /// <param name="fieldEncryptor">The field encryptor to use</param>
    public EncryptedDocumentStore(IDocumentStore innerStore, IFieldEncryptor fieldEncryptor)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
        _fieldEncryptor = fieldEncryptor ?? throw new ArgumentNullException(nameof(fieldEncryptor));
        _collectionConfigs = new Dictionary<string, FieldEncryptionConfig>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Configures field encryption for a collection
    /// </summary>
    /// <param name="config">The field encryption configuration</param>
    public void ConfigureCollection(FieldEncryptionConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrEmpty(config.CollectionName);

        _collectionConfigs[config.CollectionName] = config;

        // Also configure the field encryptor if it supports it
        if (_fieldEncryptor is FieldEncryptor fe)
        {
            fe.ConfigureCollection(config);
        }
    }

    /// <summary>
    /// Gets the field encryption configuration for a collection
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <returns>The configuration if found; otherwise, null</returns>
    public FieldEncryptionConfig? GetConfiguration(string collectionName)
    {
        _collectionConfigs.TryGetValue(collectionName, out var config);
        return config;
    }

    /// <summary>
    /// Checks if a collection has field encryption configured
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <returns>True if encryption is configured; otherwise, false</returns>
    public bool IsEncryptedCollection(string collectionName)
    {
        return _collectionConfigs.ContainsKey(collectionName) &&
               _collectionConfigs[collectionName].EncryptedFields.Count > 0;
    }

    /// <inheritdoc />
    public async Task<Document> InsertAsync(string collectionName, Document document, CancellationToken cancellationToken = default)
    {
        var config = GetConfiguration(collectionName);
        if (config != null && config.EncryptedFields.Count > 0)
        {
            document = await _fieldEncryptor.EncryptFieldsAsync(
                document,
                config.EncryptedFields,
                config.KeyId,
                cancellationToken);
        }

        return await _innerStore.InsertAsync(collectionName, document, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Document?> GetAsync(string collectionName, string documentId, CancellationToken cancellationToken = default)
    {
        var document = await _innerStore.GetAsync(collectionName, documentId, cancellationToken);
        if (document == null)
            return null;

        var config = GetConfiguration(collectionName);
        if (config != null && config.EncryptedFields.Count > 0)
        {
            document = await _fieldEncryptor.DecryptFieldsAsync(
                document,
                config.EncryptedFields,
                config.KeyId,
                cancellationToken);
        }

        return document;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Document>> GetManyAsync(string collectionName, IEnumerable<string> documentIds, CancellationToken cancellationToken = default)
    {
        var documents = await _innerStore.GetManyAsync(collectionName, documentIds, cancellationToken);
        var config = GetConfiguration(collectionName);

        if (config == null || config.EncryptedFields.Count == 0)
            return documents;

        var decrypted = new List<Document>();
        foreach (var doc in documents)
        {
            var decryptedDoc = await _fieldEncryptor.DecryptFieldsAsync(
                doc,
                config.EncryptedFields,
                config.KeyId,
                cancellationToken);
            decrypted.Add(decryptedDoc);
        }

        return decrypted;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Document>> GetAllAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        var documents = await _innerStore.GetAllAsync(collectionName, cancellationToken);
        var config = GetConfiguration(collectionName);

        if (config == null || config.EncryptedFields.Count == 0)
            return documents;

        var decrypted = new List<Document>();
        foreach (var doc in documents)
        {
            var decryptedDoc = await _fieldEncryptor.DecryptFieldsAsync(
                doc,
                config.EncryptedFields,
                config.KeyId,
                cancellationToken);
            decrypted.Add(decryptedDoc);
        }

        return decrypted;
    }

    /// <inheritdoc />
    public async Task<Document> UpdateAsync(string collectionName, Document document, CancellationToken cancellationToken = default)
    {
        var config = GetConfiguration(collectionName);
        if (config != null && config.EncryptedFields.Count > 0)
        {
            document = await _fieldEncryptor.EncryptFieldsAsync(
                document,
                config.EncryptedFields,
                config.KeyId,
                cancellationToken);
        }

        return await _innerStore.UpdateAsync(collectionName, document, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string collectionName, string documentId, CancellationToken cancellationToken = default)
    {
        return _innerStore.DeleteAsync(collectionName, documentId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string collectionName, string documentId, CancellationToken cancellationToken = default)
    {
        return await _innerStore.ExistsAsync(collectionName, documentId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<long> CountAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        return await _innerStore.CountAsync(collectionName, cancellationToken);
    }

    /// <inheritdoc />
    public Task CreateCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        return _innerStore.CreateCollectionAsync(collectionName, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> DropCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        return _innerStore.DropCollectionAsync(collectionName, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IEnumerable<string>> GetCollectionsAsync(CancellationToken cancellationToken = default)
    {
        return _innerStore.GetCollectionsAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task ClearCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        return _innerStore.ClearCollectionAsync(collectionName, cancellationToken);
    }

    /// <summary>
    /// Rotates the encryption key for a collection
    /// Re-encrypts all documents with a new key
    /// </summary>
    /// <param name="collectionName">The collection to rotate keys for</param>
    /// <param name="newKeyId">The new key ID to use</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of documents re-encrypted</returns>
    public async Task<int> RotateEncryptionKeyAsync(string collectionName, string newKeyId, CancellationToken cancellationToken = default)
    {
        var config = GetConfiguration(collectionName);
        if (config == null || config.EncryptedFields.Count == 0)
            return 0;

        var oldKeyId = config.KeyId;
        var documents = await _innerStore.GetAllAsync(collectionName, cancellationToken);
        var count = 0;

        foreach (var doc in documents)
        {
            // Decrypt with old key
            var decrypted = await _fieldEncryptor.DecryptFieldsAsync(doc, config.EncryptedFields, oldKeyId, cancellationToken);

            // Re-encrypt with new key
            var reencrypted = await _fieldEncryptor.EncryptFieldsAsync(decrypted, config.EncryptedFields, newKeyId, cancellationToken);

            // Update in store
            await _innerStore.UpdateAsync(collectionName, reencrypted, cancellationToken);
            count++;
        }

        // Update the configuration
        config.KeyId = newKeyId;

        return count;
    }
}

/// <summary>
/// Extension methods for setting up encrypted document stores
/// </summary>
public static class EncryptedDocumentStoreExtensions
{
    /// <summary>
    /// Wraps an IDocumentStore with field-level encryption
    /// </summary>
    /// <param name="store">The document store to wrap</param>
    /// <param name="fieldEncryptor">The field encryptor to use</param>
    /// <returns>An encrypted document store</returns>
    public static EncryptedDocumentStore WithFieldEncryption(this IDocumentStore store, IFieldEncryptor fieldEncryptor)
    {
        return new EncryptedDocumentStore(store, fieldEncryptor);
    }

    /// <summary>
    /// Adds field encryption configuration to an encrypted document store
    /// </summary>
    /// <param name="store">The encrypted document store</param>
    /// <param name="config">The field encryption configuration</param>
    /// <returns>The same encrypted document store for chaining</returns>
    public static EncryptedDocumentStore WithEncryptedFields(this EncryptedDocumentStore store, FieldEncryptionConfig config)
    {
        store.ConfigureCollection(config);
        return store;
    }
}
