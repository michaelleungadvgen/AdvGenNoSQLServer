// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;
using System.Diagnostics;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Storage.Indexing;

/// <summary>
/// Background index builder for non-blocking index creation on large collections
/// </summary>
public class BackgroundIndexBuilder : IBackgroundIndexBuilder, IDisposable
{
    private readonly ConcurrentDictionary<string, BackgroundIndexBuildJob> _jobs = new();
    private readonly ConcurrentDictionary<string, string> _activeBuilds = new(); // Key: collection_field, Value: jobId
    private readonly SemaphoreSlim _concurrencySemaphore;
    private readonly IndexManager _indexManager;
    private bool _disposed;
    
    /// <summary>
    /// Event raised when build progress is updated
    /// </summary>
    public event EventHandler<IndexBuildProgressEventArgs>? BuildProgress;
    
    /// <summary>
    /// Event raised when a build completes
    /// </summary>
    public event EventHandler<IndexBuildCompletedEventArgs>? BuildCompleted;
    
    /// <summary>
    /// Maximum number of concurrent builds allowed
    /// </summary>
    public int MaxConcurrentBuilds { get; set; }
    
    /// <summary>
    /// Current number of running builds
    /// </summary>
    public int RunningBuildCount => _jobs.Values.Count(j => j.Status == BackgroundIndexBuildStatus.Running);
    
    /// <summary>
    /// Creates a new background index builder
    /// </summary>
    /// <param name="indexManager">Index manager for registering completed indexes</param>
    /// <param name="maxConcurrentBuilds">Maximum concurrent builds</param>
    public BackgroundIndexBuilder(IndexManager indexManager, int maxConcurrentBuilds = 2)
    {
        _indexManager = indexManager ?? throw new ArgumentNullException(nameof(indexManager));
        MaxConcurrentBuilds = maxConcurrentBuilds > 0 ? maxConcurrentBuilds : 2;
        _concurrencySemaphore = new SemaphoreSlim(MaxConcurrentBuilds, MaxConcurrentBuilds);
    }
    
