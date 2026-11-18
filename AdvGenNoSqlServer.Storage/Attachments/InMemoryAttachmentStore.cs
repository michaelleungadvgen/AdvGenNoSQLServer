// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;
using System.Security.Cryptography;
using AdvGenNoSqlServer.Core.Attachments;

namespace AdvGenNoSqlServer.Storage.Attachments;

/// <summary>
/// In-memory attachment store used by cache storage mode. Attachment bytes and
/// metadata live only for the lifetime of the process and are never written to disk.
/// Honors the same size / content-type limits as <see cref="AttachmentStore"/>.
/// </summary>
public sealed class InMemoryAttachmentStore : IAttachmentStore, IDisposable
{
    private readonly AttachmentStoreOptions _options;
    private readonly ConcurrentDictionary<(string Collection, string DocumentId), Dictionary<string, Attachment>> _store = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Creates a new in-memory attachment store with the specified options.
    /// </summary>
    /// <param name="options">Size, quota, and content-type limits to enforce.</param>
    public InMemoryAttachmentStore(AttachmentStoreOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Stores an attachment in memory, replacing any existing attachment with the same name
    /// while preserving its original creation timestamp. Enforces content-type, size, and quota limits.
    /// </summary>
    /// <param name="collectionName">The collection containing the document.</param>
    /// <param name="documentId">The document the attachment belongs to.</param>
    /// <param name="name">The attachment name (unique per document).</param>
    /// <param name="contentType">The MIME content type of the attachment.</param>
    /// <param name="content">The attachment bytes.</param>
    /// <param name="metadata">Optional user-defined metadata.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A result indicating success with attachment info, or failure with a reason.</returns>
    public async Task<AttachmentResult> StoreAsync(
        string collectionName, string documentId, string name, string contentType,
        byte[] content, Dictionary<string, string>? metadata = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateInputs(collectionName, documentId, name, contentType);

        if (!IsContentTypeAllowed(contentType))
            return AttachmentResult.FailureResult($"Content type '{contentType}' is not allowed");

        if (content.Length > _options.MaxAttachmentSize)
            return AttachmentResult.FailureResult(
                $"Attachment size ({content.Length} bytes) exceeds maximum ({_options.MaxAttachmentSize} bytes)");

        var now = DateTime.UtcNow;
        var info = new Attachment
        {
            Name = name,
            ContentType = contentType,
            Size = content.Length,
            Hash = ComputeHash(content),
            CreatedAt = now,
            UpdatedAt = now,
            Metadata = metadata,
            Content = content
        };

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_options.MaxTotalStorage > 0)
            {
                var current = TotalSize();
                var existing = GetDocAttachments(collectionName, documentId).TryGetValue(name, out var prev) ? prev.Size : 0;
                if (current - existing + content.Length > _options.MaxTotalStorage)
                    return AttachmentResult.FailureResult("Storage quota exceeded");
            }

            var attachments = _store.GetOrAdd((collectionName, documentId), _ => new Dictionary<string, Attachment>());
            if (attachments.TryGetValue(name, out var previous))
                info.CreatedAt = previous.CreatedAt;
            attachments[name] = info;
            return AttachmentResult.SuccessResult(CloneInfo(info));
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Retrieves a copy of an attachment, including its content bytes.
    /// </summary>
    /// <param name="collectionName">The collection containing the document.</param>
    /// <param name="documentId">The document the attachment belongs to.</param>
    /// <param name="name">The attachment name.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A cloned attachment, or null if not found.</returns>
    public Task<Attachment?> GetAsync(string collectionName, string documentId, string name, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (GetDocAttachments(collectionName, documentId).TryGetValue(name, out var attachment))
        {
            return Task.FromResult<Attachment?>(new Attachment
            {
                Name = attachment.Name,
                ContentType = attachment.ContentType,
                Size = attachment.Size,
                Hash = attachment.Hash,
                CreatedAt = attachment.CreatedAt,
                UpdatedAt = attachment.UpdatedAt,
                Metadata = attachment.Metadata,
                Content = (byte[])attachment.Content.Clone()
            });
        }
        return Task.FromResult<Attachment?>(null);
    }

    /// <summary>
    /// Retrieves attachment metadata without the content bytes.
    /// </summary>
    /// <param name="collectionName">The collection containing the document.</param>
    /// <param name="documentId">The document the attachment belongs to.</param>
    /// <param name="name">The attachment name.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The attachment info, or null if not found.</returns>
    public Task<AttachmentInfo?> GetInfoAsync(string collectionName, string documentId, string name, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return Task.FromResult(GetDocAttachments(collectionName, documentId).TryGetValue(name, out var a) ? CloneInfo(a) : null);
    }

    /// <summary>
    /// Lists metadata for all attachments of a document.
    /// </summary>
    /// <param name="collectionName">The collection containing the document.</param>
    /// <param name="documentId">The document whose attachments to list.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A read-only list of attachment info entries.</returns>
    public Task<IReadOnlyList<AttachmentInfo>> ListAsync(string collectionName, string documentId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var list = GetDocAttachments(collectionName, documentId).Values.Select(CloneInfo).ToList().AsReadOnly();
        return Task.FromResult<IReadOnlyList<AttachmentInfo>>(list);
    }

    /// <summary>
    /// Deletes a single attachment from a document.
    /// </summary>
    /// <param name="collectionName">The collection containing the document.</param>
    /// <param name="documentId">The document the attachment belongs to.</param>
    /// <param name="name">The attachment name.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>True if the attachment was removed; false if it did not exist.</returns>
    public async Task<bool> DeleteAsync(string collectionName, string documentId, string name, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return GetDocAttachments(collectionName, documentId).Remove(name);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Deletes all attachments belonging to a document.
    /// </summary>
    /// <param name="collectionName">The collection containing the document.</param>
    /// <param name="documentId">The document whose attachments to delete.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The number of attachments removed.</returns>
    public async Task<int> DeleteAllAsync(string collectionName, string documentId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_store.TryRemove((collectionName, documentId), out var attachments))
                return attachments.Count;
            return 0;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Checks whether an attachment exists for a document.
    /// </summary>
    /// <param name="collectionName">The collection containing the document.</param>
    /// <param name="documentId">The document the attachment belongs to.</param>
    /// <param name="name">The attachment name.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>True if the attachment exists; otherwise false.</returns>
    public Task<bool> ExistsAsync(string collectionName, string documentId, string name, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return Task.FromResult(GetDocAttachments(collectionName, documentId).ContainsKey(name));
    }

    /// <summary>
    /// Gets the total size in bytes of all attachments held in memory.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The combined size of all stored attachment content.</returns>
    public Task<long> GetTotalStorageSizeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return Task.FromResult(TotalSize());
    }

    private Dictionary<string, Attachment> GetDocAttachments(string collectionName, string documentId)
        => _store.TryGetValue((collectionName, documentId), out var attachments)
            ? attachments
            : new Dictionary<string, Attachment>();

    private long TotalSize() => _store.Values.Sum(d => d.Values.Sum(a => a.Size));

    private static AttachmentInfo CloneInfo(Attachment a) => new()
    {
        Name = a.Name,
        ContentType = a.ContentType,
        Size = a.Size,
        Hash = a.Hash,
        CreatedAt = a.CreatedAt,
        UpdatedAt = a.UpdatedAt,
        Metadata = a.Metadata
    };

    private static string ComputeHash(byte[] content)
    {
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(content));
    }

    private static void ValidateInputs(string collectionName, string documentId, string name, string contentType)
    {
        if (string.IsNullOrWhiteSpace(collectionName)) throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));
        if (string.IsNullOrWhiteSpace(documentId)) throw new ArgumentException("Document ID cannot be empty", nameof(documentId));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Attachment name cannot be empty", nameof(name));
        if (string.IsNullOrWhiteSpace(contentType)) throw new ArgumentException("Content type cannot be empty", nameof(contentType));
    }

    private bool IsContentTypeAllowed(string contentType)
    {
        if (_options.BlockedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
            return false;
        if (_options.AllowedContentTypes?.Count > 0)
            return _options.AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase);
        return true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(InMemoryAttachmentStore));
    }

    /// <summary>
    /// Releases the internal lock and clears all stored attachments.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _lock.Dispose();
            _store.Clear();
            _disposed = true;
        }
    }
}