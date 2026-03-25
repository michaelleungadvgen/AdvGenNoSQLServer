// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.Profiling;

/// <summary>
/// Interface for memory profiling and monitoring operations.
/// Provides APIs for tracking memory usage, detecting pressure levels, and monitoring allocation patterns.
/// </summary>
public interface IMemoryProfiler
{
    /// <summary>
    /// Gets the current memory snapshot including heap size, generation sizes, and handle counts.
    /// </summary>
    /// <returns>A snapshot of current memory state.</returns>
    MemorySnapshot GetSnapshot();

    /// <summary>
    /// Gets the current memory snapshot asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation with the memory snapshot.</returns>
    Task<MemorySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Tracks a memory allocation for a specific component or collection.
    /// </summary>
    /// <param name="componentName">Name of the component (e.g., collection name).</param>
    /// <param name="bytesAllocated">Number of bytes allocated.</param>
    Task TrackAllocationAsync(string componentName, long bytesAllocated);

    /// <summary>
    /// Tracks a memory deallocation for a specific component or collection.
    /// </summary>
    /// <param name="componentName">Name of the component.</param>
    /// <param name="bytesFreed">Number of bytes freed.</param>
    Task TrackDeallocationAsync(string componentName, long bytesFreed);

    /// <summary>
    /// Gets allocation statistics for all tracked components.
    /// </summary>
    /// <returns>Dictionary of component names to their allocation info.</returns>
    IReadOnlyDictionary<string, MemoryAllocationInfo> GetAllocationStatistics();

    /// <summary>
    /// Gets the current memory pressure level.
    /// </summary>
    MemoryPressureLevel GetMemoryPressure();

    /// <summary>
    /// Records a memory snapshot for historical tracking.
    /// </summary>
    /// <returns>The recorded snapshot.</returns>
    MemorySnapshot RecordSnapshot();

    /// <summary>
    /// Gets historical snapshots within a time range.
    /// </summary>
    /// <param name="startTime">Start time (inclusive).</param>
    /// <param name="endTime">End time (inclusive).</param>
    /// <returns>List of snapshots in the time range.</returns>
    IReadOnlyList<MemorySnapshot> GetSnapshots(DateTime startTime, DateTime endTime);

    /// <summary>
    /// Clears all historical snapshots.
    /// </summary>
    Task ClearHistoryAsync();

    /// <summary>
    /// Event raised when memory pressure level changes.
    /// </summary>
    event EventHandler<MemoryPressureChangedEventArgs>? MemoryPressureChanged;

    /// <summary>
    /// Event raised when potential memory leak is detected.
    /// </summary>
    event EventHandler<MemoryLeakDetectedEventArgs>? MemoryLeakDetected;
}

/// <summary>
/// Represents a snapshot of memory state at a specific point in time.
/// </summary>
public class MemorySnapshot
{
    /// <summary>
    /// Gets the timestamp when the snapshot was taken.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gets the total managed memory used (in bytes).
    /// </summary>
    public long TotalManagedMemory { get; init; }

    /// <summary>
    /// Gets the total memory allocated by the GC (in bytes).
    /// </summary>
    public long GCTotalMemory { get; init; }

    /// <summary>
    /// Gets the size of Generation 0 heap (in bytes).
    /// </summary>
    public long Gen0Size { get; init; }

    /// <summary>
    /// Gets the size of Generation 1 heap (in bytes).
    /// </summary>
    public long Gen1Size { get; init; }

    /// <summary>
    /// Gets the size of Generation 2 heap (in bytes).
    /// </summary>
    public long Gen2Size { get; init; }

    /// <summary>
    /// Gets the size of Large Object Heap (in bytes).
    /// </summary>
    public long LargeObjectHeapSize { get; init; }

    /// <summary>
    /// Gets the number of GC handles in use.
    /// </summary>
    public long HandleCount { get; init; }

    /// <summary>
    /// Gets the number of finalization survivors.
    /// </summary>
    public long FinalizationSurvivors { get; init; }

    /// <summary>
    /// Gets the number of pinned objects.
    /// </summary>
    public long PinnedObjectCount { get; init; }

    /// <summary>
    /// Gets the memory load percentage (0-100).
    /// </summary>
    public int MemoryLoadPercentage { get; init; }

    /// <summary>
    /// Gets the process working set size (in bytes).
    /// </summary>
    public long WorkingSet { get; init; }