    /// <summary>
    /// Starts a background index build for a collection field
    /// </summary>
    public async Task<BackgroundIndexBuildJob> StartBuildAsync<TKey>(
        string collectionName,
        string fieldName,
        IEnumerable<Document> documents,
        Func<Document, TKey> keySelector,
        bool isUnique = false,
        BackgroundIndexBuildOptions? options = null,
        IProgress<IndexBuildProgress>? progress = null,
        CancellationToken cancellationToken = default) where TKey : IComparable<TKey>
    {
        ThrowIfDisposed();
        
        ArgumentException.ThrowIfNullOrEmpty(collectionName, nameof(collectionName));
        ArgumentException.ThrowIfNullOrEmpty(fieldName, nameof(fieldName));
        ArgumentNullException.ThrowIfNull(documents, nameof(documents));
        ArgumentNullException.ThrowIfNull(keySelector, nameof(keySelector));
        
        options ??= new BackgroundIndexBuildOptions();
        
        // Check for duplicate active build
        var buildKey = $"{collectionName}_{fieldName}";
        if (_activeBuilds.ContainsKey(buildKey))
        {
            throw new InvalidOperationException($"A build job for '{collectionName}.{fieldName}' is already in progress.");
        }
        
        // Create the job
        var job = new BackgroundIndexBuildJob
        {
            CollectionName = collectionName,
            FieldName = fieldName,
            Options = options,
            Status = BackgroundIndexBuildStatus.Pending,
            CancellationTokenSource = new CancellationTokenSource(),
            Progress = new IndexBuildProgress
            {
                JobId = string.Empty, // Will be set after job creation
                CollectionName = collectionName,
                FieldName = fieldName,
                Status = BackgroundIndexBuildStatus.Pending
            }
        };
        
        // Generate job ID after creation
        job.JobId = GenerateJobId(collectionName, fieldName);
        job.Progress.JobId = job.JobId;
        
        // Link external cancellation token
        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() => job.CancellationTokenSource?.Cancel());
        }
        
        // Track active build and store the job
        if (!_activeBuilds.TryAdd(buildKey, job.JobId))
        {
            throw new InvalidOperationException($"A build job for '{collectionName}.{fieldName}' is already in progress.");
        }
        
        if (!_jobs.TryAdd(job.JobId, job))
        {
            // Shouldn't happen since job IDs are unique, but clean up if it does
            _activeBuilds.TryRemove(buildKey, out _);
            throw new InvalidOperationException($"Failed to create build job for '{collectionName}.{fieldName}'.");
        }
        
        // Start the build task
        job.BuildTask = Task.Run(async () =>
        {
            try
            {
                await ExecuteBuildAsync(job, documents, keySelector, isUnique, progress);
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation - don't mark as failed
                if (job.Status != BackgroundIndexBuildStatus.Cancelled)
                {
                    job.Status = BackgroundIndexBuildStatus.Cancelled;
                    job.Result ??= BackgroundIndexBuildResult.Cancelled(job.JobId, collectionName, fieldName);
                    job.CompletedAt = DateTime.UtcNow;
                    OnBuildCompleted(job.Result);
                }
            }
            catch (Exception ex)
            {
                job.Status = BackgroundIndexBuildStatus.Failed;
                job.Result ??= BackgroundIndexBuildResult.Failure(job.JobId, collectionName, fieldName, ex.Message);
                job.Result.Errors.Add(ex.ToString());
                job.CompletedAt = DateTime.UtcNow;
                
                OnBuildCompleted(job.Result);
            }
        }, job.CancellationTokenSource.Token);
        
        return job;
    }
    
    /// <summary>
    /// Executes the build operation
    /// </summary>
    private async Task ExecuteBuildAsync<TKey>(
        BackgroundIndexBuildJob job,
        IEnumerable<Document> documents,
        Func<Document, TKey> keySelector,
        bool isUnique,
        IProgress<IndexBuildProgress>? progress) where TKey : IComparable<TKey>
    {
        var stopwatch = Stopwatch.StartNew();
        job.StartedAt = DateTime.UtcNow;
        job.Status = BackgroundIndexBuildStatus.Running;
        job.Progress.Status = BackgroundIndexBuildStatus.Running;
        job.Progress.StartedAt = job.StartedAt.Value;
        
        var result = new BackgroundIndexBuildResult
        {
            JobId = job.JobId,
            CollectionName = job.CollectionName,
            FieldName = job.FieldName,
            Status = BackgroundIndexBuildStatus.Running,
            StartedAt = job.StartedAt.Value
        };
        
        // Wait for concurrency slot
        await _concurrencySemaphore.WaitAsync(job.CancellationTokenSource?.Token ?? CancellationToken.None);
        
        try
        {
            // Create the index structure first
            var index = _indexManager.CreateIndex(
                job.CollectionName,
                job.FieldName,
                isUnique,
                keySelector);
            
            // Convert to list for multiple enumeration
            var documentList = documents.ToList();
            long totalDocuments = documentList.Count;
            long processedCount = 0;
            long entriesCreated = 0;
            var errors = new List<string>();
            int errorCount = 0;
            
            job.Progress.TotalDocuments = totalDocuments;
            
            // Process documents in batches
            var batch = new List<Document>(job.Options.BatchSize);
            
            foreach (var document in documentList)
            {
                // Check for cancellation
                if (job.CancellationTokenSource?.Token.IsCancellationRequested == true)
                {
                    job.Status = BackgroundIndexBuildStatus.Cancelled;
                    result.Status = BackgroundIndexBuildStatus.Cancelled;
                    result.DocumentsProcessed = processedCount;
                    result.EntriesCreated = entriesCreated;
                    result.ErrorCount = errorCount;
                    result.Errors = errors;
                    result.CompletedAt = DateTime.UtcNow;
                    job.CompletedAt = result.CompletedAt;
                    job.Result = result;
                    
                    OnBuildCompleted(result);
                    return;
                }
                
                batch.Add(document);
                
                // Process batch when full
                if (batch.Count >= job.Options.BatchSize)
                {
                    var (batchEntries, batchErrors) = ProcessBatch(batch, index, keySelector, isUnique);
                    
                    entriesCreated += batchEntries;
                    errorCount += batchErrors.Count;
                    errors.AddRange(batchErrors);
                    
                    processedCount += batch.Count;
                    
                    // Update progress
                    job.Progress.DocumentsProcessed = processedCount;
                    job.Progress.ErrorCount = errorCount;
                    job.Progress.Elapsed = stopwatch.Elapsed;
                    
                    // Calculate estimated remaining time
                    if (processedCount > 0 && processedCount < totalDocuments)
                    {
                        var avgTimePerDoc = stopwatch.Elapsed.TotalSeconds / processedCount;
                        var remainingDocs = totalDocuments - processedCount;
                        job.Progress.EstimatedRemaining = TimeSpan.FromSeconds(avgTimePerDoc * remainingDocs);
                    }
                    
                    // Report progress
                    progress?.Report(job.Progress);
                    OnBuildProgress(job.Progress);
                    
                    // Check error limits
                    if (errorCount > 0 && job.Options.StopOnFirstError)
                    {
                        job.Status = BackgroundIndexBuildStatus.Failed;
                        result.Status = BackgroundIndexBuildStatus.Failed;
                        result.DocumentsProcessed = processedCount;
                        result.EntriesCreated = entriesCreated;
                        result.ErrorCount = errorCount;
                        result.Errors = errors;
                        result.CompletedAt = DateTime.UtcNow;
                        job.CompletedAt = result.CompletedAt;
                        job.Result = result;
                        
                        OnBuildCompleted(result);
                        return;
                    }
                    
                    if (job.Options.MaxErrors > 0 && errorCount >= job.Options.MaxErrors)
                    {
                        errors.Add($"Maximum error count ({job.Options.MaxErrors}) exceeded. Aborting.");
                        job.Status = BackgroundIndexBuildStatus.Failed;
                        result.Status = BackgroundIndexBuildStatus.Failed;
                        result.DocumentsProcessed = processedCount;
                        result.EntriesCreated = entriesCreated;
                        result.ErrorCount = errorCount;
                        result.Errors = errors;
                        result.CompletedAt = DateTime.UtcNow;
                        job.CompletedAt = result.CompletedAt;
                        job.Result = result;
                        
                        OnBuildCompleted(result);
                        return;
                    }
                    
                    // Clear batch
                    batch.Clear();
                    
                    // Apply throttling delay if configured
                    if (job.Options.BatchDelayMs > 0)
                    {
                        await Task.Delay(job.Options.BatchDelayMs, 
                            job.CancellationTokenSource?.Token ?? CancellationToken.None);
                    }
                    
                    // Yield to allow other operations
                    if (job.Options.Priority == IndexBuildPriority.Low)
                    {
                        await Task.Yield();
                    }
                }
            }
            
            // Process remaining documents in final batch
            if (batch.Count > 0)
            {
                var (batchEntries, batchErrors) = ProcessBatch(batch, index, keySelector, isUnique);
                
                entriesCreated += batchEntries;
                errorCount += batchErrors.Count;
                errors.AddRange(batchErrors);
                
                processedCount += batch.Count;
                
                // Update and report progress for final batch
                job.Progress.DocumentsProcessed = processedCount;
                job.Progress.ErrorCount = errorCount;
                job.Progress.Elapsed = stopwatch.Elapsed;
                progress?.Report(job.Progress);
                OnBuildProgress(job.Progress);
            }
            
            // Mark as completed
            stopwatch.Stop();
            
            job.Status = BackgroundIndexBuildStatus.Completed;
            result.Status = BackgroundIndexBuildStatus.Completed;
            result.DocumentsProcessed = processedCount;
            result.EntriesCreated = entriesCreated;
            result.ErrorCount = errorCount;
            result.Errors = errors;
            result.Duration = stopwatch.Elapsed;
            result.CompletedAt = DateTime.UtcNow;
            
            job.Progress.Status = BackgroundIndexBuildStatus.Completed;
            job.Progress.DocumentsProcessed = processedCount;
            job.Progress.Elapsed = stopwatch.Elapsed;
            job.CompletedAt = result.CompletedAt;
            job.Result = result;
            
            OnBuildCompleted(result);
        }
        finally
        {
            _concurrencySemaphore.Release();
            // Remove from active builds when done
            var buildKey = $"{job.CollectionName}_{job.FieldName}";
            _activeBuilds.TryRemove(buildKey, out _);
        }
    }
    
    /// <summary>
    /// Processes a batch of documents
    /// </summary>
    private (long Entries, List<string> Errors) ProcessBatch<TKey>(
        List<Document> batch,
        IBTreeIndex<TKey, string> index,
        Func<Document, TKey> keySelector,
        bool isUnique) where TKey : IComparable<TKey>
    {
        long entries = 0;
        var errors = new List<string>();
        
        foreach (var document in batch)
        {
            try
            {
                var key = keySelector(document);
                if (key != null)
                {
                    index.Insert(key, document.Id);
                    entries++;
                }
            }
            catch (DuplicateKeyException)
            {
                if (isUnique)
                {
                    errors.Add($"Duplicate key violation for document '{document.Id}'");
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Error indexing document '{document.Id}': {ex.Message}");
            }
        }
        
        return (entries, errors);
    }
    
    /// <summary>
    /// Generates a unique job ID
    /// </summary>
    private static string GenerateJobId(string collectionName, string fieldName)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var random = Guid.NewGuid().ToString("N")[..8];
        return $"{collectionName}_{fieldName}_{timestamp}_{random}";
    }
    
    /// <summary>
    /// Raises the BuildProgress event
    /// </summary>
    protected virtual void OnBuildProgress(IndexBuildProgress progress)
    {
        BuildProgress?.Invoke(this, new IndexBuildProgressEventArgs(progress));
    }
    
    /// <summary>
    /// Raises the BuildCompleted event
    /// </summary>
    protected virtual void OnBuildCompleted(BackgroundIndexBuildResult result)
    {
        BuildCompleted?.Invoke(this, new IndexBuildCompletedEventArgs(result));
    }
    
    /// <summary>
    /// Gets a job by ID
    /// </summary>
    public BackgroundIndexBuildJob? GetJob(string jobId)
    {
        ThrowIfDisposed();
        _jobs.TryGetValue(jobId, out var job);
        return job;
    }
    
    /// <summary>
    /// Gets all jobs
    /// </summary>
    public IReadOnlyList<BackgroundIndexBuildJob> GetAllJobs()
    {
        ThrowIfDisposed();
        return _jobs.Values.ToList();
    }
    
    /// <summary>
    /// Gets jobs by status
    /// </summary>
    public IReadOnlyList<BackgroundIndexBuildJob> GetJobsByStatus(BackgroundIndexBuildStatus status)
    {
        ThrowIfDisposed();
        return _jobs.Values.Where(j => j.Status == status).ToList();
    }
    
    /// <summary>
    /// Cancels a running job
    /// </summary>
    public bool CancelJob(string jobId)
    {
        ThrowIfDisposed();
        
        if (_jobs.TryGetValue(jobId, out var job))
        {
            if (job.IsCompleted)
            {
                return false;
            }
            
            job.CancellationTokenSource?.Cancel();
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Waits for a job to complete
    /// </summary>
    public async Task<BackgroundIndexBuildResult?> WaitForCompletionAsync(
        string jobId,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        var job = GetJob(jobId);
        if (job == null)
        {
            return null;
        }
        
        if (job.BuildTask == null)
        {
            return job.Result;
        }
        
        try
        {
            if (timeout.HasValue)
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeout.Value);
                await job.BuildTask.WaitAsync(cts.Token);
            }
            else
            {
                await job.BuildTask.WaitAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            if (!job.IsCompleted)
            {
                return null; // Timeout
            }
        }
        
        return job.Result;
    }
    
    /// <summary>
    /// Throws if the object has been disposed
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(BackgroundIndexBuilder));
        }
    }
    
    /// <summary>
    /// Disposes the builder and cancels all running jobs
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        
        _disposed = true;
        
        // Cancel all running jobs
        foreach (var job in _jobs.Values.Where(j => j.IsRunning))
        {
            job.CancellationTokenSource?.Cancel();
        }
        
        // Wait for jobs to complete (with timeout)
        var runningTasks = _jobs.Values
            .Where(j => j.BuildTask != null && !j.BuildTask.IsCompleted)
            .Select(j => j.BuildTask!)
            .ToArray();
        
        if (runningTasks.Length > 0)
        {
            try
            {
                Task.WaitAll(runningTasks, TimeSpan.FromSeconds(10));
            }
            catch
            {
                // Ignore exceptions during disposal
            }
        }
        
        _concurrencySemaphore.Dispose();
        
        // Dispose cancellation token sources
        foreach (var job in _jobs.Values)
        {
            job.CancellationTokenSource?.Dispose();
        }
    }
}
