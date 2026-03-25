// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Generic;
using System.Text.Json;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Core.Patches
{
    /// <summary>
    /// Represents the type of patch operation to perform.
    /// </summary>
    public enum PatchOperationType
    {
        /// <summary>Set a field to a specific value.</summary>
        Set,
        
        /// <summary>Unset (remove) a field.</summary>
        Unset,
        
        /// <summary>Increment a numeric field by a value.</summary>
        Increment,
        
        /// <summary>Multiply a numeric field by a value.</summary>
        Multiply,
        
        /// <summary>Push a value to an array field.</summary>
        Push,
        
        /// <summary>Pull a value from an array field.</summary>
        Pull,
        
        /// <summary>Add a value to an array only if it doesn't exist (unique).</summary>
        AddToSet,
        
        /// <summary>Pop the first or last element from an array.</summary>
        Pop,
        
        /// <summary>Rename a field.</summary>
        Rename,
        
        /// <summary>Update a field only if the value is less than the current value.</summary>
        Min,
        
        /// <summary>Update a field only if the value is greater than the current value.</summary>
        Max,
        
        /// <summary>Current timestamp for a field.</summary>
        CurrentDate,
        
        /// <summary>Set a field to the bitwise AND of the current value and given value.</summary>
        BitAnd,
        
        /// <summary>Set a field to the bitwise OR of the current value and given value.</summary>
        BitOr,
        
        /// <summary>Set a field to the bitwise XOR of the current value and given value.</summary>
        BitXor
    }

    /// <summary>
    /// Represents a single patch operation on a document field.
    /// </summary>
    public class PatchOperation
    {
        /// <summary>
        /// Gets the type of patch operation.
        /// </summary>
        public PatchOperationType Type { get; }

        /// <summary>
        /// Gets the field path to apply the operation to (supports dot notation for nested fields).
        /// </summary>
        public string FieldPath { get; }

        /// <summary>
        /// Gets the value to use for the operation (null for operations that don't require a value).
        /// </summary>
        public object? Value { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="PatchOperation"/> class.
        /// </summary>
        /// <param name="type">The type of patch operation.</param>
        /// <param name="fieldPath">The field path to apply the operation to.</param>
        /// <param name="value">The value to use for the operation (optional).</param>
        public PatchOperation(PatchOperationType type, string fieldPath, object? value = null)
        {
            Type = type;
            FieldPath = fieldPath ?? throw new ArgumentNullException(nameof(fieldPath));
            Value = value;
        }

        /// <summary>
        /// Creates a Set operation.
        /// </summary>
        public static PatchOperation Set(string fieldPath, object value)
            => new PatchOperation(PatchOperationType.Set, fieldPath, value);

        /// <summary>
        /// Creates an Unset operation.
        /// </summary>
        public static PatchOperation Unset(string fieldPath)
            => new PatchOperation(PatchOperationType.Unset, fieldPath);

        /// <summary>
        /// Creates an Increment operation.
        /// </summary>
        public static PatchOperation Increment(string fieldPath, double value = 1)
            => new PatchOperation(PatchOperationType.Increment, fieldPath, value);

        /// <summary>
        /// Creates a Multiply operation.
        /// </summary>
        public static PatchOperation Multiply(string fieldPath, double value)
            => new PatchOperation(PatchOperationType.Multiply, fieldPath, value);

        /// <summary>
        /// Creates a Push operation.
        /// </summary>
        public static PatchOperation Push(string fieldPath, object value)
            => new PatchOperation(PatchOperationType.Push, fieldPath, value);

        /// <summary>
        /// Creates a Pull operation.
        /// </summary>
        public static PatchOperation Pull(string fieldPath, object value)
            => new PatchOperation(PatchOperationType.Pull, fieldPath, value);

        /// <summary>
        /// Creates an AddToSet operation.
        /// </summary>
        public static PatchOperation AddToSet(string fieldPath, object value)
            => new PatchOperation(PatchOperationType.AddToSet, fieldPath, value);

        /// <summary>
        /// Creates a Pop operation.
        /// </summary>
        /// <param name="fieldPath">The array field path.</param>
        /// <param name="first">If true, pops from the beginning; otherwise pops from the end.</param>
        public static PatchOperation Pop(string fieldPath, bool first = false)
            => new PatchOperation(PatchOperationType.Pop, fieldPath, first ? -1 : 1);

        /// <summary>
        /// Creates a Rename operation.
        /// </summary>
        public static PatchOperation Rename(string fieldPath, string newName)
            => new PatchOperation(PatchOperationType.Rename, fieldPath, newName);

        /// <summary>
        /// Creates a Min operation.
        /// </summary>
        public static PatchOperation Min(string fieldPath, object value)
            => new PatchOperation(PatchOperationType.Min, fieldPath, value);

        /// <summary>
        /// Creates a Max operation.
        /// </summary>
        public static PatchOperation Max(string fieldPath, object value)
            => new PatchOperation(PatchOperationType.Max, fieldPath, value);

        /// <summary>
        /// Creates a CurrentDate operation.
        /// </summary>
        public static PatchOperation CurrentDate(string fieldPath)
            => new PatchOperation(PatchOperationType.CurrentDate, fieldPath);

        /// <summary>
        /// Creates a BitAnd operation.
        /// </summary>
        public static PatchOperation BitAnd(string fieldPath, long value)
            => new PatchOperation(PatchOperationType.BitAnd, fieldPath, value);

        /// <summary>
        /// Creates a BitOr operation.
        /// </summary>
        public static PatchOperation BitOr(string fieldPath, long value)
            => new PatchOperation(PatchOperationType.BitOr, fieldPath, value);

        /// <summary>
        /// Creates a BitXor operation.
        /// </summary>
        public static PatchOperation BitXor(string fieldPath, long value)
            => new PatchOperation(PatchOperationType.BitXor, fieldPath, value);
    }

    /// <summary>
    /// Represents a filter condition for server-side patch operations.
    /// </summary>
    public class PatchFilter
    {
        /// <summary>
        /// Gets the field path to check (supports dot notation).
        /// </summary>
        public string FieldPath { get; }

        /// <summary>
        /// Gets the expected value for equality comparison.
        /// </summary>
        public object? ExpectedValue { get; }

        /// <summary>
        /// Gets a value indicating whether the field must exist.
        /// </summary>
        public bool FieldMustExist { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="PatchFilter"/> class.
        /// </summary>
        public PatchFilter(string fieldPath, object? expectedValue = null, bool fieldMustExist = true)
        {
            FieldPath = fieldPath ?? throw new ArgumentNullException(nameof(fieldPath));
            ExpectedValue = expectedValue;
            FieldMustExist = fieldMustExist;
        }

        /// <summary>
        /// Creates a filter that checks if a field equals a value.
        /// </summary>
        public static PatchFilter Eq(string fieldPath, object value)
            => new PatchFilter(fieldPath, value);

        /// <summary>
        /// Creates a filter that checks if a field exists.
        /// </summary>
        public static PatchFilter Exists(string fieldPath)
            => new PatchFilter(fieldPath, null, true);

        /// <summary>
        /// Creates a filter that checks if a field does not exist.
        /// </summary>
        public static PatchFilter NotExists(string fieldPath)
            => new PatchFilter(fieldPath, null, false);
    }

    /// <summary>
    /// Options for server-side patch operations.
    /// </summary>
    public class PatchOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether to return the document before the patch was applied.
        /// </summary>
        public bool ReturnDocumentBefore { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to return the document after the patch was applied.
        /// </summary>
        public bool ReturnDocumentAfter { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether to create the document if it doesn't exist (upsert).
        /// </summary>
        public bool Upsert { get; set; }

        /// <summary>
        /// Gets or sets the filter conditions that must be satisfied for the patch to be applied.
        /// </summary>
        public List<PatchFilter> Filters { get; set; } = new List<PatchFilter>();

        /// <summary>
        /// Validates the options.
        /// </summary>
        public void Validate()
        {
            if (ReturnDocumentBefore && ReturnDocumentAfter)
            {
                throw new ArgumentException("Cannot return both before and after documents. Choose one.");
            }
        }
    }

    /// <summary>
    /// Result of a server-side patch operation.
    /// </summary>
    public class PatchResult
    {
        /// <summary>
        /// Gets a value indicating whether the patch was applied successfully.
        /// </summary>
        public bool Success { get; }

        /// <summary>
        /// Gets a value indicating whether a document was matched by the operation.
        /// </summary>
        public bool Matched { get; }

        /// <summary>
        /// Gets a value indicating whether a document was modified by the operation.
        /// </summary>
        public bool Modified { get; }

        /// <summary>
        /// Gets the document before the patch was applied (if ReturnDocumentBefore was set).
        /// </summary>
        public Document? DocumentBefore { get; }

        /// <summary>
        /// Gets the document after the patch was applied (if ReturnDocumentAfter was set).
        /// </summary>
        public Document? DocumentAfter { get; }

        /// <summary>
        /// Gets the error message if the operation failed.
        /// </summary>
        public string? ErrorMessage { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="PatchResult"/> class.
        /// </summary>
        public PatchResult(
            bool success,
            bool matched,
            bool modified,
            Document? documentBefore = null,
            Document? documentAfter = null,
            string? errorMessage = null)
        {
            Success = success;
            Matched = matched;
            Modified = modified;
            DocumentBefore = documentBefore;
            DocumentAfter = documentAfter;
            ErrorMessage = errorMessage;
        }

        /// <summary>
        /// Creates a successful result.
        /// </summary>
        public static PatchResult SuccessResult(
            bool matched,
            bool modified,
            Document? documentBefore = null,
            Document? documentAfter = null)
            => new PatchResult(true, matched, modified, documentBefore, documentAfter);

        /// <summary>
        /// Creates a failure result.
        /// </summary>
        public static PatchResult FailureResult(string errorMessage)
            => new PatchResult(false, false, false, null, null, errorMessage);

        /// <summary>
        /// Creates a result for when no document was found.
        /// </summary>
        public static PatchResult NotFoundResult()
            => new PatchResult(true, false, false);
    }

    /// <summary>
    /// Statistics for patch operations.
    /// </summary>
    public class PatchStatistics
    {
        /// <summary>
        /// Gets or sets the total number of patch operations executed.
        /// </summary>
        public long TotalOperations { get; set; }

        /// <summary>
        /// Gets or sets the number of successful operations.
        /// </summary>
        public long SuccessfulOperations { get; set; }

        /// <summary>
        /// Gets or sets the number of failed operations.
        /// </summary>
        public long FailedOperations { get; set; }

        /// <summary>
        /// Gets or sets the number of documents modified.
        /// </summary>
        public long DocumentsModified { get; set; }

        /// <summary>
        /// Gets or sets the number of upserts performed.
        /// </summary>
        public long UpsertsPerformed { get; set; }
    }

    /// <summary>
    /// Interface for server-side patch operations on documents.
    /// </summary>
    public interface IServerSidePatch
    {
        /// <summary>
        /// Applies a patch to a single document matching the document ID.
        /// </summary>
        /// <param name="collectionName">The collection name.</param>
        /// <param name="documentId">The document ID.</param>
        /// <param name="operations">The patch operations to apply.</param>
        /// <param name="options">Optional patch options.</param>
        /// <returns>The result of the patch operation.</returns>
        Task<PatchResult> PatchOneAsync(
            string collectionName,
            string documentId,
            IEnumerable<PatchOperation> operations,
            PatchOptions? options = null);

        /// <summary>
        /// Applies a patch to multiple documents matching a filter.
        /// </summary>
        /// <param name="collectionName">The collection name.</param>
        /// <param name="filter">The filter to match documents.</param>
        /// <param name="operations">The patch operations to apply.</param>
        /// <returns>The number of documents modified.</returns>
        Task<long> PatchManyAsync(
            string collectionName,
            Func<Document, bool> filter,
            IEnumerable<PatchOperation> operations);

        /// <summary>
        /// Finds and modifies a document atomically.
        /// </summary>
        /// <param name="collectionName">The collection name.</param>
        /// <param name="filter">The filter to find the document.</param>
        /// <param name="operations">The patch operations to apply.</param>
        /// <param name="options">Optional patch options.</param>
        /// <returns>The result of the operation.</returns>
        Task<PatchResult> FindAndModifyAsync(
            string collectionName,
            Func<Document, bool> filter,
            IEnumerable<PatchOperation> operations,
            PatchOptions? options = null);

        /// <summary>
        /// Gets the patch operation statistics.
        /// </summary>
        Task<PatchStatistics> GetStatisticsAsync();

        /// <summary>
        /// Resets the statistics.
        /// </summary>
        Task ResetStatisticsAsync();
    }
}
