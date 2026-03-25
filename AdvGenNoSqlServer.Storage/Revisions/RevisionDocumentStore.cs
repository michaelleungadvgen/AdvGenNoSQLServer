// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Core.Abstractions;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Storage.Revisions
{
    /// <summary>
    /// Document store wrapper that adds transparent revision tracking.
    /// </summary>
    public class RevisionDocumentStore : IDocumentStore, IDisposable
    {
        private readonly IDocumentStore _innerStore;
        private readonly IRevisionManager _revisionManager;
        private readonly IDocumentComparer _documentComparer;
        private readonly RevisionOptions _options;
        private bool _disposed;

        /// <summary>
        /// Gets the inner document store.
        /// </summary>
        public IDocumentStore InnerStore => _innerStore;

        /// <summary>
        /// Gets the revision manager.
        /// </summary>
        public IRevisionManager RevisionManager => _revisionManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="RevisionDocumentStore"/> class.
        /// </summary>
        public RevisionDocumentStore(
            IDocumentStore innerStore,
            IRevisionManager? revisionManager = null,
            RevisionOptions? options = null,
            IDocumentComparer? documentComparer = null)
        {
            _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
            _revisionManager = revisionManager ?? new RevisionManager(options);
            _options = options ?? RevisionOptions.Default;
            _documentComparer = documentComparer ?? new DocumentComparer();
        }

        /// <summary>
        /// Inserts a document and creates a revision if enabled.
        /// </summary>
        public async Task<Document> InsertAsync(
            string collection,
            Document document,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            // Insert into underlying store
            var result = await _innerStore.InsertAsync(collection, document, cancellationToken);

            // Create revision if enabled
            if (ShouldCreateRevision(collection, RevisionTrigger.Insert))
            {
                try
                {
                    await _revisionManager.CreateRevisionAsync(
                        collection,
                        document.Id,
                        document,
                        RevisionTrigger.Insert,
                        cancellationToken: cancellationToken);
                }
                catch
                {
                    // Don't fail the insert if revision creation fails
                }
            }

            return result;
        }

        /// <summary>
        /// Gets a document by ID.
        /// </summary>
        public Task<Document?> GetAsync(
            string collection,
            string id,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return _innerStore.GetAsync(collection, id, cancellationToken);
        }

        /// <summary>
        /// Gets multiple documents by ID.
        /// </summary>
        public Task<IEnumerable<Document>> GetManyAsync(
            string collection,
            IEnumerable<string> ids,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return _innerStore.GetManyAsync(collection, ids, cancellationToken);
        }

        /// <summary>
        /// Gets all documents in a collection.
        /// </summary>
        public Task<IEnumerable<Document>> GetAllAsync(
            string collection,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return _innerStore.GetAllAsync(collection, cancellationToken);
        }

        /// <summary>
        /// Updates a document and creates a revision if enabled.
        /// </summary>
        public async Task<Document> UpdateAsync(
            string collection,
            Document document,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            // Get the existing document BEFORE updating (for comparison and revision)
            Document? existingDocument = null;
            if (ShouldCreateRevision(collection, RevisionTrigger.Update))
            {
                existingDocument = await _innerStore.GetAsync(collection, document.Id, cancellationToken);
            }

            // Update in underlying store
            var result = await _innerStore.UpdateAsync(collection, document, cancellationToken);

            // Create revision if enabled
            if (ShouldCreateRevision(collection, RevisionTrigger.Update))
            {
                try
                {
                    // Check if document actually changed
                    if (_options.SkipUnchangedRevisions && existingDocument != null)
                    {
                        if (_documentComparer.AreEqual(existingDocument, document))
                        {
                            return result; // No changes, skip revision
                        }
                    }

                    // Create revision from the existing document (the state before update)
                    // This captures the previous state, not the new state
                    if (existingDocument != null)
                    {
                        await _revisionManager.CreateRevisionAsync(
                            collection,
                            document.Id,
                            existingDocument,
                            RevisionTrigger.Update,
                            cancellationToken: cancellationToken);
                    }
                }
                catch
                {
                    // Don't fail the update if revision creation fails
                }
            }

            return result;
        }

        /// <summary>
        /// Deletes a document and optionally creates a revision.
        /// </summary>
        public async Task<bool> DeleteAsync(
            string collection,
            string id,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            // Get the document before deletion if we need to create a revision
            Document? document = null;
            if (ShouldCreateRevision(collection, RevisionTrigger.Delete))
            {
                document = await _innerStore.GetAsync(collection, id, cancellationToken);
            }

            // Delete from underlying store
            var result = await _innerStore.DeleteAsync(collection, id, cancellationToken);

            // Create revision if enabled and delete succeeded
            if (result && document != null && ShouldCreateRevision(collection, RevisionTrigger.Delete))
            {
                try
                {
                    await _revisionManager.CreateRevisionAsync(
                        collection,
                        id,
                        document,
                        RevisionTrigger.Delete,
                        cancellationToken: cancellationToken);
                }
                catch
                {
                    // Don't fail the delete if revision creation fails
                }
            }

            return result;
        }

        /// <summary>
        /// Checks if a document exists.
        /// </summary>
        public Task<bool> ExistsAsync(
            string collection,
            string id,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return _innerStore.ExistsAsync(collection, id, cancellationToken);
        }

        /// <summary>
        /// Counts documents in a collection.
        /// </summary>
        public Task<long> CountAsync(
            string collection,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return _innerStore.CountAsync(collection, cancellationToken);
        }

        /// <summary>
        /// Creates a new collection.
        /// </summary>
        public async Task CreateCollectionAsync(
            string collection,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await _innerStore.CreateCollectionAsync(collection, cancellationToken);

            // Enable revision tracking for new collections by default
            _revisionManager.EnableForCollection(collection);
        }

        /// <summary>
        /// Drops a collection.
        /// </summary>
        public async Task<bool> DropCollectionAsync(
            string collection,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            // Clean up all revisions for this collection by getting all documents with revisions
            // and deleting their revisions
            var allDocs = await _innerStore.GetAllAsync(collection, cancellationToken);
            foreach (var doc in allDocs)
            {
                await _revisionManager.DeleteRevisionsAsync(collection, doc.Id, cancellationToken);
            }

            return await _innerStore.DropCollectionAsync(collection, cancellationToken);
        }

        /// <summary>
        /// Gets all collection names.
        /// </summary>
        public Task<IEnumerable<string>> GetCollectionsAsync(
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return _innerStore.GetCollectionsAsync(cancellationToken);
        }

        /// <summary>
        /// Clears all documents from a collection.
        /// </summary>
        public async Task ClearCollectionAsync(
            string collection,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            // Clean up revisions for all documents in this collection
            var allDocs = await _innerStore.GetAllAsync(collection, cancellationToken);
            foreach (var doc in allDocs)
            {
                await _revisionManager.DeleteRevisionsAsync(collection, doc.Id, cancellationToken);
            }

            await _innerStore.ClearCollectionAsync(collection, cancellationToken);
        }

        /// <summary>
        /// Restores a document to a specific revision.
        /// </summary>
        public async Task<Document> RestoreRevisionAsync(
            string collection,
            string documentId,
            int version,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            // Get the revision
            var revision = await _revisionManager.GetRevisionAsync(collection, documentId, version, cancellationToken);
            if (revision == null)
                throw new InvalidOperationException($"Revision {version} not found");

            // Restore the document
            var restoredDocument = await _revisionManager.RestoreRevisionAsync(collection, documentId, version, cancellationToken);

            // Update the document in the store
            if (await _innerStore.ExistsAsync(collection, documentId, cancellationToken))
            {
                await _innerStore.UpdateAsync(collection, restoredDocument, cancellationToken);
            }
            else
            {
                await _innerStore.InsertAsync(collection, restoredDocument, cancellationToken);
            }

            return restoredDocument;
        }

        /// <summary>
        /// Gets all revisions for a document.
        /// </summary>
        public Task<IReadOnlyList<DocumentRevision>> GetRevisionsAsync(
            string collection,
            string documentId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return _revisionManager.GetAllRevisionsAsync(collection, documentId, cancellationToken);
        }

        /// <summary>
        /// Gets the revision history statistics for a collection.
        /// </summary>
        public async Task<RevisionHistoryStats> GetRevisionHistoryStatsAsync(
            string collection,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            var stats = new RevisionHistoryStats();
            var allDocs = await _innerStore.GetAllAsync(collection, cancellationToken);
            
            foreach (var doc in allDocs)
            {
                var revisions = await _revisionManager.GetAllRevisionsAsync(collection, doc.Id, cancellationToken);
                if (revisions.Count > 0)
                {
                    stats.TotalDocumentsWithRevisions++;
                    stats.TotalRevisions += revisions.Count;
                }
            }

            if (stats.TotalDocumentsWithRevisions > 0)
            {
                stats.AverageRevisionsPerDocument = (double)stats.TotalRevisions / stats.TotalDocumentsWithRevisions;
            }

            return stats;
        }

        /// <summary>
        /// Disposes resources.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            if (_revisionManager is IDisposable disposable)
            {
                disposable.Dispose();
            }

            if (_innerStore is IDisposable innerDisposable)
            {
                innerDisposable.Dispose();
            }

            _disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RevisionDocumentStore));
        }

        private bool ShouldCreateRevision(string collection, RevisionTrigger trigger)
        {
            if (!_options.Enabled)
                return false;

            return _revisionManager.IsEnabledForCollection(collection);
        }
    }

    /// <summary>
    /// Statistics for revision history.
    /// </summary>
    public class RevisionHistoryStats
    {
        /// <summary>
        /// Gets or sets the total number of documents with revisions.
        /// </summary>
        public int TotalDocumentsWithRevisions { get; set; }

        /// <summary>
        /// Gets or sets the total number of revisions.
        /// </summary>
        public int TotalRevisions { get; set; }

        /// <summary>
        /// Gets or sets the average number of revisions per document.
        /// </summary>
        public double AverageRevisionsPerDocument { get; set; }
    }

    /// <summary>
    /// Extension methods for adding revision support to document stores.
    /// </summary>
    public static class RevisionDocumentStoreExtensions
    {
        /// <summary>
        /// Wraps a document store with revision tracking.
        /// </summary>
        public static RevisionDocumentStore WithRevisions(
            this IDocumentStore store,
            RevisionOptions? options = null)
        {
            return new RevisionDocumentStore(store, options: options);
        }

        /// <summary>
        /// Wraps a document store with revision tracking using an existing revision manager.
        /// </summary>
        public static RevisionDocumentStore WithRevisions(
            this IDocumentStore store,
            IRevisionManager revisionManager)
        {
            return new RevisionDocumentStore(store, revisionManager);
        }
    }
}
