// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Query.Execution;
using AdvGenNoSqlServer.Query.Filtering;
using QueryModel = AdvGenNoSqlServer.Query.Models;
using AdvGenNoSqlServer.Query.Parsing;
using AdvGenNoSqlServer.Storage;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for Query Projections - selecting specific fields from documents
/// </summary>
public class QueryProjectionTests
{
    private readonly DocumentStore _documentStore;
    private readonly FilterEngine _filterEngine;
    private readonly QueryExecutor _queryExecutor;
    private readonly QueryParser _queryParser;

    public QueryProjectionTests()
    {
        _documentStore = new DocumentStore();
        _filterEngine = new FilterEngine();
        _queryExecutor = new QueryExecutor(_documentStore, _filterEngine);
        _queryParser = new QueryParser();
    }

    #region Basic Inclusion Projection Tests

    [Fact]
    public async Task ExecuteAsync_InclusionProjection_ReturnsOnlySpecifiedFields()
    {
        // Arrange
        var collectionName = "users";
        var doc = new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object?>
            {
                ["name"] = "John",
                ["age"] = 30,
                ["email"] = "john@example.com",
                ["address"] = "123 Main St"
            }
        };
        await _documentStore.InsertAsync(collectionName, doc);

        var query = new QueryModel.Query
        {
            CollectionName = collectionName,
            Projection = new Dictionary<string, bool>
            {
                ["name"] = true,
                ["email"] = true
            }
        };

