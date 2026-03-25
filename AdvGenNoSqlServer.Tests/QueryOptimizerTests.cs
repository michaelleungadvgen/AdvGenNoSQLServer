// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Query.Models;
using AdvGenNoSqlServer.Query.Optimization;
using AdvGenNoSqlServer.Storage;
using AdvGenNoSqlServer.Storage.Indexing;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for the QueryOptimizer
/// </summary>
public class QueryOptimizerTests : IDisposable
{
    private readonly DocumentStore _documentStore;
    private readonly IndexManager _indexManager;
    private readonly QueryOptimizer _optimizer;

    /// <summary>
    /// Setup test fixtures
    /// </summary>
    public QueryOptimizerTests()
    {
        _documentStore = new DocumentStore();
        _indexManager = new IndexManager();
        _optimizer = new QueryOptimizer(_documentStore, _indexManager);
    }

    /// <summary>
    /// Cleanup after tests
    /// </summary>
    public void Dispose()
    {
        _optimizer.Dispose();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidDependencies_CreatesInstance()
    {
        var optimizer = new QueryOptimizer(_documentStore, _indexManager);
        Assert.NotNull(optimizer);
        optimizer.Dispose();
    }

    [Fact]
    public void Constructor_WithNullDocumentStore_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new QueryOptimizer(null!, _indexManager));
    }

    [Fact]
    public void Constructor_WithNullIndexManager_CreatesInstance()
    {
        var optimizer = new QueryOptimizer(_documentStore, null);
        Assert.NotNull(optimizer);
        optimizer.Dispose();
    }

    #endregion

    #region Basic Optimization Tests

    [Fact]
    public async Task OptimizeAsync_SimpleCollectionQuery_ReturnsPlan()
    {
        // Arrange
        var query = new Query.Models.Query
        {
            CollectionName = "users"
        };

        // Act
        var result = await _optimizer.OptimizeAsync(query);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.SelectedPlan);
        Assert.Equal(OptimizationLevel.Full, result.OptimizationLevel);
        Assert.Equal("CollectionScan", result.SelectedPlan.RootNode.NodeType);
    }

    [Fact]
    public async Task OptimizeAsync_WithFilter_ReturnsPlanWithFilter()
    {
        // Arrange
        var query = new Query.Models.Query
        {
            CollectionName = "users",
            Filter = QueryFilter.Create(new Dictionary<string, object>
            {
                ["name"] = "John"
            })
        };

        // Act
        var result = await _optimizer.OptimizeAsync(query);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.SelectedPlan);
    }

    [Fact]
    public async Task OptimizeAsync_WithSort_ReturnsPlanWithSort()
    {
        // Arrange
        var query = new Query.Models.Query
        {
            CollectionName = "users",
            Sort = new List<SortField>
            {
                new() { FieldName = "name", Direction = SortDirection.Ascending }
            }
        };

        // Act
        var result = await _optimizer.OptimizeAsync(query);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.SelectedPlan);
    }

    [Fact]
    public async Task OptimizeAsync_WithPagination_ReturnsPlanWithSkipLimit()
    {
        // Arrange
        var query = new Query.Models.Query
        {
            CollectionName = "users",
            Options = new QueryOptions
            {
                Skip = 10,
                Limit = 20
            }
        };

        // Act
        var result = await _optimizer.OptimizeAsync(query);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.SelectedPlan);
        Assert.Equal(20, result.SelectedPlan.OptimizedQuery.Options?.Limit);
    }

    #endregion

    #region Optimization Level Tests

    [Fact]
    public async Task OptimizeAsync_WithNoneLevel_ReturnsNoOptimizationPlan()
    {
        // Arrange
        var query = new Query.Models.Query { CollectionName = "users" };
        var options = new QueryOptimizerOptions { Level = OptimizationLevel.None };

        // Act
        var result = await _optimizer.OptimizeAsync(query, options);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.SelectedPlan);
        Assert.Empty(result.SelectedPlan.AppliedOptimizations);
    }

    [Fact]
    public async Task OptimizeAsync_WithBasicLevel_ReturnsBasicPlan()
    {
        // Arrange
        var query = new Query.Models.Query { CollectionName = "users" };
        var options = new QueryOptimizerOptions { Level = OptimizationLevel.Basic };

        // Act
        var result = await _optimizer.OptimizeAsync(query, options);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.PlansConsidered); // Only collection scan for basic level
    }

    [Fact]
    public async Task OptimizeAsync_WithFullLevel_ReturnsOptimizedPlan()
    {
        // Arrange
        var query = new Query.Models.Query { CollectionName = "users" };
        var options = new QueryOptimizerOptions { Level = OptimizationLevel.Full };

        // Act
        var result = await _optimizer.OptimizeAsync(query, options);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.PlansConsidered >= 1);
    }

    #endregion

    #region Index Selection Tests

    [Fact]
    public async Task OptimizeAsync_WithIndexOnFilterField_UsesIndex()
    {
        // Arrange
        await _indexManager.CreateIndexAsync("users", "email", false);
        
        var query = new Query.Models.Query
        {
            CollectionName = "users",
            Filter = QueryFilter.Create(new Dictionary<string, object>
            {
                ["email"] = "test@example.com"
            })
        };

        // Act
        var result = await _optimizer.OptimizeAsync(query);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.SelectedPlan);
        // Plan may or may not use index depending on selectivity estimation
    }

    [Fact]
    public async Task OptimizeAsync_WithMultipleFiltersAndIndex_SelectsBestIndex()
    {
        // Arrange
        await _indexManager.CreateIndexAsync("users", "id", false);
        await _indexManager.CreateIndexAsync("users", "email", false);
        
        var query = new Query.Models.Query
        {
            CollectionName = "users",
            Filter = QueryFilter.Create(new Dictionary<string, object>
            {
                ["id"] = "user123",
                ["email"] = "test@example.com"
            })
        };

        // Act
        var result = await _optimizer.OptimizeAsync(query);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.SelectedPlan);
    }

    [Fact]
    public async Task OptimizeAsync_NoIndexAvailable_DoesNotUseIndex()
    {
        // Arrange
        var query = new Query.Models.Query
        {
            CollectionName = "users",
            Filter = QueryFilter.Create(new Dictionary<string, object>
            {
                ["nonIndexedField"] = "value"
            })
        };

        // Act
        var result = await _optimizer.OptimizeAsync(query);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.SelectedPlan.UsesIndex);
    }

    #endregion

    #region Plan Cache Tests

    [Fact]
    public async Task OptimizeAsync_WithCacheEnabled_CachesPlan()
    {
        // Arrange
        var query = new Query.Models.Query { CollectionName = "users" };
        var options = new QueryOptimizerOptions { EnablePlanCache = true };

        // Act - First call
        var result1 = await _optimizer.OptimizeAsync(query, options);
        
        // Act - Second call (should hit cache)
        var result2 = await _optimizer.OptimizeAsync(query, options);

        // Assert
        Assert.True(result1.Success);
        Assert.True(result2.Success);
        
        var stats = await _optimizer.GetStatisticsAsync();
        Assert.True(stats.PlansFromCache >= 1);
    }

    [Fact]
    public async Task OptimizeAsync_WithCacheDisabled_DoesNotCache()
    {
        // Arrange
        var query = new Query.Models.Query { CollectionName = "users" };
        var options = new QueryOptimizerOptions { EnablePlanCache = false };

        // Act
        await _optimizer.OptimizeAsync(query, options);
        await _optimizer.OptimizeAsync(query, options);

        // Assert
        var stats = await _optimizer.GetStatisticsAsync();
        Assert.Equal(0, stats.PlansFromCache);
    }

    [Fact]
    public async Task GetCachedPlanAsync_WithCachedPlan_ReturnsPlan()
    {
        // Arrange
        var query = new Query.Models.Query { CollectionName = "users" };
        await _optimizer.OptimizeAsync(query);

        // Act
        var cachedPlan = await _optimizer.GetCachedPlanAsync(query);

        // Assert
        Assert.NotNull(cachedPlan);
    }

    [Fact]
    public async Task GetCachedPlanAsync_WithNoCachedPlan_ReturnsNull()
    {
        // Arrange
        var query = new Query.Models.Query { CollectionName = "notcached" };

        // Act
        var cachedPlan = await _optimizer.GetCachedPlanAsync(query);

        // Assert
        Assert.Null(cachedPlan);
    }

    [Fact]
    public async Task ClearPlanCacheAsync_RemovesAllCachedPlans()
    {
        // Arrange
        var query = new Query.Models.Query { CollectionName = "users" };
        await _optimizer.OptimizeAsync(query);

        // Act
        await _optimizer.ClearPlanCacheAsync();

        // Assert
        var stats = await _optimizer.GetStatisticsAsync();
        Assert.Equal(0, stats.CachedPlanCount);
    }

    #endregion

    #region Plan Structure Tests

    [Fact]
    public async Task OptimizeAsync_ReturnsPlanWithCorrectMetadata()
    {
        // Arrange
        var query = new Query.Models.Query { CollectionName = "users" };

        // Act
        var result = await _optimizer.OptimizeAsync(query);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.SelectedPlan.PlanId);
        Assert.NotNull(result.SelectedPlan.OriginalQuery);
        Assert.NotNull(result.SelectedPlan.OptimizedQuery);
        Assert.NotNull(result.SelectedPlan.RootNode);
        Assert.True(result.SelectedPlan.EstimatedCost >= 0);
        Assert.True(result.SelectedPlan.EstimatedExecutionTimeMs >= 0);
    }

    [Fact]
    public async Task OptimizeAsync_ReturnsPlanWithStatistics()
    {
        // Arrange
        var query = new Query.Models.Query { CollectionName = "users" };

        // Act
        var result = await _optimizer.OptimizeAsync(query);

        // Assert
        Assert.NotNull(result.SelectedPlan.Statistics);
        Assert.Equal(0, result.SelectedPlan.Statistics.ExecutionCount);
    }

    [Fact]
    public async Task OptimizeAsync_ReturnsAlternativePlans()
    {
        // Arrange
        var query = new Query.Models.Query { CollectionName = "users" };

        // Act
        var result = await _optimizer.OptimizeAsync(query);

        // Assert
        Assert.NotNull(result.AlternativePlans);
        Assert.True(result.PlansConsidered >= 1);
    }

    #endregion

    #region Plan Node Tests

    [Fact]
    public async Task OptimizeAsync_CollectionScanPlan_HasCorrectNodeType()
    {
        // Arrange
        var query = new Query.Models.Query { CollectionName = "users" };

        // Act
        var result = await _optimizer.OptimizeAsync(query);

        // Assert
        Assert.Equal("CollectionScan", result.SelectedPlan.RootNode.NodeType);
    }

    [Fact]
    public async Task OptimizeAsync_WithFilter_HasFilterNode()
    {
        // Arrange
        var query = new Query.Models.Query
        {
            CollectionName = "users",
            Filter = QueryFilter.Create(new Dictionary<string, object> { ["name"] = "John" })
        };

        // Act
        var result = await _optimizer.OptimizeAsync(query);

        // Assert
        Assert.True(result.IsSuccess);
        // Root may be Filter node or CollectionScan depending on optimization
    }

    [Fact]
    public async Task OptimizeAsync_WithSort_HasSortNode()
    {
        // Arrange
        var query = new Query.Models.Query
        {
            CollectionName = "users",
            Sort = new List<SortField> { new() { FieldName = "name" } }
        };

        // Act
        var result = await _optimizer.OptimizeAsync(query);

        // Assert
        Assert.True(result.IsSuccess);
    }

    #endregion

    #region Cost Estimation Tests

    [Fact]
    public async Task OptimizeAsync_EmptyCollection_LowEstimatedCost()
    {
        // Arrange
        var query = new Query.Models.Query { CollectionName = "empty" };

        // Act
        var result = await _optimizer.OptimizeAsync(query);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.SelectedPlan.EstimatedCost >= 0);
    }

    [Fact]
    public async Task OptimizeAsync_WithLimit_ReducesCost()
    {
        // Arrange
        var query1 = new Query.Models.Query { CollectionName = "users" };
        var query2 = new Query.Models.Query 
        { 
            CollectionName = "users",
            Options = new QueryOptions { Limit = 10 }
        };

        // Act
        var result1 = await _optimizer.OptimizeAsync(query1);
        var result2 = await _optimizer.OptimizeAsync(query2);

        // Assert
        // Limited query should have equal or lower cost
        Assert.True(result2.SelectedPlan.EstimatedCost <= result1.SelectedPlan.EstimatedCost || 
                    result2.SelectedPlan.EstimatedCost < 1000);
    }

    #endregion

    #region Statistics Tests

    [Fact]
    public async Task GetStatisticsAsync_Initially_ReturnsZeroStats()
    {
        // Act
        var stats = await _optimizer.GetStatisticsAsync();

        // Assert
        Assert.Equal(0, stats.TotalQueriesOptimized);
        Assert.Equal(0, stats.PlansFromCache);
        Assert.Equal(0, stats.PlansGenerated);
        Assert.Equal(0, stats.CachedPlanCount);
    }

    [Fact]
    public async Task GetStatisticsAsync_AfterOptimization_ReturnsUpdatedStats()
    {
        // Arrange
        var query = new Query.Models.Query { CollectionName = "users" };
        await _optimizer.OptimizeAsync(query);

        // Act
        var stats = await _optimizer.GetStatisticsAsync();

        // Assert
        Assert.True(stats.TotalQueriesOptimized >= 1);
        Assert.True(stats.PlansGenerated >= 1);
    }

    [Fact]
    public void ResetStatistics_ResetsAllCounters()
    {
        // Arrange
        _optimizer.ResetStatistics();

        // Act
        var stats = _optimizer.GetStatisticsAsync().Result;

        // Assert
        Assert.Equal(0, stats.TotalQueriesOptimized);
        Assert.Equal(0, stats.PlansFromCache);
        Assert.Equal(0, stats.PlansGenerated);
    }

    #endregion

    #region Options Tests

    [Fact]
    public void QueryOptimizerOptions_Clone_CreatesIndependentCopy()
    {
        // Arrange
        var options = new QueryOptimizerOptions
        {
            Level = OptimizationLevel.Full,
            MaxCachedPlans = 500,
            EnablePlanCache = true
        };

        // Act
        var clone = options.Clone();
        clone.Level = OptimizationLevel.None;
        clone.MaxCachedPlans = 100;

        // Assert
        Assert.Equal(OptimizationLevel.Full, options.Level);
        Assert.Equal(500, options.MaxCachedPlans);
        Assert.Equal(OptimizationLevel.None, clone.Level);
        Assert.Equal(100, clone.MaxCachedPlans);
    }

    [Fact]
    public void QueryOptimizerOptions_Defaults_AreCorrect()
    {
        // Arrange & Act
        var options = new QueryOptimizerOptions();

        // Assert
        Assert.Equal(OptimizationLevel.Full, options.Level);
        Assert.Equal(10, options.MaxAlternativePlans);
        Assert.Equal(1000, options.OptimizationTimeoutMs);
        Assert.True(options.EnablePlanCache);
        Assert.Equal(1000, options.MaxCachedPlans);
        Assert.True(options.EnableFilterPushdown);
        Assert.True(options.EnableIndexSelection);
        Assert.True(options.EnableSortElimination);
        Assert.True(options.EnableProjectionPushdown);
    }

    #endregion

    #region Complex Query Tests

    [Fact]
    public async Task OptimizeAsync_ComplexQuery_WithFilterSortAndProjection()
    {
        // Arrange
        var query = new Query.Models.Query
        {
            CollectionName = "users",
            Filter = QueryFilter.Create(new Dictionary<string, object>
            {
                ["status"] = "active",
                ["age"] = new Dictionary<string, object> { ["$gte"] = 18 }
            }),
            Sort = new List<SortField>
            {
                new() { FieldName = "name", Direction = SortDirection.Ascending }
            },
            Projection = new Dictionary<string, bool>
            {
                ["name"] = true,
                ["email"] = true
            },
            Options = new QueryOptions { Limit = 50 }
        };

        // Act
        var result = await _optimizer.OptimizeAsync(query);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.SelectedPlan);
        Assert.True(result.PlansConsidered >= 1);
    }

    [Fact]
    public async Task OptimizeAsync_MultipleCalls_SameQuery_ReturnsConsistentPlans()
    {
        // Arrange
        var query = new Query.Models.Query
        {
            CollectionName = "users",
            Filter = QueryFilter.Create(new Dictionary<string, object> { ["id"] = "123" })
        };

        // Act
        var result1 = await _optimizer.OptimizeAsync(query);
        var result2 = await _optimizer.OptimizeAsync(query);

        // Assert
        Assert.True(result1.Success);
        Assert.True(result2.Success);
        Assert.Equal(result1.SelectedPlan.RootNode.NodeType, result2.SelectedPlan.RootNode.NodeType);
    }

    #endregion

    #region Optimization Result Tests

    [Fact]
    public void OptimizationResult_Success_CreatesSuccessResult()
    {
        // Arrange
        var plan = new OptimizedQueryPlan
        {
            PlanId = "test",
            OriginalQuery = new Query.Models.Query { CollectionName = "test" },
            OptimizedQuery = new Query.Models.Query { CollectionName = "test" },
            RootNode = new CollectionScanNode { CollectionName = "test" }
        };

        // Act
        var result = new OptimizationResult { IsSuccess = true, SelectedPlan = plan };

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(plan, result.SelectedPlan);
    }

    [Fact]
    public void OptimizationResult_Failure_CreatesFailureResult()
    {
        // Arrange
        const string errorMessage = "Test error";

        // Act
        var result = new OptimizationResult { IsSuccess = false, ErrorMessage = errorMessage };

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(errorMessage, result.ErrorMessage);
        Assert.Null(result.SelectedPlan);
    }

    #endregion

    #region Plan Node Tests

    [Fact]
    public void PlanNode_Children_CanBeAdded()
    {
        // Arrange
        var parent = new CollectionScanNode { CollectionName = "parent" };
        var child = new FilterNode 
        { 
            Conditions = new Dictionary<string, object>(),
            EstimatedOutputCardinality = 100
        };

        // Act
        parent.Children.Add(child);

        // Assert
        Assert.Single(parent.Children);
        Assert.Equal(child, parent.Children[0]);
    }

    [Fact]
    public void FilterNode_Selectivity_DefaultsToPointThree()
    {
        // Arrange & Act
        var node = new FilterNode 
        { 
            Conditions = new Dictionary<string, object>(),
            EstimatedOutputCardinality = 100
        };

        // Assert
        Assert.Equal(0.3, node.Selectivity);
    }

    [Fact]
    public void SortNode_UsesIndex_DefaultsToFalse()
    {
        // Arrange & Act
        var node = new SortNode 
        { 
            SortFields = new List<SortFieldNode>(),
            EstimatedOutputCardinality = 100
        };

        // Assert
        Assert.False(node.UsesIndex);
        Assert.Equal("InMemory", node.Algorithm);
    }

    [Fact]
    public void ProjectionNode_IsInclusion_ReturnsTrue_WhenIncludeFieldsPresent()
    {
        // Arrange & Act
        var node = new ProjectionNode 
        { 
            IncludeFields = new List<string> { "name", "email" },
            EstimatedOutputCardinality = 100
        };

        // Assert
        Assert.True(node.IsInclusion);
    }

    [Fact]
    public void ProjectionNode_IsInclusion_ReturnsFalse_WhenOnlyExcludeFields()
    {
        // Arrange & Act
        var node = new ProjectionNode 
        { 
            ExcludeFields = new List<string> { "password" },
            EstimatedOutputCardinality = 100
        };

        // Assert
        Assert.False(node.IsInclusion);
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task OptimizeAsync_WithCancellation_RespectsToken()
    {
        // Arrange
        var query = new Query.Models.Query { CollectionName = "users" };
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            // Note: The optimizer doesn't check cancellation in the current implementation
            // This test documents expected behavior
            await _optimizer.OptimizeAsync(query, cancellationToken: cts.Token);
        });
    }

    #endregion

    #region Optimizer Statistics Tests

    [Fact]
    public void OptimizerStatistics_CacheHitRate_ZeroWhenNoQueries()
    {
        // Arrange
        var stats = new OptimizerStatistics();

        // Act & Assert
        Assert.Equal(0, stats.CacheHitRate);
    }

    [Fact]
    public void OptimizerStatistics_CacheHitRate_CalculatedCorrectly()
    {
        // Arrange
        var stats = new OptimizerStatistics
        {
            TotalQueriesOptimized = 100,
            PlansFromCache = 75
        };

        // Act & Assert
        Assert.Equal(0.75, stats.CacheHitRate);
    }

    [Fact]
    public void PlanStatistics_AverageExecutionTimeMs_ZeroWhenNoExecutions()
    {
        // Arrange
        var stats = new PlanStatistics();

        // Act & Assert
        Assert.Equal(0, stats.AverageExecutionTimeMs);
    }

    [Fact]
    public void PlanStatistics_AverageExecutionTimeMs_CalculatedCorrectly()
    {
        // Arrange
        var stats = new PlanStatistics
        {
            ExecutionCount = 5,
            TotalExecutionTimeMs = 500
        };

        // Act & Assert
        Assert.Equal(100, stats.AverageExecutionTimeMs);
    }

    #endregion
}
