// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Storage.ImportExport;

/// <summary>
/// Import modes for handling existing documents
/// </summary>
public enum ImportMode
{
    /// <summary>Insert only, fail if document exists</summary>
    Insert,
    /// <summary>Upsert - insert if new, update if exists</summary>
    Upsert,
    /// <summary>Skip existing documents, only insert new ones</summary>
    SkipExisting,
    /// <summary>Delete all existing documents before import</summary>
    ReplaceAll
}

/// <summary>
/// Options for configuring import operations
/// </summary>
public class ImportOptions
{
    /// <summary>
    /// The format of the input data
    /// </summary>
    public ExportFormat Format { get; set; } = ExportFormat.JsonLines;

    /// <summary>
    /// How to handle existing documents with the same ID
    /// </summary>
    public ImportMode Mode { get; set; } = ImportMode.Upsert;

    /// <summary>
    /// Whether to validate documents before import
    /// </summary>
    public bool ValidateDocuments { get; set; } = true;

    /// <summary>
    /// Whether to preserve original document metadata (CreatedAt, UpdatedAt, Version)
    /// </summary>
    public bool PreserveMetadata { get; set; } = false;

    /// <summary>
    /// Batch size for bulk insert operations
    /// </summary>
    public int BatchSize { get; set; } = 1000;

    /// <summary>
    /// Maximum number of errors to tolerate before failing (null for unlimited)
    /// </summary>
    public int? MaxErrors { get; set; } = 100;

    /// <summary>
    /// Progress callback for reporting import progress (0.0 to 1.0)
    /// </summary>
    public IProgress<double>? Progress { get; set; }

    /// <summary>
    /// Cancellation token for the import operation
    /// </summary>
    public CancellationToken CancellationToken { get; set; }
}

/// <summary>
/// Result of an import operation
/// </summary>
public class ImportResult
{
    /// <summary>
    /// Number of documents successfully imported
    /// </summary>
    public int ImportedCount { get; set; }

    /// <summary>
    /// Number of documents that were updated (upsert mode)
    /// </summary>
    public int UpdatedCount { get; set; }

    /// <summary>
    /// Number of documents skipped (skip existing mode)
    /// </summary>
    public int SkippedCount { get; set; }

    /// <summary>
    /// Number of documents that failed to import
    /// </summary>
    public int ErrorCount { get; set; }

    /// <summary>
    /// Total number of documents processed
    /// </summary>
    public int TotalProcessed => ImportedCount + UpdatedCount + SkippedCount + ErrorCount;

    /// <summary>
    /// Path to the imported file
    /// </summary>
    public string InputPath { get; set; } = string.Empty;

    /// <summary>
    /// Time taken for the import operation
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// List of errors that occurred during import
    /// </summary>
    public List<ImportError> Errors { get; set; } = new();

    /// <summary>
    /// Whether the import completed successfully
    /// </summary>
    public bool Success => ErrorCount == 0 || (MaxErrorsExceeded == false);

    /// <summary>
    /// Whether the maximum error threshold was exceeded
    /// </summary>
    public bool MaxErrorsExceeded { get; set; }
}

/// <summary>
/// Represents an error that occurred during import
/// </summary>
public class ImportError
{
    /// <summary>
    /// Line number in the input file where the error occurred (if applicable)
    /// </summary>
    public int LineNumber { get; set; }

    /// <summary>
    /// Document ID that caused the error (if available)
    /// </summary>
    public string? DocumentId { get; set; }

    /// <summary>
    /// Error message
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// The raw data that caused the error (if available)
    /// </summary>
    public string? RawData { get; set; }
}

/// <summary>
/// Interface for importing data into the document store
/// </summary>
public interface IDataImporter
{
    /// <summary>
    /// Imports documents from a file into a collection
    /// </summary>
    /// <param name="store">The document store to import into</param>
    /// <param name="inputPath">The input file path</param>
    /// <param name="collectionName">The target collection name</param>
    /// <param name="options">Import options</param>
    /// <returns>Import result with statistics</returns>
    Task<ImportResult> ImportAsync(
        IDocumentStore store,
        string inputPath,
        string collectionName,
        ImportOptions? options = null);

    /// <summary>
    /// Imports documents from a stream into a collection
    /// </summary>
    /// <param name="store">The document store to import into</param>
    /// <param name="stream">The input stream</param>
    /// <param name="collectionName">The target collection name</param>
    /// <param name="options">Import options</param>
    /// <returns>Import result with statistics</returns>
    Task<ImportResult> ImportFromStreamAsync(
        IDocumentStore store,
        Stream stream,
        string collectionName,
        ImportOptions? options = null);

    /// <summary>
    /// Validates import data without actually importing
    /// </summary>
    /// <param name="inputPath">The input file path</param>
    /// <param name="options">Import options</param>
    /// <returns>List of validation errors (empty if valid)</returns>
    Task<List<ImportError>> ValidateAsync(
        string inputPath,
        ImportOptions? options = null);
}
