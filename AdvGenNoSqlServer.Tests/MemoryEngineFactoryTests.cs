// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
using AdvGenNoSqlServer.Core.MemoryManagement;
using Microsoft.Extensions.DependencyInjection;

namespace AdvGenNoSqlServer.Tests;

public class MemoryEngineFactoryTests
{
    [Fact]
    public void AddMemoryEngine_NativePlan_RegistersNativeEngine()
    {
        var services = new ServiceCollection();
        services.AddMemoryEngine(new MemoryManagementConfiguration { Plan = "Native", MaxMemoryMB = 64 });
        var sp = services.BuildServiceProvider();
        var engine = sp.GetRequiredService<IMemoryStorageEngine>();
        Assert.IsType<NativeMemoryStorageEngine>(engine);
        (engine as IDisposable)?.Dispose();
    }

    [Fact]
    public void AddMemoryEngine_ManagedPlan_RegistersManagedEngine()
    {
        var services = new ServiceCollection();
        services.AddMemoryEngine(new MemoryManagementConfiguration { Plan = "Managed", MaxMemoryMB = 64 });
        var sp = services.BuildServiceProvider();
        var engine = sp.GetRequiredService<IMemoryStorageEngine>();
        Assert.IsType<ManagedMemoryStorageEngine>(engine);
        (engine as IDisposable)?.Dispose();
    }

    [Fact]
    public void AddMemoryEngine_UnknownPlan_FallsBackToManaged()
    {
        var services = new ServiceCollection();
        services.AddMemoryEngine(new MemoryManagementConfiguration { Plan = "Bogus", MaxMemoryMB = 64 });
        var sp = services.BuildServiceProvider();
        var engine = sp.GetRequiredService<IMemoryStorageEngine>();
        Assert.IsType<ManagedMemoryStorageEngine>(engine);
        (engine as IDisposable)?.Dispose();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AddMemoryEngine_MaxMemoryMBInvalid_Throws(int mb)
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() =>
            services.AddMemoryEngine(new MemoryManagementConfiguration { MaxMemoryMB = mb }));
    }

    [Fact]
    public void AddMemoryEngine_NegativeDefaultTtl_Throws()
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() =>
            services.AddMemoryEngine(new MemoryManagementConfiguration
                { MaxMemoryMB = 64, DefaultTtlSeconds = -1 }));
    }

    [Fact]
    public void AddMemoryEngine_MaxMemoryPercentZero_UsesOnlyMBLimit()
    {
        var services = new ServiceCollection();
        services.AddMemoryEngine(new MemoryManagementConfiguration
            { Plan = "Managed", MaxMemoryMB = 128, MaxMemoryPercent = 0 });
        var sp = services.BuildServiceProvider();
        var engine = sp.GetRequiredService<IMemoryStorageEngine>();
        var stats = engine.GetStats();
        Assert.Equal(128L * 1024 * 1024, stats.LimitBytes);
        (engine as IDisposable)?.Dispose();
    }
}
