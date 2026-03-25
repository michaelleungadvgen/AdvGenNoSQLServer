// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Storage.Indexing;

/// <summary>
/// Status of a background index build job
/// </summary>
public enum BackgroundIndexBuildStatus
{
    /// <summary>
    /// Job is pending and waiting to start
    /// </summary>
    Pending,
    
    /// <summary>
    /// Job is currently running
    /// </summary>
    Running,
    
    /// <summary>
    /// Job completed successfully
    /// </summary>
    Completed,
    
    /// <summary>
    /// Job failed with errors
    /// </summary>
    Failed,
    
    /// <summary>
    /// Job was cancelled
    /// </summary>
    Cancelled
}

/// <summary>
/// Priority levels for background index builds
/// </summary>
public enum IndexBuildPriority
{
    /// <summary>
    /// Low priority - yield to other operations
    /// </summary>
    Low,
    
    /// <summary>
    /// Normal priority
    /// </summary>
    Normal,
    
    /// <summary>
    /// High priority - prefer over other operations
    /// </summary>
    High
}

/// <summary>
/// Options for configuring background index builds
/// </summary>
public class BackgroundIndexBuildOptions
{
    /// <summary>
    /// Number of documents to process in each batch (default: 1000)
    /// </summary>
    public int BatchSize { get; set; } = 1000;
    
    /// <summary>
    /// Delay between batches in milliseconds (default: 0, no delay)
    /// </summary>
    public int BatchDelayMs { get; set; } = 0;
    
    /// <summary>
    /// Priority of the build job (default: Normal)
    /// </summary>
    public IndexBuildPriority Priority { get; set; } = IndexBuildPriority.Normal;
    
    /// <summary>
    /// Whether to stop on the first error or continue processing (default: false)
    /// </summary>
    public bool StopOnFirstError { get; set; } = false;
    
    /// <summary>
    /// Maximum number of errors before aborting (default: 100, 0 = unlimited)
    /// </summary>
    public int MaxErrors { get; set; } = 100;
    
    /// <summary>
    /// Maximum number of concurrent builds (default: 2)
    /// </summary>
    public int MaxConcurrentBuilds { get; set; } = 2;
}

/// <summary>
/// Progress information for an index build job
/// </summary>
public class IndexBuildProgress
{
    /// <summary>
    /// Unique job identifier
    /// </summary>
    public string JobId { get; set; } = string.Empty;
    
    /// <summary>
    /// Collection being indexed
    /// </summary>
    public string CollectionName { get; set; } = string.Empty;
    
    /// <summary>
    /// Field being indexed
    /// </summary>
    public string FieldName { get; set; } = string.Empty;
    
    /// <summary>
    /// Number of documents processed so far
    /// </summary>
    public long DocumentsProcessed { get; set; }
    
    /// <summary>
    /// Total number of documents to process (null if unknown)
    /// </summary>
    public long? TotalDocuments { get; set; }
    
    /// <summary>
    /// Percentage complete (0-100)
    /// </summary>
    public double PercentComplete => TotalDocuments.HasValue && TotalDocuments.Value > 0
        ? Math.Min(100.0, (DocumentsProcessed * 100.0) / TotalDocuments.Value)
        : 0;
    
    /// <summary>
    /// Current status of the build
    /// </summary>
    public BackgroundIndexBuildStatus Status { get; set; }
    
    /// <summary>
    /// Number of errors encountered
    /// </summary>
    public int ErrorCount { get; set; }
    
    /// <summary>
    /// Time elapsed since build started
    /// </summary>
    public TimeSpan Elapsed { get; set; }
    
    /// <summary>
    /// Timestamp when build started
    /// </summary>
    public DateTime? StartedAt { get; set; }
    
    /// <summary>
    /// Estimated time remaining (null if unknown)
    /// </summary>
    public TimeSpan? EstimatedRemaining { get; set; }
    
    /// <summary>
    /// Current processing rate (documents per second)
    /// </summary>
    public double? DocumentsPerSecond => Elapsed.TotalSeconds > 0
        ? DocumentsProcessed / Elapsed.TotalSeconds
        : null;
}

/// <summary>
/// Result of a background index build operation
/// </summary>
public class BackgroundIndexBuildResult
{
    /// <summary>
    /// Unique job identifier
    /// </summary>
    public string JobId { get; set; } = string.Empty;
    
    /// <summary>
    /// Final status of the build
    /// </summary>
    public BackgroundIndexBuildStatus Status { get; set; }
    
