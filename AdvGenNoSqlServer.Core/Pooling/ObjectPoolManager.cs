// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;

namespace AdvGenNoSqlServer.Core.Pooling;

/// <summary>
/// Centralized manager for object pools. Provides named pool registration and lookup.
/// </summary>
public class ObjectPoolManager : IDisposable
{
    private static readonly Lazy<ObjectPoolManager> _default = new(() => new ObjectPoolManager());
    private readonly ConcurrentDictionary<string, object> _pools = new();
    private bool _disposed;

    /// <summary>
    /// Gets the default global pool manager instance.
    /// </summary>
    public static ObjectPoolManager Default => _default.Value;

    /// <summary>
    /// Gets or creates a named object pool.
    /// </summary>
    /// <typeparam name="T">The type of objects in the pool.</typeparam>
    /// <param name="name">The unique name of the pool.</param>
    /// <param name="maxCapacity">The maximum capacity of the pool.</param>
    /// <param name="factory">Optional factory function.</param>
    /// <param name="resetAction">Optional reset action.</param>
    /// <returns>The object pool.</returns>
    public IObjectPool<T> GetOrCreatePool<T>(
        string name,
        int maxCapacity = 100,
        Func<T>? factory = null,
        Action<T>? resetAction = null) where T : class, new()
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Pool name cannot be null or empty.", nameof(name));

        return (IObjectPool<T>)_pools.GetOrAdd(name, _ => 
            new ObjectPool<T>(maxCapacity, factory, resetAction));
    }

    /// <summary>
    /// Registers a pre-configured pool with the manager.
    /// </summary>
    /// <typeparam name="T">The type of objects in the pool.</typeparam>
    /// <param name="name">The unique name of the pool.</param>
    /// <param name="pool">The pool to register.</param>
    /// <returns>True if the pool was registered; false if a pool with the same name already exists.</returns>
    public bool RegisterPool<T>(string name, IObjectPool<T> pool) where T : class
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Pool name cannot be null or empty.", nameof(name));

        return _pools.TryAdd(name, pool!);
    }

    /// <summary>
    /// Gets a registered pool by name.
    /// </summary>
    /// <typeparam name="T">The type of objects in the pool.</typeparam>
    /// <param name="name">The name of the pool.</param>
    /// <returns>The object pool, or null if not found.</returns>
    public IObjectPool<T>? GetPool<T>(string name) where T : class
    {
        ThrowIfDisposed();

        if (_pools.TryGetValue(name, out var pool))
        {
            return (IObjectPool<T>)pool;
        }

        return null;
    }

    /// <summary>
    /// Removes a pool from the manager.
    /// </summary>
    /// <param name="name">The name of the pool to remove.</param>
    /// <returns>True if the pool was removed; false if not found.</returns>
    public bool RemovePool(string name)
    {
        ThrowIfDisposed();
        return _pools.TryRemove(name, out _);
    }

    /// <summary>
    /// Gets all registered pool names.
    /// </summary>
    /// <returns>A collection of pool names.</returns>
    public IEnumerable<string> GetPoolNames()
    {
        ThrowIfDisposed();
        return _pools.Keys.ToList();
    }

    /// <summary>
    /// Clears all pools from the manager.
    /// </summary>
    public void ClearAllPools()
    {
        if (_disposed) return;

        foreach (var pool in _pools.Values)
        {
            if (pool is IDisposable disposable)
            {
                try
                {
                    disposable.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // Already disposed, ignore
                }
            }
        }

        _pools.Clear();
    }

    /// <summary>
    /// Gets statistics for all registered pools.
    /// </summary>
    /// <returns>A dictionary mapping pool names to their statistics.</returns>
    public Dictionary<string, PoolStatistics> GetAllStatistics()
    {
        ThrowIfDisposed();

        var stats = new Dictionary<string, PoolStatistics>();

        foreach (var kvp in _pools)
        {
            var pool = kvp.Value;
            var poolType = pool.GetType();
            
            // Use reflection to get the Statistics property
            var statisticsProperty = poolType.GetProperty("Statistics");
            if (statisticsProperty != null)
            {
                var statistics = statisticsProperty.GetValue(pool) as PoolStatistics;
                if (statistics != null)
                {
                    stats[kvp.Key] = statistics;
                }
            }
        }

        return stats;
    }

    /// <summary>
    /// Disposes the pool manager and all registered pools.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            
            foreach (var pool in _pools.Values)
            {
                if (pool is IDisposable disposable)
                {
                    try
                    {
                        disposable.Dispose();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Already disposed, ignore
                    }
                }
            }

            _pools.Clear();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ObjectPoolManager));
    }
}
