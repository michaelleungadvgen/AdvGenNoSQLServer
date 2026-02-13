// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Storage;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for atomic update operations
/// </summary>
public class AtomicUpdateOperationsTests
{
    private AtomicUpdateDocumentStore CreateStore()
    {
        return new AtomicUpdateDocumentStore();
    }

    private Document CreateTestDocument(string id, Dictionary<string, object> data)
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

    #region Constructor Tests

    [Fact]
    public void Constructor_CreatesStoreSuccessfully()
    {
        var store = CreateStore();
        Assert.NotNull(store);
    }

    #endregion

    #region IncrementAsync Tests

    [Fact]
    public async Task IncrementAsync_NonExistentDocument_ThrowsDocumentNotFoundException()
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("test");

        await Assert.ThrowsAsync<DocumentNotFoundException>(() =>
            store.IncrementAsync("test", "nonexistent", "counter", 1));
    }

    [Theory]
    [InlineData(0, 5, 5)]      // Initialize from null
    [InlineData(10, 5, 15)]    // Increment positive
    [InlineData(10, -3, 7)]    // Decrement (negative increment)
    [InlineData(10.5, 2.5, 13)] // Double values
    public async Task IncrementAsync_ValidValues_ReturnsUpdatedValue(double initial, double increment, double expected)
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("test");

        var doc = CreateTestDocument("doc1", new Dictionary<string, object>
        {
            ["counter"] = initial
        });
        await store.InsertAsync("test", doc);

        var result = await store.IncrementAsync("test", "doc1", "counter", increment);

        Assert.NotNull(result);
        Assert.Equal(expected, result.Data!["counter"]);
        Assert.True(result.Version > doc.Version);
    }

    [Fact]
    public async Task IncrementAsync_NestedField_UpdatesNestedValue()
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("test");

        var doc = CreateTestDocument("doc1", new Dictionary<string, object>
        {
            ["stats"] = new Dictionary<string, object>
            {
                ["views"] = 100
            }
        });
        await store.InsertAsync("test", doc);

        var result = await store.IncrementAsync("test", "doc1", "stats.views", 50);

        Assert.NotNull(result);
        var stats = result.Data!["stats"] as Dictionary<string, object>;
        Assert.NotNull(stats);
        Assert.Equal(150.0, stats["views"]);
    }

    [Fact]
    public async Task IncrementAsync_DeepNestedField_UpdatesDeeplyNestedValue()
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("test");

        var doc = CreateTestDocument("doc1", new Dictionary<string, object>
        {
            ["level1"] = new Dictionary<string, object>
            {
                ["level2"] = new Dictionary<string, object>
                {
                    ["level3"] = new Dictionary<string, object>
                    {
                        ["value"] = 10
                    }
                }
            }
        });
        await store.InsertAsync("test", doc);

        var result = await store.IncrementAsync("test", "doc1", "level1.level2.level3.value", 5);

        Assert.NotNull(result);
        Assert.Equal(15.0, GetDeepValue(result.Data!, "level1", "level2", "level3", "value"));
    }

    [Fact]
    public async Task IncrementAsync_NonNumericField_ThrowsAtomicUpdateException()
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("test");

        var doc = CreateTestDocument("doc1", new Dictionary<string, object>
        {
            ["counter"] = "not a number"
        });
        await store.InsertAsync("test", doc);

        var ex = await Assert.ThrowsAsync<AtomicUpdateException>(() =>
            store.IncrementAsync("test", "doc1", "counter", 1));

        Assert.Equal(AtomicOperationType.Increment, ex.OperationType);
    }

    [Theory]
    [InlineData("", "doc1", "field")]
    [InlineData("test", "", "field")]
    [InlineData("test", "doc1", "")]
    public async Task IncrementAsync_EmptyParameters_ThrowsArgumentException(string collection, string docId, string fieldPath)
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("test");
        var doc = CreateTestDocument("doc1", new Dictionary<string, object>());
        await store.InsertAsync("test", doc);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.IncrementAsync(collection, docId, fieldPath, 1));
    }

    [Fact]
    public async Task IncrementAsync_MultipleConcurrentIncrements_AreAllApplied()
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("test");

        var doc = CreateTestDocument("doc1", new Dictionary<string, object>
        {
            ["counter"] = 0
        });
        await store.InsertAsync("test", doc);

        // Perform 10 concurrent increments
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => store.IncrementAsync("test", "doc1", "counter", 1))
            .ToArray();

        await Task.WhenAll(tasks);

        var finalDoc = await store.GetAsync("test", "doc1");
        Assert.NotNull(finalDoc);
        Assert.Equal(10.0, finalDoc.Data!["counter"]);
    }

    #endregion

    #region PushAsync Tests

    [Fact]
    public async Task PushAsync_NonExistentDocument_ThrowsDocumentNotFoundException()
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("test");

        await Assert.ThrowsAsync<DocumentNotFoundException>(() =>
            store.PushAsync("test", "nonexistent", "items", "value"));
    }

    [Fact]
    public async Task PushAsync_ToNullField_CreatesNewArray()
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("test");

        var doc = CreateTestDocument("doc1", new Dictionary<string, object>{});
        await store.InsertAsync("test", doc);

        var result = await store.PushAsync("test", "doc1", "items", "item1");

        Assert.NotNull(result);
        var items = result.Data!["items"] as List<object>;
        Assert.NotNull(items);
        Assert.Single(items);
        Assert.Equal("item1", items[0]);
    }

    [Fact]
    public async Task PushAsync_ToExistingArray_AddsToArray()
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("test");

        var doc = CreateTestDocument("doc1", new Dictionary<string, object>
        {
            ["items"] = new List<object> { "item1", "item2" }
        });
        await store.InsertAsync("test", doc);

        var result = await store.PushAsync("test", "doc1", "items", "item3");

        Assert.NotNull(result);
        var items = result.Data!["items"] as List<object>;
        Assert.NotNull(items);
        Assert.Equal(3, items.Count);
        Assert.Equal("item3", items[2]);
    }

    [Fact]
    public async Task PushAsync_NestedArray_AddsToNestedArray()
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("test");

        var doc = CreateTestDocument("doc1", new Dictionary<string, object>
        {
            ["data"] = new Dictionary<string, object>
            {
                ["tags"] = new List<object> { "tag1" }
            }
        });
        await store.InsertAsync("test", doc);

        var result = await store.PushAsync("test", "doc1", "data.tags", "tag2");

        Assert.NotNull(result);
        var data = result.Data!["data"] as Dictionary<string, object>;
        var tags = data!["tags"] as List<object>;
        Assert.NotNull(tags);
        Assert.Equal(2, tags.Count);
    }

    [Fact]
    public async Task PushAsync_NonArrayField_ThrowsAtomicUpdateException()
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("test");

        var doc = CreateTestDocument("doc1", new Dictionary<string, object>
        {
            ["items"] = "not an array"
        });
        await store.InsertAsync("test", doc);

        var ex = await Assert.ThrowsAsync<AtomicUpdateException>(() =>
            store.PushAsync("test", "doc1", "items", "item1"));

        Assert.Equal(AtomicOperationType.Push, ex.OperationType);
    }

    [Fact]
    public async Task PushManyAsync_MultipleValues_AddsAllValues()
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("test");

        var doc = CreateTestDocument("doc1", new Dictionary<string, object>
        {
            ["items"] = new List<object> { "existing" }
        });
        await store.InsertAsync("test", doc);

        var result = await store.PushManyAsync("test", "doc1", "items", new[] { "item1", "item2", "item3" });

        Assert.NotNull(result);
        var items = result.Data!["items"] as List<object>;
        Assert.NotNull(items);
        Assert.Equal(4, items.Count);
    }

    [Fact]
    public async Task PushManyAsync_EmptyList_ReturnsOriginalDocument()
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("test");

        var doc = CreateTestDocument("doc1", new Dictionary<string, object>
        {
            ["items"] = new List<object> { "existing" }
        });
        await store.InsertAsync("test", doc);

        var result = await store.PushManyAsync("test", "doc1", "items", new List<object>());

        Assert.NotNull(result);
        var items = result.Data!["items"] as List<object>;
        Assert.NotNull(items);
        Assert.Single(items);
    }

    #endregion

    #region PullAsync Tests

    [Fact]
    public async Task PullAsync_NonExistentDocument_ThrowsDocumentNotFoundException()
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("test");

        await Assert.ThrowsAsync<DocumentNotFoundException>(() =>
            store.PullAsync("test", "nonexistent", "items", "value"));
    }

    [Fact]
    public async Task PullAsync_FromNullField_ReturnsOriginalDocument()
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("test");

        var doc = CreateTestDocument("doc1", new Dictionary<string, object>{});
        await store.InsertAsync("test", doc);

        var result = await store.PullAsync("test", "doc1", "items", "item1");

        Assert.NotNull(result);
        Assert.False(result.Data!.ContainsKey("items"));
    }

    [Fact]
    public async Task PullAsync_ExistingValue_RemovesValue()
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("test");

        var doc = CreateTestDocument("doc1", new Dictionary<string, object>
        {
            ["items"] = new List<object> { "item1", "item2", "item3" }
        });
        await store.InsertAsync("test", doc);

        var result = await store.PullAsync("test", "doc1", "items", "item2");

        Assert.NotNull(result);
        var items = result.Data!["items"] as List<object>;
        Assert.NotNull(items);
        Assert.Equal(2, items.Count);
        Assert.DoesNotContain("item2", items);
    }

    [Fact]
    public async Task PullAsync_AllOccurrences_RemovesAll()
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("test");

        var doc = CreateTestDocument("doc1", new Dictionary<string, object>
        {
            ["items"] = new List<object> { "dup", "item2", "dup", "item4", "dup" }
        });
        await store.InsertAsync("test", doc);

        var result = await store.PullAsync("test", "doc1", "items", "dup");

        Assert.NotNull(result);
        var items = result.Data!["items"] as List<object>;
        Assert.NotNull(items);
        Assert.Equal(2, items.Count);
        Assert.DoesNotContain("dup", items);
    }

    [Fact]
    public async Task PullAsync_NonExistentValue_KeepsArrayUnchanged()
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("test");

        var doc = CreateTestDocument("doc1", new Dictionary<string, object>
        {
            ["items"] = new List<object> { "item1", "item2" }
        });
        await store.InsertAsync("test", doc);

        var result = await store.PullAsync("test", "doc1", "items", "nonexistent");

        Assert.NotNull(result);
        var items = result.Data!["items"] as List<object>;
        Assert.NotNull(items);
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task PullAsync_NonArrayField_ThrowsAtomicUpdateException()
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("test");

        var doc = CreateTestDocument("doc1", new Dictionary<string, object>
        {
            ["items"] = "not an array"
        });
        await store.InsertAsync("test", doc);

        var ex = await Assert.ThrowsAsync<AtomicUpdateException>(() =>
            store.PullAsync("test", "doc1", "items", "item1"));

        Assert.Equal(AtomicOperationType.Pull, ex.OperationType);
    }

    [Fact]
    public async Task PullManyAsync_MultipleValues_RemovesAllMatches()
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("test");

        var doc = CreateTestDocument("doc1", new Dictionary<string, object>
        {
            ["items"] = new List<object> { "a", "b", "c", "d", "e" }
        });
        await store.InsertAsync("test", doc);

        var result = await store.PullManyAsync("test", "doc1", "items", new[] { "b", "d" });

        Assert.NotNull(result);
        var items = result.Data!["items"] as List<object>;
        Assert.NotNull(items);
        Assert.Equal(3, items.Count);
        Assert.Contains("a", items);
        Assert.Contains("c", items);
        Assert.Contains("e", items);
    }

    [Fact]
    public async Task PullManyAsync_EmptyList_ReturnsOriginalDocument()
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("test");

        var doc = CreateTestDocument("doc1", new Dictionary<string, object>
        {
            ["items"] = new List<object> { "item1", "item2" }
        });
        await store.InsertAsync("test", doc);

        var result = await store.PullManyAsync("test", "doc1", "items", new List<object>());

        Assert.NotNull(result);
        var items = result.Data!["items"] as List<object>;
        Assert.NotNull(items);
        Assert.Equal(2, items.Count);
    }

    #endregion

    #region SetAsync Tests

    [Fact]
    public async Task SetAsync_NonExistentDocument_ThrowsDocumentNotFoundException()
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("test");

        await Assert.ThrowsAsync<DocumentNotFoundException>(() =>
            store.SetAsync("test", "nonexistent", "field", "value"));
    }

    [Fact]
    public async Task SetAsync_NewField_AddsField()
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("test");

        var doc = CreateTestDocument("doc1", new Dictionary<string, object>{});
        await store.InsertAsync("test", doc);

        var result = await store.SetAsync("test", "doc1", "newField", "newValue");

        Assert.NotNull(result);
        Assert.Equal("newValue", result.Data!["newField"]);
    }

    [Fact]
    public async Task SetAsync_ExistingField_UpdatesField()
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("test");

        var doc = CreateTestDocument("doc1", new Dictionary<string, object>
        {
            ["field"] = "oldValue"
        });
        await store.InsertAsync("test", doc);

        var result = await store.SetAsync("test", "doc1", "field", "newValue");

        Assert.NotNull(result);
        Assert.Equal("newValue", result.Data!["field"]);
    }

    [Fact]
    public async Task SetAsync_NestedField_CreatesNestedStructure()
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("test");

        var doc = CreateTestDocument("doc1", new Dictionary<string, object>{});
        await store.InsertAsync("test", doc);

        var result = await store.SetAsync("test", "doc1", "level1.level2.level3", "deepValue");

        Assert.NotNull(result);
        Assert.Equal("deepValue", GetDeepValue(result.Data!, "level1", "level2", "level3"));
    }

    [Fact]
    public async Task SetAsync_ComplexValue_SetsComplexValue()
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("test");

        var doc = CreateTestDocument("doc1", new Dictionary<string, object>{});
        await store.InsertAsync("test", doc);

        var complexValue = new Dictionary<string, object>
        {
            ["nested"] = "value",
            ["number"] = 42
        };

        var result = await store.SetAsync("test", "doc1", "complex", complexValue);

        Assert.NotNull(result);
        var retrievedComplex = result.Data!["complex"] as Dictionary<string, object>;
        Assert.NotNull(retrievedComplex);
        Assert.Equal("value", retrievedComplex["nested"]);
        Assert.Equal(42, retrievedComplex["number"]);
    }

    #endregion

    #region UnsetAsync Tests

    [Fact]
    public async Task UnsetAsync_NonExistentDocument_ThrowsDocumentNotFoundException()
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("test");

        await Assert.ThrowsAsync<DocumentNotFoundException>(() =>
            store.UnsetAsync("test", "nonexistent", "field"));
    }

    [Fact]
    public async Task UnsetAsync_ExistingField_RemovesField()
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("test");

        var doc = CreateTestDocument("doc1", new Dictionary<string, object>
        {
            ["field1"] = "value1",
            ["field2"] = "value2"
        });
        await store.InsertAsync("test", doc);

        var result = await store.UnsetAsync("test", "doc1", "field1");

        Assert.NotNull(result);
        Assert.False(result.Data!.ContainsKey("field1"));
        Assert.True(result.Data.ContainsKey("field2"));
    }

    [Fact]
    public async Task UnsetAsync_NonExistentField_ReturnsDocumentUnchanged()
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("test");

        var doc = CreateTestDocument("doc1", new Dictionary<string, object>
        {
            ["field"] = "value"
        });
        await store.InsertAsync("test", doc);

        var result = await store.UnsetAsync("test", "doc1", "nonexistent");

        Assert.NotNull(result);
        Assert.Equal("value", result.Data!["field"]);
    }

    [Fact]
    public async Task UnsetAsync_NestedField_RemovesNestedField()
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("test");

        var doc = CreateTestDocument("doc1", new Dictionary<string, object>
        {
            ["data"] = new Dictionary<string, object>
            {
                ["field1"] = "value1",
                ["field2"] = "value2"
            }
        });
        await store.InsertAsync("test", doc);

        var result = await store.UnsetAsync("test", "doc1", "data.field1");

        Assert.NotNull(result);
        var data = result.Data!["data"] as Dictionary<string, object>;
        Assert.NotNull(data);
        Assert.False(data.ContainsKey("field1"));
        Assert.True(data.ContainsKey("field2"));
    }

    #endregion

    #region UpdateMultipleAsync Tests

    [Fact]
    public async Task UpdateMultipleAsync_NonExistentDocument_ThrowsDocumentNotFoundException()
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("test");

        var operations = new[] { AtomicOperation.Set("field", "value") };
        await Assert.ThrowsAsync<DocumentNotFoundException>(() =>
            store.UpdateMultipleAsync("test", "nonexistent", operations));
    }

    [Fact]
    public async Task UpdateMultipleAsync_EmptyOperations_ReturnsOriginalDocument()
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("test");

        var doc = CreateTestDocument("doc1", new Dictionary<string, object>
        {
            ["field"] = "value"
        });
        await store.InsertAsync("test", doc);

        var result = await store.UpdateMultipleAsync("test", "doc1", new List<AtomicOperation>());

        Assert.NotNull(result);
        Assert.Equal("value", result.Data!["field"]);
    }

    [Fact]
    public async Task UpdateMultipleAsync_MultipleOperations_AppliesAll()
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("test");

        var doc = CreateTestDocument("doc1", new Dictionary<string, object>
        {
            ["counter"] = 10,
            ["items"] = new List<object> { "item1" },
            ["oldField"] = "value"
        });
        await store.InsertAsync("test", doc);

        var operations = new[]
        {
            AtomicOperation.Increment("counter", 5),
            AtomicOperation.Push("items", "item2"),
            AtomicOperation.Unset("oldField"),
            AtomicOperation.Set("newField", "newValue")
        };

        var result = await store.UpdateMultipleAsync("test", "doc1", operations);

        Assert.NotNull(result);
        Assert.Equal(15.0, result.Data!["counter"]);
        var items = result.Data["items"] as List<object>;
        Assert.NotNull(items);
        Assert.Equal(2, items.Count);
        Assert.False(result.Data.ContainsKey("oldField"));
        Assert.Equal("newValue", result.Data["newField"]);
    }

    [Fact]
    public async Task UpdateMultipleAsync_MixedOperationsOnNestedFields_WorksCorrectly()
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("test");

        var doc = CreateTestDocument("doc1", new Dictionary<string, object>
        {
            ["stats"] = new Dictionary<string, object>
            {
                ["views"] = 100,
                ["likes"] = 50
            }
        });
        await store.InsertAsync("test", doc);

        var operations = new[]
        {
            AtomicOperation.Increment("stats.views", 25),
            AtomicOperation.Set("stats.likes", 75),
            AtomicOperation.Set("stats.comments", 10)
        };

        var result = await store.UpdateMultipleAsync("test", "doc1", operations);

        Assert.NotNull(result);
        var stats = result.Data!["stats"] as Dictionary<string, object>;
        Assert.NotNull(stats);
        Assert.Equal(125.0, stats["views"]);
        Assert.Equal(75, stats["likes"]);
        Assert.Equal(10, stats["comments"]);
    }

    #endregion

    #region AtomicOperation Helper Tests

    [Fact]
    public void AtomicOperation_Increment_CreatesCorrectOperation()
    {
        var op = AtomicOperation.Increment("counter", 5);

        Assert.Equal(AtomicOperationType.Increment, op.Type);
        Assert.Equal("counter", op.FieldPath);
        Assert.Equal(5.0, op.Value);
    }

    [Fact]
    public void AtomicOperation_Push_CreatesCorrectOperation()
    {
        var op = AtomicOperation.Push("items", "value");

        Assert.Equal(AtomicOperationType.Push, op.Type);
        Assert.Equal("items", op.FieldPath);
        Assert.Equal("value", op.Value);
    }

    [Fact]
    public void AtomicOperation_Pull_CreatesCorrectOperation()
    {
        var op = AtomicOperation.Pull("items", "value");

        Assert.Equal(AtomicOperationType.Pull, op.Type);
        Assert.Equal("items", op.FieldPath);
        Assert.Equal("value", op.Value);
    }

    [Fact]
    public void AtomicOperation_Set_CreatesCorrectOperation()
    {
        var op = AtomicOperation.Set("field", "value");

        Assert.Equal(AtomicOperationType.Set, op.Type);
        Assert.Equal("field", op.FieldPath);
        Assert.Equal("value", op.Value);
    }

    [Fact]
    public void AtomicOperation_Unset_CreatesCorrectOperation()
    {
        var op = AtomicOperation.Unset("field");

        Assert.Equal(AtomicOperationType.Unset, op.Type);
        Assert.Equal("field", op.FieldPath);
        Assert.Null(op.Value);
    }

    [Fact]
    public void AtomicOperation_NullFieldPath_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new AtomicOperation(AtomicOperationType.Set, null!, "value"));
    }

    #endregion

    #region AtomicUpdateException Tests

    [Fact]
    public void AtomicUpdateException_WithOperationType_ContainsOperationInfo()
    {
        var ex = new AtomicUpdateException("test", "doc1", "field", AtomicOperationType.Increment, "test message");

        Assert.Equal("test", ex.CollectionName);
        Assert.Equal("doc1", ex.DocumentId);
        Assert.Equal("field", ex.FieldPath);
        Assert.Equal(AtomicOperationType.Increment, ex.OperationType);
        Assert.Contains("Increment", ex.Message);
    }

    [Fact]
    public void AtomicUpdateException_WithoutOperationType_HasCorrectInfo()
    {
        var ex = new AtomicUpdateException("test", "doc1", "field", "test message");

        Assert.Equal("test", ex.CollectionName);
        Assert.Equal("doc1", ex.DocumentId);
        Assert.Equal("field", ex.FieldPath);
        Assert.Null(ex.OperationType);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task AtomicOperations_RealWorldScenario_ShoppingCart()
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("carts");

        // Create a shopping cart
        var cart = CreateTestDocument("cart1", new Dictionary<string, object>
        {
            ["userId"] = "user123",
            ["items"] = new List<object>(),
            ["totalAmount"] = 0.0,
            ["itemCount"] = 0
        });
        await store.InsertAsync("carts", cart);

        // Add items to cart
        await store.PushAsync("carts", "cart1", "items", new Dictionary<string, object>
        {
            ["productId"] = "prod1",
            ["name"] = "Widget",
            ["price"] = 29.99,
            ["quantity"] = 2
        });
        await store.IncrementAsync("carts", "cart1", "totalAmount", 59.98);
        await store.IncrementAsync("carts", "cart1", "itemCount", 1);

        // Verify cart state
        var updatedCart = await store.GetAsync("carts", "cart1");
        Assert.NotNull(updatedCart);
        Assert.Equal(59.98, updatedCart.Data!["totalAmount"]);
        Assert.Equal(1.0, updatedCart.Data["itemCount"]);
        var items = updatedCart.Data["items"] as List<object>;
        Assert.NotNull(items);
        Assert.Single(items);

        // Update item quantity
        var operations = new[]
        {
            AtomicOperation.Increment("totalAmount", 29.99), // Add one more widget
        };
        await store.UpdateMultipleAsync("carts", "cart1", operations);

        // Remove item from cart
        var itemToRemove = new Dictionary<string, object>
        {
            ["productId"] = "prod1",
            ["name"] = "Widget",
            ["price"] = 29.99,
            ["quantity"] = 2
        };
        
        // Set total amount to 0 and clear items
        await store.SetAsync("carts", "cart1", "totalAmount", 0);
        await store.SetAsync("carts", "cart1", "itemCount", 0);
        await store.SetAsync("carts", "cart1", "items", new List<object>());

        var clearedCart = await store.GetAsync("carts", "cart1");
        Assert.NotNull(clearedCart);
        Assert.Equal(0, clearedCart.Data!["totalAmount"]);
        var clearedItems = clearedCart.Data["items"] as List<object>;
        Assert.NotNull(clearedItems);
        Assert.Empty(clearedItems);
    }

    [Fact]
    public async Task AtomicOperations_RealWorldScenario_UserStats()
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("users");

        // Create a user with stats
        var user = CreateTestDocument("user1", new Dictionary<string, object>
        {
            ["username"] = "john_doe",
            ["stats"] = new Dictionary<string, object>
            {
                ["logins"] = 0,
                ["posts"] = 0,
                ["comments"] = 0,
                ["reputation"] = 100.0
            },
            ["tags"] = new List<object>()
        });
        await store.InsertAsync("users", user);

        // Simulate user activity
        for (int i = 0; i < 5; i++)
        {
            await store.IncrementAsync("users", "user1", "stats.logins", 1);
        }

        await store.UpdateMultipleAsync("users", "user1", new[]
        {
            AtomicOperation.Increment("stats.posts", 3),
            AtomicOperation.Increment("stats.comments", 10),
            AtomicOperation.Increment("stats.reputation", 15.5),
            AtomicOperation.Push("tags", "active_user")
        });

        // Verify stats
        var updatedUser = await store.GetAsync("users", "user1");
        Assert.NotNull(updatedUser);
        var stats = updatedUser.Data!["stats"] as Dictionary<string, object>;
        Assert.NotNull(stats);
        Assert.Equal(5.0, stats["logins"]);
        Assert.Equal(3.0, stats["posts"]);
        Assert.Equal(10.0, stats["comments"]);
        Assert.Equal(115.5, stats["reputation"]);
        var tags = updatedUser.Data["tags"] as List<object>;
        Assert.NotNull(tags);
        Assert.Single(tags);
        Assert.Equal("active_user", tags[0]);
    }

    [Fact]
    public async Task AtomicOperations_ConcurrentArrayOperations_AreThreadSafe()
    {
        var store = CreateStore();
        await store.CreateCollectionAsync("test");

        var doc = CreateTestDocument("doc1", new Dictionary<string, object>
        {
            ["items"] = new List<object>()
        });
        await store.InsertAsync("test", doc);

        // Perform 50 concurrent push operations
        var tasks = Enumerable.Range(0, 50)
            .Select(i => store.PushAsync("test", "doc1", "items", $"item{i}"))
            .ToArray();

        await Task.WhenAll(tasks);

        var finalDoc = await store.GetAsync("test", "doc1");
        Assert.NotNull(finalDoc);
        var items = finalDoc.Data!["items"] as List<object>;
        Assert.NotNull(items);
        Assert.Equal(50, items.Count);
    }

    #endregion

    #region Helper Methods

    private static object? GetDeepValue(Dictionary<string, object> data, params string[] path)
    {
        var current = data;
        for (int i = 0; i < path.Length - 1; i++)
        {
            if (!current.TryGetValue(path[i], out var next) || next is not Dictionary<string, object> nextDict)
            {
                return null;
            }
            current = nextDict;
        }
        current.TryGetValue(path[path.Length - 1], out var value);
        return value;
    }

    #endregion
}
