// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Diagnostics;
using System.Runtime;

namespace AdvGenNoSqlServer.Core.Profiling;

/// <summary>
/// Provides automatic memory tuning based on workload patterns and memory pressure.
/// </summary>
public class MemoryTuner : IDisposable
{
    private readonly IMemoryProfiler _profiler;
    private readonly MemoryTuningOptions _options;
    private readonly Timer? _tuningTimer;
    private readonly object _tuningLock = new();
    private bool _disposed;
    private long _lastGCTotalMemory;
    private DateTime _lastTuningTime = DateTime.MinValue;

    /// <summary>
    /// Event raised when a tuning action is performed.
    /// </summary>
    public event EventHandler<TuningActionEventArgs>? TuningActionPerformed;

    /// <summary>
    /// Event raised when GC collection is triggered.
    /// </summary>
    public event EventHandler<GCCollectionEventArgs>? GCCollectionTriggered;

    /// <summary>
    /// Initializes a new instance of the MemoryTuner class.
    /// </summary>
    public MemoryTuner(IMemoryProfiler profiler, MemoryTuningOptions? options = null)
    {
        _profiler = profiler ?? throw new ArgumentNullException(nameof(profiler));
        _options = options ?? new MemoryTuningOptions();
        _options.Validate();

        if (_profiler is MemoryProfiler mp)
        {
            mp.MemoryPressureChanged += OnMemoryPressureChanged;
        }

        if (_options.EnableAutomaticTuning)
        {
            _tuningTimer = new Timer(
                _ => PerformTuning(),
                null,
                TimeSpan.FromMilliseconds(_options.TuningIntervalMs),
                TimeSpan.FromMilliseconds(_options.TuningIntervalMs));
        }

        _lastGCTotalMemory = GC.GetTotalMemory(false);
    }

    private void OnMemoryPressureChanged(object? sender, MemoryPressureChangedEventArgs e)
    {
        if (!_options.EnableAutomaticTuning)
            return;

        // Trigger immediate tuning on critical pressure
        if (e.CurrentLevel == MemoryPressureLevel.Critical)
        {
            PerformAggressiveCleanup();
        }
        else if (e.CurrentLevel == MemoryPressureLevel.High)
        {
            PerformTuning();
        }
    }

    /// <summary>
    /// Performs memory tuning based on current conditions.
    /// </summary>
    public void PerformTuning()
    {
        ThrowIfDisposed();

        lock (_tuningLock)
        {
            // Throttle tuning operations
            if ((DateTime.UtcNow - _lastTuningTime).TotalMilliseconds < _options.MinTuningIntervalMs)
                return;

            _lastTuningTime = DateTime.UtcNow;

            var snapshot = _profiler.GetSnapshot();
            var pressureLevel = _profiler.GetMemoryPressure();

            // Perform actions based on pressure level
            switch (pressureLevel)
            {
                case MemoryPressureLevel.Critical:
                    PerformAggressiveCleanup();
                    break;

                case MemoryPressureLevel.High:
                    PerformHighPressureTuning(snapshot);
                    break;

                case MemoryPressureLevel.Medium:
                    PerformMediumPressureTuning(snapshot);
                    break;

                case MemoryPressureLevel.Low:
                    PerformLowPressureTuning(snapshot);
                    break;

                case MemoryPressureLevel.None:
                    PerformNormalTuning(snapshot);
                    break;
            }
        }
    }

    /// <summary>
    /// Triggers a full garbage collection.
    /// </summary>
    public void TriggerFullGC()
    {
        ThrowIfDisposed();

        var beforeMemory = GC.GetTotalMemory(false);
        var watch = Stopwatch.StartNew();

        // Force full GC on all generations
        GC.Collect(2, GCCollectionMode.Optimized, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Optimized, blocking: true, compacting: true);

        watch.Stop();
        var afterMemory = GC.GetTotalMemory(false);
        var freedMemory = beforeMemory - afterMemory;

        GCCollectionTriggered?.Invoke(this, new GCCollectionEventArgs
        {
            Generation = 2,
            CollectionMode = GCCollectionMode.Optimized,
            DurationMs = watch.ElapsedMilliseconds,
            MemoryBeforeBytes = beforeMemory,
            MemoryAfterBytes = afterMemory,
            MemoryFreedBytes = freedMemory
        });
    }

