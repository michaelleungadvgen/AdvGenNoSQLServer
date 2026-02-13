// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Storage;

/// <summary>
/// Interface for atomic update operations on documents.
/// Provides MongoDB-like atomic update operators for field-level modifications.
/// </summary>
public interface IAtomicUpdateOperations : IDocumentStore
{
    /// <summary>
    /// Atomically increments a numeric field by the specified amount.
    /// If the field doesn't exist, it is set to the increment value.
    /// </summary>
    /// <param name="collectionName">The name of the collection</param>
    /// <param name="documentId">The ID of the document to update</param>
    /// <param name="fieldPath">The path to the numeric field (supports dot notation for nested fields)</param>
    /// <param name="increment">The amount to increment (can be negative for decrement)</param>
    /// <returns>The updated document with the new field value</returns>
    /// <exception cref="DocumentNotFoundException">Thrown when document ID does not exist</exception>
    /// <exception cref="AtomicUpdateException">Thrown when the field is not a numeric type</exception>
    Task<Document> IncrementAsync(string collectionName, string documentId, string fieldPath, double increment);

    /// <summary>
    /// Atomically pushes a value to an array field.
    /// If the field doesn't exist, it creates a new array with the value.
    /// </summary>
    /// <param name="collectionName">The name of the collection</param>
    /// <param name="documentId">The ID of the document to update</param>
    /// <param name="fieldPath">The path to the array field (supports dot notation for nested fields)</param>
    /// <param name="value">The value to push to the array</param>
    /// <returns>The updated document with the modified array</returns>
    /// <exception cref="DocumentNotFoundException">Thrown when document ID does not exist</exception>
    /// <exception cref="AtomicUpdateException">Thrown when the field is not an array type</exception>
    Task<Document> PushAsync(string collectionName, string documentId, string fieldPath, object value);

    /// <summary>
    /// Atomically pushes multiple values to an array field.
    /// If the field doesn't exist, it creates a new array with the values.
    /// </summary>
    /// <param name="collectionName">The name of the collection</param>
    /// <param name="documentId">The ID of the document to update</param>
    /// <param name="fieldPath">The path to the array field (supports dot notation for nested fields)</param>
    /// <param name="values">The values to push to the array</param>
    /// <returns>The updated document with the modified array</returns>
    /// <exception cref="DocumentNotFoundException">Thrown when document ID does not exist</exception>
    Task<Document> PushManyAsync(string collectionName, string documentId, string fieldPath, IEnumerable<object> values);

    /// <summary>
    /// Atomically pulls (removes) all occurrences of a value from an array field.
    /// </summary>
    /// <param name="collectionName">The name of the collection</param>
    /// <param name="documentId">The ID of the document to update</param>
    /// <param name="fieldPath">The path to the array field (supports dot notation for nested fields)</param>
    /// <param name="value">The value to remove from the array</param>
    /// <returns>The updated document with the modified array</returns>
    /// <exception cref="DocumentNotFoundException">Thrown when document ID does not exist</exception>
    /// <exception cref="AtomicUpdateException">Thrown when the field is not an array type</exception>
    Task<Document> PullAsync(string collectionName, string documentId, string fieldPath, object value);

    /// <summary>
    /// Atomically pulls (removes) all values that match any value in the provided list from an array field.
    /// </summary>
    /// <param name="collectionName">The name of the collection</param>
    /// <param name="documentId">The ID of the document to update</param>
    /// <param name="fieldPath">The path to the array field (supports dot notation for nested fields)</param>
    /// <param name="values">The values to remove from the array</param>
    /// <returns>The updated document with the modified array</returns>
    /// <exception cref="DocumentNotFoundException">Thrown when document ID does not exist</exception>
    Task<Document> PullManyAsync(string collectionName, string documentId, string fieldPath, IEnumerable<object> values);

    /// <summary>
    /// Atomically sets a field to the specified value.
    /// Creates the field if it doesn't exist.
    /// </summary>
    /// <param name="collectionName">The name of the collection</param>
    /// <param name="documentId">The ID of the document to update</param>
    /// <param name="fieldPath">The path to the field (supports dot notation for nested fields)</param>
    /// <param name="value">The value to set</param>
    /// <returns>The updated document with the modified field</returns>
    /// <exception cref="DocumentNotFoundException">Thrown when document ID does not exist</exception>
    Task<Document> SetAsync(string collectionName, string documentId, string fieldPath, object value);

