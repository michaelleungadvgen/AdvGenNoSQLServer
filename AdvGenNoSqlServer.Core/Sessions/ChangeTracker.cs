// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;
using System.Text.Json;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Core.Sessions;

/// <summary>
/// Default implementation of the change tracker
/// </summary>
public class ChangeTracker : IChangeTracker
{
    private readonly ConcurrentDictionary<string, TrackedEntity> _trackedEntities = new();
    private readonly bool _throwOnDuplicateTracking;
    private readonly JsonSerializerOptions _snapshotOptions;

    /// <summary>
    /// Creates a new change tracker
    /// </summary>
    public ChangeTracker(bool throwOnDuplicateTracking = false)
    {
        _throwOnDuplicateTracking = throwOnDuplicateTracking;
        _snapshotOptions = new JsonSerializerOptions
        {
            WriteIndented = false
        };
    }

    /// <inheritdoc />
    public IReadOnlyCollection<TrackedEntity> TrackedEntities => _trackedEntities.Values.ToList().AsReadOnly();

    /// <inheritdoc />
    public int Count => _trackedEntities.Count;

    /// <inheritdoc />
    public event EventHandler<EntityTrackedEventArgs>? EntityTracked;

    /// <inheritdoc />
    public event EventHandler<EntityStateChangedEventArgs>? EntityStateChanged;

    /// <inheritdoc />
    public IEnumerable<TrackedEntity> GetEntities(EntityState state)
    {
        return _trackedEntities.Values.Where(e => e.State == state);
    }

    /// <inheritdoc />
    public EntityState GetEntityState(string collectionName, string documentId)
    {
        var key = GetKey(collectionName, documentId);
        return _trackedEntities.TryGetValue(key, out var entity) ? entity.State : EntityState.Detached;
    }

    /// <inheritdoc />
    public void TrackAdded(string collectionName, Document entity)
    {
        var key = GetKey(collectionName, entity.Id);

        if (_trackedEntities.TryGetValue(key, out var existing))
        {
            if (_throwOnDuplicateTracking)
            {
                throw new InvalidOperationException($"Entity with ID '{entity.Id}' in collection '{collectionName}' is already being tracked with state '{existing.State}'");
            }

            // If already tracked as added or modified, keep that state
            if (existing.State == EntityState.Added || existing.State == EntityState.Modified)
            {
                return;
            }

            // Update to added state
            var oldState = existing.State;
            existing.State = EntityState.Added;
            OnEntityStateChanged(existing, oldState, EntityState.Added);
        }
        else
        {
            var tracked = new TrackedEntity(collectionName, entity, EntityState.Added);
            _trackedEntities[key] = tracked;
            OnEntityTracked(tracked);
        }
    }

    /// <inheritdoc />
    public void TrackUnchanged(string collectionName, Document entity)
    {
        var key = GetKey(collectionName, entity.Id);

        if (_trackedEntities.ContainsKey(key))
        {
            if (_throwOnDuplicateTracking)
            {
                throw new InvalidOperationException($"Entity with ID '{entity.Id}' in collection '{collectionName}' is already being tracked");
            }
            return;
        }

        // Capture original values snapshot
        var originalValues = CreateSnapshot(entity);
        var tracked = new TrackedEntity(collectionName, entity, EntityState.Unchanged, originalValues);
        _trackedEntities[key] = tracked;
        OnEntityTracked(tracked);
    }

    /// <inheritdoc />
    public void TrackModified(string collectionName, Document entity)
    {
        var key = GetKey(collectionName, entity.Id);

        if (_trackedEntities.TryGetValue(key, out var existing))
        {
            if (existing.State == EntityState.Added)
            {
                // If added, just update the entity reference
                existing.Entity.Data = entity.Data;
                return;
            }

            if (existing.State == EntityState.Deleted)
            {
                throw new InvalidOperationException($"Cannot modify entity with ID '{entity.Id}' as it has been marked for deletion");
            }

            var oldState = existing.State;
            existing.Entity.Data = entity.Data;
            existing.State = EntityState.Modified;

            if (oldState != EntityState.Modified)
            {
                OnEntityStateChanged(existing, oldState, EntityState.Modified);
            }
        }
        else
        {
            var originalValues = CreateSnapshot(entity);
            var tracked = new TrackedEntity(collectionName, entity, EntityState.Modified, originalValues);
            _trackedEntities[key] = tracked;
            OnEntityTracked(tracked);
        }
    }

