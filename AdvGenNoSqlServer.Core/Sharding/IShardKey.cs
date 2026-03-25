// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Core.Sharding;

/// <summary>
/// Defines the type of shard key strategy.
/// </summary>
public enum ShardKeyStrategy
{
    /// <summary>
    /// Hash-based sharding using consistent hashing.
    /// </summary>
    Hash,

    /// <summary>
    /// Range-based sharding for ordered data.
    /// </summary>
    Range,

    /// <summary>
    /// Tagged sharding for explicit shard assignment.
    /// </summary>
    Tagged
}

/// <summary>
/// Interface for shard keys that determine which shard a document belongs to.
/// </summary>
public interface IShardKey
{
    /// <summary>
    /// Gets the shard key strategy.
    /// </summary>
    ShardKeyStrategy Strategy { get; }

    /// <summary>
    /// Gets the field path(s) used to extract the shard key from documents.
    /// </summary>
    IReadOnlyList<string> FieldPaths { get; }

    /// <summary>
    /// Computes the shard hash for a given document.
    /// </summary>
    /// <param name="document">The document to compute shard hash for.</param>
    /// <returns>A hash value used for shard routing.</returns>
    int ComputeShardHash(Document document);

    /// <summary>
    /// Extracts the shard key value from a document.
    /// </summary>
    /// <param name="document">The document to extract the key from.</param>
    /// <returns>The extracted key value as a string.</returns>
    string ExtractKeyValue(Document document);

    /// <summary>
    /// Gets the shard tag for tagged sharding strategy.
    /// </summary>
    /// <param name="document">The document to get tag for.</param>
    /// <returns>The shard tag, or null if not applicable.</returns>
    string? GetShardTag(Document document);
}

/// <summary>
/// Configuration options for shard keys.
/// </summary>
public class ShardKeyOptions
{
    /// <summary>
    /// Gets or sets the shard key strategy.
    /// </summary>
    public ShardKeyStrategy Strategy { get; set; } = ShardKeyStrategy.Hash;

    /// <summary>
    /// Gets or sets the field path(s) used to extract the shard key.
    /// For composite keys, specify multiple field paths.
    /// </summary>
    public List<string> FieldPaths { get; set; } = new();

    /// <summary>
    /// Gets or sets the hash algorithm for hash-based sharding.
    /// </summary>
    public ShardHashAlgorithm HashAlgorithm { get; set; } = ShardHashAlgorithm.Murmur3;

    /// <summary>
    /// Gets or sets whether to use consistent hashing for better distribution.
    /// </summary>
    public bool UseConsistentHashing { get; set; } = true;

    /// <summary>
    /// Validates the configuration.
    /// </summary>
    /// <exception cref="ValidationException">Thrown when configuration is invalid.</exception>
    public void Validate()
    {
        if (FieldPaths == null || FieldPaths.Count == 0)
        {
            throw new ValidationException("At least one field path must be specified for shard key.");
        }

        foreach (var path in FieldPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ValidationException("Field path cannot be null or whitespace.");
            }
        }
    }
}

/// <summary>
/// Hash algorithms supported for sharding.
/// </summary>
public enum ShardHashAlgorithm
{
    /// <summary>
    /// MurmurHash3 - Fast, good distribution.
    /// </summary>
    Murmur3,

    /// <summary>
    /// FNV-1a - Fast, simple.
    /// </summary>
    Fnv1a,

    /// <summary>
    /// SHA256 - Cryptographic, slower but collision-resistant.
    /// </summary>
    Sha256,

    /// <summary>
    /// .NET GetHashCode - Simple but may vary between runs.
    /// </summary>
    DotNet
}

/// <summary>
/// Exception for sharding validation errors.
/// </summary>
public class ValidationException : Exception
{
    public ValidationException(string message) : base(message) { }
    public ValidationException(string message, Exception inner) : base(message, inner) { }
}
