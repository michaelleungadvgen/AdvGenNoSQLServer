// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Storage.Indexing;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for Partial Index functionality
/// </summary>
public class PartialIndexTests
{
    private readonly IndexManager _indexManager = new();

    #region PartialBTreeIndex Tests

    [Fact]
    public void PartialBTreeIndex_Constructor_ValidParameters_CreatesIndex()
    {
        // Arrange
        string indexName = "test_idx";
        string collectionName = "test_collection";
        string fieldName = "status";
        Func<Document, bool> filter = doc => doc.Data.TryGetValue("active", out var val) && val is bool b && b;

        // Act
        var index = new PartialBTreeIndex<string>(indexName, collectionName, fieldName, false, filter);

        // Assert
        Assert.NotNull(index);
        Assert.Equal(indexName, index.Name);
        Assert.Equal(collectionName, index.CollectionName);
        Assert.Equal(fieldName, index.FieldName);
        Assert.False(index.IsUnique);
        Assert.Equal(PartialIndexType.Partial, index.PartialType);
        Assert.Equal(filter, index.FilterExpression);
    }

    [Fact]
    public void PartialBTreeIndex_Constructor_NullFilter_ThrowsArgumentNullException()
    {
        // Arrange
        string indexName = "test_idx";
        string collectionName = "test_collection";
        string fieldName = "status";
        Func<Document, bool> filter = null!;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new PartialBTreeIndex<string>(indexName, collectionName, fieldName, false, filter));
    }

    [Fact]
    public void PartialBTreeIndex_ShouldIncludeDocument_DocumentHasFieldAndMatchesFilter_ReturnsTrue()
    {
        // Arrange
        Func<Document, bool> filter = doc => doc.Data.TryGetValue("status", out var val) && val?.ToString() == "active";
        var index = new PartialBTreeIndex<string>("test_idx", "test_collection", "email", false, filter);
        var document = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object?>
            {
                ["email"] = "test@example.com",
                ["status"] = "active"
            }
        };

        // Act
        bool result = index.ShouldIncludeDocument(document);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void PartialBTreeIndex_ShouldIncludeDocument_DocumentHasFieldButNoMatchFilter_ReturnsFalse()
    {
        // Arrange
        Func<Document, bool> filter = doc => doc.Data.TryGetValue("status", out var val) && val?.ToString() == "active";
        var index = new PartialBTreeIndex<string>("test_idx", "test_collection", "email", false, filter);
        var document = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object?>
            {
                ["email"] = "test@example.com",
                ["status"] = "inactive"
            }
        };

        // Act
        bool result = index.ShouldIncludeDocument(document);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void PartialBTreeIndex_ShouldIncludeDocument_DocumentMissingIndexedField_ReturnsFalse()
    {
        // Arrange
        Func<Document, bool> filter = doc => doc.Data.TryGetValue("status", out var val) && val?.ToString() == "active";
        var index = new PartialBTreeIndex<string>("test_idx", "test_collection", "email", false, filter);
        var document = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object?>
            {
                ["status"] = "active"
                // No "email" field
            }
        };

        // Act
        bool result = index.ShouldIncludeDocument(document);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void PartialBTreeIndex_ShouldIncludeDocument_DocumentMissingFilterField_ReturnsFalse()
    {
        // Arrange
        Func<Document, bool> filter = doc => doc.Data.TryGetValue("status", out var val) && val?.ToString() == "active";
        var index = new PartialBTreeIndex<string>("test_idx", "test_collection", "email", false, filter);
        var document = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object?>
            {
                ["email"] = "test@example.com"
                // No "status" field
            }
        };

        // Act
        bool result = index.ShouldIncludeDocument(document);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region IndexManager CreatePartialIndex Tests

    [Fact]
    public void IndexManager_CreatePartialIndex_ValidParameters_CreatesIndex()
    {
        // Arrange
        string collectionName = "users";
        string fieldName = "email";
        Func<Document, bool> filter = doc => doc.Data.TryGetValue("status", out var val) && val?.ToString() == "active";

        // Act
        var index = _indexManager.CreatePartialIndex<string>(collectionName, fieldName, false,
            doc => doc.Data.TryGetValue("email", out var val) ? val?.ToString() : null,
            filter);

        // Assert
        Assert.NotNull(index);
        Assert.True(_indexManager.HasIndex(collectionName, fieldName));
    }

    [Fact]
    public void IndexManager_CreatePartialIndex_DuplicateIndex_ThrowsInvalidOperationException()
    {
        // Arrange
        string collectionName = "users";
        string fieldName = "email";
        Func<Document, bool> filter = doc => doc.Data.TryGetValue("status", out var val) && val?.ToString() == "active";
        _indexManager.CreatePartialIndex<string>(collectionName, fieldName, false,
            doc => doc.Data.TryGetValue("email", out var val) ? val?.ToString() : null,
            filter);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            _indexManager.CreatePartialIndex<string>(collectionName, fieldName, false,
                doc => doc.Data.TryGetValue("email", out var val) ? val?.ToString() : null,
                filter));
    }

    [Fact]
    public void IndexManager_CreatePartialIndex_EmptyCollectionName_ThrowsArgumentException()
    {
        // Arrange
        Func<Document, bool> filter = doc => true;

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            _indexManager.CreatePartialIndex<string>("", "email", false, doc => "test", filter));
    }

    [Fact]
    public void IndexManager_CreatePartialIndex_EmptyFieldName_ThrowsArgumentException()
    {
        // Arrange
        Func<Document, bool> filter = doc => true;

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            _indexManager.CreatePartialIndex<string>("users", "", false, doc => "test", filter));
    }

    [Fact]
    public void IndexManager_CreatePartialIndex_NullKeySelector_ThrowsArgumentNullException()
    {
        // Arrange
        Func<Document, bool> filter = doc => true;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            _indexManager.CreatePartialIndex<string>("users", "email", false, null!, filter));
    }

    [Fact]
    public void IndexManager_CreatePartialIndex_NullFilterExpression_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            _indexManager.CreatePartialIndex<string>("users", "email", false, doc => "test", null!));
    }

    #endregion

    #region Partial Index Document Indexing Tests

    [Fact]
    public void PartialIndex_IndexDocument_DocumentMatchesFilter_DocumentIndexed()
    {
        // Arrange
        string collectionName = "users";
        string fieldName = "email";
        Func<Document, bool> filter = doc => doc.Data.TryGetValue("status", out var val) && val?.ToString() == "active";

        var index = _indexManager.CreatePartialIndex<string>(collectionName, fieldName, false,
            doc => doc.Data.TryGetValue("email", out var val) ? val?.ToString() : null,
            filter);

        var document = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object?>
            {
                ["email"] = "active@example.com",
                ["status"] = "active"
            }
        };

        // Act
        _indexManager.IndexDocument(collectionName, document);

        // Assert
        var results = index.GetValues("active@example.com").ToList();
        Assert.Single(results);
        Assert.Contains("doc1", results);
    }

    [Fact]
    public void PartialIndex_IndexDocument_DocumentDoesNotMatchFilter_DocumentNotIndexed()
    {
        // Arrange
        string collectionName = "users";
        string fieldName = "email";
        Func<Document, bool> filter = doc => doc.Data.TryGetValue("status", out var val) && val?.ToString() == "active";

        var index = _indexManager.CreatePartialIndex<string>(collectionName, fieldName, false,
            doc => doc.Data.TryGetValue("email", out var val) ? val?.ToString() : null,
            filter);

        var document = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object?>
            {
                ["email"] = "inactive@example.com",
                ["status"] = "inactive"
            }
        };

        // Act
        _indexManager.IndexDocument(collectionName, document);

        // Assert
        var results = index.GetValues("inactive@example.com").ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void PartialIndex_IndexDocument_MultipleDocumentsSomeMatching_OnlyMatchingIndexed()
    {
        // Arrange
        string collectionName = "users";
        string fieldName = "email";
        Func<Document, bool> filter = doc => doc.Data.TryGetValue("status", out var val) && val?.ToString() == "active";

        var index = _indexManager.CreatePartialIndex<string>(collectionName, fieldName, false,
            doc => doc.Data.TryGetValue("email", out var val) ? val?.ToString() : null,
            filter);

        var activeDoc1 = new Document { Id = "active1", Data = new Dictionary<string, object?> { ["email"] = "a1@test.com", ["status"] = "active" } };
        var activeDoc2 = new Document { Id = "active2", Data = new Dictionary<string, object?> { ["email"] = "a2@test.com", ["status"] = "active" } };
        var inactiveDoc1 = new Document { Id = "inactive1", Data = new Dictionary<string, object?> { ["email"] = "i1@test.com", ["status"] = "inactive" } };
        var inactiveDoc2 = new Document { Id = "inactive2", Data = new Dictionary<string, object?> { ["email"] = "i2@test.com", ["status"] = "inactive" } };

        // Act
        _indexManager.IndexDocument(collectionName, activeDoc1);
        _indexManager.IndexDocument(collectionName, inactiveDoc1);
        _indexManager.IndexDocument(collectionName, activeDoc2);
        _indexManager.IndexDocument(collectionName, inactiveDoc2);

        // Assert
        Assert.Equal(2, index.Count);
        Assert.Contains("active1", index.GetValues("a1@test.com"));
        Assert.Contains("active2", index.GetValues("a2@test.com"));
        Assert.Empty(index.GetValues("i1@test.com"));
        Assert.Empty(index.GetValues("i2@test.com"));
    }

    [Fact]
    public void PartialIndex_RemoveDocument_MatchingDocument_DocumentRemoved()
    {
        // Arrange
        string collectionName = "users";
        string fieldName = "email";
        Func<Document, bool> filter = doc => doc.Data.TryGetValue("status", out var val) && val?.ToString() == "active";

        var index = _indexManager.CreatePartialIndex<string>(collectionName, fieldName, false,
            doc => doc.Data.TryGetValue("email", out var val) ? val?.ToString() : null,
            filter);

        var document = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object?>
            {
                ["email"] = "test@example.com",
                ["status"] = "active"
            }
        };
        _indexManager.IndexDocument(collectionName, document);
        Assert.Equal(1, index.Count);

        // Act
        _indexManager.RemoveDocument(collectionName, document);

        // Assert
        Assert.Equal(0, index.Count);
        Assert.Empty(index.GetValues("test@example.com"));
    }

    [Fact]
    public void PartialIndex_UpdateDocument_FromMatchingToNonMatching_DocumentRemoved()
    {
        // Arrange
        string collectionName = "users";
        string fieldName = "email";
        Func<Document, bool> filter = doc => doc.Data.TryGetValue("status", out var val) && val?.ToString() == "active";

        var index = _indexManager.CreatePartialIndex<string>(collectionName, fieldName, false,
            doc => doc.Data.TryGetValue("email", out var val) ? val?.ToString() : null,
            filter);

        var oldDocument = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object?>
            {
                ["email"] = "test@example.com",
                ["status"] = "active"
            }
        };
        _indexManager.IndexDocument(collectionName, oldDocument);
        Assert.Equal(1, index.Count);

        var newDocument = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object?>
            {
                ["email"] = "test@example.com",
                ["status"] = "inactive"  // Changed from active to inactive
            }
        };

        // Act
        _indexManager.UpdateDocument(collectionName, oldDocument, newDocument);

        // Assert
        Assert.Equal(0, index.Count);
        Assert.Empty(index.GetValues("test@example.com"));
    }

    [Fact]
    public void PartialIndex_UpdateDocument_FromNonMatchingToMatching_DocumentAdded()
    {
        // Arrange
        string collectionName = "users";
        string fieldName = "email";
        Func<Document, bool> filter = doc => doc.Data.TryGetValue("status", out var val) && val?.ToString() == "active";

        var index = _indexManager.CreatePartialIndex<string>(collectionName, fieldName, false,
            doc => doc.Data.TryGetValue("email", out var val) ? val?.ToString() : null,
            filter);

        var oldDocument = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object?>
            {
                ["email"] = "test@example.com",
                ["status"] = "inactive"
            }
        };
        _indexManager.IndexDocument(collectionName, oldDocument);
        Assert.Equal(0, index.Count); // Not indexed because inactive

        var newDocument = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object?>
            {
                ["email"] = "test@example.com",
                ["status"] = "active"  // Changed from inactive to active
            }
        };

        // Act
        _indexManager.UpdateDocument(collectionName, oldDocument, newDocument);

        // Assert
        Assert.Equal(1, index.Count);
        Assert.Contains("doc1", index.GetValues("test@example.com"));
    }

    [Fact]
    public void PartialIndex_UpdateDocument_MatchingWithChangedKey_KeyUpdated()
    {
        // Arrange
        string collectionName = "users";
        string fieldName = "email";
        Func<Document, bool> filter = doc => doc.Data.TryGetValue("status", out var val) && val?.ToString() == "active";

        var index = _indexManager.CreatePartialIndex<string>(collectionName, fieldName, false,
            doc => doc.Data.TryGetValue("email", out var val) ? val?.ToString() : null,
            filter);

        var oldDocument = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object?>
            {
                ["email"] = "old@example.com",
                ["status"] = "active"
            }
        };
        _indexManager.IndexDocument(collectionName, oldDocument);
        Assert.Contains("doc1", index.GetValues("old@example.com"));

        var newDocument = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object?>
            {
                ["email"] = "new@example.com",  // Changed email
                ["status"] = "active"
            }
        };

        // Act
        _indexManager.UpdateDocument(collectionName, oldDocument, newDocument);

        // Assert
        Assert.Equal(1, index.Count);
        Assert.Empty(index.GetValues("old@example.com"));
        Assert.Contains("doc1", index.GetValues("new@example.com"));
    }

    #endregion

    #region Partial Index with Different Filter Types

    [Fact]
    public void PartialIndex_NumericFilter_GreaterThanCondition()
    {
        // Arrange - only index documents with age > 18
        string collectionName = "users";
        string fieldName = "name";
        Func<Document, bool> filter = doc =>
            doc.Data.TryGetValue("age", out var val) &&
            val is int age &&
            age > 18;

        var index = _indexManager.CreatePartialIndex<string>(collectionName, fieldName, false,
            doc => doc.Data.TryGetValue("name", out var val) ? val?.ToString() : null,
            filter);

        var adult = new Document { Id = "adult", Data = new Dictionary<string, object?> { ["name"] = "John", ["age"] = 25 } };
        var teen = new Document { Id = "teen", Data = new Dictionary<string, object?> { ["name"] = "Jane", ["age"] = 16 } };

        // Act
        _indexManager.IndexDocument(collectionName, adult);
        _indexManager.IndexDocument(collectionName, teen);

        // Assert
        Assert.Equal(1, index.Count);
        Assert.Contains("adult", index.GetValues("John"));
        Assert.Empty(index.GetValues("Jane"));
    }

    [Fact]
    public void PartialIndex_BooleanFilter_OnlyActiveDocuments()
    {
        // Arrange - only index documents where isActive is true
        string collectionName = "users";
        string fieldName = "username";
        Func<Document, bool> filter = doc =>
            doc.Data.TryGetValue("isActive", out var val) &&
            val is bool isActive &&
            isActive;

        var index = _indexManager.CreatePartialIndex<string>(collectionName, fieldName, false,
            doc => doc.Data.TryGetValue("username", out var val) ? val?.ToString() : null,
            filter);

        var activeUser = new Document { Id = "user1", Data = new Dictionary<string, object?> { ["username"] = "activeUser", ["isActive"] = true } };
        var inactiveUser = new Document { Id = "user2", Data = new Dictionary<string, object?> { ["username"] = "inactiveUser", ["isActive"] = false } };

        // Act
        _indexManager.IndexDocument(collectionName, activeUser);
        _indexManager.IndexDocument(collectionName, inactiveUser);

        // Assert
        Assert.Equal(1, index.Count);
        Assert.Contains("user1", index.GetValues("activeUser"));
        Assert.Empty(index.GetValues("inactiveUser"));
    }

    [Fact]
    public void PartialIndex_ComplexFilter_MultipleConditions()
    {
        // Arrange - only index documents where status=active AND type=premium
        string collectionName = "users";
        string fieldName = "email";
        Func<Document, bool> filter = doc =>
            doc.Data.TryGetValue("status", out var status) && status?.ToString() == "active" &&
            doc.Data.TryGetValue("type", out var type) && type?.ToString() == "premium";

        var index = _indexManager.CreatePartialIndex<string>(collectionName, fieldName, false,
            doc => doc.Data.TryGetValue("email", out var val) ? val?.ToString() : null,
            filter);

        var premiumActive = new Document { Id = "1", Data = new Dictionary<string, object?> { ["email"] = "pa@test.com", ["status"] = "active", ["type"] = "premium" } };
        var regularActive = new Document { Id = "2", Data = new Dictionary<string, object?> { ["email"] = "ra@test.com", ["status"] = "active", ["type"] = "regular" } };
        var premiumInactive = new Document { Id = "3", Data = new Dictionary<string, object?> { ["email"] = "pi@test.com", ["status"] = "inactive", ["type"] = "premium" } };

        // Act
        _indexManager.IndexDocument(collectionName, premiumActive);
        _indexManager.IndexDocument(collectionName, regularActive);
        _indexManager.IndexDocument(collectionName, premiumInactive);

        // Assert
        Assert.Equal(1, index.Count);
        Assert.Contains("1", index.GetValues("pa@test.com"));
        Assert.Empty(index.GetValues("ra@test.com"));
        Assert.Empty(index.GetValues("pi@test.com"));
    }

    [Fact]
    public void PartialIndex_ExistsFilter_FieldExistsCondition()
    {
        // Arrange - only index documents that have a "verifiedAt" field
        string collectionName = "users";
        string fieldName = "email";
        Func<Document, bool> filter = doc => doc.Data.ContainsKey("verifiedAt");

        var index = _indexManager.CreatePartialIndex<string>(collectionName, fieldName, false,
            doc => doc.Data.TryGetValue("email", out var val) ? val?.ToString() : null,
            filter);

        var verifiedUser = new Document { Id = "1", Data = new Dictionary<string, object?> { ["email"] = "verified@test.com", ["verifiedAt"] = DateTime.UtcNow } };
        var unverifiedUser = new Document { Id = "2", Data = new Dictionary<string, object?> { ["email"] = "unverified@test.com" } };

        // Act
        _indexManager.IndexDocument(collectionName, verifiedUser);
        _indexManager.IndexDocument(collectionName, unverifiedUser);

        // Assert
        Assert.Equal(1, index.Count);
        Assert.Contains("1", index.GetValues("verified@test.com"));
        Assert.Empty(index.GetValues("unverified@test.com"));
    }

    #endregion

    #region Partial Index Stats Tests

    [Fact]
    public void PartialIndex_GetStats_ReturnsCorrectType()
    {
        // Arrange
        string collectionName = "users";
        string fieldName = "email";
        Func<Document, bool> filter = doc => doc.Data.TryGetValue("status", out var val) && val?.ToString() == "active";

        _indexManager.CreatePartialIndex<string>(collectionName, fieldName, false,
            doc => doc.Data.TryGetValue("email", out var val) ? val?.ToString() : null,
            filter);

        // Act
        var stats = _indexManager.GetIndexStats(collectionName).First();

        // Assert
        Assert.Equal("email", stats.FieldName);
        Assert.Contains("Partial", stats.IndexType);
    }

    [Fact]
    public void PartialIndex_GetStats_Unique_ReturnsUniquePartialType()
    {
        // Arrange
        string collectionName = "users";
        string fieldName = "email";
        Func<Document, bool> filter = doc => doc.Data.TryGetValue("status", out var val) && val?.ToString() == "active";

        _indexManager.CreatePartialIndex<string>(collectionName, fieldName, true,
            doc => doc.Data.TryGetValue("email", out var val) ? val?.ToString() : null,
            filter);

        // Act
        var stats = _indexManager.GetIndexStats(collectionName).First();

        // Assert
        Assert.Contains("Unique", stats.IndexType);
        Assert.Contains("Partial", stats.IndexType);
    }

    [Fact]
    public void PartialIndex_GetStats_CountReflectsOnlyMatchingDocuments()
    {
        // Arrange
        string collectionName = "users";
        string fieldName = "email";
        Func<Document, bool> filter = doc => doc.Data.TryGetValue("status", out var val) && val?.ToString() == "active";

        _indexManager.CreatePartialIndex<string>(collectionName, fieldName, false,
            doc => doc.Data.TryGetValue("email", out var val) ? val?.ToString() : null,
            filter);

        var activeDoc = new Document { Id = "1", Data = new Dictionary<string, object?> { ["email"] = "active@test.com", ["status"] = "active" } };
        var inactiveDoc = new Document { Id = "2", Data = new Dictionary<string, object?> { ["email"] = "inactive@test.com", ["status"] = "inactive" } };

        _indexManager.IndexDocument(collectionName, activeDoc);
        _indexManager.IndexDocument(collectionName, inactiveDoc);

        // Act
        var stats = _indexManager.GetIndexStats(collectionName).First();

        // Assert
        Assert.Equal(1, stats.EntryCount);
    }

    #endregion

    #region Partial Index Unique Constraint Tests

    [Fact]
    public void PartialIndex_Unique_DuplicateKeyAmongMatchingDocuments_ThrowsDuplicateKeyException()
    {
        // Arrange
        string collectionName = "users";
        string fieldName = "code";
        Func<Document, bool> filter = doc => doc.Data.TryGetValue("status", out var val) && val?.ToString() == "active";

        var index = _indexManager.CreatePartialIndex<string>(collectionName, fieldName, true,
            doc => doc.Data.TryGetValue("code", out var val) ? val?.ToString() : null,
            filter);

        var doc1 = new Document { Id = "1", Data = new Dictionary<string, object?> { ["code"] = "ABC", ["status"] = "active" } };
        var doc2 = new Document { Id = "2", Data = new Dictionary<string, object?> { ["code"] = "ABC", ["status"] = "active" } }; // Same code

        _indexManager.IndexDocument(collectionName, doc1);

        // Act & Assert
        Assert.Throws<DuplicateKeyException>(() => _indexManager.IndexDocument(collectionName, doc2));
    }

    [Fact]
    public void PartialIndex_Unique_DuplicateKeyButOneDoesNotMatchFilter_NoException()
    {
        // Arrange
        string collectionName = "users";
        string fieldName = "code";
        Func<Document, bool> filter = doc => doc.Data.TryGetValue("status", out var val) && val?.ToString() == "active";

        var index = _indexManager.CreatePartialIndex<string>(collectionName, fieldName, true,
            doc => doc.Data.TryGetValue("code", out var val) ? val?.ToString() : null,
            filter);

        var activeDoc = new Document { Id = "1", Data = new Dictionary<string, object?> { ["code"] = "ABC", ["status"] = "active" } };
        var inactiveDoc = new Document { Id = "2", Data = new Dictionary<string, object?> { ["code"] = "ABC", ["status"] = "inactive" } }; // Same code but inactive

        // Act & Assert - should not throw because inactive doc won't be indexed
        _indexManager.IndexDocument(collectionName, activeDoc);
        _indexManager.IndexDocument(collectionName, inactiveDoc); // No exception

        // Verify only active is indexed
        Assert.Equal(1, index.Count);
    }

    #endregion

    #region Partial Index Integration with GetPartialIndex

    [Fact]
    public void PartialIndex_GetPartialIndex_ReturnsCorrectIndex()
    {
        // Arrange
        string collectionName = "users";
        string fieldName = "email";
        Func<Document, bool> filter = doc => doc.Data.TryGetValue("status", out var val) && val?.ToString() == "active";

        _indexManager.CreatePartialIndex<string>(collectionName, fieldName, false,
            doc => doc.Data.TryGetValue("email", out var val) ? val?.ToString() : null,
            filter);

        // Act
        var retrievedIndex = _indexManager.GetPartialIndex<string>(collectionName, fieldName);

        // Assert
        Assert.NotNull(retrievedIndex);
    }

    [Fact]
    public void PartialIndex_GetPartialIndex_NonExistent_ReturnsNull()
    {
        // Act
        var retrievedIndex = _indexManager.GetPartialIndex<string>("nonexistent", "field");

        // Assert
        Assert.Null(retrievedIndex);
    }

    #endregion
}