    /// <summary>
    /// Atomically unsets (removes) a field from the document.
    /// </summary>
    /// <param name="collectionName">The name of the collection</param>
    /// <param name="documentId">The ID of the document to update</param>
    /// <param name="fieldPath">The path to the field to remove (supports dot notation for nested fields)</param>
    /// <returns>The updated document with the field removed</returns>
    /// <exception cref="DocumentNotFoundException">Thrown when document ID does not exist</exception>
    Task<Document> UnsetAsync(string collectionName, string documentId, string fieldPath);

    /// <summary>
    /// Atomically applies multiple update operations to a document in a single operation.
    /// </summary>
    /// <param name="collectionName">The name of the collection</param>
    /// <param name="documentId">The ID of the document to update</param>
    /// <param name="operations">The list of atomic operations to apply</param>
    /// <returns>The updated document with all modifications applied</returns>
    /// <exception cref="DocumentNotFoundException">Thrown when document ID does not exist</exception>
    /// <exception cref="AtomicUpdateException">Thrown when any operation fails</exception>
    Task<Document> UpdateMultipleAsync(string collectionName, string documentId, IEnumerable<AtomicOperation> operations);
}

/// <summary>
/// Represents a single atomic operation to be applied to a document
/// </summary>
public class AtomicOperation
{
    /// <summary>
    /// The type of atomic operation
    /// </summary>
    public AtomicOperationType Type { get; }

    /// <summary>
    /// The path to the field (supports dot notation for nested fields)
    /// </summary>
    public string FieldPath { get; }

    /// <summary>
    /// The value for the operation (null for Unset)
    /// </summary>
    public object? Value { get; }

    /// <summary>
    /// Creates a new atomic operation
    /// </summary>
    /// <param name="type">The operation type</param>
    /// <param name="fieldPath">The field path</param>
    /// <param name="value">The operation value</param>
    public AtomicOperation(AtomicOperationType type, string fieldPath, object? value = null)
    {
        Type = type;
        FieldPath = fieldPath ?? throw new ArgumentNullException(nameof(fieldPath));
        Value = value;
    }

    /// <summary>
    /// Creates an Increment operation
    /// </summary>
    public static AtomicOperation Increment(string fieldPath, double amount) =>
        new(AtomicOperationType.Increment, fieldPath, amount);

    /// <summary>
    /// Creates a Push operation
    /// </summary>
    public static AtomicOperation Push(string fieldPath, object value) =>
        new(AtomicOperationType.Push, fieldPath, value);

    /// <summary>
    /// Creates a Pull operation
    /// </summary>
    public static AtomicOperation Pull(string fieldPath, object value) =>
        new(AtomicOperationType.Pull, fieldPath, value);

    /// <summary>
    /// Creates a Set operation
    /// </summary>
    public static AtomicOperation Set(string fieldPath, object value) =>
        new(AtomicOperationType.Set, fieldPath, value);

    /// <summary>
    /// Creates an Unset operation
    /// </summary>
    public static AtomicOperation Unset(string fieldPath) =>
        new(AtomicOperationType.Unset, fieldPath, null);
}

/// <summary>
/// Types of atomic operations supported
/// </summary>
public enum AtomicOperationType
{
    /// <summary>
    /// Increment a numeric field
    /// </summary>
    Increment,

    /// <summary>
    /// Push a value to an array
    /// </summary>
    Push,

    /// <summary>
    /// Pull a value from an array
    /// </summary>
    Pull,

    /// <summary>
    /// Set a field value
    /// </summary>
    Set,

    /// <summary>
    /// Unset (remove) a field
    /// </summary>
    Unset
}

/// <summary>
/// Exception thrown when an atomic update operation fails
/// </summary>
public class AtomicUpdateException : DocumentStoreException
{
    public string CollectionName { get; }
    public string DocumentId { get; }
    public string FieldPath { get; }
    public AtomicOperationType? OperationType { get; }

    public AtomicUpdateException(string collectionName, string documentId, string fieldPath, string message)
        : base($"Atomic update failed for '{fieldPath}' in document '{documentId}': {message}")
    {
        CollectionName = collectionName;
        DocumentId = documentId;
        FieldPath = fieldPath;
    }

    public AtomicUpdateException(string collectionName, string documentId, string fieldPath, AtomicOperationType operationType, string message)
        : base($"Atomic update failed for '{fieldPath}' in document '{documentId}' ({operationType}): {message}")
    {
        CollectionName = collectionName;
        DocumentId = documentId;
        FieldPath = fieldPath;
        OperationType = operationType;
    }
}
