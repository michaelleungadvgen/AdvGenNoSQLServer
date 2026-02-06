// Copyright (c) 2026 [Your Organization]
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Storage;

/// <summary>
/// Interface for persistent document store with file-based storage capabilities
/// Extends IDocumentStore with persistence operations
/// </summary>
public interface IPersistentDocumentStore : IDocumentStore
{
    /// <summary>
    /// Gets the base path where collection data is stored
    /// </summary>
    string DataPath { get; }

    /// <summary>
    /// Initializes the persistent store by loading existing collections from disk
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Persists all in-memory changes to disk
    /// </summary>
    Task SaveChangesAsync();

    /// <summary>
    /// Persists a specific collection to disk
    /// </summary>
    /// <param name="collectionName">The name of the collection to persist</param>
    Task SaveCollectionAsync(string collectionName);

    /// <summary>
    /// Loads a collection from disk into memory
    /// </summary>
    /// <param name="collectionName">The name of the collection to load</param>
    Task LoadCollectionAsync(string collectionName);

    /// <summary>
    /// Checks if a collection exists on disk
    /// </summary>
    /// <param name="collectionName">The name of the collection to check</param>
    /// <returns>True if the collection exists on disk, false otherwise</returns>
    Task<bool> CollectionExistsOnDiskAsync(string collectionName);
}
