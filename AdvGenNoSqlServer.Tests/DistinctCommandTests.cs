// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Query.Execution;
using AdvGenNoSqlServer.Query.Filtering;
using AdvGenNoSqlServer.Query.Models;
using AdvGenNoSqlServer.Storage;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for the DISTINCT command functionality
/// </summary>
public class DistinctCommandTests
{
    private readonly DocumentStore _documentStore;
    private readonly FilterEngine _filterEngine;
    private readonly QueryExecutor _queryExecutor;

    public DistinctCommandTests()
    {
        _documentStore = new DocumentStore();
        _filterEngine = new FilterEngine();
        _queryExecutor = new QueryExecutor(_documentStore, _filterEngine);
    }

    #region Basic Distinct Tests

    [Fact]
    public async Task DistinctAsync_EmptyCollection_ReturnsEmptyResult()
    {
        // Arrange
        await _documentStore.CreateCollectionAsync("products");

        // Act
        var result = await _queryExecutor.DistinctAsync("products", "category");

        // Assert
        Assert.True(result.Success);
        Assert.Empty(result.Values);
        Assert.Equal(0, result.Count);
        Assert.Equal("products", result.CollectionName);
        Assert.Equal("category", result.FieldName);
    }

    [Fact]
    public async Task DistinctAsync_NonExistentCollection_ReturnsFailure()
    {
        // Act
        var result = await _queryExecutor.DistinctAsync("nonexistent", "field");

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("not found", result.ErrorMessage);
    }

