// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
using AdvGenNoSqlServer.Core.MemoryManagement;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Verifies full startup per plan: factory builds the right engine, stats are non-zero
/// after use, DefaultTtlSeconds=0 doesn't crash.
/// </summary>
public class MemoryManagementIntegrationTests
{
    [Theory]
    [InlineData("Managed")]
    [InlineData("Native")]
    public void StartupPerPlan_GetStats_ReturnsNonNullPlan(string plan)
    {
        var services = new ServiceCollection();
        services.AddMemoryEngine(new MemoryManagementConfiguration
        {
            Plan = plan,
            MaxMemoryMB = 16,
            MaxMemoryPercent = 0,
            DefaultTtlSeconds = 0
        });
        var sp = services.BuildServiceProvider();

        using var engine = sp.GetRequiredService<IMemoryStorageEngine>();
        engine.Set("hello", new byte[] { 1, 2, 3 });
        engine.TryGet("hello", out _);

        var stats = engine.GetStats();
        Assert.Equal(plan, stats.Plan);
        Assert.True(stats.EntryCount >= 1);
        Assert.True(stats.HitCount >= 1);
    }

    [Fact]
    public void DefaultTtlSecondsZero_DoesNotCrash()
    {
        var services = new ServiceCollection();
        services.AddMemoryEngine(new MemoryManagementConfiguration
        {
            Plan = "Managed",
            MaxMemoryMB = 16,
            DefaultTtlSeconds = 0
        });
        var sp = services.BuildServiceProvider();
        using var engine = sp.GetRequiredService<IMemoryStorageEngine>();
        engine.Set("k", new byte[] { 99 });
        Assert.True(engine.TryGet("k", out _));
    }

    [Fact]
    public void UnknownPlan_FallsBackToManaged_NoException()
    {
        var services = new ServiceCollection();
        services.AddMemoryEngine(new MemoryManagementConfiguration
        {
            Plan = "Quantum",
            MaxMemoryMB = 16
        });
        var sp = services.BuildServiceProvider();
        using var engine = sp.GetRequiredService<IMemoryStorageEngine>();
        Assert.IsType<ManagedMemoryStorageEngine>(engine);
    }

    [Fact]
    public void MixedPlan_WithMockedDocumentStore_GetStats_ReturnsMixedPlan()
    {
        var storeMock = new Mock<AdvGenNoSqlServer.Core.Abstractions.IDocumentStore>();
        storeMock.Setup(s => s.CountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0L);
        storeMock.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AdvGenNoSqlServer.Core.Models.Document?)null);
        storeMock.Setup(s => s.InsertAsync(It.IsAny<string>(), It.IsAny<AdvGenNoSqlServer.Core.Models.Document>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, AdvGenNoSqlServer.Core.Models.Document d, CancellationToken _) => d);
        storeMock.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        storeMock.Setup(s => s.ClearCollectionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(storeMock.Object);
        services.AddMemoryEngine(new MemoryManagementConfiguration
        {
            Plan = "Mixed",
            MaxMemoryMB = 32,
            MaxMemoryPercent = 0,
            Mixed = new MixedTierConfiguration { HotTierMaxMB = 8, SpillCollection = "_int_cold" }
        });
        var sp = services.BuildServiceProvider();

        using var engine = sp.GetRequiredService<IMemoryStorageEngine>();
        engine.Set("test", new byte[] { 1, 2, 3 });
        engine.TryGet("test", out _);

        var stats = engine.GetStats();
        Assert.Equal("Mixed", stats.Plan);
        Assert.True(stats.HitCount >= 1);
    }
}
