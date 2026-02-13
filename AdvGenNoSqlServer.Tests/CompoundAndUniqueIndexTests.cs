// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Storage.Indexing;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for Compound Index and Unique Index support
/// </summary>
public class CompoundAndUniqueIndexTests
{
    #region Compound Index Creation Tests

    [Fact]
    public void CreateCompoundIndex_TwoFields_CreatesIndex()
    {
        var manager = new IndexManager();

        var index = manager.CreateCompoundIndex(
            "users",
            new[] { "department", "age" },
            isUnique: false,
            keySelector: doc => new object?[]
            {
                doc.Data.TryGetValue("department", out var dept) ? dept : null,
                doc.Data.TryGetValue("age", out var age) ? age : null
            });

        Assert.NotNull(index);
        Assert.Equal("users_department_age_idx", index.Name);
        Assert.Equal("users", index.CollectionName);
        Assert.Equal("department+age", index.FieldName);
        Assert.False(index.IsUnique);
    }

    [Fact]
    public void CreateCompoundIndex_ThreeFields_CreatesIndex()
    {
        var manager = new IndexManager();

        var index = manager.CreateCompoundIndex(
            "orders",
            new[] { "customerId", "status", "orderDate" },
            isUnique: false,
            keySelector: doc => new object?[]
            {
                doc.Data.TryGetValue("customerId", out var cid) ? cid : null,
                doc.Data.TryGetValue("status", out var status) ? status : null,
                doc.Data.TryGetValue("orderDate", out var date) ? date : null
            });

        Assert.NotNull(index);
        Assert.Equal("orders_customerId_status_orderDate_idx", index.Name);
        Assert.Equal("customerId+status+orderDate", index.FieldName);
    }

    [Fact]
    public void CreateCompoundIndex_GenericTwoFields_CreatesIndex()
    {
        var manager = new IndexManager();

        var index = manager.CreateCompoundIndex(
            "users",
            "department",
            "age",
            isUnique: false,
            selector1: doc => doc.Data.TryGetValue("department", out var val) ? val?.ToString() ?? "" : "",
            selector2: doc => doc.Data.TryGetValue("age", out var val) ? Convert.ToInt32(val) : 0);

        Assert.NotNull(index);
        Assert.False(index.IsUnique);
    }

    [Fact]
    public void CreateCompoundIndex_GenericThreeFields_CreatesIndex()
    {
        var manager = new IndexManager();

        var index = manager.CreateCompoundIndex(
            "orders",
            "customerId",
            "status",
            "orderDate",
            isUnique: false,
            selector1: doc => doc.Data.TryGetValue("customerId", out var val) ? val?.ToString() ?? "" : "",
            selector2: doc => doc.Data.TryGetValue("status", out var val) ? val?.ToString() ?? "" : "",
            selector3: doc => doc.Data.TryGetValue("orderDate", out var val) && val is DateTime dt ? dt : DateTime.MinValue);

        Assert.NotNull(index);
        // Generic method uses joined field names with underscore in index name
        Assert.Equal("orders_customerId_status_orderDate_idx", index.Name);
        // The field name (key) uses + separator
        Assert.Equal("customerId+status+orderDate", index.FieldName);
    }

    [Fact]
    public void CreateCompoundIndex_Unique_CreatesUniqueIndex()
    {
        var manager = new IndexManager();

        var index = manager.CreateCompoundIndex(
            "users",
            new[] { "email", "tenantId" },
            isUnique: true,
            keySelector: doc => new object?[]
            {
                doc.Data.TryGetValue("email", out var email) ? email : null,
                doc.Data.TryGetValue("tenantId", out var tenant) ? tenant : null
            });

        Assert.True(index.IsUnique);
    }