    /// <summary>
    /// Gets recommended settings based on current memory state.
    /// </summary>
    public TuningRecommendations GetRecommendations()
    {
        ThrowIfDisposed();

        var snapshot = _profiler.GetSnapshot();
        var pressureLevel = _profiler.GetMemoryPressure();
        var stats = _profiler.GetAllocationStatistics();

        var recommendations = new TuningRecommendations
        {
            Timestamp = DateTime.UtcNow,
            CurrentPressureLevel = pressureLevel,
            CurrentSnapshot = snapshot,
            RecommendedCacheSize = CalculateRecommendedCacheSize(snapshot),
            RecommendedBufferPoolSize = CalculateRecommendedBufferPoolSize(snapshot),
            RecommendedLOHCompaction = snapshot.LargeObjectHeapSize > 100 * 1024 * 1024, // > 100MB
            RecommendedGCLatencyMode = GetRecommendedGCLatencyMode(pressureLevel),
            SuggestedOptimizations = new List<string>()
        };

        // Add suggestions based on analysis
        if (snapshot.LargeObjectHeapSize > 1024L * 1024 * 1024) // > 1GB
        {
            recommendations.SuggestedOptimizations.Add("Consider reducing large object allocations");
        }

        if (snapshot.Gen2Collections > snapshot.Gen1Collections * 10)
        {
            recommendations.SuggestedOptimizations.Add("High Gen2 collection rate - consider object pooling");
        }

        var topConsumers = stats
            .OrderByDescending(s => s.Value.CurrentUsage)
            .Take(3)
            .Select(s => s.Key)
            .ToList();

        if (topConsumers.Any())
        {
            recommendations.SuggestedOptimizations.Add($"Top memory consumers: {string.Join(", ", topConsumers)}");
        }

        if (snapshot.HandleCount > 10000)
        {
            recommendations.SuggestedOptimizations.Add("High GC handle count - check for pinned objects");
        }

        return recommendations;
    }

