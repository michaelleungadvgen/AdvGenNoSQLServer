// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AdvGenNoSqlServer.Core.MemoryManagement;

public static class MemoryEngineFactory
{
    public static IServiceCollection AddMemoryEngine(
        this IServiceCollection services,
        MemoryManagementConfiguration config)
    {
        ValidateConfig(config);
        long effectiveLimit = ComputeEffectiveLimit(config);

        services.AddSingleton<IMemoryStorageEngine>(sp => config.Plan switch
        {
            "Native"  => (IMemoryStorageEngine)new NativeMemoryStorageEngine(config, effectiveLimit),
            "Mixed"   => throw new NotImplementedException("Mixed plan not yet implemented."),
            "Managed" => new ManagedMemoryStorageEngine(config, effectiveLimit),
            _         => FallbackToManaged(config, effectiveLimit, sp)
        });

        return services;
    }

    private static IMemoryStorageEngine FallbackToManaged(
        MemoryManagementConfiguration config, long limit, IServiceProvider sp)
    {
        var loggerFactory = sp.GetService<ILoggerFactory>();
        var logger = loggerFactory?.CreateLogger(nameof(MemoryEngineFactory));
        logger?.LogWarning(
            "Unknown MemoryManagement.Plan '{Plan}'. Falling back to Managed.", config.Plan);
        return new ManagedMemoryStorageEngine(config, limit);
    }

    internal static long ComputeEffectiveLimit(MemoryManagementConfiguration config)
    {
        long mbLimit = (long)config.MaxMemoryMB * 1_048_576;
        if (config.MaxMemoryPercent <= 0)
            return Math.Max(mbLimit, 1_048_576);

        long ramLimit = (long)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes
                        * config.MaxMemoryPercent / 100.0);
        return Math.Max(Math.Min(mbLimit, ramLimit), 1_048_576);
    }

    private static void ValidateConfig(MemoryManagementConfiguration config)
    {
        if (config.MaxMemoryMB <= 0)
            throw new InvalidOperationException(
                $"MemoryManagement.MaxMemoryMB must be > 0, got {config.MaxMemoryMB}.");

        if (config.DefaultTtlSeconds < 0)
            throw new InvalidOperationException(
                $"MemoryManagement.DefaultTtlSeconds must be >= 0, got {config.DefaultTtlSeconds}.");

        if (config.Plan == "Mixed")
        {
            if (config.Mixed.HotTierMaxMB <= 0)
                throw new InvalidOperationException(
                    "MemoryManagement.Mixed.HotTierMaxMB must be > 0.");
            if (config.Mixed.HotTierMaxMB >= config.MaxMemoryMB)
                throw new InvalidOperationException(
                    "MemoryManagement.Mixed.HotTierMaxMB must be less than MaxMemoryMB.");
        }
    }
}
