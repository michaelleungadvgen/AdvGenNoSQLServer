// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Query.Execution;
using AdvGenNoSqlServer.Query.Filtering;
using AdvGenNoSqlServer.Query.Models;
using AdvGenNoSqlServer.Query.Parsing;
using AdvGenNoSqlServer.Storage;
using AdvGenNoSqlServer.Storage.Indexing;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for the Query Engine components
/// </summary>
public class QueryEngineTests
{
    #region QueryParser Tests

    public class QueryParserTests
    {
        private readonly QueryParser _parser = new();

        [Fact]
        public void Parse_SimpleQuery_ReturnsQuery()
        {
            // Arrange
            var json = @"{ ""collection"": ""users"", ""filter"": { ""name"": ""John"" } }";

            // Act
            var query = _parser.Parse(json);

            // Assert
            Assert.Equal("users", query.CollectionName);
            Assert.NotNull(query.Filter);
            Assert.Equal("John", query.Filter.Conditions["name"]);
        }

        [Fact]
        public void Parse_QueryWithSort_ReturnsQueryWithSort()
        {
            // Arrange
            var json = @"{ ""collection"": ""users"", ""sort"": { ""age"": -1, ""name"": 1 } }";

            // Act
            var query = _parser.Parse(json);

            // Assert
            Assert.NotNull(query.Sort);
            Assert.Equal(2, query.Sort.Count);
            Assert.Equal("age", query.Sort[0].FieldName);
            Assert.Equal(SortDirection.Descending, query.Sort[0].Direction);
            Assert.Equal("name", query.Sort[1].FieldName);
            Assert.Equal(SortDirection.Ascending, query.Sort[1].Direction);
        }

        [Fact]
        public void Parse_QueryWithOptions_ReturnsQueryWithOptions()
        {
            // Arrange
            var json = @"{ ""collection"": ""users"", ""options"": { ""limit"": 10, ""skip"": 20, ""includeTotalCount"": true } }";

            // Act
            var query = _parser.Parse(json);

            // Assert
            Assert.NotNull(query.Options);
            Assert.Equal(10, query.Options.Limit);
            Assert.Equal(20, query.Options.Skip);
            Assert.True(query.Options.IncludeTotalCount);
        }

        [Fact]
        public void Parse_QueryWithComplexFilter_ReturnsQuery()
        {
            // Arrange
            var json = @"{ ""collection"": ""users"", ""filter"": { ""age"": { ""$gt"": 18 }, ""status"": ""active"" } }";

            // Act
            var query = _parser.Parse(json);

            // Assert
            Assert.NotNull(query.Filter);
            Assert.Contains("age", query.Filter.Conditions.Keys);
            Assert.Contains("status", query.Filter.Conditions.Keys);
        }

        [Fact]
        public void Parse_InvalidJson_ThrowsQueryParseException()
        {
            // Arrange
            var json = "invalid json";

            // Act & Assert
            Assert.Throws<QueryParseException>(() => _parser.Parse(json));
        }

        [Fact]
        public void Parse_MissingCollection_ThrowsQueryParseException()
        {
            // Arrange
            var json = @"{ ""filter"": { ""name"": ""John"" } }";

            // Act & Assert
            Assert.Throws<QueryParseException>(() => _parser.Parse(json));
        }

        [Fact]
        public void TryParse_ValidJson_ReturnsTrue()
        {
            // Arrange
            var json = @"{ ""collection"": ""users"" }";

            // Act
            var result = _parser.TryParse(json, out var query, out var error);

            // Assert
            Assert.True(result);
            Assert.NotNull(query);
            Assert.Null(error);
        }

        [Fact]
        public void TryParse_InvalidJson_ReturnsFalse()
        {
            // Arrange
            var json = "invalid";

            // Act
            var result = _parser.TryParse(json, out var query, out var error);

            // Assert
            Assert.False(result);
            Assert.Null(query);
            Assert.NotNull(error);
        }

