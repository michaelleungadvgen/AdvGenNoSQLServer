// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Clustering;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

public class ReadPreferenceTests
{
    #region ReadPreferenceMode Tests

    [Fact]
    public void ReadPreferenceMode_ShouldHaveExpectedValues()
    {
        // Assert
        Assert.Equal(0, (int)ReadPreferenceMode.Primary);
        Assert.Equal(1, (int)ReadPreferenceMode.PrimaryPreferred);
        Assert.Equal(2, (int)ReadPreferenceMode.Secondary);
        Assert.Equal(3, (int)ReadPreferenceMode.SecondaryPreferred);
        Assert.Equal(4, (int)ReadPreferenceMode.Nearest);
    }

    #endregion

    #region TagSet Tests

    [Fact]
    public void TagSet_Constructor_ShouldCreateEmptySet()
    {
        // Act
        var tagSet = new TagSet();

        // Assert
        Assert.Empty(tagSet);
    }

    [Fact]
    public void TagSet_Constructor_WithDictionary_ShouldCopyTags()
    {
        // Arrange
        var tags = new Dictionary<string, string>
        {
            ["region"] = "us-east",
            ["workload"] = "analytics"
        };

        // Act
        var tagSet = new TagSet(tags);

        // Assert
        Assert.Equal(2, tagSet.Count);
        Assert.Equal("us-east", tagSet["region"]);
        Assert.Equal("analytics", tagSet["workload"]);
    }

