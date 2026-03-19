// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Threading.Channels;

namespace AdvGenNoSqlServer.Core.ChangeStreams;

/// <summary>
/// Default implementation of a change stream subscription
/// </summary>
public class ChangeStreamSubscription : IChangeStreamSubscription
{
    private readonly Channel<IChangeStreamEvent> _eventChannel;
    private long _lastEventSequence;
    private bool _isActive;
    private readonly CancellationTokenSource _cts;
    private readonly Task _processingTask;

    /// <inheritdoc />
    public string SubscriptionId { get; }

    /// <inheritdoc />
    public string CollectionName { get; }

    /// <inheritdoc />
    public IChangeStreamFilter? Filter { get; }

    /// <inheritdoc />
    public DateTime CreatedAt { get; }

    /// <inheritdoc />
    public long LastEventSequence => Interlocked.Read(ref _lastEventSequence);

    /// <inheritdoc />
    public bool IsActive => _isActive;

    /// <inheritdoc />
    public bool IncludeFullDocument { get; }

    /// <inheritdoc />
    public event EventHandler<ChangeStreamEventArgs>? OnChange;

    /// <summary>
    /// Creates a new change stream subscription
    /// </summary>
    public ChangeStreamSubscription(
        string subscriptionId,
        string collectionName,
        IChangeStreamFilter? filter = null,
        bool includeFullDocument = true,
        int bufferCapacity = 1000)
    {
        SubscriptionId = subscriptionId ?? throw new ArgumentNullException(nameof(subscriptionId));
        CollectionName = collectionName ?? throw new ArgumentNullException(nameof(collectionName));
        Filter = filter;
        IncludeFullDocument = includeFullDocument;
        CreatedAt = DateTime.UtcNow;
        _isActive = true;
        _lastEventSequence = 0;

        _cts = new CancellationTokenSource();
        _eventChannel = Channel.CreateBounded<IChangeStreamEvent>(new BoundedChannelOptions(bufferCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        // Start background processing task
        _processingTask = Task.Run(ProcessEventsAsync);
    }

    /// <inheritdoc />
    public bool TryProcessEvent(IChangeStreamEvent @event)
    {
        if (!_isActive)
            return false;

        if (!string.IsNullOrEmpty(CollectionName) && @event.CollectionName != CollectionName)
            return false;

        if (Filter != null && !Filter.Matches(@event))
            return false;

        // Try to write to channel without blocking
        return _eventChannel.Writer.TryWrite(@event);
    }

    /// <inheritdoc />
    public void ResumeFrom(long sequenceNumber)
    {
        Interlocked.Exchange(ref _lastEventSequence, sequenceNumber);
    }

    /// <inheritdoc />
    public void Deactivate()
    {
        if (!_isActive)
            return;

        _isActive = false;
        try
        {
            _eventChannel.Writer.Complete();
        }
        catch (ChannelClosedException)
        {
            // Already closed
        }
        _cts.Cancel();
    }

    /// <summary>
    /// Background task to process events from the channel
    /// </summary>
    private async Task ProcessEventsAsync()
    {
        try
        {
            await foreach (var @event in _eventChannel.Reader.ReadAllAsync(_cts.Token))
            {
                try
                {
                    Interlocked.Increment(ref _lastEventSequence);
                    OnChange?.Invoke(this, new ChangeStreamEventArgs(@event));
                }
                catch (Exception)
                {
                    // Subscriber exceptions shouldn't stop processing
                    // Consider logging here
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on dispose
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Deactivate();
        _cts.Dispose();

        try
        {
            _processingTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Expected if task was cancelled
        }
    }
}