    /// <summary>
    /// Sets the server GC mode.
    /// </summary>
    public void SetServerGCMODE(bool enabled)
    {
        // This is a runtime configuration, typically set in .csproj or runtimeconfig.json
        // We can only log the recommendation
        TuningActionPerformed?.Invoke(this, new TuningActionEventArgs
        {
            Action = "SetServerGC",
            Description = $"Server GC mode recommended: {enabled}",
            IsRuntimeConfigurable = false,
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Compacts the Large Object Heap.
    /// </summary>
    public void CompactLargeObjectHeap()
    {
        ThrowIfDisposed();

        var beforeMemory = GC.GetTotalMemory(false);

        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Optimized, blocking: true, compacting: true);

        var afterMemory = GC.GetTotalMemory(false);

        TuningActionPerformed?.Invoke(this, new TuningActionEventArgs
        {
            Action = "CompactLOH",
            Description = "Compacted Large Object Heap",
            MemoryBeforeBytes = beforeMemory,
            MemoryAfterBytes = afterMemory,
            MemorySavedBytes = beforeMemory - afterMemory,
            Timestamp = DateTime.UtcNow
        });
    }

    private void PerformAggressiveCleanup()
    {
        var beforeMemory = GC.GetTotalMemory(false);

        // Trigger full GC with compaction
        GC.Collect(0, GCCollectionMode.Aggressive, blocking: true);
        GC.Collect(1, GCCollectionMode.Aggressive, blocking: true);
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true);

        // Trim working set on Windows
        if (OperatingSystem.IsWindows())
        {
            GC.Collect();
        }

        var afterMemory = GC.GetTotalMemory(false);

        TuningActionPerformed?.Invoke(this, new TuningActionEventArgs
        {
            Action = "AggressiveCleanup",
            Description = "Performed aggressive memory cleanup due to critical pressure",
            MemoryBeforeBytes = beforeMemory,
            MemoryAfterBytes = afterMemory,
            MemorySavedBytes = beforeMemory - afterMemory,
            Timestamp = DateTime.UtcNow
        });
    }

    private void PerformHighPressureTuning(MemorySnapshot snapshot)
    {
        var beforeMemory = GC.GetTotalMemory(false);

        // Force Gen 2 collection
        GC.Collect(2, GCCollectionMode.Optimized, blocking: false);

        // Compact LOH if needed
        if (snapshot.LargeObjectHeapSize > 50 * 1024 * 1024) // > 50MB
        {
            CompactLargeObjectHeap();
        }

        var afterMemory = GC.GetTotalMemory(false);

        TuningActionPerformed?.Invoke(this, new TuningActionEventArgs
        {
            Action = "HighPressureTuning",
            Description = "Performed high pressure tuning",
            MemoryBeforeBytes = beforeMemory,
            MemoryAfterBytes = afterMemory,
            MemorySavedBytes = beforeMemory - afterMemory,
            Timestamp = DateTime.UtcNow
        });
    }

    private void PerformMediumPressureTuning(MemorySnapshot snapshot)
    {
        // Gentle Gen 1 collection
        GC.Collect(1, GCCollectionMode.Optimized, blocking: false);

        TuningActionPerformed?.Invoke(this, new TuningActionEventArgs
        {
            Action = "MediumPressureTuning",
            Description = "Performed medium pressure tuning (Gen 1 collection)",
            Timestamp = DateTime.UtcNow
        });
    }

    private void PerformLowPressureTuning(MemorySnapshot snapshot)
    {
        // Just Gen 0 collection
        GC.Collect(0, GCCollectionMode.Optimized, blocking: false);

        TuningActionPerformed?.Invoke(this, new TuningActionEventArgs
        {
            Action = "LowPressureTuning",
            Description = "Performed low pressure tuning (Gen 0 collection)",
            Timestamp = DateTime.UtcNow
        });
    }

    private void PerformNormalTuning(MemorySnapshot snapshot)
    {
        // Monitor memory growth
        var currentMemory = GC.GetTotalMemory(false);
        var growth = currentMemory - _lastGCTotalMemory;

        // Only collect if memory has grown significantly
        if (growth > 100 * 1024 * 1024) // > 100MB growth
        {
            GC.Collect(0, GCCollectionMode.Optimized, blocking: false);
            _lastGCTotalMemory = GC.GetTotalMemory(false);
        }
    }

    private long CalculateRecommendedCacheSize(MemorySnapshot snapshot)
    {
        // Simple heuristic: use up to 20% of available memory for cache
        var availableMemory = snapshot.GCTotalMemory - snapshot.TotalManagedMemory;
        return (long)(availableMemory * 0.2);
    }

    private long CalculateRecommendedBufferPoolSize(MemorySnapshot snapshot)
    {
        // Recommend buffer pool size based on memory pressure
        return _profiler.GetMemoryPressure() switch
        {
            MemoryPressureLevel.Critical => 10 * 1024 * 1024,  // 10MB
            MemoryPressureLevel.High => 50 * 1024 * 1024,      // 50MB
            MemoryPressureLevel.Medium => 100 * 1024 * 1024,   // 100MB
            _ => 200 * 1024 * 1024                              // 200MB
        };
    }

    private GCLatencyMode GetRecommendedGCLatencyMode(MemoryPressureLevel pressure)
    {
        return pressure switch
        {
            MemoryPressureLevel.Critical => GCLatencyMode.Batch,
            MemoryPressureLevel.High => GCLatencyMode.Interactive,
            _ => GCLatencyMode.SustainedLowLatency
        };
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MemoryTuner));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _tuningTimer?.Dispose();

        if (_profiler is MemoryProfiler mp)
        {
            mp.MemoryPressureChanged -= OnMemoryPressureChanged;
        }
    }
}

/// <summary>
/// Configuration options for memory tuning.
/// </summary>
public class MemoryTuningOptions
{
    /// <summary>
    /// Gets or sets whether automatic tuning is enabled.
    /// </summary>
    public bool EnableAutomaticTuning { get; set; } = true;

    /// <summary>
    /// Gets or sets the interval between tuning operations in milliseconds (default: 30000 = 30 seconds).
    /// </summary>
    public int TuningIntervalMs { get; set; } = 30000;