    /// <inheritdoc />
    public void TrackDeleted(string collectionName, string documentId)
    {
        var key = GetKey(collectionName, documentId);

        if (_trackedEntities.TryGetValue(key, out var existing))
        {
            var oldState = existing.State;

            if (oldState == EntityState.Added)
            {
                // If it was added, just remove it from tracking
                _trackedEntities.TryRemove(key, out _);
                return;
            }

            existing.State = EntityState.Deleted;
            OnEntityStateChanged(existing, oldState, EntityState.Deleted);
        }
        else
        {
            // Create a placeholder document for tracking
            var placeholder = new Document
            {
                Id = documentId,
                Data = new Dictionary<string, object?>()
            };
            var tracked = new TrackedEntity(collectionName, placeholder, EntityState.Deleted);
            _trackedEntities[key] = tracked;
            OnEntityTracked(tracked);
        }
    }

    /// <inheritdoc />
    public bool Untrack(string collectionName, string documentId)
    {
        var key = GetKey(collectionName, documentId);
        return _trackedEntities.TryRemove(key, out _);
    }

    /// <inheritdoc />
    public bool IsTracked(string collectionName, string documentId)
    {
        var key = GetKey(collectionName, documentId);
        return _trackedEntities.ContainsKey(key);
    }

    /// <inheritdoc />
    public TrackedEntity? GetTrackedEntity(string collectionName, string documentId)
    {
        var key = GetKey(collectionName, documentId);
        return _trackedEntities.TryGetValue(key, out var entity) ? entity : null;
    }

    /// <inheritdoc />
    public int DetectChanges()
    {
        int changesDetected = 0;

        foreach (var tracked in _trackedEntities.Values.Where(e => e.State == EntityState.Unchanged))
        {
            if (HasChanged(tracked))
            {
                var oldState = tracked.State;
                tracked.State = EntityState.Modified;
                OnEntityStateChanged(tracked, oldState, EntityState.Modified);
                changesDetected++;
            }
        }

        return changesDetected;
    }

    /// <inheritdoc />
    public void Clear()
    {
        _trackedEntities.Clear();
    }

    /// <inheritdoc />
    public bool HasChanges()
    {
        return _trackedEntities.Values.Any(e =>
            e.State == EntityState.Added ||
            e.State == EntityState.Modified ||
            e.State == EntityState.Deleted);
    }

    /// <inheritdoc />
    public int GetPendingChangeCount()
    {
        return _trackedEntities.Values.Count(e =>
            e.State == EntityState.Added ||
            e.State == EntityState.Modified ||
            e.State == EntityState.Deleted);
    }

    /// <summary>
    /// Creates a key for the tracked entity dictionary
    /// </summary>
    private static string GetKey(string collectionName, string documentId)
    {
        return $"{collectionName}:{documentId}";
    }

    /// <summary>
    /// Creates a snapshot of the document's data
    /// </summary>
    private Dictionary<string, object?> CreateSnapshot(Document document)
    {
        if (document.Data == null)
        {
            return new Dictionary<string, object?>();
        }

        // Deep clone using JSON serialization
        var json = JsonSerializer.Serialize(document.Data, _snapshotOptions);
        var snapshot = JsonSerializer.Deserialize<Dictionary<string, object?>>(json, _snapshotOptions);
        return snapshot ?? new Dictionary<string, object?>();
    }

    /// <summary>
    /// Checks if an entity has changed from its original values
    /// </summary>
    private bool HasChanged(TrackedEntity tracked)
    {
        if (tracked.OriginalValues == null || tracked.Entity.Data == null)
        {
            return tracked.OriginalValues != null || tracked.Entity.Data != null;
        }

        var currentJson = JsonSerializer.Serialize(tracked.Entity.Data, _snapshotOptions);
        var originalJson = JsonSerializer.Serialize(tracked.OriginalValues, _snapshotOptions);

        return !string.Equals(currentJson, originalJson, StringComparison.Ordinal);
    }

    /// <summary>
    /// Raises the EntityTracked event
    /// </summary>
    protected virtual void OnEntityTracked(TrackedEntity trackedEntity)
    {
        EntityTracked?.Invoke(this, new EntityTrackedEventArgs(trackedEntity));
    }

    /// <summary>
    /// Raises the EntityStateChanged event
    /// </summary>
    protected virtual void OnEntityStateChanged(TrackedEntity trackedEntity, EntityState oldState, EntityState newState)
    {
        EntityStateChanged?.Invoke(this, new EntityStateChangedEventArgs(trackedEntity, oldState, newState));
    }
}
