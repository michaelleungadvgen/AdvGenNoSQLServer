// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Core.Abstractions;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Core.Patches;

namespace AdvGenNoSqlServer.Storage.Patches
{
    /// <summary>
    /// Document store wrapper that adds server-side patch operation capabilities.
    /// </summary>
    public class PatchDocumentStore : IServerSidePatch, IDisposable
    {
        private readonly IDocumentStore _innerStore;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _documentLocks;
        private readonly PatchStatistics _statistics;
        private long _totalOperations;
        private long _successfulOperations;
        private long _failedOperations;
        private long _documentsModified;
        private long _upsertsPerformed;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="PatchDocumentStore"/> class.
        /// </summary>
        /// <param name="innerStore">The underlying document store.</param>
        public PatchDocumentStore(IDocumentStore innerStore)
        {
            _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
            _documentLocks = new ConcurrentDictionary<string, SemaphoreSlim>();
            _statistics = new PatchStatistics();
        }

        /// <inheritdoc />
        public async Task<PatchResult> PatchOneAsync(
            string collectionName,
            string documentId,
            IEnumerable<PatchOperation> operations,
            PatchOptions? options = null)
        {
            ThrowIfDisposed();
            Interlocked.Increment(ref _totalOperations);

            try
            {
                options?.Validate();

                // Get or create the lock for this document
                var lockKey = $"{collectionName}:{documentId}";
                var docLock = _documentLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

                await docLock.WaitAsync();
                try
                {
                    // Get the existing document
                    var existingDoc = await _innerStore.GetAsync(collectionName, documentId);

                    // Check if document exists
                    if (existingDoc == null)
                    {
                        if (options?.Upsert == true)
                        {
                            // Create new document for upsert
                            existingDoc = new Document
                            {
                                Id = documentId,
                                Data = new Dictionary<string, object?>()
                            };
                            Interlocked.Increment(ref _upsertsPerformed);
                        }
                        else
                        {
                            Interlocked.Increment(ref _successfulOperations);
                            return PatchResult.NotFoundResult();
                        }
                    }

                    // Store the document before changes if requested
                    Document? documentBefore = null;
                    if (options?.ReturnDocumentBefore == true)
                    {
                        documentBefore = existingDoc.Clone();
                    }

                    // Check filter conditions
                    if (options?.Filters?.Count > 0)
                    {
                        foreach (var filter in options.Filters)
                        {
                            if (!EvaluateFilter(existingDoc, filter))
                            {
                                Interlocked.Increment(ref _successfulOperations);
                                return PatchResult.SuccessResult(true, false, documentBefore, existingDoc);
                            }
                        }
                    }

                    // Apply all patch operations
                    var wasNewDocument = existingDoc.Version == 0 && existingDoc.CreatedAt == default;
                    var modified = ApplyPatchOperations(existingDoc, operations);

                    // Save the document (insert if new, update if existing)
                    if (wasNewDocument)
                    {
                        existingDoc.CreatedAt = DateTime.UtcNow;
                        await _innerStore.InsertAsync(collectionName, existingDoc);
                    }
                    else
                    {
                        await _innerStore.UpdateAsync(collectionName, existingDoc);
                    }

                    if (modified)
                    {
                        Interlocked.Increment(ref _documentsModified);
                    }

                    Interlocked.Increment(ref _successfulOperations);

                    // Return result with appropriate document
                    // When options is null, default to returning the document after modification
                    bool returnBefore = options?.ReturnDocumentBefore ?? false;
                    bool returnAfter = options?.ReturnDocumentAfter ?? true; // Default is true

                    if (returnBefore)
                    {
                        return PatchResult.SuccessResult(true, modified, documentBefore, null);
                    }
                    else if (returnAfter)
                    {
                        return PatchResult.SuccessResult(true, modified, null, existingDoc.Clone());
                    }
                    else
                    {
                        return PatchResult.SuccessResult(true, modified);
                    }
                }
                finally
                {
                    docLock.Release();
                }
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _failedOperations);
                return PatchResult.FailureResult(ex.Message);
            }
        }

