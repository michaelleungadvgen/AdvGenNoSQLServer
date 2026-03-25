// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Storage.Revisions
{
    /// <summary>
    /// Interface for managing document revisions.
    /// </summary>
    public interface IRevisionManager
    {
        /// <summary>
        /// Gets the current options.
        /// </summary>
        RevisionOptions Options { get; }

        /// <summary>
        /// Gets the current statistics.
        /// </summary>
        RevisionStatistics Statistics { get; }

        /// <summary>
        /// Event raised when a revision is created.
        /// </summary>
        event EventHandler<RevisionEventArgs>? RevisionCreated;

        /// <summary>
        /// Event raised when a revision is removed.
        /// </summary>
        event EventHandler<RevisionEventArgs>? RevisionRemoved;

        /// <summary>
        /// Creates a revision for a document.
        /// </summary>
        Task<DocumentRevision> CreateRevisionAsync(
            string collectionName,
            string documentId,
            Document document,
            RevisionTrigger trigger,
            string? modifiedBy = null,
            string? changeReason = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a specific revision by version number.
        /// </summary>
        Task<DocumentRevision?> GetRevisionAsync(
            string collectionName,
            string documentId,
            int version,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets all revisions for a document.
        /// </summary>
        Task<IReadOnlyList<DocumentRevision>> GetAllRevisionsAsync(
            string collectionName,
            string documentId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the latest revision for a document.
        /// </summary>
        Task<DocumentRevision?> GetLatestRevisionAsync(
            string collectionName,
            string documentId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets revisions for a document within a time range.
        /// </summary>
        Task<IReadOnlyList<DocumentRevision>> GetRevisionsInRangeAsync(
            string collectionName,
            string documentId,
            DateTime from,
            DateTime to,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Restores a document to a specific revision.
        /// </summary>
        Task<Document> RestoreRevisionAsync(
            string collectionName,
            string documentId,
            int version,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes all revisions for a document.
        /// </summary>
        Task<bool> DeleteRevisionsAsync(
            string collectionName,
            string documentId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Cleans up old revisions according to retention policy.
        /// </summary>
        Task<CleanupResult> CleanupAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the next version number for a document.
        /// </summary>
        Task<int> GetNextVersionAsync(
            string collectionName,
            string documentId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates the revision options.
        /// </summary>
        void UpdateOptions(RevisionOptions options);

        /// <summary>
        /// Checks if revision tracking is enabled for a collection.
        /// </summary>
        bool IsEnabledForCollection(string collectionName);

        /// <summary>
        /// Enables revision tracking for a collection.
        /// </summary>
        void EnableForCollection(string collectionName);

        /// <summary>
        /// Disables revision tracking for a collection.
        /// </summary>
        void DisableForCollection(string collectionName);
    }

    /// <summary>
    /// Result of a cleanup operation.
    /// </summary>
    public class CleanupResult
    {
        /// <summary>
        /// Gets the number of revisions removed.
        /// </summary>
        public int RevisionsRemoved { get; }

        /// <summary>
        /// Gets the number of documents affected.
        /// </summary>
        public int DocumentsAffected { get; }

        /// <summary>
        /// Gets the duration of the cleanup.
        /// </summary>
        public TimeSpan Duration { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="CleanupResult"/> class.
        /// </summary>
        public CleanupResult(int revisionsRemoved, int documentsAffected, TimeSpan duration)
        {
            RevisionsRemoved = revisionsRemoved;
            DocumentsAffected = documentsAffected;
            Duration = duration;
        }

        /// <summary>
        /// Creates a successful cleanup result.
        /// </summary>
        public static CleanupResult Success(int revisionsRemoved, int documentsAffected, TimeSpan duration)
            => new(revisionsRemoved, documentsAffected, duration);

        /// <summary>
        /// Creates an empty cleanup result.
        /// </summary>
        public static CleanupResult Empty() => new(0, 0, TimeSpan.Zero);
    }
}
