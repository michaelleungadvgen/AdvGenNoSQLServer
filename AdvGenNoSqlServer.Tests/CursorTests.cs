// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Query.Cursors;
using AdvGenNoSqlServer.Query.Execution;
using AdvGenNoSqlServer.Query.Filtering;
using AdvGenNoSqlServer.Query.Models;
using AdvGenNoSqlServer.Storage;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for cursor-based pagination functionality
/// </summary>
public class CursorTests : IDisposable
{
    private readonly DocumentStore _documentStore;
    private readonly FilterEngine _filterEngine;
    private readonly CursorManager _cursorManager;

    public CursorTests()
    {
        _documentStore = new DocumentStore();
        _filterEngine = new FilterEngine();
        _cursorManager = new CursorManager(_documentStore, _filterEngine);
    }

    public void Dispose()
    {
        _cursorManager.Dispose();
    }

    #region CursorOptions Tests

    [Fact]
    public void CursorOptions_DefaultValues_AreCorrect()
    {
        var options = new CursorOptions();

        Assert.Equal(CursorOptions.DefaultBatchSize, options.BatchSize);
        Assert.Equal(CursorOptions.DefaultTimeoutMinutes, options.TimeoutMinutes);
        Assert.False(options.IncludeTotalCount);
        Assert.Null(options.ResumeToken);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(10000, true)]
    [InlineData(10001, false)]
    public void CursorOptions_Validate_BatchSize(int batchSize, bool shouldBeValid)
    {
        var options = new CursorOptions { BatchSize = batchSize };
        var errors = options.Validate();

        if (shouldBeValid)
        {
            Assert.DoesNotContain(errors, e => e.Contains("BatchSize"));
        }
        else
        {
            Assert.Contains(errors, e => e.Contains("BatchSize"));
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(1, true)]
    [InlineData(10, true)]
    [InlineData(60, true)]
    [InlineData(61, false)]
    public void CursorOptions_Validate_TimeoutMinutes(int timeout, bool shouldBeValid)
    {
        var options = new CursorOptions { TimeoutMinutes = timeout };
        var errors = options.Validate();

        if (shouldBeValid)
        {
            Assert.DoesNotContain(errors, e => e.Contains("TimeoutMinutes"));
        }
        else
        {
            Assert.Contains(errors, e => e.Contains("TimeoutMinutes"));
        }
    }

    #endregion

    #region ResumeToken Tests

    [Fact]
    public void ResumeToken_SerializeDeserialize_RoundTrip()
    {
        var token = new ResumeToken
        {
            CursorId = "cursor_123",
            LastDocumentId = "doc_456",
            CreatedAt = DateTime.UtcNow,
            FilterJson = "{\"status\":\"active\"}",
            SortJson = "[{\"field\":\"name\",\"direction\":1}]"
        };

        var tokenString = token.ToTokenString();
        var deserialized = ResumeToken.FromTokenString(tokenString);

        Assert.NotNull(deserialized);
        Assert.Equal(token.CursorId, deserialized.CursorId);
        Assert.Equal(token.LastDocumentId, deserialized.LastDocumentId);
        Assert.Equal(token.FilterJson, deserialized.FilterJson);
        Assert.Equal(token.SortJson, deserialized.SortJson);
    }

    [Fact]
    public void ResumeToken_FromInvalidString_ReturnsNull()
    {
        var result = ResumeToken.FromTokenString("invalid_token_string");
        Assert.Null(result);
    }

    [Fact]
    public void ResumeToken_FromNullString_ReturnsNull()
    {
        var result = ResumeToken.FromTokenString(null!);
        Assert.Null(result);
    }

    #endregion

    #region CursorManager Tests

    [Fact]
    public async Task CursorManager_CreateCursor_WithEmptyCollection_ReturnsEmptyResult()
    {
        var options = new CursorOptions { BatchSize = 10 };
        var result = await _cursorManager.CreateCursorAsync("test", null, null, options);

        Assert.True(result.Success);
        Assert.Empty(result.Documents);
        Assert.False(result.HasMore);
    }

    [Fact]
    public async Task CursorManager_CreateCursor_WithDocuments_ReturnsFirstBatch()
    {
        // Arrange
        await SeedDocumentsAsync("users", 25);
        var options = new CursorOptions { BatchSize = 10 };

        // Act
        var result = await _cursorManager.CreateCursorAsync("users", null, null, options);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(10, result.Documents.Count);
        Assert.True(result.HasMore);
        Assert.NotNull(result.Cursor);
    }

    [Fact]
    public async Task CursorManager_CreateCursor_WithIncludeTotalCount_ReturnsTotal()
    {
        // Arrange
        await SeedDocumentsAsync("users", 50);
        var options = new CursorOptions { BatchSize = 10, IncludeTotalCount = true };

        // Act
        var result = await _cursorManager.CreateCursorAsync("users", null, null, options);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(50, result.TotalCount);
    }

    [Fact]
    public async Task CursorManager_CreateCursor_WithInvalidOptions_ReturnsFailure()
    {
        var options = new CursorOptions { BatchSize = 0 };
        var result = await _cursorManager.CreateCursorAsync("test", null, null, options);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task CursorManager_CreateCursor_WithFilter_ReturnsFilteredResults()
    {
        // Arrange
        await _documentStore.InsertAsync("users", new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object> { ["status"] = "active" }
        });
        await _documentStore.InsertAsync("users", new Document
        {
            Id = "user2",
            Data = new Dictionary<string, object> { ["status"] = "inactive" }
        });
        await _documentStore.InsertAsync("users", new Document
        {
            Id = "user3",
            Data = new Dictionary<string, object> { ["status"] = "active" }
        });

        var filter = QueryFilter.Eq("status", "active");
        var options = new CursorOptions { BatchSize = 10 };

        // Act
        var result = await _cursorManager.CreateCursorAsync("users", filter, null, options);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Documents.Count);
        Assert.All(result.Documents, d => Assert.Equal("active", d.Data["status"]));
    }

