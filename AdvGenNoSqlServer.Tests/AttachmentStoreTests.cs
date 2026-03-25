// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Attachments;
using AdvGenNoSqlServer.Storage;
using AdvGenNoSqlServer.Storage.Attachments;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for AttachmentStore and AttachmentEnabledDocumentStore
/// </summary>
public class AttachmentStoreTests : IDisposable
{
    private readonly string _testBasePath;
    private readonly AttachmentStoreOptions _options;
    private readonly AttachmentStore _store;

    public AttachmentStoreTests()
    {
        _testBasePath = Path.Combine(Path.GetTempPath(), $"AttachmentTests_{Guid.NewGuid()}");
        _options = new AttachmentStoreOptions
        {
            BasePath = _testBasePath,
            MaxAttachmentSize = 10 * 1024 * 1024, // 10MB for tests
            MaxTotalStorage = 100 * 1024 * 1024 // 100MB for tests
        };
        _store = new AttachmentStore(_options);
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
    public void Constructor_WithValidOptions_ShouldCreateStore()
    {
        var options = new AttachmentStoreOptions { BasePath = Path.Combine(_testBasePath, "new") };
        var store = new AttachmentStore(options);
        Assert.NotNull(store);
        Assert.True(Directory.Exists(options.BasePath));
        store.Dispose();
    }

    [Fact]
    public void Constructor_WithNullOptions_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(() => new AttachmentStore(null!));
    }

    [Fact]
    public void Constructor_WithEmptyBasePath_ShouldThrow()
    {
        var options = new AttachmentStoreOptions { BasePath = "" };
        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void Constructor_WithInvalidMaxSize_ShouldThrow()
    {
        var options = new AttachmentStoreOptions 
        { 
            BasePath = _testBasePath, 
            MaxAttachmentSize = 0 
        };
        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    #endregion

    #region StoreAsync Tests

    [Fact]
    public async Task StoreAsync_WithValidData_ShouldStoreAttachment()
    {
        var content = new byte[] { 1, 2, 3, 4, 5 };
        
        var result = await _store.StoreAsync("users", "doc1", "profile.png", "image/png", content);
        
        Assert.True(result.Success);
        Assert.NotNull(result.Info);
        Assert.Equal("profile.png", result.Info!.Name);
        Assert.Equal("image/png", result.Info.ContentType);
        Assert.Equal(5, result.Info.Size);
        Assert.NotEmpty(result.Info.Hash);
    }

    [Fact]
    public async Task StoreAsync_WithMetadata_ShouldStoreMetadata()
    {
        var content = new byte[] { 1, 2, 3 };
        var metadata = new Dictionary<string, string>
        {
            ["author"] = "test",
            ["version"] = "1.0"
        };
        
        var result = await _store.StoreAsync("users", "doc1", "file.txt", "text/plain", content, metadata);
        
        Assert.True(result.Success);
        Assert.NotNull(result.Info?.Metadata);
        Assert.Equal("test", result.Info.Metadata["author"]);
        Assert.Equal("1.0", result.Info.Metadata["version"]);
    }

    [Fact]
    public async Task StoreAsync_WithEmptyContent_ShouldStoreEmptyAttachment()
    {
        var content = Array.Empty<byte>();
        
        var result = await _store.StoreAsync("users", "doc1", "empty.txt", "text/plain", content);
        
        Assert.True(result.Success);
        Assert.Equal(0, result.Info!.Size);
    }

    [Fact]
    public async Task StoreAsync_WithBlockedContentType_ShouldFail()
    {
        var content = new byte[] { 1, 2, 3 };
        
        var result = await _store.StoreAsync("users", "doc1", "file.exe", "application/x-msdownload", content);
        
        Assert.False(result.Success);
        Assert.Contains("not allowed", result.ErrorMessage);
    }

    [Fact]
    public async Task StoreAsync_WithOversizedContent_ShouldFail()
    {
        var content = new byte[_options.MaxAttachmentSize + 1];
        
        var result = await _store.StoreAsync("users", "doc1", "large.bin", "application/octet-stream", content);
        
        Assert.False(result.Success);
        Assert.Contains("exceeds maximum", result.ErrorMessage);
    }

    [Fact]
    public async Task StoreAsync_WithSpecialCharactersInName_ShouldSanitize()
    {
        var content = new byte[] { 1, 2, 3 };
        
        var result = await _store.StoreAsync("users", "doc1", "file:name?.txt", "text/plain", content);
        
        Assert.True(result.Success);
    }

    [Fact]
    public async Task StoreAsync_UpdateExisting_ShouldOverwrite()
    {
        var content1 = new byte[] { 1, 2, 3 };
        var content2 = new byte[] { 4, 5, 6, 7 };
        
        await _store.StoreAsync("users", "doc1", "file.txt", "text/plain", content1);
        var result = await _store.StoreAsync("users", "doc1", "file.txt", "text/plain", content2);
        
        Assert.True(result.Success);
        Assert.Equal(4, result.Info!.Size);
        
        var retrieved = await _store.GetAsync("users", "doc1", "file.txt");
        Assert.Equal(content2, retrieved!.Content);
    }

    [Fact]
    public async Task StoreAsync_WithEmptyCollectionName_ShouldThrow()
    {
        var content = new byte[] { 1, 2, 3 };
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _store.StoreAsync("", "doc1", "file.txt", "text/plain", content));
    }

    [Fact]
    public async Task StoreAsync_WithEmptyDocumentId_ShouldThrow()
    {
        var content = new byte[] { 1, 2, 3 };
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _store.StoreAsync("users", "", "file.txt", "text/plain", content));
    }