    [Fact]
    public async Task DistinctAsync_SingleDocument_ReturnsSingleValue()
    {
        // Arrange
        await _documentStore.CreateCollectionAsync("products");
        await _documentStore.InsertAsync("products", new Document
        {
            Id = "prod1",
            Data = new Dictionary<string, object>
            {
                ["name"] = "Laptop",
                ["category"] = "Electronics"
            }
        });

        // Act
        var result = await _queryExecutor.DistinctAsync("products", "category");

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Values);
        Assert.Contains("Electronics", result.Values);
    }

    [Fact]
    public async Task DistinctAsync_MultipleDocuments_ReturnsDistinctValues()
    {
        // Arrange
        await _documentStore.CreateCollectionAsync("products");
        await _documentStore.InsertAsync("products", new Document
        {
            Id = "prod1",
            Data = new Dictionary<string, object> { ["category"] = "Electronics" }
        });
        await _documentStore.InsertAsync("products", new Document
        {
            Id = "prod2",
            Data = new Dictionary<string, object> { ["category"] = "Clothing" }
        });
        await _documentStore.InsertAsync("products", new Document
        {
            Id = "prod3",
            Data = new Dictionary<string, object> { ["category"] = "Electronics" } // Duplicate
        });

        // Act
        var result = await _queryExecutor.DistinctAsync("products", "category");

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Count);
        Assert.Contains("Electronics", result.Values);
        Assert.Contains("Clothing", result.Values);
    }

    #endregion

    #region Data Type Tests

    [Fact]
    public async Task DistinctAsync_StringValues_WorksCorrectly()
    {
        // Arrange
        await _documentStore.CreateCollectionAsync("items");
        await _documentStore.InsertAsync("items", new Document
        {
            Id = "1",
            Data = new Dictionary<string, object> { ["status"] = "active" }
        });
        await _documentStore.InsertAsync("items", new Document
        {
            Id = "2",
            Data = new Dictionary<string, object> { ["status"] = "inactive" }
        });
        await _documentStore.InsertAsync("items", new Document
        {
            Id = "3",
            Data = new Dictionary<string, object> { ["status"] = "active" }
        });

        // Act
        var result = await _queryExecutor.DistinctAsync("items", "status");

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Count);
        Assert.Contains("active", result.Values);
        Assert.Contains("inactive", result.Values);
    }

    [Fact]
    public async Task DistinctAsync_IntegerValues_WorksCorrectly()
    {
        // Arrange
        await _documentStore.CreateCollectionAsync("orders");
        await _documentStore.InsertAsync("orders", new Document
        {
            Id = "1",
            Data = new Dictionary<string, object> { ["priority"] = 1 }
        });
        await _documentStore.InsertAsync("orders", new Document
        {
            Id = "2",
            Data = new Dictionary<string, object> { ["priority"] = 2 }
        });
        await _documentStore.InsertAsync("orders", new Document
        {
            Id = "3",
            Data = new Dictionary<string, object> { ["priority"] = 1 }
        });

        // Act
        var result = await _queryExecutor.DistinctAsync("orders", "priority");

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Count);
        Assert.Contains(1, result.Values);
        Assert.Contains(2, result.Values);
    }

    [Fact]
    public async Task DistinctAsync_DoubleValues_WorksCorrectly()
    {
        // Arrange
        await _documentStore.CreateCollectionAsync("measurements");
        await _documentStore.InsertAsync("measurements", new Document
        {
            Id = "1",
            Data = new Dictionary<string, object> { ["temperature"] = 23.5 }
        });
        await _documentStore.InsertAsync("measurements", new Document
        {
            Id = "2",
            Data = new Dictionary<string, object> { ["temperature"] = 25.0 }
        });
        await _documentStore.InsertAsync("measurements", new Document
        {
            Id = "3",
            Data = new Dictionary<string, object> { ["temperature"] = 23.5 }
        });

        // Act
        var result = await _queryExecutor.DistinctAsync("measurements", "temperature");

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Count);
        Assert.Contains(23.5, result.Values);
        Assert.Contains(25.0, result.Values);
    }

    [Fact]
    public async Task DistinctAsync_BooleanValues_WorksCorrectly()
    {
        // Arrange
        await _documentStore.CreateCollectionAsync("tasks");
        await _documentStore.InsertAsync("tasks", new Document
        {
            Id = "1",
            Data = new Dictionary<string, object> { ["completed"] = true }
        });
        await _documentStore.InsertAsync("tasks", new Document
        {
            Id = "2",
            Data = new Dictionary<string, object> { ["completed"] = false }
        });
        await _documentStore.InsertAsync("tasks", new Document
        {
            Id = "3",
            Data = new Dictionary<string, object> { ["completed"] = true }
        });

        // Act
        var result = await _queryExecutor.DistinctAsync("tasks", "completed");

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Count);
        Assert.Contains(true, result.Values);
        Assert.Contains(false, result.Values);
    }

    [Fact]
    public async Task DistinctAsync_DateTimeValues_WorksCorrectly()
    {
        // Arrange
        var date1 = new DateTime(2024, 1, 15);
        var date2 = new DateTime(2024, 2, 20);

        await _documentStore.CreateCollectionAsync("events");
        await _documentStore.InsertAsync("events", new Document
        {
            Id = "1",
            Data = new Dictionary<string, object> { ["eventDate"] = date1 }
        });
        await _documentStore.InsertAsync("events", new Document
        {
            Id = "2",
            Data = new Dictionary<string, object> { ["eventDate"] = date2 }
        });
        await _documentStore.InsertAsync("events", new Document
        {
            Id = "3",
            Data = new Dictionary<string, object> { ["eventDate"] = date1 }
        });

        // Act
        var result = await _queryExecutor.DistinctAsync("events", "eventDate");

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Count);
        Assert.Contains(date1, result.Values);
        Assert.Contains(date2, result.Values);
    }

    #endregion

    #region Null Value Tests

    [Fact]
    public async Task DistinctAsync_MixedNullAndNonNullValues_IncludesNull()
    {
        // Arrange
        await _documentStore.CreateCollectionAsync("items");
        await _documentStore.InsertAsync("items", new Document
        {
            Id = "1",
            Data = new Dictionary<string, object> { ["optionalField"] = "value1" }
        });
        await _documentStore.InsertAsync("items", new Document
        {
            Id = "2",
            Data = new Dictionary<string, object> { ["optionalField"] = null! }
        });
        await _documentStore.InsertAsync("items", new Document
        {
            Id = "3",
            Data = new Dictionary<string, object> { ["optionalField"] = "value1" }
        });

        // Act
        var result = await _queryExecutor.DistinctAsync("items", "optionalField");

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Count);
        Assert.Contains("value1", result.Values);
        Assert.Contains(null, result.Values);
    }

    [Fact]
    public async Task DistinctAsync_AllNullValues_ReturnsSingleNull()
    {
        // Arrange
        await _documentStore.CreateCollectionAsync("items");
        await _documentStore.InsertAsync("items", new Document
        {
            Id = "1",
            Data = new Dictionary<string, object> { ["field"] = null! }
        });
        await _documentStore.InsertAsync("items", new Document
        {
            Id = "2",
            Data = new Dictionary<string, object> { ["field"] = null! }
        });

        // Act
        var result = await _queryExecutor.DistinctAsync("items", "field");

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Values);
        Assert.Contains(null, result.Values);
    }

    [Fact]
    public async Task DistinctAsync_MissingField_ReturnsSingleNull()
    {
        // Arrange
        await _documentStore.CreateCollectionAsync("items");
        await _documentStore.InsertAsync("items", new Document
        {
            Id = "1",
            Data = new Dictionary<string, object> { ["otherField"] = "value" }
        });
        await _documentStore.InsertAsync("items", new Document
        {
            Id = "2",
            Data = new Dictionary<string, object> { ["anotherField"] = "value2" }
        });

        // Act
        var result = await _queryExecutor.DistinctAsync("items", "missingField");

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Values);
        Assert.Contains(null, result.Values);
    }

    #endregion

    #region Filter Tests

    [Fact]
    public async Task DistinctAsync_WithFilter_AppliesFilterBeforeDistinct()
    {
        // Arrange
        await _documentStore.CreateCollectionAsync("products");
        await _documentStore.InsertAsync("products", new Document
        {
            Id = "1",
            Data = new Dictionary<string, object> { ["category"] = "A", ["active"] = true }
        });
        await _documentStore.InsertAsync("products", new Document
        {
            Id = "2",
            Data = new Dictionary<string, object> { ["category"] = "B", ["active"] = false }
        });
        await _documentStore.InsertAsync("products", new Document
        {
            Id = "3",
            Data = new Dictionary<string, object> { ["category"] = "A", ["active"] = true }
        });

        var filter = QueryFilter.Eq("active", true);

        // Act
        var result = await _queryExecutor.DistinctAsync("products", "category", filter);

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Values); // Only "A" because B is inactive
        Assert.Contains("A", result.Values);
    }

    [Fact]
    public async Task DistinctAsync_WithComplexFilter_WorksCorrectly()
    {
        // Arrange
        await _documentStore.CreateCollectionAsync("orders");
        await _documentStore.InsertAsync("orders", new Document
        {
            Id = "1",
            Data = new Dictionary<string, object> { ["status"] = "pending", ["priority"] = 1 }
        });
        await _documentStore.InsertAsync("orders", new Document
        {
            Id = "2",
            Data = new Dictionary<string, object> { ["status"] = "completed", ["priority"] = 2 }
        });
        await _documentStore.InsertAsync("orders", new Document
        {
            Id = "3",
            Data = new Dictionary<string, object> { ["status"] = "pending", ["priority"] = 1 }
        });

        var filter = QueryFilter.Eq("priority", 1);

        // Act
        var result = await _queryExecutor.DistinctAsync("orders", "status", filter);

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Values);
        Assert.Contains("pending", result.Values);
    }

    #endregion

    #region Performance and Edge Cases

    [Fact]
    public async Task DistinctAsync_LargeNumberOfDocuments_WorksCorrectly()
    {
        // Arrange
        await _documentStore.CreateCollectionAsync("large");
        for (int i = 0; i < 100; i++)
        {
            await _documentStore.InsertAsync("large", new Document
            {
                Id = $"doc{i}",
                Data = new Dictionary<string, object> { ["group"] = i % 10 } // 10 distinct groups
            });
        }

        // Act
        var result = await _queryExecutor.DistinctAsync("large", "group");

        // Assert
        Assert.True(result.Success);
        Assert.Equal(10, result.Count);
        for (int i = 0; i < 10; i++)
        {
            Assert.Contains(i, result.Values);
        }
    }

    [Fact]
    public async Task DistinctAsync_AllUniqueValues_ReturnsAllValues()
    {
        // Arrange
        await _documentStore.CreateCollectionAsync("unique");
        for (int i = 0; i < 50; i++)
        {
            await _documentStore.InsertAsync("unique", new Document
            {
                Id = $"doc{i}",
                Data = new Dictionary<string, object> { ["uniqueField"] = $"value{i}" }
            });
        }

        // Act
        var result = await _queryExecutor.DistinctAsync("unique", "uniqueField");

        // Assert
        Assert.True(result.Success);
        Assert.Equal(50, result.Count);
    }

    [Fact]
    public async Task DistinctAsync_EmptyStringValue_WorksCorrectly()
    {
        // Arrange
        await _documentStore.CreateCollectionAsync("items");
        await _documentStore.InsertAsync("items", new Document
        {
            Id = "1",
            Data = new Dictionary<string, object> { ["field"] = "" }
        });
        await _documentStore.InsertAsync("items", new Document
        {
            Id = "2",
            Data = new Dictionary<string, object> { ["field"] = "value" }
        });
        await _documentStore.InsertAsync("items", new Document
        {
            Id = "3",
            Data = new Dictionary<string, object> { ["field"] = "" }
        });

        // Act
        var result = await _queryExecutor.DistinctAsync("items", "field");

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Count);
        Assert.Contains("", result.Values);
        Assert.Contains("value", result.Values);
    }

    [Fact]
    public async Task DistinctAsync_SetsExecutionTime()
    {
        // Arrange
        await _documentStore.CreateCollectionAsync("test");
        await _documentStore.InsertAsync("test", new Document
        {
            Id = "1",
            Data = new Dictionary<string, object> { ["field"] = "value" }
        });

        // Act
        var result = await _queryExecutor.DistinctAsync("test", "field");

        // Assert
        Assert.True(result.Success);
        Assert.True(result.ExecutionTimeMs >= 0);
    }

    #endregion

    #region Nested Field Tests

    [Fact]
    public async Task DistinctAsync_NestedField_WorksCorrectly()
    {
        // Arrange
        await _documentStore.CreateCollectionAsync("users");
        await _documentStore.InsertAsync("users", new Document
        {
            Id = "1",
            Data = new Dictionary<string, object>
            {
                ["profile"] = new Dictionary<string, object> { ["city"] = "New York" }
            }
        });
        await _documentStore.InsertAsync("users", new Document
        {
            Id = "2",
            Data = new Dictionary<string, object>
            {
                ["profile"] = new Dictionary<string, object> { ["city"] = "Los Angeles" }
            }
        });
        await _documentStore.InsertAsync("users", new Document
        {
            Id = "3",
            Data = new Dictionary<string, object>
            {
                ["profile"] = new Dictionary<string, object> { ["city"] = "New York" }
            }
        });

        // Act
        var result = await _queryExecutor.DistinctAsync("users", "profile.city");

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Count);
        Assert.Contains("New York", result.Values);
        Assert.Contains("Los Angeles", result.Values);
    }

    #endregion
}
