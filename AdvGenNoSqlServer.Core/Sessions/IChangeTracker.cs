// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Core.Sessions;

/// <summary>
/// Represents the state of an entity being tracked
/// </summary>
public enum EntityState
{
    /// <summary>
    /// Entity is not being tracked
    /// </summary>
    Detached,

    /// <summary>
    /// Entity is unchanged from the database
    /// </summary>
    Unchanged,

    /// <summary>
    /// Entity is new and will be inserted
    /// </summary>
    Added,

    /// <summary>
    /// Entity has been modified and will be updated
    /// </summary>
    Modified,

    /// <summary>
    /// Entity has been marked for deletion
    /// </summary>
    Deleted
}

/// <summary>
/// Represents information about a tracked entity
/// </summary>
public class TrackedEntity
{
    /// <summary>
    /// The collection name where the entity is stored
    /// </summary>
    public string CollectionName { get; }

    /// <summary>
    /// The entity/document being tracked
    /// </summary>
    public Document Entity { get; }

    /// <summary>
    /// The current state of the entity
    /// </summary>
    public EntityState State { get; set; }

    /// <summary>
    /// The original values of the entity when it was first tracked
    /// </summary>
    public Dictionary<string, object?>? OriginalValues { get; set; }

    /// <summary>
    /// When the entity started being tracked
    /// </summary>
    public DateTime TrackedAt { get; }

    /// <summary>
    /// Creates a new tracked entity
    /// </summary>
    public TrackedEntity(string collectionName, Document entity, EntityState state)
    {
        CollectionName = collectionName ?? throw new ArgumentNullException(nameof(collectionName));
        Entity = entity ?? throw new ArgumentNullException(nameof(entity));
        State = state;
        TrackedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Creates a tracked entity with original values snapshot
    /// </summary>
    public TrackedEntity(string collectionName, Document entity, EntityState state, Dictionary<string, object?> originalValues)
        : this(collectionName, entity, state)
    {
        OriginalValues = originalValues;
    }
}

/// <summary>
/// Interface for tracking entity changes within a session
/// </summary>
public interface IChangeTracker
{
    /// <summary>
    /// Gets all tracked entities
    /// </summary>
    IReadOnlyCollection<TrackedEntity> TrackedEntities { get; }

    /// <summary>
    /// Gets the number of tracked entities
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Gets entities in a specific state
    /// </summary>
    IEnumerable<TrackedEntity> GetEntities(EntityState state);

    /// <summary>
    /// Gets the entity state for a specific document
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="documentId">The document ID</param>
    /// <returns>The entity state, or Detached if not tracked</returns>
    EntityState GetEntityState(string collectionName, string documentId);

    /// <summary>
    /// Tracks an entity as added
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="entity">The entity to track</param>
    void TrackAdded(string collectionName, Document entity);

    /// <summary>
    /// Tracks an entity as unchanged (original values captured)
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="entity">The entity to track</param>
    void TrackUnchanged(string collectionName, Document entity);

    /// <summary>
    /// Tracks an entity as modified
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="entity">The modified entity</param>
    void TrackModified(string collectionName, Document entity);

    /// <summary>
    /// Tracks an entity as deleted
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="documentId">The ID of the entity to delete</param>
    void TrackDeleted(string collectionName, string documentId);

    /// <summary>
    /// Removes an entity from tracking
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="documentId">The document ID</param>
    /// <returns>True if the entity was removed, false if not found</returns>
    bool Untrack(string collectionName, string documentId);

    /// <summary>
    /// Checks if an entity is being tracked
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="documentId">The document ID</param>
    /// <returns>True if tracked, false otherwise</returns>
    bool IsTracked(string collectionName, string documentId);

    /// <summary>
    /// Gets a tracked entity
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="documentId">The document ID</param>
    /// <returns>The tracked entity, or null if not found</returns>
    TrackedEntity? GetTrackedEntity(string collectionName, string documentId);

    /// <summary>
    /// Detects changes in all tracked entities and updates their states
    /// </summary>
    /// <returns>The number of entities with detected changes</returns>
    int DetectChanges();

    /// <summary>
    /// Clears all tracked entities
    /// </summary>
    void Clear();

    /// <summary>
    /// Checks if there are any pending changes (Added, Modified, or Deleted entities)
    /// </summary>
    bool HasChanges();

    /// <summary>
    /// Gets the number of pending changes
    /// </summary>
    int GetPendingChangeCount();

    /// <summary>
    /// Event raised when an entity is tracked
    /// </summary>
    event EventHandler<EntityTrackedEventArgs>? EntityTracked;

    /// <summary>
    /// Event raised when an entity's state changes
    /// </summary>
    event EventHandler<EntityStateChangedEventArgs>? EntityStateChanged;
}

/// <summary>
/// Event arguments for entity tracked events
/// </summary>
public class EntityTrackedEventArgs : EventArgs
{
    /// <summary>
    /// The tracked entity information
    /// </summary>
    public TrackedEntity TrackedEntity { get; }

    /// <summary>
    /// Creates new entity tracked event args
    /// </summary>
    public EntityTrackedEventArgs(TrackedEntity trackedEntity)
    {
        TrackedEntity = trackedEntity ?? throw new ArgumentNullException(nameof(trackedEntity));
    }
}

/// <summary>
/// Event arguments for entity state changed events
/// </summary>
public class EntityStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// The tracked entity whose state changed
    /// </summary>
    public TrackedEntity TrackedEntity { get; }

    /// <summary>
    /// The previous entity state
    /// </summary>
    public EntityState OldState { get; }

    /// <summary>
    /// The new entity state
    /// </summary>
    public EntityState NewState { get; }

    /// <summary>
    /// Creates new entity state changed event args
    /// </summary>
    public EntityStateChangedEventArgs(TrackedEntity trackedEntity, EntityState oldState, EntityState newState)
    {
        TrackedEntity = trackedEntity ?? throw new ArgumentNullException(nameof(trackedEntity));
        OldState = oldState;
        NewState = newState;
    }
}
