// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
namespace AdvGenNoSqlServer.Core.MemoryManagement;

/// <summary>
/// Core storage contract. TryGet ownership rule: the returned span is valid only
/// for the duration of the call frame — callers must copy bytes before returning.
/// </summary>
public interface IMemoryStorageEngine : IDisposable
{
    /// <summary>
    /// Attempts to read an entry; the returned span is only valid for the duration of the call frame
    /// </summary>
    /// <param name="key">The entry key</param>
    /// <param name="value">Receives the entry bytes on success; copy before the span leaves the call frame</param>
    /// <returns>True if the entry exists and is not expired; otherwise false</returns>
    bool TryGet(string key, out ReadOnlySpan<byte> value);

    /// <summary>
    /// Inserts or replaces an entry, evicting other entries if the memory limit requires it
    /// </summary>
    /// <param name="key">The entry key</param>
    /// <param name="value">The bytes to store (copied by the engine)</param>
    /// <param name="ttl">Optional time-to-live; null means no expiration</param>
    void Set(string key, ReadOnlySpan<byte> value, TimeSpan? ttl = null);

    /// <summary>
    /// Removes an entry
    /// </summary>
    /// <param name="key">The entry key</param>
    /// <returns>True if the entry was removed; false if it did not exist</returns>
    bool Remove(string key);

    /// <summary>
    /// Removes all entries from the engine
    /// </summary>
    void Clear();

    /// <summary>
    /// Gets a snapshot of the engine's usage and hit/miss statistics
    /// </summary>
    /// <returns>The current engine statistics</returns>
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
    /// <summary>
    /// Raised synchronously when an entry is evicted, inside the engine's shard write lock;
    /// handlers receive (key, evictedBytes, remainingTtl) and must be fast
    /// </summary>
    event Action<string, byte[], TimeSpan?> EntryEvicted;
}
