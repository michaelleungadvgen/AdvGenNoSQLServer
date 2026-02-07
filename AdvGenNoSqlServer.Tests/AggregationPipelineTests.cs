// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Query.Aggregation;
using AdvGenNoSqlServer.Query.Aggregation.Stages;
using AdvGenNoSqlServer.Query.Filtering;
using AdvGenNoSqlServer.Query.Models;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for the Aggregation Pipeline implementation
/// </summary>
public class AggregationPipelineTests
{
    private readonly FilterEngine _filterEngine = new();

    #region Test Data

    private List<Document> CreateTestDocuments()
    {
        return new List<Document>
        {
            new() { Id = "1", Data = new() { ["category"] = "A", ["value"] = 10, ["quantity"] = 2, ["name"] = "Item 1" } },
            new() { Id = "2", Data = new() { ["category"] = "B", ["value"] = 20, ["quantity"] = 3, ["name"] = "Item 2" } },
            new() { Id = "3", Data = new() { ["category"] = "A", ["value"] = 30, ["quantity"] = 1, ["name"] = "Item 3" } },
            new() { Id = "4", Data = new() { ["category"] = "B", ["value"] = 40, ["quantity"] = 4, ["name"] = "Item 4" } },
            new() { Id = "5", Data = new() { ["category"] = "C", ["value"] = 50, ["quantity"] = 5, ["name"] = "Item 5" } }
        };
    }

    private List<Document> CreateNumericTestDocuments()
    {
        return new List<Document>
        {
            new() { Id = "1", Data = new() { ["group"] = "X", ["score"] = 100.0, ["count"] = 10 } },
            new() { Id = "2", Data = new() { ["group"] = "X", ["score"] = 200.0, ["count"] = 20 } },
            new() { Id = "3", Data = new() { ["group"] = "Y", ["score"] = 300.0, ["count"] = 30 } },
            new() { Id = "4", Data = new() { ["group"] = "Y", ["score"] = 400.0, ["count"] = 40 } },
            new() { Id = "5", Data = new() { ["group"] = "Y", ["score"] = 500.0, ["count"] = 50 } }
        };
    }

    #endregion

    #region AggregationPipeline Tests

    [Fact]
    public void AggregationPipeline_EmptyPipeline_ReturnsAllDocuments()
    {
        // Arrange
        var documents = CreateTestDocuments();
        var pipeline = new AggregationPipeline();

        // Act
        var result = pipeline.Execute(documents);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(5, result.Count);
        Assert.Equal(0, result.StagesExecuted);
    }

    [Fact]
    public void AggregationPipeline_AddStage_IncreasesStageCount()
    {
        // Arrange
        var pipeline = new AggregationPipeline();
        
        // Act
        pipeline.AddStage(new LimitStage(10));
        pipeline.AddStage(new SkipStage(5));

        // Assert
        Assert.Equal(2, pipeline.StageCount);
    }

    [Fact]
    public void AggregationPipeline_Clear_RemovesAllStages()
    {
        // Arrange
        var pipeline = new AggregationPipeline();
        pipeline.AddStage(new LimitStage(10));
        
        // Act
        pipeline.Clear();

        // Assert
        Assert.Equal(0, pipeline.StageCount);
    }