        [Fact]
        public void ParseFilter_SimpleFilter_ReturnsFilter()
        {
            // Arrange
            var json = @"{ ""name"": ""John"", ""age"": 30 }";

            // Act
            var filter = _parser.ParseFilter(json);

            // Assert
            Assert.Equal(2, filter.Conditions.Count);
            Assert.Equal("John", filter.Conditions["name"]);
            Assert.Equal(30L, filter.Conditions["age"]);
        }
    }

    #endregion

    #region FilterEngine Tests

    public class FilterEngineTests
    {
        private readonly FilterEngine _filterEngine = new();

        private static Document CreateDocument(string id, Dictionary<string, object> data)
        {
            return new Document
            {
                Id = id,
                Data = data,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Version = 1
            };
        }

        [Fact]
        public void Matches_NullFilter_ReturnsTrue()
        {
            // Arrange
            var doc = CreateDocument("1", new Dictionary<string, object> { ["name"] = "John" });

            // Act
            var result = _filterEngine.Matches(doc, null);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void Matches_EmptyFilter_ReturnsTrue()
        {
            // Arrange
            var doc = CreateDocument("1", new Dictionary<string, object> { ["name"] = "John" });
            var filter = new QueryFilter();

            // Act
            var result = _filterEngine.Matches(doc, filter);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void Matches_SimpleEquality_ReturnsTrue()
        {
            // Arrange
            var doc = CreateDocument("1", new Dictionary<string, object> { ["name"] = "John" });
            var filter = QueryFilter.Eq("name", "John");

            // Act
            var result = _filterEngine.Matches(doc, filter);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void Matches_SimpleEquality_ReturnsFalse()
        {
            // Arrange
            var doc = CreateDocument("1", new Dictionary<string, object> { ["name"] = "John" });
            var filter = QueryFilter.Eq("name", "Jane");

            // Act
            var result = _filterEngine.Matches(doc, filter);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void Matches_GreaterThan_ReturnsTrue()
        {
            // Arrange
            var doc = CreateDocument("1", new Dictionary<string, object> { ["age"] = 25 });
            var filter = QueryFilter.Gt("age", 18);

            // Act
            var result = _filterEngine.Matches(doc, filter);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void Matches_LessThan_ReturnsFalse()
        {
            // Arrange
            var doc = CreateDocument("1", new Dictionary<string, object> { ["age"] = 15 });
            var filter = QueryFilter.Gt("age", 18);

            // Act
            var result = _filterEngine.Matches(doc, filter);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void Matches_GreaterThanOrEqual_ReturnsTrue()
        {
            // Arrange
            var doc = CreateDocument("1", new Dictionary<string, object> { ["age"] = 18 });
            var filter = QueryFilter.Gte("age", 18);

            // Act
            var result = _filterEngine.Matches(doc, filter);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void Matches_LessThanOrEqual_ReturnsTrue()
        {
            // Arrange
            var doc = CreateDocument("1", new Dictionary<string, object> { ["age"] = 18 });
            var filter = QueryFilter.Lte("age", 18);

            // Act
            var result = _filterEngine.Matches(doc, filter);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void Matches_NotEqual_ReturnsTrue()
        {
            // Arrange
            var doc = CreateDocument("1", new Dictionary<string, object> { ["name"] = "John" });
            var filter = QueryFilter.Ne("name", "Jane");

            // Act
            var result = _filterEngine.Matches(doc, filter);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void Matches_InOperator_ReturnsTrue()
        {
            // Arrange
            var doc = CreateDocument("1", new Dictionary<string, object> { ["status"] = "active" });
            var filter = QueryFilter.In("status", new List<object> { "active", "pending" });

            // Act
            var result = _filterEngine.Matches(doc, filter);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void Matches_InOperator_ReturnsFalse()
        {
            // Arrange
            var doc = CreateDocument("1", new Dictionary<string, object> { ["status"] = "deleted" });
            var filter = QueryFilter.In("status", new List<object> { "active", "pending" });

            // Act
            var result = _filterEngine.Matches(doc, filter);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void Matches_NotInOperator_ReturnsTrue()
        {
            // Arrange
            var doc = CreateDocument("1", new Dictionary<string, object> { ["status"] = "deleted" });
            var filter = QueryFilter.Nin("status", new List<object> { "active", "pending" });

            // Act
            var result = _filterEngine.Matches(doc, filter);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void Matches_AndOperator_ReturnsTrue()
        {
            // Arrange
            var doc = CreateDocument("1", new Dictionary<string, object> { ["age"] = 25, ["status"] = "active" });
            var filter = QueryFilter.Gt("age", 18).And(QueryFilter.Eq("status", "active"));

            // Act
            var result = _filterEngine.Matches(doc, filter);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void Matches_AndOperator_ReturnsFalse()
        {
            // Arrange
            var doc = CreateDocument("1", new Dictionary<string, object> { ["age"] = 15, ["status"] = "active" });
            var filter = QueryFilter.Gt("age", 18).And(QueryFilter.Eq("status", "active"));

            // Act
            var result = _filterEngine.Matches(doc, filter);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void Matches_OrOperator_ReturnsTrue()
        {
            // Arrange
            var doc = CreateDocument("1", new Dictionary<string, object> { ["status"] = "pending" });
            var filter = QueryFilter.Eq("status", "active").Or(QueryFilter.Eq("status", "pending"));

            // Act
            var result = _filterEngine.Matches(doc, filter);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void Matches_OrOperator_ReturnsFalse()
        {
            // Arrange
            var doc = CreateDocument("1", new Dictionary<string, object> { ["status"] = "deleted" });
            var filter = QueryFilter.Eq("status", "active").Or(QueryFilter.Eq("status", "pending"));

            // Act
            var result = _filterEngine.Matches(doc, filter);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void Matches_FieldDoesNotExist_ReturnsFalse()
        {
            // Arrange
            var doc = CreateDocument("1", new Dictionary<string, object> { ["name"] = "John" });
            var filter = QueryFilter.Eq("age", 25);

            // Act
            var result = _filterEngine.Matches(doc, filter);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void Matches_NumericComparison_ReturnsTrue()
        {
            // Arrange
            var doc = CreateDocument("1", new Dictionary<string, object> { ["price"] = 10.5 });
            var filter = QueryFilter.Gt("price", 10);

            // Act
            var result = _filterEngine.Matches(doc, filter);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void Filter_MultipleDocuments_ReturnsMatching()
        {
            // Arrange
            var docs = new List<Document>
            {
                CreateDocument("1", new Dictionary<string, object> { ["status"] = "active" }),
                CreateDocument("2", new Dictionary<string, object> { ["status"] = "inactive" }),
                CreateDocument("3", new Dictionary<string, object> { ["status"] = "active" })
            };
            var filter = QueryFilter.Eq("status", "active");

            // Act
            var result = _filterEngine.Filter(docs, filter).ToList();

            // Assert
            Assert.Equal(2, result.Count);
            Assert.All(result, d => Assert.Equal("active", d.Data!["status"]));
        }

        [Fact]
        public void GetFieldValue_DotNotation_ReturnsValue()
        {
            // Arrange
            var doc = CreateDocument("1", new Dictionary<string, object>
            {
                ["address"] = new Dictionary<string, object>
                {
                    ["city"] = "New York"
                }
            });

            // Act
            var result = _filterEngine.GetFieldValue(doc, "address.city");

            // Assert
            Assert.Equal("New York", result);
        }

        [Fact]
        public void GetFieldValue_NestedFieldDoesNotExist_ReturnsNull()
        {
            // Arrange
            var doc = CreateDocument("1", new Dictionary<string, object>
            {
                ["address"] = new Dictionary<string, object>
                {
                    ["city"] = "New York"
                }
            });

            // Act
            var result = _filterEngine.GetFieldValue(doc, "address.country");

            // Assert
            Assert.Null(result);
        }
    }

    #endregion

    #region QueryExecutor Tests

    public class QueryExecutorTests : IDisposable
    {
        private readonly DocumentStore _documentStore;
        private readonly FilterEngine _filterEngine;
        private readonly QueryExecutor _queryExecutor;
        private readonly IndexManager _indexManager;

        public QueryExecutorTests()
        {
            _documentStore = new DocumentStore();
            _filterEngine = new FilterEngine();
            _indexManager = new IndexManager();
            _queryExecutor = new QueryExecutor(_documentStore, _filterEngine, _indexManager);
        }

        public void Dispose()
        {
            // Cleanup
        }

        private static Document CreateDoc(string id, Dictionary<string, object> data)
        {
            return new Document
            {
                Id = id,
                Data = data,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Version = 1
            };
        }

        [Fact]
        public async Task ExecuteAsync_SimpleQuery_ReturnsAllDocuments()
        {
            // Arrange
            await _documentStore.CreateCollectionAsync("users");
            await _documentStore.InsertAsync("users", CreateDoc("1", new Dictionary<string, object> { ["name"] = "John" }));
            await _documentStore.InsertAsync("users", CreateDoc("2", new Dictionary<string, object> { ["name"] = "Jane" }));

            var query = new AdvGenNoSqlServer.Query.Models.Query
            {
                CollectionName = "users"
            };

            // Act
            var result = await _queryExecutor.ExecuteAsync(query);

            // Assert
            Assert.True(result.Success);
            Assert.Equal(2, result.Documents.Count);
        }

        [Fact]
        public async Task ExecuteAsync_WithFilter_ReturnsMatchingDocuments()
        {
            // Arrange
            await _documentStore.CreateCollectionAsync("users");
            await _documentStore.InsertAsync("users", CreateDoc("1", new Dictionary<string, object> { ["name"] = "John", ["age"] = 25 }));
            await _documentStore.InsertAsync("users", CreateDoc("2", new Dictionary<string, object> { ["name"] = "Jane", ["age"] = 30 }));

            var query = new AdvGenNoSqlServer.Query.Models.Query
            {
                CollectionName = "users",
                Filter = QueryFilter.Gt("age", 25)
            };

            // Act
            var result = await _queryExecutor.ExecuteAsync(query);

            // Assert
            Assert.True(result.Success);
            Assert.Single(result.Documents);
            Assert.Equal("2", result.Documents[0].Id);
        }

        [Fact]
        public async Task ExecuteAsync_WithLimit_ReturnsLimitedDocuments()
        {
            // Arrange
            await _documentStore.CreateCollectionAsync("users");
            for (int i = 1; i <= 10; i++)
            {
                await _documentStore.InsertAsync("users", CreateDoc(i.ToString(), new Dictionary<string, object> { ["id"] = i }));
            }

            var query = new AdvGenNoSqlServer.Query.Models.Query
            {
                CollectionName = "users",
                Options = new QueryOptions { Limit = 5 }
            };

            // Act
            var result = await _queryExecutor.ExecuteAsync(query);

            // Assert
            Assert.True(result.Success);
            Assert.Equal(5, result.Documents.Count);
        }

        [Fact]
        public async Task ExecuteAsync_WithSkip_ReturnsSkippedDocuments()
        {
            // Arrange
            await _documentStore.CreateCollectionAsync("users");
            for (int i = 1; i <= 10; i++)
            {
                await _documentStore.InsertAsync("users", CreateDoc(i.ToString(), new Dictionary<string, object> { ["id"] = i }));
            }

            var query = new AdvGenNoSqlServer.Query.Models.Query
            {
                CollectionName = "users",
                Options = new QueryOptions { Skip = 5 }
            };

            // Act
            var result = await _queryExecutor.ExecuteAsync(query);

            // Assert
            Assert.True(result.Success);
            Assert.Equal(5, result.Documents.Count);
            Assert.Equal(5, result.Skipped);
        }

        [Fact]
        public async Task ExecuteAsync_WithSort_ReturnsSortedDocuments()
        {
            // Arrange
            await _documentStore.CreateCollectionAsync("users");
            await _documentStore.InsertAsync("users", CreateDoc("1", new Dictionary<string, object> { ["age"] = 30 }));
            await _documentStore.InsertAsync("users", CreateDoc("2", new Dictionary<string, object> { ["age"] = 20 }));
            await _documentStore.InsertAsync("users", CreateDoc("3", new Dictionary<string, object> { ["age"] = 25 }));

            var query = new AdvGenNoSqlServer.Query.Models.Query
            {
                CollectionName = "users",
                Sort = new List<SortField> { SortField.Ascending("age") }
            };

            // Act
            var result = await _queryExecutor.ExecuteAsync(query);

            // Assert
            Assert.True(result.Success);
            Assert.Equal(3, result.Documents.Count);
            Assert.Equal("2", result.Documents[0].Id); // age 20
            Assert.Equal("3", result.Documents[1].Id); // age 25
            Assert.Equal("1", result.Documents[2].Id); // age 30
        }

        [Fact]
        public async Task ExecuteAsync_WithTotalCount_ReturnsTotalCount()
        {
            // Arrange
            await _documentStore.CreateCollectionAsync("users");
            for (int i = 1; i <= 10; i++)
            {
                await _documentStore.InsertAsync("users", CreateDoc(i.ToString(), new Dictionary<string, object> { ["id"] = i }));
            }

            var query = new AdvGenNoSqlServer.Query.Models.Query
            {
                CollectionName = "users",
                Options = new QueryOptions { Limit = 5, IncludeTotalCount = true }
            };

            // Act
            var result = await _queryExecutor.ExecuteAsync(query);

            // Assert
            Assert.True(result.Success);
            Assert.Equal(5, result.Documents.Count);
            Assert.Equal(10, result.TotalCount);
        }

        [Fact]
        public async Task CountAsync_ReturnsCorrectCount()
        {
            // Arrange
            await _documentStore.CreateCollectionAsync("users");
            await _documentStore.InsertAsync("users", CreateDoc("1", new Dictionary<string, object> { ["status"] = "active" }));
            await _documentStore.InsertAsync("users", CreateDoc("2", new Dictionary<string, object> { ["status"] = "inactive" }));
            await _documentStore.InsertAsync("users", CreateDoc("3", new Dictionary<string, object> { ["status"] = "active" }));

            var query = new AdvGenNoSqlServer.Query.Models.Query
            {
                CollectionName = "users",
                Filter = QueryFilter.Eq("status", "active")
            };

            // Act
            var count = await _queryExecutor.CountAsync(query);

            // Assert
            Assert.Equal(2, count);
        }

        [Fact]
        public async Task ExistsAsync_DocumentsExist_ReturnsTrue()
        {
            // Arrange
            await _documentStore.CreateCollectionAsync("users");
            await _documentStore.InsertAsync("users", CreateDoc("1", new Dictionary<string, object> { ["status"] = "active" }));

            var query = new AdvGenNoSqlServer.Query.Models.Query
            {
                CollectionName = "users",
                Filter = QueryFilter.Eq("status", "active")
            };

            // Act
            var exists = await _queryExecutor.ExistsAsync(query);

            // Assert
            Assert.True(exists);
        }

        [Fact]
        public async Task ExistsAsync_NoDocumentsExist_ReturnsFalse()
        {
            // Arrange
            await _documentStore.CreateCollectionAsync("users");
            await _documentStore.InsertAsync("users", CreateDoc("1", new Dictionary<string, object> { ["status"] = "active" }));

            var query = new AdvGenNoSqlServer.Query.Models.Query
            {
                CollectionName = "users",
                Filter = QueryFilter.Eq("status", "deleted")
            };

            // Act
            var exists = await _queryExecutor.ExistsAsync(query);

            // Assert
            Assert.False(exists);
        }

        [Fact]
        public async Task ExplainAsync_ReturnsQueryStats()
        {
            // Arrange
            await _documentStore.CreateCollectionAsync("users");

            var query = new AdvGenNoSqlServer.Query.Models.Query
            {
                CollectionName = "users",
                Filter = QueryFilter.Eq("status", "active")
            };

            // Act
            var stats = await _queryExecutor.ExplainAsync(query);

            // Assert
            Assert.NotNull(stats.ExecutionPlan);
            Assert.NotEmpty(stats.ExecutionPlan);
        }
    }

    #endregion

    #region Query Model Tests

    public class QueryModelTests
    {
        [Fact]
        public void QueryFilter_Eq_CreatesCorrectFilter()
        {
            // Act
            var filter = QueryFilter.Eq("name", "John");

            // Assert
            Assert.Single(filter.Conditions);
            Assert.Equal("John", filter.Conditions["name"]);
        }

        [Fact]
        public void QueryFilter_Gt_CreatesCorrectFilter()
        {
            // Act
            var filter = QueryFilter.Gt("age", 18);

            // Assert
            Assert.Single(filter.Conditions);
            var operators = filter.Conditions["age"] as Dictionary<string, object>;
            Assert.NotNull(operators);
            Assert.Equal(18, operators["$gt"]);
        }

        [Fact]
        public void QueryFilter_And_CombinesFilters()
        {
            // Arrange
            var filter1 = QueryFilter.Gt("age", 18);
            var filter2 = QueryFilter.Eq("status", "active");

            // Act
            var combined = filter1.And(filter2);

            // Assert
            Assert.Single(combined.Conditions);
            var andConditions = combined.Conditions["$and"] as List<object>;
            Assert.NotNull(andConditions);
            Assert.Equal(2, andConditions.Count);
        }

        [Fact]
        public void QueryFilter_Or_CombinesFilters()
        {
            // Arrange
            var filter1 = QueryFilter.Eq("status", "active");
            var filter2 = QueryFilter.Eq("status", "pending");

            // Act
            var combined = filter1.Or(filter2);

            // Assert
            Assert.Single(combined.Conditions);
            var orConditions = combined.Conditions["$or"] as List<object>;
            Assert.NotNull(orConditions);
            Assert.Equal(2, orConditions.Count);
        }

        [Fact]
        public void SortField_Ascending_CreatesAscendingSort()
        {
            // Act
            var sort = SortField.Ascending("name");

            // Assert
            Assert.Equal("name", sort.FieldName);
            Assert.Equal(SortDirection.Ascending, sort.Direction);
        }

        [Fact]
        public void SortField_Descending_CreatesDescendingSort()
        {
            // Act
            var sort = SortField.Descending("age");

            // Assert
            Assert.Equal("age", sort.FieldName);
            Assert.Equal(SortDirection.Descending, sort.Direction);
        }

        [Fact]
        public void QueryResult_SuccessResult_HasCorrectProperties()
        {
            // Arrange
            var docs = new List<Document> { new() { Id = "1", Data = new() } };

            // Act
            var result = QueryResult.SuccessResult(docs, 10, 0, 5);

            // Assert
            Assert.True(result.Success);
            Assert.Equal(docs, result.Documents);
            Assert.Equal(10, result.TotalCount);
            Assert.Equal(5, result.ExecutionTimeMs);
        }

        [Fact]
        public void QueryResult_FailureResult_HasErrorMessage()
        {
            // Act
            var result = QueryResult.FailureResult("Something went wrong");

            // Assert
            Assert.False(result.Success);
            Assert.Equal("Something went wrong", result.ErrorMessage);
        }
    }

    #endregion
}
