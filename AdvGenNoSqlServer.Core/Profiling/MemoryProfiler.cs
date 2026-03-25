// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;
using System.Diagnostics;

namespace AdvGenNoSqlServer.Core.Profiling;

/// <summary>
/// Implementation of memory profiler for monitoring and tracking memory usage.
/// </summary>
public class MemoryProfiler : IMemoryProfiler, IDisposable
{
    private readonly MemoryProfilerOptions _options;
    private readonly ConcurrentDictionary<string, MemoryAllocationInfo> _allocations = new();
    private readonly ConcurrentQueue<MemorySnapshot> _snapshots = new();
    private readonly Timer? _snapshotTimer;
    private readonly object _leakDetectionLock = new();
    private MemoryPressureLevel _currentPressureLevel = MemoryPressureLevel.None;
    private readonly Dictionary<string, LeakDetectionState> _leakDetectionStates = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the MemoryProfiler class.
    /// </summary>
    public MemoryProfiler(MemoryProfilerOptions? options = null)
    {
        _options = options ?? new MemoryProfilerOptions();
        _options.Validate();

        if (_options.EnableAutomaticSnapshots)
        {
            _snapshotTimer = new Timer(
                _ => RecordSnapshot(),
                null,
                TimeSpan.FromMilliseconds(_options.SnapshotIntervalMs),
                TimeSpan.FromMilliseconds(_options.SnapshotIntervalMs));
        }
    }

    /// <inheritdoc />
    public MemorySnapshot GetSnapshot()
    {
        ThrowIfDisposed();
        return MemorySnapshot.Capture();
    }

