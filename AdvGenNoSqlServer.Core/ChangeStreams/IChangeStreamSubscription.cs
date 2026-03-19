// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.ChangeStreams;

/// <summary>
/// Represents a subscription to a change stream
/// </summary>
public interface IChangeStreamSubscription : IDisposable
{
    /// <summary>
    /// Gets the unique identifier for this subscription
    /// </summary>
    string SubscriptionId { get; }

    /// <summary>
    /// Gets the name of the collection being watched (empty string for all collections)
    /// </summary>
    string CollectionName { get; }

    /// <summary>
    /// Gets the filter applied to this subscription
    /// </summary>
    IChangeStreamFilter? Filter { get; }

    /// <summary>
    /// Gets the timestamp when the subscription was created
    /// </summary>
    DateTime CreatedAt { get; }

    /// <summary>
    /// Gets the last event sequence number processed by this subscription
    /// </summary>
    long LastEventSequence { get; }

    /// <summary>
    /// Gets whether the subscription is still active
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Gets whether to include the full document in change events
    /// </summary>
    bool IncludeFullDocument { get; }

    /// <summary>
    /// Event raised when a change event matches this subscription
    /// </summary>
    event EventHandler<ChangeStreamEventArgs>? OnChange;

    /// <summary>
    /// Processes a change event if it matches the subscription criteria
    /// </summary>
    /// <param name="event">The change event to process</param>
    /// <returns>True if the event was handled, false otherwise</returns>
    bool TryProcessEvent(IChangeStreamEvent @event);

    /// <summary>
    /// Resumes the subscription from a specific event sequence
    /// </summary>
    /// <param name="sequenceNumber">The sequence number to resume from</param>
    void ResumeFrom(long sequenceNumber);

    /// <summary>
    /// Deactivates the subscription
    /// </summary>
    void Deactivate();
}

/// <summary>
/// Represents a filter for change stream events
/// </summary>
public interface IChangeStreamFilter
{
    /// <summary>
    /// Checks if a change event matches this filter
    /// </summary>
    /// <param name="event">The change event to check</param>
    /// <returns>True if the event matches, false otherwise</returns>
    bool Matches(IChangeStreamEvent @event);
}

/// <summary>
/// A filter that matches specific operation types
/// </summary>
public class OperationTypeFilter : IChangeStreamFilter
{
    private readonly HashSet<ChangeOperationType> _operationTypes;

    /// <summary>
    /// Creates a new operation type filter
    /// </summary>
    public OperationTypeFilter(params ChangeOperationType[] operationTypes)
    {
        _operationTypes = new HashSet<ChangeOperationType>(operationTypes);
    }

    /// <inheritdoc />
    public bool Matches(IChangeStreamEvent @event)
    {
        return _operationTypes.Contains(@event.OperationType);
    }
}

/// <summary>
/// A filter that matches specific document IDs
/// </summary>
public class DocumentIdFilter : IChangeStreamFilter
{
    private readonly HashSet<string> _documentIds;

    /// <summary>
    /// Creates a new document ID filter
    /// </summary>
    public DocumentIdFilter(params string[] documentIds)
    {
        _documentIds = new HashSet<string>(documentIds);
    }

    /// <inheritdoc />
    public bool Matches(IChangeStreamEvent @event)
    {
        return _documentIds.Contains(@event.DocumentId);
    }
}

/// <summary>
/// A filter that matches events within a time range
/// </summary>
public class TimeRangeFilter : IChangeStreamFilter
{
    private readonly DateTime? _startTime;
    private readonly DateTime? _endTime;

    /// <summary>
    /// Creates a new time range filter
    /// </summary>
    public TimeRangeFilter(DateTime? startTime = null, DateTime? endTime = null)
    {
        _startTime = startTime;
        _endTime = endTime;
    }

    /// <inheritdoc />
    public bool Matches(IChangeStreamEvent @event)
    {
        if (_startTime.HasValue && @event.Timestamp < _startTime.Value)
            return false;

        if (_endTime.HasValue && @event.Timestamp > _endTime.Value)
            return false;

        return true;
    }
}

/// <summary>
/// A composite filter that combines multiple filters with AND logic
/// </summary>
public class CompositeFilter : IChangeStreamFilter
{
    private readonly List<IChangeStreamFilter> _filters;

    /// <summary>
    /// Creates a new composite filter
    /// </summary>
    public CompositeFilter(params IChangeStreamFilter[] filters)
    {
        _filters = new List<IChangeStreamFilter>(filters);
    }

    /// <summary>
    /// Adds a filter to the composite
    /// </summary>
    public void AddFilter(IChangeStreamFilter filter)
    {
        _filters.Add(filter);
    }

    /// <inheritdoc />
    public bool Matches(IChangeStreamEvent @event)
    {
        return _filters.All(f => f.Matches(@event));
    }
}

/// <summary>
/// A filter that matches all events (pass-through)
/// </summary>
public class MatchAllFilter : IChangeStreamFilter
{
    /// <inheritdoc />
    public bool Matches(IChangeStreamEvent @event)
    {
        return true;
    }
}
