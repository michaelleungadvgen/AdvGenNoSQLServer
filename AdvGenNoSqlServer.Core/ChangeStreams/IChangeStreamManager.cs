// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.ChangeStreams;

/// <summary>
/// Manages change stream subscriptions and event publishing
/// </summary>
public interface IChangeStreamManager
{
    /// <summary>
    /// Creates a subscription to changes in a specific collection
    /// </summary>
    /// <param name="collectionName">The collection to watch (empty for all collections)</param>
    /// <param name="filter">Optional filter for events</param>
    /// <param name="includeFullDocument">Whether to include full document in events</param>
    /// <param name="onChange">Callback for change events</param>
    /// <returns>A subscription object that can be used to unsubscribe</returns>
    IChangeStreamSubscription Subscribe(
        string collectionName,
        IChangeStreamFilter? filter = null,
        bool includeFullDocument = true,
        EventHandler<ChangeStreamEventArgs>? onChange = null);

    /// <summary>
    /// Creates a subscription to all changes across all collections
    /// </summary>
    IChangeStreamSubscription SubscribeToAll(
        IChangeStreamFilter? filter = null,
        bool includeFullDocument = true,
        EventHandler<ChangeStreamEventArgs>? onChange = null);

    /// <summary>
    /// Publishes a change event to all matching subscriptions
    /// </summary>
    /// <param name="event">The change event to publish</param>
    void PublishEvent(IChangeStreamEvent @event);

    /// <summary>
    /// Publishes a change event asynchronously to all matching subscriptions
    /// </summary>
    Task PublishEventAsync(IChangeStreamEvent @event, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all active subscriptions
    /// </summary>
    IReadOnlyList<IChangeStreamSubscription> GetActiveSubscriptions();

    /// <summary>
    /// Gets subscriptions for a specific collection
    /// </summary>
    IReadOnlyList<IChangeStreamSubscription> GetSubscriptionsForCollection(string collectionName);

    /// <summary>
    /// Unsubscribes a specific subscription
    /// </summary>
    /// <param name="subscriptionId">The subscription ID to unsubscribe</param>
    /// <returns>True if the subscription was found and removed, false otherwise</returns>
    bool Unsubscribe(string subscriptionId);

    /// <summary>
    /// Gets statistics about the change stream manager
    /// </summary>
    ChangeStreamStatistics GetStatistics();

    /// <summary>
    /// Event raised when any change event is published
    /// </summary>
    event EventHandler<ChangeStreamEventArgs>? ChangePublished;
}

/// <summary>
/// Statistics for the change stream manager
/// </summary>
public class ChangeStreamStatistics
{
    /// <summary>
    /// Total number of active subscriptions
    /// </summary>
    public int ActiveSubscriptionCount { get; set; }

    /// <summary>
    /// Total number of events published since startup
    /// </summary>
    public long TotalEventsPublished { get; set; }

    /// <summary>
    /// Total number of events delivered to subscribers
    /// </summary>
    public long TotalEventsDelivered { get; set; }

    /// <summary>
    /// Number of events dropped due to subscriber backlog
    /// </summary>
    public long EventsDropped { get; set; }

    /// <summary>
    /// Average time to deliver an event (in milliseconds)
    /// </summary>
    public double AverageDeliveryTimeMs { get; set; }

    /// <summary>
    /// Timestamp when the manager was started
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Gets the uptime duration
    /// </summary>
    public TimeSpan Uptime => DateTime.UtcNow - StartTime;
}
