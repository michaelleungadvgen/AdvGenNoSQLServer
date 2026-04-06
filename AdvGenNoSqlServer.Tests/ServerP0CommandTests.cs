// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Text.Json;
using AdvGenNoSqlServer.Core.Abstractions;
using AdvGenNoSqlServer.Core.Configuration;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Storage;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for P0 Critical Server Commands (INSERT, REPLACE, UPSERT, FIND_ONE, TOUCH)
/// These tests validate the underlying document store operations used by the server commands.
/// </summary>
public class ServerP0CommandTests : IDisposable
{
    private readonly string _testStoragePath;
    private readonly HybridDocumentStore _documentStore;

    public ServerP0CommandTests()
    {
        _testStoragePath = Path.Combine(Path.GetTempPath(), $"nosql_p0_tests_{Guid.NewGuid()}");
        _documentStore = new HybridDocumentStore(_testStoragePath);
        _documentStore.InitializeAsync().Wait();
    }

    public void Dispose()
    {
        try
        {
            _documentStore.DisposeAsync().AsTask().Wait();
            if (Directory.Exists(_testStoragePath))
            {
                Directory.Delete(_testStoragePath, true);
            }
        }
        catch { }
    }

    #region INSERT Command Tests

    [Fact]
    public async Task InsertCommand_NewDocument_InsertsSuccessfully()
    {
        // Arrange
        var document = new Document
        {
            Id = "insert_test_1",
            Data = new Dictionary<string, object>
            {
                ["name"] = "John Doe",
                ["email"] = "john@example.com"
            }
        };

        // Act
        await _documentStore.InsertAsync("users", document);
        var retrieved = await _documentStore.GetAsync("users", "insert_test_1");

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("John Doe", retrieved.Data["name"]);
        Assert.Equal("john@example.com", retrieved.Data["email"]);
    }

    [Fact]
    public async Task InsertCommand_DuplicateId_ThrowsException()
    {
        // Arrange
        var document = new Document
        {
            Id = "duplicate_test",
            Data = new Dictionary<string, object> { ["name"] = "First" }
        };
        await _documentStore.InsertAsync("users", document);

        var duplicate = new Document
        {
            Id = "duplicate_test",
            Data = new Dictionary<string, object> { ["name"] = "Second" }
        };

        // Act & Assert
        await Assert.ThrowsAsync<DocumentAlreadyExistsException>(async () =>
        {
            await _documentStore.InsertAsync("users", duplicate);
        });
    }

    #endregion

    #region REPLACE Command Tests

    [Fact]
    public async Task ReplaceCommand_ExistingDocument_ReplacesSuccessfully()
    {
        // Arrange
        var original = new Document
        {
            Id = "replace_test_1",
            Data = new Dictionary<string, object>
            {
                ["name"] = "Original Name",
                ["age"] = 25
            }
        };
        await _documentStore.InsertAsync("users", original);

        var replacement = new Document
        {
            Id = "replace_test_1",
            Data = new Dictionary<string, object>
            {
                ["name"] = "Replaced Name",
                ["email"] = "new@example.com"
            }
        };

        // Act
        await _documentStore.UpdateAsync("users", replacement);
        var retrieved = await _documentStore.GetAsync("users", "replace_test_1");

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("Replaced Name", retrieved.Data["name"]);
        Assert.Equal("new@example.com", retrieved.Data["email"]);
        Assert.False(retrieved.Data.ContainsKey("age")); // Old fields should be gone
    }

    [Fact]
    public async Task ReplaceCommand_NonExistentDocument_ThrowsException()
    {
        // Arrange
        var document = new Document
        {
            Id = "nonexistent_replace",
            Data = new Dictionary<string, object> { ["name"] = "Test" }
        };

        // Act & Assert
        await Assert.ThrowsAsync<DocumentNotFoundException>(async () =>
        {
            await _documentStore.UpdateAsync("users", document);
        });
    }

    #endregion

    #region UPSERT Command Tests

    [Fact]
    public async Task UpsertCommand_NewDocument_InsertsSuccessfully()
    {
        // Arrange
        var document = new Document
        {
            Id = "upsert_new_test",
            Data = new Dictionary<string, object>
            {
                ["name"] = "Upsert New User"
            }
        };

        // Act - simulate upsert by checking existence first
        var exists = await _documentStore.ExistsAsync("users", "upsert_new_test");
        if (!exists)
        {
            await _documentStore.InsertAsync("users", document);
        }
        else
        {
            await _documentStore.UpdateAsync("users", document);
        }

        var retrieved = await _documentStore.GetAsync("users", "upsert_new_test");

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("Upsert New User", retrieved.Data["name"]);
        Assert.True(exists == false); // Was a new insert
    }

