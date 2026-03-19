// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;

namespace AdvGenNoSqlServer.Query.Profiling;

/// <summary>
/// Implementation of the query profiler for slow query logging
/// </summary>
public class QueryProfiler : IQueryProfiler, IDisposable
{
    private readonly ConcurrentQueue<QueryProfile> _queryQueue;
    private readonly ConcurrentDictionary<string, QueryProfile> _queryIndex;
    private readonly ReaderWriterLockSlim _statsLock;
    private long _totalQueries;
    private long _slowQueries;
    private long _totalExecutionTime;
    private long _maxExecutionTime;
    private long _minExecutionTime = long.MaxValue;
    private long _indexUsageCount;
    private readonly DateTime _startedAt;
    private bool _disposed;

    /// <summary>
    /// Creates a new QueryProfiler with the specified options
    /// </summary>
    public QueryProfiler(ProfilingOptions? options = null)
    {
        Options = options ?? new ProfilingOptions();
        Options.Validate();

        _queryQueue = new ConcurrentQueue<QueryProfile>();
        _queryIndex = new ConcurrentDictionary<string, QueryProfile>();
        _statsLock = new ReaderWriterLockSlim();
        _startedAt = DateTime.UtcNow;
    }

    /// <inheritdoc />
    public bool IsEnabled => Options.Enabled;

    /// <inheritdoc />
    public ProfilingOptions Options { get; }

    /// <inheritdoc />
    public event EventHandler<SlowQueryDetectedEventArgs>? SlowQueryDetected;