    /// <inheritdoc />
    public Task<MemorySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return Task.FromResult(GetSnapshot());
    }

    /// <inheritdoc />
    public Task TrackAllocationAsync(string componentName, long bytesAllocated)
    {
        ThrowIfDisposed();

        if (!_options.Enabled || bytesAllocated <= 0)
            return Task.CompletedTask;

        var info = _allocations.GetOrAdd(componentName, _ => new MemoryAllocationInfo
        {
            ComponentName = componentName,
            FirstAllocationTime = DateTime.UtcNow
        });

        lock (info)
        {
            info.BytesAllocated += bytesAllocated;
            info.AllocationCount++;
            info.LastAllocationTime = DateTime.UtcNow;

            var currentUsage = info.CurrentUsage;
            if (currentUsage > info.PeakUsage)
            {
                info.PeakUsage = currentUsage;
            }
        }

        // Check for memory leaks if enabled
        if (_options.EnableLeakDetection)
        {
            CheckForMemoryLeak(componentName);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task TrackDeallocationAsync(string componentName, long bytesFreed)
    {
        ThrowIfDisposed();

        if (!_options.Enabled || bytesFreed <= 0)
            return Task.CompletedTask;

        if (_allocations.TryGetValue(componentName, out var info))
        {
            lock (info)
            {
                info.BytesFreed += bytesFreed;
                info.DeallocationCount++;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, MemoryAllocationInfo> GetAllocationStatistics()
    {
        ThrowIfDisposed();
        return new Dictionary<string, MemoryAllocationInfo>(_allocations);
    }

    /// <inheritdoc />
    public MemoryPressureLevel GetMemoryPressure()
    {
        ThrowIfDisposed();

        var snapshot = GetSnapshot();
        return CalculatePressureLevel(snapshot);
    }

    /// <inheritdoc />
    public MemorySnapshot RecordSnapshot()
    {
        ThrowIfDisposed();

        var snapshot = GetSnapshot();

        // Add to history
        _snapshots.Enqueue(snapshot);

        // Trim old snapshots if needed
        while (_snapshots.Count > _options.MaxHistoricalSnapshots && _snapshots.TryDequeue(out _))
        {
            // Remove excess snapshots
        }

        // Check for pressure level change
        var newPressureLevel = CalculatePressureLevel(snapshot);
        if (newPressureLevel != _currentPressureLevel)
        {
            var previousLevel = _currentPressureLevel;
            _currentPressureLevel = newPressureLevel;

            MemoryPressureChanged?.Invoke(this, new MemoryPressureChangedEventArgs
            {
                PreviousLevel = previousLevel,
                CurrentLevel = newPressureLevel,
                Snapshot = snapshot,
                Timestamp = DateTime.UtcNow
            });
        }

        return snapshot;
    }

    /// <inheritdoc />
    public IReadOnlyList<MemorySnapshot> GetSnapshots(DateTime startTime, DateTime endTime)
    {
        ThrowIfDisposed();

        return _snapshots
            .Where(s => s.Timestamp >= startTime && s.Timestamp <= endTime)
            .OrderBy(s => s.Timestamp)
            .ToList();
    }

    /// <inheritdoc />
    public Task ClearHistoryAsync()
    {
        ThrowIfDisposed();

        while (_snapshots.TryDequeue(out _))
        {
            // Clear all snapshots
        }

        _allocations.Clear();
        _leakDetectionStates.Clear();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public event EventHandler<MemoryPressureChangedEventArgs>? MemoryPressureChanged;

    /// <inheritdoc />
    public event EventHandler<MemoryLeakDetectedEventArgs>? MemoryLeakDetected;

    /// <summary>
    /// Gets the current memory pressure level.
    /// </summary>
    public MemoryPressureLevel CurrentPressureLevel => _currentPressureLevel;

    /// <summary>
    /// Gets the profiler options.
    /// </summary>
    public MemoryProfilerOptions Options => _options;

    private MemoryPressureLevel CalculatePressureLevel(MemorySnapshot snapshot)
    {
        var memoryLoad = snapshot.MemoryLoadPercentage;

        if (memoryLoad >= _options.CriticalPressureThreshold)
            return MemoryPressureLevel.Critical;
        if (memoryLoad >= _options.HighPressureThreshold)
            return MemoryPressureLevel.High;
        if (memoryLoad >= _options.MediumPressureThreshold)
            return MemoryPressureLevel.Medium;
        if (memoryLoad >= _options.LowPressureThreshold)
            return MemoryPressureLevel.Low;

        return MemoryPressureLevel.None;
    }

    private void CheckForMemoryLeak(string componentName)
    {
        lock (_leakDetectionLock)
        {
            if (!_allocations.TryGetValue(componentName, out var info))
                return;

            var now = DateTime.UtcNow;
            var currentUsage = info.CurrentUsage;

            if (!_leakDetectionStates.TryGetValue(componentName, out var state))
            {
                _leakDetectionStates[componentName] = new LeakDetectionState
                {
                    StartTime = now,
                    StartingUsage = currentUsage,
                    LastCheckTime = now,
                    LastCheckUsage = currentUsage
                };
                return;
            }

            // Only check every minute
            if ((now - state.LastCheckTime).TotalSeconds < 60)
                return;

            var timeDiff = now - state.StartTime;
            var usageDiff = currentUsage - state.StartingUsage;

            if (timeDiff.TotalMinutes >= 5 && state.StartingUsage > 0) // Minimum 5 minutes for detection
            {
                var growthRate = (usageDiff * 100.0) / state.StartingUsage / timeDiff.TotalMinutes;

                if (growthRate > _options.LeakDetectionThresholdPercentPerMinute)
                {
                    MemoryLeakDetected?.Invoke(this, new MemoryLeakDetectedEventArgs
                    {
                        ComponentName = componentName,
                        GrowthRatePercentPerMinute = growthRate,
                        CurrentUsageBytes = currentUsage,
                        StartingUsageBytes = state.StartingUsage,
                        DetectionDuration = timeDiff
                    });

                    // Reset detection state to avoid repeated alerts
                    _leakDetectionStates[componentName] = new LeakDetectionState
                    {
                        StartTime = now,
                        StartingUsage = currentUsage,
                        LastCheckTime = now,
                        LastCheckUsage = currentUsage
                    };
                }
            }

            state.LastCheckTime = now;
            state.LastCheckUsage = currentUsage;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MemoryProfiler));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _snapshotTimer?.Dispose();
        _allocations.Clear();
        _leakDetectionStates.Clear();
        while (_snapshots.TryDequeue(out _)) { }
    }

    private class LeakDetectionState
    {
        public DateTime StartTime { get; set; }
        public long StartingUsage { get; set; }
        public DateTime LastCheckTime { get; set; }
        public long LastCheckUsage { get; set; }
    }
}
