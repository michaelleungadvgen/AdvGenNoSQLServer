// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;

namespace AdvGenNoSqlServer.Core.ChangeStreams;

/// <summary>
/// Default implementation of the change stream manager
/// </summary>
public class ChangeStreamManager : IChangeStreamManager, IDisposable
{
    private readonly ConcurrentDictionary<string, IChangeStreamSubscription> _subscriptions;
    private readonly ConcurrentDictionary<string, HashSet<string>> _collectionSubscriptions;
    private long _totalEventsPublished;
    private long _totalEventsDelivered;
    private long _eventsDropped;
    private readonly DateTime _startTime;
    private bool _disposed;

    /// <summary>
    /// Creates a new change stream manager
    /// </summary>
    public ChangeStreamManager()
    {
        _subscriptions = new ConcurrentDictionary<string, IChangeStreamSubscription>();
        _collectionSubscriptions = new ConcurrentDictionary<string, HashSet<string>>();
        _startTime = DateTime.UtcNow;
        _totalEventsPublished = 0;
        _totalEventsDelivered = 0;
        _eventsDropped = 0;
    }

    /// <inheritdoc />
    public event EventHandler<ChangeStreamEventArgs>? ChangePublished;

    /// <inheritdoc />
    public IChangeStreamSubscription Subscribe(
        string collectionName,
        IChangeStreamFilter? filter = null,
        bool includeFullDocument = true,
        EventHandler<ChangeStreamEventArgs>? onChange = null)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ChangeStreamManager));

        var subscriptionId = Guid.NewGuid().ToString("N");
        var subscription = new ChangeStreamSubscription(
            subscriptionId,
            collectionName,
            filter,
            includeFullDocument);

        if (onChange != null)
        {
            subscription.OnChange += onChange;
        }

        _subscriptions[subscriptionId] = subscription;

        // Track subscription by collection
        var collectionKey = string.IsNullOrEmpty(collectionName) ? "__all__" : collectionName;
        var subs = _collectionSubscriptions.GetOrAdd(collectionKey, _ => new HashSet<string>());
        lock (subs)
        {
            subs.Add(subscriptionId);
        }

        return subscription;
    }

    /// <inheritdoc />
    public IChangeStreamSubscription SubscribeToAll(
        IChangeStreamFilter? filter = null,
        bool includeFullDocument = true,
        EventHandler<ChangeStreamEventArgs>? onChange = null)
    {
        return Subscribe(string.Empty, filter, includeFullDocument, onChange);
    }

    /// <inheritdoc />
    public void PublishEvent(IChangeStreamEvent @event)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ChangeStreamManager));

        if (@event == null)
            throw new ArgumentNullException(nameof(@event));

        Interlocked.Increment(ref _totalEventsPublished);

        // Get all subscription IDs that might match
        var matchingSubscriptionIds = new HashSet<string>();

        // Add subscriptions for this specific collection
        if (_collectionSubscriptions.TryGetValue(@event.CollectionName, out var collectionSubs))
        {
            lock (collectionSubs)
            {
                matchingSubscriptionIds.UnionWith(collectionSubs);
            }
        }

        // Add subscriptions for "all collections"
        if (_collectionSubscriptions.TryGetValue("__all__", out var allSubs))
        {
            lock (allSubs)
            {
                matchingSubscriptionIds.UnionWith(allSubs);
            }
        }

        // Deliver to each subscription
        int deliveredCount = 0;
        foreach (var subId in matchingSubscriptionIds)
        {
            if (_subscriptions.TryGetValue(subId, out var subscription))
            {
                if (subscription.IsActive && subscription.TryProcessEvent(@event))
                {
                    deliveredCount++;
                }
                else if (!subscription.IsActive)
                {
                    // Clean up inactive subscription
                    Unsubscribe(subId);
                }
            }
        }

        Interlocked.Add(ref _totalEventsDelivered, deliveredCount);

        // Raise global event
        ChangePublished?.Invoke(this, new ChangeStreamEventArgs(@event));
    }

    /// <inheritdoc />
    public async Task PublishEventAsync(IChangeStreamEvent @event, CancellationToken cancellationToken = default)
    {
        await Task.Run(() => PublishEvent(@event), cancellationToken);
    }

    /// <inheritdoc />
    public IReadOnlyList<IChangeStreamSubscription> GetActiveSubscriptions()
    {
        return _subscriptions.Values.Where(s => s.IsActive).ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<IChangeStreamSubscription> GetSubscriptionsForCollection(string collectionName)
    {
        var result = new List<IChangeStreamSubscription>();

        if (_collectionSubscriptions.TryGetValue(collectionName, out var subIds))
        {
            lock (subIds)
            {
                foreach (var subId in subIds)
                {
                    if (_subscriptions.TryGetValue(subId, out var subscription) && subscription.IsActive)
                    {
                        result.Add(subscription);
                    }
                }
            }
        }

        // Also include "all collections" subscriptions
        if (_collectionSubscriptions.TryGetValue("__all__", out var allSubs))
        {
            lock (allSubs)
            {
                foreach (var subId in allSubs)
                {
                    if (_subscriptions.TryGetValue(subId, out var subscription) && subscription.IsActive)
                    {
                        if (!result.Contains(subscription))
                        {
                            result.Add(subscription);
                        }
                    }
                }
            }
        }

        return result;
    }

    /// <inheritdoc />
    public bool Unsubscribe(string subscriptionId)
    {
        if (!_subscriptions.TryRemove(subscriptionId, out var subscription))
            return false;

        // Remove from collection tracking
        var collectionKey = string.IsNullOrEmpty(subscription.CollectionName) ? "__all__" : subscription.CollectionName;
        if (_collectionSubscriptions.TryGetValue(collectionKey, out var subs))
        {
            lock (subs)
            {
                subs.Remove(subscriptionId);
            }
        }

        subscription.Deactivate();
        subscription.Dispose();

        return true;
    }

    /// <inheritdoc />
    public ChangeStreamStatistics GetStatistics()
    {
        return new ChangeStreamStatistics
        {
            ActiveSubscriptionCount = _subscriptions.Count(s => s.Value.IsActive),
            TotalEventsPublished = Interlocked.Read(ref _totalEventsPublished),
            TotalEventsDelivered = Interlocked.Read(ref _totalEventsDelivered),
            EventsDropped = Interlocked.Read(ref _eventsDropped),
            StartTime = _startTime
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Dispose all subscriptions
        foreach (var subscription in _subscriptions.Values)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        _collectionSubscriptions.Clear();
    }
}