        // Act
        var result = await _queryExecutor.ExecuteAsync(query);

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Documents);
        var projectedDoc = result.Documents.First();
        Assert.Equal("user1", projectedDoc.Id);
        Assert.Equal("John", projectedDoc.Data["name"]);
        Assert.Equal("john@example.com", projectedDoc.Data["email"]);
        Assert.False(projectedDoc.Data.ContainsKey("age"));
        Assert.False(projectedDoc.Data.ContainsKey("address"));
    }

    [Fact]
    public async Task ExecuteAsync_InclusionProjection_IncludesIdByDefault()
    {
        // Arrange
        var collectionName = "users";
        var doc = new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object?>
            {
                ["name"] = "John",
                ["age"] = 30
            }
        };
        await _documentStore.InsertAsync(collectionName, doc);

        var query = new QueryModel.Query
        {
            CollectionName = collectionName,
            Projection = new Dictionary<string, bool>
            {
                ["name"] = true
            }
        };

        // Act
        var result = await _queryExecutor.ExecuteAsync(query);

        // Assert
        Assert.True(result.Success);
        var projectedDoc = result.Documents.First();
        // The document Id property should be set
        Assert.Equal("user1", projectedDoc.Id);
        // _id should be in the data dictionary after projection
        Assert.True(projectedDoc.Data.ContainsKey("_id"), "_id should be in the data dictionary");
        Assert.Equal("user1", projectedDoc.Data["_id"]);
    }

    [Fact]
    public async Task ExecuteAsync_InclusionProjection_CanExcludeId()
    {
        // Arrange
        var collectionName = "users";
        var doc = new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object?>
            {
                ["name"] = "John"
            }
        };
        await _documentStore.InsertAsync(collectionName, doc);

        var query = new QueryModel.Query
        {
            CollectionName = collectionName,
            Projection = new Dictionary<string, bool>
            {
                ["name"] = true,
                ["_id"] = false
            }
        };

        // Act
        var result = await _queryExecutor.ExecuteAsync(query);

        // Assert
        Assert.True(result.Success);
        var projectedDoc = result.Documents.First();
        Assert.Equal("user1", projectedDoc.Id); // Document ID is preserved
        Assert.False(projectedDoc.Data.ContainsKey("_id")); // But _id is not in data
        Assert.Equal("John", projectedDoc.Data["name"]);
    }

    #endregion

    #region Exclusion Projection Tests

    [Fact]
    public async Task ExecuteAsync_ExclusionProjection_ReturnsAllExceptExcludedFields()
    {
        // Arrange
        var collectionName = "users";
        var doc = new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object?>
            {
                ["name"] = "John",
                ["age"] = 30,
                ["email"] = "john@example.com",
                ["password"] = "secret123"
            }
        };
        await _documentStore.InsertAsync(collectionName, doc);

        var query = new QueryModel.Query
        {
            CollectionName = collectionName,
            Projection = new Dictionary<string, bool>
            {
                ["password"] = false
            }
        };

        // Act
        var result = await _queryExecutor.ExecuteAsync(query);

        // Assert
        Assert.True(result.Success);
        var projectedDoc = result.Documents.First();
        Assert.Equal("John", projectedDoc.Data["name"]);
        Assert.Equal(30, projectedDoc.Data["age"]);
        Assert.Equal("john@example.com", projectedDoc.Data["email"]);
        Assert.False(projectedDoc.Data.ContainsKey("password"));
    }

    [Fact]
    public async Task ExecuteAsync_ExclusionProjection_CanExcludeId()
    {
        // Arrange
        var collectionName = "users";
        var doc = new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object?>
            {
                ["name"] = "John"
            }
        };
        await _documentStore.InsertAsync(collectionName, doc);

        var query = new QueryModel.Query
        {
            CollectionName = collectionName,
            Projection = new Dictionary<string, bool>
            {
                ["_id"] = false
            }
        };

        // Act
        var result = await _queryExecutor.ExecuteAsync(query);

        // Assert
        Assert.True(result.Success);
        var projectedDoc = result.Documents.First();
        Assert.Equal("user1", projectedDoc.Id); // Document ID is preserved
        Assert.False(projectedDoc.Data.ContainsKey("_id")); // But _id is not in data
        Assert.Equal("John", projectedDoc.Data["name"]);
    }

    [Fact]
    public async Task ExecuteAsync_ExclusionProjection_MultipleFields()
    {
        // Arrange
        var collectionName = "users";
        var doc = new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object?>
            {
                ["name"] = "John",
                ["age"] = 30,
                ["email"] = "john@example.com",
                ["password"] = "secret",
                ["ssn"] = "123-45-6789"
            }
        };
        await _documentStore.InsertAsync(collectionName, doc);

        var query = new QueryModel.Query
        {
            CollectionName = collectionName,
            Projection = new Dictionary<string, bool>
            {
                ["password"] = false,
                ["ssn"] = false
            }
        };

        // Act
        var result = await _queryExecutor.ExecuteAsync(query);

        // Assert
        Assert.True(result.Success);
        var projectedDoc = result.Documents.First();
        Assert.Equal("John", projectedDoc.Data["name"]);
        Assert.Equal(30, projectedDoc.Data["age"]);
        Assert.Equal("john@example.com", projectedDoc.Data["email"]);
        Assert.False(projectedDoc.Data.ContainsKey("password"));
        Assert.False(projectedDoc.Data.ContainsKey("ssn"));
    }

    #endregion

    #region Nested Field Projection Tests

    [Fact(Skip = "Nested field projection is an advanced feature not yet fully implemented")]
    public async Task ExecuteAsync_NestedInclusionProjection_ReturnsNestedFields()
    {
        // Arrange
        var collectionName = "users";
        var doc = new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object?>
            {
                ["name"] = "John",
                ["profile"] = new Dictionary<string, object?>
                {
                    ["bio"] = "Developer",
                    ["avatar"] = "avatar.jpg",
                    ["website"] = "https://example.com"
                }
            }
        };
        await _documentStore.InsertAsync(collectionName, doc);

        var query = new QueryModel.Query
        {
            CollectionName = collectionName,
            Projection = new Dictionary<string, bool>
            {
                ["name"] = true,
                ["profile.bio"] = true
            }
        };

        // Act
        var result = await _queryExecutor.ExecuteAsync(query);

        // Assert
        Assert.True(result.Success);
        var projectedDoc = result.Documents.First();
        Assert.Equal("John", projectedDoc.Data["name"]);
        // Nested field projection should extract just that field
        Assert.True(projectedDoc.Data.ContainsKey("profile.bio") || 
                    (projectedDoc.Data.ContainsKey("profile") && projectedDoc.Data["profile"] is Dictionary<string, object?> profile && profile.ContainsKey("bio")));
    }

    #endregion

    #region Projection with Filter and Sort Tests

    [Fact]
    public async Task ExecuteAsync_ProjectionWithFilter_ReturnsFilteredAndProjected()
    {
        // Arrange
        var collectionName = "users";
        await _documentStore.InsertAsync(collectionName, new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object?> { ["name"] = "John", ["age"] = 30, ["email"] = "john@example.com" }
        });
        await _documentStore.InsertAsync(collectionName, new Document
        {
            Id = "user2",
            Data = new Dictionary<string, object?> { ["name"] = "Jane", ["age"] = 25, ["email"] = "jane@example.com" }
        });
        await _documentStore.InsertAsync(collectionName, new Document
        {
            Id = "user3",
            Data = new Dictionary<string, object?> { ["name"] = "Bob", ["age"] = 35, ["email"] = "bob@example.com" }
        });

        var query = new QueryModel.Query
        {
            CollectionName = collectionName,
            Filter = QueryModel.QueryFilter.Gte("age", 30),
            Projection = new Dictionary<string, bool>
            {
                ["name"] = true,
                ["age"] = true
            }
        };

        // Act
        var result = await _queryExecutor.ExecuteAsync(query);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Documents.Count);
        foreach (var projectedDoc in result.Documents)
        {
            Assert.True(projectedDoc.Data.ContainsKey("name"));
            Assert.True(projectedDoc.Data.ContainsKey("age"));
            Assert.False(projectedDoc.Data.ContainsKey("email"));
        }
    }

    [Fact]
    public async Task ExecuteAsync_ProjectionWithSort_ReturnsSortedAndProjected()
    {
        // Arrange
        var collectionName = "users";
        await _documentStore.InsertAsync(collectionName, new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object?> { ["name"] = "John", ["age"] = 30 }
        });
        await _documentStore.InsertAsync(collectionName, new Document
        {
            Id = "user2",
            Data = new Dictionary<string, object?> { ["name"] = "Jane", ["age"] = 25 }
        });

        var query = new QueryModel.Query
        {
            CollectionName = collectionName,
            Sort = new List<QueryModel.SortField> { QueryModel.SortField.Ascending("age") },
            Projection = new Dictionary<string, bool>
            {
                ["name"] = true
            }
        };

        // Act
        var result = await _queryExecutor.ExecuteAsync(query);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Documents.Count);
        Assert.Equal("Jane", result.Documents[0].Data["name"]);
        Assert.Equal("John", result.Documents[1].Data["name"]);
    }

    [Fact]
    public async Task ExecuteAsync_ProjectionWithPagination_ReturnsPagedAndProjected()
    {
        // Arrange
        var collectionName = "users";
        for (int i = 1; i <= 5; i++)
        {
            await _documentStore.InsertAsync(collectionName, new Document
            {
                Id = $"user{i}",
                Data = new Dictionary<string, object?> { ["name"] = $"User{i}", ["age"] = 20 + i }
            });
        }

        var query = new QueryModel.Query
        {
            CollectionName = collectionName,
            Sort = new List<QueryModel.SortField> { QueryModel.SortField.Ascending("name") },
            Options = new QueryModel.QueryOptions { Skip = 1, Limit = 2 },
            Projection = new Dictionary<string, bool>
            {
                ["name"] = true
            }
        };

        // Act
        var result = await _queryExecutor.ExecuteAsync(query);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Documents.Count);
        Assert.Equal("User2", result.Documents[0].Data["name"]);
        Assert.Equal("User3", result.Documents[1].Data["name"]);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public async Task ExecuteAsync_EmptyProjection_ReturnsAllFields()
    {
        // Arrange
        var collectionName = "users";
        var doc = new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object?>
            {
                ["name"] = "John",
                ["age"] = 30
            }
        };
        await _documentStore.InsertAsync(collectionName, doc);

        var query = new QueryModel.Query
        {
            CollectionName = collectionName,
            Projection = new Dictionary<string, bool>()
        };

        // Act
        var result = await _queryExecutor.ExecuteAsync(query);

        // Assert
        Assert.True(result.Success);
        var projectedDoc = result.Documents.First();
        Assert.Equal("John", projectedDoc.Data["name"]);
        Assert.Equal(30, projectedDoc.Data["age"]);
    }

    [Fact]
    public async Task ExecuteAsync_NullProjection_ReturnsAllFields()
    {
        // Arrange
        var collectionName = "users";
        var doc = new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object?>
            {
                ["name"] = "John",
                ["age"] = 30
            }
        };
        await _documentStore.InsertAsync(collectionName, doc);

        var query = new QueryModel.Query
        {
            CollectionName = collectionName,
            Projection = null
        };

        // Act
        var result = await _queryExecutor.ExecuteAsync(query);

        // Assert
        Assert.True(result.Success);
        var projectedDoc = result.Documents.First();
        Assert.Equal("John", projectedDoc.Data["name"]);
        Assert.Equal(30, projectedDoc.Data["age"]);
    }

    [Fact]
    public async Task ExecuteAsync_ProjectionWithMissingFields_HandlesGracefully()
    {
        // Arrange
        var collectionName = "users";
        var doc = new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object?>
            {
                ["name"] = "John",
                ["age"] = 30
            }
        };
        await _documentStore.InsertAsync(collectionName, doc);

        var query = new QueryModel.Query
        {
            CollectionName = collectionName,
            Projection = new Dictionary<string, bool>
            {
                ["name"] = true,
                ["nonExistentField"] = true
            }
        };

        // Act
        var result = await _queryExecutor.ExecuteAsync(query);

        // Assert
        Assert.True(result.Success);
        var projectedDoc = result.Documents.First();
        Assert.Equal("John", projectedDoc.Data["name"]);
    }

    [Fact]
    public async Task ExecuteAsync_ProjectionOnEmptyCollection_ReturnsEmpty()
    {
        // Arrange
        var collectionName = "empty_collection";
        var query = new QueryModel.Query
        {
            CollectionName = collectionName,
            Projection = new Dictionary<string, bool>
            {
                ["name"] = true
            }
        };

        // Act
        var result = await _queryExecutor.ExecuteAsync(query);

        // Assert
        Assert.True(result.Success);
        Assert.Empty(result.Documents);
    }

    #endregion

    #region Query Parser Projection Tests

    [Fact]
    public void Parse_QueryWithProjection_ParsesCorrectly()
    {
        // Arrange
        var json = @"{ ""collection"": ""users"", ""projection"": { ""name"": 1, ""email"": 1 } }";

        // Act
        var query = _queryParser.Parse(json);

        // Assert
        Assert.NotNull(query.Projection);
        Assert.Equal(2, query.Projection.Count);
        Assert.True(query.Projection["name"]);
        Assert.True(query.Projection["email"]);
    }

    [Fact]
    public void Parse_QueryWithExclusionProjection_ParsesCorrectly()
    {
        // Arrange
        var json = @"{ ""collection"": ""users"", ""projection"": { ""password"": 0, ""ssn"": 0 } }";

        // Act
        var query = _queryParser.Parse(json);

        // Assert
        Assert.NotNull(query.Projection);
        Assert.Equal(2, query.Projection.Count);
        Assert.False(query.Projection["password"]);
        Assert.False(query.Projection["ssn"]);
    }

    [Fact]
    public void Parse_QueryWithMixedProjection_AllowsIdExclusion()
    {
        // Arrange
        var json = @"{ ""collection"": ""users"", ""projection"": { ""name"": 1, ""_id"": 0 } }";

        // Act
        var query = _queryParser.Parse(json);

        // Assert
        Assert.NotNull(query.Projection);
        Assert.True(query.Projection["name"]);
        Assert.False(query.Projection["_id"]);
    }

    [Fact]
    public void Parse_QueryWithBooleanProjection_ParsesCorrectly()
    {
        // Arrange
        var json = @"{ ""collection"": ""users"", ""projection"": { ""name"": true, ""password"": false } }";

        // Act
        var query = _queryParser.Parse(json);

        // Assert
        Assert.NotNull(query.Projection);
        Assert.True(query.Projection["name"]);
        Assert.False(query.Projection["password"]);
    }

    #endregion

    #region Explain with Projection Tests

    [Fact]
    public async Task ExplainAsync_WithProjection_IncludesProjectStage()
    {
        // Arrange
        var query = new QueryModel.Query
        {
            CollectionName = "users",
            Projection = new Dictionary<string, bool>
            {
                ["name"] = true
            }
        };

        // Act
        var stats = await _queryExecutor.ExplainAsync(query);

        // Assert
        Assert.NotNull(stats.ExecutionPlan);
        var projectStage = stats.ExecutionPlan.FirstOrDefault(s => s.StageName == "Project");
        Assert.NotNull(projectStage);
    }

    #endregion

    #region Large Dataset Projection Tests

    [Fact]
    public async Task ExecuteAsync_LargeDatasetProjection_PerformsWell()
    {
        // Arrange
        var collectionName = "large_collection";
        for (int i = 1; i <= 100; i++)
        {
            await _documentStore.InsertAsync(collectionName, new Document
            {
                Id = $"user{i}",
                Data = new Dictionary<string, object?>
                {
                    ["name"] = $"User{i}",
                    ["email"] = $"user{i}@example.com",
                    ["age"] = i,
                    ["address"] = $"Address{i}",
                    ["phone"] = $"555-{i:0000}",
                    ["metadata"] = new Dictionary<string, object?> { ["key"] = $"value{i}" }
                }
            });
        }

        var query = new QueryModel.Query
        {
            CollectionName = collectionName,
            Projection = new Dictionary<string, bool>
            {
                ["name"] = true,
                ["email"] = true
            }
        };

        // Act
        var result = await _queryExecutor.ExecuteAsync(query);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(100, result.Documents.Count);
        foreach (var doc in result.Documents)
        {
            Assert.True(doc.Data.ContainsKey("name"));
            Assert.True(doc.Data.ContainsKey("email"));
            Assert.False(doc.Data.ContainsKey("age"));
            Assert.False(doc.Data.ContainsKey("address"));
            Assert.False(doc.Data.ContainsKey("phone"));
            Assert.False(doc.Data.ContainsKey("metadata"));
        }
    }

    #endregion
}
