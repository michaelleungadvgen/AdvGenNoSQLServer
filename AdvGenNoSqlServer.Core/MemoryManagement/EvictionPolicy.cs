// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
namespace AdvGenNoSqlServer.Core.MemoryManagement;

/// <summary>
/// Controls which entries are evicted when the memory limit is reached.
/// TTL evicts expired entries first, then falls back to LRU ordering.
/// </summary>
public enum EvictionPolicy
{
    /// <summary>Evict the least recently used entries first.</summary>
    LRU,

    /// <summary>Evict the least frequently used entries first.</summary>
    LFU,

    /// <summary>TTL+LRU: expired entries first, then least-recently-used.</summary>
    TTL
}
