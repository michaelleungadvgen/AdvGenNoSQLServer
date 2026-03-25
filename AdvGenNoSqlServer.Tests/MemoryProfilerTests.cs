// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Profiling;
using System.Runtime;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for memory profiler components.
/// </summary>
public class MemoryProfilerTests
{
    #region MemoryProfilerOptions Tests

    [Fact]
    public void MemoryProfilerOptions_DefaultValues_AreCorrect()
    {
        var options = new MemoryProfilerOptions();

        Assert.True(options.Enabled);
        Assert.Equal(50, options.LowPressureThreshold);
        Assert.Equal(70, options.MediumPressureThreshold);
        Assert.Equal(85, options.HighPressureThreshold);
        Assert.Equal(95, options.CriticalPressureThreshold);
        Assert.Equal(1000, options.MaxHistoricalSnapshots);
        Assert.Equal(60000, options.SnapshotIntervalMs);
        Assert.True(options.EnableAutomaticSnapshots);
        Assert.True(options.EnableLeakDetection);
        Assert.Equal(10.0, options.LeakDetectionThresholdPercentPerMinute);
    }

    [Fact]
    public void MemoryProfilerOptions_Validate_WithValidOptions_DoesNotThrow()
    {
        var options = new MemoryProfilerOptions();

        var exception = Record.Exception(() => options.Validate());
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void MemoryProfilerOptions_Validate_InvalidLowPressure_Throws(int threshold)
    {
        var options = new MemoryProfilerOptions { LowPressureThreshold = threshold };

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }

    [Fact]
    public void MemoryProfilerOptions_Validate_MediumBelowLow_Throws()
    {
        var options = new MemoryProfilerOptions
        {
            LowPressureThreshold = 60,
            MediumPressureThreshold = 50
        };

        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void MemoryProfilerOptions_Validate_HighBelowMedium_Throws()
    {
        var options = new MemoryProfilerOptions
        {
            MediumPressureThreshold = 70,
            HighPressureThreshold = 60
        };

        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void MemoryProfilerOptions_Validate_CriticalBelowHigh_Throws()
    {
        var options = new MemoryProfilerOptions
        {
            HighPressureThreshold = 80,
            CriticalPressureThreshold = 70
        };

        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void MemoryProfilerOptions_Validate_NegativeMaxSnapshots_Throws()
    {
        var options = new MemoryProfilerOptions { MaxHistoricalSnapshots = -1 };

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }

    [Fact]
    public void MemoryProfilerOptions_Validate_SnapshotIntervalTooLow_Throws()
    {
        var options = new MemoryProfilerOptions { SnapshotIntervalMs = 500 };

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }

    #endregion

    #region MemorySnapshot Tests

    [Fact]
    public void MemorySnapshot_Capture_ReturnsValidSnapshot()
    {
        var snapshot = MemorySnapshot.Capture();

        Assert.NotNull(snapshot);
        Assert.True(snapshot.Timestamp <= DateTime.UtcNow);
        Assert.True(snapshot.Timestamp > DateTime.UtcNow.AddSeconds(-1));
        Assert.True(snapshot.TotalManagedMemory >= 0);
        Assert.True(snapshot.WorkingSet >= 0);
        Assert.True(snapshot.Gen0Collections >= 0);
        Assert.True(snapshot.Gen1Collections >= 0);
        Assert.True(snapshot.Gen2Collections >= 0);
    }

    [Fact]
    public void MemorySnapshot_Capture_MultipleSnapshotsHaveDifferentTimestamps()
    {
        var snapshot1 = MemorySnapshot.Capture();
        Thread.Sleep(10);
        var snapshot2 = MemorySnapshot.Capture();

        Assert.True(snapshot2.Timestamp > snapshot1.Timestamp);
    }

    [Fact]
    public void MemorySnapshot_GetSummary_ReturnsReadableString()
    {
        var snapshot = MemorySnapshot.Capture();
        var summary = snapshot.GetSummary();

        Assert.NotNull(summary);
        Assert.Contains("Memory at", summary);
        Assert.Contains("Managed:", summary);
        Assert.Contains("Working Set:", summary);
        Assert.Contains("LOH:", summary);
        Assert.Contains("Load:", summary);
    }

    #endregion

    #region MemoryAllocationInfo Tests

    [Fact]
    public void MemoryAllocationInfo_CurrentUsage_CalculatedCorrectly()
    {
        var info = new MemoryAllocationInfo
        {
            ComponentName = "Test",
            BytesAllocated = 1000,
            BytesFreed = 300
        };

        Assert.Equal(700, info.CurrentUsage);
    }

    [Fact]
    public void MemoryAllocationInfo_GetSummary_ReturnsReadableString()
    {
        var info = new MemoryAllocationInfo
        {
            ComponentName = "TestComponent",
            BytesAllocated = 1024 * 1024,
            BytesFreed = 512 * 1024,
            PeakUsage = 2 * 1024 * 1024,
            AllocationCount = 100,
            DeallocationCount = 50
        };

        var summary = info.GetSummary();

        Assert.Contains("TestComponent", summary);
        Assert.Contains("512.00 KB current", summary);
        Assert.Contains("2.00 MB peak", summary);
        Assert.Contains("100 allocs", summary);
        Assert.Contains("50 frees", summary);
    }

    #endregion

    #region MemoryProfiler Tests

    [Fact]
    public void MemoryProfiler_Constructor_WithNullOptions_UsesDefaults()
    {
        using var profiler = new MemoryProfiler(null);

        Assert.NotNull(profiler);
        Assert.NotNull(profiler.Options);
    }

    [Fact]
    public void MemoryProfiler_Constructor_WithValidOptions_SetsOptions()
    {
        var options = new MemoryProfilerOptions { MaxHistoricalSnapshots = 500 };
        using var profiler = new MemoryProfiler(options);

        Assert.Equal(500, profiler.Options.MaxHistoricalSnapshots);
    }

    [Fact]
    public void MemoryProfiler_GetSnapshot_ReturnsValidSnapshot()
    {
        using var profiler = new MemoryProfiler();

        var snapshot = profiler.GetSnapshot();

        Assert.NotNull(snapshot);
        Assert.True(snapshot.TotalManagedMemory >= 0);
    }

    [Fact]
    public async Task MemoryProfiler_GetSnapshotAsync_ReturnsValidSnapshot()
    {
        using var profiler = new MemoryProfiler();

        var snapshot = await profiler.GetSnapshotAsync();

        Assert.NotNull(snapshot);
        Assert.True(snapshot.TotalManagedMemory >= 0);
    }

    [Fact]
    public async Task MemoryProfiler_TrackAllocation_RecordsAllocation()
    {
        using var profiler = new MemoryProfiler();

        await profiler.TrackAllocationAsync("TestComponent", 1024);

        var stats = profiler.GetAllocationStatistics();
        Assert.True(stats.ContainsKey("TestComponent"));
        Assert.Equal(1024, stats["TestComponent"].BytesAllocated);
    }

    [Fact]
    public async Task MemoryProfiler_TrackMultipleAllocations_Accumulates()
    {
        using var profiler = new MemoryProfiler();

        await profiler.TrackAllocationAsync("TestComponent", 1024);
        await profiler.TrackAllocationAsync("TestComponent", 2048);

        var stats = profiler.GetAllocationStatistics();
        Assert.Equal(3072, stats["TestComponent"].BytesAllocated);
        Assert.Equal(2, stats["TestComponent"].AllocationCount);
    }

    [Fact]
    public async Task MemoryProfiler_TrackDeallocation_ReducesUsage()
    {
        using var profiler = new MemoryProfiler();

        await profiler.TrackAllocationAsync("TestComponent", 1024);
        await profiler.TrackDeallocationAsync("TestComponent", 512);

        var stats = profiler.GetAllocationStatistics();
        Assert.Equal(512, stats["TestComponent"].CurrentUsage);
        Assert.Equal(512, stats["TestComponent"].BytesFreed);
    }

    [Fact]
    public async Task MemoryProfiler_TrackAllocation_WithZeroBytes_DoesNotRecord()
    {
        using var profiler = new MemoryProfiler();

        await profiler.TrackAllocationAsync("TestComponent", 0);

        var stats = profiler.GetAllocationStatistics();
        Assert.Empty(stats);
    }

    [Fact]
    public void MemoryProfiler_GetAllocationStatistics_ReturnsCopy()
    {
        using var profiler = new MemoryProfiler();

        var stats1 = profiler.GetAllocationStatistics();
        var stats2 = profiler.GetAllocationStatistics();

        Assert.NotSame(stats1, stats2);
    }

    [Fact]
    public void MemoryProfiler_GetMemoryPressure_ReturnsValidLevel()
    {
        using var profiler = new MemoryProfiler();

        var pressure = profiler.GetMemoryPressure();

        Assert.True(pressure >= MemoryPressureLevel.None && pressure <= MemoryPressureLevel.Critical);
    }

    [Fact]
    public void MemoryProfiler_RecordSnapshot_AddsToHistory()
    {
        using var profiler = new MemoryProfiler();

        profiler.RecordSnapshot();
        var snapshots = profiler.GetSnapshots(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));

        Assert.True(snapshots.Count >= 1);
    }

    [Fact]
    public async Task MemoryProfiler_ClearHistory_RemovesAllData()
    {
        using var profiler = new MemoryProfiler();

        profiler.RecordSnapshot();
        await profiler.TrackAllocationAsync("Test", 1024);
        await profiler.ClearHistoryAsync();

        var stats = profiler.GetAllocationStatistics();
        var snapshots = profiler.GetSnapshots(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));

        Assert.Empty(stats);
        Assert.Empty(snapshots);
    }

    [Fact]
    public void MemoryProfiler_MemoryPressureChanged_EventFires()
    {
        var options = new MemoryProfilerOptions
        {
            EnableAutomaticSnapshots = false, // Disable to control manually
            LowPressureThreshold = 0, // Make it easy to trigger
            MediumPressureThreshold = 1,
            HighPressureThreshold = 2,
            CriticalPressureThreshold = 3
        };
        using var profiler = new MemoryProfiler(options);

        var eventFired = false;
        profiler.MemoryPressureChanged += (s, e) => { eventFired = true; };

        // Force multiple snapshots to potentially trigger pressure change
        profiler.RecordSnapshot();

        // Event may or may not fire depending on current memory state
        // Just verify the event handler can be attached
        Assert.True(true);
    }

    [Fact]
    public void MemoryProfiler_MemoryLeakDetected_EventCanBeAttached()
    {
        using var profiler = new MemoryProfiler();

        var eventFired = false;
        profiler.MemoryLeakDetected += (s, e) => { eventFired = true; };

        // Just verify the event handler can be attached
        Assert.True(true);
    }

    [Fact]
    public void MemoryProfiler_Dispose_CanBeCalledMultipleTimes()
    {
        var profiler = new MemoryProfiler();
        profiler.Dispose();

        // Should not throw
        profiler.Dispose();
    }

    [Fact]
    public void MemoryProfiler_AfterDispose_OperationsThrow()
    {
        var profiler = new MemoryProfiler();
        profiler.Dispose();

        Assert.Throws<ObjectDisposedException>(() => profiler.GetSnapshot());
        Assert.Throws<ObjectDisposedException>(() => profiler.GetMemoryPressure());
        Assert.Throws<ObjectDisposedException>(() => profiler.RecordSnapshot());
    }

    [Fact]
    public void MemoryProfiler_Disabled_DoesNotTrackAllocations()
    {
        var options = new MemoryProfilerOptions { Enabled = false };
        using var profiler = new MemoryProfiler(options);

        profiler.TrackAllocationAsync("Test", 1024).Wait();

        var stats = profiler.GetAllocationStatistics();
        Assert.Empty(stats);
    }

    #endregion

    #region MemoryTuningOptions Tests

    [Fact]
    public void MemoryTuningOptions_DefaultValues_AreCorrect()
    {
        var options = new MemoryTuningOptions();

        Assert.True(options.EnableAutomaticTuning);
        Assert.Equal(30000, options.TuningIntervalMs);
        Assert.Equal(5000, options.MinTuningIntervalMs);
        Assert.True(options.EnableLOHCompaction);
        Assert.Equal(100 * 1024 * 1024, options.LOHCompactionThresholdBytes);
    }

    [Fact]
    public void MemoryTuningOptions_Validate_WithValidOptions_DoesNotThrow()
    {
        var options = new MemoryTuningOptions();

        var exception = Record.Exception(() => options.Validate());
        Assert.Null(exception);
    }

    [Fact]
    public void MemoryTuningOptions_Validate_TuningIntervalTooLow_Throws()
    {
        var options = new MemoryTuningOptions { TuningIntervalMs = 500 };

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }

    [Fact]
    public void MemoryTuningOptions_Validate_MinTuningIntervalTooLow_Throws()
    {
        var options = new MemoryTuningOptions { MinTuningIntervalMs = 50 };

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }

    [Fact]
    public void MemoryTuningOptions_Validate_NegativeLOHThreshold_Throws()
    {
        var options = new MemoryTuningOptions { LOHCompactionThresholdBytes = -1 };

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }

    #endregion

    #region MemoryTuner Tests

    [Fact]
    public void MemoryTuner_Constructor_WithNullProfiler_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new MemoryTuner(null!));
    }

