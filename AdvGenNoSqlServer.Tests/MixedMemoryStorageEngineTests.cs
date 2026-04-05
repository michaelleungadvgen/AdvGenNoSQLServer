// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
using AdvGenNoSqlServer.Core.Abstractions;
using AdvGenNoSqlServer.Core.MemoryManagement;
using AdvGenNoSqlServer.Core.Models;
using Moq;

namespace AdvGenNoSqlServer.Tests;

public class MixedMemoryStorageEngineTests : IDisposable
{
    private readonly Mock<IDocumentStore> _storeMock = new();
    private readonly MemoryManagementConfiguration _config = new()
    {
        Plan = "Mixed",
        MaxMemoryMB = 32,
        DefaultTtlSeconds = 0,
        Mixed = new MixedTierConfiguration { HotTierMaxMB = 4, SpillCollection = "_test_cold" }
    };
    private readonly MixedMemoryStorageEngine _engine;

    public MixedMemoryStorageEngineTests()
    {
        _storeMock.Setup(s => s.InsertAsync(It.IsAny<string>(), It.IsAny<Document>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string col, Document doc, CancellationToken ct) => doc);
        _storeMock.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Document?)null);
        _storeMock.Setup(s => s.GetAllAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<Document>());
        _storeMock.Setup(s => s.CountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0L);
        _storeMock.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _storeMock.Setup(s => s.ClearCollectionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _engine = new MixedMemoryStorageEngine(_config, 32L * 1024 * 1024, _storeMock.Object);
    }

    public void Dispose() => _engine.Dispose();

    [Fact]
    public void Set_ThenTryGet_ReturnsFromHotTier()
    {
        byte[] data = [1, 2, 3];
        _engine.Set("hot", data);
        bool found = _engine.TryGet("hot", out var result);
        Assert.True(found);
        Assert.Equal(data, result.ToArray());
        _storeMock.Verify(s => s.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void TryGet_ColdHit_ReturnsData()
    {
        var coldDoc = new Document
        {
            Id = "cold-key",
            Data = new Dictionary<string, object?> { ["_value"] = "AQID", ["_expiry"] = 0L }
        };
        _storeMock.Setup(s => s.GetAsync("_test_cold", "cold-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(coldDoc);

        bool found = _engine.TryGet("cold-key", out _);
        Assert.True(found);
    }

    [Fact]
    public void TryGet_ColdExpiredEntry_ReturnsMiss()
    {
        long pastMs = DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeMilliseconds();
        var expiredDoc = new Document
        {
            Id = "expired",
            Data = new Dictionary<string, object?> { ["_value"] = "AQ==", ["_expiry"] = pastMs }
        };
        _storeMock.Setup(s => s.GetAsync("_test_cold", "expired", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expiredDoc);

        bool found = _engine.TryGet("expired", out _);
        Assert.False(found);
    }

    [Fact]
    public void GetStats_ReturnsMixedPlan()
    {
        var stats = _engine.GetStats();
        Assert.Equal("Mixed", stats.Plan);
    }
}