    [Fact]
    public async Task StoreAsync_WithEmptyName_ShouldThrow()
    {
        var content = new byte[] { 1, 2, 3 };
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _store.StoreAsync("users", "doc1", "", "text/plain", content));
    }

    #endregion

    #region GetAsync Tests

    [Fact]
    public async Task GetAsync_WithExistingAttachment_ShouldReturnAttachment()
    {
        var content = new byte[] { 1, 2, 3, 4, 5 };
        await _store.StoreAsync("users", "doc1", "file.txt", "text/plain", content);
        
        var result = await _store.GetAsync("users", "doc1", "file.txt");
        
        Assert.NotNull(result);
        Assert.Equal("file.txt", result.Name);
        Assert.Equal("text/plain", result.ContentType);
        Assert.Equal(content, result.Content);
    }

    [Fact]
    public async Task GetAsync_WithNonExistingAttachment_ShouldReturnNull()
    {
        var result = await _store.GetAsync("users", "doc1", "nonexistent.txt");
        
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_WithNonExistingDocument_ShouldReturnNull()
    {
        var result = await _store.GetAsync("users", "nonexistent", "file.txt");
        
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_ShouldVerifyHash()
    {
        var content = new byte[] { 1, 2, 3, 4, 5 };
        await _store.StoreAsync("users", "doc1", "file.txt", "text/plain", content);
        
        var result = await _store.GetAsync("users", "doc1", "file.txt");
        
        Assert.NotNull(result);
        Assert.NotEmpty(result.Hash);
    }

    #endregion

    #region GetInfoAsync Tests

    [Fact]
    public async Task GetInfoAsync_WithExistingAttachment_ShouldReturnInfo()
    {
        var content = new byte[] { 1, 2, 3 };
        await _store.StoreAsync("users", "doc1", "file.txt", "text/plain", content);
        
        var info = await _store.GetInfoAsync("users", "doc1", "file.txt");
        
        Assert.NotNull(info);
        Assert.Equal("file.txt", info.Name);
        Assert.Equal(3, info.Size);
        Assert.IsType<AttachmentInfo>(info); // Should be AttachmentInfo without Content property
    }

    [Fact]
    public async Task GetInfoAsync_WithNonExistingAttachment_ShouldReturnNull()
    {
        var info = await _store.GetInfoAsync("users", "doc1", "nonexistent.txt");
        
        Assert.Null(info);
    }

    #endregion

    #region ListAsync Tests

    [Fact]
    public async Task ListAsync_WithAttachments_ShouldReturnAll()
    {
        await _store.StoreAsync("users", "doc1", "file1.txt", "text/plain", new byte[] { 1 });
        await _store.StoreAsync("users", "doc1", "file2.txt", "text/plain", new byte[] { 2 });
        await _store.StoreAsync("users", "doc1", "file3.txt", "text/plain", new byte[] { 3 });
        
        var list = await _store.ListAsync("users", "doc1");
        
        Assert.Equal(3, list.Count);
        Assert.Contains(list, a => a.Name == "file1.txt");
        Assert.Contains(list, a => a.Name == "file2.txt");
        Assert.Contains(list, a => a.Name == "file3.txt");
    }

    [Fact]
    public async Task ListAsync_WithNoAttachments_ShouldReturnEmpty()
    {
        var list = await _store.ListAsync("users", "doc1");
        
        Assert.Empty(list);
    }

    [Fact]
    public async Task ListAsync_DifferentDocuments_ShouldBeIsolated()
    {
        await _store.StoreAsync("users", "doc1", "file.txt", "text/plain", new byte[] { 1 });
        await _store.StoreAsync("users", "doc2", "file.txt", "text/plain", new byte[] { 2 });
        
        var list1 = await _store.ListAsync("users", "doc1");
        var list2 = await _store.ListAsync("users", "doc2");
        
        Assert.Single(list1);
        Assert.Single(list2);
    }

    [Fact]
    public async Task ListAsync_DifferentCollections_ShouldBeIsolated()
    {
        await _store.StoreAsync("col1", "doc1", "file.txt", "text/plain", new byte[] { 1 });
        await _store.StoreAsync("col2", "doc1", "file.txt", "text/plain", new byte[] { 2 });
        
        var list1 = await _store.ListAsync("col1", "doc1");
        var list2 = await _store.ListAsync("col2", "doc1");
        
        Assert.Single(list1);
        Assert.Single(list2);
    }

    #endregion

    #region ExistsAsync Tests

    [Fact]
    public async Task ExistsAsync_WithExistingAttachment_ShouldReturnTrue()
    {
        await _store.StoreAsync("users", "doc1", "file.txt", "text/plain", new byte[] { 1 });
        
        var exists = await _store.ExistsAsync("users", "doc1", "file.txt");
        
        Assert.True(exists);
    }

    [Fact]
    public async Task ExistsAsync_WithNonExistingAttachment_ShouldReturnFalse()
    {
        var exists = await _store.ExistsAsync("users", "doc1", "nonexistent.txt");
        
        Assert.False(exists);
    }

    #endregion

    #region DeleteAsync Tests

    [Fact]
    public async Task DeleteAsync_WithExistingAttachment_ShouldDelete()
    {
        await _store.StoreAsync("users", "doc1", "file.txt", "text/plain", new byte[] { 1 });
        
        var deleted = await _store.DeleteAsync("users", "doc1", "file.txt");
        
        Assert.True(deleted);
        Assert.False(await _store.ExistsAsync("users", "doc1", "file.txt"));
    }

    [Fact]
    public async Task DeleteAsync_WithNonExistingAttachment_ShouldReturnFalse()
    {
        var deleted = await _store.DeleteAsync("users", "doc1", "nonexistent.txt");
        
        Assert.False(deleted);
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveFromList()
    {
        await _store.StoreAsync("users", "doc1", "file1.txt", "text/plain", new byte[] { 1 });
        await _store.StoreAsync("users", "doc1", "file2.txt", "text/plain", new byte[] { 2 });
        
        await _store.DeleteAsync("users", "doc1", "file1.txt");
        
        var list = await _store.ListAsync("users", "doc1");
        Assert.Single(list);
        Assert.Equal("file2.txt", list[0].Name);
    }

    [Fact]
    public async Task DeleteAsync_LastAttachment_ShouldRemoveMetadata()
    {
        await _store.StoreAsync("users", "doc1", "file.txt", "text/plain", new byte[] { 1 });
        
        await _store.DeleteAsync("users", "doc1", "file.txt");
        
        var docPath = Path.Combine(_testBasePath, "users", "doc1");
        Assert.False(Directory.Exists(docPath));
    }

    #endregion

    #region DeleteAllAsync Tests

    [Fact]
    public async Task DeleteAllAsync_WithAttachments_ShouldDeleteAll()
    {
        await _store.StoreAsync("users", "doc1", "file1.txt", "text/plain", new byte[] { 1 });
        await _store.StoreAsync("users", "doc1", "file2.txt", "text/plain", new byte[] { 2 });
        
        var count = await _store.DeleteAllAsync("users", "doc1");
        
        Assert.Equal(2, count);
        Assert.Empty(await _store.ListAsync("users", "doc1"));
    }

    [Fact]
    public async Task DeleteAllAsync_WithNoAttachments_ShouldReturnZero()
    {
        var count = await _store.DeleteAllAsync("users", "doc1");
        
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task DeleteAllAsync_ShouldRemoveDirectory()
    {
        await _store.StoreAsync("users", "doc1", "file.txt", "text/plain", new byte[] { 1 });
        
        await _store.DeleteAllAsync("users", "doc1");
        
        var docPath = Path.Combine(_testBasePath, "users", "doc1");
        Assert.False(Directory.Exists(docPath));
    }

    #endregion

    #region GetTotalStorageSizeAsync Tests

    [Fact]
    public async Task GetTotalStorageSizeAsync_WithNoAttachments_ShouldReturnZero()
    {
        var size = await _store.GetTotalStorageSizeAsync();
        
        Assert.Equal(0, size);
    }

    [Fact]
    public async Task GetTotalStorageSizeAsync_WithAttachments_ShouldReturnTotalSize()
    {
        await _store.StoreAsync("users", "doc1", "file1.txt", "text/plain", new byte[] { 1, 2, 3 });
        await _store.StoreAsync("users", "doc2", "file2.txt", "text/plain", new byte[] { 4, 5, 6, 7, 8 });
        
        var size = await _store.GetTotalStorageSizeAsync();
        
        Assert.Equal(8, size); // 3 + 5 bytes
    }

    #endregion

    #region Concurrency Tests

    [Fact]
    public async Task StoreAsync_ConcurrentStores_ShouldSucceed()
    {
        var tasks = new List<Task>();
        for (int i = 0; i < 10; i++)
        {
            var index = i;
            tasks.Add(_store.StoreAsync("users", "doc1", $"file{index}.txt", "text/plain", new byte[] { (byte)index }));
        }
        
        await Task.WhenAll(tasks);
        
        var list = await _store.ListAsync("users", "doc1");
        Assert.Equal(10, list.Count);
    }

    [Fact]
    public async Task StoreAsync_ConcurrentSameFile_ShouldSucceed()
    {
        var tasks = new List<Task<AttachmentResult>>();
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(_store.StoreAsync("users", "doc1", "file.txt", "text/plain", new byte[] { (byte)i }));
        }
        
        await Task.WhenAll(tasks);
        
        // Last write should win
        var result = await _store.GetAsync("users", "doc1", "file.txt");
        Assert.NotNull(result);
        Assert.Single(await _store.ListAsync("users", "doc1"));
    }

    #endregion

    #region IDisposable Tests

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        var options = new AttachmentStoreOptions { BasePath = Path.Combine(_testBasePath, "dispose_test") };
        var store = new AttachmentStore(options);
        
        store.Dispose();
        
        // Should be able to dispose multiple times
        store.Dispose();
    }

    [Fact]
    public async Task Operations_AfterDispose_ShouldThrow()
    {
        var options = new AttachmentStoreOptions { BasePath = Path.Combine(_testBasePath, "dispose_test2") };
        var store = new AttachmentStore(options);
        store.Dispose();
        
        await Assert.ThrowsAsync<ObjectDisposedException>(() => 
            store.StoreAsync("users", "doc1", "file.txt", "text/plain", new byte[] { 1 }));
    }

    #endregion
}
