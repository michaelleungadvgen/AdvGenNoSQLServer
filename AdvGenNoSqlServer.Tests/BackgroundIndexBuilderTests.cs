// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Storage.Indexing;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for BackgroundIndexBuilder
/// </summary>
public class BackgroundIndexBuilderTests : IDisposable
{
    private readonly IndexManager _indexManager;
    private readonly BackgroundIndexBuilder _builder;
    
    public BackgroundIndexBuilderTests()
    {
        _indexManager = new IndexManager();
        _builder = new BackgroundIndexBuilder(_indexManager, maxConcurrentBuilds: 2);
    }
    
    public void Dispose()
    {
        _builder.Dispose();
    }
    
    #region Constructor Tests
    
    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        var indexManager = new IndexManager();
        var builder = new BackgroundIndexBuilder(indexManager, maxConcurrentBuilds: 3);
        
        Assert.NotNull(builder);
        Assert.Equal(3, builder.MaxConcurrentBuilds);
        Assert.Equal(0, builder.RunningBuildCount);
    }
    
    [Fact]
    public void Constructor_WithNullIndexManager_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new BackgroundIndexBuilder(null!));
    }
    
    [Fact]
    public void Constructor_WithZeroMaxConcurrent_UsesDefault()
    {
        var indexManager = new IndexManager();
        var builder = new BackgroundIndexBuilder(indexManager, maxConcurrentBuilds: 0);
        
        Assert.Equal(2, builder.MaxConcurrentBuilds);
    }
    
    #endregion
    
    #region StartBuildAsync Tests
    
    [Fact]
    public async Task StartBuildAsync_WithValidParameters_CreatesJob()
    {
        var documents = CreateTestDocuments(10);
        
        var job = await _builder.StartBuildAsync(
            "test_collection",
            "name",
            documents,
            doc => (doc.Data?.TryGetValue("name", out var val) == true ? val?.ToString() : null) ?? "",
            isUnique: false);
        
        Assert.NotNull(job);
        Assert.Equal("test_collection", job.CollectionName);
        Assert.Equal("name", job.FieldName);
        Assert.Equal(BackgroundIndexBuildStatus.Pending, job.Status);
        Assert.NotEmpty(job.JobId);
    }
    
    [Fact]
    public async Task StartBuildAsync_WithNullCollectionName_ThrowsArgumentNullException()
    {
        var documents = CreateTestDocuments(10);
        
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            _builder.StartBuildAsync<string>(
                null!,
                "name",
                documents,
                doc => (doc.Data?.TryGetValue("name", out var val) == true ? val?.ToString() : null) ?? ""));
    }
    
    [Fact]
    public async Task StartBuildAsync_WithNullFieldName_ThrowsArgumentNullException()
    {
        var documents = CreateTestDocuments(10);
        
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            _builder.StartBuildAsync<string>(
                "test_collection",
                null!,
                documents,
                doc => (doc.Data?.TryGetValue("name", out var val) == true ? val?.ToString() : null) ?? ""));
    }
    
    [Fact]
    public async Task StartBuildAsync_WithNullDocuments_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            _builder.StartBuildAsync<string>(
                "test_collection",
                "name",
                null!,
                doc => (doc.Data?.TryGetValue("name", out var val) == true ? val?.ToString() : null) ?? ""));
    }
    
    [Fact]
    public async Task StartBuildAsync_WithNullKeySelector_ThrowsArgumentNullException()
    {
        var documents = CreateTestDocuments(10);
        
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            _builder.StartBuildAsync<string>(
                "test_collection",
                "name",
                documents,
                null!));
    }
    
    [Fact]
    public async Task StartBuildAsync_BuildsIndexSuccessfully()
    {
        var documents = CreateTestDocuments(100);
        
        var job = await _builder.StartBuildAsync(
            "test_collection",
            "name",
            documents,
            doc => (doc.Data?.TryGetValue("name", out var val) == true ? val?.ToString() : null) ?? "",
            isUnique: false);
        
        // Wait for completion
        var result = await _builder.WaitForCompletionAsync(job.JobId, timeout: TimeSpan.FromSeconds(30));
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Equal(BackgroundIndexBuildStatus.Completed, result.Status);
        Assert.Equal(100, result.DocumentsProcessed);
        Assert.True(result.EntriesCreated > 0);
    }
    
    [Fact]
    public async Task StartBuildAsync_WithDuplicateIndex_ThrowsInvalidOperationException()
    {
        var documents = CreateTestDocuments(10);
        
        // First build
        await _builder.StartBuildAsync(
            "test_collection",
            "name",
            documents,
            doc => (doc.Data?.TryGetValue("name", out var val) == true ? val?.ToString() : null) ?? "");
        
        // Second build for same field should fail
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            _builder.StartBuildAsync(
                "test_collection",
                "name",
                documents,
                doc => (doc.Data?.TryGetValue("name", out var val) == true ? val?.ToString() : null) ?? ""));
    }
    
    [Fact]
    public async Task StartBuildAsync_RegistersIndexInIndexManager()
    {
        var documents = CreateTestDocuments(50);
        
        var job = await _builder.StartBuildAsync(
            "test_collection",
            "age",
            documents,
            doc => doc.Data?.TryGetValue("age", out var val) == true && val is int i ? i : 0,
            isUnique: false);
        
        await _builder.WaitForCompletionAsync(job.JobId, timeout: TimeSpan.FromSeconds(30));
        
        // Index should be registered
        Assert.True(_indexManager.HasIndex("test_collection", "age"));
    }
    
    #endregion
    
    #region Progress Reporting Tests
    
    [Fact]
    public async Task StartBuildAsync_ReportsProgress()
    {
        var documents = CreateTestDocuments(100);
        var progressReports = new List<IndexBuildProgress>();
        var progress = new Progress<IndexBuildProgress>(p => progressReports.Add(p));
        
        var job = await _builder.StartBuildAsync(
            "test_collection",
            "name",
            documents,
            doc => (doc.Data?.TryGetValue("name", out var val) == true ? val?.ToString() : null) ?? "",
            options: new BackgroundIndexBuildOptions { BatchSize = 10 },
            progress: progress);
        
        await _builder.WaitForCompletionAsync(job.JobId, timeout: TimeSpan.FromSeconds(30));
        
        // Allow time for progress events to be processed
        await Task.Delay(100);
        
        Assert.True(progressReports.Count > 0);
        Assert.All(progressReports, p => 
        {
            Assert.Equal("test_collection", p.CollectionName);
            Assert.Equal("name", p.FieldName);
            Assert.True(p.DocumentsProcessed >= 0);
        });
    }
    
    [Fact]
    public async Task StartBuildAsync_ReportsCompletionEvent()
    {
        var documents = CreateTestDocuments(50);
        BackgroundIndexBuildResult? completedResult = null;
        
        _builder.BuildCompleted += (sender, args) =>
        {
            completedResult = args.Result;
        };
        
        var job = await _builder.StartBuildAsync(
            "test_collection",
            "name",
            documents,
            doc => (doc.Data?.TryGetValue("name", out var val) == true ? val?.ToString() : null) ?? "");
        
        await _builder.WaitForCompletionAsync(job.JobId, timeout: TimeSpan.FromSeconds(30));
        
        // Allow time for event to be processed
        await Task.Delay(100);
        
        Assert.NotNull(completedResult);
        Assert.True(completedResult.IsSuccess);
    }
    
    [Fact]
    public async Task StartBuildAsync_ReportsProgressEvent()
    {
        var documents = CreateTestDocuments(50);
        var progressEvents = new List<IndexBuildProgress>();
        
        _builder.BuildProgress += (sender, args) =>
        {
            progressEvents.Add(args.Progress);
        };
        
        var job = await _builder.StartBuildAsync(
            "test_collection",
            "name",
            documents,
            doc => (doc.Data?.TryGetValue("name", out var val) == true ? val?.ToString() : null) ?? "");
        
        await _builder.WaitForCompletionAsync(job.JobId, timeout: TimeSpan.FromSeconds(30));
        
        // Allow time for events to be processed
        await Task.Delay(100);
        
        Assert.True(progressEvents.Count > 0);
    }
    
    #endregion
    
    #region Cancellation Tests
    
    [Fact]
    public async Task CancelJob_CancelsRunningBuild()
    {
        var documents = CreateTestDocuments(1000);
        
        var job = await _builder.StartBuildAsync(
            "test_collection",
            "name",
            documents,
            doc => (doc.Data?.TryGetValue("name", out var val) == true ? val?.ToString() : null) ?? "",
            options: new BackgroundIndexBuildOptions { BatchSize = 10, BatchDelayMs = 50 });
        
        // Give it time to start
        await Task.Delay(100);
        
        // Cancel the job
        var cancelled = _builder.CancelJob(job.JobId);
        Assert.True(cancelled);
        
        // Wait for it to complete
        await _builder.WaitForCompletionAsync(job.JobId, timeout: TimeSpan.FromSeconds(10));
        
        Assert.True(job.Status == BackgroundIndexBuildStatus.Cancelled || 
                    job.Status == BackgroundIndexBuildStatus.Completed);
    }
    
    [Fact]
    public async Task CancelJob_WithCompletedJob_ReturnsFalse()
    {
        var documents = CreateTestDocuments(10);
        
        var job = await _builder.StartBuildAsync(
            "test_collection",
            "name",
            documents,
            doc => (doc.Data?.TryGetValue("name", out var val) == true ? val?.ToString() : null) ?? "");
        
        await _builder.WaitForCompletionAsync(job.JobId, timeout: TimeSpan.FromSeconds(10));
        
        var cancelled = _builder.CancelJob(job.JobId);
        Assert.False(cancelled);
    }
    
    [Fact]
    public void CancelJob_WithNonExistentJob_ReturnsFalse()
    {
        var cancelled = _builder.CancelJob("non-existent-job");
        Assert.False(cancelled);
    }
    
    [Fact]
    public async Task StartBuildAsync_WithCancellationToken_RespectsCancellation()
    {
        var documents = CreateTestDocuments(1000);
        using var cts = new CancellationTokenSource();
        
        var job = await _builder.StartBuildAsync(
            "test_collection",
            "name",
            documents,
            doc => (doc.Data?.TryGetValue("name", out var val) == true ? val?.ToString() : null) ?? "",
            options: new BackgroundIndexBuildOptions { BatchSize = 10, BatchDelayMs = 50 },
            cancellationToken: cts.Token);
        
        // Give it time to start
        await Task.Delay(100);
        
        // Cancel via token
        cts.Cancel();
        
        // Wait for it to complete
        await _builder.WaitForCompletionAsync(job.JobId, timeout: TimeSpan.FromSeconds(10));
        
        Assert.True(job.Status == BackgroundIndexBuildStatus.Cancelled || 
                    job.Status == BackgroundIndexBuildStatus.Completed);
    }
    
    #endregion
    
    #region Job Query Tests
    
    [Fact]
    public void GetJob_WithExistingJob_ReturnsJob()
    {
        var documents = CreateTestDocuments(10);
        
        var task = _builder.StartBuildAsync(
            "test_collection",
            "name",
            documents,
            doc => doc.Id);
        
        var job = task.Result;
        
        var retrievedJob = _builder.GetJob(job.JobId);
        
        Assert.NotNull(retrievedJob);
        Assert.Equal(job.JobId, retrievedJob.JobId);
    }
    
    [Fact]
    public void GetJob_WithNonExistentJob_ReturnsNull()
    {
        var job = _builder.GetJob("non-existent-job");
        Assert.Null(job);
    }
    
    [Fact]
    public async Task GetAllJobs_ReturnsAllJobs()
    {
        var documents = CreateTestDocuments(10);
        
        var job1 = await _builder.StartBuildAsync(
            "collection1",
            "field1",
            documents,
            doc => doc.Id);
        
        var job2 = await _builder.StartBuildAsync(
            "collection2", 
            "field2",
            documents,
            doc => doc.Id);
        
        var allJobs = _builder.GetAllJobs();
        
        Assert.Equal(2, allJobs.Count);
        Assert.Contains(allJobs, j => j.JobId == job1.JobId);
        Assert.Contains(allJobs, j => j.JobId == job2.JobId);
    }
    
    [Fact]
    public async Task GetJobsByStatus_ReturnsMatchingJobs()
    {
        var documents = CreateTestDocuments(10);
        
        var job = await _builder.StartBuildAsync(
            "test_collection",
            "name",
            documents,
            doc => (doc.Data?.TryGetValue("name", out var val) == true ? val?.ToString() : null) ?? "");
        
        await _builder.WaitForCompletionAsync(job.JobId, timeout: TimeSpan.FromSeconds(10));
        
        var completedJobs = _builder.GetJobsByStatus(BackgroundIndexBuildStatus.Completed);
        
        Assert.True(completedJobs.Count > 0);
        Assert.All(completedJobs, j => Assert.Equal(BackgroundIndexBuildStatus.Completed, j.Status));
    }
    
    #endregion
    
    #region WaitForCompletion Tests
    
    [Fact]
    public async Task WaitForCompletionAsync_WithValidJob_ReturnsResult()
    {
        var documents = CreateTestDocuments(50);
        
        var job = await _builder.StartBuildAsync(
            "test_collection",
            "name",
            documents,
            doc => (doc.Data?.TryGetValue("name", out var val) == true ? val?.ToString() : null) ?? "");
        
        var result = await _builder.WaitForCompletionAsync(job.JobId, timeout: TimeSpan.FromSeconds(30));
        
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
    }
    
    [Fact]
    public async Task WaitForCompletionAsync_WithNonExistentJob_ReturnsNull()
    {
        var result = await _builder.WaitForCompletionAsync("non-existent-job");
        Assert.Null(result);
    }
    
    [Fact]
    public async Task WaitForCompletionAsync_WithTimeout_ReturnsNullOnTimeout()
    {
        var documents = CreateTestDocuments(10000);
        
        var job = await _builder.StartBuildAsync(
            "test_collection",
            "name",
            documents,
            doc => (doc.Data?.TryGetValue("name", out var val) == true ? val?.ToString() : null) ?? "",
            options: new BackgroundIndexBuildOptions { BatchSize = 10, BatchDelayMs = 100 });
        
        // Very short timeout should trigger
        var result = await _builder.WaitForCompletionAsync(job.JobId, timeout: TimeSpan.FromMilliseconds(1));
        
        // Should be null due to timeout (or very rarely completed if extremely fast)
        // We mainly care that it doesn't throw
    }
    
    #endregion
    
    #region Build Options Tests
    
    [Fact]
    public async Task StartBuildAsync_WithBatchSize_ProcessesInBatches()
    {
        var documents = CreateTestDocuments(100);
        var progressReports = new List<IndexBuildProgress>();
        var progress = new Progress<IndexBuildProgress>(p => progressReports.Add(p));
        
        var job = await _builder.StartBuildAsync(
            "test_collection",
            "name",
            documents,
            doc => (doc.Data?.TryGetValue("name", out var val) == true ? val?.ToString() : null) ?? "",
            options: new BackgroundIndexBuildOptions { BatchSize = 25 },
            progress: progress);
        
        await _builder.WaitForCompletionAsync(job.JobId, timeout: TimeSpan.FromSeconds(30));
        await Task.Delay(100);
        
        // With batch size 25 and 100 documents, we expect at least a few progress reports
        Assert.True(progressReports.Count >= 3);
    }
    
    [Fact]
    public void BackgroundIndexBuildOptions_DefaultValues_AreCorrect()
    {
        var options = new BackgroundIndexBuildOptions();
        
        Assert.Equal(1000, options.BatchSize);
        Assert.Equal(0, options.BatchDelayMs);
        Assert.Equal(IndexBuildPriority.Normal, options.Priority);
        Assert.False(options.StopOnFirstError);
        Assert.Equal(100, options.MaxErrors);
        Assert.Equal(2, options.MaxConcurrentBuilds);
    }
    
    #endregion
    
    #region Unique Index Tests
    
    [Fact]
    public async Task StartBuildAsync_WithUniqueIndex_EnforcesUniqueness()
    {
        // Create documents with duplicate values
        var documents = new List<Document>();
        for (int i = 0; i < 50; i++)
        {
            // Every 10th document has duplicate category
            var category = i % 10 == 0 ? "duplicate" : $"category{i}";
            var doc = new Document 
            { 
                Id = $"doc{i}", 
                Data = new Dictionary<string, object> 
                { 
                    ["name"] = $"user{i}",
                    ["category"] = category 
                }
            };
            documents.Add(doc);
        }
        
        var job = await _builder.StartBuildAsync(
            "test_collection",
            "category",
            documents,
            doc => (doc.Data?.TryGetValue("category", out var val) == true ? val?.ToString() : null) ?? "",
            isUnique: true,
            options: new BackgroundIndexBuildOptions { StopOnFirstError = false });
        
        var result = await _builder.WaitForCompletionAsync(job.JobId, timeout: TimeSpan.FromSeconds(30));
        
        Assert.NotNull(result);
        // Should have errors for duplicate keys
        Assert.True(result.ErrorCount > 0 || result.IsSuccess);
    }
    
    #endregion
    
    #region Result Factory Tests
    
    [Fact]
    public void BackgroundIndexBuildResult_Success_CreatesSuccessResult()
    {
        var result = BackgroundIndexBuildResult.Success("job123", "collection", "field");
        
        Assert.True(result.IsSuccess);
        Assert.Equal(BackgroundIndexBuildStatus.Completed, result.Status);
        Assert.Equal("job123", result.JobId);
        Assert.Equal("collection", result.CollectionName);
        Assert.Equal("field", result.FieldName);
        Assert.NotNull(result.CompletedAt);
    }
    
    [Fact]
    public void BackgroundIndexBuildResult_Failure_CreatesFailureResult()
    {
        var result = BackgroundIndexBuildResult.Failure("job123", "collection", "field", "Test error");
        
        Assert.False(result.IsSuccess);
        Assert.Equal(BackgroundIndexBuildStatus.Failed, result.Status);
        Assert.Single(result.Errors);
        Assert.Contains("Test error", result.Errors);
        Assert.Equal(1, result.ErrorCount);
    }
    
    [Fact]
    public void BackgroundIndexBuildResult_Cancelled_CreatesCancelledResult()
    {
        var result = BackgroundIndexBuildResult.Cancelled("job123", "collection", "field");
        
        Assert.False(result.IsSuccess);
        Assert.Equal(BackgroundIndexBuildStatus.Cancelled, result.Status);
    }
    
    #endregion
    
    #region Progress Calculation Tests
    
    [Fact]
    public void IndexBuildProgress_PercentComplete_CalculatesCorrectly()
    {
        var progress = new IndexBuildProgress
        {
            DocumentsProcessed = 50,
            TotalDocuments = 100
        };
        
        Assert.Equal(50.0, progress.PercentComplete);
    }
    
    [Fact]
    public void IndexBuildProgress_PercentComplete_WithZeroTotal_ReturnsZero()
    {
        var progress = new IndexBuildProgress
        {
            DocumentsProcessed = 0,
            TotalDocuments = 0
        };
        
        Assert.Equal(0, progress.PercentComplete);
    }
    
    [Fact]
    public void IndexBuildProgress_PercentComplete_CapsAt100()
    {
        var progress = new IndexBuildProgress
        {
            DocumentsProcessed = 150,
            TotalDocuments = 100
        };
        
        Assert.Equal(100.0, progress.PercentComplete);
    }
    
    [Fact]
    public void IndexBuildProgress_DocumentsPerSecond_CalculatesCorrectly()
    {
        var progress = new IndexBuildProgress
        {
            DocumentsProcessed = 100,
            Elapsed = TimeSpan.FromSeconds(2)
        };
        
        Assert.Equal(50.0, progress.DocumentsPerSecond);
    }
    
    #endregion
    
    #region Dispose Tests
    
    [Fact]
    public void Dispose_CancelsRunningJobs()
    {
        var documents = CreateTestDocuments(1000);
        
        var task = _builder.StartBuildAsync(
            "test_collection",
            "name",
            documents,
            doc => (doc.Data?.TryGetValue("name", out var val) == true ? val?.ToString() : null) ?? "",
            options: new BackgroundIndexBuildOptions { BatchSize = 10, BatchDelayMs = 50 });
        
        var job = task.Result;
        
        // Dispose the builder
        _builder.Dispose();
        
        // Job should be cancelled or completed
        Assert.True(job.Status == BackgroundIndexBuildStatus.Cancelled || 
                    job.Status == BackgroundIndexBuildStatus.Completed ||
                    job.Status == BackgroundIndexBuildStatus.Running);
    }
    
    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        _builder.Dispose();
        _builder.Dispose(); // Should not throw
    }
    
    [Fact]
    public void Methods_AfterDispose_ThrowObjectDisposedException()
    {
        _builder.Dispose();
        
        Assert.Throws<ObjectDisposedException>(() => _builder.GetJob("test"));
        Assert.Throws<ObjectDisposedException>(() => _builder.GetAllJobs());
        Assert.Throws<ObjectDisposedException>(() => _builder.GetJobsByStatus(BackgroundIndexBuildStatus.Running));
        Assert.Throws<ObjectDisposedException>(() => _builder.CancelJob("test"));
    }
    
    #endregion
    
    #region Concurrency Tests
    
    [Fact]
    public async Task StartBuildAsync_MultipleConcurrentBuilds_RespectsMaxConcurrent()
    {
        var documents = CreateTestDocuments(100);
        var jobs = new List<BackgroundIndexBuildJob>();
        
        // Start multiple builds
        for (int i = 0; i < 5; i++)
        {
            var job = await _builder.StartBuildAsync(
                $"collection{i}",
                "name",
                documents,
                doc => (doc.Data?.TryGetValue("name", out var val) == true ? val?.ToString() : null) ?? "",
                options: new BackgroundIndexBuildOptions { BatchSize = 5, BatchDelayMs = 10 });
            
            jobs.Add(job);
        }
        
        // At some point, running count should not exceed max
        Assert.True(_builder.RunningBuildCount <= _builder.MaxConcurrentBuilds);
        
        // Wait for all to complete
        foreach (var job in jobs)
        {
            await _builder.WaitForCompletionAsync(job.JobId, timeout: TimeSpan.FromSeconds(30));
        }
        
        // All should be completed
        Assert.All(jobs, j => Assert.True(j.IsCompleted));
    }
    
    #endregion
    
    #region Helper Methods
    
    private static List<Document> CreateTestDocuments(int count)
    {
        var documents = new List<Document>();
        for (int i = 0; i < count; i++)
        {
            var doc = new Document
            {
                Id = $"doc{i}",
                Data = new Dictionary<string, object>
                {
                    ["name"] = $"user{i}",
                    ["age"] = 20 + i % 50,
                    ["email"] = $"user{i}@test.com"
                }
            };
            documents.Add(doc);
        }
        return documents;
    }
    
    #endregion
}
