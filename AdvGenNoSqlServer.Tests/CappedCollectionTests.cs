// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Storage;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for CappedCollection and CappedDocumentStore
/// </summary>
public class CappedCollectionTests
{
    #region CappedCollection Tests

    [Fact]
    public void Constructor_WithValidParameters_CreatesCollection()
    {
        var options = new CappedCollectionOptions { MaxDocuments = 100 };
        var collection = new CappedCollection("test", options);

        Assert.Equal("test", collection.Name);
        Assert.Equal(0, collection.Count);
        Assert.True(collection.CreatedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void Constructor_WithNullName_ThrowsArgumentException()
    {
        var options = new CappedCollectionOptions();
        Assert.Throws<ArgumentException>(() => new CappedCollection(null!, options));
    }

    [Fact]
    public void Constructor_WithEmptyName_ThrowsArgumentException()
    {
        var options = new CappedCollectionOptions();
        Assert.Throws<ArgumentException>(() => new CappedCollection("", options));
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new CappedCollection("test", null!));
    }

    [Fact]
    public void Insert_SingleDocument_IncreasesCount()
    {
        var options = new CappedCollectionOptions { MaxDocuments = 10 };
        var collection = new CappedCollection("test", options);

        var doc = new Document { Id = "1", Data = new Dictionary<string, object> { ["name"] = "Test" } };
        var result = collection.Insert(doc);

        Assert.Equal(1, collection.Count);
        Assert.Equal("1", result.Id);
        Assert.Equal(1, result.Version);
        Assert.True(result.CreatedAt > DateTime.MinValue);
    }

    [Fact]
    public void Insert_MultipleDocuments_MaintainsInsertionOrder()
    {
        var options = new CappedCollectionOptions { MaxDocuments = 10 };
        var collection = new CappedCollection("test", options);

        for (int i = 1; i <= 5; i++)
        {
            collection.Insert(new Document { Id = $"doc{i}", Data = new Dictionary<string, object> { ["order"] = i } });
        }

        var allDocs = collection.GetAll().ToList();
        Assert.Equal(5, allDocs.Count);

        // Verify insertion order (oldest first)
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal($"doc{i + 1}", allDocs[i].Id);
        }
    }

    [Fact]
    public void Insert_DuplicateId_ThrowsException()
    {
        var options = new CappedCollectionOptions { MaxDocuments = 10 };
        var collection = new CappedCollection("test", options);

        var doc = new Document { Id = "1", Data = new Dictionary<string, object>() };
        collection.Insert(doc);

        Assert.Throws<DocumentAlreadyExistsException>(() => collection.Insert(doc));
    }

    [Fact]
    public void Insert_NullDocument_ThrowsArgumentNullException()
    {
        var options = new CappedCollectionOptions();
        var collection = new CappedCollection("test", options);

        Assert.Throws<ArgumentNullException>(() => collection.Insert(null!));
    }

    [Fact]
    public void Insert_ExceedsMaxDocuments_RemovesOldest()
    {
        var options = new CappedCollectionOptions
        {
            MaxDocuments = 5,
            EnforceMaxDocuments = true,
            EnforceMaxSize = false
        };
        var collection = new CappedCollection("test", options);
        var removedIds = new List<string>();

        collection.CollectionTrimmed += (s, e) => removedIds.AddRange(e.RemovedDocumentIds);

        // Insert 7 documents (2 over limit)
        for (int i = 1; i <= 7; i++)
        {
            collection.Insert(new Document { Id = $"doc{i}", Data = new Dictionary<string, object>() });
        }

        Assert.Equal(5, collection.Count);
        Assert.Contains("doc1", removedIds);
        Assert.Contains("doc2", removedIds);
        Assert.Null(collection.Get("doc1"));
        Assert.Null(collection.Get("doc2"));
        Assert.NotNull(collection.Get("doc6"));
        Assert.NotNull(collection.Get("doc7"));
    }

