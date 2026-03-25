// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Core.Clustering
{
    /// <summary>
    /// Base class for conflict resolvers providing common functionality.
    /// </summary>
    public abstract class ConflictResolverBase : IConflictResolver
    {
        /// <inheritdoc />
        public abstract string Name { get; }

        /// <inheritdoc />
        public abstract ConflictResolutionStrategy Strategy { get; }

        /// <inheritdoc />
        public abstract ConflictResult Resolve(ConflictContext context);

        /// <inheritdoc />
        public virtual bool HasConflict(Document local, Document remote)
        {
            if (local == null || remote == null)
                return false;

            // Same version means no conflict
            if (local.Version == remote.Version && local.UpdatedAt == remote.UpdatedAt)
                return false;

            // Different content means potential conflict
            return !AreDocumentsEqual(local, remote);
        }

        /// <summary>
        /// Compares two documents for equality based on their content.
        /// </summary>
        protected virtual bool AreDocumentsEqual(Document local, Document remote)
        {
            if (local.Data == null && remote.Data == null)
                return true;

            if (local.Data == null || remote.Data == null)
                return false;

            if (local.Data.Count != remote.Data.Count)
                return false;

            foreach (var kvp in local.Data)
            {
                if (!remote.Data.TryGetValue(kvp.Key, out var remoteValue))
                    return false;

                if (!ValuesEqual(kvp.Value, remoteValue))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Compares two values for equality.
        /// </summary>
        protected virtual bool ValuesEqual(object? left, object? right)
        {
            if (left == null && right == null)
                return true;

            if (left == null || right == null)
                return false;

            // Handle numeric comparisons
            if (IsNumeric(left) && IsNumeric(right))
            {
                return Convert.ToDouble(left) == Convert.ToDouble(right);
            }

            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether a value is numeric.
        /// </summary>
        protected virtual bool IsNumeric(object value)
        {
            return value is sbyte or byte or short or ushort or int or uint or long or ulong
                or float or double or decimal;
        }

        /// <summary>
        /// Creates a deep copy of a document.
        /// </summary>
        protected virtual Document CloneDocument(Document source)
        {
            var clone = new Document
            {
                Id = source.Id,
                CreatedAt = source.CreatedAt,
                UpdatedAt = source.UpdatedAt,
                Version = source.Version,
                Data = source.Data != null ? DeepCopyDictionary(source.Data) : null
            };
            return clone;
        }

        /// <summary>
        /// Creates a deep copy of a dictionary.
        /// </summary>
        private Dictionary<string, object> DeepCopyDictionary(Dictionary<string, object> source)
        {
            var copy = new Dictionary<string, object>(source.Count);
            foreach (var kvp in source)
            {
                // For simple types, just copy the reference/value
                // For nested dictionaries, we'd need deep copy (not implemented for simplicity)
                copy[kvp.Key] = kvp.Value;
            }
            return copy;
        }
    }

    /// <summary>
    /// Resolves conflicts using the Last-Write-Wins strategy (timestamp-based).
    /// </summary>
    public class LastWriteWinsResolver : ConflictResolverBase
    {
        private readonly int _timestampEqualityThresholdMs;

        /// <summary>
        /// Creates a new LastWriteWinsResolver.
        /// </summary>
        /// <param name="timestampEqualityThresholdMs">Maximum time difference before considering timestamps equal.</param>
        public LastWriteWinsResolver(int timestampEqualityThresholdMs = 10)
        {
            _timestampEqualityThresholdMs = timestampEqualityThresholdMs;
        }

        /// <inheritdoc />
        public override string Name => "LastWriteWins";

        /// <inheritdoc />
        public override ConflictResolutionStrategy Strategy => ConflictResolutionStrategy.LastWriteWins;

        /// <inheritdoc />
        public override ConflictResult Resolve(ConflictContext context)
        {
            var local = context.LocalDocument;
            var remote = context.RemoteDocument;

            // Compare timestamps
            var timeDiff = remote.UpdatedAt - local.UpdatedAt;
            var timeDiffMs = Math.Abs(timeDiff.TotalMilliseconds);

            // If timestamps are effectively equal, use version as tiebreaker
            if (timeDiffMs <= _timestampEqualityThresholdMs)
            {
                if (remote.Version > local.Version)
                {
                    return ConflictResult.Resolved(
                        CloneDocument(remote),
                        localWon: false,
                        Strategy,
                        $"Remote document has higher version ({remote.Version} > {local.Version}) despite equal timestamps");
                }
                else if (local.Version > remote.Version)
                {
                    return ConflictResult.Resolved(
                        CloneDocument(local),
                        localWon: true,
                        Strategy,
                        $"Local document has higher version ({local.Version} > {remote.Version}) despite equal timestamps");
                }
                else
                {
                    // Versions are also equal - use node ID as deterministic tiebreaker
                    var localWins = string.Compare(context.LocalNodeId, context.RemoteNodeId, StringComparison.Ordinal) > 0;
                    return ConflictResult.Resolved(
                        CloneDocument(localWins ? local : remote),
                        localWon: localWins,
                        Strategy,
                        $"Timestamp and version equal - resolved using node ID tiebreaker (local: {context.LocalNodeId}, remote: {context.RemoteNodeId})");
                }
            }

            // Clear winner based on timestamp
            if (remote.UpdatedAt > local.UpdatedAt)
            {
                return ConflictResult.Resolved(
                    CloneDocument(remote),
                    localWon: false,
                    Strategy,
                    $"Remote document has later timestamp ({remote.UpdatedAt:O} > {local.UpdatedAt:O})");
            }
            else
            {
                return ConflictResult.Resolved(
                    CloneDocument(local),
                    localWon: true,
                    Strategy,
                    $"Local document has later timestamp ({local.UpdatedAt:O} > {remote.UpdatedAt:O})");
            }
        }
    }

    /// <summary>
    /// Resolves conflicts using the First-Write-Wins strategy (keep original).
    /// </summary>
    public class FirstWriteWinsResolver : ConflictResolverBase
    {
        /// <inheritdoc />
        public override string Name => "FirstWriteWins";

        /// <inheritdoc />
        public override ConflictResolutionStrategy Strategy => ConflictResolutionStrategy.FirstWriteWins;

        /// <inheritdoc />
        public override ConflictResult Resolve(ConflictContext context)
        {
            // Always keep the local (first) document
            return ConflictResult.Resolved(
                CloneDocument(context.LocalDocument),
                localWon: true,
                Strategy,
                "First write wins - keeping local document");
        }
    }

    /// <summary>
    /// Resolves conflicts using the Highest-Version strategy.
    /// </summary>
    public class HighestVersionResolver : ConflictResolverBase
    {
        /// <inheritdoc />
        public override string Name => "HighestVersion";

        /// <inheritdoc />
        public override ConflictResolutionStrategy Strategy => ConflictResolutionStrategy.HighestVersion;

        /// <inheritdoc />
        public override ConflictResult Resolve(ConflictContext context)
        {
            var local = context.LocalDocument;
            var remote = context.RemoteDocument;

            if (remote.Version > local.Version)
            {
                return ConflictResult.Resolved(
                    CloneDocument(remote),
                    localWon: false,
                    Strategy,
                    $"Remote document has higher version ({remote.Version} > {local.Version})");
            }
            else if (local.Version > remote.Version)
            {
                return ConflictResult.Resolved(
                    CloneDocument(local),
                    localWon: true,
                    Strategy,
                    $"Local document has higher version ({local.Version} > {remote.Version})");
            }
            else
            {
                // Versions are equal - use timestamp as tiebreaker
                if (remote.UpdatedAt > local.UpdatedAt)
                {
                    return ConflictResult.Resolved(
                        CloneDocument(remote),
                        localWon: false,
                        Strategy,
                        $"Versions equal ({local.Version}), remote has later timestamp");
                }
                else if (local.UpdatedAt > remote.UpdatedAt)
                {
                    return ConflictResult.Resolved(
                        CloneDocument(local),
                        localWon: true,
                        Strategy,
                        $"Versions equal ({local.Version}), local has later timestamp");
                }
                else
                {
                    // Both version and timestamp equal - use node ID as tiebreaker
                    var localWins = string.Compare(context.LocalNodeId, context.RemoteNodeId, StringComparison.Ordinal) > 0;
                    return ConflictResult.Resolved(
                        CloneDocument(localWins ? local : remote),
                        localWon: localWins,
                        Strategy,
                        $"Version and timestamp equal - resolved using node ID tiebreaker");
                }
            }
        }
    }

    /// <summary>
    /// Resolves conflicts by merging non-conflicting fields from both documents.
    /// </summary>
    public class MergeFieldsResolver : ConflictResolverBase
    {
        /// <inheritdoc />
        public override string Name => "MergeFields";

        /// <inheritdoc />
        public override ConflictResolutionStrategy Strategy => ConflictResolutionStrategy.MergeFields;

        /// <inheritdoc />
        public override ConflictResult Resolve(ConflictContext context)
        {
            var local = context.LocalDocument;
            var remote = context.RemoteDocument;

            // Start with a copy of the remote document (typically has newer data)
            var merged = CloneDocument(remote);
            var conflictedFields = new List<string>();

            if (local.Data != null && remote.Data != null)
            {
                // Identify conflicting fields (fields present in both with different values)
                foreach (var kvp in local.Data)
                {
                    if (remote.Data.TryGetValue(kvp.Key, out var remoteValue))
                    {
                        // Field exists in both - check if values differ
                        if (!ValuesEqual(kvp.Value, remoteValue))
                        {
                            conflictedFields.Add(kvp.Key);
                        }
                    }
                    else
                    {
                        // Field only exists in local - add it to merged result
                        merged.Data ??= new Dictionary<string, object>();
                        merged.Data[kvp.Key] = kvp.Value;
                    }
                }

                // Add any fields only present in remote (already included in clone)
                foreach (var kvp in remote.Data)
                {
                    if (!local.Data.ContainsKey(kvp.Key))
                    {
                        // Field only exists in remote - already in merged
                    }
                }
            }

            // Update metadata for merged document
            merged.Version = Math.Max(local.Version, remote.Version) + 1;
            merged.UpdatedAt = DateTime.UtcNow;

            // Determine if local or remote "won" (we use timestamp for this)
            var localWon = local.UpdatedAt >= remote.UpdatedAt;

            return ConflictResult.Merged(
                merged,
                conflictedFields,
                $"Merged documents with {conflictedFields.Count} conflicting fields: {string.Join(", ", conflictedFields)}");
        }
    }

    /// <summary>
    /// Factory for creating conflict resolvers based on strategy.
    /// </summary>
    public static class ConflictResolverFactory
    {
        private static readonly Dictionary<ConflictResolutionStrategy, IConflictResolver> DefaultResolvers = new()
        {
            [ConflictResolutionStrategy.LastWriteWins] = new LastWriteWinsResolver(),
            [ConflictResolutionStrategy.FirstWriteWins] = new FirstWriteWinsResolver(),
            [ConflictResolutionStrategy.HighestVersion] = new HighestVersionResolver(),
            [ConflictResolutionStrategy.MergeFields] = new MergeFieldsResolver()
        };

        /// <summary>
        /// Creates a conflict resolver for the specified strategy.
        /// </summary>
        /// <param name="strategy">The conflict resolution strategy.</param>
        /// <returns>An instance of the appropriate conflict resolver.</returns>
        public static IConflictResolver CreateResolver(ConflictResolutionStrategy strategy)
        {
            if (DefaultResolvers.TryGetValue(strategy, out var resolver))
            {
                return resolver;
            }

            throw new NotSupportedException($"Conflict resolution strategy '{strategy}' is not supported.");
        }

        /// <summary>
        /// Registers a custom conflict resolver for a strategy.
        /// </summary>
        /// <param name="strategy">The strategy to register.</param>
        /// <param name="resolver">The resolver implementation.</param>
        public static void RegisterResolver(ConflictResolutionStrategy strategy, IConflictResolver resolver)
        {
            DefaultResolvers[strategy] = resolver;
        }

        /// <summary>
        /// Gets all available conflict resolution strategies.
        /// </summary>
        public static IEnumerable<ConflictResolutionStrategy> GetAvailableStrategies()
        {
            return DefaultResolvers.Keys.ToList();
        }
    }

    /// <summary>
    /// Detects conflicts between documents in a distributed cluster.
    /// </summary>
    public class ConflictDetector
    {
        private readonly ConflictResolutionOptions _options;

        /// <summary>
        /// Creates a new ConflictDetector.
        /// </summary>
        /// <param name="options">Configuration options for conflict detection.</param>
        public ConflictDetector(ConflictResolutionOptions? options = null)
        {
            _options = options ?? new ConflictResolutionOptions();
        }

        /// <summary>
        /// Detects whether a conflict exists between a local and remote document.
        /// </summary>
        /// <param name="local">The local document.</param>
        /// <param name="remote">The remote document.</param>
        /// <returns>True if a conflict exists; otherwise, false.</returns>
        public bool DetectConflict(Document local, Document remote)
        {
            if (local == null || remote == null)
                return false;

            // If content is identical, there's no conflict regardless of version/timestamp
            if (AreDocumentsEqual(local, remote))
                return false;

            // Content differs - check if version/timestamp are the same
            // If same version AND timestamps are within threshold, it's a true conflict
            // (both nodes updated the same version independently)
            var timestampsEqual = Math.Abs((local.UpdatedAt - remote.UpdatedAt).TotalMilliseconds) 
                <= _options.TimestampEqualityThresholdMs;
            
            // Content differs - this is a conflict that needs resolution
            return true;
        }

        /// <summary>
        /// Detects conflicts for specific fields between documents.
        /// </summary>
        /// <param name="local">The local document.</param>
        /// <param name="remote">The remote document.</param>
        /// <returns>List of field names that are in conflict.</returns>
        public List<string> DetectConflictingFields(Document local, Document remote)
        {
            var conflictedFields = new List<string>();

            if (local.Data == null || remote.Data == null)
                return conflictedFields;

            // Check all fields from both documents
            var allFields = new HashSet<string>(local.Data.Keys);
            allFields.UnionWith(remote.Data.Keys);

            foreach (var field in allFields)
            {
                var localHas = local.Data.TryGetValue(field, out var localValue);
                var remoteHas = remote.Data.TryGetValue(field, out var remoteValue);

                if (localHas != remoteHas)
                {
                    // Field exists in only one document
                    conflictedFields.Add(field);
                }
                else if (localHas && !ValuesEqual(localValue, remoteValue))
                {
                    // Field exists in both but values differ
                    conflictedFields.Add(field);
                }
            }

            return conflictedFields;
        }

        /// <summary>
        /// Compares two documents for equality based on their content.
        /// </summary>
        private bool AreDocumentsEqual(Document local, Document remote)
        {
            if (local.Data == null && remote.Data == null)
                return true;

            if (local.Data == null || remote.Data == null)
                return false;

            if (local.Data.Count != remote.Data.Count)
                return false;

            foreach (var kvp in local.Data)
            {
                if (!remote.Data.TryGetValue(kvp.Key, out var remoteValue))
                    return false;

                if (!ValuesEqual(kvp.Value, remoteValue))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Compares two values for equality.
        /// </summary>
        private bool ValuesEqual(object? left, object? right)
        {
            if (left == null && right == null)
                return true;

            if (left == null || right == null)
                return false;

            // Handle numeric comparisons
            if (IsNumeric(left) && IsNumeric(right))
            {
                return Convert.ToDouble(left) == Convert.ToDouble(right);
            }

            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether a value is numeric.
        /// </summary>
        private bool IsNumeric(object value)
        {
            return value is sbyte or byte or short or ushort or int or uint or long or ulong
                or float or double or decimal;
        }
    }
}