        /// <inheritdoc />
        public async Task<long> PatchManyAsync(
            string collectionName,
            Func<Document, bool> filter,
            IEnumerable<PatchOperation> operations)
        {
            ThrowIfDisposed();
            Interlocked.Increment(ref _totalOperations);

            try
            {
                var allDocs = await _innerStore.GetAllAsync(collectionName);
                var matchingDocs = allDocs.Where(filter).ToList();
                var modifiedCount = 0L;

                foreach (var doc in matchingDocs)
                {
                    var result = await PatchOneAsync(collectionName, doc.Id, operations);
                    if (result.Success && result.Modified)
                    {
                        modifiedCount++;
                    }
                }

                Interlocked.Add(ref _documentsModified, modifiedCount);
                Interlocked.Increment(ref _successfulOperations);
                return modifiedCount;
            }
            catch
            {
                Interlocked.Increment(ref _failedOperations);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task<PatchResult> FindAndModifyAsync(
            string collectionName,
            Func<Document, bool> filter,
            IEnumerable<PatchOperation> operations,
            PatchOptions? options = null)
        {
            ThrowIfDisposed();
            Interlocked.Increment(ref _totalOperations);

            try
            {
                var allDocs = await _innerStore.GetAllAsync(collectionName);
                var matchingDoc = allDocs.FirstOrDefault(filter);

                if (matchingDoc == null)
                {
                    if (options?.Upsert == true)
                    {
                        // Create a new document
                        var newId = Guid.NewGuid().ToString();
                        return await PatchOneAsync(collectionName, newId, operations, options);
                    }

                    Interlocked.Increment(ref _successfulOperations);
                    return PatchResult.NotFoundResult();
                }

                return await PatchOneAsync(collectionName, matchingDoc.Id, operations, options);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _failedOperations);
                return PatchResult.FailureResult(ex.Message);
            }
        }

        /// <inheritdoc />
        public Task<PatchStatistics> GetStatisticsAsync()
        {
            ThrowIfDisposed();
            return Task.FromResult(new PatchStatistics
            {
                TotalOperations = Interlocked.Read(ref _totalOperations),
                SuccessfulOperations = Interlocked.Read(ref _successfulOperations),
                FailedOperations = Interlocked.Read(ref _failedOperations),
                DocumentsModified = Interlocked.Read(ref _documentsModified),
                UpsertsPerformed = Interlocked.Read(ref _upsertsPerformed)
            });
        }

        /// <inheritdoc />
        public Task ResetStatisticsAsync()
        {
            ThrowIfDisposed();
            Interlocked.Exchange(ref _totalOperations, 0);
            Interlocked.Exchange(ref _successfulOperations, 0);
            Interlocked.Exchange(ref _failedOperations, 0);
            Interlocked.Exchange(ref _documentsModified, 0);
            Interlocked.Exchange(ref _upsertsPerformed, 0);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Evaluates a filter condition against a document.
        /// </summary>
        private bool EvaluateFilter(Document document, PatchFilter filter)
        {
            var fieldValue = GetFieldValue(document.Data, filter.FieldPath);

            if (filter.FieldMustExist && fieldValue == null)
            {
                return false;
            }

            if (!filter.FieldMustExist && fieldValue != null)
            {
                return false;
            }

            if (filter.ExpectedValue != null && !ValuesEqual(fieldValue, filter.ExpectedValue))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Applies patch operations to a document.
        /// </summary>
        private bool ApplyPatchOperations(Document document, IEnumerable<PatchOperation> operations)
        {
            var modified = false;

            foreach (var operation in operations)
            {
                if (ApplySingleOperation(document, operation))
                {
                    modified = true;
                }
            }

            if (modified)
            {
                document.UpdatedAt = DateTime.UtcNow;
                document.Version++;
            }

            return modified;
        }

        /// <summary>
        /// Applies a single patch operation to a document.
        /// </summary>
        private bool ApplySingleOperation(Document document, PatchOperation operation)
        {
            var pathParts = operation.FieldPath.Split('.');
            var target = document.Data;

            // Navigate to the parent container
            for (int i = 0; i < pathParts.Length - 1; i++)
            {
                if (!target.TryGetValue(pathParts[i], out var next) || next is not Dictionary<string, object?> nextDict)
                {
                    // Create nested path if it doesn't exist
                    nextDict = new Dictionary<string, object?>();
                    target[pathParts[i]] = nextDict;
                }
                target = nextDict;
            }

            var fieldName = pathParts[^1];

            switch (operation.Type)
            {
                case PatchOperationType.Set:
                    target[fieldName] = ConvertValue(operation.Value);
                    return true;

                case PatchOperationType.Unset:
                    return target.Remove(fieldName);

                case PatchOperationType.Increment:
                    return ApplyNumericOperation(target, fieldName, operation.Value, (a, b) => a + b);

                case PatchOperationType.Multiply:
                    return ApplyNumericOperation(target, fieldName, operation.Value, (a, b) => a * b);

                case PatchOperationType.Push:
                    return ApplyArrayOperation(target, fieldName, operation.Value, ArrayOperation.Push);

                case PatchOperationType.Pull:
                    return ApplyArrayOperation(target, fieldName, operation.Value, ArrayOperation.Pull);

                case PatchOperationType.AddToSet:
                    return ApplyArrayOperation(target, fieldName, operation.Value, ArrayOperation.AddToSet);

                case PatchOperationType.Pop:
                    return ApplyPopOperation(target, fieldName, operation.Value);

                case PatchOperationType.Rename:
                    return ApplyRenameOperation(target, fieldName, operation.Value?.ToString());

                case PatchOperationType.Min:
                    return ApplyMinMaxOperation(target, fieldName, operation.Value, true);

                case PatchOperationType.Max:
                    return ApplyMinMaxOperation(target, fieldName, operation.Value, false);

                case PatchOperationType.CurrentDate:
                    target[fieldName] = DateTime.UtcNow;
                    return true;

                case PatchOperationType.BitAnd:
                    return ApplyBitwiseOperation(target, fieldName, operation.Value, (a, b) => a & b);

                case PatchOperationType.BitOr:
                    return ApplyBitwiseOperation(target, fieldName, operation.Value, (a, b) => a | b);

                case PatchOperationType.BitXor:
                    return ApplyBitwiseOperation(target, fieldName, operation.Value, (a, b) => a ^ b);

                default:
                    return false;
            }
        }

        /// <summary>
        /// Gets a field value from a dictionary using dot notation.
        /// </summary>
        private object? GetFieldValue(Dictionary<string, object?> data, string fieldPath)
        {
            var parts = fieldPath.Split('.');
            var current = data;

            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (!current.TryGetValue(parts[i], out var next) || next is not Dictionary<string, object?> nextDict)
                {
                    return null;
                }
                current = nextDict;
            }

            current.TryGetValue(parts[^1], out var value);
            return value;
        }

        /// <summary>
        /// Applies a numeric operation (increment, multiply) to a field.
        /// </summary>
        private bool ApplyNumericOperation(Dictionary<string, object?> target, string fieldName, object? value, Func<double, double, double> operation)
        {
            if (!target.TryGetValue(fieldName, out var current) || !TryConvertToDouble(current, out var currentVal))
            {
                currentVal = 0;
            }

            if (!TryConvertToDouble(value, out var operand))
            {
                operand = 0;
            }

            var result = operation(currentVal, operand);
            target[fieldName] = result;
            return true;
        }

        /// <summary>
        /// Applies an array operation (push, pull, addToSet).
        /// </summary>
        private bool ApplyArrayOperation(Dictionary<string, object?> target, string fieldName, object? value, ArrayOperation operation)
        {
            if (!target.TryGetValue(fieldName, out var current) || current is not List<object?> list)
            {
                list = new List<object?>();
                target[fieldName] = list;
            }

            switch (operation)
            {
                case ArrayOperation.Push:
                    list.Add(ConvertValue(value));
                    return true;

                case ArrayOperation.Pull:
                    return list.RemoveAll(item => ValuesEqual(item, value)) > 0;

                case ArrayOperation.AddToSet:
                    if (!list.Any(item => ValuesEqual(item, value)))
                    {
                        list.Add(ConvertValue(value));
                        return true;
                    }
                    return false;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Applies a pop operation to an array field.
        /// </summary>
        private bool ApplyPopOperation(Dictionary<string, object?> target, string fieldName, object? value)
        {
            if (!target.TryGetValue(fieldName, out var current) || current is not List<object?> list || list.Count == 0)
            {
                return false;
            }

            var popFirst = value is int i && i < 0;

            if (popFirst)
            {
                list.RemoveAt(0);
            }
            else
            {
                list.RemoveAt(list.Count - 1);
            }

            return true;
        }

        /// <summary>
        /// Applies a rename operation to a field.
        /// </summary>
        private bool ApplyRenameOperation(Dictionary<string, object?> target, string oldName, string? newName)
        {
            if (string.IsNullOrEmpty(newName) || !target.ContainsKey(oldName))
            {
                return false;
            }

            target[newName] = target[oldName];
            target.Remove(oldName);
            return true;
        }

        /// <summary>
        /// Applies a min/max operation to a field.
        /// </summary>
        private bool ApplyMinMaxOperation(Dictionary<string, object?> target, string fieldName, object? value, bool isMin)
        {
            if (!target.TryGetValue(fieldName, out var current) || !TryConvertToDouble(current, out var currentVal))
            {
                // If field doesn't exist or isn't numeric, set it as double for consistency
                if (TryConvertToDouble(value, out var val))
                {
                    target[fieldName] = val;
                }
                else
                {
                    target[fieldName] = value;
                }
                return true;
            }

            if (!TryConvertToDouble(value, out var compareVal))
            {
                return false;
            }

            if (isMin && compareVal < currentVal)
            {
                // Store as double for consistency with other numeric operations
                target[fieldName] = compareVal;
                return true;
            }

            if (!isMin && compareVal > currentVal)
            {
                // Store as double for consistency with other numeric operations
                target[fieldName] = compareVal;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Applies a bitwise operation to a field.
        /// </summary>
        private bool ApplyBitwiseOperation(Dictionary<string, object?> target, string fieldName, object? value, Func<long, long, long> operation)
        {
            if (!target.TryGetValue(fieldName, out var current) || !TryConvertToLong(current, out var currentVal))
            {
                currentVal = 0;
            }

            if (!TryConvertToLong(value, out var operand))
            {
                operand = 0;
            }

            target[fieldName] = operation(currentVal, operand);
            return true;
        }

        /// <summary>
        /// Converts a value for storage.
        /// </summary>
        private object? ConvertValue(object? value)
        {
            // Handle JsonElement conversion
            if (value is System.Text.Json.JsonElement jsonElement)
            {
                return ConvertJsonElement(jsonElement);
            }
            return value;
        }

        /// <summary>
        /// Converts a JsonElement to a CLR object.
        /// </summary>
        private object? ConvertJsonElement(System.Text.Json.JsonElement element)
        {
            return element.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => element.GetString(),
                System.Text.Json.JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
                System.Text.Json.JsonValueKind.True => true,
                System.Text.Json.JsonValueKind.False => false,
                System.Text.Json.JsonValueKind.Null => null,
                System.Text.Json.JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
                System.Text.Json.JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => ConvertJsonElement(p.Value)),
                _ => element.ToString()
            };
        }

        /// <summary>
        /// Tries to convert a value to double.
        /// </summary>
        private bool TryConvertToDouble(object? value, out double result)
        {
            result = 0;
            if (value == null) return false;

            return value switch
            {
                double d => (result = d) == result,
                float f => (result = f) == result,
                int i => (result = i) == result,
                long l => (result = l) == result,
                decimal dec => (result = (double)dec) == result,
                string s => double.TryParse(s, out result),
                _ => false
            };
        }

        /// <summary>
        /// Tries to convert a value to long.
        /// </summary>
        private bool TryConvertToLong(object? value, out long result)
        {
            result = 0;
            if (value == null) return false;

            return value switch
            {
                long l => (result = l) == result,
                int i => (result = i) == result,
                double d => (result = (long)d) == result,
                string s => long.TryParse(s, out result),
                _ => false
            };
        }

        /// <summary>
        /// Compares two values for equality.
        /// </summary>
        private bool ValuesEqual(object? a, object? b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;

            // Handle numeric comparison
            if (TryConvertToDouble(a, out var aNum) && TryConvertToDouble(b, out var bNum))
            {
                return Math.Abs(aNum - bNum) < 0.0001;
            }

            return a.Equals(b);
        }

        /// <summary>
        /// Throws if the object has been disposed.
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PatchDocumentStore));
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            // Clean up semaphores
            foreach (var kvp in _documentLocks)
            {
                kvp.Value.Dispose();
            }
            _documentLocks.Clear();
        }

        private enum ArrayOperation
        {
            Push,
            Pull,
            AddToSet
        }
    }

    /// <summary>
    /// Extension methods for PatchDocumentStore.
    /// </summary>
    public static class PatchDocumentStoreExtensions
    {
        /// <summary>
        /// Wraps a document store with patch operation capabilities.
        /// </summary>
        public static PatchDocumentStore WithPatchSupport(this IDocumentStore store)
        {
            return new PatchDocumentStore(store);
        }
    }
}