    [Fact]
    public void Get_ExistingDocument_ReturnsDocument()
    {
        var options = new CappedCollectionOptions();
        var collection = new CappedCollection("test", options);
        var doc = new Document { Id = "1", Data = new Dictionary<string, object> { ["name"] = "Test" } };
        collection.Insert(doc);

        var result = collection.Get("1");

        Assert.NotNull(result);
        Assert.Equal("1", result.Id);
        Assert.Equal("Test", result.Data["name"]);
    }

    [Fact]
    public void Get_NonExistingDocument_ReturnsNull()
    {
        var options = new CappedCollectionOptions();
        var collection = new CappedCollection("test", options);

        var result = collection.Get("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public void GetMany_WithValidIds_ReturnsMatchingDocuments()
    {
        var options = new CappedCollectionOptions();
        var collection = new CappedCollection("test", options);

        collection.Insert(new Document { Id = "1", Data = new Dictionary<string, object>() });
        collection.Insert(new Document { Id = "2", Data = new Dictionary<string, object>() });
        collection.Insert(new Document { Id = "3", Data = new Dictionary<string, object>() });

        var results = collection.GetMany(new[] { "1", "3", "nonexistent" }).ToList();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, d => d.Id == "1");
        Assert.Contains(results, d => d.Id == "3");
    }

    [Fact]
    public void GetMany_WithNullIds_ReturnsEmpty()
    {
        var options = new CappedCollectionOptions();
        var collection = new CappedCollection("test", options);

        var results = collection.GetMany(null!).ToList();

        Assert.Empty(results);
    }

    [Fact]
    public void GetRecent_ReturnsDocumentsInReverseOrder()
    {
        var options = new CappedCollectionOptions();
        var collection = new CappedCollection("test", options);

        for (int i = 1; i <= 5; i++)
        {
            collection.Insert(new Document { Id = $"doc{i}", Data = new Dictionary<string, object>() });
        }

        var recent = collection.GetRecent().ToList();

        Assert.Equal(5, recent.Count);
        Assert.Equal("doc5", recent[0].Id);
        Assert.Equal("doc4", recent[1].Id);
        Assert.Equal("doc1", recent[4].Id);
    }

    [Fact]
    public void GetRecent_WithLimit_ReturnsLimitedDocuments()
    {
        var options = new CappedCollectionOptions();
        var collection = new CappedCollection("test", options);

        for (int i = 1; i <= 10; i++)
        {
            collection.Insert(new Document { Id = $"doc{i}", Data = new Dictionary<string, object>() });
        }

        var recent = collection.GetRecent(3).ToList();

        Assert.Equal(3, recent.Count);
        Assert.Equal("doc10", recent[0].Id);
        Assert.Equal("doc9", recent[1].Id);
        Assert.Equal("doc8", recent[2].Id);
    }

    [Fact]
    public void Exists_ExistingDocument_ReturnsTrue()
    {
        var options = new CappedCollectionOptions();
        var collection = new CappedCollection("test", options);
        collection.Insert(new Document { Id = "1", Data = new Dictionary<string, object>() });

        Assert.True(collection.Exists("1"));
    }

    [Fact]
    public void Exists_NonExistingDocument_ReturnsFalse()
    {
        var options = new CappedCollectionOptions();
        var collection = new CappedCollection("test", options);

        Assert.False(collection.Exists("nonexistent"));
    }

    [Fact]
    public void Delete_ExistingDocument_RemovesAndDecreasesCount()
    {
        var options = new CappedCollectionOptions();
        var collection = new CappedCollection("test", options);
        collection.Insert(new Document { Id = "1", Data = new Dictionary<string, object>() });

        var result = collection.Delete("1");

        Assert.True(result);
        Assert.Equal(0, collection.Count);
        Assert.False(collection.Exists("1"));
    }

    [Fact]
    public void Delete_NonExistingDocument_ReturnsFalse()
    {
        var options = new CappedCollectionOptions();
        var collection = new CappedCollection("test", options);

        var result = collection.Delete("nonexistent");

        Assert.False(result);
    }

    [Fact]
    public void Clear_RemovesAllDocuments()
    {
        var options = new CappedCollectionOptions();
        var collection = new CappedCollection("test", options);

        for (int i = 1; i <= 5; i++)
        {
            collection.Insert(new Document { Id = $"doc{i}", Data = new Dictionary<string, object>() });
        }

        collection.Clear();

        Assert.Equal(0, collection.Count);
        Assert.Empty(collection.GetAll());
    }

    [Fact]
    public void GetStats_ReturnsCorrectStatistics()
    {
        var options = new CappedCollectionOptions
        {
            MaxDocuments = 100,
            EnforceMaxDocuments = true,
            MaxSizeBytes = 1024 * 1024,
            EnforceMaxSize = true
        };
        var collection = new CappedCollection("test", options);

        for (int i = 1; i <= 5; i++)
        {
            collection.Insert(new Document { Id = $"doc{i}", Data = new Dictionary<string, object> { ["data"] = "value" } });
        }

        var stats = collection.GetStats();

        Assert.Equal("test", stats.Name);
        Assert.Equal(5, stats.DocumentCount);
        Assert.True(stats.TotalSizeBytes > 0);
        Assert.Equal(100, stats.MaxDocuments);
        Assert.Equal(1024 * 1024, stats.MaxSizeBytes);
        Assert.True(stats.IsCapped);
    }

    [Fact]
    public void CollectionTrimmed_EventRaised_WhenDocumentsRemoved()
    {
        var options = new CappedCollectionOptions
        {
            MaxDocuments = 3,
            EnforceMaxDocuments = true,
            EnforceMaxSize = false
        };
        var collection = new CappedCollection("test", options);
        var eventRaised = false;
        CappedCollectionTrimmedEventArgs? eventArgs = null;

        var totalRemoved = 0;
        collection.CollectionTrimmed += (s, e) =>
        {
            eventRaised = true;
            totalRemoved += e.RemovedCount;
        };

        // Insert 5 documents (2 over limit)
        for (int i = 1; i <= 5; i++)
        {
            collection.Insert(new Document { Id = $"doc{i}", Data = new Dictionary<string, object>() });
        }

        Assert.True(eventRaised);
        Assert.Equal(2, totalRemoved); // Total 2 documents should be removed across all trim events
    }

    #endregion

    #region CappedDocumentStore Tests

    [Fact]
    public async Task CreateCappedCollection_WithValidOptions_CreatesCollection()
    {
        var underlyingStore = new DocumentStore();
        var cappedStore = new CappedDocumentStore(underlyingStore);
        var options = new CappedCollectionOptions { MaxDocuments = 100 };

        await cappedStore.CreateCappedCollectionAsync("logs", options);

        Assert.True(cappedStore.IsCappedCollection("logs"));
        var retrievedOptions = cappedStore.GetCappedCollectionOptions("logs");
        Assert.NotNull(retrievedOptions);
        Assert.Equal(100, retrievedOptions.MaxDocuments);
    }

    [Fact]
    public async Task CreateCappedCollection_WithNullName_ThrowsArgumentException()
    {
        var underlyingStore = new DocumentStore();
        var cappedStore = new CappedDocumentStore(underlyingStore);
        var options = new CappedCollectionOptions();

        await Assert.ThrowsAsync<ArgumentException>(() => cappedStore.CreateCappedCollectionAsync(null!, options));
    }

    [Fact]
    public async Task CreateCappedCollection_WithNullOptions_ThrowsArgumentNullException()
    {
        var underlyingStore = new DocumentStore();
        var cappedStore = new CappedDocumentStore(underlyingStore);

        await Assert.ThrowsAsync<ArgumentNullException>(() => cappedStore.CreateCappedCollectionAsync("logs", null!));
    }

    [Fact]
    public async Task CreateCappedCollection_WithInvalidMaxDocuments_ThrowsArgumentException()
    {
        var underlyingStore = new DocumentStore();
        var cappedStore = new CappedDocumentStore(underlyingStore);
        var options = new CappedCollectionOptions
        {
            EnforceMaxDocuments = true,
            MaxDocuments = 0
        };

        await Assert.ThrowsAsync<ArgumentException>(() => cappedStore.CreateCappedCollectionAsync("logs", options));
    }

    [Fact]
    public async Task InsertAsync_ToCappedCollection_StoresDocument()
    {
        var underlyingStore = new DocumentStore();
        var cappedStore = new CappedDocumentStore(underlyingStore);
        await cappedStore.CreateCappedCollectionAsync("logs", new CappedCollectionOptions { MaxDocuments = 100 });

        var doc = new Document { Id = "1", Data = new Dictionary<string, object> { ["message"] = "Test log" } };
        var result = await cappedStore.InsertAsync("logs", doc);

        Assert.Equal("1", result.Id);
        Assert.Equal(1, await cappedStore.CountAsync("logs"));
    }

    [Fact]
    public async Task InsertAsync_ToCappedCollection_EnforcesLimits()
    {
        var underlyingStore = new DocumentStore();
        var cappedStore = new CappedDocumentStore(underlyingStore);
        await cappedStore.CreateCappedCollectionAsync("logs", new CappedCollectionOptions
        {
            MaxDocuments = 5,
            EnforceMaxDocuments = true,
            EnforceMaxSize = false
        });

        // Insert 10 documents
        for (int i = 1; i <= 10; i++)
        {
            await cappedStore.InsertAsync("logs", new Document
            {
                Id = $"log{i}",
                Data = new Dictionary<string, object> { ["message"] = $"Log entry {i}" }
            });
        }

        var count = await cappedStore.CountAsync("logs");
        Assert.Equal(5, count);

        // Oldest documents should be removed
        Assert.Null(await cappedStore.GetAsync("logs", "log1"));
        Assert.Null(await cappedStore.GetAsync("logs", "log5"));
        Assert.NotNull(await cappedStore.GetAsync("logs", "log6"));
        Assert.NotNull(await cappedStore.GetAsync("logs", "log10"));
    }

    [Fact]
    public async Task GetAsync_FromCappedCollection_ReturnsDocument()
    {
        var underlyingStore = new DocumentStore();
        var cappedStore = new CappedDocumentStore(underlyingStore);
        await cappedStore.CreateCappedCollectionAsync("logs", new CappedCollectionOptions());

        await cappedStore.InsertAsync("logs", new Document { Id = "1", Data = new Dictionary<string, object> { ["msg"] = "Hello" } });

        var result = await cappedStore.GetAsync("logs", "1");

        Assert.NotNull(result);
        Assert.Equal("Hello", result.Data["msg"]);
    }

    [Fact]
    public async Task UpdateAsync_OnCappedCollection_ThrowsNotSupportedException()
    {
        var underlyingStore = new DocumentStore();
        var cappedStore = new CappedDocumentStore(underlyingStore);
        await cappedStore.CreateCappedCollectionAsync("logs", new CappedCollectionOptions());

        await cappedStore.InsertAsync("logs", new Document { Id = "1", Data = new Dictionary<string, object>() });

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            cappedStore.UpdateAsync("logs", new Document { Id = "1", Data = new Dictionary<string, object>() }));
    }