    /// <summary>
    /// Gets or sets the minimum interval between tuning operations in milliseconds (default: 5000 = 5 seconds).
    /// </summary>
    public int MinTuningIntervalMs { get; set; } = 5000;

    /// <summary>
    /// Gets or sets whether to compact the Large Object Heap.
    /// </summary>
    public bool EnableLOHCompaction { get; set; } = true;

    /// <summary>
    /// Gets or sets the LOH compaction threshold in bytes (default: 100MB).
    /// </summary>
    public long LOHCompactionThresholdBytes { get; set; } = 100 * 1024 * 1024;

    /// <summary>
    /// Validates the options.
    /// </summary>
    public void Validate()
    {
        if (TuningIntervalMs < 1000)
            throw new ArgumentOutOfRangeException(nameof(TuningIntervalMs), "Must be at least 1000ms");

        if (MinTuningIntervalMs < 100)
            throw new ArgumentOutOfRangeException(nameof(MinTuningIntervalMs), "Must be at least 100ms");

        if (LOHCompactionThresholdBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(LOHCompactionThresholdBytes), "Must be non-negative");
    }
}

/// <summary>
/// Event arguments for tuning actions.
/// </summary>
public class TuningActionEventArgs : EventArgs
{
    /// <summary>
    /// Gets the action name.
    /// </summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>
    /// Gets the action description.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Gets the memory usage before the action.
    /// </summary>
    public long MemoryBeforeBytes { get; init; }

    /// <summary>
    /// Gets the memory usage after the action.
    /// </summary>
    public long MemoryAfterBytes { get; init; }

    /// <summary>
    /// Gets the memory saved by the action.
    /// </summary>
    public long MemorySavedBytes { get; init; }

    /// <summary>
    /// Gets whether the setting is runtime configurable.
    /// </summary>
    public bool IsRuntimeConfigurable { get; init; } = true;

    /// <summary>
    /// Gets the timestamp of the action.
    /// </summary>
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// Event arguments for GC collection.
/// </summary>
public class GCCollectionEventArgs : EventArgs
{
    /// <summary>
    /// Gets the generation that was collected.
    /// </summary>
    public int Generation { get; init; }

    /// <summary>
    /// Gets the collection mode used.
    /// </summary>
    public GCCollectionMode CollectionMode { get; init; }

    /// <summary>
    /// Gets the duration of the collection in milliseconds.
    /// </summary>
    public long DurationMs { get; init; }

    /// <summary>
    /// Gets the memory usage before collection.
    /// </summary>
    public long MemoryBeforeBytes { get; init; }

    /// <summary>
    /// Gets the memory usage after collection.
    /// </summary>
    public long MemoryAfterBytes { get; init; }

    /// <summary>
    /// Gets the memory freed by the collection.
    /// </summary>
    public long MemoryFreedBytes { get; init; }
}

/// <summary>
/// Tuning recommendations based on memory analysis.
/// </summary>
public class TuningRecommendations
{
    /// <summary>
    /// Gets or sets the timestamp of the recommendations.
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the current pressure level.
    /// </summary>
    public MemoryPressureLevel CurrentPressureLevel { get; set; }

    /// <summary>
    /// Gets or sets the current memory snapshot.
    /// </summary>
    public MemorySnapshot? CurrentSnapshot { get; set; }

    /// <summary>
    /// Gets or sets the recommended cache size in bytes.
    /// </summary>
    public long RecommendedCacheSize { get; set; }

    /// <summary>
    /// Gets or sets the recommended buffer pool size in bytes.
    /// </summary>
    public long RecommendedBufferPoolSize { get; set; }

    /// <summary>
    /// Gets or sets whether LOH compaction is recommended.
    /// </summary>
    public bool RecommendedLOHCompaction { get; set; }

    /// <summary>
    /// Gets or sets the recommended GC latency mode.
    /// </summary>
    public GCLatencyMode RecommendedGCLatencyMode { get; set; }

    /// <summary>
    /// Gets or sets the list of suggested optimizations.
    /// </summary>
    public List<string> SuggestedOptimizations { get; set; } = new();
}
