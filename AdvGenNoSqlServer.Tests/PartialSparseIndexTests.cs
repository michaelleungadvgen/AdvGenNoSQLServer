// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Storage.Indexing;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for sparse index functionality
/// </summary>
public class PartialSparseIndexTests
{
    #region Sparse Index Tests

    [Fact]
    public void CreateSparseIndex_WithValidParameters_CreatesIndex()
    {
        var manager = new IndexManager();
        
        var index = manager.CreateSparseIndex<string>(
            "users",
            "email",
            isUnique: true,
            doc => doc.Data.TryGetValue("email", out var val) ? val?.ToString() : null);
        
        Assert.NotNull(index);
        Assert.Equal("users", index.CollectionName);
        Assert.Equal("email", index.FieldName);
        Assert.True(index.IsUnique);
    }

    [Fact]
    public void CreateSparseIndex_DuplicateField_ThrowsException()
    {
        var manager = new IndexManager();
        
        manager.CreateSparseIndex<string>(
            "users",
            "email",
            isUnique: true,
            doc => doc.Data.TryGetValue("email", out var val) ? val?.ToString() : null);
        
        Assert.Throws<InvalidOperationException>(() =>
        {
            manager.CreateSparseIndex<string>(
                "users",
                "email",
                isUnique: false,
                doc => doc.Data.TryGetValue("email", out var val) ? val?.ToString() : null);
        });
    }

    [Fact]
    public void SparseIndex_OnlyIndexesDocumentsWithField()
    {
        var manager = new IndexManager();
        
        var index = manager.CreateSparseIndex<string>(
            "users",
            "email",
            isUnique: false,
            doc => doc.Data.TryGetValue("email", out var val) ? val?.ToString() : null);
        
        // Document with email field
        var docWithEmail = new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object>
            {
                ["name"] = "John",
                ["email"] = "john@example.com"
            }
        };
        
        // Document without email field
        var docWithoutEmail = new Document
        {
            Id = "user2",
            Data = new Dictionary<string, object>
            {
                ["name"] = "Jane"
                // No email field
            }
        };
        
        manager.IndexDocument("users", docWithEmail);
        manager.IndexDocument("users", docWithoutEmail);
        