    /// <summary>
    /// Collection that was indexed
    /// </summary>
    public string CollectionName { get; set; } = string.Empty;
    
    /// <summary>
    /// Field that was indexed
    /// </summary>
    public string FieldName { get; set; } = string.Empty;
    
    /// <summary>
    /// Total number of documents processed
    /// </summary>
    public long DocumentsProcessed { get; set; }
    
    /// <summary>
    /// Number of index entries created
    /// </summary>
    public long EntriesCreated { get; set; }
    
    /// <summary>
    /// Number of errors encountered
    /// </summary>
    public int ErrorCount { get; set; }
    
    /// <summary>
    /// List of error messages (if any)
    /// </summary>
    public List<string> Errors { get; set; } = new();
    
    /// <summary>
    /// Time taken to complete the build
    /// </summary>
    public TimeSpan Duration { get; set; }
    
    /// <summary>
    /// Timestamp when build started
    /// </summary>
    public DateTime StartedAt { get; set; }
    
    /// <summary>
    /// Timestamp when build completed
    /// </summary>
    public DateTime? CompletedAt { get; set; }
    
    /// <summary>
    /// Whether the build was successful
    /// </summary>
    public bool IsSuccess => Status == BackgroundIndexBuildStatus.Completed;
    
    /// <summary>
    /// Average processing rate (documents per second)
    /// </summary>
    public double? DocumentsPerSecond => Duration.TotalSeconds > 0
        ? DocumentsProcessed / Duration.TotalSeconds
        : null;
    
    /// <summary>
    /// Factory method for success result
    /// </summary>
    public static BackgroundIndexBuildResult Success(string jobId, string collectionName, string fieldName)
    {
        return new BackgroundIndexBuildResult
        {
            JobId = jobId,
            CollectionName = collectionName,
            FieldName = fieldName,
            Status = BackgroundIndexBuildStatus.Completed,
            CompletedAt = DateTime.UtcNow
        };
    }
    
    /// <summary>
    /// Factory method for failure result
    /// </summary>
    public static BackgroundIndexBuildResult Failure(string jobId, string collectionName, string fieldName, string error)
    {
        return new BackgroundIndexBuildResult
        {
            JobId = jobId,
            CollectionName = collectionName,
            FieldName = fieldName,
            Status = BackgroundIndexBuildStatus.Failed,
            Errors = new List<string> { error },
            ErrorCount = 1,
            CompletedAt = DateTime.UtcNow
        };
    }
    
    /// <summary>
    /// Factory method for cancelled result
    /// </summary>
    public static BackgroundIndexBuildResult Cancelled(string jobId, string collectionName, string fieldName)
    {
        return new BackgroundIndexBuildResult
        {
            JobId = jobId,
            CollectionName = collectionName,
            FieldName = fieldName,
            Status = BackgroundIndexBuildStatus.Cancelled,
            CompletedAt = DateTime.UtcNow
        };
    }
}

/// <summary>
/// Represents a background index build job
/// </summary>
public class BackgroundIndexBuildJob
{
    /// <summary>
    /// Unique job identifier
    /// </summary>
    public string JobId { get; set; } = Guid.NewGuid().ToString("N")[..16];
    
    /// <summary>
    /// Collection being indexed
    /// </summary>
    public string CollectionName { get; set; } = string.Empty;
    
    /// <summary>
    /// Field being indexed
    /// </summary>
    public string FieldName { get; set; } = string.Empty;
    
    /// <summary>
    /// Current status of the job
    /// </summary>
    public BackgroundIndexBuildStatus Status { get; set; } = BackgroundIndexBuildStatus.Pending;
    
    /// <summary>
    /// Build options
    /// </summary>
    public BackgroundIndexBuildOptions Options { get; set; } = new();
    
    /// <summary>
    /// Timestamp when job was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Timestamp when job started
    /// </summary>
    public DateTime? StartedAt { get; set; }
    
    /// <summary>
    /// Timestamp when job completed
    /// </summary>
    public DateTime? CompletedAt { get; set; }
    
    /// <summary>
    /// Progress information
    /// </summary>
    public IndexBuildProgress Progress { get; set; } = new();
    
    /// <summary>
    /// Result of the build (available after completion)
    /// </summary>
    public BackgroundIndexBuildResult? Result { get; set; }
    
    /// <summary>
    /// Cancellation token source for this job
    /// </summary>
    internal CancellationTokenSource? CancellationTokenSource { get; set; }
    
