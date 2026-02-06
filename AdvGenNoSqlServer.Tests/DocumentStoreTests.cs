// Copyright (c) 2026 [Your Organization]
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Storage;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for DocumentStore implementation
/// </summary>
public class DocumentStoreTests
{
    private readonly DocumentStore _store;

    public DocumentStoreTests()
    {
        _store = new DocumentStore();
    }

    #region Insert Tests

    [Fact]
    public async Task InsertAsync_ValidDocument_ReturnsDocumentWithMetadata()
    {
        // Arrange
        var document = new Document
        {
            Id = "test-1",
            Data = new Dictionary<string, object> { { "name", "Test" } }
        };

        // Act
        var result = await _store.InsertAsync("users", document);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test-1", result.Id);
        Assert.NotNull(result.Data);
        Assert.Equal("Test", result.Data["name"]);
        Assert.True(result.Version == 1);
        Assert.True(result.CreatedAt > DateTime.MinValue);
        Assert.True(result.UpdatedAt > DateTime.MinValue);
    }

    [Fact]
    public async Task InsertAsync_DuplicateId_ThrowsDocumentAlreadyExistsException()
    {
        // Arrange
        var document = new Document
        {
            Id = "duplicate-id",
            Data = new Dictionary<string, object>()
        };
        await _store.InsertAsync("users", document);

        // Act & Assert
        await Assert.ThrowsAsync<DocumentAlreadyExistsException>(() =>
            _store.InsertAsync("users", document));
    }

    [Fact]
    public async Task InsertAsync_EmptyCollectionName_ThrowsArgumentException()
    {
        // Arrange
        var document = new Document { Id = "test" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _store.InsertAsync("", document));
    }

