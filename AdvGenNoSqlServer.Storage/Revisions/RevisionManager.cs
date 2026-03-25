// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Storage.Revisions
{
    /// <summary>
    /// Manages document revisions with thread-safe operations.
    /// </summary>
    public class RevisionManager : IRevisionManager, IDisposable
    {
        // Collection -> DocumentId -> List of Revisions (sorted by version)
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, List<DocumentRevision>>> _revisions;
        private readonly ConcurrentDictionary<string, int> _versionCounters;
        private readonly IDocumentComparer _documentComparer;
        private readonly HashSet<string> _enabledCollections;
        private readonly Timer? _cleanupTimer;
        private readonly object _optionsLock = new();

        // Statistics counters (using fields for Interlocked support)
        private long _revisionsCreated;
        private long _revisionsRemoved;
        private long _revisionsRetrieved;
        private long _documentsRestored;

        private RevisionOptions _options;
        private bool _disposed;

        /// <summary>
        /// Gets the current options.
        /// </summary>
        public RevisionOptions Options
        {
            get
            {
                lock (_optionsLock)
                {
                    return _options.Clone();
                }
            }
        }

        /// <summary>
        /// Gets the current statistics.
        /// </summary>
        public RevisionStatistics Statistics => new()
        {
            TotalRevisions = GetTotalRevisionCount(),
            DocumentsWithRevisions = GetDocumentsWithRevisionsCount(),
            RevisionsCreated = _revisionsCreated,
            RevisionsRemoved = _revisionsRemoved,
            RevisionsRetrieved = _revisionsRetrieved,
            DocumentsRestored = _documentsRestored
        };

        /// <summary>
        /// Event raised when a revision is created.
        /// </summary>
        public event EventHandler<RevisionEventArgs>? RevisionCreated;

        /// <summary>
        /// Event raised when a revision is removed.
        /// </summary>
        public event EventHandler<RevisionEventArgs>? RevisionRemoved;

        /// <summary>
        /// Initializes a new instance of the <see cref="RevisionManager"/> class.
        /// </summary>
        public RevisionManager(RevisionOptions? options = null, IDocumentComparer? documentComparer = null)
        {
            _options = options?.Clone() ?? RevisionOptions.Default;
            _options.Validate();

            _revisions = new ConcurrentDictionary<string, ConcurrentDictionary<string, List<DocumentRevision>>>();
            _versionCounters = new ConcurrentDictionary<string, int>();
            _documentComparer = documentComparer ?? new DocumentComparer();
            _enabledCollections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Setup automatic cleanup if enabled
            if (_options.EnableAutomaticCleanup)
            {
                _cleanupTimer = new Timer(
                    async _ => await RunCleanupAsync(),
                    null,
                    _options.CleanupInterval,
                    _options.CleanupInterval);
            }
        }

        /// <summary>
        /// Creates a revision for a document.
        /// </summary>
        public Task<DocumentRevision> CreateRevisionAsync(
            string collectionName,
            string documentId,
            Document document,
            RevisionTrigger trigger,
            string? modifiedBy = null,
            string? changeReason = null,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (!Options.Enabled)
                throw new InvalidOperationException("Revision tracking is disabled");

            if (!_enabledCollections.Contains(collectionName))
                throw new InvalidOperationException($"Revision tracking is not enabled for collection '{collectionName}'");

            // Check trigger type
            bool shouldCreate = trigger switch
            {
                RevisionTrigger.Insert => Options.CreateRevisionOnInsert,
                RevisionTrigger.Update => Options.CreateRevisionOnUpdate,
                RevisionTrigger.Delete => Options.CreateRevisionOnDelete,
                RevisionTrigger.Manual => true,
                _ => true
            };

            if (!shouldCreate)
                throw new InvalidOperationException($"Revision creation for '{trigger}' is disabled");

            // Get next version
            var versionKey = $"{collectionName}:{documentId}";
            var version = _versionCounters.AddOrUpdate(versionKey, 1, (_, v) => v + 1);

            // Create revision (with deep copy of document)
            var documentCopy = CreateDocumentCopy(document);
            var revision = DocumentRevision.Create(
                documentId,
                collectionName,
                version,
                documentCopy,
                modifiedBy,
                changeReason);

            // Store revision
            var collectionRevisions = _revisions.GetOrAdd(collectionName, _ => new ConcurrentDictionary<string, List<DocumentRevision>>());
            var documentRevisions = collectionRevisions.GetOrAdd(documentId, _ => new List<DocumentRevision>());

            lock (documentRevisions)
            {
                documentRevisions.Add(revision);
                documentRevisions.Sort((a, b) => a.Version.CompareTo(b.Version));
            }

            // Update statistics
            Interlocked.Increment(ref _revisionsCreated);

            // Raise event
            RevisionCreated?.Invoke(this, new RevisionEventArgs(revision, trigger));

            return Task.FromResult(revision);
        }

        /// <summary>
        /// Gets a specific revision by version number.
        /// </summary>
        public Task<DocumentRevision?> GetRevisionAsync(
            string collectionName,
            string documentId,
            int version,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (_revisions.TryGetValue(collectionName, out var collectionRevisions) &&
                collectionRevisions.TryGetValue(documentId, out var documentRevisions))
            {
                lock (documentRevisions)
                {
                    var revision = documentRevisions.FirstOrDefault(r => r.Version == version);
                    if (revision != null)
                    {
                        Interlocked.Increment(ref _revisionsRetrieved);
                        return Task.FromResult<DocumentRevision?>(revision);
                    }
                }
            }

            return Task.FromResult<DocumentRevision?>(null);
        }

        /// <summary>
        /// Gets all revisions for a document.
        /// </summary>
        public Task<IReadOnlyList<DocumentRevision>> GetAllRevisionsAsync(
            string collectionName,
            string documentId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (_revisions.TryGetValue(collectionName, out var collectionRevisions) &&
                collectionRevisions.TryGetValue(documentId, out var documentRevisions))
            {
                lock (documentRevisions)
                {
                    var result = documentRevisions.ToList().AsReadOnly();
                    Interlocked.Add(ref _revisionsRetrieved, result.Count);
                    return Task.FromResult<IReadOnlyList<DocumentRevision>>(result);
                }
            }

            return Task.FromResult<IReadOnlyList<DocumentRevision>>(Array.Empty<DocumentRevision>());
        }

        /// <summary>
        /// Gets the latest revision for a document.
        /// </summary>
        public Task<DocumentRevision?> GetLatestRevisionAsync(
            string collectionName,
            string documentId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (_revisions.TryGetValue(collectionName, out var collectionRevisions) &&
                collectionRevisions.TryGetValue(documentId, out var documentRevisions))
            {
                lock (documentRevisions)
                {
                    var revision = documentRevisions.OrderByDescending(r => r.Version).FirstOrDefault();
                    if (revision != null)
                    {
                        Interlocked.Increment(ref _revisionsRetrieved);
                        return Task.FromResult<DocumentRevision?>(revision);
                    }
                }
            }

            return Task.FromResult<DocumentRevision?>(null);
        }

        /// <summary>
        /// Gets revisions for a document within a time range.
        /// </summary>
        public Task<IReadOnlyList<DocumentRevision>> GetRevisionsInRangeAsync(
            string collectionName,
            string documentId,
            DateTime from,
            DateTime to,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (_revisions.TryGetValue(collectionName, out var collectionRevisions) &&
                collectionRevisions.TryGetValue(documentId, out var documentRevisions))
            {
                lock (documentRevisions)
                {
                    var result = documentRevisions
                        .Where(r => r.CreatedAt >= from && r.CreatedAt <= to)
                        .ToList()
                        .AsReadOnly();

                    Interlocked.Add(ref _revisionsRetrieved, result.Count);
                    return Task.FromResult<IReadOnlyList<DocumentRevision>>(result);
                }
            }

            return Task.FromResult<IReadOnlyList<DocumentRevision>>(Array.Empty<DocumentRevision>());
        }

        /// <summary>
        /// Restores a document to a specific revision.
        /// </summary>
        public async Task<Document> RestoreRevisionAsync(
            string collectionName,
            string documentId,
            int version,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            var revision = await GetRevisionAsync(collectionName, documentId, version, cancellationToken);
            if (revision == null)
                throw new InvalidOperationException($"Revision {version} not found for document '{documentId}' in collection '{collectionName}'");

            // Create a copy of the document from the revision
            var restoredDocument = CreateDocumentCopy(revision.Document);
            restoredDocument.UpdatedAt = DateTime.UtcNow;

            Interlocked.Increment(ref _documentsRestored);

            return restoredDocument;
        }

        /// <summary>
        /// Deletes all revisions for a document.
        /// </summary>
        public Task<bool> DeleteRevisionsAsync(
            string collectionName,
            string documentId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (_revisions.TryGetValue(collectionName, out var collectionRevisions) &&
                collectionRevisions.TryRemove(documentId, out var documentRevisions))
            {
                var count = documentRevisions.Count;
                Interlocked.Add(ref _revisionsRemoved, count);

                // Reset version counter
                _versionCounters.TryRemove($"{collectionName}:{documentId}", out _);

                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }

        /// <summary>
        /// Cleans up old revisions according to retention policy.
        /// </summary>
        public Task<CleanupResult> CleanupAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(RunCleanup());
        }

        private async Task RunCleanupAsync()
        {
            try
            {
                await CleanupAsync();
            }
            catch
            {
                // Silently fail in background cleanup
            }
        }

        private CleanupResult RunCleanup()
        {
            ThrowIfDisposed();

            var startTime = DateTime.UtcNow;
            var removedCount = 0;
            var documentsAffected = 0;

            var maxRevisions = Options.MaxRevisionsPerDocument;
            var maxAge = Options.MaxRevisionAge;
            var cutoffTime = maxAge.HasValue ? DateTime.UtcNow - maxAge.Value : (DateTime?)null;

            foreach (var collection in _revisions)
            {
                foreach (var document in collection.Value)
                {
                    var documentRevisions = document.Value;
                    List<DocumentRevision> toRemove = new();

                    lock (documentRevisions)
                    {
                        // Check max revisions limit
                        if (maxRevisions > 0 && documentRevisions.Count > maxRevisions)
                        {
                            toRemove.AddRange(documentRevisions.Take(documentRevisions.Count - maxRevisions));
                        }

                        // Check max age limit
                        if (cutoffTime.HasValue)
                        {
                            toRemove.AddRange(documentRevisions.Where(r => r.CreatedAt < cutoffTime.Value && !toRemove.Contains(r)));
                        }

                        // Remove revisions
                        foreach (var revision in toRemove)
                        {
                            documentRevisions.Remove(revision);
                            RevisionRemoved?.Invoke(this, new RevisionEventArgs(revision, RevisionTrigger.Manual));
                        }
                    }

                    if (toRemove.Count > 0)
                    {
                        removedCount += toRemove.Count;
                        documentsAffected++;
                    }
                }
            }

            Interlocked.Add(ref _revisionsRemoved, removedCount);

            return CleanupResult.Success(removedCount, documentsAffected, DateTime.UtcNow - startTime);
        }

        /// <summary>
        /// Gets the next version number for a document.
        /// </summary>
        public Task<int> GetNextVersionAsync(
            string collectionName,
            string documentId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            var versionKey = $"{collectionName}:{documentId}";
            var currentVersion = _versionCounters.GetOrAdd(versionKey, 0);
            return Task.FromResult(currentVersion + 1);
        }

        /// <summary>
        /// Updates the revision options.
        /// </summary>
        public void UpdateOptions(RevisionOptions options)
        {
            lock (_optionsLock)
            {
                _options = options.Clone();
                _options.Validate();
            }
        }

        /// <summary>
        /// Checks if revision tracking is enabled for a collection.
        /// </summary>
        public bool IsEnabledForCollection(string collectionName)
        {
            return Options.Enabled && _enabledCollections.Contains(collectionName);
        }

        /// <summary>
        /// Enables revision tracking for a collection.
        /// </summary>
        public void EnableForCollection(string collectionName)
        {
            lock (_enabledCollections)
            {
                _enabledCollections.Add(collectionName);
            }
        }

        /// <summary>
        /// Disables revision tracking for a collection.
        /// </summary>
        public void DisableForCollection(string collectionName)
        {
            lock (_enabledCollections)
            {
                _enabledCollections.Remove(collectionName);
            }
        }

        /// <summary>
        /// Disposes resources.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _cleanupTimer?.Dispose();
            _disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RevisionManager));
        }

        private long GetTotalRevisionCount()
        {
            return _revisions.Values
                .SelectMany(c => c.Values)
                .Sum(d => d.Count);
        }

        private long GetDocumentsWithRevisionsCount()
        {
            return _revisions.Values
                .Sum(c => c.Count);
        }

        private Document CreateDocumentCopy(Document document)
        {
            // Deep copy by serializing and deserializing
            var json = System.Text.Json.JsonSerializer.Serialize(document);
            return System.Text.Json.JsonSerializer.Deserialize<Document>(json)!;
        }
    }
}