    [Fact]
    public async Task DeleteAsync_FromCappedCollection_RemovesDocument()
    {
        var underlyingStore = new DocumentStore();
        var cappedStore = new CappedDocumentStore(underlyingStore);
        await cappedStore.CreateCappedCollectionAsync("logs", new CappedCollectionOptions());

        await cappedStore.InsertAsync("logs", new Document { Id = "1", Data = new Dictionary<string, object>() });

        var result = await cappedStore.DeleteAsync("logs", "1");

        Assert.True(result);
        Assert.Equal(0, await cappedStore.CountAsync("logs"));
    }

    [Fact]
    public async Task GetRecentAsync_FromCappedCollection_ReturnsRecentDocuments()
    {
        var underlyingStore = new DocumentStore();
        var cappedStore = new CappedDocumentStore(underlyingStore);
        await cappedStore.CreateCappedCollectionAsync("logs", new CappedCollectionOptions());

        for (int i = 1; i <= 10; i++)
        {
            await cappedStore.InsertAsync("logs", new Document
            {
                Id = $"log{i}",
                Data = new Dictionary<string, object> { ["seq"] = i }
            });
        }

        var recent = (await cappedStore.GetRecentAsync("logs", 3)).ToList();

        Assert.Equal(3, recent.Count);
        Assert.Equal("log10", recent[0].Id);
        Assert.Equal("log9", recent[1].Id);
        Assert.Equal("log8", recent[2].Id);
    }