    [Fact]
    public void TagSet_Constructor_WithSingleTag_ShouldCreateSetWithOneTag()
    {
        // Act
        var tagSet = new TagSet("workload", "analytics");

        // Assert
        Assert.Single(tagSet);
        Assert.Equal("analytics", tagSet["workload"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TagSet_Matches_WithNullNodeTags_ShouldReturnExpected(bool emptyTagSet)
    {
        // Arrange
        var tagSet = emptyTagSet ? new TagSet() : new TagSet("key", "value");

        // Act
        var result = tagSet.Matches(null);

        // Assert
        Assert.Equal(emptyTagSet, result);
    }

    [Fact]
    public void TagSet_Matches_WithAllMatchingTags_ShouldReturnTrue()
    {
        // Arrange
        var tagSet = new TagSet("region", "us-east");
        tagSet.Add("workload", "analytics");

        var nodeTags = new Dictionary<string, string>
        {
            ["region"] = "us-east",
            ["workload"] = "analytics",
            ["extra"] = "value"
        };

        // Act
        var result = tagSet.Matches(nodeTags);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void TagSet_Matches_WithNonMatchingTagValue_ShouldReturnFalse()
    {
        // Arrange
        var tagSet = new TagSet("region", "us-east");
        var nodeTags = new Dictionary<string, string> { ["region"] = "us-west" };

        // Act
        var result = tagSet.Matches(nodeTags);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void TagSet_Matches_WithMissingTag_ShouldReturnFalse()
    {
        // Arrange
        var tagSet = new TagSet("region", "us-east");
        var nodeTags = new Dictionary<string, string> { ["other"] = "value" };

        // Act
        var result = tagSet.Matches(nodeTags);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void TagSet_Matches_IsCaseInsensitive()
    {
        // Arrange
        var tagSet = new TagSet("Region", "US-East");
        var nodeTags = new Dictionary<string, string> { ["region"] = "us-east" };

        // Act
        var result = tagSet.Matches(nodeTags);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void TagSet_Analytics_ShouldReturnCorrectTagSet()
    {
        // Act
        var tagSet = TagSet.Analytics;

        // Assert
        Assert.Single(tagSet);
        Assert.Equal("analytics", tagSet["workload"]);
    }

    [Fact]
    public void TagSet_Reporting_ShouldReturnCorrectTagSet()
    {
        // Act
        var tagSet = TagSet.Reporting;

        // Assert
        Assert.Single(tagSet);
        Assert.Equal("reporting", tagSet["workload"]);
    }

    #endregion

    #region ReadPreferenceOptions Tests

    [Fact]
    public void ReadPreferenceOptions_Default_ShouldHavePrimaryMode()
    {
        // Arrange
        var options = ReadPreferenceOptions.Primary;

        // Assert
        Assert.Equal(ReadPreferenceMode.Primary, options.Mode);
    }

    [Fact]
    public void ReadPreferenceOptions_SecondaryPreferred_ShouldHaveCorrectMode()
    {
        // Arrange
        var options = ReadPreferenceOptions.SecondaryPreferred;

        // Assert
        Assert.Equal(ReadPreferenceMode.SecondaryPreferred, options.Mode);
    }

    [Fact]
    public void ReadPreferenceOptions_Nearest_ShouldHaveLatencyBasedStrategy()
    {
        // Arrange
        var options = ReadPreferenceOptions.Nearest;

        // Assert
        Assert.Equal(ReadPreferenceMode.Nearest, options.Mode);
        Assert.Equal(NodeSelectionStrategy.LatencyBased, options.SelectionStrategy);
    }

    [Fact]
    public void ReadPreferenceOptions_DefaultValues_ShouldBeCorrect()
    {
        // Arrange
        var options = new ReadPreferenceOptions();

        // Assert
        Assert.Equal(ReadPreferenceMode.Primary, options.Mode);
        Assert.Empty(options.TagSets);
        Assert.Equal(0, options.MaxStalenessMs);
        Assert.Equal(NodeSelectionStrategy.RoundRobin, options.SelectionStrategy);
        Assert.Equal(5000, options.HealthCheckTimeoutMs);
        Assert.True(options.EnableHealthChecks);
    }

    #endregion

    #region ReadPreferenceResult Tests

    [Fact]
    public void ReadPreferenceResult_SuccessResult_ShouldCreateSuccessResult()
    {
        // Arrange
        var node = CreateNodeIdentity("test-node", true);

        // Act
        var result = ReadPreferenceResult.SuccessResult(node, ReadPreferenceMode.Primary, 5, 2, 10.5);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(node, result.SelectedNode);
        Assert.Equal(ReadPreferenceMode.Primary, result.Mode);
        Assert.Equal(5, result.NodesConsidered);
        Assert.Equal(2, result.NodesExcluded);
        Assert.Equal(10.5, result.LatencyMs);
    }

    [Fact]
    public void ReadPreferenceResult_FailureResult_ShouldCreateFailureResult()
    {
        // Act
        var result = ReadPreferenceResult.FailureResult(ReadPreferenceMode.Secondary, "No secondaries available", 3, 3);

        // Assert
        Assert.False(result.Success);
        Assert.Null(result.SelectedNode);
        Assert.Equal("No secondaries available", result.ErrorMessage);
        Assert.Equal(3, result.NodesConsidered);
        Assert.Equal(3, result.NodesExcluded);
    }

    #endregion

    #region ReadPreferenceStatistics Tests

    [Fact]
    public void ReadPreferenceStatistics_Reset_ShouldClearAllStatistics()
    {
        // Arrange
        var stats = new ReadPreferenceStatistics
        {
            TotalSelections = 100,
            SuccessfulSelections = 90,
            FailedSelections = 10,
            LastSelectionTime = DateTime.UtcNow
        };

        // Act
        stats.Reset();

        // Assert
        Assert.Equal(0, stats.TotalSelections);
        Assert.Equal(0, stats.SuccessfulSelections);
        Assert.Equal(0, stats.FailedSelections);
        Assert.Equal(0, stats.AverageSelectionLatencyMs);
        Assert.Null(stats.LastSelectionTime);
    }

    #endregion

    #region NodeReadInfo Tests

    [Fact]
    public void NodeReadInfo_DefaultValues_ShouldBeCorrect()
    {
        // Arrange
        var node = CreateNodeIdentity("test-node", true);
        var info = new NodeReadInfo { Node = node };

        // Assert
        Assert.False(info.IsPrimary);
        Assert.False(info.IsHealthy);
        Assert.Equal(0, info.ReplicationLagMs);
        Assert.Empty(info.Tags);
        Assert.Equal(0, info.LatencyMs);
        Assert.Equal(0, info.LoadFactor);
        Assert.Null(info.LastHealthCheck);
    }

    #endregion

    #region ReadPreferenceManager Tests

    [Fact]
    public void ReadPreferenceManager_Constructor_WithDefaults_ShouldInitializeCorrectly()
    {
        // Act
        using var manager = new ReadPreferenceManager();

        // Assert
        Assert.NotNull(manager.GetDefaultOptions());
        Assert.Equal(ReadPreferenceMode.Primary, manager.GetDefaultOptions().Mode);
    }

    [Fact]
    public void ReadPreferenceManager_Constructor_WithCustomDefaults_ShouldUseCustomOptions()
    {
        // Arrange
        var customOptions = new ReadPreferenceOptions { Mode = ReadPreferenceMode.Secondary };

        // Act
        using var manager = new ReadPreferenceManager(null, customOptions);

        // Assert
        Assert.Equal(ReadPreferenceMode.Secondary, manager.GetDefaultOptions().Mode);
    }

    [Fact]
    public async Task ReadPreferenceManager_SelectNodeAsync_PrimaryMode_WithPrimaryAvailable_ShouldSelectPrimary()
    {
        // Arrange
        using var manager = new ReadPreferenceManager();
        var primary = CreateNodeReadInfo("primary-1", true, true);
        var secondary = CreateNodeReadInfo("secondary-1", false, true);
        manager.RegisterNode(primary);
        manager.RegisterNode(secondary);

        var options = new ReadPreferenceOptions { Mode = ReadPreferenceMode.Primary };

        // Act
        var result = await manager.SelectNodeAsync(options);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(primary.Node.NodeId, result.SelectedNode!.NodeId);
        Assert.Equal(ReadPreferenceMode.Primary, result.Mode);
    }

    [Fact]
    public async Task ReadPreferenceManager_SelectNodeAsync_PrimaryMode_WithNoPrimary_ShouldFail()
    {
        // Arrange
        using var manager = new ReadPreferenceManager();
        var secondary = CreateNodeReadInfo("secondary-1", false, true);
        manager.RegisterNode(secondary);

        var options = new ReadPreferenceOptions { Mode = ReadPreferenceMode.Primary };

        // Act
        var result = await manager.SelectNodeAsync(options);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("No eligible nodes", result.ErrorMessage);
    }

    [Fact]
    public async Task ReadPreferenceManager_SelectNodeAsync_SecondaryMode_WithSecondaryAvailable_ShouldSelectSecondary()
    {
        // Arrange
        using var manager = new ReadPreferenceManager();
        var primary = CreateNodeReadInfo("primary-1", true, true);
        var secondary = CreateNodeReadInfo("secondary-1", false, true);
        manager.RegisterNode(primary);
        manager.RegisterNode(secondary);

        var options = new ReadPreferenceOptions { Mode = ReadPreferenceMode.Secondary };

        // Act
        var result = await manager.SelectNodeAsync(options);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(secondary.Node.NodeId, result.SelectedNode!.NodeId);
        Assert.Equal(ReadPreferenceMode.Secondary, result.Mode);
    }

    [Fact]
    public async Task ReadPreferenceManager_SelectNodeAsync_PrimaryPreferred_WithPrimaryAvailable_ShouldSelectPrimary()
    {
        // Arrange
        using var manager = new ReadPreferenceManager();
        var primary = CreateNodeReadInfo("primary-1", true, true);
        var secondary = CreateNodeReadInfo("secondary-1", false, true);
        manager.RegisterNode(primary);
        manager.RegisterNode(secondary);

        var options = new ReadPreferenceOptions { Mode = ReadPreferenceMode.PrimaryPreferred };

        // Act
        var result = await manager.SelectNodeAsync(options);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(primary.Node.NodeId, result.SelectedNode!.NodeId);
    }

    [Fact]
    public async Task ReadPreferenceManager_SelectNodeAsync_PrimaryPreferred_WithNoPrimary_ShouldFallbackToSecondary()
    {
        // Arrange
        using var manager = new ReadPreferenceManager();
        var secondary = CreateNodeReadInfo("secondary-1", false, true);
        manager.RegisterNode(secondary);

        var options = new ReadPreferenceOptions { Mode = ReadPreferenceMode.PrimaryPreferred };

        // Act
        var result = await manager.SelectNodeAsync(options);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(secondary.Node.NodeId, result.SelectedNode!.NodeId);
    }

    [Fact]
    public async Task ReadPreferenceManager_SelectNodeAsync_SecondaryPreferred_WithSecondaryAvailable_ShouldSelectSecondary()
    {
        // Arrange
        using var manager = new ReadPreferenceManager();
        var primary = CreateNodeReadInfo("primary-1", true, true);
        var secondary = CreateNodeReadInfo("secondary-1", false, true);
        manager.RegisterNode(primary);
        manager.RegisterNode(secondary);

        var options = new ReadPreferenceOptions { Mode = ReadPreferenceMode.SecondaryPreferred };

        // Act
        var result = await manager.SelectNodeAsync(options);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(secondary.Node.NodeId, result.SelectedNode!.NodeId);
    }

    [Fact]
    public async Task ReadPreferenceManager_SelectNodeAsync_SecondaryPreferred_WithNoSecondary_ShouldFallbackToPrimary()
    {
        // Arrange
        using var manager = new ReadPreferenceManager();
        var primary = CreateNodeReadInfo("primary-1", true, true);
        manager.RegisterNode(primary);

        var options = new ReadPreferenceOptions { Mode = ReadPreferenceMode.SecondaryPreferred };

        // Act
        var result = await manager.SelectNodeAsync(options);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(primary.Node.NodeId, result.SelectedNode!.NodeId);
    }

    [Fact]
    public async Task ReadPreferenceManager_SelectNodeAsync_WithTagSet_ShouldFilterByTags()
    {
        // Arrange
        using var manager = new ReadPreferenceManager();
        var analyticsNode = CreateNodeReadInfo("analytics-1", false, true, tags: new() { ["workload"] = "analytics" });
        var reportingNode = CreateNodeReadInfo("reporting-1", false, true, tags: new() { ["workload"] = "reporting" });
        manager.RegisterNode(analyticsNode);
        manager.RegisterNode(reportingNode);

        var options = new ReadPreferenceOptions
        {
            Mode = ReadPreferenceMode.Secondary,
            TagSets = { TagSet.Analytics }
        };

        // Act
        var result = await manager.SelectNodeAsync(options);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(analyticsNode.Node.NodeId, result.SelectedNode!.NodeId);
    }

    [Fact]
    public async Task ReadPreferenceManager_SelectNodeAsync_WithStalenessLimit_ShouldFilterStaleNodes()
    {
        // Arrange
        using var manager = new ReadPreferenceManager();
        var freshSecondary = CreateNodeReadInfo("fresh-1", false, true, replicationLagMs: 100);
        var staleSecondary = CreateNodeReadInfo("stale-1", false, true, replicationLagMs: 5000);
        manager.RegisterNode(freshSecondary);
        manager.RegisterNode(staleSecondary);

        var options = new ReadPreferenceOptions
        {
            Mode = ReadPreferenceMode.Secondary,
            MaxStalenessMs = 1000
        };

        // Act
        var result = await manager.SelectNodeAsync(options);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(freshSecondary.Node.NodeId, result.SelectedNode!.NodeId);
        Assert.Equal(1, result.NodesExcluded);
    }

    [Fact]
    public async Task ReadPreferenceManager_SelectNodeAsync_WithRandomStrategy_ShouldSelectRandomNode()
    {
        // Arrange
        using var manager = new ReadPreferenceManager();
        for (int i = 0; i < 5; i++)
        {
            manager.RegisterNode(CreateNodeReadInfo($"secondary-{i}", false, true));
        }

        var options = new ReadPreferenceOptions
        {
            Mode = ReadPreferenceMode.Secondary,
            SelectionStrategy = NodeSelectionStrategy.Random
        };

        // Act - Run multiple times to verify randomness
        var selectedNodes = new HashSet<string>();
        for (int i = 0; i < 20; i++)
        {
            var result = await manager.SelectNodeAsync(options);
            if (result.Success)
            {
                selectedNodes.Add(result.SelectedNode!.NodeId);
            }
        }

        // Assert - With random selection, we should see multiple different nodes over 20 iterations
        Assert.True(selectedNodes.Count > 1, "Random strategy should select different nodes");
    }

    [Fact]
    public async Task ReadPreferenceManager_SelectNodeAsync_WithLatencyStrategy_ShouldSelectLowestLatency()
    {
        // Arrange
        using var manager = new ReadPreferenceManager();
        var fastNode = CreateNodeReadInfo("fast-1", false, true, latencyMs: 10);
        var slowNode = CreateNodeReadInfo("slow-1", false, true, latencyMs: 100);
        manager.RegisterNode(fastNode);
        manager.RegisterNode(slowNode);

        var options = new ReadPreferenceOptions
        {
            Mode = ReadPreferenceMode.Secondary,
            SelectionStrategy = NodeSelectionStrategy.LatencyBased
        };

        // Act
        var result = await manager.SelectNodeAsync(options);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(fastNode.Node.NodeId, result.SelectedNode!.NodeId);
    }

    [Fact]
    public async Task ReadPreferenceManager_SelectNodeAsync_WithLoadStrategy_ShouldSelectLowestLoad()
    {
        // Arrange
        using var manager = new ReadPreferenceManager();
        var idleNode = CreateNodeReadInfo("idle-1", false, true, loadFactor: 0.1);
        var busyNode = CreateNodeReadInfo("busy-1", false, true, loadFactor: 0.9);
        manager.RegisterNode(idleNode);
        manager.RegisterNode(busyNode);

        var options = new ReadPreferenceOptions
        {
            Mode = ReadPreferenceMode.Secondary,
            SelectionStrategy = NodeSelectionStrategy.LoadBased
        };

        // Act
        var result = await manager.SelectNodeAsync(options);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(idleNode.Node.NodeId, result.SelectedNode!.NodeId);
    }

    [Fact]
    public async Task ReadPreferenceManager_SelectNodeAsync_WithNoOptions_ShouldUseDefaults()
    {
        // Arrange
        using var manager = new ReadPreferenceManager();
        var primary = CreateNodeReadInfo("primary-1", true, true);
        manager.RegisterNode(primary);

        // Act
        var result = await manager.SelectNodeAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Equal(primary.Node.NodeId, result.SelectedNode!.NodeId);
    }

    [Fact]
    public void ReadPreferenceManager_GetPrimaryNodeAsync_WithPrimaryAvailable_ShouldReturnPrimary()
    {
        // Arrange
        using var manager = new ReadPreferenceManager();
        var primary = CreateNodeReadInfo("primary-1", true, true);
        var secondary = CreateNodeReadInfo("secondary-1", false, true);
        manager.RegisterNode(primary);
        manager.RegisterNode(secondary);

        // Act
        var result = manager.GetPrimaryNodeAsync().Result;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(primary.Node.NodeId, result.NodeId);
    }

    [Fact]
    public void ReadPreferenceManager_GetSecondaryNodesAsync_WithSecondariesAvailable_ShouldReturnSecondaries()
    {
        // Arrange
        using var manager = new ReadPreferenceManager();
        var primary = CreateNodeReadInfo("primary-1", true, true);
        var secondary1 = CreateNodeReadInfo("secondary-1", false, true);
        var secondary2 = CreateNodeReadInfo("secondary-2", false, true);
        manager.RegisterNode(primary);
        manager.RegisterNode(secondary1);
        manager.RegisterNode(secondary2);

        // Act
        var result = manager.GetSecondaryNodesAsync().Result;

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(result, n => n.NodeId == secondary1.Node.NodeId);
        Assert.Contains(result, n => n.NodeId == secondary2.Node.NodeId);
    }

    [Fact]
    public void ReadPreferenceManager_UpdateNodeLatency_ShouldUpdateOrCreateNode()
    {
        // Arrange
        using var manager = new ReadPreferenceManager();
        var nodeId = Guid.NewGuid().ToString();

        // Act
        manager.UpdateNodeLatency(nodeId, 25.5);

        // Assert
        var nodes = manager.GetAvailableNodesAsync().Result;
        Assert.Single(nodes);
        Assert.Equal(25.5, nodes[0].LatencyMs);
    }

    [Fact]
    public void ReadPreferenceManager_UpdateNodeHealth_ShouldUpdateHealthStatus()
    {
        // Arrange
        using var manager = new ReadPreferenceManager();
        var node = CreateNodeReadInfo("test-1", false, true);
        manager.RegisterNode(node);

        // Act
        manager.UpdateNodeHealth(node.Node.NodeId.ToString(), false, 500);

        // Assert
        var nodes = manager.GetAvailableNodesAsync().Result;
        Assert.Empty(nodes); // Node is now unhealthy

        var allNodes = manager.GetAvailableNodesAsync(true).Result;
        Assert.Single(allNodes);
        Assert.False(allNodes[0].IsHealthy);
        Assert.Equal(500, allNodes[0].ReplicationLagMs);
    }

    [Fact]
    public void ReadPreferenceManager_GetStatistics_AfterSelections_ShouldReturnCorrectStats()
    {
        // Arrange
        using var manager = new ReadPreferenceManager();
        var primary = CreateNodeReadInfo("primary-1", true, true);
        var secondary = CreateNodeReadInfo("secondary-1", false, true);
        manager.RegisterNode(primary);
        manager.RegisterNode(secondary);

        // Act
        manager.SelectNodeAsync(new ReadPreferenceOptions { Mode = ReadPreferenceMode.Primary }).Wait();
        manager.SelectNodeAsync(new ReadPreferenceOptions { Mode = ReadPreferenceMode.Secondary }).Wait();
        manager.SelectNodeAsync(new ReadPreferenceOptions { Mode = ReadPreferenceMode.Primary }).Wait();

        var stats = manager.GetStatistics();

        // Assert
        Assert.Equal(3, stats.TotalSelections);
        Assert.Equal(3, stats.SuccessfulSelections);
        Assert.Equal(0, stats.FailedSelections);
        Assert.Equal(2, stats.PrimarySelections);
        Assert.Equal(1, stats.SecondarySelections);
    }

    [Fact]
    public void ReadPreferenceManager_ResetStatistics_ShouldClearStatistics()
    {
        // Arrange
        using var manager = new ReadPreferenceManager();
        var primary = CreateNodeReadInfo("primary-1", true, true);
        manager.RegisterNode(primary);
        manager.SelectNodeAsync().Wait();

        // Act
        manager.ResetStatistics();
        var stats = manager.GetStatistics();

        // Assert
        Assert.Equal(0, stats.TotalSelections);
        Assert.Equal(0, stats.SuccessfulSelections);
    }

    [Fact]
    public void ReadPreferenceManager_SetDefaultOptions_ShouldUpdateDefaults()
    {
        // Arrange
        using var manager = new ReadPreferenceManager();
        var newOptions = new ReadPreferenceOptions { Mode = ReadPreferenceMode.Secondary };

        // Act
        manager.SetDefaultOptions(newOptions);
        var result = manager.GetDefaultOptions();

        // Assert
        Assert.Equal(ReadPreferenceMode.Secondary, result.Mode);
    }

    [Fact]
    public void ReadPreferenceManager_RegisterNode_ShouldAddNode()
    {
        // Arrange
        using var manager = new ReadPreferenceManager();
        var node = CreateNodeReadInfo("test-1", false, true);

        // Act
        manager.RegisterNode(node);
        var nodes = manager.GetAvailableNodesAsync(true).Result;

        // Assert
        Assert.Single(nodes);
        Assert.Equal(node.Node.NodeId, nodes[0].Node.NodeId);
    }

    [Fact]
    public void ReadPreferenceManager_UnregisterNode_ShouldRemoveNode()
    {
        // Arrange
        using var manager = new ReadPreferenceManager();
        var node = CreateNodeReadInfo("test-1", false, true);
        manager.RegisterNode(node);

        // Act
        var removed = manager.UnregisterNode(node.Node.NodeId.ToString());
        var nodes = manager.GetAvailableNodesAsync(true).Result;

        // Assert
        Assert.True(removed);
        Assert.Empty(nodes);
    }

    [Fact]
    public void ReadPreferenceManager_UnregisterNode_NonExistentNode_ShouldReturnFalse()
    {
        // Arrange
        using var manager = new ReadPreferenceManager();

        // Act
        var removed = manager.UnregisterNode("non-existent");

        // Assert
        Assert.False(removed);
    }

    [Fact]
    public void ReadPreferenceManager_NodeSelectedEvent_ShouldFireOnSuccessfulSelection()
    {
        // Arrange
        using var manager = new ReadPreferenceManager();
        var primary = CreateNodeReadInfo("primary-1", true, true);
        manager.RegisterNode(primary);

        NodeSelectedEventArgs? capturedEvent = null;
        manager.NodeSelected += (sender, e) => capturedEvent = e;

        // Act
        manager.SelectNodeAsync().Wait();

        // Assert
        Assert.NotNull(capturedEvent);
        Assert.Equal(primary.Node.NodeId, capturedEvent.Node.NodeId);
        Assert.True(capturedEvent.IsPrimary);
    }

    [Fact]
    public void ReadPreferenceManager_NodeSelectionFailedEvent_ShouldFireOnFailedSelection()
    {
        // Arrange
        using var manager = new ReadPreferenceManager();
        // No nodes registered

        NodeSelectionFailedEventArgs? capturedEvent = null;
        manager.NodeSelectionFailed += (sender, e) => capturedEvent = e;

        // Act
        manager.SelectNodeAsync().Wait();

        // Assert
        Assert.NotNull(capturedEvent);
        Assert.Contains("No eligible nodes", capturedEvent.ErrorMessage);
    }

    [Fact]
    public void ReadPreferenceManager_SetDefaultOptions_NullOptions_ShouldThrow()
    {
        // Arrange
        using var manager = new ReadPreferenceManager();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => manager.SetDefaultOptions(null!));
    }

    [Fact]
    public void ReadPreferenceManager_RegisterNode_NullNodeInfo_ShouldThrow()
    {
        // Arrange
        using var manager = new ReadPreferenceManager();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => manager.RegisterNode(null!));
    }

    [Fact]
    public void ReadPreferenceManager_Dispose_ShouldMarkAsDisposed()
    {
        // Arrange
        var manager = new ReadPreferenceManager();

        // Act
        manager.Dispose();

        // Assert
        Assert.Throws<ObjectDisposedException>(() => manager.SelectNodeAsync().Result);
    }

    #endregion

    #region Helper Methods

    private static NodeIdentity CreateNodeIdentity(string name, bool isSeed)
    {
        return new NodeIdentity
        {
            NodeId = Guid.NewGuid().ToString(),
            ClusterId = "test-cluster",
            Host = "localhost",
            Port = 9090,
            P2PPort = 9091,
            Tags = new[] { $"name:{name}" }
        };
    }

    private static NodeReadInfo CreateNodeReadInfo(
        string name,
        bool isPrimary,
        bool isHealthy,
        long replicationLagMs = 0,
        double latencyMs = 0,
        double loadFactor = 0,
        Dictionary<string, string>? tags = null)
    {
        return new NodeReadInfo
        {
            Node = new NodeIdentity
            {
                NodeId = Guid.NewGuid().ToString(),
                ClusterId = "test-cluster",
                Host = "localhost",
                Port = 9090,
                P2PPort = 9091,
                Tags = new[] { $"name:{name}" }
            },
            IsPrimary = isPrimary,
            IsHealthy = isHealthy,
            ReplicationLagMs = replicationLagMs,
            LatencyMs = latencyMs,
            LoadFactor = loadFactor,
            Tags = tags ?? new Dictionary<string, string>(),
            LastHealthCheck = DateTime.UtcNow
        };
    }

    #endregion
}
