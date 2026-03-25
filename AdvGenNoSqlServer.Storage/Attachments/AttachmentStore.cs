// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Security.Cryptography;
using System.Text.Json;
using AdvGenNoSqlServer.Core.Attachments;

namespace AdvGenNoSqlServer.Storage.Attachments;

/// <summary>
/// File-based implementation of attachment storage
/// Stores attachments in a folder structure: basePath/collectionName/documentId/attachmentName
/// </summary>
public class AttachmentStore : IAttachmentStore, IDisposable
{
    private readonly AttachmentStoreOptions _options;
    private readonly string _metadataFileName = "_metadata.json";
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Creates a new attachment store
    /// </summary>
    public AttachmentStore(AttachmentStoreOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        
        // Ensure base directory exists
        Directory.CreateDirectory(_options.BasePath);
    }

    /// <inheritdoc />
    public async Task<AttachmentResult> StoreAsync(
        string collectionName, 
        string documentId, 
        string name, 
        string contentType, 
        byte[] content,
        Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        ValidateInputs(collectionName, documentId, name, contentType);
        
        // Check content type restrictions
        if (!IsContentTypeAllowed(contentType))
        {
            return AttachmentResult.FailureResult($"Content type '{contentType}' is not allowed");
        }
        
        // Check size limit
        if (content.Length > _options.MaxAttachmentSize)
        {
            return AttachmentResult.FailureResult(
                $"Attachment size ({content.Length} bytes) exceeds maximum ({_options.MaxAttachmentSize} bytes)");
        }
        
        // Check total storage limit
        if (_options.MaxTotalStorage > 0)
        {
            var currentSize = await GetTotalStorageSizeAsync(cancellationToken);
            if (currentSize + content.Length > _options.MaxTotalStorage)
            {
                return AttachmentResult.FailureResult("Storage quota exceeded");
            }
        }
        
        var now = DateTime.UtcNow;
        var hash = ComputeHash(content);
        
        var attachmentInfo = new AttachmentInfo
        {
            Name = name,
            ContentType = contentType,
            Size = content.Length,
            Hash = hash,
            CreatedAt = now,
            UpdatedAt = now,
            Metadata = metadata
        };
        
        var docPath = GetDocumentPath(collectionName, documentId);
        var attachmentPath = Path.Combine(docPath, SanitizeFileName(name));
        var metadataPath = Path.Combine(docPath, _metadataFileName);
        var tempPath = attachmentPath + ".tmp";
        
        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Ensure directory exists
            Directory.CreateDirectory(docPath);
            
            // Write content to file (atomic write via temp file)
            await File.WriteAllBytesAsync(tempPath, content, cancellationToken);
            
            // Move temp to final (atomic on most filesystems)
            if (File.Exists(attachmentPath))
            {
                File.Delete(attachmentPath);
            }
            File.Move(tempPath, attachmentPath);
            
            // Update metadata
            var attachments = await LoadMetadataAsync(metadataPath, cancellationToken);
            attachments[name] = attachmentInfo;
            await SaveMetadataAsync(metadataPath, attachments, cancellationToken);
            
            return AttachmentResult.SuccessResult(attachmentInfo);
        }
        catch (Exception ex)
        {
            // Clean up temp file if exists
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
            return AttachmentResult.FailureResult($"Failed to store attachment: {ex.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<Attachment?> GetAsync(
        string collectionName, 
        string documentId, 
        string name,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        var info = await GetInfoAsync(collectionName, documentId, name, cancellationToken);
        if (info == null)
            return null;
        
        var attachmentPath = GetAttachmentPath(collectionName, documentId, name);
        
        if (!File.Exists(attachmentPath))
            return null;
        
        var content = await File.ReadAllBytesAsync(attachmentPath, cancellationToken);
        
        // Verify integrity
        var actualHash = ComputeHash(content);
        if (actualHash != info.Hash)
        {
            throw new AttachmentStoreException($"Attachment integrity check failed for '{name}'");
        }
        
        return new Attachment
        {
            Name = info.Name,
            ContentType = info.ContentType,
            Size = info.Size,
            Hash = info.Hash,
            CreatedAt = info.CreatedAt,
            UpdatedAt = info.UpdatedAt,
            Metadata = info.Metadata,
            Content = content
        };
    }

    /// <inheritdoc />
    public Task<AttachmentInfo?> GetInfoAsync(
        string collectionName, 
        string documentId, 
        string name,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        var metadataPath = Path.Combine(GetDocumentPath(collectionName, documentId), _metadataFileName);
        
        if (!File.Exists(metadataPath))
            return Task.FromResult<AttachmentInfo?>(null);
        
        return GetInfoInternalAsync(metadataPath, name, cancellationToken);
    }

    private async Task<AttachmentInfo?> GetInfoInternalAsync(string metadataPath, string name, CancellationToken cancellationToken)
    {
        var attachments = await LoadMetadataAsync(metadataPath, cancellationToken);
        
        if (attachments.TryGetValue(name, out var info))
            return info;
        
        return null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AttachmentInfo>> ListAsync(
        string collectionName, 
        string documentId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        var metadataPath = Path.Combine(GetDocumentPath(collectionName, documentId), _metadataFileName);
        
        if (!File.Exists(metadataPath))
            return new List<AttachmentInfo>();
        
        var attachments = await LoadMetadataAsync(metadataPath, cancellationToken);
        return attachments.Values.ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(
        string collectionName, 
        string documentId, 
        string name,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        var docPath = GetDocumentPath(collectionName, documentId);
        var attachmentPath = Path.Combine(docPath, SanitizeFileName(name));
        var metadataPath = Path.Combine(docPath, _metadataFileName);
        
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var deleted = false;
            
            // Delete file
            if (File.Exists(attachmentPath))
            {
                File.Delete(attachmentPath);
                deleted = true;
            }
            
            // Update metadata
            if (File.Exists(metadataPath))
            {
                var attachments = await LoadMetadataAsync(metadataPath, cancellationToken);
                if (attachments.Remove(name))
                {
                    if (attachments.Count == 0)
                    {
                        File.Delete(metadataPath);
                    }
                    else
                    {
                        await SaveMetadataAsync(metadataPath, attachments, cancellationToken);
                    }
                    deleted = true;
                }
            }
            
            // Clean up empty document directory
            if (Directory.Exists(docPath) && !Directory.EnumerateFileSystemEntries(docPath).Any())
            {
                Directory.Delete(docPath);
            }
            
            return deleted;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<int> DeleteAllAsync(
        string collectionName, 
        string documentId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        var docPath = GetDocumentPath(collectionName, documentId);
        
        if (!Directory.Exists(docPath))
            return 0;
        
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var metadataPath = Path.Combine(docPath, _metadataFileName);
            var count = 0;
            
            // Load metadata to get attachment names
            if (File.Exists(metadataPath))
            {
                var attachments = await LoadMetadataAsync(metadataPath, cancellationToken);
                
                // Delete all attachment files
                foreach (var name in attachments.Keys)
                {
                    var attachmentPath = Path.Combine(docPath, SanitizeFileName(name));
                    if (File.Exists(attachmentPath))
                    {
                        File.Delete(attachmentPath);
                        count++;
                    }
                }
                
                // Delete metadata file
                File.Delete(metadataPath);
            }
            
            // Delete any remaining files and directory
            if (Directory.Exists(docPath))
            {
                Directory.Delete(docPath, true);
            }
            
            return count;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(
        string collectionName, 
        string documentId, 
        string name,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        var attachmentPath = GetAttachmentPath(collectionName, documentId, name);
        return Task.FromResult(File.Exists(attachmentPath));
    }

    /// <inheritdoc />
    public Task<long> GetTotalStorageSizeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        if (!Directory.Exists(_options.BasePath))
            return Task.FromResult(0L);
        
        long size = 0;
        foreach (var file in Directory.EnumerateFiles(_options.BasePath, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(_metadataFileName) || file.EndsWith(".tmp"))
                continue;
            
            try
            {
                size += new FileInfo(file).Length;
            }
            catch (FileNotFoundException)
            {
                // File was deleted between enumeration and access
            }
        }
        
        return Task.FromResult(size);
    }

    #region Helper Methods

    private string GetDocumentPath(string collectionName, string documentId)
    {
        return Path.Combine(_options.BasePath, SanitizeFileName(collectionName), SanitizeFileName(documentId));
    }

    private string GetAttachmentPath(string collectionName, string documentId, string attachmentName)
    {
        return Path.Combine(GetDocumentPath(collectionName, documentId), SanitizeFileName(attachmentName));
    }

    private static string SanitizeFileName(string name)
    {
        // Replace invalid characters with underscores
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        
        // Prevent directory traversal
        sanitized = sanitized.Replace("..", "__");
        
        // Limit length
        if (sanitized.Length > 200)
            sanitized = sanitized.Substring(0, 200);
        
        return sanitized;
    }

    private static string ComputeHash(byte[] content)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(content);
        return Convert.ToHexString(hash);
    }

    private static void ValidateInputs(string collectionName, string documentId, string name, string contentType)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));
        
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Document ID cannot be empty", nameof(documentId));
        
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Attachment name cannot be empty", nameof(name));
        
        if (string.IsNullOrWhiteSpace(contentType))
            throw new ArgumentException("Content type cannot be empty", nameof(contentType));
    }