    [Fact]
    public async Task UpsertCommand_ExistingDocument_UpdatesSuccessfully()
    {
        // Arrange
        var original = new Document
        {
            Id = "upsert_existing_test",
            Data = new Dictionary<string, object>
            {
                ["name"] = "Original",
                ["status"] = "old"
            }
        };
        await _documentStore.InsertAsync("users", original);

        var updated = new Document
        {
            Id = "upsert_existing_test",
            Data = new Dictionary<string, object>
            {
                ["name"] = "Updated",
                ["status"] = "new"
            }
        };

        // Act - simulate upsert
        var exists = await _documentStore.ExistsAsync("users", "upsert_existing_test");
        if (!exists)
        {
            await _documentStore.InsertAsync("users", updated);
        }
        else
        {
            await _documentStore.UpdateAsync("users", updated);
        }

        var retrieved = await _documentStore.GetAsync("users", "upsert_existing_test");

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("Updated", retrieved.Data["name"]);
        Assert.Equal("new", retrieved.Data["status"]);
        Assert.True(exists); // Was an update
    }

    #endregion

    #region FIND_ONE Command Tests

    [Fact]
    public async Task FindOneCommand_ById_ExistingDocument_ReturnsDocument()
    {
        // Arrange
        var document = new Document
        {
            Id = "find_one_test",
            Data = new Dictionary<string, object>
            {
                ["name"] = "Findable User",
                ["status"] = "active"
            }
        };
        await _documentStore.InsertAsync("users", document);

        // Act
        var retrieved = await _documentStore.GetAsync("users", "find_one_test");

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("Findable User", retrieved.Data["name"]);
        Assert.Equal("active", retrieved.Data["status"]);
    }