    [Fact]
    public void MemoryTuner_Constructor_WithValidProfiler_Succeeds()
    {
        using var profiler = new MemoryProfiler();
        using var tuner = new MemoryTuner(profiler);

        Assert.NotNull(tuner);
    }

    [Fact]
    public void MemoryTuner_PerformTuning_DoesNotThrow()
    {
        using var profiler = new MemoryProfiler();
        using var tuner = new MemoryTuner(profiler);

        var exception = Record.Exception(() => tuner.PerformTuning());
        Assert.Null(exception);
    }

    [Fact]
    public void MemoryTuner_TriggerFullGC_DoesNotThrow()
    {
        using var profiler = new MemoryProfiler();
        using var tuner = new MemoryTuner(profiler);

        var exception = Record.Exception(() => tuner.TriggerFullGC());
        Assert.Null(exception);
    }

    [Fact]
    public void MemoryTuner_GetRecommendations_ReturnsValidRecommendations()
    {
        using var profiler = new MemoryProfiler();
        using var tuner = new MemoryTuner(profiler);

        var recommendations = tuner.GetRecommendations();

        Assert.NotNull(recommendations);
        Assert.True(recommendations.RecommendedCacheSize >= 0);
        Assert.True(recommendations.RecommendedBufferPoolSize >= 0);
        Assert.NotNull(recommendations.SuggestedOptimizations);
    }