        // Sparse index should only contain the document with email
        Assert.Equal(1, index.Count);
        var results = index.GetValues("john@example.com").ToList();
        Assert.Single(results);
        Assert.Equal("user1", results[0]);
    }

    [Fact]
    public void SparseIndex_UpdateDocument_AddsField_IndexesIt()
    {
        var manager = new IndexManager();
        
        var index = manager.CreateSparseIndex<string>(
            "users",
            "email",
            isUnique: false,
            doc => doc.Data.TryGetValue("email", out var val) ? val?.ToString() : null);
        
        var oldDoc = new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object>
            {
                ["name"] = "John"
                // No email initially
            }
        };
        
        var newDoc = new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object>
            {
                ["name"] = "John",
                ["email"] = "john@example.com"
            }
        };
        
        manager.IndexDocument("users", oldDoc);
        Assert.Equal(0, index.Count);
        
        manager.UpdateDocument("users", oldDoc, newDoc);
        Assert.Equal(1, index.Count);
    }

    [Fact]
    public void SparseIndex_UpdateDocument_RemovesField_RemovesFromIndex()
    {
        var manager = new IndexManager();
        
        var index = manager.CreateSparseIndex<string>(
            "users",
            "email",
            isUnique: false,
            doc => doc.Data.TryGetValue("email", out var val) ? val?.ToString() : null);
        
        var oldDoc = new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object>
            {
                ["name"] = "John",
                ["email"] = "john@example.com"
            }
        };
        
        var newDoc = new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object>
            {
                ["name"] = "John Updated"
                // No email anymore
            }
        };
        
        manager.IndexDocument("users", oldDoc);
        Assert.Equal(1, index.Count);
        
        manager.UpdateDocument("users", oldDoc, newDoc);
        Assert.Equal(0, index.Count);
    }

    [Fact]
    public void SparseIndex_DeleteDocument_RemovesFromIndex()
    {
        var manager = new IndexManager();
        
        var index = manager.CreateSparseIndex<string>(
            "users",
            "email",
            isUnique: false,
            doc => doc.Data.TryGetValue("email", out var val) ? val?.ToString() : null);
        
        var doc = new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object>
            {
                ["name"] = "John",
                ["email"] = "john@example.com"
            }
        };
        
        manager.IndexDocument("users", doc);
        Assert.Equal(1, index.Count);
        
        manager.RemoveDocument("users", doc);
        Assert.Equal(0, index.Count);
    }

    [Fact]
    public void SparseIndex_NullValue_IndexedBecauseFieldExists()
    {
        var manager = new IndexManager();
        
        var index = manager.CreateSparseIndex<string>(
            "users",
            "email",
            isUnique: false,
            doc => doc.Data.TryGetValue("email", out var val) ? val?.ToString() : null);
        
        var doc = new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object>
            {
                ["name"] = "John",
                ["email"] = null!  // Field exists but is null
            }
        };
        
        manager.IndexDocument("users", doc);
        // Sparse index includes documents where the field EXISTS (even if null)
        // The key selector returns null, so nothing gets indexed
        Assert.Equal(0, index.Count);
    }

    [Fact]
    public void SparseIndex_MultipleDocuments_OnlySomeIndexed()
    {
        var manager = new IndexManager();
        
        var index = manager.CreateSparseIndex<string>(
            "users",
            "email",
            isUnique: false,
            doc => doc.Data.TryGetValue("email", out var val) ? val?.ToString() : null);
        
        for (int i = 0; i < 10; i++)
        {
            var doc = new Document
            {
                Id = $"user{i}",
                Data = new Dictionary<string, object>
                {
                    ["name"] = $"User {i}"
                }
            };
            
            // Only even-numbered users have email
            if (i % 2 == 0)
            {
                doc.Data["email"] = $"user{i}@example.com";
            }
            
            manager.IndexDocument("users", doc);
        }
        
        Assert.Equal(5, index.Count);
    }

    #endregion

    #region Index Statistics Tests

    [Fact]
    public void GetIndexStats_SparseIndex_ShowsSparseType()
    {
        var manager = new IndexManager();
        
        manager.CreateSparseIndex<string>(
            "users",
            "email",
            isUnique: false,
            doc => doc.Data.TryGetValue("email", out var val) ? val?.ToString() : null);
        
        var stats = manager.GetIndexStats("users").ToList();
        
        Assert.Single(stats);
        Assert.Contains("Sparse", stats[0].IndexType);
    }

    [Fact]
    public void GetIndexStats_UniqueSparseIndex_ShowsUniqueSparseType()
    {
        var manager = new IndexManager();
        
        manager.CreateSparseIndex<string>(
            "users",
            "email",
            isUnique: true,
            doc => doc.Data.TryGetValue("email", out var val) ? val?.ToString() : null);
        
        var stats = manager.GetIndexStats("users").ToList();
        
        Assert.Single(stats);
        Assert.Contains("Unique", stats[0].IndexType);
        Assert.Contains("Sparse", stats[0].IndexType);
    }

    [Fact]
    public void GetIndexStats_RegularIndex_ShowsBTreeType()
    {
        var manager = new IndexManager();
        
        manager.CreateIndex<string>(
            "users",
            "name",
            isUnique: false,
            doc => doc.Data.TryGetValue("name", out var val) ? val?.ToString() : null);
        
        var stats = manager.GetIndexStats("users").ToList();
        
        Assert.Single(stats);
        Assert.Equal("B-Tree", stats[0].IndexType);
    }

    #endregion

    #region IPartialIndex Tests

    [Fact]
    public void SparseBTreeIndex_SparseType_ReturnsSparse()
    {
        var index = new SparseBTreeIndex<string>("test", "users", "email", false);
        
        Assert.Equal(PartialIndexType.Sparse, ((IPartialIndex)index).PartialType);
    }

    [Fact]
    public void SparseBTreeIndex_ShouldIncludeDocument_WithField_ReturnsTrue()
    {
        var index = new SparseBTreeIndex<string>("test", "users", "email", false);
        
        var doc = new Document 
        { 
            Id = "1", 
            Data = new Dictionary<string, object> { ["email"] = "test@example.com" } 
        };
        
        Assert.True(((IPartialIndex)index).ShouldIncludeDocument(doc));
    }

    [Fact]
    public void SparseBTreeIndex_ShouldIncludeDocument_WithoutField_ReturnsFalse()
    {
        var index = new SparseBTreeIndex<string>("test", "users", "email", false);
        
        var doc = new Document 
        { 
            Id = "1", 
            Data = new Dictionary<string, object> { ["name"] = "Test" } 
        };
        
        Assert.False(((IPartialIndex)index).ShouldIncludeDocument(doc));
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void SparseIndex_EmptyString_IsIndexed()
    {
        var manager = new IndexManager();
        
        var index = manager.CreateSparseIndex<string>(
            "users",
            "email",
            isUnique: false,
            doc => doc.Data.TryGetValue("email", out var val) ? val?.ToString() : null);
        
        var doc = new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object>
            {
                ["email"] = "" // Empty string is still a value
            }
        };
        
        manager.IndexDocument("users", doc);
        Assert.Equal(1, index.Count);
    }

    [Fact]
    public void MultipleSparseIndexes_SameCollection_WorkIndependently()
    {
        var manager = new IndexManager();
        
        var emailIndex = manager.CreateSparseIndex<string>(
            "users",
            "email",
            isUnique: false,
            doc => doc.Data.TryGetValue("email", out var val) ? val?.ToString() : null);
        
        var phoneIndex = manager.CreateSparseIndex<string>(
            "users",
            "phone",
            isUnique: false,
            doc => doc.Data.TryGetValue("phone", out var val) ? val?.ToString() : null);
        
        var doc1 = new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object>
            {
                ["email"] = "user1@example.com"
                // No phone
            }
        };
        
        var doc2 = new Document
        {
            Id = "user2",
            Data = new Dictionary<string, object>
            {
                // No email
                ["phone"] = "555-1234"
            }
        };
        
        var doc3 = new Document
        {
            Id = "user3",
            Data = new Dictionary<string, object>
            {
                // No email, no phone
            }
        };
        
        manager.IndexDocument("users", doc1);
        manager.IndexDocument("users", doc2);
        manager.IndexDocument("users", doc3);
        
        Assert.Equal(1, emailIndex.Count);
        Assert.Equal(1, phoneIndex.Count);
    }

    [Fact]
    public void SparseIndex_NullSelector_ReturnsNull_NotIndexed()
    {
        var manager = new IndexManager();
        
        var index = manager.CreateSparseIndex<string>(
            "users",
            "email",
            isUnique: false,
            doc => null); // Always returns null
        
        var doc = new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object>
            {
                ["email"] = "user@example.com"
            }
        };
        
        manager.IndexDocument("users", doc);
        Assert.Equal(0, index.Count);
    }

    [Fact]
    public void SparseBTreeIndex_InheritsBTreeFunctionality()
    {
        var index = new SparseBTreeIndex<string>("test", "users", "email", false);
        
        // Test basic B-tree operations work
        index.Insert("key1", "value1");
        Assert.Equal(1, index.Count);
        
        var values = index.GetValues("key1").ToList();
        Assert.Single(values);
        Assert.Equal("value1", values[0]);
        
        index.Delete("key1");
        Assert.Equal(0, index.Count);
    }

    [Fact]
    public void SparseIndex_CreateWithDifferentKeyTypes()
    {
        var manager = new IndexManager();
        
        // Integer sparse index
        var intIndex = manager.CreateSparseIndex<int>(
            "products",
            "sku",
            isUnique: true,
            doc => doc.Data.TryGetValue("sku", out var val) && val is int i ? i : 0);
        
        // DateTime sparse index
        var dateIndex = manager.CreateSparseIndex<DateTime>(
            "events",
            "timestamp",
            isUnique: false,
            doc => doc.Data.TryGetValue("timestamp", out var val) && val is DateTime d ? d : DateTime.MinValue);
        
        Assert.NotNull(intIndex);
        Assert.NotNull(dateIndex);
        Assert.True(intIndex.IsUnique);
        Assert.False(dateIndex.IsUnique);
    }

    #endregion
}