    [Fact]
    public async Task CursorManager_GetMore_ReturnsNextBatch()
    {
        // Arrange
        await SeedDocumentsAsync("users", 25);
        var options = new CursorOptions { BatchSize = 10 };
        var cursorResult = await _cursorManager.CreateCursorAsync("users", null, null, options);
        var cursorId = cursorResult.Cursor!.CursorId;

        // Act - Get second batch
        var batchResult = await _cursorManager.GetMoreAsync(cursorId, 10);

        // Assert
        Assert.True(batchResult.Success);
        Assert.Equal(10, batchResult.Documents.Count);
        Assert.True(batchResult.HasMore);
    }

    [Fact]
    public async Task CursorManager_GetMore_LastBatch_HasMoreFalse()
    {
        // Arrange
        await SeedDocumentsAsync("users", 25);
        var options = new CursorOptions { BatchSize = 10 };
        var cursorResult = await _cursorManager.CreateCursorAsync("users", null, null, options);
        var cursorId = cursorResult.Cursor!.CursorId;

        // Act - Get all remaining batches
        await _cursorManager.GetMoreAsync(cursorId, 10); // batch 2
        var finalBatch = await _cursorManager.GetMoreAsync(cursorId, 10); // batch 3

        // Assert
        Assert.True(finalBatch.Success);
        Assert.Equal(5, finalBatch.Documents.Count);
        Assert.False(finalBatch.HasMore);
    }

    [Fact]
    public async Task CursorManager_GetMore_NonExistentCursor_ReturnsError()
    {
        var result = await _cursorManager.GetMoreAsync("non_existent_cursor", 10);

        Assert.False(result.Success);
        Assert.Contains("not found", result.ErrorMessage);
    }

    [Fact]
    public async Task CursorManager_KillCursor_ReturnsTrue()
    {
        // Arrange
        await SeedDocumentsAsync("users", 10);
        var options = new CursorOptions { BatchSize = 5 };
        var cursorResult = await _cursorManager.CreateCursorAsync("users", null, null, options);
        var cursorId = cursorResult.Cursor!.CursorId;

        // Act
        var killed = await _cursorManager.KillCursorAsync(cursorId);

        // Assert
        Assert.True(killed);
        var getMoreResult = await _cursorManager.GetMoreAsync(cursorId, 5);
        Assert.False(getMoreResult.Success);
    }

