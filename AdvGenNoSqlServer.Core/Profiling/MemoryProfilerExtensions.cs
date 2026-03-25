// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using Microsoft.Extensions.DependencyInjection;

namespace AdvGenNoSqlServer.Core.Profiling;

/// <summary>
/// Extension methods for memory profiler integration.
/// </summary>
public static class MemoryProfilerExtensions
{
    /// <summary>
    /// Adds memory profiling services to the dependency injection container.
    /// </summary>
    public static IServiceCollection AddMemoryProfiling(
        this IServiceCollection services,
        Action<MemoryProfilerOptions>? configureOptions = null,
        Action<MemoryTuningOptions>? configureTuningOptions = null)
    {
        var options = new MemoryProfilerOptions();
        configureOptions?.Invoke(options);

        var tuningOptions = new MemoryTuningOptions();
        configureTuningOptions?.Invoke(tuningOptions);

        services.AddSingleton<IMemoryProfiler>(_ => new MemoryProfiler(options));
        services.AddSingleton<MemoryTuner>(sp => new MemoryTuner(sp.GetRequiredService<IMemoryProfiler>(), tuningOptions));

        return services;
    }

    /// <summary>
    /// Formats bytes to human-readable string.
    /// </summary>
    public static string ToHumanReadableSize(this long bytes)
    {
        const long KB = 1024;
        const long MB = KB * 1024;
        const long GB = MB * 1024;
        const long TB = GB * 1024;

        return bytes switch
        {
            >= TB => $"{bytes / (double)TB:F2} TB",
            >= GB => $"{bytes / (double)GB:F2} GB",
            >= MB => $"{bytes / (double)MB:F2} MB",
            >= KB => $"{bytes / (double)KB:F2} KB",
            _ => $"{bytes} bytes"
        };
    }

    /// <summary>
    /// Formats bytes to human-readable string.
    /// </summary>
    public static string ToHumanReadableSize(this int bytes) => ToHumanReadableSize((long)bytes);

    /// <summary>
    /// Gets a summary string of the memory snapshot.
    /// </summary>
    public static string GetSummary(this MemorySnapshot snapshot)
    {
        return $"Memory at {snapshot.Timestamp:yyyy-MM-dd HH:mm:ss}: " +
               $"Managed: {snapshot.TotalManagedMemory.ToHumanReadableSize()}, " +
               $"Working Set: {snapshot.WorkingSet.ToHumanReadableSize()}, " +
               $"LOH: {snapshot.LargeObjectHeapSize.ToHumanReadableSize()}, " +
               $"Load: {snapshot.MemoryLoadPercentage}%";
    }

    /// <summary>
    /// Gets a summary of memory allocation info.
    /// </summary>
    public static string GetSummary(this MemoryAllocationInfo info)
    {
        return $"{info.ComponentName}: {info.CurrentUsage.ToHumanReadableSize()} current, " +
               $"{info.PeakUsage.ToHumanReadableSize()} peak, " +
               $"{info.AllocationCount} allocs, {info.DeallocationCount} frees";
    }

    /// <summary>
    /// Calculates the total memory usage across all components.
    /// </summary>
    public static long GetTotalUsage(this IReadOnlyDictionary<string, MemoryAllocationInfo> allocations)
    {
        return allocations.Values.Sum(a => a.CurrentUsage);
    }

    /// <summary>
    /// Gets the top memory consumers.
    /// </summary>
    public static IEnumerable<MemoryAllocationInfo> GetTopConsumers(
        this IReadOnlyDictionary<string, MemoryAllocationInfo> allocations,
        int count = 10)
    {
        return allocations.Values
            .OrderByDescending(a => a.CurrentUsage)
            .Take(count);
    }

    /// <summary>
    /// Checks if memory pressure is elevated (Medium, High, or Critical).
    /// </summary>
    public static bool IsElevated(this MemoryPressureLevel level)
    {
        return level >= MemoryPressureLevel.Medium;
    }

    /// <summary>
    /// Checks if memory pressure is critical.
    /// </summary>
    public static bool IsCritical(this MemoryPressureLevel level)
    {
        return level == MemoryPressureLevel.Critical;
    }
}