    private bool IsContentTypeAllowed(string contentType)
    {
        // Check blocked types
        if (_options.BlockedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
            return false;
        
        // Check allowed types (if specified)
        if (_options.AllowedContentTypes?.Count > 0)
        {
            return _options.AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase);
        }
        
        return true;
    }

    private async Task<Dictionary<string, AttachmentInfo>> LoadMetadataAsync(string metadataPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(metadataPath))
            return new Dictionary<string, AttachmentInfo>();
        
        try
        {
            var json = await File.ReadAllTextAsync(metadataPath, cancellationToken);
            var result = JsonSerializer.Deserialize<Dictionary<string, AttachmentInfo>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return result ?? new Dictionary<string, AttachmentInfo>();
        }
        catch
        {
            return new Dictionary<string, AttachmentInfo>();
        }
    }

    private async Task SaveMetadataAsync(string metadataPath, Dictionary<string, AttachmentInfo> attachments, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(attachments, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        
        // Atomic write
        var tempPath = metadataPath + ".tmp";
        await File.WriteAllTextAsync(tempPath, json, cancellationToken);
        
        if (File.Exists(metadataPath))
        {
            File.Delete(metadataPath);
        }
        File.Move(tempPath, metadataPath);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AttachmentStore));
    }

    #endregion

    #region IDisposable

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            _lock.Dispose();
            _disposed = true;
        }
    }

    #endregion
}
