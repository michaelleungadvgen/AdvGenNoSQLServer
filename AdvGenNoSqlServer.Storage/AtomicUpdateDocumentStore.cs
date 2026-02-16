// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Storage;

/// <summary>
/// Document store implementation with atomic update operations support.
/// Provides MongoDB-like atomic update operators for field-level modifications.
/// </summary>
public class AtomicUpdateDocumentStore : DocumentStore, IAtomicUpdateOperations
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _documentLocks;

    /// <summary>
    /// Creates a new AtomicUpdateDocumentStore instance
    /// </summary>
    public AtomicUpdateDocumentStore()
    {
        _documentLocks = new ConcurrentDictionary<string, SemaphoreSlim>();
    }

    /// <inheritdoc />
    public async Task<Document> IncrementAsync(string collectionName, string documentId, string fieldPath, double increment)
    {
        ValidateParameters(collectionName, documentId, fieldPath);

        var lockKey = $"{collectionName}:{documentId}";
        var docLock = _documentLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

        await docLock.WaitAsync();
        try
        {
            var document = await GetAsync(collectionName, documentId);
            if (document == null)
            {
                throw new DocumentNotFoundException(collectionName, documentId);
            }

            // Clone the document data to avoid modifying the original
            var newData = CloneData(document.Data);
            
            // Get or create the current value
            var currentValue = GetFieldValue(newData, fieldPath);
            double newValue;

            if (currentValue == null)
            {
                // Field doesn't exist, set it to the increment value
                newValue = increment;
            }
            else
            {
                // Try to parse the current value as a number
                if (!TryConvertToDouble(currentValue, out var currentNumeric))
                {
                    throw new AtomicUpdateException(
                        collectionName, documentId, fieldPath, AtomicOperationType.Increment,
                        $"Field value '{currentValue}' is not a numeric type");
                }
                newValue = currentNumeric + increment;
            }

            // Set the new value
            SetFieldValue(newData, fieldPath, newValue);

            // Update the document
            var updatedDocument = new Document
            {
                Id = documentId,
                Data = newData,
                CreatedAt = document.CreatedAt,
                UpdatedAt = DateTime.UtcNow,
                Version = document.Version + 1
            };

            return await UpdateAsync(collectionName, updatedDocument);
        }
        finally
        {
            docLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<Document> PushAsync(string collectionName, string documentId, string fieldPath, object value)
    {
        return await PushManyAsync(collectionName, documentId, fieldPath, new[] { value });
    }

    /// <inheritdoc />
    public async Task<Document> PushManyAsync(string collectionName, string documentId, string fieldPath, IEnumerable<object> values)
    {
        ValidateParameters(collectionName, documentId, fieldPath);

        if (values == null)
            throw new ArgumentNullException(nameof(values));

        var valueList = values.ToList();
        if (valueList.Count == 0)
        {
            // Nothing to push, return the document as-is
            var doc = await GetAsync(collectionName, documentId);
            if (doc == null)
                throw new DocumentNotFoundException(collectionName, documentId);
            return doc;
        }

        var lockKey = $"{collectionName}:{documentId}";
        var docLock = _documentLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

        await docLock.WaitAsync();
        try
        {
            var document = await GetAsync(collectionName, documentId);
            if (document == null)
            {
                throw new DocumentNotFoundException(collectionName, documentId);
            }

            // Clone the document data to avoid modifying the original
            var newData = CloneData(document.Data);

            // Get or create the array
            var currentValue = GetFieldValue(newData, fieldPath);
            List<object> array;

            if (currentValue == null)
            {
                // Field doesn't exist, create a new array
                array = new List<object>();
            }
            else if (currentValue is List<object> list)
            {
                array = list;
            }
            else if (currentValue is System.Collections.IEnumerable enumerable && currentValue is not string)
            {
                // Convert other enumerable types to list
                array = enumerable.Cast<object>().ToList();
            }
            else
            {
                throw new AtomicUpdateException(
                    collectionName, documentId, fieldPath, AtomicOperationType.Push,
                    $"Field value is not an array type");
            }

            // Add the values
            array.AddRange(valueList);

            // Set the updated array
            SetFieldValue(newData, fieldPath, array);

            // Update the document
            var updatedDocument = new Document
            {
                Id = documentId,
                Data = newData,
                CreatedAt = document.CreatedAt,
                UpdatedAt = DateTime.UtcNow,
                Version = document.Version + 1
            };

            return await UpdateAsync(collectionName, updatedDocument);
        }
        finally
        {
            docLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<Document> PullAsync(string collectionName, string documentId, string fieldPath, object value)
    {
        return await PullManyAsync(collectionName, documentId, fieldPath, new[] { value });
    }

    /// <inheritdoc />
    public async Task<Document> PullManyAsync(string collectionName, string documentId, string fieldPath, IEnumerable<object> values)
    {
        ValidateParameters(collectionName, documentId, fieldPath);

        if (values == null)
            throw new ArgumentNullException(nameof(values));

        var valuesToRemove = values.ToList();
        if (valuesToRemove.Count == 0)
        {
            // Nothing to pull, return the document as-is
            var doc = await GetAsync(collectionName, documentId);
            if (doc == null)
                throw new DocumentNotFoundException(collectionName, documentId);
            return doc;
        }

        var lockKey = $"{collectionName}:{documentId}";
        var docLock = _documentLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

        await docLock.WaitAsync();
        try
        {
            var document = await GetAsync(collectionName, documentId);
            if (document == null)
            {
                throw new DocumentNotFoundException(collectionName, documentId);
            }

            // Clone the document data to avoid modifying the original
            var newData = CloneData(document.Data);

            // Get the array
            var currentValue = GetFieldValue(newData, fieldPath);
            
            if (currentValue == null)
            {
                // Field doesn't exist, nothing to pull
                return document;
            }

            List<object> array;
            if (currentValue is List<object> list)
            {
                array = list;
            }
            else if (currentValue is System.Collections.IEnumerable enumerable && currentValue is not string)
            {
                array = enumerable.Cast<object>().ToList();
            }
            else
            {
                throw new AtomicUpdateException(
                    collectionName, documentId, fieldPath, AtomicOperationType.Pull,
                    $"Field value is not an array type");
            }

            // Remove all matching values
            array.RemoveAll(item => valuesToRemove.Any(v => AreValuesEqual(item, v)));

            // Set the updated array
            SetFieldValue(newData, fieldPath, array);

            // Update the document
            var updatedDocument = new Document
            {
                Id = documentId,
                Data = newData,
                CreatedAt = document.CreatedAt,
                UpdatedAt = DateTime.UtcNow,
                Version = document.Version + 1
            };

            return await UpdateAsync(collectionName, updatedDocument);
        }
        finally
        {
            docLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<Document> SetAsync(string collectionName, string documentId, string fieldPath, object value)
    {
        ValidateParameters(collectionName, documentId, fieldPath);

        var lockKey = $"{collectionName}:{documentId}";
        var docLock = _documentLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

        await docLock.WaitAsync();
        try
        {
            var document = await GetAsync(collectionName, documentId);
            if (document == null)
            {
                throw new DocumentNotFoundException(collectionName, documentId);
            }

            // Clone the document data to avoid modifying the original
            var newData = CloneData(document.Data);

            // Set the value
            SetFieldValue(newData, fieldPath, value);

            // Update the document
            var updatedDocument = new Document
            {
                Id = documentId,
                Data = newData,
                CreatedAt = document.CreatedAt,
                UpdatedAt = DateTime.UtcNow,
                Version = document.Version + 1
            };

            return await UpdateAsync(collectionName, updatedDocument);
        }
        finally
        {
            docLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<Document> UnsetAsync(string collectionName, string documentId, string fieldPath)
    {
        ValidateParameters(collectionName, documentId, fieldPath);

        var lockKey = $"{collectionName}:{documentId}";
        var docLock = _documentLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

        await docLock.WaitAsync();
        try
        {
            var document = await GetAsync(collectionName, documentId);
            if (document == null)
            {
                throw new DocumentNotFoundException(collectionName, documentId);
            }

            // Clone the document data to avoid modifying the original
            var newData = CloneData(document.Data);

            // Remove the field
            RemoveField(newData, fieldPath);

            // Update the document
            var updatedDocument = new Document
            {
                Id = documentId,
                Data = newData,
                CreatedAt = document.CreatedAt,
                UpdatedAt = DateTime.UtcNow,
                Version = document.Version + 1
            };

            return await UpdateAsync(collectionName, updatedDocument);
        }
        finally
        {
            docLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<Document> UpdateMultipleAsync(string collectionName, string documentId, IEnumerable<AtomicOperation> operations)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Document ID cannot be empty", nameof(documentId));

        if (operations == null)
            throw new ArgumentNullException(nameof(operations));

        var operationList = operations.ToList();
        if (operationList.Count == 0)
        {
            // No operations to apply, return the document as-is
            var doc = await GetAsync(collectionName, documentId);
            if (doc == null)
                throw new DocumentNotFoundException(collectionName, documentId);
            return doc;
        }

        var lockKey = $"{collectionName}:{documentId}";
        var docLock = _documentLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

        await docLock.WaitAsync();
        try
        {
            var document = await GetAsync(collectionName, documentId);
            if (document == null)
            {
                throw new DocumentNotFoundException(collectionName, documentId);
            }

            // Clone the document data to avoid modifying the original
            var newData = CloneData(document.Data);
            var currentData = newData;

            // Apply each operation in sequence
            foreach (var operation in operationList)
            {
                ApplyOperation(currentData, operation, collectionName, documentId);
            }

            // Update the document
            var updatedDocument = new Document
            {
                Id = documentId,
                Data = currentData,
                CreatedAt = document.CreatedAt,
                UpdatedAt = DateTime.UtcNow,
                Version = document.Version + 1
            };

            return await UpdateAsync(collectionName, updatedDocument);
        }
        finally
        {
            docLock.Release();
        }
    }

    #region Helper Methods

    private void ValidateParameters(string collectionName, string documentId, string fieldPath)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Document ID cannot be empty", nameof(documentId));

        if (string.IsNullOrWhiteSpace(fieldPath))
            throw new ArgumentException("Field path cannot be empty", nameof(fieldPath));
    }

    private Dictionary<string, object> CloneData(Dictionary<string, object>? data)
    {
        if (data == null)
            return new Dictionary<string, object>();

        var clone = new Dictionary<string, object>();
        foreach (var kvp in data)
        {
            clone[kvp.Key] = CloneValue(kvp.Value);
        }
        return clone;
    }

    private object? CloneValue(object? value)
    {
        if (value == null)
            return null;

        if (value is Dictionary<string, object> dict)
        {
            return CloneData(dict);
        }

        if (value is List<object> list)
        {
            return list.Select(CloneValue).ToList();
        }

        // For primitive types, just return the value
        return value;
    }

    private object? GetFieldValue(Dictionary<string, object> data, string fieldPath)
    {
        var parts = fieldPath.Split('.');
        var current = data;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (!current.TryGetValue(parts[i], out var next) || next is not Dictionary<string, object> nextDict)
            {
                return null;
            }
            current = nextDict;
        }

        current.TryGetValue(parts[parts.Length - 1], out var value);
        return value;
    }

    private void SetFieldValue(Dictionary<string, object> data, string fieldPath, object? value)
    {
        var parts = fieldPath.Split('.');
        var current = data;

        // Create nested dictionaries as needed
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (!current.TryGetValue(parts[i], out var next) || next is not Dictionary<string, object> nextDict)
            {
                nextDict = new Dictionary<string, object>();
                current[parts[i]] = nextDict;
            }
            current = nextDict;
        }

        current[parts[parts.Length - 1]] = value!;
    }

    private void RemoveField(Dictionary<string, object> data, string fieldPath)
    {
        var parts = fieldPath.Split('.');
        var current = data;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (!current.TryGetValue(parts[i], out var next) || next is not Dictionary<string, object> nextDict)
            {
                return; // Path doesn't exist, nothing to remove
            }
            current = nextDict;
        }

        current.Remove(parts[parts.Length - 1]);
    }

    private bool TryConvertToDouble(object value, out double result)
    {
        result = 0;

        if (value is double d)
        {
            result = d;
            return true;
        }

        if (value is float f)
        {
            result = f;
            return true;
        }

        if (value is int i)
        {
            result = i;
            return true;
        }

        if (value is long l)
        {
            result = l;
            return true;
        }

        if (value is decimal dec)
        {
            result = (double)dec;
            return true;
        }

        if (value is short s)
        {
            result = s;
            return true;
        }

        if (value is byte b)
        {
            result = b;
            return true;
        }

        // Try parsing as string
        if (value is string str)
        {
            return double.TryParse(str, out result);
        }

        // Try converting via IConvertible
        if (value is IConvertible convertible)
        {
            try
            {
                result = convertible.ToDouble(null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    private bool AreValuesEqual(object? a, object? b)
    {
        if (ReferenceEquals(a, b))
            return true;

        if (a == null || b == null)
            return a == b;

        if (a.GetType() != b.GetType())
        {
            // Try numeric comparison
            if (TryConvertToDouble(a, out var aNum) && TryConvertToDouble(b, out var bNum))
            {
                return Math.Abs(aNum - bNum) < 0.0001;
            }

            // Try string comparison
            return a.ToString() == b.ToString();
        }

        if (a is Dictionary<string, object> aDict && b is Dictionary<string, object> bDict)
        {
            if (aDict.Count != bDict.Count)
                return false;
            return aDict.All(kvp => bDict.TryGetValue(kvp.Key, out var bValue) && AreValuesEqual(kvp.Value, bValue));
        }

        if (a is List<object> aList && b is List<object> bList)
        {
            if (aList.Count != bList.Count)
                return false;
            return !aList.Where((t, i) => !AreValuesEqual(t, bList[i])).Any();
        }

        return a.Equals(b);
    }

    private void ApplyOperation(Dictionary<string, object> data, AtomicOperation operation, string collectionName, string documentId)
    {
        switch (operation.Type)
        {
            case AtomicOperationType.Increment:
                ApplyIncrement(data, operation, collectionName, documentId);
                break;

            case AtomicOperationType.Push:
                ApplyPush(data, operation, collectionName, documentId);
                break;

            case AtomicOperationType.Pull:
                ApplyPull(data, operation, collectionName, documentId);
                break;

            case AtomicOperationType.Set:
                SetFieldValue(data, operation.FieldPath, operation.Value);
                break;

            case AtomicOperationType.Unset:
                RemoveField(data, operation.FieldPath);
                break;

            default:
                throw new AtomicUpdateException(
                    collectionName, documentId, operation.FieldPath, operation.Type,
                    $"Unknown operation type: {operation.Type}");
        }
    }

    private void ApplyIncrement(Dictionary<string, object> data, AtomicOperation operation, string collectionName, string documentId)
    {
        if (operation.Value == null || !TryConvertToDouble(operation.Value, out var increment))
        {
            throw new AtomicUpdateException(
                collectionName, documentId, operation.FieldPath, AtomicOperationType.Increment,
                "Increment value must be numeric");
        }

        var currentValue = GetFieldValue(data, operation.FieldPath);
        double newValue;

        if (currentValue == null)
        {
            newValue = increment;
        }
        else if (!TryConvertToDouble(currentValue, out var currentNumeric))
        {
            throw new AtomicUpdateException(
                collectionName, documentId, operation.FieldPath, AtomicOperationType.Increment,
                $"Field value '{currentValue}' is not a numeric type");
        }
        else
        {
            newValue = currentNumeric + increment;
        }

        SetFieldValue(data, operation.FieldPath, newValue);
    }

    private void ApplyPush(Dictionary<string, object> data, AtomicOperation operation, string collectionName, string documentId)
    {
        var currentValue = GetFieldValue(data, operation.FieldPath);
        List<object> array;

        if (currentValue == null)
        {
            array = new List<object>();
        }
        else if (currentValue is List<object> list)
        {
            array = list;
        }
        else if (currentValue is System.Collections.IEnumerable enumerable && currentValue is not string)
        {
            array = enumerable.Cast<object>().ToList();
        }
        else
        {
            throw new AtomicUpdateException(
                collectionName, documentId, operation.FieldPath, AtomicOperationType.Push,
                $"Field value is not an array type");
        }

        if (operation.Value != null)
        {
            array.Add(operation.Value);
        }

        SetFieldValue(data, operation.FieldPath, array);
    }

    private void ApplyPull(Dictionary<string, object> data, AtomicOperation operation, string collectionName, string documentId)
    {
        var currentValue = GetFieldValue(data, operation.FieldPath);

        if (currentValue == null)
        {
            return; // Nothing to pull
        }

        List<object> array;
        if (currentValue is List<object> list)
        {
            array = list;
        }
        else if (currentValue is System.Collections.IEnumerable enumerable && currentValue is not string)
        {
            array = enumerable.Cast<object>().ToList();
        }
        else
        {
            throw new AtomicUpdateException(
                collectionName, documentId, operation.FieldPath, AtomicOperationType.Pull,
                $"Field value is not an array type");
        }

        if (operation.Value != null)
        {
            array.RemoveAll(item => AreValuesEqual(item, operation.Value));
        }

        SetFieldValue(data, operation.FieldPath, array);
    }

    #endregion

    #region Insert, Replace, Upsert Operations

    /// <inheritdoc />
    public async Task<Document> InsertAsync(string collectionName, string documentId, Dictionary<string, object> data)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Document ID cannot be empty", nameof(documentId));

        if (data == null)
            throw new ArgumentNullException(nameof(data));

        var lockKey = $"{collectionName}:{documentId}";
        var docLock = _documentLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

        await docLock.WaitAsync();
        try
        {
            // Check if document already exists
            var existingDocument = await GetAsync(collectionName, documentId);
            if (existingDocument != null)
            {
                throw new DocumentAlreadyExistsException(collectionName, documentId);
            }

            // Clone the data to avoid modifying the original
            var clonedData = CloneData(data);

            // Create the new document
            var now = DateTime.UtcNow;
            var document = new Document
            {
                Id = documentId,
                Data = clonedData,
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1
            };

            // Insert using base class
            var result = await base.InsertAsync(collectionName, document);
            return result;
        }
        finally
        {
            docLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<Document> ReplaceAsync(string collectionName, string documentId, Dictionary<string, object> data)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Document ID cannot be empty", nameof(documentId));

        if (data == null)
            throw new ArgumentNullException(nameof(data));

        var lockKey = $"{collectionName}:{documentId}";
        var docLock = _documentLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

        await docLock.WaitAsync();
        try
        {
            // Check if document exists
            var existingDocument = await GetAsync(collectionName, documentId);
            if (existingDocument == null)
            {
                throw new DocumentNotFoundException(collectionName, documentId);
            }

            // Clone the data to avoid modifying the original
            var clonedData = CloneData(data);

            // Create the replacement document
            var document = new Document
            {
                Id = documentId,
                Data = clonedData,
                CreatedAt = existingDocument.CreatedAt,
                UpdatedAt = DateTime.UtcNow,
                Version = existingDocument.Version + 1
            };

            // Replace using base class
            var result = await base.UpdateAsync(collectionName, document);
            return result;
        }
        finally
        {
            docLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<(Document Document, bool WasInserted)> UpsertAsync(string collectionName, string documentId, Dictionary<string, object> data)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));

        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Document ID cannot be empty", nameof(documentId));

        if (data == null)
            throw new ArgumentNullException(nameof(data));

        var lockKey = $"{collectionName}:{documentId}";
        var docLock = _documentLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

        await docLock.WaitAsync();
        try
        {
            // Clone the data to avoid modifying the original
            var clonedData = CloneData(data);

            // Check if document exists
            var existingDocument = await GetAsync(collectionName, documentId);

            if (existingDocument == null)
            {
                // Insert new document
                var now = DateTime.UtcNow;
                var document = new Document
                {
                    Id = documentId,
                    Data = clonedData,
                    CreatedAt = now,
                    UpdatedAt = now,
                    Version = 1
                };

                var result = await base.InsertAsync(collectionName, document);
                return (result, true);
            }
            else
            {
                // Update existing document
                var document = new Document
                {
                    Id = documentId,
                    Data = clonedData,
                    CreatedAt = existingDocument.CreatedAt,
                    UpdatedAt = DateTime.UtcNow,
                    Version = existingDocument.Version + 1
                };

                var result = await base.UpdateAsync(collectionName, document);
                return (result, false);
            }
        }
        finally
        {
            docLock.Release();
        }
    }

    #endregion
}