    /// <inheritdoc />
    public void RecordQuery(QueryProfile profile)
    {
        if (!IsEnabled)
            return;

        if (profile == null)
            throw new ArgumentNullException(nameof(profile));

        // Apply sampling rate
        if (Options.SampleRate < 1.0)
        {
            var random = Random.Shared;
            if (random.NextDouble() > Options.SampleRate)
                return;
        }

        // Check if we should only log slow queries
        if (Options.LogOnlySlowQueries && !profile.IsSlowQuery)
            return;

        // Add to queue and index
        _queryQueue.Enqueue(profile);
        _queryIndex[profile.QueryId] = profile;

        // Update statistics
        UpdateStatistics(profile);

        // Raise slow query event if applicable
        if (profile.IsSlowQuery)
        {
            SlowQueryDetected?.Invoke(this, new SlowQueryDetectedEventArgs
            {
                Profile = profile,
                ThresholdMs = Options.SlowQueryThresholdMs
            });
        }

        // Trim queue if exceeded max size
        TrimQueueIfNeeded();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<QueryProfile>> GetSlowQueriesAsync(int limit = 100)
    {
        if (limit <= 0)
            throw new ArgumentException("Limit must be positive", nameof(limit));

        var slowQueries = _queryQueue
            .Where(q => q.IsSlowQuery)
            .OrderByDescending(q => q.Timestamp)
            .Take(limit)
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyList<QueryProfile>>(slowQueries);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<QueryProfile>> GetAllQueriesAsync(int limit = 100)
    {
        if (limit <= 0)
            throw new ArgumentException("Limit must be positive", nameof(limit));

        var allQueries = _queryQueue
            .OrderByDescending(q => q.Timestamp)
            .Take(limit)
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyList<QueryProfile>>(allQueries);
    }

    /// <inheritdoc />
    public Task ClearProfileDataAsync()
    {
        while (_queryQueue.TryDequeue(out _)) { }
        _queryIndex.Clear();

        _statsLock.EnterWriteLock();
        try
        {
            _totalQueries = 0;
            _slowQueries = 0;
            _totalExecutionTime = 0;
            _maxExecutionTime = 0;
            _minExecutionTime = long.MaxValue;
            _indexUsageCount = 0;
        }
        finally
        {
            _statsLock.ExitWriteLock();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ProfilingStats GetStatistics()
    {
        if (_disposed)
        {
            // Return a snapshot of statistics even after disposal
            return new ProfilingStats
            {
                TotalQueriesProfiled = Interlocked.Read(ref _totalQueries),
                SlowQueriesCount = Interlocked.Read(ref _slowQueries),
                AverageQueryTimeMs = Interlocked.Read(ref _totalQueries) > 0 
                    ? (double)Interlocked.Read(ref _totalExecutionTime) / Interlocked.Read(ref _totalQueries) 
                    : 0,
                MaxQueryTimeMs = Interlocked.Read(ref _maxExecutionTime),
                MinQueryTimeMs = Interlocked.Read(ref _minExecutionTime) == long.MaxValue 
                    ? 0 
                    : Interlocked.Read(ref _minExecutionTime),
                IndexUsagePercentage = Interlocked.Read(ref _totalQueries) > 0 
                    ? (double)Interlocked.Read(ref _indexUsageCount) / Interlocked.Read(ref _totalQueries) * 100 
                    : 0,
                ProfilingStartedAt = _startedAt,
                CurrentQueryCount = _queryQueue.Count
            };
        }

        _statsLock.EnterReadLock();
        try
        {
            return new ProfilingStats
            {
                TotalQueriesProfiled = _totalQueries,
                SlowQueriesCount = _slowQueries,
                AverageQueryTimeMs = _totalQueries > 0 ? (double)_totalExecutionTime / _totalQueries : 0,
                MaxQueryTimeMs = _maxExecutionTime,
                MinQueryTimeMs = _minExecutionTime == long.MaxValue ? 0 : _minExecutionTime,
                IndexUsagePercentage = _totalQueries > 0 ? (double)_indexUsageCount / _totalQueries * 100 : 0,
                ProfilingStartedAt = _startedAt,
                CurrentQueryCount = _queryQueue.Count
            };
        }
        finally
        {
            _statsLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Gets a specific query profile by ID
    /// </summary>
    public QueryProfile? GetQueryById(string queryId)
    {
        if (string.IsNullOrEmpty(queryId))
            throw new ArgumentException("QueryId cannot be null or empty", nameof(queryId));

        _queryIndex.TryGetValue(queryId, out var profile);
        return profile;
    }

    /// <summary>
    /// Gets queries for a specific collection
    /// </summary>
    public IReadOnlyList<QueryProfile> GetQueriesByCollection(string collection, int limit = 100)
    {
        if (string.IsNullOrEmpty(collection))
            throw new ArgumentException("Collection cannot be null or empty", nameof(collection));

        if (limit <= 0)
            throw new ArgumentException("Limit must be positive", nameof(limit));

        return _queryQueue
            .Where(q => q.Collection.Equals(collection, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(q => q.Timestamp)
            .Take(limit)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Gets slow queries for a specific time range
    /// </summary>
    public IReadOnlyList<QueryProfile> GetQueriesByTimeRange(DateTime start, DateTime end, bool slowOnly = false)
    {
        var query = _queryQueue.Where(q => q.Timestamp >= start && q.Timestamp <= end);
        
        if (slowOnly)
            query = query.Where(q => q.IsSlowQuery);

        return query.OrderByDescending(q => q.Timestamp).ToList().AsReadOnly();
    }

    private void UpdateStatistics(QueryProfile profile)
    {
        _statsLock.EnterWriteLock();
        try
        {
            _totalQueries++;
            
            if (profile.IsSlowQuery)
                _slowQueries++;

            _totalExecutionTime += profile.DurationMs;

            if (profile.DurationMs > _maxExecutionTime)
                _maxExecutionTime = profile.DurationMs;

            if (profile.DurationMs < _minExecutionTime)
                _minExecutionTime = profile.DurationMs;

            if (profile.UsedIndex)
                _indexUsageCount++;
        }
        finally
        {
            _statsLock.ExitWriteLock();
        }
    }

    private void TrimQueueIfNeeded()
    {
        while (_queryQueue.Count > Options.MaxLoggedQueries && _queryQueue.TryDequeue(out var removedQuery))
        {
            _queryIndex.TryRemove(removedQuery.QueryId, out _);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _statsLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