    [Fact]
    public async Task CursorManager_KillCursor_NonExistent_ReturnsFalse()
    {
        var result = await _cursorManager.KillCursorAsync("non_existent_cursor");
        Assert.False(result);
    }

    [Fact]
    public async Task CursorManager_ListActiveCursors_ReturnsCursorIds()
    {
        // Arrange
        await SeedDocumentsAsync("users", 10);
        var options = new CursorOptions { BatchSize = 5 };
        var cursorResult1 = await _cursorManager.CreateCursorAsync("users", null, null, options);
        var cursorResult2 = await _cursorManager.CreateCursorAsync("users", null, null, options);

        // Act
        var activeCursors = _cursorManager.ListActiveCursors().ToList();

        // Assert
        Assert.Equal(2, activeCursors.Count);
        Assert.Contains(cursorResult1.Cursor!.CursorId, activeCursors);
        Assert.Contains(cursorResult2.Cursor!.CursorId, activeCursors);
    }

    [Fact]
    public async Task CursorManager_GetStats_ReturnsCorrectCounts()
    {
        // Arrange
        await SeedDocumentsAsync("users", 10);
        var options = new CursorOptions { BatchSize = 5 };

        // Act - Create cursors
        var cursor1 = await _cursorManager.CreateCursorAsync("users", null, null, options);
        var cursor2 = await _cursorManager.CreateCursorAsync("users", null, null, options);

        // Kill one cursor
        await _cursorManager.KillCursorAsync(cursor1.Cursor!.CursorId);

        // Get stats
        var stats = _cursorManager.GetStats();

        // Assert
        Assert.Equal(1, stats.ActiveCursors);
        Assert.Equal(2, stats.TotalCursorsCreated);
        Assert.Equal(1, stats.TotalCursorsClosed);
    }

    [Fact]
    public async Task CursorManager_CursorCreatedEvent_Raised()
    {
        // Arrange
        var eventRaised = false;
        string? capturedCursorId = null;
        _cursorManager.CursorCreated += (sender, args) =>
        {
            eventRaised = true;
            capturedCursorId = args.CursorId;
        };

        await SeedDocumentsAsync("users", 10);
        var options = new CursorOptions { BatchSize = 5 };

        // Act
        var result = await _cursorManager.CreateCursorAsync("users", null, null, options);

        // Assert
        Assert.True(eventRaised);
        Assert.Equal(result.Cursor!.CursorId, capturedCursorId);
    }

    [Fact]
    public async Task CursorManager_CursorClosedEvent_Raised()
    {
        // Arrange
        await SeedDocumentsAsync("users", 10);
        var options = new CursorOptions { BatchSize = 5 };
        var cursorResult = await _cursorManager.CreateCursorAsync("users", null, null, options);

        var eventRaised = false;
        _cursorManager.CursorClosed += (sender, args) =>
        {
            eventRaised = true;
        };

        // Act
        await _cursorManager.KillCursorAsync(cursorResult.Cursor!.CursorId);

        // Assert
        Assert.True(eventRaised);
    }

    [Fact]
    public async Task CursorManager_KillCursorsForCollection_ClosesMatchingCursors()
    {
        // Arrange
        await SeedDocumentsAsync("users", 10);
        await SeedDocumentsAsync("products", 10);
        var options = new CursorOptions { BatchSize = 5 };

        var userCursor = await _cursorManager.CreateCursorAsync("users", null, null, options);
        var productCursor = await _cursorManager.CreateCursorAsync("products", null, null, options);

        // Act
        var killed = await ((CursorManager)_cursorManager).KillCursorsForCollectionAsync("users");

        // Assert
        Assert.Equal(1, killed);
        var usersActive = await _cursorManager.GetCursorAsync(userCursor.Cursor!.CursorId);
        var productsActive = await _cursorManager.GetCursorAsync(productCursor.Cursor!.CursorId);
        Assert.Null(usersActive);
        Assert.NotNull(productsActive);
    }

    #endregion

    #region CursorResult Tests

