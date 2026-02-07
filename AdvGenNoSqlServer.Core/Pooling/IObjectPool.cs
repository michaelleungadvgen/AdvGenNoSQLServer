// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.Pooling;

/// <summary>
/// Represents a generic object pool for reusable objects.
/// </summary>
/// <typeparam name="T">The type of objects in the pool.</typeparam>
public interface IObjectPool<T> where T : class
{
    /// <summary>
    /// Gets an object from the pool or creates a new one if the pool is empty.
    /// </summary>
    /// <returns>An object from the pool or a new instance.</returns>
    T Rent();

    /// <summary>
    /// Returns an object to the pool.
    /// </summary>
    /// <param name="obj">The object to return to the pool.</param>
    void Return(T obj);

    /// <summary>
    /// Gets the current number of objects in the pool.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Gets the maximum number of objects the pool can hold.
    /// </summary>
    int MaxCapacity { get; }

    /// <summary>
    /// Gets statistics about the pool's usage.
    /// </summary>
    PoolStatistics Statistics { get; }
}

/// <summary>
/// Statistics for an object pool.
/// </summary>
public class PoolStatistics
{
    private long _rented;
    private long _returned;
    private long _created;
    private long _dropped;

    /// <summary>
    /// Gets the total number of objects rented from the pool.
    /// </summary>
    public long TotalRented => Interlocked.Read(ref _rented);

    /// <summary>
    /// Gets the total number of objects returned to the pool.
    /// </summary>
    public long TotalReturned => Interlocked.Read(ref _returned);

    /// <summary>
    /// Gets the total number of objects created by the pool.
    /// </summary>
    public long TotalCreated => Interlocked.Read(ref _created);

    /// <summary>
    /// Gets the total number of objects dropped (not returned to pool because it was full).
    /// </summary>
    public long TotalDropped => Interlocked.Read(ref _dropped);

    /// <summary>
    /// Gets the number of objects currently in use.
    /// </summary>
    public long InUse => TotalRented - TotalReturned;

    /// <summary>
    /// Increments the rented counter.
    /// </summary>
    internal void IncrementRented() => Interlocked.Increment(ref _rented);

    /// <summary>
    /// Increments the returned counter.
    /// </summary>
    internal void IncrementReturned() => Interlocked.Increment(ref _returned);

    /// <summary>
    /// Increments the created counter.
    /// </summary>
    internal void IncrementCreated() => Interlocked.Increment(ref _created);

    /// <summary>
    /// Increments the dropped counter.
    /// </summary>
    internal void IncrementDropped() => Interlocked.Increment(ref _dropped);

    /// <summary>
    /// Resets all statistics to zero.
    /// </summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _rented, 0);
        Interlocked.Exchange(ref _returned, 0);
        Interlocked.Exchange(ref _created, 0);
        Interlocked.Exchange(ref _dropped, 0);
    }
}