    /// <summary>
    /// Gets the process private memory size (in bytes).
    /// </summary>
    public long PrivateMemory { get; init; }

    /// <summary>
    /// Gets the number of Gen 0 collections so far.
    /// </summary>
    public int Gen0Collections { get; init; }

    /// <summary>
    /// Gets the number of Gen 1 collections so far.
    /// </summary>
    public int Gen1Collections { get; init; }

    /// <summary>
    /// Gets the number of Gen 2 collections so far.
    /// </summary>
    public int Gen2Collections { get; init; }

    /// <summary>
    /// Creates a snapshot from current memory state.
    /// </summary>
    public static MemorySnapshot Capture()
    {
        var gcInfo = GC.GetGCMemoryInfo();
        var process = System.Diagnostics.Process.GetCurrentProcess();

        return new MemorySnapshot
        {
            Timestamp = DateTime.UtcNow,
            TotalManagedMemory = GC.GetTotalMemory(false),
            GCTotalMemory = gcInfo.TotalAvailableMemoryBytes,
            Gen0Size = gcInfo.GenerationInfo.Length > 0 ? gcInfo.GenerationInfo[0].SizeAfterBytes : 0,
            Gen1Size = gcInfo.GenerationInfo.Length > 1 ? gcInfo.GenerationInfo[1].SizeAfterBytes : 0,
            Gen2Size = gcInfo.GenerationInfo.Length > 2 ? gcInfo.GenerationInfo[2].SizeAfterBytes : 0,
            LargeObjectHeapSize = gcInfo.TotalAvailableMemoryBytes > 0 ? gcInfo.PromotedBytes : 0,
            HandleCount = gcInfo.Index,
            FinalizationSurvivors = gcInfo.FinalizationPendingCount,
            PinnedObjectCount = gcInfo.PinnedObjectsCount,
            MemoryLoadPercentage = gcInfo.MemoryLoadBytes > 0 && gcInfo.TotalAvailableMemoryBytes > 0
                ? (int)((gcInfo.MemoryLoadBytes * 100) / gcInfo.TotalAvailableMemoryBytes)
                : 0,
            WorkingSet = process.WorkingSet64,
            PrivateMemory = process.PrivateMemorySize64,
            Gen0Collections = GC.CollectionCount(0),
            Gen1Collections = GC.CollectionCount(1),
            Gen2Collections = GC.CollectionCount(2)
        };
    }
}

/// <summary>
/// Represents memory allocation information for a specific component.
/// </summary>
public class MemoryAllocationInfo
{
    /// <summary>
    /// Gets or sets the component name.
    /// </summary>
    public string ComponentName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the total bytes allocated.
    /// </summary>
    public long BytesAllocated { get; set; }

    /// <summary>
    /// Gets or sets the total bytes freed.
    /// </summary>
    public long BytesFreed { get; set; }

    /// <summary>
    /// Gets the current memory usage (Allocated - Freed).
    /// </summary>
    public long CurrentUsage => BytesAllocated - BytesFreed;

    /// <summary>
    /// Gets or sets the number of allocation operations.
    /// </summary>
    public long AllocationCount { get; set; }

    /// <summary>
    /// Gets or sets the number of deallocation operations.
    /// </summary>
    public long DeallocationCount { get; set; }

    /// <summary>
    /// Gets or sets the peak memory usage (in bytes).
    /// </summary>
    public long PeakUsage { get; set; }

    /// <summary>
    /// Gets or sets the timestamp of first allocation.
    /// </summary>
    public DateTime FirstAllocationTime { get; set; }

    /// <summary>
    /// Gets or sets the timestamp of last allocation.
    /// </summary>
    public DateTime LastAllocationTime { get; set; }
}

/// <summary>
/// Represents the level of memory pressure.
/// </summary>
public enum MemoryPressureLevel
{
    /// <summary>
    /// No memory pressure - memory usage is normal.
    /// </summary>
    None,

    /// <summary>
    /// Low memory pressure - monitor but no action needed.
    /// </summary>
    Low,

    /// <summary>
    /// Medium memory pressure - consider optimization.
    /// </summary>
    Medium,

    /// <summary>
    /// High memory pressure - take action to reduce memory.
    /// </summary>
    High,

    /// <summary>
    /// Critical memory pressure - immediate action required.
    /// </summary>
    Critical
}