    [Fact]
    public async Task InsertAsync_NullDocument_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _store.InsertAsync("users", null!));
    }

    [Fact]
    public async Task InsertAsync_EmptyDocumentId_ThrowsArgumentException()
    {
        // Arrange
        var document = new Document { Id = "" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _store.InsertAsync("users", document));
    }

    [Fact]
    public async Task InsertAsync_NullData_CreatesEmptyDataDictionary()
    {
        // Arrange
        var document = new Document { Id = "test-null-data" };

        // Act
        var result = await _store.InsertAsync("users", document);

        // Assert
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data);
    }

    #endregion

    #region Get Tests

    [Fact]
    public async Task GetAsync_ExistingDocument_ReturnsDocument()
    {
        // Arrange
        var document = new Document
        {
            Id = "get-test",
            Data = new Dictionary<string, object> { { "key", "value" } }
        };
        await _store.InsertAsync("users", document);

        // Act
        var result = await _store.GetAsync("users", "get-test");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("get-test", result.Id);
        Assert.Equal("value", result.Data!["key"]);
    }

    [Fact]
    public async Task GetAsync_NonExistentDocument_ReturnsNull()
    {
        // Act
        var result = await _store.GetAsync("users", "non-existent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_NonExistentCollection_ReturnsNull()
    {
        // Act
        var result = await _store.GetAsync("non-existent-collection", "test");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_EmptyDocumentId_ReturnsNull()
    {
        // Act
        var result = await _store.GetAsync("users", "");

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetAll Tests

    [Fact]
    public async Task GetAllAsync_EmptyCollection_ReturnsEmptyList()
    {
        // Act
        var result = await _store.GetAllAsync("empty-collection");

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAllAsync_WithDocuments_ReturnsAllDocuments()
    {
        // Arrange
        await _store.InsertAsync("users", new Document { Id = "user-1" });
        await _store.InsertAsync("users", new Document { Id = "user-2" });
        await _store.InsertAsync("users", new Document { Id = "user-3" });

        // Act
        var result = await _store.GetAllAsync("users");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Count());
    }

    [Fact]
    public async Task GetAllAsync_DifferentCollections_ReturnsCorrectDocuments()
    {
        // Arrange
        await _store.InsertAsync("users", new Document { Id = "user-1" });
        await _store.InsertAsync("products", new Document { Id = "product-1" });

        // Act
        var users = await _store.GetAllAsync("users");
        var products = await _store.GetAllAsync("products");

        // Assert
        Assert.Single(users);
        Assert.Single(products);
        Assert.Equal("user-1", users.First().Id);
        Assert.Equal("product-1", products.First().Id);
    }

    #endregion

    #region Update Tests

    [Fact]
    public async Task UpdateAsync_ExistingDocument_UpdatesAndIncrementsVersion()
    {
        // Arrange
        var document = new Document
        {
            Id = "update-test",
            Data = new Dictionary<string, object> { { "name", "Original" } }
        };
        var inserted = await _store.InsertAsync("users", document);

        // Act
        var updatedDocument = new Document
        {
            Id = "update-test",
            Data = new Dictionary<string, object> { { "name", "Updated" } }
        };
        var result = await _store.UpdateAsync("users", updatedDocument);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("update-test", result.Id);
        Assert.Equal("Updated", result.Data!["name"]);
        Assert.Equal(inserted.Version + 1, result.Version);
        Assert.True(result.UpdatedAt > result.CreatedAt);
    }

    [Fact]
    public async Task UpdateAsync_NonExistentDocument_ThrowsDocumentNotFoundException()
    {
        // Arrange - create collection but not the document
        await _store.CreateCollectionAsync("users");
        var document = new Document
        {
            Id = "non-existent",
            Data = new Dictionary<string, object>()
        };

        // Act & Assert
        await Assert.ThrowsAsync<DocumentNotFoundException>(() =>
            _store.UpdateAsync("users", document));
    }

    [Fact]
    public async Task UpdateAsync_NonExistentCollection_ThrowsCollectionNotFoundException()
    {
        // Arrange
        var document = new Document
        {
            Id = "test",
            Data = new Dictionary<string, object>()
        };

        // Act & Assert
        await Assert.ThrowsAsync<CollectionNotFoundException>(() =>
            _store.UpdateAsync("non-existent-collection", document));
    }

    [Fact]
    public async Task UpdateAsync_NullData_KeepsExistingData()
    {
        // Arrange
        var document = new Document
        {
            Id = "update-null-data",
            Data = new Dictionary<string, object> { { "key", "value" } }
        };
        await _store.InsertAsync("users", document);

        // Act
        var updatedDocument = new Document { Id = "update-null-data" };
        var result = await _store.UpdateAsync("users", updatedDocument);

        // Assert
        Assert.NotNull(result.Data);
        Assert.Equal("value", result.Data!["key"]);
    }

    #endregion

    #region Delete Tests

    [Fact]
    public async Task DeleteAsync_ExistingDocument_ReturnsTrue()
    {
        // Arrange
        await _store.InsertAsync("users", new Document { Id = "delete-test" });

        // Act
        var result = await _store.DeleteAsync("users", "delete-test");

        // Assert
        Assert.True(result);
        Assert.Null(await _store.GetAsync("users", "delete-test"));
    }

    [Fact]
    public async Task DeleteAsync_NonExistentDocument_ReturnsFalse()
    {
        // Act
        var result = await _store.DeleteAsync("users", "non-existent");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task DeleteAsync_NonExistentCollection_ReturnsFalse()
    {
        // Act
        var result = await _store.DeleteAsync("non-existent-collection", "test");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task DeleteAsync_EmptyDocumentId_ReturnsFalse()
    {
        // Act
        var result = await _store.DeleteAsync("users", "");

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Exists Tests

    [Fact]
    public async Task ExistsAsync_ExistingDocument_ReturnsTrue()
    {
        // Arrange
        await _store.InsertAsync("users", new Document { Id = "exists-test" });

        // Act
        var result = await _store.ExistsAsync("users", "exists-test");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ExistsAsync_NonExistentDocument_ReturnsFalse()
    {
        // Act
        var result = await _store.ExistsAsync("users", "non-existent");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ExistsAsync_NonExistentCollection_ReturnsFalse()
    {
        // Act
        var result = await _store.ExistsAsync("non-existent-collection", "test");

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Count Tests

    [Fact]
    public async Task CountAsync_EmptyCollection_ReturnsZero()
    {
        // Act
        var result = await _store.CountAsync("empty-collection");

        // Assert
        Assert.Equal(0L, result);
    }

    [Fact]
    public async Task CountAsync_WithDocuments_ReturnsCorrectCount()
    {
        // Arrange
        await _store.InsertAsync("users", new Document { Id = "count-1" });
        await _store.InsertAsync("users", new Document { Id = "count-2" });
        await _store.InsertAsync("users", new Document { Id = "count-3" });

        // Act
        var result = await _store.CountAsync("users");

        // Assert
        Assert.Equal(3L, result);
    }

    [Fact]
    public async Task CountAsync_AfterDelete_ReturnsUpdatedCount()
    {
        // Arrange
        await _store.InsertAsync("users", new Document { Id = "delete-count-1" });
        await _store.InsertAsync("users", new Document { Id = "delete-count-2" });

        // Act
        await _store.DeleteAsync("users", "delete-count-1");
        var result = await _store.CountAsync("users");

        // Assert
        Assert.Equal(1L, result);
    }

    #endregion

    #region Collection Management Tests

    [Fact]
    public async Task CreateCollectionAsync_NewCollection_CreatesSuccessfully()
    {
        // Act
        await _store.CreateCollectionAsync("new-collection");

        // Assert
        var collections = await _store.GetCollectionsAsync();
        Assert.Contains("new-collection", collections);
    }

    [Fact]
    public async Task CreateCollectionAsync_DuplicateCollection_DoesNotThrow()
    {
        // Arrange
        await _store.CreateCollectionAsync("duplicate-collection");

        // Act & Assert (should not throw)
        await _store.CreateCollectionAsync("duplicate-collection");
    }

    [Fact]
    public async Task DropCollectionAsync_ExistingCollection_RemovesCollection()
    {
        // Arrange
        await _store.CreateCollectionAsync("drop-test");
        await _store.InsertAsync("drop-test", new Document { Id = "doc-1" });

        // Act
        var result = await _store.DropCollectionAsync("drop-test");

        // Assert
        Assert.True(result);
        var collections = await _store.GetCollectionsAsync();
        Assert.DoesNotContain("drop-test", collections);
    }

    [Fact]
    public async Task DropCollectionAsync_NonExistentCollection_ReturnsFalse()
    {
        // Act
        var result = await _store.DropCollectionAsync("non-existent-collection");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task GetCollectionsAsync_WithMultipleCollections_ReturnsAllNames()
    {
        // Arrange
        await _store.CreateCollectionAsync("collection-a");
        await _store.CreateCollectionAsync("collection-b");
        await _store.CreateCollectionAsync("collection-c");

        // Act
        var result = await _store.GetCollectionsAsync();

        // Assert
        Assert.Equal(3, result.Count());
        Assert.Contains("collection-a", result);
        Assert.Contains("collection-b", result);
        Assert.Contains("collection-c", result);
    }

    [Fact]
    public async Task GetCollectionsAsync_NoCollections_ReturnsEmptyList()
    {
        // Act
        var result = await _store.GetCollectionsAsync();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task ClearCollectionAsync_WithDocuments_RemovesAllDocuments()
    {
        // Arrange
        await _store.InsertAsync("clear-test", new Document { Id = "doc-1" });
        await _store.InsertAsync("clear-test", new Document { Id = "doc-2" });

        // Act
        await _store.ClearCollectionAsync("clear-test");

        // Assert
        var count = await _store.CountAsync("clear-test");
        Assert.Equal(0L, count);
        var allDocs = await _store.GetAllAsync("clear-test");
        Assert.Empty(allDocs);
    }

    [Fact]
    public async Task ClearCollectionAsync_NonExistentCollection_DoesNotThrow()
    {
        // Act & Assert (should not throw)
        await _store.ClearCollectionAsync("non-existent-collection");
    }

    #endregion

    #region Validation Tests

    [Fact]
    public async Task InsertAsync_SameIdDifferentCollections_Allowed()
    {
        // Arrange
        var document = new Document
        {
            Id = "shared-id",
            Data = new Dictionary<string, object> { { "type", "test" } }
        };

        // Act
        var result1 = await _store.InsertAsync("collection-1", document);
        var result2 = await _store.InsertAsync("collection-2", document);

        // Assert
        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.Equal("shared-id", result1.Id);
        Assert.Equal("shared-id", result2.Id);
    }

    [Fact]
    public async Task InsertAsync_AutoCreatesCollection()
    {
        // Act
        await _store.InsertAsync("auto-created", new Document { Id = "test" });

        // Assert
        var collections = await _store.GetCollectionsAsync();
        Assert.Contains("auto-created", collections);
    }

    #endregion
}
