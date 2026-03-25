// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Core.Sharding;

namespace AdvGenNoSqlServer.Storage.Sharding;

/// <summary>
/// Implementation of shard router using consistent hashing and range-based routing.
/// </summary>
public class ShardRouter : IShardRouter
{
    private readonly ShardConfiguration _configuration;
    private readonly Dictionary<string, ShardNode> _shards;
    private readonly SortedDictionary<int, string> _consistentHashRing;
    private readonly ReaderWriterLockSlim _lock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ShardRouter"/> class.
    /// </summary>
    /// <param name="configuration">The shard configuration.</param>
    public ShardRouter(ShardConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _configuration.Validate();
        
        _shards = new Dictionary<string, ShardNode>();
        _consistentHashRing = new SortedDictionary<int, string>();
        
        // Initialize with existing shards
        foreach (var shard in _configuration.Shards)
        {
            AddShardToInternalStructures(shard);
        }
    }

    /// <inheritdoc />
    public ShardConfiguration Configuration => _configuration;

    /// <inheritdoc />
    public ShardNode RouteDocument(Document document, IShardKey shardKey)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        if (shardKey == null) throw new ArgumentNullException(nameof(shardKey));

        int hash;
        
        switch (shardKey.Strategy)
        {
            case ShardKeyStrategy.Hash:
                hash = shardKey.ComputeShardHash(document);
                return RouteByHash(hash);
                
            case ShardKeyStrategy.Range:
                hash = shardKey.ComputeShardHash(document);
                return RouteByHash(hash);
                
            case ShardKeyStrategy.Tagged:
                var tag = shardKey.GetShardTag(document);
                if (tag != null)
                {
                    var taggedShard = RouteByTag(tag);
                    if (taggedShard != null)
                        return taggedShard;
                }
                // Fall back to hash routing if tag not found
                hash = shardKey.ComputeShardHash(document);
                return RouteByHash(hash);
                
            default:
                throw new ShardingException($"Unknown shard key strategy: {shardKey.Strategy}");
        }
    }

    /// <inheritdoc />
    public ShardNode RouteByHash(int hash)
    {
        _lock.EnterReadLock();
        try
        {
            // Check if we have configured ranges
            if (_configuration.Ranges.Count > 0)
            {
                foreach (var range in _configuration.Ranges)
                {
                    if (range.Contains(hash) && _shards.TryGetValue(range.ShardId, out var shard) && shard.IsActive)
                    {
                        return shard;
                    }
                }
            }

            // Use consistent hashing ring
            if (_consistentHashRing.Count > 0)
            {
                // Find the first node with hash >= document hash
                foreach (var kvp in _consistentHashRing)
                {
                    if (kvp.Key >= hash && _shards.TryGetValue(kvp.Value, out var shard) && shard.IsActive)
                    {
                        return shard;
                    }
                }

                // Wrap around to the first node
                var firstNodeId = _consistentHashRing.First().Value;
                if (_shards.TryGetValue(firstNodeId, out var firstShard) && firstShard.IsActive)
                {
                    return firstShard;
                }
            }

            // Fallback: return any active shard
            var activeShard = _shards.Values.FirstOrDefault(s => s.IsActive);
            if (activeShard != null)
            {
                return activeShard;
            }

            throw new ShardNotFoundException("No active shards available for routing.");
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <inheritdoc />
    public ShardNode? RouteByShardId(string shardId)
    {
        if (string.IsNullOrEmpty(shardId)) return null;

        _lock.EnterReadLock();
        try
        {
            return _shards.TryGetValue(shardId, out var shard) && shard.IsActive ? shard : null;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <inheritdoc />
    public ShardNode? RouteByTag(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return null;

        _lock.EnterReadLock();
        try
        {
            // First try exact match on tags
            var exactMatch = _shards.Values
                .Where(s => s.IsActive && s.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(s => s.Priority)
                .FirstOrDefault();
            
            if (exactMatch != null)
                return exactMatch;

            // Try case-insensitive match on shard name
            return _shards.Values
                .Where(s => s.IsActive && s.Name.Equals(tag, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => s.Priority)
                .FirstOrDefault();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ShardNode> RouteByRange(int minHash, int maxHash)
    {
        _lock.EnterReadLock();
        try
        {
            var matchingShards = new HashSet<string>();

            // Check configured ranges
            foreach (var range in _configuration.Ranges)
            {
                // Check if ranges overlap
                if (range.MinHash < maxHash && range.MaxHash > minHash)
                {
                    matchingShards.Add(range.ShardId);
                }
            }

            // If no configured ranges, use consistent hashing
            if (_configuration.Ranges.Count == 0)
            {
                // Sample points in the range to find affected shards
                var samplePoints = new[] { minHash, (minHash + maxHash) / 2, maxHash - 1 };
                foreach (var point in samplePoints)
                {
                    try
                    {
                        var shard = RouteByHash(point);
                        matchingShards.Add(shard.ShardId);
                    }
                    catch (ShardNotFoundException)
                    {
                        // Ignore
                    }
                }
            }

            return matchingShards
                .Select(id => _shards.TryGetValue(id, out var shard) ? shard : null)
                .Where(s => s != null && s.IsActive)
                .Select(s => s!)
                .ToList();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ShardNode> GetAllActiveShards()
    {
        _lock.EnterReadLock();
        try
        {
            return _shards.Values.Where(s => s.IsActive).ToList();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <inheritdoc />
    public ShardNode GetPrimaryShard(Document document, IShardKey shardKey)
    {
        return RouteDocument(document, shardKey);
    }

    /// <inheritdoc />
    public IReadOnlyList<ShardNode> GetReplicaShards(Document document, IShardKey shardKey)
    {
        var primaryShard = RouteDocument(document, shardKey);
        
        // For now, return other active shards as replicas
        // In a real implementation, this would use configured replication topology
        return GetAllActiveShards()
            .Where(s => s.ShardId != primaryShard.ShardId)
            .Take(_configuration.ReplicationFactor - 1)
            .ToList();
    }

    /// <inheritdoc />
    public void AddShard(ShardNode shard)
    {
        if (shard == null) throw new ArgumentNullException(nameof(shard));

        _lock.EnterWriteLock();
        try
        {
            AddShardToInternalStructures(shard);
            
            RoutingChanged?.Invoke(this, new ShardRoutingChangedEventArgs
            {
                ChangeType = RoutingChangeType.ShardAdded,
                Shard = shard,
                Message = $"Added shard {shard.Name} ({shard.ShardId})"
            });
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <inheritdoc />
    public bool RemoveShard(string shardId)
    {
        if (string.IsNullOrEmpty(shardId)) return false;

        _lock.EnterWriteLock();
        try
        {
            if (!_shards.TryGetValue(shardId, out var shard))
                return false;

            _shards.Remove(shardId);
            
            // Remove from consistent hash ring
            var keysToRemove = _consistentHashRing
                .Where(kvp => kvp.Value == shardId)
                .Select(kvp => kvp.Key)
                .ToList();
            
            foreach (var key in keysToRemove)
            {
                _consistentHashRing.Remove(key);
            }

            RoutingChanged?.Invoke(this, new ShardRoutingChangedEventArgs
            {
                ChangeType = RoutingChangeType.ShardRemoved,
                Shard = shard,
                Message = $"Removed shard {shard.Name} ({shardId})"
            });

            return true;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <inheritdoc />
    public void UpdateShard(ShardNode shard)
    {
        if (shard == null) throw new ArgumentNullException(nameof(shard));

        _lock.EnterWriteLock();
        try
        {
            if (!_shards.ContainsKey(shard.ShardId))
            {
                throw new ShardNotFoundException($"Shard {shard.ShardId} not found.");
            }

            _shards[shard.ShardId] = shard;

            RoutingChanged?.Invoke(this, new ShardRoutingChangedEventArgs
            {
                ChangeType = RoutingChangeType.ShardUpdated,
                Shard = shard,
                Message = $"Updated shard {shard.Name} ({shard.ShardId})"
            });
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, IReadOnlyList<ShardRange>> GetShardDistribution()
    {
        _lock.EnterReadLock();
        try
        {
            var distribution = new Dictionary<string, List<ShardRange>>();

            // Add configured ranges
            foreach (var range in _configuration.Ranges)
            {
                if (!distribution.ContainsKey(range.ShardId))
                    distribution[range.ShardId] = new List<ShardRange>();
                distribution[range.ShardId].Add(range);
            }

            // If no configured ranges, infer from consistent hash ring
            if (_configuration.Ranges.Count == 0)
            {
                var ranges = CalculateRangesFromConsistentHashRing();
                foreach (var kvp in ranges)
                {
                    distribution[kvp.Key] = new List<ShardRange>(kvp.Value);
                }
            }

            return distribution.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlyList<ShardRange>)kvp.Value);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <inheritdoc />
    public event EventHandler<ShardRoutingChangedEventArgs>? RoutingChanged;

    private void AddShardToInternalStructures(ShardNode shard)
    {
        _shards[shard.ShardId] = shard;

        // Add virtual nodes to consistent hash ring
        if (_configuration.VirtualNodesPerShard > 0)
        {
            for (int i = 0; i < _configuration.VirtualNodesPerShard; i++)
            {
                var virtualNodeKey = $"{shard.ShardId}:{i}";
                var hash = ComputeConsistentHash(virtualNodeKey);
                
                // Handle hash collisions
                while (_consistentHashRing.ContainsKey(hash))
                {
                    hash = (hash + 1) % int.MaxValue;
                }
                
                _consistentHashRing[hash] = shard.ShardId;
            }
        }
    }

    private int ComputeConsistentHash(string key)
    {
        // Use FNV-1a for consistent hashing
        const uint fnvOffsetBasis = 0x811c9dc5;
        const uint fnvPrime = 0x01000193;
        
        uint hash = fnvOffsetBasis;
        foreach (var c in key)
        {
            hash ^= c;
            hash *= fnvPrime;
        }
        
        return (int)hash;
    }

    private Dictionary<string, List<ShardRange>> CalculateRangesFromConsistentHashRing()
    {
        var ranges = new Dictionary<string, List<ShardRange>>();
        
        if (_consistentHashRing.Count == 0)
            return ranges;

        var sortedHashes = _consistentHashRing.Keys.ToList();
        sortedHashes.Sort();

        for (int i = 0; i < sortedHashes.Count; i++)
        {
            var currentHash = sortedHashes[i];
            var nextHash = i < sortedHashes.Count - 1 ? sortedHashes[i + 1] : int.MaxValue;
            var shardId = _consistentHashRing[currentHash];

            if (!ranges.ContainsKey(shardId))
                ranges[shardId] = new List<ShardRange>();

            ranges[shardId].Add(new ShardRange
            {
                ShardId = shardId,
                MinHash = currentHash,
                MaxHash = nextHash
            });
        }

        return ranges;
    }
}