/// <summary>
/// Configuration options for memory profiling.
/// </summary>
public class MemoryProfilerOptions
{
    /// <summary>
    /// Gets or sets whether memory profiling is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the threshold percentage for low memory pressure (default: 50).
    /// </summary>
    public int LowPressureThreshold { get; set; } = 50;

    /// <summary>
    /// Gets or sets the threshold percentage for medium memory pressure (default: 70).
    /// </summary>
    public int MediumPressureThreshold { get; set; } = 70;

    /// <summary>
    /// Gets or sets the threshold percentage for high memory pressure (default: 85).
    /// </summary>
    public int HighPressureThreshold { get; set; } = 85;

    /// <summary>
    /// Gets or sets the threshold percentage for critical memory pressure (default: 95).
    /// </summary>
    public int CriticalPressureThreshold { get; set; } = 95;

    /// <summary>
    /// Gets or sets the maximum number of historical snapshots to keep (default: 1000).
    /// </summary>
    public int MaxHistoricalSnapshots { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the interval for automatic snapshots in milliseconds (default: 60000 = 1 minute).
    /// </summary>
    public int SnapshotIntervalMs { get; set; } = 60000;

    /// <summary>
    /// Gets or sets whether to enable automatic snapshots.
    /// </summary>
    public bool EnableAutomaticSnapshots { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to enable memory leak detection.
    /// </summary>
    public bool EnableLeakDetection { get; set; } = true;

    /// <summary>
    /// Gets or sets the growth rate threshold for leak detection (percentage per minute, default: 10).
    /// </summary>
    public double LeakDetectionThresholdPercentPerMinute { get; set; } = 10.0;

    /// <summary>
    /// Validates the options.
    /// </summary>
    public void Validate()
    {
        if (LowPressureThreshold < 0 || LowPressureThreshold > 100)
            throw new ArgumentOutOfRangeException(nameof(LowPressureThreshold), "Must be between 0 and 100");

        if (MediumPressureThreshold < LowPressureThreshold)
            throw new ArgumentException("MediumPressureThreshold must be >= LowPressureThreshold");

        if (HighPressureThreshold < MediumPressureThreshold)
            throw new ArgumentException("HighPressureThreshold must be >= MediumPressureThreshold");

        if (CriticalPressureThreshold < HighPressureThreshold)
            throw new ArgumentException("CriticalPressureThreshold must be >= HighPressureThreshold");

        if (MaxHistoricalSnapshots < 0)
            throw new ArgumentOutOfRangeException(nameof(MaxHistoricalSnapshots), "Must be non-negative");

        if (SnapshotIntervalMs < 1000)
            throw new ArgumentOutOfRangeException(nameof(SnapshotIntervalMs), "Must be at least 1000ms");
    }
}

/// <summary>
/// Event arguments for memory pressure changes.
/// </summary>
public class MemoryPressureChangedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the previous pressure level.
    /// </summary>
    public MemoryPressureLevel PreviousLevel { get; init; }

    /// <summary>
    /// Gets the current pressure level.
    /// </summary>
    public MemoryPressureLevel CurrentLevel { get; init; }

    /// <summary>
    /// Gets the memory snapshot at the time of change.
    /// </summary>
    public MemorySnapshot Snapshot { get; init; } = null!;

    /// <summary>
    /// Gets the timestamp of the change.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Event arguments for memory leak detection.
/// </summary>
public class MemoryLeakDetectedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the component name where leak was detected (empty if system-wide).
    /// </summary>
    public string ComponentName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the growth rate percentage per minute.
    /// </summary>
    public double GrowthRatePercentPerMinute { get; init; }

    /// <summary>
    /// Gets the current memory usage in bytes.
    /// </summary>
    public long CurrentUsageBytes { get; init; }

    /// <summary>
    /// Gets the memory usage at start of detection period.
    /// </summary>
    public long StartingUsageBytes { get; init; }

    /// <summary>
    /// Gets the duration of the detection period.
    /// </summary>
    public TimeSpan DetectionDuration { get; init; }

    /// <summary>
    /// Gets the timestamp when leak was detected.
    /// </summary>
    public DateTime DetectedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gets a message describing the leak.
    /// </summary>
    public string Message => $"Potential memory leak detected in '{ComponentName}': " +
        $"{GrowthRatePercentPerMinute:F2}% growth per minute " +
        $"({StartingUsageBytes / 1024 / 1024:N1} MB -> {CurrentUsageBytes / 1024 / 1024:N1} MB " +
        $"over {DetectionDuration.TotalMinutes:N1} minutes)";
}