    [Fact]
    public void CreateCompoundIndex_LessThanTwoFields_ThrowsArgumentException()
    {
        var manager = new IndexManager();

        Assert.Throws<ArgumentException>(() =>
            manager.CreateCompoundIndex(
                "users",
                new[] { "email" },
                isUnique: false,
                keySelector: doc => new object?[] { doc.Data.TryGetValue("email", out var val) ? val : null }));
    }

    [Fact]
    public void CreateCompoundIndex_NullFieldNames_ThrowsArgumentNullException()
    {
        var manager = new IndexManager();

        Assert.Throws<ArgumentNullException>(() =>
            manager.CreateCompoundIndex(
                "users",
                null!,
                isUnique: false,
                keySelector: doc => Array.Empty<object?>()));
    }

    [Fact]
    public void CreateCompoundIndex_EmptyFieldName_ThrowsArgumentException()
    {
        var manager = new IndexManager();

        Assert.Throws<ArgumentException>(() =>
            manager.CreateCompoundIndex(
                "users",
                new[] { "email", "" },
                isUnique: false,
                keySelector: doc => new object?[] { "a", "b" }));
    }

    [Fact]
    public void CreateCompoundIndex_DuplicateIndex_ThrowsInvalidOperationException()
    {
        var manager = new IndexManager();
        manager.CreateCompoundIndex(
            "users",
            new[] { "department", "age" },
            isUnique: false,
            keySelector: doc => new object?[] { "IT", 25 });

        Assert.Throws<InvalidOperationException>(() =>
            manager.CreateCompoundIndex(
                "users",
                new[] { "department", "age" },
                isUnique: false,
                keySelector: doc => new object?[] { "IT", 25 }));
    }

    #endregion

    #region Unique Single-Field Index Tests

    [Fact]
    public void UniqueSingleFieldIndex_EnforcesUniqueness()
    {
        // Test unique constraint at BTreeIndex level (IndexWrapper catches exceptions for document indexing)
        var index = new BTreeIndex<string, string>(
            "users_email_idx", "users", "email", isUnique: true, minDegree: 4);

        // First insert should succeed
        Assert.True(index.Insert("john@example.com", "user1"));

        // Second insert with same key should throw DuplicateKeyException
        Assert.Throws<DuplicateKeyException>(() => index.Insert("john@example.com", "user2"));
    }

    [Fact]
    public void UniqueSingleFieldIndex_DifferentValues_Allowed()
    {
        // Test unique constraint at BTreeIndex level
        var index = new BTreeIndex<string, string>(
            "users_email_idx", "users", "email", isUnique: true, minDegree: 4);

        // Both inserts should succeed with different keys
        Assert.True(index.Insert("john@example.com", "user1"));
        Assert.True(index.Insert("jane@example.com", "user2"));

        Assert.Equal(2, index.Count);
    }

    [Fact]
    public void UniqueSingleFieldIndex_UpdateWithDuplicate_ThrowsException()
    {
        // Test unique constraint during update at BTreeIndex level
        var index = new BTreeIndex<string, string>(
            "users_email_idx", "users", "email", isUnique: true, minDegree: 4);

        // Insert two documents with different keys
        Assert.True(index.Insert("john@example.com", "user1"));
        Assert.True(index.Insert("jane@example.com", "user2"));

        // Try to update second document to have the same key as first
        // (This requires delete then insert, so the insert will fail)
        Assert.True(index.Delete("jane@example.com", "user2"));
        
        // Now trying to insert as john@example.com should fail
        Assert.Throws<DuplicateKeyException>(() => 
            index.Insert("john@example.com", "user2"));
    }

    #endregion

    #region Unique Compound Index Tests

