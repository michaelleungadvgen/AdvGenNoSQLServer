// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
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
    public class ConnectionPool : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly int _maxConnections;
        private int _activeConnections;
        private int _totalAcquired;
        private int _totalReleased;
        private bool _disposed;

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
            ThrowIfDisposed();
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
            ThrowIfDisposed();
            _semaphore.Wait(cancellationToken);
            Interlocked.Increment(ref _activeConnections);
            Interlocked.Increment(ref _totalAcquired);
        }

        /// <summary>
        /// Attempts to acquire a connection slot with a timeout
        /// </summary>
        public bool TryAcquire(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
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
            ThrowIfDisposed();
            Interlocked.Decrement(ref _activeConnections);
            Interlocked.Increment(ref _totalReleased);
            _semaphore.Release();
        }

        /// <summary>
        /// Gets current pool statistics
        /// </summary>
        public ConnectionPoolStatistics GetStatistics()
        {
            ThrowIfDisposed();
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
            ThrowIfDisposed();
            Interlocked.Exchange(ref _totalAcquired, 0);
            Interlocked.Exchange(ref _totalReleased, 0);
        }

        /// <summary>
        /// Releases all resources used by the connection pool
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the unmanaged resources used by the connection pool
        /// and optionally releases the managed resources
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _semaphore?.Dispose();
                }

                _disposed = true;
            }
        }

        /// <summary>
        /// Throws ObjectDisposedException if the pool has been disposed
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ConnectionPool));
            }
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
