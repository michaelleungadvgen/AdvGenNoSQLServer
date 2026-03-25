// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Attachments;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Storage;
using AdvGenNoSqlServer.Storage.Attachments;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for AttachmentEnabledDocumentStore
/// </summary>
public class AttachmentEnabledDocumentStoreTests : IDisposable
{
    private readonly string _testBasePath;
    private readonly DocumentStore _documentStore;
    private readonly AttachmentStore _attachmentStore;
    private readonly AttachmentEnabledDocumentStore _store;

    public AttachmentEnabledDocumentStoreTests()
    {
        _testBasePath = Path.Combine(Path.GetTempPath(), $"AttDocTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testBasePath);
        
        _documentStore = new DocumentStore();
        _attachmentStore = new AttachmentStore(new AttachmentStoreOptions
        {
            BasePath = Path.Combine(_testBasePath, "attachments")
        });
        _store = new AttachmentEnabledDocumentStore(_documentStore, _attachmentStore);
    }

    public void Dispose()
    {
        _store.Dispose();
        if (Directory.Exists(_testBasePath))
        {
            Directory.Delete(_testBasePath, true);
        }
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidStores_ShouldCreate()
    {
        var docStore = new DocumentStore();
        var attStore = new AttachmentStore(new AttachmentStoreOptions { BasePath = Path.Combine(_testBasePath, "test") });
        
        var store = new AttachmentEnabledDocumentStore(docStore, attStore);
        
        Assert.NotNull(store);
        Assert.Equal(attStore, store.AttachmentStore);
        store.Dispose();
    }

    [Fact]
    public void Constructor_WithNullDocumentStore_ShouldThrow()
    {
        var attStore = new AttachmentStore(new AttachmentStoreOptions { BasePath = Path.Combine(_testBasePath, "test") });
        
        Assert.Throws<ArgumentNullException>(() => new AttachmentEnabledDocumentStore(null!, attStore));
        attStore.Dispose();
    }

    [Fact]
    public void Constructor_WithNullAttachmentStore_ShouldThrow()
    {
        var docStore = new DocumentStore();
        
        Assert.Throws<ArgumentNullException>(() => new AttachmentEnabledDocumentStore(docStore, null!));
    }

    #endregion

    #region Document Operations Tests

    [Fact]
    public async Task InsertAsync_ShouldInsertDocument()
    {
        var doc = new Document 
        { 
            Id = "doc1", 
            Data = new Dictionary<string, object> { ["name"] = "Test" } 
        };
        
        var result = await _store.InsertAsync("users", doc);
        
        Assert.NotNull(result);
        Assert.Equal("doc1", result.Id);
    }

    [Fact]
    public async Task GetAsync_ShouldReturnDocument()
    {
        var doc = new Document 
        { 
            Id = "doc1", 
            Data = new Dictionary<string, object> { ["name"] = "Test" } 
        };
        await _store.InsertAsync("users", doc);
        
        var result = await _store.GetAsync("users", "doc1");
        
        Assert.NotNull(result);
        Assert.Equal("Test", result.Data!["name"]);
    }

    [Fact]
    public async Task DeleteAsync_WithCascadeDelete_ShouldDeleteAttachments()
    {
        var doc = new Document 
        { 
            Id = "doc1", 
            Data = new Dictionary<string, object> { ["name"] = "Test" } 
        };
        await _store.InsertAsync("users", doc);
        await _store.StoreAttachmentAsync("users", "doc1", "file.txt", "text/plain", new byte[] { 1, 2, 3 });
        
        await _store.DeleteAsync("users", "doc1");
        
        Assert.Empty(await _store.ListAttachmentsAsync("users", "doc1"));
    }

    [Fact]
    public async Task DeleteAsync_WithoutCascadeDelete_ShouldKeepAttachments()
    {
        var doc = new Document 
        { 
            Id = "doc1", 
            Data = new Dictionary<string, object> { ["name"] = "Test" } 
        };
        
        var store = new AttachmentEnabledDocumentStore(_documentStore, _attachmentStore, cascadeDelete: false);
        await store.InsertAsync("users", doc);
        await store.StoreAttachmentAsync("users", "doc1", "file.txt", "text/plain", new byte[] { 1, 2, 3 });
        
        await store.DeleteAsync("users", "doc1");
        
        Assert.Single(await store.ListAttachmentsAsync("users", "doc1"));
    }

    [Fact]
    public async Task DropCollectionAsync_WithCascadeDelete_ShouldDeleteAllAttachments()
    {
        var doc1 = new Document { Id = "doc1", Data = new Dictionary<string, object> { ["name"] = "Test1" } };
        var doc2 = new Document { Id = "doc2", Data = new Dictionary<string, object> { ["name"] = "Test2" } };
        await _store.InsertAsync("users", doc1);
        await _store.InsertAsync("users", doc2);
        await _store.StoreAttachmentAsync("users", "doc1", "file1.txt", "text/plain", new byte[] { 1 });
        await _store.StoreAttachmentAsync("users", "doc2", "file2.txt", "text/plain", new byte[] { 2 });
        
        await _store.DropCollectionAsync("users");
        
        Assert.Empty(await _store.ListAttachmentsAsync("users", "doc1"));
        Assert.Empty(await _store.ListAttachmentsAsync("users", "doc2"));
    }

    #endregion

    #region Attachment Operations Tests

    [Fact]
    public async Task StoreAttachmentAsync_ShouldStoreAttachment()
    {
        var doc = new Document 
        { 
            Id = "doc1", 
            Data = new Dictionary<string, object> { ["name"] = "Test" } 
        };
        await _store.InsertAsync("users", doc);
        
        var result = await _store.StoreAttachmentAsync("users", "doc1", "profile.png", "image/png", new byte[] { 1, 2, 3 });
        
        Assert.True(result.Success);
    }

    [Fact]
    public async Task GetAttachmentAsync_ShouldReturnAttachment()
    {
        var doc = new Document 
        { 
            Id = "doc1", 
            Data = new Dictionary<string, object> { ["name"] = "Test" } 
        };
        await _store.InsertAsync("users", doc);
        await _store.StoreAttachmentAsync("users", "doc1", "file.txt", "text/plain", new byte[] { 1, 2, 3 });
        
        var result = await _store.GetAttachmentAsync("users", "doc1", "file.txt");
        
        Assert.NotNull(result);
        Assert.Equal(new byte[] { 1, 2, 3 }, result.Content);
    }

    [Fact]
    public async Task ListAttachmentsAsync_ShouldReturnAllAttachments()
    {
        var doc = new Document 
        { 
            Id = "doc1", 
            Data = new Dictionary<string, object> { ["name"] = "Test" } 
        };
        await _store.InsertAsync("users", doc);
        await _store.StoreAttachmentAsync("users", "doc1", "file1.txt", "text/plain", new byte[] { 1 });
        await _store.StoreAttachmentAsync("users", "doc1", "file2.txt", "text/plain", new byte[] { 2 });
        
        var list = await _store.ListAttachmentsAsync("users", "doc1");
        
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task DeleteAttachmentAsync_ShouldDeleteAttachment()
    {
        var doc = new Document 
        { 
            Id = "doc1", 
            Data = new Dictionary<string, object> { ["name"] = "Test" } 
        };
        await _store.InsertAsync("users", doc);
        await _store.StoreAttachmentAsync("users", "doc1", "file.txt", "text/plain", new byte[] { 1, 2, 3 });
        
        var deleted = await _store.DeleteAttachmentAsync("users", "doc1", "file.txt");
        
        Assert.True(deleted);
        Assert.Null(await _store.GetAttachmentAsync("users", "doc1", "file.txt"));
    }

    [Fact]
    public async Task HasAttachmentsAsync_WithAttachments_ShouldReturnTrue()
    {
        var doc = new Document 
        { 
            Id = "doc1", 
            Data = new Dictionary<string, object> { ["name"] = "Test" } 
        };
        await _store.InsertAsync("users", doc);
        await _store.StoreAttachmentAsync("users", "doc1", "file.txt", "text/plain", new byte[] { 1 });
        
        var hasAttachments = await _store.HasAttachmentsAsync("users", "doc1");
        
        Assert.True(hasAttachments);
    }

    [Fact]
    public async Task HasAttachmentsAsync_WithoutAttachments_ShouldReturnFalse()
    {
        var doc = new Document 
        { 
            Id = "doc1", 
            Data = new Dictionary<string, object> { ["name"] = "Test" } 
        };
        await _store.InsertAsync("users", doc);
        
        var hasAttachments = await _store.HasAttachmentsAsync("users", "doc1");
        
        Assert.False(hasAttachments);
    }

    [Fact]
    public async Task GetAttachmentCountAsync_ShouldReturnCorrectCount()
    {
        var doc = new Document 
        { 
            Id = "doc1", 
            Data = new Dictionary<string, object> { ["name"] = "Test" } 
        };
        await _store.InsertAsync("users", doc);
        await _store.StoreAttachmentAsync("users", "doc1", "file1.txt", "text/plain", new byte[] { 1 });
        await _store.StoreAttachmentAsync("users", "doc1", "file2.txt", "text/plain", new byte[] { 2 });
        await _store.StoreAttachmentAsync("users", "doc1", "file3.txt", "text/plain", new byte[] { 3 });
        
        var count = await _store.GetAttachmentCountAsync("users", "doc1");
        
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task GetAttachmentStorageSizeAsync_ShouldReturnTotalSize()
    {
        var doc = new Document 
        { 
            Id = "doc1", 
            Data = new Dictionary<string, object> { ["name"] = "Test" } 
        };
        await _store.InsertAsync("users", doc);
        await _store.StoreAttachmentAsync("users", "doc1", "file1.txt", "text/plain", new byte[] { 1, 2, 3 });
        await _store.StoreAttachmentAsync("users", "doc1", "file2.txt", "text/plain", new byte[] { 4, 5 });
        
        var size = await _store.GetAttachmentStorageSizeAsync();
        
        Assert.Equal(5, size);
    }

    #endregion

    #region Extension Method Tests

    [Fact]
    public void WithAttachments_WithOptions_ShouldCreateAttachmentEnabledStore()
    {
        var docStore = new DocumentStore();
        var options = new AttachmentStoreOptions { BasePath = Path.Combine(_testBasePath, "ext_test") };
        
        var store = docStore.WithAttachments(options);
        
        Assert.NotNull(store);
        Assert.IsType<AttachmentEnabledDocumentStore>(store);
        store.Dispose();
    }

    [Fact]
    public void WithAttachments_WithExistingStore_ShouldCreateAttachmentEnabledStore()
    {
        var docStore = new DocumentStore();
        var attStore = new AttachmentStore(new AttachmentStoreOptions { BasePath = Path.Combine(_testBasePath, "ext_test2") });
        
        var store = docStore.WithAttachments(attStore);
        
        Assert.NotNull(store);
        Assert.IsType<AttachmentEnabledDocumentStore>(store);
        store.Dispose();
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task FullWorkflow_StoreDocumentWithAttachments_Retrieve_Delete()
    {
        // Insert document
        var doc = new Document 
        { 
            Id = "user123", 
            Data = new Dictionary<string, object> 
            { 
                ["name"] = "John Doe",
                ["email"] = "john@example.com"
            } 
        };
        await _store.InsertAsync("users", doc);
        
        // Add attachments
        await _store.StoreAttachmentAsync("users", "user123", "avatar.png", "image/png", 
            new byte[] { 0x89, 0x50, 0x4E, 0x47 }); // PNG header
        await _store.StoreAttachmentAsync("users", "user123", "resume.pdf", "application/pdf", 
            new byte[] { 0x25, 0x50, 0x44, 0x46 }); // PDF header
        
        // Verify document
        var retrievedDoc = await _store.GetAsync("users", "user123");
        Assert.NotNull(retrievedDoc);
        Assert.Equal("John Doe", retrievedDoc.Data!["name"]);
        
        // Verify attachments
        var attachments = await _store.ListAttachmentsAsync("users", "user123");
        Assert.Equal(2, attachments.Count);
        Assert.Contains(attachments, a => a.Name == "avatar.png");
        Assert.Contains(attachments, a => a.Name == "resume.pdf");
        
        // Verify has attachments
        Assert.True(await _store.HasAttachmentsAsync("users", "user123"));
        Assert.Equal(2, await _store.GetAttachmentCountAsync("users", "user123"));
        
        // Retrieve specific attachment
        var avatar = await _store.GetAttachmentAsync("users", "user123", "avatar.png");
        Assert.NotNull(avatar);
        Assert.Equal("image/png", avatar.ContentType);
        Assert.Equal(4, avatar.Size);
        
        // Delete attachment
        await _store.DeleteAttachmentAsync("users", "user123", "avatar.png");
        Assert.Single(await _store.ListAttachmentsAsync("users", "user123"));
        
        // Delete document (should cascade delete remaining attachments)
        await _store.DeleteAsync("users", "user123");
        Assert.Empty(await _store.ListAttachmentsAsync("users", "user123"));
    }

    [Fact]
    public async Task MultipleDocuments_SameCollection_ShouldBeIndependent()
    {
        // Create two documents
        var doc1 = new Document { Id = "doc1", Data = new Dictionary<string, object> { ["name"] = "Doc1" } };
        var doc2 = new Document { Id = "doc2", Data = new Dictionary<string, object> { ["name"] = "Doc2" } };
        await _store.InsertAsync("items", doc1);
        await _store.InsertAsync("items", doc2);
        
        // Add attachments to each
        await _store.StoreAttachmentAsync("items", "doc1", "file.txt", "text/plain", new byte[] { 1 });
        await _store.StoreAttachmentAsync("items", "doc2", "file.txt", "text/plain", new byte[] { 2 });
        
        // Verify independence
        var attachments1 = await _store.ListAttachmentsAsync("items", "doc1");
        var attachments2 = await _store.ListAttachmentsAsync("items", "doc2");
        
        Assert.Single(attachments1);
        Assert.Single(attachments2);
        Assert.NotEqual(attachments1[0].Hash, attachments2[0].Hash);
        
        // Delete one document
        await _store.DeleteAsync("items", "doc1");
        
        // Verify other document still has attachment
        Assert.Empty(await _store.ListAttachmentsAsync("items", "doc1"));
        Assert.Single(await _store.ListAttachmentsAsync("items", "doc2"));
    }

    #endregion

    #region IDisposable Tests

    [Fact]
    public void Dispose_ShouldDisposeBothStores()
    {
        var docStore = new DocumentStore();
        var attStore = new AttachmentStore(new AttachmentStoreOptions { BasePath = Path.Combine(_testBasePath, "dispose") });
        var store = new AttachmentEnabledDocumentStore(docStore, attStore);
        
        store.Dispose();
        
        // Should be able to dispose multiple times
        store.Dispose();
    }

    [Fact]
    public async Task Operations_AfterDispose_ShouldThrow()
    {
        var docStore = new DocumentStore();
        var attStore = new AttachmentStore(new AttachmentStoreOptions { BasePath = Path.Combine(_testBasePath, "dispose2") });
        var store = new AttachmentEnabledDocumentStore(docStore, attStore);
        store.Dispose();
        
        await Assert.ThrowsAsync<ObjectDisposedException>(() => 
            store.InsertAsync("users", new Document { Id = "doc1", Data = new Dictionary<string, object>() }));
    }

    #endregion
}