    [Fact]
    public async Task FindOneCommand_ById_NonExistent_ReturnsNull()
    {
        // Act
        var retrieved = await _documentStore.GetAsync("users", "nonexistent_find_one");

        // Assert
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task FindOneCommand_ByFilter_MatchingDocument_ReturnsDocument()
    {
        // Arrange
        await _documentStore.InsertAsync("products", new Document
        {
            Id = "prod1",
            Data = new Dictionary<string, object>
            {
                ["name"] = "Laptop",
                ["category"] = "electronics",
                ["price"] = 999.99
            }
        });
        await _documentStore.InsertAsync("products", new Document
        {
            Id = "prod2",
            Data = new Dictionary<string, object>
            {
                ["name"] = "Book",
                ["category"] = "books",
                ["price"] = 19.99
            }
        });

        // Act - simulate find_one by filtering
        var allDocs = await _documentStore.GetAllAsync("products");
        var found = allDocs.FirstOrDefault(d => 
            d.Data.TryGetValue("category", out var cat) && 
            cat?.ToString() == "electronics");

        // Assert
        Assert.NotNull(found);
        Assert.Equal("Laptop", found.Data["name"]);
    }

    [Fact]
    public async Task FindOneCommand_ByFilter_NoMatch_ReturnsNull()
    {
        // Arrange
        await _documentStore.InsertAsync("products", new Document
        {
            Id = "prod1",
            Data = new Dictionary<string, object>
            {
                ["name"] = "Laptop",
                ["category"] = "electronics"
            }
        });

        // Act - simulate find_one with non-matching filter
        var allDocs = await _documentStore.GetAllAsync("products");
        var found = allDocs.FirstOrDefault(d => 
            d.Data.TryGetValue("category", out var cat) && 
            cat?.ToString() == "nonexistent");

        // Assert
        Assert.Null(found);
    }

    #endregion

    #region TOUCH Command Tests

    [Fact]
    public async Task TouchCommand_ExistingDocument_UpdatesTimestamp()
    {
        // Arrange
        var document = new Document
        {
            Id = "touch_test",
            Data = new Dictionary<string, object>
            {
                ["name"] = "Touchable User"
            }
        };
        await _documentStore.InsertAsync("users", document);

        // Get original timestamp
        var original = await _documentStore.GetAsync("users", "touch_test");
        var originalUpdatedAt = original!.UpdatedAt;

        // Wait to ensure timestamp changes
        await Task.Delay(50);

        // Act - simulate touch by updating document with new timestamp
        var touched = new Document
        {
            Id = "touch_test",
            Data = original.Data,
            Version = original.Version + 1,
            UpdatedAt = DateTime.UtcNow
        };
        await _documentStore.UpdateAsync("users", touched);

        var retrieved = await _documentStore.GetAsync("users", "touch_test");

        // Assert
        Assert.NotNull(retrieved);
        Assert.True(retrieved.UpdatedAt > originalUpdatedAt);
        Assert.True(retrieved.Version > original.Version);
    }

    [Fact]
    public async Task TouchCommand_NonExistentDocument_ThrowsException()
    {
        // Arrange
        var document = new Document
        {
            Id = "nonexistent_touch",
            Data = new Dictionary<string, object> { ["name"] = "Test" }
        };

        // Act & Assert
        await Assert.ThrowsAsync<DocumentNotFoundException>(async () =>
        {
            await _documentStore.UpdateAsync("users", document);
        });
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task InsertReplaceUpsertFindOneTouch_FullWorkflow_Success()
    {
        // Step 1: Insert
        var insertDoc = new Document
        {
            Id = "workflow_doc",
            Data = new Dictionary<string, object>
            {
                ["name"] = "Workflow Test",
                ["version"] = 1
            }
        };
        await _documentStore.InsertAsync("workflow", insertDoc);

        // Step 2: Find One
        var found = await _documentStore.GetAsync("workflow", "workflow_doc");
        Assert.NotNull(found);
        Assert.Equal("Workflow Test", found.Data["name"]);

        // Step 3: Replace
        var replaceDoc = new Document
        {
            Id = "workflow_doc",
            Data = new Dictionary<string, object>
            {
                ["name"] = "Replaced Workflow",
                ["status"] = "active"
            }
        };
        await _documentStore.UpdateAsync("workflow", replaceDoc);

        // Verify replacement
        var afterReplace = await _documentStore.GetAsync("workflow", "workflow_doc");
        Assert.Equal("Replaced Workflow", afterReplace!.Data["name"]);
        Assert.False(afterReplace.Data.ContainsKey("version"));

        // Step 4: Upsert (update existing)
        var exists = await _documentStore.ExistsAsync("workflow", "workflow_doc");
        Assert.True(exists);
        
        var upsertDoc = new Document
        {
            Id = "workflow_doc",
            Data = new Dictionary<string, object>
            {
                ["name"] = "Upserted Workflow"
            }
        };
        await _documentStore.UpdateAsync("workflow", upsertDoc);

        // Step 5: Touch (update timestamp)
        var beforeTouch = await _documentStore.GetAsync("workflow", "workflow_doc");
        await Task.Delay(10);
        
        var touchDoc = new Document
        {
            Id = "workflow_doc",
            Data = beforeTouch!.Data,
            Version = beforeTouch.Version + 1,
            UpdatedAt = DateTime.UtcNow
        };
        await _documentStore.UpdateAsync("workflow", touchDoc);

        // Final verification
        var final = await _documentStore.GetAsync("workflow", "workflow_doc");
        Assert.NotNull(final);
        Assert.Equal("Upserted Workflow", final.Data["name"]);
        Assert.True(final.UpdatedAt > beforeTouch.UpdatedAt);
    }

    [Fact]
    public async Task P0Commands_MultipleCollections_Isolated()
    {
        // Arrange
        var userDoc = new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object> { ["name"] = "Alice" }
        };
        var productDoc = new Document
        {
            Id = "product1",
            Data = new Dictionary<string, object> { ["name"] = "Widget" }
        };

        // Act
        await _documentStore.InsertAsync("users", userDoc);
        await _documentStore.InsertAsync("products", productDoc);

        // Assert - documents are isolated by collection
        var users = await _documentStore.GetAllAsync("users");
        var products = await _documentStore.GetAllAsync("products");

        Assert.Single(users);
        Assert.Single(products);
        Assert.Equal("Alice", users.First().Data["name"]);
        Assert.Equal("Widget", products.First().Data["name"]);

        // Cross-collection queries return null
        var userInProducts = await _documentStore.GetAsync("products", "user1");
        Assert.Null(userInProducts);
    }

    #endregion
}