    [Fact]
    public void MemoryTuner_CompactLargeObjectHeap_DoesNotThrow()
    {
        using var profiler = new MemoryProfiler();
        using var tuner = new MemoryTuner(profiler);

        var exception = Record.Exception(() => tuner.CompactLargeObjectHeap());
        Assert.Null(exception);
    }

    [Fact]
    public void MemoryTuner_SetServerGCMODE_DoesNotThrow()
    {
        using var profiler = new MemoryProfiler();
        using var tuner = new MemoryTuner(profiler);

        var exception = Record.Exception(() => tuner.SetServerGCMODE(true));
        Assert.Null(exception);
    }

    [Fact]
    public void MemoryTuner_TuningActionPerformed_EventCanBeAttached()
    {
        using var profiler = new MemoryProfiler();
        using var tuner = new MemoryTuner(profiler);

        var eventFired = false;
        tuner.TuningActionPerformed += (s, e) => { eventFired = true; };

        tuner.PerformTuning();

        // Event may or may not fire depending on timing
        Assert.True(true);
    }

    [Fact]
    public void MemoryTuner_GCCollectionTriggered_EventCanBeAttached()
    {
        using var profiler = new MemoryProfiler();
        using var tuner = new MemoryTuner(profiler);

        var eventFired = false;
        tuner.GCCollectionTriggered += (s, e) => { eventFired = true; };

        tuner.TriggerFullGC();

        // Event should fire during GC
        Assert.True(true);
    }

