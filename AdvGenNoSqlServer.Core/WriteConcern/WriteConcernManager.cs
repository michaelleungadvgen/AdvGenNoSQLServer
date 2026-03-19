// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;

namespace AdvGenNoSqlServer.Core.WriteConcern;

/// <summary>
/// Default implementation of the write concern manager.
/// </summary>
public class WriteConcernManager : IWriteConcernManager
{
    private WriteConcern _defaultWriteConcern;
    private readonly ConcurrentDictionary<string, WriteConcern> _collectionWriteConcerns;
    private readonly WriteConcernOptions _options;
    private readonly ConcurrentDictionary<string, long> _operationCounts;
    private long _timeoutCount;
    private long _totalAcknowledgmentTimeMs;
    private DateTime _lastResetAt;

    /// <summary>
    /// Creates a new WriteConcernManager with default options.
    /// </summary>
    public WriteConcernManager()
        : this(new WriteConcernOptions())
    {
    }

    /// <summary>
    /// Creates a new WriteConcernManager with the specified options.
    /// </summary>
    public WriteConcernManager(WriteConcernOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _defaultWriteConcern = options.DefaultWriteConcern;
        _collectionWriteConcerns = new ConcurrentDictionary<string, WriteConcern>(options.CollectionWriteConcerns);
        _operationCounts = new ConcurrentDictionary<string, long>();
        _lastResetAt = DateTime.UtcNow;
    }

    /// <inheritdoc />
    public WriteConcern DefaultWriteConcern
    {
        get => _defaultWriteConcern;
        set
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            value.Validate();
            _defaultWriteConcern = value;
        }
    }

    /// <inheritdoc />
    public WriteConcern GetWriteConcernForCollection(string collectionName)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        if (_collectionWriteConcerns.TryGetValue(collectionName, out var concern))
            return concern;

        return _defaultWriteConcern;
    }

    /// <inheritdoc />
    public Task SetCollectionWriteConcernAsync(string collectionName, WriteConcern writeConcern)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        if (writeConcern == null)
            throw new ArgumentNullException(nameof(writeConcern));

        writeConcern.Validate();

        // Check if unacknowledged writes are allowed
        if (!_options.AllowUnacknowledgedWrites && !writeConcern.IsAcknowledged)
        {
            throw new InvalidOperationException("Unacknowledged writes are not allowed by server configuration");
        }

        _collectionWriteConcerns[collectionName] = writeConcern;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveCollectionWriteConcernAsync(string collectionName)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        _collectionWriteConcerns.TryRemove(collectionName, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, WriteConcern>> GetCollectionsWithCustomWriteConcernAsync()
    {
        return Task.FromResult<IReadOnlyDictionary<string, WriteConcern>>(
            new Dictionary<string, WriteConcern>(_collectionWriteConcerns));
    }

    /// <inheritdoc />
    public bool ValidateWriteConcern(WriteConcern writeConcern)
    {
        if (writeConcern == null)
            return false;

        try
        {
            writeConcern.Validate();

            // Check if unacknowledged writes are allowed
            if (!_options.AllowUnacknowledgedWrites && !writeConcern.IsAcknowledged)
                return false;

            // Check timeout bounds
            if (writeConcern.WTimeout.HasValue)
            {
                if (writeConcern.WTimeout.Value > _options.MaxTimeout)
                    return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public WriteConcernStatistics GetStatistics()
    {
        var total = _operationCounts.Values.Sum();
        var unacknowledged = _operationCounts.GetValueOrDefault("unacknowledged");
        var acknowledged = _operationCounts.GetValueOrDefault("acknowledged");
        var journaled = _operationCounts.GetValueOrDefault("journaled");
        var majority = _operationCounts.GetValueOrDefault("majority");

        return new WriteConcernStatistics
        {
            TotalWriteOperations = total,
            UnacknowledgedOperations = unacknowledged,
            AcknowledgedOperations = acknowledged,
            JournaledOperations = journaled,
            MajorityOperations = majority,
            TimeoutCount = _timeoutCount,
            AverageAcknowledgmentTimeMs = total > 0 ? (double)_totalAcknowledgmentTimeMs / total : 0,
            LastResetAt = _lastResetAt
        };
    }

    /// <inheritdoc />
    public void ResetStatistics()
    {
        _operationCounts.Clear();
        Interlocked.Exchange(ref _timeoutCount, 0);
        Interlocked.Exchange(ref _totalAcknowledgmentTimeMs, 0);
        _lastResetAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Records a write operation for statistics tracking.
    /// </summary>
    public void RecordOperation(WriteConcern writeConcern, TimeSpan acknowledgmentTime)
    {
        var key = GetConcernKey(writeConcern);
        _operationCounts.AddOrUpdate(key, 1, (_, count) => count + 1);
        Interlocked.Add(ref _totalAcknowledgmentTimeMs, (long)acknowledgmentTime.TotalMilliseconds);
    }

    /// <summary>
    /// Records a timeout for statistics tracking.
    /// Internal use only.
    /// </summary>
    internal void RecordTimeout()
    {
        Interlocked.Increment(ref _timeoutCount);
    }

    private static string GetConcernKey(WriteConcern concern)
    {
        if (!concern.IsAcknowledged)
            return "unacknowledged";
        if (concern.IsJournaled)
            return "journaled";
        if (concern.IsMajority)
            return "majority";
        return "acknowledged";
    }
}
