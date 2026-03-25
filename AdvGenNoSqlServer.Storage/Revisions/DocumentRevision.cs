// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Text.Json;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Storage.Revisions
{
    /// <summary>
    /// Represents a single revision of a document at a point in time.
    /// </summary>
    public class DocumentRevision
    {
        /// <summary>
        /// Gets the unique identifier for this revision.
        /// </summary>
        public string RevisionId { get; }

        /// <summary>
        /// Gets the document ID this revision belongs to.
        /// </summary>
        public string DocumentId { get; }

        /// <summary>
        /// Gets the collection name.
        /// </summary>
        public string CollectionName { get; }

        /// <summary>
        /// Gets the version number of this revision (starts at 1).
        /// </summary>
        public int Version { get; }

        /// <summary>
        /// Gets the timestamp when this revision was created.
        /// </summary>
        public DateTime CreatedAt { get; }

        /// <summary>
        /// Gets the document data at this revision.
        /// </summary>
        public Document Document { get; }

        /// <summary>
        /// Gets the ID of the user who made this change (if available).
        /// </summary>
        public string? ModifiedBy { get; }

        /// <summary>
        /// Gets the reason for this change (if provided).
        /// </summary>
        public string? ChangeReason { get; }

        /// <summary>
        /// Gets a value indicating whether this is the first revision (insert).
        /// </summary>
        public bool IsInitialRevision => Version == 1;

        /// <summary>
        /// Initializes a new instance of the <see cref="DocumentRevision"/> class.
        /// </summary>
        public DocumentRevision(
            string revisionId,
            string documentId,
            string collectionName,
            int version,
            DateTime createdAt,
            Document document,
            string? modifiedBy = null,
            string? changeReason = null)
        {
            RevisionId = revisionId ?? throw new ArgumentNullException(nameof(revisionId));
            DocumentId = documentId ?? throw new ArgumentNullException(nameof(documentId));
            CollectionName = collectionName ?? throw new ArgumentNullException(nameof(collectionName));
            Version = version > 0 ? version : throw new ArgumentException("Version must be greater than 0", nameof(version));
            CreatedAt = createdAt;
            Document = document ?? throw new ArgumentNullException(nameof(document));
            ModifiedBy = modifiedBy;
            ChangeReason = changeReason;
        }

        /// <summary>
        /// Creates a new revision for a document.
        /// </summary>
        public static DocumentRevision Create(
            string documentId,
            string collectionName,
            int version,
            Document document,
            string? modifiedBy = null,
            string? changeReason = null)
        {
            var revisionId = $"{collectionName}:{documentId}:{version}";
            return new DocumentRevision(
                revisionId,
                documentId,
                collectionName,
                version,
                DateTime.UtcNow,
                document,
                modifiedBy,
                changeReason);
        }

        /// <summary>
        /// Creates a copy of this revision with updated metadata.
        /// </summary>
        public DocumentRevision WithMetadata(string? modifiedBy = null, string? changeReason = null)
        {
            return new DocumentRevision(
                RevisionId,
                DocumentId,
                CollectionName,
                Version,
                CreatedAt,
                Document,
                modifiedBy ?? ModifiedBy,
                changeReason ?? ChangeReason);
        }

        /// <summary>
        /// Returns a string representation of this revision.
        /// </summary>
        public override string ToString()
        {
            return $"Revision {Version} of {CollectionName}:{DocumentId} at {CreatedAt:O}";
        }
    }

    /// <summary>
    /// Options for configuring document revision tracking.
    /// </summary>
    public class RevisionOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether revision tracking is enabled.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Gets or sets the maximum number of revisions to keep per document (0 = unlimited).
        /// </summary>
        public int MaxRevisionsPerDocument { get; set; } = 10;

        /// <summary>
        /// Gets or sets the maximum age of revisions to keep (null = unlimited).
        /// </summary>
        public TimeSpan? MaxRevisionAge { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to create revisions on insert.
        /// </summary>
        public bool CreateRevisionOnInsert { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether to create revisions on update.
        /// </summary>
        public bool CreateRevisionOnUpdate { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether to create revisions on delete.
        /// </summary>
        public bool CreateRevisionOnDelete { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether to skip revisions when no changes detected.
        /// </summary>
        public bool SkipUnchangedRevisions { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether to enable automatic cleanup.
        /// </summary>
        public bool EnableAutomaticCleanup { get; set; } = true;

        /// <summary>
        /// Gets or sets the interval for automatic cleanup.
        /// </summary>
        public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);

        /// <summary>
        /// Validates the options.
        /// </summary>
        public void Validate()
        {
            if (MaxRevisionsPerDocument < 0)
                throw new ArgumentException("MaxRevisionsPerDocument cannot be negative");

            if (CleanupInterval < TimeSpan.FromMinutes(1))
                throw new ArgumentException("CleanupInterval must be at least 1 minute");
        }

        /// <summary>
        /// Creates a copy of these options.
        /// </summary>
        public RevisionOptions Clone()
        {
            return new RevisionOptions
            {
                Enabled = Enabled,
                MaxRevisionsPerDocument = MaxRevisionsPerDocument,
                MaxRevisionAge = MaxRevisionAge,
                CreateRevisionOnInsert = CreateRevisionOnInsert,
                CreateRevisionOnUpdate = CreateRevisionOnUpdate,
                CreateRevisionOnDelete = CreateRevisionOnDelete,
                SkipUnchangedRevisions = SkipUnchangedRevisions,
                EnableAutomaticCleanup = EnableAutomaticCleanup,
                CleanupInterval = CleanupInterval
            };
        }

        /// <summary>
        /// Creates default options.
        /// </summary>
        public static RevisionOptions Default => new();

        /// <summary>
        /// Creates options with unlimited revisions.
        /// </summary>
        public static RevisionOptions Unlimited => new() { MaxRevisionsPerDocument = 0 };

        /// <summary>
        /// Creates options with revisions disabled.
        /// </summary>
        public static RevisionOptions Disabled => new() { Enabled = false };
    }

    /// <summary>
    /// Statistics for revision tracking.
    /// </summary>
    public class RevisionStatistics
    {
        /// <summary>
        /// Gets the total number of revisions stored.
        /// </summary>
        public long TotalRevisions { get; set; }

        /// <summary>
        /// Gets the number of documents with revisions.
        /// </summary>
        public long DocumentsWithRevisions { get; set; }

        /// <summary>
        /// Gets the number of revisions created.
        /// </summary>
        public long RevisionsCreated { get; set; }

        /// <summary>
        /// Gets the number of revisions removed.
        /// </summary>
        public long RevisionsRemoved { get; set; }

        /// <summary>
        /// Gets the number of revision retrievals.
        /// </summary>
        public long RevisionsRetrieved { get; set; }

        /// <summary>
        /// Gets the number of documents restored from revisions.
        /// </summary>
        public long DocumentsRestored { get; set; }

        /// <summary>
        /// Gets the timestamp of the last cleanup.
        /// </summary>
        public DateTime? LastCleanupAt { get; set; }

        /// <summary>
        /// Gets the total storage size used by revisions (in bytes, estimated).
        /// </summary>
        public long EstimatedStorageSize { get; set; }

        /// <summary>
        /// Creates a snapshot of these statistics.
        /// </summary>
        public RevisionStatistics Snapshot()
        {
            return new RevisionStatistics
            {
                TotalRevisions = TotalRevisions,
                DocumentsWithRevisions = DocumentsWithRevisions,
                RevisionsCreated = RevisionsCreated,
                RevisionsRemoved = RevisionsRemoved,
                RevisionsRetrieved = RevisionsRetrieved,
                DocumentsRestored = DocumentsRestored,
                LastCleanupAt = LastCleanupAt,
                EstimatedStorageSize = EstimatedStorageSize
            };
        }
    }

    /// <summary>
    /// Event arguments for revision events.
    /// </summary>
    public class RevisionEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the revision that was created.
        /// </summary>
        public DocumentRevision Revision { get; }

        /// <summary>
        /// Gets the type of operation that triggered the revision.
        /// </summary>
        public RevisionTrigger Operation { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="RevisionEventArgs"/> class.
        /// </summary>
        public RevisionEventArgs(DocumentRevision revision, RevisionTrigger operation)
        {
            Revision = revision ?? throw new ArgumentNullException(nameof(revision));
            Operation = operation;
        }
    }

    /// <summary>
    /// Types of operations that can trigger revision creation.
    /// </summary>
    public enum RevisionTrigger
    {
        /// <summary>
        /// Document was inserted.
        /// </summary>
        Insert,

        /// <summary>
        /// Document was updated.
        /// </summary>
        Update,

        /// <summary>
        /// Document was deleted.
        /// </summary>
        Delete,

        /// <summary>
        /// Revision was created manually.
        /// </summary>
        Manual
    }

    /// <summary>
    /// Result of a document comparison.
    /// </summary>
    public class DocumentComparisonResult
    {
        /// <summary>
        /// Gets a value indicating whether the documents are equal.
        /// </summary>
        public bool AreEqual { get; }

        /// <summary>
        /// Gets the list of changed field paths.
        /// </summary>
        public IReadOnlyList<string> ChangedFields { get; }

        /// <summary>
        /// Gets the list of added field paths.
        /// </summary>
        public IReadOnlyList<string> AddedFields { get; }

        /// <summary>
        /// Gets the list of removed field paths.
        /// </summary>
        public IReadOnlyList<string> RemovedFields { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DocumentComparisonResult"/> class.
        /// </summary>
        public DocumentComparisonResult(
            bool areEqual,
            IReadOnlyList<string>? changedFields = null,
            IReadOnlyList<string>? addedFields = null,
            IReadOnlyList<string>? removedFields = null)
        {
            AreEqual = areEqual;
            ChangedFields = changedFields ?? Array.Empty<string>();
            AddedFields = addedFields ?? Array.Empty<string>();
            RemovedFields = removedFields ?? Array.Empty<string>();
        }

        /// <summary>
        /// Creates a result indicating documents are equal.
        /// </summary>
        public static DocumentComparisonResult Equal() => new(true);

        /// <summary>
        /// Creates a result indicating documents are different.
        /// </summary>
        public static DocumentComparisonResult Different(
            IReadOnlyList<string>? changedFields = null,
            IReadOnlyList<string>? addedFields = null,
            IReadOnlyList<string>? removedFields = null)
            => new(false, changedFields, addedFields, removedFields);
    }
}
