// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Generic;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Core.Clustering
{
    /// <summary>
    /// Defines strategies for resolving conflicts between documents in a distributed cluster.
    /// </summary>
    public enum ConflictResolutionStrategy
    {
        /// <summary>
        /// Use the document with the most recent timestamp (default strategy).
        /// </summary>
        LastWriteWins,

        /// <summary>
        /// Keep the original document, reject the incoming change.
        /// </summary>
        FirstWriteWins,

        /// <summary>
        /// Use the document with the highest version number.
        /// </summary>
        HighestVersion,

        /// <summary>
        /// Merge non-conflicting fields from both documents.
        /// </summary>
        MergeFields,

        /// <summary>
        /// Use a custom resolver implementation.
        /// </summary>
        Custom
    }

    /// <summary>
    /// Represents the context in which a conflict is being resolved.
    /// </summary>
    public class ConflictContext
    {
        /// <summary>
        /// The name of the collection containing the conflicting documents.
        /// </summary>
        public required string CollectionName { get; set; }

        /// <summary>
        /// The ID of the conflicting document.
        /// </summary>
        public required string DocumentId { get; set; }

        /// <summary>
        /// The local document (current state on this node).
        /// </summary>
        public required Document LocalDocument { get; set; }

        /// <summary>
        /// The remote document (incoming change from another node).
        /// </summary>
        public required Document RemoteDocument { get; set; }

        /// <summary>
        /// The node ID of the remote document's origin.
        /// </summary>
        public required string RemoteNodeId { get; set; }

        /// <summary>
        /// The local node ID.
        /// </summary>
        public required string LocalNodeId { get; set; }

        /// <summary>
        /// The conflict resolution strategy being used.
        /// </summary>
        public ConflictResolutionStrategy Strategy { get; set; }

        /// <summary>
        /// Additional metadata about the conflict.
        /// </summary>
        public Dictionary<string, object>? Metadata { get; set; }

        /// <summary>
        /// Timestamp when the conflict was detected.
        /// </summary>
        public DateTime ConflictDetectedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Represents the result of a conflict resolution operation.
    /// </summary>
    public class ConflictResult
    {
        /// <summary>
        /// The resolved document (the winner of the conflict).
        /// </summary>
        public required Document ResolvedDocument { get; set; }

        /// <summary>
        /// Indicates whether the local document won the conflict.
        /// </summary>
        public bool LocalWon { get; set; }

        /// <summary>
        /// Indicates whether the remote document won the conflict.
        /// </summary>
        public bool RemoteWon => !LocalWon;

        /// <summary>
        /// The strategy used to resolve the conflict.
        /// </summary>
        public ConflictResolutionStrategy Strategy { get; set; }

        /// <summary>
        /// Indicates whether a merge operation was performed.
        /// </summary>
        public bool WasMerged { get; set; }

        /// <summary>
        /// List of fields that were in conflict (for merge operations).
        /// </summary>
        public List<string> ConflictedFields { get; set; } = new();

        /// <summary>
        /// Human-readable description of the resolution.
        /// </summary>
        public string? ResolutionDescription { get; set; }

        /// <summary>
        /// Timestamp when the conflict was resolved.
        /// </summary>
        public DateTime ResolvedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Creates a success result with the resolved document.
        /// </summary>
        public static ConflictResult Resolved(Document document, bool localWon, ConflictResolutionStrategy strategy, string? description = null)
        {
            return new ConflictResult
            {
                ResolvedDocument = document,
                LocalWon = localWon,
                Strategy = strategy,
                ResolutionDescription = description
            };
        }

        /// <summary>
        /// Creates a result indicating a merge was performed.
        /// </summary>
        public static ConflictResult Merged(Document mergedDocument, List<string> conflictedFields, string? description = null)
        {
            return new ConflictResult
            {
                ResolvedDocument = mergedDocument,
                LocalWon = false,
                Strategy = ConflictResolutionStrategy.MergeFields,
                WasMerged = true,
                ConflictedFields = conflictedFields,
                ResolutionDescription = description ?? $"Merged documents with {conflictedFields.Count} conflicting fields"
            };
        }
    }

    /// <summary>
    /// Interface for resolving conflicts between documents in a distributed cluster.
    /// </summary>
    public interface IConflictResolver
    {
        /// <summary>
        /// Gets the name of this resolver.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets the conflict resolution strategy used by this resolver.
        /// </summary>
        ConflictResolutionStrategy Strategy { get; }

        /// <summary>
        /// Resolves a conflict between two documents.
        /// </summary>
        /// <param name="context">The context containing information about the conflict.</param>
        /// <returns>The result of the conflict resolution.</returns>
        ConflictResult Resolve(ConflictContext context);

        /// <summary>
        /// Determines whether a conflict exists between two documents.
        /// </summary>
        /// <param name="local">The local document.</param>
        /// <param name="remote">The remote document.</param>
        /// <returns>True if a conflict exists; otherwise, false.</returns>
        bool HasConflict(Document local, Document remote);
    }

    /// <summary>
    /// Configuration options for conflict resolution.
    /// </summary>
    public class ConflictResolutionOptions
    {
        /// <summary>
        /// The default conflict resolution strategy.
        /// </summary>
        public ConflictResolutionStrategy DefaultStrategy { get; set; } = ConflictResolutionStrategy.LastWriteWins;

        /// <summary>
        /// Strategy to use per collection (collection name -> strategy).
        /// </summary>
        public Dictionary<string, ConflictResolutionStrategy> CollectionStrategies { get; set; } = new();

        /// <summary>
        /// Whether to enable automatic conflict resolution.
        /// </summary>
        public bool AutoResolve { get; set; } = true;

        /// <summary>
        /// Whether to log all conflicts (even automatically resolved ones).
        /// </summary>
        public bool LogAllConflicts { get; set; } = false;

        /// <summary>
        /// Maximum time difference (in milliseconds) before considering timestamps equal.
        /// </summary>
        public int TimestampEqualityThresholdMs { get; set; } = 10;

        /// <summary>
        /// Whether to preserve conflict history for audit purposes.
        /// </summary>
        public bool PreserveHistory { get; set; } = false;

        /// <summary>
        /// Gets the strategy for a specific collection.
        /// </summary>
        public ConflictResolutionStrategy GetStrategyForCollection(string collectionName)
        {
            return CollectionStrategies.TryGetValue(collectionName, out var strategy)
                ? strategy
                : DefaultStrategy;
        }

        /// <summary>
        /// Sets the strategy for a specific collection.
        /// </summary>
        public void SetStrategyForCollection(string collectionName, ConflictResolutionStrategy strategy)
        {
            CollectionStrategies[collectionName] = strategy;
        }
    }
}