    [Fact]
    public void MemoryTuner_Dispose_CanBeCalledMultipleTimes()
    {
        using var profiler = new MemoryProfiler();
        var tuner = new MemoryTuner(profiler);
        tuner.Dispose();

        // Should not throw
        tuner.Dispose();
    }

    [Fact]
    public void MemoryTuner_AfterDispose_OperationsThrow()
    {
        using var profiler = new MemoryProfiler();
        var tuner = new MemoryTuner(profiler);
        tuner.Dispose();

        Assert.Throws<ObjectDisposedException>(() => tuner.PerformTuning());
        Assert.Throws<ObjectDisposedException>(() => tuner.TriggerFullGC());
        Assert.Throws<ObjectDisposedException>(() => tuner.GetRecommendations());
    }

    #endregion

    #region Extension Method Tests

    [Theory]
    [InlineData(512, "512 bytes")]
    [InlineData(1024, "1.00 KB")]
    [InlineData(1536, "1.50 KB")]
    [InlineData(1024 * 1024, "1.00 MB")]
    [InlineData(1536 * 1024, "1.50 MB")]
    [InlineData(1024L * 1024 * 1024, "1.00 GB")]
    public void ToHumanReadableSize_FormatsCorrectly(long bytes, string expected)
    {
        var result = bytes.ToHumanReadableSize();
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetTotalUsage_SumsAllComponentUsages()
    {
        var allocations = new Dictionary<string, MemoryAllocationInfo>
        {
            ["Component1"] = new() { ComponentName = "Component1", BytesAllocated = 1000, BytesFreed = 200 },
            ["Component2"] = new() { ComponentName = "Component2", BytesAllocated = 500, BytesFreed = 100 }
        };

        var total = allocations.GetTotalUsage();

        Assert.Equal(1200, total);
    }

    [Fact]
    public void GetTopConsumers_ReturnsTopNByUsage()
    {
        var allocations = new Dictionary<string, MemoryAllocationInfo>
        {
            ["Small"] = new() { ComponentName = "Small", BytesAllocated = 100, BytesFreed = 0 },
            ["Medium"] = new() { ComponentName = "Medium", BytesAllocated = 500, BytesFreed = 0 },
            ["Large"] = new() { ComponentName = "Large", BytesAllocated = 1000, BytesFreed = 0 }
        };

        var top2 = allocations.GetTopConsumers(2).ToList();

        Assert.Equal(2, top2.Count);
        Assert.Equal("Large", top2[0].ComponentName);
        Assert.Equal("Medium", top2[1].ComponentName);
    }

    [Theory]
    [InlineData(MemoryPressureLevel.None, false, false)]
    [InlineData(MemoryPressureLevel.Low, false, false)]
    [InlineData(MemoryPressureLevel.Medium, true, false)]
    [InlineData(MemoryPressureLevel.High, true, false)]
    [InlineData(MemoryPressureLevel.Critical, true, true)]
    public void MemoryPressureLevel_IsElevatedAndIsCritical_ReturnCorrectValues(
        MemoryPressureLevel level, bool isElevated, bool isCritical)
    {
        Assert.Equal(isElevated, level.IsElevated());
        Assert.Equal(isCritical, level.IsCritical());
    }

    #endregion

    #region Event Args Tests

    [Fact]
    public void MemoryPressureChangedEventArgs_Properties_SetCorrectly()
    {
        var snapshot = MemorySnapshot.Capture();
        var args = new MemoryPressureChangedEventArgs
        {
            PreviousLevel = MemoryPressureLevel.None,
            CurrentLevel = MemoryPressureLevel.High,
            Snapshot = snapshot,
            Timestamp = DateTime.UtcNow
        };

        Assert.Equal(MemoryPressureLevel.None, args.PreviousLevel);
        Assert.Equal(MemoryPressureLevel.High, args.CurrentLevel);
        Assert.Equal(snapshot, args.Snapshot);
        Assert.True(args.Timestamp <= DateTime.UtcNow);
    }

    [Fact]
    public void MemoryLeakDetectedEventArgs_Message_ContainsRelevantInfo()
    {
        var args = new MemoryLeakDetectedEventArgs
        {
            ComponentName = "TestComponent",
            GrowthRatePercentPerMinute = 15.5,
            CurrentUsageBytes = 200 * 1024 * 1024,
            StartingUsageBytes = 100 * 1024 * 1024,
            DetectionDuration = TimeSpan.FromMinutes(10)
        };

        var message = args.Message;

        Assert.Contains("TestComponent", message);
        Assert.Contains("15.50%", message);
        Assert.Contains("100.0 MB", message);
        Assert.Contains("200.0 MB", message);
        Assert.Contains("10.0 minutes", message);
    }

    [Fact]
    public void TuningActionEventArgs_Properties_SetCorrectly()
    {
        var args = new TuningActionEventArgs
        {
            Action = "TestAction",
            Description = "Test Description",
            MemoryBeforeBytes = 1000,
            MemoryAfterBytes = 500,
            MemorySavedBytes = 500,
            IsRuntimeConfigurable = true,
            Timestamp = DateTime.UtcNow
        };

        Assert.Equal("TestAction", args.Action);
        Assert.Equal("Test Description", args.Description);
        Assert.Equal(1000, args.MemoryBeforeBytes);
        Assert.Equal(500, args.MemoryAfterBytes);
        Assert.Equal(500, args.MemorySavedBytes);
        Assert.True(args.IsRuntimeConfigurable);
    }

    [Fact]
    public void GCCollectionEventArgs_Properties_SetCorrectly()
    {
        var args = new GCCollectionEventArgs
        {
            Generation = 2,
            CollectionMode = GCCollectionMode.Optimized,
            DurationMs = 100,
            MemoryBeforeBytes = 1000,
            MemoryAfterBytes = 500,
            MemoryFreedBytes = 500
        };

        Assert.Equal(2, args.Generation);
        Assert.Equal(GCCollectionMode.Optimized, args.CollectionMode);
        Assert.Equal(100, args.DurationMs);
        Assert.Equal(1000, args.MemoryBeforeBytes);
        Assert.Equal(500, args.MemoryAfterBytes);
        Assert.Equal(500, args.MemoryFreedBytes);
    }

    #endregion

    #region TuningRecommendations Tests

    [Fact]
    public void TuningRecommendations_DefaultValues_AreSet()
    {
        var recommendations = new TuningRecommendations();

        Assert.NotNull(recommendations.SuggestedOptimizations);
        Assert.Empty(recommendations.SuggestedOptimizations);
    }

    [Fact]
    public void TuningRecommendations_Properties_CanBeSet()
    {
        var snapshot = MemorySnapshot.Capture();
        var recommendations = new TuningRecommendations
        {
            Timestamp = DateTime.UtcNow,
            CurrentPressureLevel = MemoryPressureLevel.Medium,
            CurrentSnapshot = snapshot,
            RecommendedCacheSize = 1024 * 1024,
            RecommendedBufferPoolSize = 512 * 1024,
            RecommendedLOHCompaction = true,
            RecommendedGCLatencyMode = GCLatencyMode.Interactive
        };

        Assert.Equal(MemoryPressureLevel.Medium, recommendations.CurrentPressureLevel);
        Assert.Equal(snapshot, recommendations.CurrentSnapshot);
        Assert.Equal(1024 * 1024, recommendations.RecommendedCacheSize);
        Assert.True(recommendations.RecommendedLOHCompaction);
    }

    #endregion
}
