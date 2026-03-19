// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Text.Json;

namespace AdvGenNoSqlServer.Query.Profiling;

/// <summary>
/// Interface for profiling and logging slow queries
/// </summary>
public interface IQueryProfiler
{
    /// <summary>
    /// Records a query execution profile
    /// </summary>
    /// <param name="profile">The query profile to record</param>
    void RecordQuery(QueryProfile profile);

    /// <summary>
    /// Gets the list of slow queries that exceeded the threshold
    /// </summary>
    /// <param name="limit">Maximum number of queries to return</param>
    /// <returns>List of slow query profiles</returns>
    Task<IReadOnlyList<QueryProfile>> GetSlowQueriesAsync(int limit = 100);

    /// <summary>
    /// Gets all recorded query profiles
    /// </summary>
    /// <param name="limit">Maximum number of queries to return</param>
    /// <returns>List of all query profiles</returns>
    Task<IReadOnlyList<QueryProfile>> GetAllQueriesAsync(int limit = 100);

    /// <summary>
    /// Clears all profiled query data
    /// </summary>
    Task ClearProfileDataAsync();

    /// <summary>
    /// Gets profiling statistics
    /// </summary>
    /// <returns>Profiling statistics</returns>
    ProfilingStats GetStatistics();

    /// <summary>
    /// Whether profiling is currently enabled
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Current profiling options
    /// </summary>
    ProfilingOptions Options { get; }

    /// <summary>
    /// Event raised when a slow query is detected
    /// </summary>
    event EventHandler<SlowQueryDetectedEventArgs>? SlowQueryDetected;
}

/// <summary>
/// Represents a profiled query execution
/// </summary>
public record QueryProfile
{
    /// <summary>
    /// Unique identifier for this query execution
    /// </summary>
    public string QueryId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// The collection being queried
    /// </summary>
    public required string Collection { get; init; }

    /// <summary>
    /// The query filter as JSON
    /// </summary>
    public JsonElement? Query { get; init; }

    /// <summary>
    /// The query execution plan (if available)
    /// </summary>
    public Models.QueryStats? Plan { get; init; }

    /// <summary>
    /// Query execution time in milliseconds
    /// </summary>
    public required long DurationMs { get; init; }

    /// <summary>
    /// Number of documents examined during query execution
    /// </summary>
    public long DocumentsExamined { get; init; }

    /// <summary>
    /// Number of documents returned by the query
    /// </summary>
    public long DocumentsReturned { get; init; }

    /// <summary>
    /// Whether an index was used for this query
    /// </summary>
    public bool UsedIndex { get; init; }

    /// <summary>
    /// Name of the index used (if any)
    /// </summary>
    public string? IndexUsed { get; init; }

    /// <summary>
    /// When the query was executed
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// The user who executed the query (if authenticated)
    /// </summary>
    public string? User { get; init; }

    /// <summary>
    /// Client IP address (if available)
    /// </summary>
    public string? ClientIp { get; init; }

    /// <summary>
    /// Whether this query exceeded the slow query threshold
    /// </summary>
    public bool IsSlowQuery { get; init; }

    /// <summary>
    /// Additional metadata about the query execution
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Configuration options for query profiling
/// </summary>
public class ProfilingOptions
{
    /// <summary>
    /// Whether profiling is enabled
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Threshold in milliseconds above which a query is considered slow
    /// </summary>
    public int SlowQueryThresholdMs { get; set; } = 100;

    /// <summary>
    /// Whether to log the query execution plan
    /// </summary>
    public bool LogQueryPlan { get; set; } = true;

    /// <summary>
    /// Sampling rate for query profiling (0.0 to 1.0, where 1.0 = profile all queries)
    /// </summary>
    public double SampleRate { get; set; } = 1.0;

    /// <summary>
    /// Maximum number of queries to keep in memory
    /// </summary>
    public int MaxLoggedQueries { get; set; } = 10000;

    /// <summary>
    /// Whether to log only slow queries (true) or all queries (false)
    /// </summary>
    public bool LogOnlySlowQueries { get; set; } = false;

    /// <summary>
    /// Validates the options
    /// </summary>
    public void Validate()
    {
        if (SlowQueryThresholdMs < 0)
            throw new ArgumentException("SlowQueryThresholdMs must be non-negative", nameof(SlowQueryThresholdMs));

        if (SampleRate < 0 || SampleRate > 1)
            throw new ArgumentException("SampleRate must be between 0.0 and 1.0", nameof(SampleRate));

        if (MaxLoggedQueries < 1)
            throw new ArgumentException("MaxLoggedQueries must be at least 1", nameof(MaxLoggedQueries));
    }
}

/// <summary>
/// Statistics for query profiling
/// </summary>
public class ProfilingStats
{
    /// <summary>
    /// Total number of queries profiled
    /// </summary>
    public long TotalQueriesProfiled { get; set; }

    /// <summary>
    /// Number of slow queries detected
    /// </summary>
    public long SlowQueriesCount { get; set; }

    /// <summary>
    /// Average query execution time in milliseconds
    /// </summary>
    public double AverageQueryTimeMs { get; set; }

    /// <summary>
    /// Slowest query execution time in milliseconds
    /// </summary>
    public long MaxQueryTimeMs { get; set; }

    /// <summary>
    /// Fastest query execution time in milliseconds
    /// </summary>
    public long MinQueryTimeMs { get; set; }

    /// <summary>
    /// Percentage of queries that used an index
    /// </summary>
    public double IndexUsagePercentage { get; set; }

    /// <summary>
    /// When profiling was started
    /// </summary>
    public DateTime ProfilingStartedAt { get; set; }

    /// <summary>
    /// Current number of queries stored in memory
    /// </summary>
    public int CurrentQueryCount { get; set; }
}

/// <summary>
/// Event arguments for slow query detection
/// </summary>
public class SlowQueryDetectedEventArgs : EventArgs
{
    /// <summary>
    /// The slow query profile
    /// </summary>
    public required QueryProfile Profile { get; init; }

    /// <summary>
    /// The threshold that was exceeded
    /// </summary>
    public required int ThresholdMs { get; init; }

    /// <summary>
    /// How much the query exceeded the threshold
    /// </summary>
    public long ExceededByMs => Profile.DurationMs - ThresholdMs;
}
