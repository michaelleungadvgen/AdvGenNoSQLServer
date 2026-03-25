// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Security.Cryptography;
using System.Text;

using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Core.Sharding;

/// <summary>
/// Implementation of shard key with support for hash, range, and tagged strategies.
/// </summary>
public class ShardKey : IShardKey
{
    private readonly ShardKeyOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShardKey"/> class.
    /// </summary>
    /// <param name="options">The shard key options.</param>
    public ShardKey(ShardKeyOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ShardKey"/> class with default hash strategy.
    /// </summary>
    /// <param name="fieldPath">The field path to use as shard key.</param>
    public ShardKey(string fieldPath)
        : this(new ShardKeyOptions { Strategy = ShardKeyStrategy.Hash, FieldPaths = new List<string> { fieldPath } })
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ShardKey"/> class.
    /// </summary>
    /// <param name="fieldPath">The field path to use as shard key.</param>
    /// <param name="strategy">The shard key strategy.</param>
    public ShardKey(string fieldPath, ShardKeyStrategy strategy)
        : this(new ShardKeyOptions { Strategy = strategy, FieldPaths = new List<string> { fieldPath } })
    {
    }

    /// <inheritdoc />
    public ShardKeyStrategy Strategy => _options.Strategy;

    /// <inheritdoc />
    public IReadOnlyList<string> FieldPaths => _options.FieldPaths.AsReadOnly();

    /// <inheritdoc />
    public int ComputeShardHash(Document document)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));

        var keyValue = ExtractKeyValue(document);
        return ComputeHash(keyValue);
    }

    /// <inheritdoc />
    public string ExtractKeyValue(Document document)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));

        var sb = new StringBuilder();
        
        foreach (var fieldPath in _options.FieldPaths)
        {
            var value = GetValueByPath(document.Data, fieldPath);
            if (value != null)
            {
                sb.Append(value.ToString());
                sb.Append('|'); // Separator for composite keys
            }
        }

        // Remove trailing separator
        if (sb.Length > 0)
        {
            sb.Length--;
        }

        return sb.ToString();
    }

    /// <inheritdoc />
    public string? GetShardTag(Document document)
    {
        if (Strategy != ShardKeyStrategy.Tagged)
            return null;

        return ExtractKeyValue(document);
    }

    /// <summary>
    /// Computes the hash for a key value.
    /// </summary>
    /// <param name="value">The value to hash.</param>
    /// <returns>The computed hash.</returns>
    public int ComputeHash(string value)
    {
        if (string.IsNullOrEmpty(value))
            return 0;

        return _options.HashAlgorithm switch
        {
            ShardHashAlgorithm.Murmur3 => MurmurHash3(value),
            ShardHashAlgorithm.Fnv1a => Fnv1aHash(value),
            ShardHashAlgorithm.Sha256 => Sha256Hash(value),
            ShardHashAlgorithm.DotNet => value.GetHashCode(),
            _ => MurmurHash3(value)
        };
    }

    private static object? GetValueByPath(Dictionary<string, object> data, string path)
    {
        if (data == null || string.IsNullOrEmpty(path))
            return null;

        // Handle nested paths (e.g., "user.id")
        var parts = path.Split('.');
        object? current = data;

        foreach (var part in parts)
        {
            if (current is Dictionary<string, object> dict && dict.TryGetValue(part, out var value))
            {
                current = value;
            }
            else
            {
                return null;
            }
        }

        return current;
    }

    /// <summary>
    /// MurmurHash3 implementation for consistent hashing.
    /// </summary>
    private static int MurmurHash3(string value)
    {
        const uint seed = 0xc58f1a7b;
        var bytes = Encoding.UTF8.GetBytes(value);
        
        uint h1 = seed;
        const uint c1 = 0xcc9e2d51;
        const uint c2 = 0x1b873593;
        const int r1 = 15;
        const int r2 = 13;
        const uint m = 5;
        const uint n = 0xe6546b64;

        int i = 0;
        for (; i <= bytes.Length - 4; i += 4)
        {
            uint k = (uint)(bytes[i] | (bytes[i + 1] << 8) | (bytes[i + 2] << 16) | (bytes[i + 3] << 24));
            
            k *= c1;
            k = (k << r1) | (k >> (32 - r1));
            k *= c2;

            h1 ^= k;
            h1 = (h1 << r2) | (h1 >> (32 - r2));
            h1 = h1 * m + n;
        }

        uint k1 = 0;
        switch (bytes.Length & 3)
        {
            case 3: k1 ^= (uint)bytes[i + 2] << 16; goto case 2;
            case 2: k1 ^= (uint)bytes[i + 1] << 8; goto case 1;
            case 1: k1 ^= bytes[i];
                k1 *= c1;
                k1 = (k1 << r1) | (k1 >> (32 - r1));
                k1 *= c2;
                h1 ^= k1;
                break;
        }

        h1 ^= (uint)bytes.Length;
        h1 ^= h1 >> 16;
        h1 *= 0x85ebca6b;
        h1 ^= h1 >> 13;
        h1 *= 0xc2b2ae35;
        h1 ^= h1 >> 16;

        return (int)h1;
    }

    /// <summary>
    /// FNV-1a hash implementation.
    /// </summary>
    private static int Fnv1aHash(string value)
    {
        const uint fnvOffsetBasis = 0x811c9dc5;
        const uint fnvPrime = 0x01000193;
        
        uint hash = fnvOffsetBasis;
        foreach (var c in value)
        {
            hash ^= c;
            hash *= fnvPrime;
        }
        
        return (int)hash;
    }

    /// <summary>
    /// SHA256-based hash.
    /// </summary>
    private static int Sha256Hash(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        return BitConverter.ToInt32(hash, 0);
    }
}

/// <summary>
/// Extension methods for creating shard keys.
/// </summary>
public static class ShardKeyExtensions
{
    /// <summary>
    /// Creates a hash-based shard key.
    /// </summary>
    /// <param name="fieldPath">The field path to use as shard key.</param>
    /// <returns>A new hash-based shard key.</returns>
    public static ShardKey CreateHashKey(string fieldPath)
    {
        return new ShardKey(fieldPath, ShardKeyStrategy.Hash);
    }

    /// <summary>
    /// Creates a range-based shard key.
    /// </summary>
    /// <param name="fieldPath">The field path to use as shard key.</param>
    /// <returns>A new range-based shard key.</returns>
    public static ShardKey CreateRangeKey(string fieldPath)
    {
        return new ShardKey(fieldPath, ShardKeyStrategy.Range);
    }

    /// <summary>
    /// Creates a tagged shard key.
    /// </summary>
    /// <param name="fieldPath">The field path to use as shard key.</param>
    /// <returns>A new tagged shard key.</returns>
    public static ShardKey CreateTaggedKey(string fieldPath)
    {
        return new ShardKey(fieldPath, ShardKeyStrategy.Tagged);
    }

    /// <summary>
    /// Creates a composite hash-based shard key from multiple field paths.
    /// </summary>
    /// <param name="fieldPaths">The field paths to use as composite shard key.</param>
    /// <returns>A new composite hash-based shard key.</returns>
    public static ShardKey CreateCompositeHashKey(params string[] fieldPaths)
    {
        return new ShardKey(new ShardKeyOptions 
        { 
            Strategy = ShardKeyStrategy.Hash, 
            FieldPaths = fieldPaths.ToList() 
        });
    }
}
