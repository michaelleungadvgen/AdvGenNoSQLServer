// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;

namespace AdvGenNoSqlServer.Core.Pooling;

/// <summary>
/// A thread-safe object pool implementation that reuses objects to reduce garbage collection pressure.
/// </summary>
/// <typeparam name="T">The type of objects in the pool. Must be a class with a parameterless constructor.</typeparam>
public class ObjectPool<T> : IObjectPool<T>, IDisposable where T : class, new()
{
    private readonly ConcurrentBag<T> _pool;
    private readonly Func<T> _factory;
    private readonly Action<T>? _resetAction;
    private readonly int _maxCapacity;
    private int _count;
    private bool _disposed;

    /// <summary>
    /// Gets the current number of objects in the pool.
    /// </summary>
    public int Count => _count;

    /// <summary>
    /// Gets the maximum number of objects the pool can hold.
    /// </summary>
    public int MaxCapacity => _maxCapacity;

    /// <summary>
    /// Gets statistics about the pool's usage.
    /// </summary>
    public PoolStatistics Statistics { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ObjectPool{T}"/> class.
    /// </summary>
    /// <param name="maxCapacity">The maximum number of objects to keep in the pool.</param>
    /// <param name="factory">Optional factory function to create new objects. Defaults to parameterless constructor.</param>
    /// <param name="resetAction">Optional action to reset object state when returning to pool.</param>
    public ObjectPool(int maxCapacity = 100, Func<T>? factory = null, Action<T>? resetAction = null)
    {
        if (maxCapacity <= 0)
            throw new ArgumentException("Max capacity must be greater than 0.", nameof(maxCapacity));

        _maxCapacity = maxCapacity;
        _pool = new ConcurrentBag<T>();
        _factory = factory ?? (() => new T());
        _resetAction = resetAction;
        Statistics = new PoolStatistics();
    }

    /// <summary>
    /// Gets an object from the pool or creates a new one if the pool is empty.
    /// </summary>
    /// <returns>An object from the pool or a new instance.</returns>
    public T Rent()
    {
        ThrowIfDisposed();

        Statistics.IncrementRented();

        if (_pool.TryTake(out var obj))
        {
            Interlocked.Decrement(ref _count);
            return obj;
        }

        Statistics.IncrementCreated();
        return _factory();
    }

    /// <summary>
    /// Returns an object to the pool.
    /// </summary>
    /// <param name="obj">The object to return to the pool.</param>
    public void Return(T obj)
    {
        if (obj == null)
            throw new ArgumentNullException(nameof(obj));

        ThrowIfDisposed();

        Statistics.IncrementReturned();

        // If pool is at capacity, drop the object
        if (_count >= _maxCapacity)
        {
            Statistics.IncrementDropped();
            return;
        }

        // Reset object state if reset action provided
        _resetAction?.Invoke(obj);

        _pool.Add(obj);
        Interlocked.Increment(ref _count);
    }

    /// <summary>
    /// Pre-populates the pool with a specified number of objects.
    /// </summary>
    /// <param name="count">The number of objects to pre-allocate.</param>
    public void PrePopulate(int count)
    {
        ThrowIfDisposed();

        for (int i = 0; i < count && _count < _maxCapacity; i++)
        {
            _pool.Add(_factory());
            Interlocked.Increment(ref _count);
            Statistics.IncrementCreated();
        }
    }

    /// <summary>
    /// Clears all objects from the pool.
    /// </summary>
    public void Clear()
    {
        while (_pool.TryTake(out _))
        {
            Interlocked.Decrement(ref _count);
        }
    }

    /// <summary>
    /// Disposes the pool and clears all objects.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            Clear();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ObjectPool<T>));
    }
}
