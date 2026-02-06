// Copyright (c) 2026 [Your Organization]
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Concurrent;
using System.Threading;

namespace AdvGenNoSqlServer.Network
{
    /// <summary>
    /// Manages a pool of available connection slots for resource limiting
    /// </summary>
    public class ConnectionPool
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly int _maxConnections;
        private int _activeConnections;
        private int _totalAcquired;
        private int _totalReleased;

        /// <summary>
        /// Maximum number of concurrent connections allowed
        /// </summary>
        public int MaxConnections => _maxConnections;

        /// <summary>
        /// Current number of active connections
        /// </summary>
        public int ActiveConnections => _activeConnections;

        /// <summary>
        /// Number of available connection slots
        /// </summary>
        public int AvailableSlots => _maxConnections - _activeConnections;

        /// <summary>
        /// Total number of connections ever acquired
        /// </summary>
        public int TotalAcquired => _totalAcquired;

        /// <summary>
        /// Total number of connections released
        /// </summary>
        public int TotalReleased => _totalReleased;

        /// <summary>
        /// Whether the pool has available slots
        /// </summary>
        public bool HasAvailableSlots => _activeConnections < _maxConnections;

        /// <summary>
        /// Creates a new connection pool with the specified capacity
        /// </summary>
        public ConnectionPool(int maxConnections)
        {
            if (maxConnections <= 0)
                throw new ArgumentException("Max connections must be greater than 0", nameof(maxConnections));

            _maxConnections = maxConnections;
            _semaphore = new SemaphoreSlim(maxConnections, maxConnections);
            _activeConnections = 0;
            _totalAcquired = 0;
            _totalReleased = 0;
        }

        /// <summary>
        /// Attempts to acquire a connection slot without waiting
        /// </summary>
        public bool TryAcquire()
        {
            if (_semaphore.Wait(0))
            {
                Interlocked.Increment(ref _activeConnections);
                Interlocked.Increment(ref _totalAcquired);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Acquires a connection slot, waiting if necessary
        /// </summary>
        public void Acquire(CancellationToken cancellationToken = default)
        {
            _semaphore.Wait(cancellationToken);
            Interlocked.Increment(ref _activeConnections);
            Interlocked.Increment(ref _totalAcquired);
        }

        /// <summary>
        /// Attempts to acquire a connection slot with a timeout
        /// </summary>
        public bool TryAcquire(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (_semaphore.Wait(timeout, cancellationToken))
            {
                Interlocked.Increment(ref _activeConnections);
                Interlocked.Increment(ref _totalAcquired);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Releases a connection slot back to the pool
        /// </summary>
        public void Release()
        {
            Interlocked.Decrement(ref _activeConnections);
            Interlocked.Increment(ref _totalReleased);
            _semaphore.Release();
        }

        /// <summary>
        /// Gets current pool statistics
        /// </summary>
        public ConnectionPoolStatistics GetStatistics()
        {
            return new ConnectionPoolStatistics
            {
                MaxConnections = _maxConnections,
                ActiveConnections = _activeConnections,
                AvailableSlots = AvailableSlots,
                TotalAcquired = _totalAcquired,
                TotalReleased = _totalReleased,
                UtilizationPercent = _maxConnections > 0 
                    ? (double)_activeConnections / _maxConnections * 100 
                    : 0
            };
        }

        /// <summary>
        /// Resets the pool statistics
        /// </summary>
        public void ResetStatistics()
        {
            Interlocked.Exchange(ref _totalAcquired, 0);
            Interlocked.Exchange(ref _totalReleased, 0);
        }
    }

    /// <summary>
    /// Statistics for the connection pool
    /// </summary>
    public class ConnectionPoolStatistics
    {
        /// <summary>
        /// Maximum number of connections allowed
        /// </summary>
        public int MaxConnections { get; set; }

        /// <summary>
        /// Currently active connections
        /// </summary>
        public int ActiveConnections { get; set; }

        /// <summary>
        /// Available connection slots
        /// </summary>
        public int AvailableSlots { get; set; }

        /// <summary>
        /// Total connections acquired since start
        /// </summary>
        public int TotalAcquired { get; set; }

        /// <summary>
        /// Total connections released since start
        /// </summary>
        public int TotalReleased { get; set; }

        /// <summary>
        /// Current utilization percentage
        /// </summary>
        public double UtilizationPercent { get; set; }

        /// <summary>
        /// Returns a string representation of the statistics
        /// </summary>
        public override string ToString()
        {
            return $"Connections: {ActiveConnections}/{MaxConnections} " +
                   $"({UtilizationPercent:F1}%), Available: {AvailableSlots}, " +
                   $"Total Acquired: {TotalAcquired}";
        }
    }
}
