// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Generic;

namespace AdvGenNoSqlServer.Core.Models
{
    /// <summary>
    /// Types of batch operations supported
    /// </summary>
    public enum BatchOperationType
    {
        /// <summary>
        /// Insert multiple documents
        /// </summary>
        Insert = 0x01,

        /// <summary>
        /// Update multiple documents
        /// </summary>
        Update = 0x02,

        /// <summary>
        /// Delete multiple documents
        /// </summary>
        Delete = 0x03,

        /// <summary>
        /// Mixed operations (insert, update, delete in one batch)
        /// </summary>
        Mixed = 0x04
    }

    /// <summary>
    /// Represents a single operation within a batch
    /// </summary>
    public class BatchOperationItem
    {
        /// <summary>
        /// The type of operation
        /// </summary>
        public BatchOperationType OperationType { get; set; }

        /// <summary>
        /// The document ID (required for update/delete)
        /// </summary>
        public string? DocumentId { get; set; }

        /// <summary>
        /// The document data (required for insert/update)
        /// </summary>
        public Dictionary<string, object>? Document { get; set; }

        /// <summary>
        /// Filter for update/delete operations (alternative to DocumentId)
        /// </summary>
        public Dictionary<string, object>? Filter { get; set; }

        /// <summary>
        /// Update fields for partial updates
        /// </summary>
        public Dictionary<string, object>? UpdateFields { get; set; }
    }

    /// <summary>
    /// Request for batch operations
    /// </summary>
    public class BatchOperationRequest
    {
        /// <summary>
        /// The target collection
        /// </summary>
        public string Collection { get; set; } = string.Empty;

        /// <summary>
        /// The operations to perform
        /// </summary>
        public List<BatchOperationItem> Operations { get; set; } = new();

        /// <summary>
        /// Whether to stop on first error (true) or continue processing (false)
        /// </summary>
        public bool StopOnError { get; set; } = false;

        /// <summary>
        /// Whether to execute operations in a transaction
        /// </summary>
        public bool UseTransaction { get; set; } = true;

        /// <summary>
        /// Optional transaction ID if using an existing transaction
        /// </summary>
        public string? TransactionId { get; set; }
    }

    /// <summary>
    /// Result of a single batch operation item
    /// </summary>
    public class BatchOperationItemResult
    {
        /// <summary>
        /// The index of the operation in the batch
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// Whether the operation succeeded
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// The document ID affected (if applicable)
        /// </summary>
        public string? DocumentId { get; set; }

        /// <summary>
        /// Error message if operation failed
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Error code if operation failed
        /// </summary>
        public string? ErrorCode { get; set; }
    }

    /// <summary>
    /// Response for batch operations
    /// </summary>
    public class BatchOperationResponse
    {
        /// <summary>
        /// Whether the entire batch succeeded
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Number of documents inserted
        /// </summary>
        public int InsertedCount { get; set; }

        /// <summary>
        /// Number of documents updated
        /// </summary>
        public int UpdatedCount { get; set; }

        /// <summary>
        /// Number of documents deleted
        /// </summary>
        public int DeletedCount { get; set; }

        /// <summary>
        /// Total number of operations processed
        /// </summary>
        public int TotalProcessed { get; set; }

        /// <summary>
        /// Individual results for each operation
        /// </summary>
        public List<BatchOperationItemResult> Results { get; set; } = new();

        /// <summary>
        /// Error message if the entire batch failed
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Error code if the entire batch failed
        /// </summary>
        public string? ErrorCode { get; set; }

        /// <summary>
        /// Processing time in milliseconds
        /// </summary>
        public long ProcessingTimeMs { get; set; }

        /// <summary>
        /// Number of failed operations
        /// </summary>
        public int FailedCount => Results.FindAll(r => !r.Success).Count;
    }

    /// <summary>
    /// Options for batch operations
    /// </summary>
    public class BatchOptions
    {
        /// <summary>
        /// Maximum number of operations in a single batch (default: 1000)
        /// </summary>
        public int MaxBatchSize { get; set; } = 1000;

        /// <summary>
        /// Timeout for batch operations in milliseconds (default: 30 seconds)
        /// </summary>
        public int TimeoutMs { get; set; } = 30000;

        /// <summary>
        /// Whether to stop on first error
        /// </summary>
        public bool StopOnError { get; set; } = false;

        /// <summary>
        /// Whether to use transactions for batch operations
        /// </summary>
        public bool UseTransaction { get; set; } = true;
    }
}