    /// <summary>
    /// The task running the build
    /// </summary>
    internal Task? BuildTask { get; set; }
    
    /// <summary>
    /// Whether the job is currently running
    /// </summary>
    public bool IsRunning => Status == BackgroundIndexBuildStatus.Running;
    
    /// <summary>
    /// Whether the job has completed (success, failure, or cancelled)
    /// </summary>
    public bool IsCompleted => Status is BackgroundIndexBuildStatus.Completed 
        or BackgroundIndexBuildStatus.Failed 
        or BackgroundIndexBuildStatus.Cancelled;
}

/// <summary>
/// Event arguments for index build progress events
/// </summary>
public class IndexBuildProgressEventArgs : EventArgs
{
    /// <summary>
    /// Progress information
    /// </summary>
    public IndexBuildProgress Progress { get; set; } = new();
    
    /// <summary>
    /// Constructor
    /// </summary>
    public IndexBuildProgressEventArgs(IndexBuildProgress progress)
    {
        Progress = progress;
    }
}

/// <summary>
/// Event arguments for index build completion events
/// </summary>
public class IndexBuildCompletedEventArgs : EventArgs
{
    /// <summary>
    /// Build result
    /// </summary>
    public BackgroundIndexBuildResult Result { get; set; } = new();
    
    /// <summary>
    /// Constructor
    /// </summary>
    public IndexBuildCompletedEventArgs(BackgroundIndexBuildResult result)
    {
        Result = result;
    }
}

/// <summary>
/// Interface for background index building operations
/// </summary>
public interface IBackgroundIndexBuilder
{
    /// <summary>
    /// Event raised when build progress is updated
    /// </summary>
    event EventHandler<IndexBuildProgressEventArgs>? BuildProgress;
    
    /// <summary>
    /// Event raised when a build completes
    /// </summary>
    event EventHandler<IndexBuildCompletedEventArgs>? BuildCompleted;
    
    /// <summary>
    /// Starts a background index build for a collection field
    /// </summary>
    /// <typeparam name="TKey">Type of the index key</typeparam>
    /// <param name="collectionName">Collection name</param>
    /// <param name="fieldName">Field to index</param>
    /// <param name="documents">Documents to index</param>
    /// <param name="keySelector">Function to extract key from document</param>
    /// <param name="isUnique">Whether index should enforce uniqueness</param>
    /// <param name="options">Build options</param>
    /// <param name="progress">Progress reporter</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The build job</returns>
    Task<BackgroundIndexBuildJob> StartBuildAsync<TKey>(
        string collectionName,
        string fieldName,
        IEnumerable<Document> documents,
        Func<Document, TKey> keySelector,
        bool isUnique = false,
        BackgroundIndexBuildOptions? options = null,
        IProgress<IndexBuildProgress>? progress = null,
        CancellationToken cancellationToken = default) where TKey : IComparable<TKey>;
    
    /// <summary>
    /// Gets a job by ID
    /// </summary>
    /// <param name="jobId">Job identifier</param>
    /// <returns>The job if found, null otherwise</returns>
    BackgroundIndexBuildJob? GetJob(string jobId);
    
    /// <summary>
    /// Gets all jobs
    /// </summary>
    /// <returns>List of all jobs</returns>
    IReadOnlyList<BackgroundIndexBuildJob> GetAllJobs();
    
    /// <summary>
    /// Gets jobs by status
    /// </summary>
    /// <param name="status">Status to filter by</param>
    /// <returns>List of jobs with the specified status</returns>
    IReadOnlyList<BackgroundIndexBuildJob> GetJobsByStatus(BackgroundIndexBuildStatus status);
    
    /// <summary>
    /// Cancels a running job
    /// </summary>
    /// <param name="jobId">Job identifier</param>
    /// <returns>True if job was cancelled, false if not found or already completed</returns>
    bool CancelJob(string jobId);
    
    /// <summary>
    /// Waits for a job to complete
    /// </summary>
    /// <param name="jobId">Job identifier</param>
    /// <param name="timeout">Maximum time to wait</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The build result, or null if timeout</returns>
    Task<BackgroundIndexBuildResult?> WaitForCompletionAsync(
        string jobId, 
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the number of currently running builds
    /// </summary>
    int RunningBuildCount { get; }
    
    /// <summary>
    /// Maximum number of concurrent builds allowed
    /// </summary>
    int MaxConcurrentBuilds { get; set; }
}
