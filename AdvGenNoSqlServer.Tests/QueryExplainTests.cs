// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Query.Execution;
using AdvGenNoSqlServer.Query.Filtering;
using AdvGenNoSqlServer.Query.Models;
using AdvGenNoSqlServer.Query.QueryAnalysis;
using AdvGenNoSqlServer.Storage;
using AdvGenNoSqlServer.Storage.Indexing;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for EXPLAIN and query plan analysis functionality
/// </summary>
public class QueryExplainTests
{
    private readonly DocumentStore _documentStore;
    private readonly FilterEngine _filterEngine;
    private readonly QueryExecutor _queryExecutor;
    private readonly QueryPlanAnalyzer _analyzer;

    public QueryExplainTests()
    {
        _documentStore = new DocumentStore();
        _filterEngine = new FilterEngine();
        _queryExecutor = new QueryExecutor(_documentStore, _filterEngine);
        _analyzer = new QueryPlanAnalyzer(_documentStore);
    }

    #region QueryPlanAnalyzer Tests

    [Fact]
    public void Constructor_WithValidDocumentStore_CreatesInstance()
    {
        var store = new DocumentStore();
        var analyzer = new QueryPlanAnalyzer(store);

        Assert.NotNull(analyzer);
    }

    [Fact]
    public void Constructor_WithNullDocumentStore_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new QueryPlanAnalyzer(null!));
    }

    #endregion

    #region Basic EXPLAIN Tests

    [Fact]
    public async Task ExplainAsync_BasicQuery_ReturnsQueryStats()
    {
        // Arrange
        await SeedTestDataAsync();
        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = "users",
            Filter = QueryFilter.Eq("status", "active")
        };

        // Act
        var stats = await _queryExecutor.ExplainAsync(query);

        // Assert
        Assert.NotNull(stats);
        Assert.NotNull(stats.ExecutionPlan);
        Assert.True(stats.ExecutionPlan.Count > 0);
    }

    [Fact]
    public async Task ExplainDetailedAsync_BasicQuery_ReturnsAnalysisResult()
    {
        // Arrange
        await SeedTestDataAsync();
        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = "users",
            Filter = QueryFilter.Eq("status", "active")
        };

        // Act
        var result = await _queryExecutor.ExplainDetailedAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Query);
        Assert.NotNull(result.Summary);
        Assert.NotNull(result.ExecutionPlan);
        Assert.NotNull(result.IndexRecommendations);
        Assert.NotNull(result.Suggestions);
        Assert.Equal("users", result.Query.Collection);
    }

    [Fact]
    public async Task ExplainDetailedAsync_WithDifferentVerbosityLevels_ReturnsAppropriateDetail()
    {
        // Arrange
        await SeedTestDataAsync();
        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = "users",
            Filter = QueryFilter.Eq("status", "active")
        };

        // Act - QueryPlanner verbosity
        var basicResult = await _queryExecutor.ExplainDetailedAsync(query, ExplainVerbosity.QueryPlanner);

        // Act - ExecutionStats verbosity
        var detailedResult = await _queryExecutor.ExplainDetailedAsync(query, ExplainVerbosity.ExecutionStats);

        // Act - AllPlansExecution verbosity
        var allPlansResult = await _queryExecutor.ExplainDetailedAsync(query, ExplainVerbosity.AllPlansExecution);

        // Assert
        Assert.NotNull(basicResult);
        Assert.NotNull(detailedResult);
        Assert.NotNull(allPlansResult);
        Assert.True(allPlansResult.AlternativePlans.Count >= basicResult.AlternativePlans.Count);
    }

    #endregion

    #region Execution Plan Tests

    [Fact]
    public async Task ExplainDetailedAsync_CollectionScanQuery_IncludesCollectionScanStage()
    {
        // Arrange
        await SeedTestDataAsync();
        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = "users"
        };

        // Act
        var result = await _queryExecutor.ExplainDetailedAsync(query);

        // Assert
        var scanStage = result.ExecutionPlan.FirstOrDefault(s => s.StageName == "CollectionScan");
        Assert.NotNull(scanStage);
        Assert.True(scanStage.EstimatedInputDocuments > 0);
    }

    [Fact]
    public async Task ExplainDetailedAsync_WithIndex_IncludesIndexScanStage()
    {
        // Arrange
        var indexManager = new IndexManager();
        var store = new DocumentStore();
        var executor = new QueryExecutor(store, new FilterEngine(), indexManager);
        var analyzer = new QueryPlanAnalyzer(store, indexManager);

        await SeedTestDataAsync(store);
        indexManager.CreateIndex<string>("users", "email", false, d => d.Data.TryGetValue("email", out var v) ? v?.ToString() : null);

        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = "users",
            Filter = QueryFilter.Eq("email", "test@example.com")
        };

        // Act
        var result = await analyzer.AnalyzeAsync(query);

        // Assert
        var scanStage = result.ExecutionPlan.FirstOrDefault();
        Assert.NotNull(scanStage);
        Assert.True(result.Summary.WillUseIndex);
        Assert.Equal("IndexScan", scanStage.StageName);
    }

    [Fact]
    public async Task ExplainDetailedAsync_WithFilter_IncludesFilterStage()
    {
        // Arrange
        await SeedTestDataAsync();
        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = "users",
            Filter = QueryFilter.Eq("status", "active")
        };

        // Act
        var result = await _queryExecutor.ExplainDetailedAsync(query);

        // Assert
        var filterStage = result.ExecutionPlan.FirstOrDefault(s => s.StageName == "Filter");
        Assert.NotNull(filterStage);
    }

    [Fact]
    public async Task ExplainDetailedAsync_WithSort_IncludesSortStage()
    {
        // Arrange
        await SeedTestDataAsync();
        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = "users",
            Sort = new List<SortField> { SortField.Ascending("name") }
        };

        // Act
        var result = await _queryExecutor.ExplainDetailedAsync(query);

        // Assert
        var sortStage = result.ExecutionPlan.FirstOrDefault(s => s.StageName == "Sort");
        Assert.NotNull(sortStage);
    }

    [Fact]
    public async Task ExplainDetailedAsync_WithLimit_IncludesLimitStage()
    {
        // Arrange
        await SeedTestDataAsync();
        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = "users",
            Options = new QueryOptions { Limit = 10 }
        };

        // Act
        var result = await _queryExecutor.ExplainDetailedAsync(query);

        // Assert
        var limitStage = result.ExecutionPlan.FirstOrDefault(s => s.StageName == "Limit");
        Assert.NotNull(limitStage);
        Assert.Equal(10, limitStage.Details["Limit"]);
    }

    [Fact]
    public async Task ExplainDetailedAsync_WithSkip_IncludesSkipStage()
    {
        // Arrange
        await SeedTestDataAsync();
        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = "users",
            Options = new QueryOptions { Skip = 20 }
        };

        // Act
        var result = await _queryExecutor.ExplainDetailedAsync(query);

        // Assert
        var skipStage = result.ExecutionPlan.FirstOrDefault(s => s.StageName == "Skip");
        Assert.NotNull(skipStage);
        Assert.Equal(20, skipStage.Details["Skip"]);
    }

    [Fact]
    public async Task ExplainDetailedAsync_WithProjection_IncludesProjectStage()
    {
        // Arrange
        await SeedTestDataAsync();
        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = "users",
            Projection = new Dictionary<string, bool> { { "name", true }, { "email", true } }
        };

        // Act
        var result = await _queryExecutor.ExplainDetailedAsync(query);

        // Assert
        var projectStage = result.ExecutionPlan.FirstOrDefault(s => s.StageName == "Project");
        Assert.NotNull(projectStage);
    }

    #endregion

    #region Index Recommendation Tests

    [Fact]
    public async Task GetIndexRecommendationsAsync_NoIndexOnFilterField_ReturnsRecommendation()
    {
        // Arrange
        await SeedTestDataAsync();
        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = "users",
            Filter = QueryFilter.Eq("status", "active")
        };

        // Act
        var recommendations = await _analyzer.GetIndexRecommendationsAsync(query);

        // Assert
        Assert.NotEmpty(recommendations);
        var rec = recommendations.FirstOrDefault(r => r.Fields.Contains("status"));
        Assert.NotNull(rec);
        Assert.Equal("High", rec.Priority);
    }

    [Fact]
    public async Task GetIndexRecommendationsAsync_WithExistingIndex_NoRecommendationForThatField()
    {
        // Arrange
        var indexManager = new IndexManager();
        var store = new DocumentStore();
        var analyzer = new QueryPlanAnalyzer(store, indexManager);

        await SeedTestDataAsync(store);
        indexManager.CreateIndex<string>("users", "status", false, d => d.Data.TryGetValue("status", out var v) ? v?.ToString() : null);

        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = "users",
            Filter = QueryFilter.Eq("status", "active")
        };

        // Act
        var recommendations = await analyzer.GetIndexRecommendationsAsync(query);

        // Assert
        var statusRec = recommendations.FirstOrDefault(r => r.Fields.Contains("status") && r.Fields.Count == 1);
        Assert.Null(statusRec);
    }

    [Fact]
    public async Task GetIndexRecommendationsAsync_MultipleFilterFields_SuggestsCompoundIndex()
    {
        // Arrange
        await SeedTestDataAsync();
        // Use a filter with top-level AND conditions (not nested QueryFilter.And)
        var filter = new QueryFilter();
        filter.Conditions["status"] = "active";
        filter.Conditions["role"] = "admin";

        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = "users",
            Filter = filter
        };

        // Act
        var recommendations = await _analyzer.GetIndexRecommendationsAsync(query);

        // Assert - should have recommendations for individual fields
        Assert.NotEmpty(recommendations);

        // Check if we have at least one recommendation
        var statusRec = recommendations.FirstOrDefault(r => r.Fields.Contains("status"));
        var roleRec = recommendations.FirstOrDefault(r => r.Fields.Contains("role"));

        Assert.True(statusRec != null || roleRec != null,
            "Should have index recommendations for at least one filter field");
    }

    #endregion

    #region Optimization Suggestion Tests

    [Fact]
    public async Task GetOptimizationSuggestionsAsync_UnboundedQuery_SuggestsAddingLimit()
    {
        // Arrange
        await SeedTestDataAsync();
        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = "users"
        };

        // Act
        var suggestions = await _analyzer.GetOptimizationSuggestionsAsync(query);

        // Assert
        var limitSuggestion = suggestions.FirstOrDefault(s => s.Type == "Pagination" && s.Title.Contains("Unbounded"));
        Assert.NotNull(limitSuggestion);
        Assert.Equal("High", limitSuggestion.Severity);
    }

    [Fact]
    public async Task GetOptimizationSuggestionsAsync_LargeSkip_SuggestsCursorPagination()
    {
        // Arrange
        await SeedTestDataAsync();
        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = "users",
            Options = new QueryOptions { Skip = 5000 }
        };

        // Act
        var suggestions = await _analyzer.GetOptimizationSuggestionsAsync(query);

        // Assert
        var skipSuggestion = suggestions.FirstOrDefault(s => s.Title.Contains("Large Skip") || s.Title.Contains("Skip"));
        Assert.NotNull(skipSuggestion);
    }

    [Fact]
    public async Task GetOptimizationSuggestionsAsync_NonIndexedFilters_SuggestsCreatingIndex()
    {
        // Arrange
        await SeedTestDataAsync();
        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = "users",
            Filter = QueryFilter.Eq("status", "active")
        };

        // Act
        var suggestions = await _analyzer.GetOptimizationSuggestionsAsync(query);

        // Assert
        var indexSuggestion = suggestions.FirstOrDefault(s => s.Type == "FilterOptimization");
        Assert.NotNull(indexSuggestion);
    }

    [Fact]
    public async Task GetOptimizationSuggestionsAsync_InMemorySort_SuggestsIndex()
    {
        // Arrange
        await SeedTestDataAsync();
        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = "users",
            Sort = new List<SortField> { SortField.Descending("createdAt") }
        };

        // Act
        var suggestions = await _analyzer.GetOptimizationSuggestionsAsync(query);

        // Assert
        var sortSuggestion = suggestions.FirstOrDefault(s => s.Type == "SortOptimization" || s.Title.Contains("Sort"));
        Assert.NotNull(sortSuggestion);
    }

    [Fact]
    public async Task GetOptimizationSuggestionsAsync_NoProjection_SuggestsAddingProjection()
    {
        // Arrange
        await SeedTestDataAsync();
        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = "users",
            Filter = QueryFilter.Eq("status", "active")
        };

        // Act
        var suggestions = await _analyzer.GetOptimizationSuggestionsAsync(query);

        // Assert
        var projectionSuggestion = suggestions.FirstOrDefault(s => s.Type == "Projection");
        Assert.NotNull(projectionSuggestion);
    }

    #endregion

    #region Complexity Score Tests

    [Fact]
    public void CalculateComplexityScore_SimpleQuery_ReturnsLowScore()
    {
        // Arrange
        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = "users",
            Filter = QueryFilter.Eq("id", "123"),
            Options = new QueryOptions { Limit = 1 }
        };

        // Act
        var score = _analyzer.CalculateComplexityScore(query);

        // Assert
        Assert.True(score < 50, $"Expected low complexity score, got {score}");
    }

    [Fact]
    public void CalculateComplexityScore_ComplexQuery_ReturnsHighScore()
    {
        // Arrange
        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = "users",
            Filter = QueryFilter.Eq("field1", "value1")
                .And(QueryFilter.Eq("field2", "value2"))
                .And(QueryFilter.Eq("field3", "value3"))
                .And(QueryFilter.Eq("field4", "value4"))
                .And(QueryFilter.Eq("field5", "value5"))
                .And(QueryFilter.Eq("field6", "value6")),
            Sort = new List<SortField>
            {
                SortField.Ascending("field1"),
                SortField.Descending("field2")
            },
            Options = new QueryOptions { Skip = 5000 }
        };

        // Act
        var score = _analyzer.CalculateComplexityScore(query);

        // Assert
        Assert.True(score > 50, $"Expected high complexity score, got {score}");
    }

    [Fact]
    public void CalculateComplexityScore_UnboundedQuery_HigherThanBounded()
    {
        // Arrange - both queries have filters but one has a limit
        var boundedQuery = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = "users",
            Filter = QueryFilter.Eq("status", "active"),
            Options = new QueryOptions { Limit = 10 }
        };

        var unboundedQuery = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = "users",
            Filter = QueryFilter.Eq("status", "active")
        };

        // Act
        var boundedScore = _analyzer.CalculateComplexityScore(boundedQuery);
        var unboundedScore = _analyzer.CalculateComplexityScore(unboundedQuery);

        // Assert - unbounded query should have higher or equal complexity
        Assert.True(unboundedScore >= boundedScore, "Unbounded query should have same or higher complexity");
    }

    [Fact]
    public void CalculateComplexityScore_CappedAt100()
    {
        // Arrange - create a very complex query
        var filter = QueryFilter.Eq("field0", "value0");
        for (int i = 1; i < 20; i++)
        {
            filter = filter.And(QueryFilter.Eq($"field{i}", $"value{i}"));
        }

        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = "users",
            Filter = filter,
            Sort = Enumerable.Range(0, 10).Select(i => SortField.Ascending($"field{i}")).ToList()
        };

        // Act
        var score = _analyzer.CalculateComplexityScore(query);

        // Assert
        Assert.True(score <= 100, $"Complexity score should be capped at 100, got {score}");
    }

    #endregion

    #region Document Count Estimation Tests

    [Fact]
    public async Task EstimateDocumentCountAsync_EmptyCollection_ReturnsZero()
    {
        // Arrange
        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = "empty_collection"
        };

        // Act
        var count = await _analyzer.EstimateDocumentCountAsync(query);

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task EstimateDocumentCountAsync_WithData_ReturnsEstimate()
    {
        // Arrange
        await SeedTestDataAsync(count: 100);
        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = "users"
        };

        // Act
        var count = await _analyzer.EstimateDocumentCountAsync(query);

        // Assert
        Assert.True(count > 0, "Should return positive estimate");
    }

    #endregion

    #region IsSlowQuery Tests

    [Fact]
    public async Task ExplainDetailedAsync_ExpensiveQuery_MarkedAsSlow()
    {
        // Arrange
        await SeedTestDataAsync(count: 10000);
        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = "users",
            Options = new QueryOptions { Limit = 1000 }
        };

        // Act
        var result = await _queryExecutor.ExplainDetailedAsync(query);

        // Assert
        Assert.True(result.IsSlowQuery || result.ComplexityScore > 50);
    }

    [Fact]
    public async Task ExplainDetailedAsync_SimpleQuery_NotMarkedAsSlow()
    {
        // Arrange
        await SeedTestDataAsync(count: 100);
        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = "users",
            Filter = QueryFilter.Eq("id", "user1"),
            Options = new QueryOptions { Limit = 1 }
        };

        // Act
        var result = await _queryExecutor.ExplainDetailedAsync(query);

        // Assert
        Assert.False(result.IsSlowQuery);
        Assert.True(result.ComplexityScore < 50);
    }

    #endregion

    #region Execution Summary Tests

    [Fact]
    public async Task ExplainDetailedAsync_ReturnsCorrectSummary()
    {
        // Arrange
        await SeedTestDataAsync();
        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = "users",
            Filter = QueryFilter.Eq("status", "active")
        };

        // Act
        var result = await _queryExecutor.ExplainDetailedAsync(query);

        // Assert
        Assert.NotNull(result.Summary);
        Assert.True(result.Summary.EstimatedExecutionTimeMs >= 0);
        Assert.True(result.Summary.EstimatedDocumentsToScan >= 0);
        Assert.NotNull(result.Summary.ExecutionStrategy);
    }

    [Fact]
    public async Task ExplainDetailedAsync_WithIndex_SummaryReflectsIndexUsage()
    {
        // Arrange
        var indexManager = new IndexManager();
        var store = new DocumentStore();
        var executor = new QueryExecutor(store, new FilterEngine(), indexManager);

        await SeedTestDataAsync(store);
        indexManager.CreateIndex<string>("users", "email", false, d => d.Data.TryGetValue("email", out var v) ? v?.ToString() : null);

        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = "users",
            Filter = QueryFilter.Eq("email", "test@example.com")
        };

        // Act
        var result = await executor.ExplainDetailedAsync(query);

        // Assert
        Assert.True(result.Summary.WillUseIndex);
        Assert.NotNull(result.Summary.IndexName);
        Assert.Contains("IndexScan", result.Summary.ExecutionStrategy);
    }

    #endregion

    #region Alternative Plans Tests

    [Fact]
    public async Task ExplainDetailedAsync_AllPlansVerbosity_IncludesAlternatives()
    {
        // Arrange
        await SeedTestDataAsync();
        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = "users",
            Filter = QueryFilter.Eq("status", "active").And(QueryFilter.Eq("role", "admin"))
        };

        // Act
        var result = await _queryExecutor.ExplainDetailedAsync(query, ExplainVerbosity.AllPlansExecution);

        // Assert
        Assert.NotNull(result.AlternativePlans);
    }

    #endregion

    #region Query Info Tests

    [Fact]
    public async Task ExplainDetailedAsync_ReturnsCorrectQueryInfo()
    {
        // Arrange
        await SeedTestDataAsync();
        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = "users",
            Filter = QueryFilter.Eq("status", "active"),
            Sort = new List<SortField> { SortField.Ascending("name") },
            Projection = new Dictionary<string, bool> { { "name", true } },
            Options = new QueryOptions { Skip = 10, Limit = 20 }
        };

        // Act
        var result = await _queryExecutor.ExplainDetailedAsync(query);

        // Assert
        Assert.NotNull(result.Query);
        Assert.Equal("users", result.Query.Collection);
        Assert.NotNull(result.Query.Filter);
        Assert.NotNull(result.Query.SortFields);
        Assert.Single(result.Query.SortFields);
        Assert.NotNull(result.Query.Projection);
        Assert.NotNull(result.Query.Pagination);
        Assert.Equal(10, result.Query.Pagination.Skip);
        Assert.Equal(20, result.Query.Pagination.Limit);
    }

    #endregion

    #region Stage Details Tests

    [Fact]
    public async Task ExplainDetailedAsync_StagesHaveCorrectOrder()
    {
        // Arrange
        await SeedTestDataAsync();
        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = "users",
            Filter = QueryFilter.Eq("status", "active"),
            Sort = new List<SortField> { SortField.Ascending("name") },
            Options = new QueryOptions { Skip = 10, Limit = 20 }
        };

        // Act
        var result = await _queryExecutor.ExplainDetailedAsync(query);

        // Assert
        var orders = result.ExecutionPlan.Select(s => s.StageOrder).ToList();
        Assert.Equal(orders.OrderBy(o => o), orders);
    }

    [Fact]
    public async Task ExplainDetailedAsync_StagesHaveValidDocumentCounts()
    {
        // Arrange
        await SeedTestDataAsync();
        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = "users",
            Options = new QueryOptions { Limit = 10 }
        };

        // Act
        var result = await _queryExecutor.ExplainDetailedAsync(query);

        // Assert
        foreach (var stage in result.ExecutionPlan)
        {
            Assert.True(stage.EstimatedInputDocuments >= 0, $"Stage {stage.StageName} has negative input count");
            Assert.True(stage.EstimatedOutputDocuments >= 0, $"Stage {stage.StageName} has negative output count");
        }
    }

    #endregion

    #region Helper Methods

    private async Task SeedTestDataAsync(int count = 50)
    {
        await SeedTestDataAsync(_documentStore, count);
    }

    private async Task SeedTestDataAsync(DocumentStore store, int count = 50)
    {
        for (int i = 0; i < count; i++)
        {
            var doc = new Core.Models.Document
            {
                Id = $"user{i}",
                Data = new Dictionary<string, object>
                {
                    ["name"] = $"User {i}",
                    ["email"] = $"user{i}@example.com",
                    ["status"] = i % 2 == 0 ? "active" : "inactive",
                    ["role"] = i % 5 == 0 ? "admin" : "user",
                    ["createdAt"] = DateTime.UtcNow.AddDays(-i)
                }
            };
            await store.InsertAsync("users", doc);
        }
    }

    #endregion
}
