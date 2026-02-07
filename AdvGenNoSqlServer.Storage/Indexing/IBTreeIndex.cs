// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Diagnostics.CodeAnalysis;

namespace AdvGenNoSqlServer.Storage.Indexing;

/// <summary>
/// Interface for B-tree index supporting generic key types
/// Provides efficient O(log n) lookups, insertions, and deletions
/// </summary>
/// <typeparam name="TKey">The type of keys in the index. Must implement IComparable{TKey}.</typeparam>
/// <typeparam name="TValue">The type of values stored in the index</typeparam>
public interface IBTreeIndex<TKey, TValue> where TKey : IComparable<TKey>
{
    /// <summary>
    /// Gets the name of the index
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the name of the collection this index belongs to
    /// </summary>
    string CollectionName { get; }

    /// <summary>
    /// Gets the field/property name being indexed
    /// </summary>
    string FieldName { get; }

    /// <summary>
    /// Gets whether this index enforces unique keys
    /// </summary>
    bool IsUnique { get; }

    /// <summary>
    /// Gets the number of key-value pairs in the index
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Gets the height of the B-tree (number of levels)
    /// </summary>
    int Height { get; }

    /// <summary>
    /// Inserts a key-value pair into the index
    /// </summary>
    /// <param name="key">The key to insert</param>
    /// <param name="value">The value associated with the key</param>
    /// <returns>True if insertion succeeded, false if key already exists in unique index</returns>
    /// <exception cref="ArgumentNullException">Thrown when key is null</exception>
    /// <exception cref="DuplicateKeyException">Thrown when inserting duplicate key in unique index</exception>
    bool Insert(TKey key, TValue value);

    /// <summary>
    /// Deletes a key from the index
    /// </summary>
    /// <param name="key">The key to delete</param>
    /// <returns>True if key was found and deleted, false otherwise</returns>
    /// <exception cref="ArgumentNullException">Thrown when key is null</exception>
    bool Delete(TKey key);

    /// <summary>
    /// Deletes a specific key-value pair (for non-unique indexes)
    /// </summary>
    /// <param name="key">The key to delete</param>
    /// <param name="value">The specific value to remove</param>
    /// <returns>True if key-value pair was found and deleted, false otherwise</returns>
    bool Delete(TKey key, TValue value);

    /// <summary>
    /// Searches for a key in the index
    /// </summary>
    /// <param name="key">The key to search for</param>
    /// <param name="value">The value associated with the key, if found</param>
    /// <returns>True if key was found, false otherwise</returns>
    bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value);

    /// <summary>
    /// Gets all values associated with a key (for non-unique indexes)
    /// </summary>
    /// <param name="key">The key to search for</param>
    /// <returns>Collection of values associated with the key</returns>
    IEnumerable<TValue> GetValues(TKey key);

    /// <summary>
    /// Performs a range query to find all keys within the specified range
    /// </summary>
    /// <param name="startKey">The start of the range (inclusive)</param>
    /// <param name="endKey">The end of the range (inclusive)</param>
    /// <returns>Collection of key-value pairs within the range</returns>
    IEnumerable<KeyValuePair<TKey, TValue>> RangeQuery(TKey startKey, TKey endKey);

    /// <summary>
    /// Finds all keys greater than or equal to the specified key
    /// </summary>
    /// <param name="key">The key to compare against</param>
    /// <returns>Collection of key-value pairs with keys >= specified key</returns>
    IEnumerable<KeyValuePair<TKey, TValue>> GetGreaterThanOrEqual(TKey key);

    /// <summary>
    /// Finds all keys less than or equal to the specified key
    /// </summary>
    /// <param name="key">The key to compare against</param>
    /// <returns>Collection of key-value pairs with keys <= specified key</returns>
    IEnumerable<KeyValuePair<TKey, TValue>> GetLessThanOrEqual(TKey key);

    /// <summary>
    /// Clears all entries from the index
    /// </summary>
    void Clear();

    /// <summary>
    /// Checks if the index contains the specified key
    /// </summary>
    /// <param name="key">The key to check</param>
    /// <returns>True if key exists, false otherwise</returns>
    bool ContainsKey(TKey key);

    /// <summary>
    /// Gets all key-value pairs in the index in sorted order
    /// </summary>
    /// <returns>Collection of all key-value pairs</returns>
    IEnumerable<KeyValuePair<TKey, TValue>> GetAll();

    /// <summary>
    /// Updates the value for an existing key
    /// </summary>
    /// <param name="key">The key to update</param>
    /// <param name="newValue">The new value</param>
    /// <returns>True if key was found and updated, false otherwise</returns>
    bool Update(TKey key, TValue newValue);

    /// <summary>
    /// Gets the minimum key in the index
    /// </summary>
    /// <param name="key">The minimum key, if found</param>
    /// <param name="value">The value associated with the minimum key</param>
    /// <returns>True if index is not empty, false otherwise</returns>
    bool TryGetMin([MaybeNullWhen(false)] out TKey key, [MaybeNullWhen(false)] out TValue value);

    /// <summary>
    /// Gets the maximum key in the index
    /// </summary>
    /// <param name="key">The maximum key, if found</param>
    /// <param name="value">The value associated with the maximum key</param>
    /// <returns>True if index is not empty, false otherwise</returns>
    bool TryGetMax([MaybeNullWhen(false)] out TKey key, [MaybeNullWhen(false)] out TValue value);
}

/// <summary>
/// Exception thrown when attempting to insert a duplicate key into a unique index
/// </summary>
public class DuplicateKeyException : Exception
{
    public DuplicateKeyException(string message) : base(message) { }
    public DuplicateKeyException(string message, Exception innerException) : base(message, innerException) { }
}