    [Fact]
    public void AggregationPipeline_NullDocuments_ThrowsArgumentNullException()
    {
        // Arrange
        var pipeline = new AggregationPipeline();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => pipeline.Execute(null!));
    }

    [Fact]
    public void AggregationPipeline_Chaining_AddsStagesCorrectly()
    {
        // Arrange & Act
        var pipeline = new AggregationPipeline()
            .AddStage(new SkipStage(2))
            .AddStage(new LimitStage(3));

        // Assert
        Assert.Equal(2, pipeline.StageCount);
    }

    #endregion

    #region MatchStage Tests

    [Fact]
    public void MatchStage_SingleCondition_FiltersCorrectly()
    {
        // Arrange
        var documents = CreateTestDocuments();
        var filter = QueryFilter.Eq("category", "A");
        var stage = new MatchStage(_filterEngine, filter);

        // Act
        var result = stage.Execute(documents).ToList();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, d => Assert.Equal("A", d.Data!["category"]));
    }

    [Fact]
    public void MatchStage_MultipleConditions_FiltersCorrectly()
    {
        // Arrange
        var documents = CreateTestDocuments();
        var filter = QueryFilter.Gt("value", 15).And(QueryFilter.Lt("value", 45));
        var stage = new MatchStage(_filterEngine, filter);

        // Act
        var result = stage.Execute(documents).ToList();

        // Assert
        Assert.Equal(3, result.Count);
        Assert.All(result, d => Assert.True((int)d.Data!["value"] > 15 && (int)d.Data["value"] < 45));
    }

    [Fact]
    public void MatchStage_NoMatches_ReturnsEmpty()
    {
        // Arrange
        var documents = CreateTestDocuments();
        var filter = QueryFilter.Eq("category", "Z");
        var stage = new MatchStage(_filterEngine, filter);

        // Act
        var result = stage.Execute(documents).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void MatchStage_Async_ExecutesCorrectly()
    {
        // Arrange
        var documents = CreateTestDocuments();
        var filter = QueryFilter.Eq("category", "A");
        var stage = new MatchStage(_filterEngine, filter);

        // Act
        var result = stage.ExecuteAsync(documents).Result.ToList();

        // Assert
        Assert.Equal(2, result.Count);
    }

    #endregion

    #region GroupStage Tests

    [Fact]
    public void GroupStage_NoGroupBy_AggregatesAll()
    {
        // Arrange
        var documents = CreateTestDocuments();
        var aggregations = new Dictionary<string, GroupFieldSpec>
        {
            ["total"] = new(GroupOperator.Sum, "$value"),
            ["count"] = new(GroupOperator.Count),
            ["avg"] = new(GroupOperator.Avg, "$value")
        };
        var stage = new GroupStage(aggregations);

        // Act
        var result = stage.Execute(documents).ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal(150.0, result[0].Data!["total"]);
        Assert.Equal(5, result[0].Data["count"]);
        Assert.Equal(30.0, result[0].Data["avg"]);
    }

    [Fact]
    public void GroupStage_ByField_GroupsCorrectly()
    {
        // Arrange
        var documents = CreateTestDocuments();
        var aggregations = new Dictionary<string, GroupFieldSpec>
        {
            ["totalValue"] = new(GroupOperator.Sum, "$value"),
            ["count"] = new(GroupOperator.Count)
        };
        var stage = new GroupStage("$category", aggregations);

        // Act
        var result = stage.Execute(documents).ToList();

        // Assert
        Assert.Equal(3, result.Count);
        
        var groupA = result.First(r => "A".Equals(r.Data!["_id"]));
        Assert.Equal(40.0, groupA.Data["totalValue"]);
        Assert.Equal(2, groupA.Data["count"]);

        var groupB = result.First(r => "B".Equals(r.Data!["_id"]));
        Assert.Equal(60.0, groupB.Data["totalValue"]);
        Assert.Equal(2, groupB.Data["count"]);
    }

    [Fact]
    public void GroupStage_Sum_CalculatesCorrectly()
    {
        // Arrange
        var documents = CreateNumericTestDocuments();
        var aggregations = new Dictionary<string, GroupFieldSpec>
        {
            ["totalScore"] = Aggregation.Sum("$score")
        };
        var stage = new GroupStage("$group", aggregations);

        // Act
        var result = stage.Execute(documents).ToList();

        // Assert
        var groupX = result.First(r => "X".Equals(r.Data!["_id"]));
        Assert.Equal(300.0, groupX.Data!["totalScore"]);

        var groupY = result.First(r => "Y".Equals(r.Data!["_id"]));
        Assert.Equal(1200.0, groupY.Data!["totalScore"]);
    }

    [Fact]
    public void GroupStage_Avg_CalculatesCorrectly()
    {
        // Arrange
        var documents = CreateNumericTestDocuments();
        var aggregations = new Dictionary<string, GroupFieldSpec>
        {
            ["avgScore"] = Aggregation.Avg("$score")
        };
        var stage = new GroupStage("$group", aggregations);

        // Act
        var result = stage.Execute(documents).ToList();

        // Assert
        var groupX = result.First(r => "X".Equals(r.Data!["_id"]));
        Assert.Equal(150.0, groupX.Data!["avgScore"]);

        var groupY = result.First(r => "Y".Equals(r.Data!["_id"]));
        Assert.Equal(400.0, groupY.Data!["avgScore"]);
    }

    [Fact]
    public void GroupStage_MinMax_CalculatesCorrectly()
    {
        // Arrange
        var documents = CreateNumericTestDocuments();
        var aggregations = new Dictionary<string, GroupFieldSpec>
        {
            ["minScore"] = Aggregation.Min("$score"),
            ["maxScore"] = Aggregation.Max("$score")
        };
        var stage = new GroupStage("$group", aggregations);

        // Act
        var result = stage.Execute(documents).ToList();

        // Assert
        var groupX = result.First(r => "X".Equals(r.Data!["_id"]));
        Assert.Equal(100.0, groupX.Data!["minScore"]);
        Assert.Equal(200.0, groupX.Data!["maxScore"]);

        var groupY = result.First(r => "Y".Equals(r.Data!["_id"]));
        Assert.Equal(300.0, groupY.Data!["minScore"]);
        Assert.Equal(500.0, groupY.Data!["maxScore"]);
    }

    [Fact]
    public void GroupStage_FirstLast_ReturnsCorrectValues()
    {
        // Arrange
        var documents = CreateTestDocuments();
        var aggregations = new Dictionary<string, GroupFieldSpec>
        {
            ["firstName"] = Aggregation.First("$name"),
            ["lastName"] = Aggregation.Last("$name")
        };
        var stage = new GroupStage("$category", aggregations);

        // Act
        var result = stage.Execute(documents).ToList();

        // Assert
        var groupA = result.First(r => "A".Equals(r.Data!["_id"]));
        Assert.Equal("Item 1", groupA.Data!["firstName"]);
        Assert.Equal("Item 3", groupA.Data!["lastName"]);
    }

    [Fact]
    public void GroupStage_Push_CreatesArray()
    {
        // Arrange
        var documents = CreateTestDocuments();
        var aggregations = new Dictionary<string, GroupFieldSpec>
        {
            ["names"] = Aggregation.Push("$name")
        };
        var stage = new GroupStage("$category", aggregations);

        // Act
        var result = stage.Execute(documents).ToList();

        // Assert
        var groupA = result.First(r => "A".Equals(r.Data!["_id"]));
        var namesA = groupA.Data!["names"] as List<object?>;
        Assert.NotNull(namesA);
        Assert.Equal(2, namesA.Count);
        Assert.Contains("Item 1", namesA);
        Assert.Contains("Item 3", namesA);
    }

    [Fact]
    public void GroupStage_AddToSet_CreatesUniqueArray()
    {
        // Arrange
        var documents = new List<Document>
        {
            new() { Id = "1", Data = new() { ["category"] = "A", ["tag"] = "red" } },
            new() { Id = "2", Data = new() { ["category"] = "A", ["tag"] = "blue" } },
            new() { Id = "3", Data = new() { ["category"] = "A", ["tag"] = "red" } }, // duplicate
            new() { Id = "4", Data = new() { ["category"] = "A", ["tag"] = "green" } }
        };
        var aggregations = new Dictionary<string, GroupFieldSpec>
        {
            ["uniqueTags"] = Aggregation.AddToSet("$tag")
        };
        var stage = new GroupStage("$category", aggregations);

        // Act
        var result = stage.Execute(documents).ToList();

        // Assert
        var uniqueTags = result[0].Data!["uniqueTags"] as List<object?>;
        Assert.NotNull(uniqueTags);
        Assert.Equal(3, uniqueTags.Count); // red, blue, green (no duplicate red)
    }

    [Fact]
    public void GroupStage_Async_ExecutesCorrectly()
    {
        // Arrange
        var documents = CreateTestDocuments();
        var aggregations = new Dictionary<string, GroupFieldSpec>
        {
            ["count"] = new(GroupOperator.Count)
        };
        var stage = new GroupStage(aggregations);

        // Act
        var result = stage.ExecuteAsync(documents).Result.ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal(5, result[0].Data!["count"]);
    }

    #endregion

    #region ProjectStage Tests

    [Fact]
    public void ProjectStage_Inclusion_IncludesOnlySpecifiedFields()
    {
        // Arrange
        var documents = CreateTestDocuments();
        var projections = new Dictionary<string, bool>
        {
            ["name"] = true,
            ["value"] = true
        };
        var stage = new ProjectStage(projections);

        // Act
        var result = stage.Execute(documents).ToList();

        // Assert
        Assert.Equal(5, result.Count);
        foreach (var doc in result)
        {
            Assert.NotNull(doc.Data);
            Assert.True(doc.Data.ContainsKey("name"));
            Assert.True(doc.Data.ContainsKey("value"));
            Assert.True(doc.Data.ContainsKey("_id")); // Always included unless excluded
            Assert.False(doc.Data.ContainsKey("category"));
            Assert.False(doc.Data.ContainsKey("quantity"));
        }
    }

    [Fact]
    public void ProjectStage_Exclusion_ExcludesSpecifiedFields()
    {
        // Arrange
        var documents = CreateTestDocuments();
        var projections = new Dictionary<string, bool>
        {
            ["category"] = false,
            ["quantity"] = false
        };
        var stage = new ProjectStage(projections);

        // Act
        var result = stage.Execute(documents).ToList();

        // Assert
        Assert.Equal(5, result.Count);
        foreach (var doc in result)
        {
            Assert.NotNull(doc.Data);
            Assert.True(doc.Data.ContainsKey("name"));
            Assert.True(doc.Data.ContainsKey("value"));
            Assert.True(doc.Data.ContainsKey("_id")); // _id is included by default
            Assert.False(doc.Data.ContainsKey("category"));
            Assert.False(doc.Data.ContainsKey("quantity"));
        }
    }

    [Fact]
    public void ProjectStage_ExcludesId_WhenSpecified()
    {
        // Arrange
        var documents = CreateTestDocuments();
        var projections = new Dictionary<string, bool>
        {
            ["name"] = true,
            ["_id"] = false
        };
        var stage = new ProjectStage(projections);

        // Act
        var result = stage.Execute(documents).ToList();

        // Assert
        Assert.Equal(5, result.Count);
        foreach (var doc in result)
        {
            Assert.NotNull(doc.Data);
            Assert.True(doc.Data.ContainsKey("name"));
            Assert.False(doc.Data.ContainsKey("_id"));
        }
    }

    [Fact]
    public void ProjectStage_Async_ExecutesCorrectly()
    {
        // Arrange
        var documents = CreateTestDocuments();
        var projections = new Dictionary<string, bool> { ["name"] = true };
        var stage = new ProjectStage(projections);

        // Act
        var result = stage.ExecuteAsync(documents).Result.ToList();

        // Assert
        Assert.Equal(5, result.Count);
    }

    #endregion

    #region SortStage Tests

    [Fact]
    public void SortStage_Ascending_SortsCorrectly()
    {
        // Arrange
        var documents = CreateTestDocuments();
        var stage = new SortStage("value", true);

        // Act
        var result = stage.Execute(documents).ToList();

        // Assert
        Assert.Equal(5, result.Count);
        Assert.Equal("1", result[0].Id); // value 10
        Assert.Equal("2", result[1].Id); // value 20
        Assert.Equal("3", result[2].Id); // value 30
        Assert.Equal("4", result[3].Id); // value 40
        Assert.Equal("5", result[4].Id); // value 50
    }

    [Fact]
    public void SortStage_Descending_SortsCorrectly()
    {
        // Arrange
        var documents = CreateTestDocuments();
        var stage = new SortStage("value", false);

        // Act
        var result = stage.Execute(documents).ToList();

        // Assert
        Assert.Equal(5, result.Count);
        Assert.Equal("5", result[0].Id); // value 50
        Assert.Equal("4", result[1].Id); // value 40
        Assert.Equal("3", result[2].Id); // value 30
        Assert.Equal("2", result[3].Id); // value 20
        Assert.Equal("1", result[4].Id); // value 10
    }

    [Fact]
    public void SortStage_MultipleFields_SortsCorrectly()
    {
        // Arrange
        var documents = new List<Document>
        {
            new() { Id = "1", Data = new() { ["category"] = "B", ["value"] = 10 } },
            new() { Id = "2", Data = new() { ["category"] = "A", ["value"] = 20 } },
            new() { Id = "3", Data = new() { ["category"] = "A", ["value"] = 10 } },
            new() { Id = "4", Data = new() { ["category"] = "B", ["value"] = 20 } }
        };
        var sortSpecs = new List<SortSpec>
        {
            new("category", true),
            new("value", false)
        };
        var stage = new SortStage(sortSpecs);

        // Act
        var result = stage.Execute(documents).ToList();

        // Assert
        Assert.Equal(4, result.Count);
        // First by category (A before B)
        // Then by value descending within category
        Assert.Equal("2", result[0].Id); // A, 20
        Assert.Equal("3", result[1].Id); // A, 10
        Assert.Equal("4", result[2].Id); // B, 20
        Assert.Equal("1", result[3].Id); // B, 10
    }

    [Fact]
    public void SortStage_FromDictionary_CreatesSpecsCorrectly()
    {
        // Arrange
        var documents = CreateTestDocuments();
        var dict = new Dictionary<string, int> { ["$value"] = -1 }; // Descending
        var stage = new SortStage(dict);

        // Act
        var result = stage.Execute(documents).ToList();

        // Assert
        Assert.Equal("5", result[0].Id); // Highest value first
    }

    #endregion

    #region LimitStage Tests

    [Fact]
    public void LimitStage_LimitsCorrectly()
    {
        // Arrange
        var documents = CreateTestDocuments();
        var stage = new LimitStage(3);

        // Act
        var result = stage.Execute(documents).ToList();

        // Assert
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void LimitStage_Zero_ReturnsEmpty()
    {
        // Arrange
        var documents = CreateTestDocuments();
        var stage = new LimitStage(0);

        // Act
        var result = stage.Execute(documents).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void LimitStage_MoreThanAvailable_ReturnsAll()
    {
        // Arrange
        var documents = CreateTestDocuments();
        var stage = new LimitStage(100);

        // Act
        var result = stage.Execute(documents).ToList();

        // Assert
        Assert.Equal(5, result.Count);
    }

    [Fact]
    public void LimitStage_NegativeLimit_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new LimitStage(-1));
    }

    #endregion

    #region SkipStage Tests

    [Fact]
    public void SkipStage_SkipsCorrectly()
    {
        // Arrange
        var documents = CreateTestDocuments();
        var stage = new SkipStage(2);

        // Act
        var result = stage.Execute(documents).ToList();

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("3", result[0].Id);
        Assert.Equal("4", result[1].Id);
        Assert.Equal("5", result[2].Id);
    }

    [Fact]
    public void SkipStage_Zero_ReturnsAll()
    {
        // Arrange
        var documents = CreateTestDocuments();
        var stage = new SkipStage(0);

        // Act
        var result = stage.Execute(documents).ToList();

        // Assert
        Assert.Equal(5, result.Count);
    }

    [Fact]
    public void SkipStage_MoreThanAvailable_ReturnsEmpty()
    {
        // Arrange
        var documents = CreateTestDocuments();
        var stage = new SkipStage(100);

        // Act
        var result = stage.Execute(documents).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void SkipStage_NegativeSkip_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new SkipStage(-1));
    }

    #endregion

    #region Pipeline Integration Tests

    [Fact]
    public void Pipeline_MatchThenGroup_AggregatesFiltered()
    {
        // Arrange
        var documents = CreateTestDocuments();
        var pipeline = new AggregationPipeline()
            .AddStage(new MatchStage(_filterEngine, QueryFilter.Eq("category", "A")))
            .AddStage(new GroupStage(new Dictionary<string, GroupFieldSpec>
            {
                ["total"] = Aggregation.Sum("$value"),
                ["count"] = Aggregation.Count()
            }));

        // Act
        var result = pipeline.Execute(documents);

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Documents);
        Assert.Equal(40.0, result.Documents[0].Data!["total"]);
        Assert.Equal(2, result.Documents[0].Data["count"]);
    }

    [Fact]
    public void Pipeline_SortThenLimit_ReturnsTopN()
    {
        // Arrange
        var documents = CreateTestDocuments();
        var pipeline = new AggregationPipeline()
            .AddStage(new SortStage("value", false)) // Descending
            .AddStage(new LimitStage(2));

        // Act
        var result = pipeline.Execute(documents);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Count);
        Assert.Equal("5", result.Documents[0].Id); // value 50
        Assert.Equal("4", result.Documents[1].Id); // value 40
    }

    [Fact]
    public void Pipeline_SkipThenLimit_Paginates()
    {
        // Arrange
        var documents = CreateTestDocuments();
        var pipeline = new AggregationPipeline()
            .AddStage(new SkipStage(2))  // Skip first 2
            .AddStage(new LimitStage(2)); // Take next 2

        // Act
        var result = pipeline.Execute(documents);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Count);
        Assert.Equal("3", result.Documents[0].Id);
        Assert.Equal("4", result.Documents[1].Id);
    }

    [Fact]
    public void Pipeline_GroupThenSort_SortsGroups()
    {
        // Arrange
        var documents = CreateTestDocuments();
        var pipeline = new AggregationPipeline()
            .AddStage(new GroupStage("$category", new Dictionary<string, GroupFieldSpec>
            {
                ["total"] = Aggregation.Sum("$value")
            }))
            .AddStage(new SortStage("total", false)); // Descending by total

        // Act
        var result = pipeline.Execute(documents);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, result.Count);
        // Descending order by total: B (60), C (50), A (40)
        Assert.Equal("B", result.Documents[0].Data!["_id"]); // 60
        Assert.Equal("C", result.Documents[1].Data!["_id"]); // 50
        Assert.Equal("A", result.Documents[2].Data!["_id"]); // 40
    }

    [Fact]
    public void Pipeline_ComplexAggregation_CalculatesCorrectly()
    {
        // Arrange
        var documents = CreateTestDocuments();
        var pipeline = new AggregationPipeline()
            .AddStage(new MatchStage(_filterEngine, QueryFilter.Gte("value", 20))) // Items 2,3,4,5
            .AddStage(new GroupStage("$category", new Dictionary<string, GroupFieldSpec>
            {
                ["count"] = Aggregation.Count(),
                ["total"] = Aggregation.Sum("$value"),
                ["avg"] = Aggregation.Avg("$value"),
                ["items"] = Aggregation.Push("$name")
            }))
            .AddStage(new SortStage("total", false))
            .AddStage(new LimitStage(2));

        // Act
        var result = pipeline.Execute(documents);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Count);
        
        // B: 20+40 = 60
        var groupB = result.Documents.First(d => "B".Equals(d.Data!["_id"]));
        Assert.Equal(2, groupB.Data!["count"]);
        Assert.Equal(60.0, groupB.Data["total"]);
        Assert.Equal(30.0, groupB.Data["avg"]);
    }

    [Fact]
    public void Pipeline_Async_ExecutesCorrectly()
    {
        // Arrange
        var documents = CreateTestDocuments();
        var pipeline = new AggregationPipeline()
            .AddStage(new LimitStage(3));

        // Act
        var result = pipeline.ExecuteAsync(documents).Result;

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Pipeline_Cancellation_ThrowsOperationCanceled()
    {
        // Arrange
        var documents = CreateTestDocuments();
        var pipeline = new AggregationPipeline()
            .AddStage(new LimitStage(3));
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        Assert.Throws<AggregateException>(() => pipeline.ExecuteAsync(documents, cts.Token).Result);
    }

    #endregion

    #region AggregationPipelineBuilder Tests

    [Fact]
    public void Builder_Match_CreatesMatchStage()
    {
        // Arrange & Act
        var builder = new AggregationPipelineBuilder(_filterEngine)
            .Match(QueryFilter.Eq("category", "A"));

        var pipeline = builder.Build();

        // Assert
        Assert.Equal(1, pipeline.StageCount);
    }

    [Fact]
    public void Builder_Group_CreatesGroupStage()
    {
        // Arrange & Act
        var builder = new AggregationPipelineBuilder(_filterEngine)
            .Group(new Dictionary<string, GroupFieldSpec> { ["count"] = Aggregation.Count() });

        var pipeline = builder.Build();

        // Assert
        Assert.Equal(1, pipeline.StageCount);
    }

    [Fact]
    public void Builder_Chaining_CreatesPipeline()
    {
        // Arrange & Act
        var builder = new AggregationPipelineBuilder(_filterEngine)
            .Match(QueryFilter.Gte("value", 10))
            .Group("$category", new Dictionary<string, GroupFieldSpec>
            {
                ["total"] = Aggregation.Sum("$value")
            })
            .Sort("total", false)
            .Limit(5);

        var pipeline = builder.Build();

        // Assert
        Assert.Equal(4, pipeline.StageCount);
    }

    [Fact]
    public void Builder_Execute_ReturnsResult()
    {
        // Arrange
        var documents = CreateTestDocuments();
        var builder = new AggregationPipelineBuilder(_filterEngine)
            .Limit(3);

        // Act
        var result = builder.Execute(documents);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Builder_StaticNew_CreatesBuilder()
    {
        // Arrange & Act
        var builder = Aggregation.New(_filterEngine)
            .Limit(5);

        // Assert
        Assert.NotNull(builder);
    }

    [Fact]
    public void Builder_StaticAggregationHelpers_CreateSpecs()
    {
        // Act
        var sum = Aggregation.Sum("$value");
        var avg = Aggregation.Avg("$value");
        var min = Aggregation.Min("$value");
        var max = Aggregation.Max("$value");
        var count = Aggregation.Count();

        // Assert
        Assert.Equal(GroupOperator.Sum, sum.Operator);
        Assert.Equal("$value", sum.FieldPath);
        Assert.Equal(GroupOperator.Avg, avg.Operator);
        Assert.Equal(GroupOperator.Min, min.Operator);
        Assert.Equal(GroupOperator.Max, max.Operator);
        Assert.Equal(GroupOperator.Count, count.Operator);
        Assert.Null(count.FieldPath);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public void GroupStage_InvalidOperator_ThrowsException()
    {
        // Arrange
        var documents = CreateTestDocuments();
        var aggregations = new Dictionary<string, GroupFieldSpec>
        {
            ["invalid"] = new((GroupOperator)999, "$value")
        };
        var stage = new GroupStage(aggregations);

        // Act & Assert
        Assert.Throws<AggregationStageException>(() => stage.Execute(documents).ToList());
    }

    [Fact]
    public void Pipeline_StageException_ReturnsFailure()
    {
        // Arrange
        var documents = CreateTestDocuments();
        // Create a pipeline that would fail (mixing inclusion/exclusion badly)
        var pipeline = new AggregationPipeline()
            .AddStage(new ProjectStage(new Dictionary<string, bool>
            {
                ["name"] = true,
                ["value"] = false // This should cause an error
            }));

        // Act
        var result = pipeline.Execute(documents);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    #endregion
}