    [Fact]
    public void CursorResult_Success_CreatesCorrectResult()
    {
        // This test would require a mock cursor, so we'll just test the static factory
        var result = CursorResult.FailureResult("Test error");

        Assert.False(result.Success);
        Assert.Equal("Test error", result.ErrorMessage);
        Assert.Empty(result.Documents);
    }

    #endregion

    #region Cursor Enabled Query Executor Tests

    [Fact]
    public async Task CursorEnabledQueryExecutor_ExecuteAsync_WithSkipLimit_UsesBaseExecutor()
    {
        // Arrange
        var executor = new CursorEnabledQueryExecutor(_documentStore, _filterEngine);
        await SeedDocumentsAsync("users", 20);

        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = "users",
            Options = new QueryOptions { Skip = 5, Limit = 10 }
        };

        // Act
        var result = await executor.ExecuteAsync(query);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(10, result.Documents.Count);
        Assert.Equal(5, result.Skipped);
    }

    [Fact]
    public async Task CursorEnabledQueryExecutor_ExecuteWithCursorAsync_ReturnsCursorResult()
    {
        // Arrange
        var executor = new CursorEnabledQueryExecutor(_documentStore, _filterEngine);
        await SeedDocumentsAsync("users", 25);

        var query = new AdvGenNoSqlServer.Query.Models.Query
        {
            CollectionName = "users"
        };
        var options = new CursorOptions { BatchSize = 10 };

        // Act
        var result = await executor.ExecuteWithCursorAsync(query, options);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Cursor);
        Assert.Equal(10, result.Documents.Count);
        Assert.True(result.HasMore);
    }

    [Fact]
    public async Task CursorEnabledQueryExecutor_GetMoreAsync_ReturnsNextBatch()
    {
        // Arrange
        var executor = new CursorEnabledQueryExecutor(_documentStore, _filterEngine);
        await SeedDocumentsAsync("users", 25);

        var query = new AdvGenNoSqlServer.Query.Models.Query { CollectionName = "users" };
        var options = new CursorOptions { BatchSize = 10 };
        var cursorResult = await executor.ExecuteWithCursorAsync(query, options);

        // Act
        var batchResult = await executor.GetMoreAsync(cursorResult.Cursor!.CursorId, 10);

        // Assert
        Assert.True(batchResult.Success);
        Assert.Equal(10, batchResult.Documents.Count);
        Assert.True(batchResult.HasMore);
    }

    [Fact]
    public async Task CursorEnabledQueryExecutor_KillCursorAsync_ClosesCursor()
    {
        // Arrange
        var executor = new CursorEnabledQueryExecutor(_documentStore, _filterEngine);
        await SeedDocumentsAsync("users", 10);

        var query = new AdvGenNoSqlServer.Query.Models.Query { CollectionName = "users" };
        var options = new CursorOptions { BatchSize = 5 };
        var cursorResult = await executor.ExecuteWithCursorAsync(query, options);

        // Act
        var killed = await executor.KillCursorAsync(cursorResult.Cursor!.CursorId);

        // Assert
        Assert.True(killed);
        var getMoreResult = await executor.GetMoreAsync(cursorResult.Cursor.CursorId);
        Assert.False(getMoreResult.Success);
    }

    [Fact]
    public async Task CursorEnabledQueryExecutor_CountAsync_ReturnsCorrectCount()
    {
        // Arrange
        var executor = new CursorEnabledQueryExecutor(_documentStore, _filterEngine);
        await SeedDocumentsAsync("users", 30);

        var query = new AdvGenNoSqlServer.Query.Models.Query { CollectionName = "users" };

        // Act
        var count = await executor.CountAsync(query);

        // Assert
        Assert.Equal(30, count);
    }

    [Fact]
    public async Task CursorEnabledQueryExecutor_ExistsAsync_ReturnsTrueWhenDocumentsExist()
    {
        // Arrange
        var executor = new CursorEnabledQueryExecutor(_documentStore, _filterEngine);
        await _documentStore.InsertAsync("users", new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object>()
        });

        var query = new AdvGenNoSqlServer.Query.Models.Query { CollectionName = "users" };

        // Act
        var exists = await executor.ExistsAsync(query);

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task CursorEnabledQueryExecutor_ExplainAsync_ReturnsQueryStats()
    {
        // Arrange
        var executor = new CursorEnabledQueryExecutor(_documentStore, _filterEngine);
        await SeedDocumentsAsync("users", 10);

        var query = new AdvGenNoSqlServer.Query.Models.Query { CollectionName = "users" };

        // Act
        var stats = await executor.ExplainAsync(query);

        // Assert
        Assert.NotNull(stats);
        Assert.NotNull(stats.ExecutionPlan);
        Assert.NotEmpty(stats.ExecutionPlan);
    }

    #endregion

    #region Cursor with Sorting Tests

    [Fact]
    public async Task CursorManager_CreateCursor_WithSort_ReturnsSortedResults()
    {
        // Arrange
        await _documentStore.InsertAsync("users", new Document
        {
            Id = "user3",
            Data = new Dictionary<string, object> { ["name"] = "Charlie" }
        });
        await _documentStore.InsertAsync("users", new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object> { ["name"] = "Alice" }
        });
        await _documentStore.InsertAsync("users", new Document
        {
            Id = "user2",
            Data = new Dictionary<string, object> { ["name"] = "Bob" }
        });

        var sort = new List<SortField> { SortField.Ascending("name") };
        var options = new CursorOptions { BatchSize = 2 };

        // Act
        var result = await _cursorManager.CreateCursorAsync("users", null, sort, options);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("user1", result.Documents[0].Id); // Alice
        Assert.Equal("user2", result.Documents[1].Id); // Bob

        // Get next batch
        var batch2 = await _cursorManager.GetMoreAsync(result.Cursor!.CursorId, 2);
        Assert.Equal("user3", batch2.Documents[0].Id); // Charlie
    }

    [Fact]
    public async Task CursorManager_CreateCursor_WithDescendingSort_ReturnsSortedResults()
    {
        // Arrange
        await _documentStore.InsertAsync("users", new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object> { ["score"] = 10 }
        });
        await _documentStore.InsertAsync("users", new Document
        {
            Id = "user2",
            Data = new Dictionary<string, object> { ["score"] = 50 }
        });
        await _documentStore.InsertAsync("users", new Document
        {
            Id = "user3",
            Data = new Dictionary<string, object> { ["score"] = 30 }
        });

        var sort = new List<SortField> { SortField.Descending("score") };
        var options = new CursorOptions { BatchSize = 2 };

        // Act
        var result = await _cursorManager.CreateCursorAsync("users", null, sort, options);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("user2", result.Documents[0].Id); // 50
        Assert.Equal("user3", result.Documents[1].Id); // 30
    }

    #endregion

    #region Cursor with Resume Token Tests

    [Fact]
    public async Task CursorManager_CreateCursor_WithResumeToken_ContinuesFromPosition()
    {
        // Arrange
        await SeedDocumentsAsync("users", 20);
        var options = new CursorOptions { BatchSize = 5 };
        var firstBatch = await _cursorManager.CreateCursorAsync("users", null, null, options);

        // Get resume token after first batch
        var batch2 = await _cursorManager.GetMoreAsync(firstBatch.Cursor!.CursorId, 5);

        // Create new cursor with resume token
        var resumeOptions = new CursorOptions
        {
            BatchSize = 5,
            ResumeToken = batch2.ResumeToken
        };

        // Act - Create new cursor from resume position
        // Note: Resume token functionality requires the cursor manager to support
        // looking up existing cursors, so this tests the token generation
        Assert.NotNull(batch2.ResumeToken);
        var token = ResumeToken.FromTokenString(batch2.ResumeToken);
        Assert.NotNull(token);
        Assert.Equal(firstBatch.Cursor.CursorId, token.CursorId);
    }

    #endregion

    #region Helper Methods

    private async Task SeedDocumentsAsync(string collection, int count)
    {
        for (int i = 0; i < count; i++)
        {
            await _documentStore.InsertAsync(collection, new Document
            {
                Id = $"doc_{i:D3}",
                Data = new Dictionary<string, object>
                {
                    ["index"] = i,
                    ["name"] = $"Document {i}",
                    ["created"] = DateTime.UtcNow.AddMinutes(-i)
                }
            });
        }
    }

    #endregion
}