    [Fact]
    public async Task GetAllInNaturalOrderAsync_FromCappedCollection_ReturnsInInsertionOrder()
    {
        var underlyingStore = new DocumentStore();
        var cappedStore = new CappedDocumentStore(underlyingStore);
        await cappedStore.CreateCappedCollectionAsync("logs", new CappedCollectionOptions());

        for (int i = 1; i <= 5; i++)
        {
            await cappedStore.InsertAsync("logs", new Document
            {
                Id = $"log{i}",
                Data = new Dictionary<string, object> { ["seq"] = i }
            });
        }

        var docs = (await cappedStore.GetAllInNaturalOrderAsync("logs")).ToList();

        Assert.Equal(5, docs.Count);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal($"log{i + 1}", docs[i].Id);
        }
    }

    [Fact]
    public async Task GetRecentAsync_FromNonCappedCollection_ThrowsInvalidOperationException()
    {
        var underlyingStore = new DocumentStore();
        var cappedStore = new CappedDocumentStore(underlyingStore);

        // Create regular collection
        await underlyingStore.CreateCollectionAsync("regular");

        await Assert.ThrowsAsync<InvalidOperationException>(() => cappedStore.GetRecentAsync("regular"));
    }

    [Fact]
    public async Task GetCappedCollectionStats_ReturnsStats()
    {
        var underlyingStore = new DocumentStore();
        var cappedStore = new CappedDocumentStore(underlyingStore);
        await cappedStore.CreateCappedCollectionAsync("logs", new CappedCollectionOptions
        {
            MaxDocuments = 1000,
            EnforceMaxDocuments = true
        });

        await cappedStore.InsertAsync("logs", new Document { Id = "1", Data = new Dictionary<string, object>() });

        var stats = cappedStore.GetCappedCollectionStats("logs");

        Assert.NotNull(stats);
        Assert.Equal("logs", stats.Name);
        Assert.Equal(1, stats.DocumentCount);
        Assert.Equal(1000, stats.MaxDocuments);
        Assert.True(stats.IsCapped);
    }

    [Fact]
    public void GetCappedCollectionStats_ForNonCappedCollection_ReturnsNull()
    {
        var underlyingStore = new DocumentStore();
        var cappedStore = new CappedDocumentStore(underlyingStore);

        var stats = cappedStore.GetCappedCollectionStats("nonexistent");

        Assert.Null(stats);
    }

    [Fact]
    public void IsCappedCollection_ForCappedCollection_ReturnsTrue()
    {
        var underlyingStore = new DocumentStore();
        var cappedStore = new CappedDocumentStore(underlyingStore);
        cappedStore.CreateCappedCollectionAsync("logs", new CappedCollectionOptions()).Wait();

        Assert.True(cappedStore.IsCappedCollection("logs"));
    }

    [Fact]
    public void IsCappedCollection_ForNonCappedCollection_ReturnsFalse()
    {
        var underlyingStore = new DocumentStore();
        var cappedStore = new CappedDocumentStore(underlyingStore);

        Assert.False(cappedStore.IsCappedCollection("regular"));
    }

    [Fact]
    public void IsCappedCollection_WithNullName_ReturnsFalse()
    {
        var underlyingStore = new DocumentStore();
        var cappedStore = new CappedDocumentStore(underlyingStore);

        Assert.False(cappedStore.IsCappedCollection(null!));
    }

    [Fact]
    public async Task DropCollectionAsync_RemovesCappedCollection()
    {
        var underlyingStore = new DocumentStore();
        var cappedStore = new CappedDocumentStore(underlyingStore);
        await cappedStore.CreateCappedCollectionAsync("logs", new CappedCollectionOptions());

        Assert.True(cappedStore.IsCappedCollection("logs"));

        var result = await cappedStore.DropCollectionAsync("logs");

        Assert.True(result);
        Assert.False(cappedStore.IsCappedCollection("logs"));
    }

    [Fact]
    public async Task GetCollectionsAsync_IncludesCappedCollections()
    {
        var underlyingStore = new DocumentStore();
        var cappedStore = new CappedDocumentStore(underlyingStore);

        await underlyingStore.CreateCollectionAsync("regular");
        await cappedStore.CreateCappedCollectionAsync("logs", new CappedCollectionOptions());

        var collections = (await cappedStore.GetCollectionsAsync()).ToList();

        Assert.Contains("regular", collections);
        Assert.Contains("logs", collections);
    }

    [Fact]
    public async Task CappedCollectionTrimmed_EventPropagated()
    {
        var underlyingStore = new DocumentStore();
        var cappedStore = new CappedDocumentStore(underlyingStore);
        await cappedStore.CreateCappedCollectionAsync("logs", new CappedCollectionOptions
        {
            MaxDocuments = 3,
            EnforceMaxDocuments = true,
            EnforceMaxSize = false
        });

        var eventRaised = false;
        cappedStore.CappedCollectionTrimmed += (s, e) => eventRaised = true;

        // Insert 5 documents to trigger trim
        for (int i = 1; i <= 5; i++)
        {
            await cappedStore.InsertAsync("logs", new Document { Id = $"log{i}", Data = new Dictionary<string, object>() });
        }

        Assert.True(eventRaised);
    }

    [Fact]
    public async Task ClearCollectionAsync_ClearsCappedCollection()
    {
        var underlyingStore = new DocumentStore();
        var cappedStore = new CappedDocumentStore(underlyingStore);
        await cappedStore.CreateCappedCollectionAsync("logs", new CappedCollectionOptions());

        for (int i = 1; i <= 5; i++)
        {
            await cappedStore.InsertAsync("logs", new Document { Id = $"log{i}", Data = new Dictionary<string, object>() });
        }

        await cappedStore.ClearCollectionAsync("logs");

        Assert.Equal(0, await cappedStore.CountAsync("logs"));
    }

    [Fact]
    public async Task ExistsAsync_OnCappedCollection_Works()
    {
        var underlyingStore = new DocumentStore();
        var cappedStore = new CappedDocumentStore(underlyingStore);
        await cappedStore.CreateCappedCollectionAsync("logs", new CappedCollectionOptions());

        await cappedStore.InsertAsync("logs", new Document { Id = "1", Data = new Dictionary<string, object>() });

        Assert.True(await cappedStore.ExistsAsync("logs", "1"));
        Assert.False(await cappedStore.ExistsAsync("logs", "nonexistent"));
    }

    [Fact]
    public async Task GetManyAsync_OnCappedCollection_Works()
    {
        var underlyingStore = new DocumentStore();
        var cappedStore = new CappedDocumentStore(underlyingStore);
        await cappedStore.CreateCappedCollectionAsync("logs", new CappedCollectionOptions());

        for (int i = 1; i <= 3; i++)
        {
            await cappedStore.InsertAsync("logs", new Document { Id = $"log{i}", Data = new Dictionary<string, object>() });
        }

        var results = (await cappedStore.GetManyAsync("logs", new[] { "log1", "log3" })).ToList();

        Assert.Equal(2, results.Count);
    }

    #endregion
}
