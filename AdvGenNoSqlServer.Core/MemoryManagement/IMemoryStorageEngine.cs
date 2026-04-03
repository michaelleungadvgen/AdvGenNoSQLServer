// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
namespace AdvGenNoSqlServer.Core.MemoryManagement;

/// <summary>
/// Core storage contract. TryGet ownership rule: the returned span is valid only
/// for the duration of the call frame — callers must copy bytes before returning.
/// </summary>
public interface IMemoryStorageEngine : IDisposable
{
    bool TryGet(string key, out ReadOnlySpan<byte> value);
    void Set(string key, ReadOnlySpan<byte> value, TimeSpan? ttl = null);
    bool Remove(string key);
    void Clear();
    MemoryEngineStats GetStats();
}

/// <summary>
/// Extended interface for engines that notify when an entry is evicted.
/// MixedMemoryStorageEngine subscribes to EntryEvicted to spill hot evictions to cold tier.
/// The event fires synchronously inside the engine's shard write lock — handlers must be fast.
/// Handler signature: (key, evictedBytes, remainingTtl)
/// </summary>
public interface IEvictingMemoryStorageEngine : IMemoryStorageEngine
{
    event Action<string, byte[], TimeSpan?> EntryEvicted;
}
