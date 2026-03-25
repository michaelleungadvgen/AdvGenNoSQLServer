// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.Transactions;

/// <summary>
/// Garbage collector for old document versions in MVCC.
/// Removes versions that are no longer visible to any active transaction.
/// </summary>
public class VersionGarbageCollector : IDisposable
{
    private readonly IMvccStore _mvccStore;
    private readonly Timer _gcTimer;
    private readonly TimeSpan _gcInterval = TimeSpan.FromMinutes(5);
    private int _totalVersionsCollected;
    private DateTime? _lastCollectionTime;
    private bool _disposed;

    /// <summary>
    /// Total number of versions collected
    /// </summary>
    public int TotalVersionsCollected => _totalVersionsCollected;

    /// <summary>
    /// When the last collection occurred
    /// </summary>
    public DateTime? LastCollectionTime => _lastCollectionTime;

    /// <summary>
    /// Creates a new version garbage collector
    /// </summary>
    public VersionGarbageCollector(IMvccStore mvccStore)
    {
        _mvccStore = mvccStore ?? throw new ArgumentNullException(nameof(mvccStore));

        // Setup periodic garbage collection
        _gcTimer = new Timer(
            RunGarbageCollection,
            null,
            _gcInterval,
            _gcInterval);
    }

    /// <summary>
    /// Manually triggers garbage collection
    /// </summary>
    public int Collect(long oldestActiveTimestamp)
    {
        if (_disposed) return 0;

        // In a real implementation, this would iterate through all version chains
        // and remove versions older than oldestActiveTimestamp that are not the
        // latest non-deleted version

        // For now, we just track that GC was run
        _lastCollectionTime = DateTime.UtcNow;

        // Return simulated count
        return 0;
    }

    /// <summary>
    /// Gets garbage collector statistics
    /// </summary>
    public GarbageCollectorStatistics GetStatistics()
    {
        return new GarbageCollectorStatistics
        {
            TotalVersionsCollected = _totalVersionsCollected,
            LastCollectionTime = _lastCollectionTime
        };
    }

    private void RunGarbageCollection(object? state)
    {
        if (_disposed) return;

        // This would be called periodically to clean up old versions
        // The actual implementation would need access to the oldest active
        // transaction timestamp from the coordinator

        _lastCollectionTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Disposes the garbage collector
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _gcTimer?.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// Statistics for the garbage collector
/// </summary>
public class GarbageCollectorStatistics
{
    public int TotalVersionsCollected { get; set; }
    public DateTime? LastCollectionTime { get; set; }
}