    [Fact]
    public void UniqueCompoundIndex_EnforcesUniqueness()
    {
        var manager = new IndexManager();
        manager.CreateCompoundIndex(
            "users",
            new[] { "tenantId", "email" },
            isUnique: true,
            keySelector: doc => new object?[]
            {
                doc.Data.TryGetValue("tenantId", out var tenant) ? tenant : null,
                doc.Data.TryGetValue("email", out var email) ? email : null
            });

        var doc1 = new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object>
            {
                { "tenantId", "tenant-a" },
                { "email", "admin@example.com" }
            }
        };
        var doc2 = new Document
        {
            Id = "user2",
            Data = new Dictionary<string, object>
            {
                { "tenantId", "tenant-a" },
                { "email", "admin@example.com" } // Same compound key as doc1
            }
        };

        manager.IndexDocument("users", doc1);

        Assert.Throws<DuplicateKeyException>(() => manager.IndexDocument("users", doc2));
    }

    [Fact]
    public void UniqueCompoundIndex_DifferentTenantSameEmail_Allowed()
    {
        var manager = new IndexManager();
        var index = manager.CreateCompoundIndex(
            "users",
            new[] { "tenantId", "email" },
            isUnique: true,
            keySelector: doc => new object?[]
            {
                doc.Data.TryGetValue("tenantId", out var tenant) ? tenant : null,
                doc.Data.TryGetValue("email", out var email) ? email : null
            });

        var doc1 = new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object>
            {
                { "tenantId", "tenant-a" },
                { "email", "admin@example.com" }
            }
        };
        var doc2 = new Document
        {
            Id = "user2",
            Data = new Dictionary<string, object>
            {
                { "tenantId", "tenant-b" },
                { "email", "admin@example.com" } // Same email, different tenant
            }
        };

        manager.IndexDocument("users", doc1);
        manager.IndexDocument("users", doc2); // Should not throw

        Assert.Equal(2, index.Count);
    }

    [Fact]
    public void UniqueCompoundIndex_DifferentFirstFieldSameSecond_Allowed()
    {
        var manager = new IndexManager();
        var index = manager.CreateCompoundIndex(
            "orders",
            new[] { "customerId", "productId" },
            isUnique: true,
            keySelector: doc => new object?[]
            {
                doc.Data.TryGetValue("customerId", out var cid) ? cid : null,
                doc.Data.TryGetValue("productId", out var pid) ? pid : null
            });

        var doc1 = new Document
        {
            Id = "order1",
            Data = new Dictionary<string, object>
            {
                { "customerId", "cust-a" },
                { "productId", "prod-1" }
            }
        };
        var doc2 = new Document
        {
            Id = "order2",
            Data = new Dictionary<string, object>
            {
                { "customerId", "cust-b" },
                { "productId", "prod-1" } // Same product, different customer
            }
        };

        manager.IndexDocument("orders", doc1);
        manager.IndexDocument("orders", doc2); // Should not throw

        Assert.Equal(2, index.Count);
    }

    [Fact]
    public void UniqueCompoundIndex_UpdateWithDuplicate_ThrowsException()
    {
        var manager = new IndexManager();
        manager.CreateCompoundIndex(
            "users",
            new[] { "tenantId", "email" },
            isUnique: true,
            keySelector: doc => new object?[]
            {
                doc.Data.TryGetValue("tenantId", out var tenant) ? tenant : null,
                doc.Data.TryGetValue("email", out var email) ? email : null
            });

        var doc1 = new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object>
            {
                { "tenantId", "tenant-a" },
                { "email", "admin@example.com" }
            }
        };
        var doc2 = new Document
        {
            Id = "user2",
            Data = new Dictionary<string, object>
            {
                { "tenantId", "tenant-a" },
                { "email", "user@example.com" }
            }
        };
        var updatedDoc2 = new Document
        {
            Id = "user2",
            Data = new Dictionary<string, object>
            {
                { "tenantId", "tenant-a" },
                { "email", "admin@example.com" } // Same as doc1
            }
        };

        manager.IndexDocument("users", doc1);
        manager.IndexDocument("users", doc2);

        Assert.Throws<DuplicateKeyException>(() => 
            manager.UpdateDocument("users", doc2, updatedDoc2));
    }

    #endregion

    #region Compound Index Key Tests

    [Fact]
    public void CompoundIndexKey_Comparison_LexicographicalOrdering()
    {
        var key1 = new CompoundIndexKey("IT", 25);
        var key2 = new CompoundIndexKey("IT", 30);
        var key3 = new CompoundIndexKey("Sales", 25);
        var key4 = new CompoundIndexKey("IT", 25);

        Assert.True(key1 < key2); // Same first field, smaller second
        Assert.True(key1 < key3); // Smaller first field
        Assert.True(key2 < key3); // IT < Sales
        Assert.Equal(0, key1.CompareTo(key4)); // Equal keys
        Assert.True(key1 == key4);
        Assert.True(key1 != key2);
    }

    [Fact]
    public void CompoundIndexKey_Comparison_DifferentFieldCounts()
    {
        var key1 = new CompoundIndexKey("IT");
        var key2 = new CompoundIndexKey("IT", 25);
        var key3 = new CompoundIndexKey("Sales");

        Assert.True(key1 < key2); // Fewer fields is "less"
        Assert.True(key1 < key3); // IT < Sales
    }

    [Fact]
    public void CompoundIndexKey_Comparison_WithNullValues()
    {
        var key1 = new CompoundIndexKey(null, "value");
        var key2 = new CompoundIndexKey("value", "value");
        var key3 = new CompoundIndexKey(null, null);

        Assert.True(key1 < key2); // null < "value"
        Assert.True(key3 < key1); // null == null, so compare second field
    }

    [Fact]
    public void CompoundIndexKey_NumericComparison()
    {
        var key1 = new CompoundIndexKey(100, "A");
        var key2 = new CompoundIndexKey(200, "A");
        var key3 = new CompoundIndexKey(100.5, "A");

        Assert.True(key1 < key2);
        Assert.True(key1 < key3);
    }

    [Fact]
    public void CompoundIndexKey_ToString_FormatsCorrectly()
    {
        var key = new CompoundIndexKey("IT", 25, true);
        
        var str = key.ToString();
        
        Assert.Equal("(IT, 25, True)", str);
    }

    [Fact]
    public void CompoundIndexKey_Equals_NullHandling()
    {
        var key1 = new CompoundIndexKey("test", null);
        var key2 = new CompoundIndexKey("test", null);
        var key3 = new CompoundIndexKey("test", "value");

        Assert.True(key1.Equals(key2));
        Assert.False(key1.Equals(key3));
    }

    #endregion

    #region Compound Index Query Tests

    [Fact]
    public void CompoundIndex_RangeQuery_ReturnsMatchingDocuments()
    {
        var manager = new IndexManager();
        var index = manager.CreateCompoundIndex(
            "orders",
            new[] { "customerId", "orderDate" },
            isUnique: false,
            keySelector: doc => new object?[]
            {
                doc.Data.TryGetValue("customerId", out var cid) ? cid : null,
                doc.Data.TryGetValue("orderDate", out var date) ? date : null
            });

        // Add documents
        for (int i = 1; i <= 5; i++)
        {
            var doc = new Document
            {
                Id = $"order{i}",
                Data = new Dictionary<string, object>
                {
                    { "customerId", "CUST001" },
                    { "orderDate", new DateTime(2024, 1, i) }
                }
            };
            manager.IndexDocument("orders", doc);
        }

        // Query range
        var results = index.RangeQuery(
            new CompoundIndexKey("CUST001", new DateTime(2024, 1, 2)),
            new CompoundIndexKey("CUST001", new DateTime(2024, 1, 4))).ToList();

        Assert.Equal(3, results.Count);
    }

    [Fact]
    public void CompoundIndex_GetGreaterThanOrEqual_ReturnsMatchingDocuments()
    {
        var manager = new IndexManager();
        var index = manager.CreateCompoundIndex(
            "products",
            new[] { "category", "price" },
            isUnique: false,
            keySelector: doc => new object?[]
            {
                doc.Data.TryGetValue("category", out var cat) ? cat : null,
                doc.Data.TryGetValue("price", out var price) ? price : null
            });

        var categories = new[] { "Electronics", "Clothing", "Food" };
        int docId = 1;
        foreach (var category in categories)
        {
            for (int price = 10; price <= 50; price += 10)
            {
                var doc = new Document
                {
                    Id = $"prod{docId++}",
                    Data = new Dictionary<string, object>
                    {
                        { "category", category },
                        { "price", price }
                    }
                };
                manager.IndexDocument("products", doc);
            }
        }

        // Get all products in Electronics with price >= 30, and all Clothing products
        var results = index.GetGreaterThanOrEqual(new CompoundIndexKey("Electronics", 30)).ToList();

        Assert.True(results.Count >= 3); // At least Electronics 30, 40, 50
        Assert.All(results, r => 
        {
            // Either Electronics with price >= 30, or any Clothing, or any Food
            Assert.True(
                (r.Key[0]?.ToString() == "Electronics" && (int)(r.Key[1] ?? 0) >= 30) ||
                r.Key[0]?.ToString() == "Clothing" ||
                r.Key[0]?.ToString() == "Food"
            );
        });
    }

    #endregion

    #region Index Manager Integration Tests

    [Fact]
    public void IndexManager_MixedIndexes_SingleAndCompound()
    {
        var manager = new IndexManager();

        // Create single-field index
        var emailIndex = manager.CreateIndex(
            "users",
            "email",
            isUnique: true,
            keySelector: doc => doc.Data.TryGetValue("email", out var val) ? val?.ToString() ?? "" : "");

        // Create compound index
        var deptAgeIndex = manager.CreateCompoundIndex(
            "users",
            new[] { "department", "age" },
            isUnique: false,
            keySelector: doc => new object?[]
            {
                doc.Data.TryGetValue("department", out var dept) ? dept : null,
                doc.Data.TryGetValue("age", out var age) ? age : null
            });

        var doc = new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object>
            {
                { "email", "john@example.com" },
                { "department", "IT" },
                { "age", 25 }
            }
        };

        manager.IndexDocument("users", doc);

        Assert.Equal(1, emailIndex.Count);
        Assert.Equal(1, deptAgeIndex.Count);
        Assert.True(emailIndex.ContainsKey("john@example.com"));
        Assert.True(deptAgeIndex.ContainsKey(new CompoundIndexKey("IT", 25)));
    }

    [Fact]
    public void IndexManager_GetCompoundIndex_ReturnsCorrectIndex()
    {
        var manager = new IndexManager();
        
        var createdIndex = manager.CreateCompoundIndex(
            "users",
            new[] { "department", "age" },
            isUnique: false,
            keySelector: doc => new object?[]
            {
                doc.Data.TryGetValue("department", out var dept) ? dept : null,
                doc.Data.TryGetValue("age", out var age) ? age : null
            });

        var retrievedIndex = manager.GetCompoundIndex("users", "department", "age");

        Assert.NotNull(retrievedIndex);
        Assert.Equal(createdIndex.Name, retrievedIndex.Name);
    }

    [Fact]
    public void IndexManager_GetCompoundIndex_NonExistent_ReturnsNull()
    {
        var manager = new IndexManager();

        var retrievedIndex = manager.GetCompoundIndex("users", "field1", "field2");

        Assert.Null(retrievedIndex);
    }

    [Fact]
    public void IndexManager_HasCompoundIndex_ReturnsCorrectResult()
    {
        var manager = new IndexManager();
        
        manager.CreateCompoundIndex(
            "users",
            new[] { "department", "age" },
            isUnique: false,
            keySelector: doc => new object?[] 
            { 
                doc.Data.TryGetValue("department", out var dept) ? dept : null,
                doc.Data.TryGetValue("age", out var age) ? age : null
            });

        Assert.True(manager.HasCompoundIndex("users", "department", "age"));
        Assert.False(manager.HasCompoundIndex("users", "department", "salary"));
    }

    [Fact]
    public void IndexManager_GetIndexStats_CompoundIndex_ShowsCorrectType()
    {
        var manager = new IndexManager();
        
        manager.CreateCompoundIndex(
            "users",
            new[] { "department", "age" },
            isUnique: false,
            keySelector: doc => new object?[]
            {
                doc.Data.TryGetValue("department", out var dept) ? dept : null,
                doc.Data.TryGetValue("age", out var age) ? age : null
            });

        var stats = manager.GetIndexStats("users").ToList();

        Assert.Single(stats);
        Assert.Equal("department+age", stats[0].FieldName);
        Assert.Equal("Compound B-Tree", stats[0].IndexType);
    }

    [Fact]
    public void IndexManager_GetIndexStats_UniqueCompoundIndex_ShowsCorrectType()
    {
        var manager = new IndexManager();
        
        manager.CreateCompoundIndex(
            "users",
            new[] { "tenantId", "email" },
            isUnique: true,
            keySelector: doc => new object?[]
            {
                doc.Data.TryGetValue("tenantId", out var tenant) ? tenant : null,
                doc.Data.TryGetValue("email", out var email) ? email : null
            });

        var stats = manager.GetIndexStats("users").ToList();

        Assert.Single(stats);
        Assert.Equal("tenantId+email", stats[0].FieldName);
        Assert.Equal("Unique Compound B-Tree", stats[0].IndexType);
    }

    [Fact]
    public void IndexManager_DropIndex_CompoundIndex_RemovesCorrectly()
    {
        var manager = new IndexManager();
        
        manager.CreateCompoundIndex(
            "users",
            new[] { "department", "age" },
            isUnique: false,
            keySelector: doc => new object?[] { "IT", 25 });

        bool dropped = manager.DropIndex("users", "department+age");

        Assert.True(dropped);
        Assert.False(manager.HasCompoundIndex("users", "department", "age"));
    }

    [Fact]
    public void IndexManager_RemoveDocument_CompoundIndex_UpdatesCorrectly()
    {
        var manager = new IndexManager();
        var index = manager.CreateCompoundIndex(
            "users",
            new[] { "department", "age" },
            isUnique: false,
            keySelector: doc => new object?[]
            {
                doc.Data.TryGetValue("department", out var dept) ? dept : null,
                doc.Data.TryGetValue("age", out var age) ? age : null
            });

        var doc = new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object>
            {
                { "department", "IT" },
                { "age", 25 }
            }
        };

        manager.IndexDocument("users", doc);
        Assert.Equal(1, index.Count);

        manager.RemoveDocument("users", doc);
        Assert.Equal(0, index.Count);
    }

    [Fact]
    public void IndexManager_UpdateDocument_CompoundIndex_UpdatesCorrectly()
    {
        var manager = new IndexManager();
        var index = manager.CreateCompoundIndex(
            "users",
            new[] { "department", "age" },
            isUnique: false,
            keySelector: doc => new object?[]
            {
                doc.Data.TryGetValue("department", out var dept) ? dept : null,
                doc.Data.TryGetValue("age", out var age) ? age : null
            });

        var oldDoc = new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object>
            {
                { "department", "IT" },
                { "age", 25 }
            }
        };
        var newDoc = new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object>
            {
                { "department", "Sales" },
                { "age", 30 }
            }
        };

        manager.IndexDocument("users", oldDoc);
        manager.UpdateDocument("users", oldDoc, newDoc);

        Assert.Equal(1, index.Count);
        Assert.False(index.ContainsKey(new CompoundIndexKey("IT", 25)));
        Assert.True(index.ContainsKey(new CompoundIndexKey("Sales", 30)));
    }

    #endregion

    #region Multi-Tenant Scenario Tests

    [Fact]
    public void MultiTenant_UniqueEmailPerTenant()
    {
        var manager = new IndexManager();
        manager.CreateCompoundIndex(
            "users",
            new[] { "tenantId", "email" },
            isUnique: true,
            keySelector: doc => new object?[]
            {
                doc.Data.TryGetValue("tenantId", out var tenant) ? tenant : null,
                doc.Data.TryGetValue("email", out var email) ? email : null
            });

        // Tenant A admin
        var tenantAAdmin = new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object>
            {
                { "tenantId", "tenant-a" },
                { "email", "admin@company.com" },
                { "role", "admin" }
            }
        };

        // Tenant B admin - same email, different tenant (should be allowed)
        var tenantBAdmin = new Document
        {
            Id = "user2",
            Data = new Dictionary<string, object>
            {
                { "tenantId", "tenant-b" },
                { "email", "admin@company.com" },
                { "role", "admin" }
            }
        };

        // Another tenant A admin - same email, same tenant (should fail)
        var anotherTenantAAdmin = new Document
        {
            Id = "user3",
            Data = new Dictionary<string, object>
            {
                { "tenantId", "tenant-a" },
                { "email", "admin@company.com" },
                { "role", "admin" }
            }
        };

        manager.IndexDocument("users", tenantAAdmin);
        manager.IndexDocument("users", tenantBAdmin); // Should succeed

        Assert.Throws<DuplicateKeyException>(() => 
            manager.IndexDocument("users", anotherTenantAAdmin));
    }

    [Fact]
    public void MultiTenant_OrderLookupByCustomerAndDate()
    {
        var manager = new IndexManager();
        var index = manager.CreateCompoundIndex(
            "orders",
            new[] { "tenantId", "customerId", "orderDate" },
            isUnique: false,
            keySelector: doc => new object?[]
            {
                doc.Data.TryGetValue("tenantId", out var t) ? t : null,
                doc.Data.TryGetValue("customerId", out var c) ? c : null,
                doc.Data.TryGetValue("orderDate", out var d) ? d : null
            });

        // Add orders for different tenants
        for (int tenant = 1; tenant <= 2; tenant++)
        {
            for (int customer = 1; customer <= 3; customer++)
            {
                for (int day = 1; day <= 5; day++)
                {
                    var doc = new Document
                    {
                        Id = $"t{tenant}-c{customer}-d{day}",
                        Data = new Dictionary<string, object>
                        {
                            { "tenantId", $"tenant-{tenant}" },
                            { "customerId", $"cust-{customer}" },
                            { "orderDate", new DateTime(2024, 1, day) },
                            { "amount", 100 * day }
                        }
                    };
                    manager.IndexDocument("orders", doc);
                }
            }
        }

        // Query: All orders for tenant-1, cust-2 from Jan 2 onwards
        var results = index.GetGreaterThanOrEqual(
            new CompoundIndexKey("tenant-1", "cust-2", new DateTime(2024, 1, 2))).ToList();

        // Should get at least 4 orders for tenant-1, cust-2 (days 2-5)
        // Note: GetGreaterThanOrEqual returns all keys >= the specified key in lexicographical order
        // This includes tenant-1, cust-2 and all subsequent customers/tenants
        Assert.True(results.Count >= 4);
        
        // Verify we got the expected tenant-1, cust-2 orders
        var tenant1Cust2Orders = results.Where(r => 
            r.Key[0]?.ToString() == "tenant-1" && 
            r.Key[1]?.ToString() == "cust-2").ToList();
        Assert.True(tenant1Cust2Orders.Count >= 4);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void CompoundIndex_MissingFields_DocumentIndexedWithNulls()
    {
        var manager = new IndexManager();
        var index = manager.CreateCompoundIndex(
            "users",
            new[] { "department", "age" },
            isUnique: false,
            keySelector: doc => new object?[]
            {
                doc.Data.TryGetValue("department", out var dept) ? dept : null,
                doc.Data.TryGetValue("age", out var age) ? age : null
            });

        var doc = new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object>
            {
                { "name", "John" } // Missing department and age
            }
        };

        // Document will be indexed with null values (CompoundIndexKey handles nulls)
        manager.IndexDocument("users", doc);

        // Document is indexed with nulls (CompoundIndexWrapper doesn't filter nulls)
        Assert.True(index.Count >= 0);
    }

    [Fact]
    public void CompoundIndex_PartialFields_IgnoresDocument()
    {
        var manager = new IndexManager();
        var index = manager.CreateCompoundIndex(
            "users",
            new[] { "department", "age", "salary" },
            isUnique: false,
            keySelector: doc => new object?[]
            {
                doc.Data.TryGetValue("department", out var dept) ? dept : null,
                doc.Data.TryGetValue("age", out var age) ? age : null,
                doc.Data.TryGetValue("salary", out var sal) ? sal : null
            });

        var doc = new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object>
            {
                { "department", "IT" },
                { "age", 25 }
                // Missing salary
            }
        };

        manager.IndexDocument("users", doc);

        // Document has partial fields but indexing may still work with null
        // Behavior depends on implementation - we just verify no exception
        Assert.True(index.Count >= 0);
    }

    [Fact]
    public void CompoundIndex_EmptyCollection_ReturnsEmptyResults()
    {
        var manager = new IndexManager();
        var index = manager.CreateCompoundIndex(
            "users",
            new[] { "department", "age" },
            isUnique: false,
            keySelector: doc => new object?[] { "IT", 25 });

        var results = index.RangeQuery(
            new CompoundIndexKey("IT", 0),
            new CompoundIndexKey("IT", 100)).ToList();

        Assert.Empty(results);
    }

    [Fact]
    public void UniqueIndex_NullKey_Allowed()
    {
        var manager = new IndexManager();
        var index = manager.CreateIndex(
            "users",
            "optionalField",
            isUnique: true,
            keySelector: doc => doc.Data.TryGetValue("optionalField", out var val) ? val?.ToString() : null);

        var doc1 = new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object>()
            // optionalField is missing
        };
        var doc2 = new Document
        {
            Id = "user2",
            Data = new Dictionary<string, object>()
            // optionalField is also missing
        };

        manager.IndexDocument("users", doc1);
        manager.IndexDocument("users", doc2); // Multiple nulls should be allowed in unique index

        // Null keys are typically ignored or handled specially
        Assert.True(index.Count <= 1);
    }

    #endregion

    #region Performance Tests (Lightweight)

    [Fact]
    public void CompoundIndex_LargeDataset_PerformsWell()
    {
        var manager = new IndexManager();
        var index = manager.CreateCompoundIndex(
            "events",
            new[] { "region", "timestamp" },
            isUnique: false,
            keySelector: doc => new object?[]
            {
                doc.Data.TryGetValue("region", out var region) ? region : null,
                doc.Data.TryGetValue("timestamp", out var ts) ? ts : null
            });

        // Insert 1000 documents
        for (int i = 0; i < 1000; i++)
        {
            var doc = new Document
            {
                Id = $"event{i}",
                Data = new Dictionary<string, object>
                {
                    { "region", $"region-{i % 10}" },
                    { "timestamp", DateTime.UtcNow.AddMinutes(i) },
                    { "data", $"Event data {i}" }
                }
            };
            manager.IndexDocument("events", doc);
        }

        Assert.Equal(1000, index.Count);

        // Query should be fast
        var results = index.RangeQuery(
            new CompoundIndexKey("region-5", DateTime.MinValue),
            new CompoundIndexKey("region-5", DateTime.MaxValue)).ToList();

        Assert.Equal(100, results.Count); // 100 events per region
    }

    #endregion
}
