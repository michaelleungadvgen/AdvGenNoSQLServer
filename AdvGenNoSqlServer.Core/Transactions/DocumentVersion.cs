// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Core.Transactions;

/// <summary>
/// Represents a version of a document in the MVCC system.
/// </summary>
public sealed class DocumentVersion
{
    public Guid VersionId { get; }
    public Document Document { get; }
    public string CreatedByTransactionId { get; }
    public DateTime CreatedAt { get; }
    public DateTime? DeletedAt { get; private set; }
    public string? DeletedByTransactionId { get; private set; }
    public bool IsDeleted => DeletedAt.HasValue;
    public DocumentVersion? PreviousVersion { get; }

    public DocumentVersion(Document document, string transactionId, DocumentVersion? previousVersion = null)
    {
        VersionId = Guid.NewGuid();
        Document = document ?? throw new ArgumentNullException(nameof(document));
        CreatedByTransactionId = transactionId ?? throw new ArgumentNullException(nameof(transactionId));
        CreatedAt = new DateTime(MvccTimestamp.Next());
        PreviousVersion = previousVersion;
    }

    public void MarkDeleted(string transactionId)
    {
        if (IsDeleted)
            throw new InvalidOperationException("Version is already deleted");
        DeletedAt = new DateTime(MvccTimestamp.Next());
        DeletedByTransactionId = transactionId ?? throw new ArgumentNullException(nameof(transactionId));
    }

    public bool IsVisibleTo(long readTimestamp, string currentTransactionId)
    {
        if (CreatedByTransactionId == currentTransactionId)
            return true;
        if (CreatedAt.Ticks > readTimestamp)
            return false;
        if (DeletedAt.HasValue && DeletedAt.Value.Ticks <= readTimestamp)
            return false;
        return true;
    }
}

/// <summary>
/// Represents a chain of document versions for MVCC.
/// </summary>
public sealed class VersionChain
{
    private readonly object _lock = new();
    private DocumentVersion? _latestVersion;

    public string DocumentId { get; }
    public string CollectionName { get; }

    public DocumentVersion? LatestVersion
    {
        get { lock (_lock) { return _latestVersion; } }
    }

    public int VersionCount { get; private set; }

    public VersionChain(string collectionName, string documentId)
    {
        CollectionName = collectionName ?? throw new ArgumentNullException(nameof(collectionName));
        DocumentId = documentId ?? throw new ArgumentNullException(nameof(documentId));
        VersionCount = 0;
    }

    public void AddVersion(DocumentVersion version)
    {
        lock (_lock)
        {
            if (_latestVersion != null && version.PreviousVersion != _latestVersion)
            {
                throw new ArgumentException("New version must link to the latest version");
            }
            _latestVersion = version;
            VersionCount++;
        }
    }

    public DocumentVersion? GetVisibleVersion(long readTimestamp, string transactionId)
    {
        lock (_lock)
        {
            var current = _latestVersion;
            while (current != null)
            {
                if (current.IsVisibleTo(readTimestamp, transactionId))
                    return current;
                current = current.PreviousVersion;
            }
            return null;
        }
    }

    public bool MarkLatestDeleted(string transactionId)
    {
        lock (_lock)
        {
            if (_latestVersion == null)
                return false;
            _latestVersion.MarkDeleted(transactionId);
            return true;
        }
    }

    public IReadOnlyList<DocumentVersion> GetAllVersions()
    {
        lock (_lock)
        {
            var versions = new List<DocumentVersion>();
            var current = _latestVersion;
            while (current != null)
            {
                versions.Add(current);
                current = current.PreviousVersion;
            }
            return versions;
        }
    }
}

/// <summary>
/// Global timestamp generator for MVCC.
/// </summary>
public static class MvccTimestamp
{
    private static long _currentTimestamp = DateTime.UtcNow.Ticks;

    public static long Next()
    {
        return Interlocked.Increment(ref _currentTimestamp);
    }

    public static long Current()
    {
        return Interlocked.Read(ref _currentTimestamp);
    }
}
