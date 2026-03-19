// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Storage.ImportExport;

/// <summary>
/// Supported export formats for data export operations
/// </summary>
public enum ExportFormat
{
    /// <summary>JSON Lines format (one JSON object per line)</summary>
    JsonLines,
    /// <summary>Standard JSON array format</summary>
    JsonArray,
    /// <summary>Comma-separated values</summary>
    Csv,
    /// <summary>BSON binary format</summary>
    Bson
}

/// <summary>
/// Options for configuring export operations
/// </summary>
public class ExportOptions
{
    /// <summary>
    /// The format to export data in
    /// </summary>
    public ExportFormat Format { get; set; } = ExportFormat.JsonLines;

    /// <summary>
    /// Whether to include document metadata (CreatedAt, UpdatedAt, Version)
    /// </summary>
    public bool IncludeMetadata { get; set; } = true;

    /// <summary>
    /// Whether to pretty-print JSON output (applies to JSON formats only)
    /// </summary>
    public bool PrettyPrint { get; set; } = false;

    /// <summary>
    /// Maximum number of documents to export (null for unlimited)
    /// </summary>
    public int? MaxDocuments { get; set; }

    /// <summary>
    /// Progress callback for reporting export progress (0.0 to 1.0)
    /// </summary>
    public IProgress<double>? Progress { get; set; }

    /// <summary>
    /// Cancellation token for the export operation
    /// </summary>
    public CancellationToken CancellationToken { get; set; }
}

/// <summary>
/// Result of an export operation
/// </summary>
public class ExportResult
{
    /// <summary>
    /// Number of documents exported
    /// </summary>
    public int ExportedCount { get; set; }

    /// <summary>
    /// Path to the exported file
    /// </summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>
    /// Format used for export
    /// </summary>
    public ExportFormat Format { get; set; }

    /// <summary>
    /// Size of the exported file in bytes
    /// </summary>
    public long FileSizeBytes { get; set; }

    /// <summary>
    /// Time taken for the export operation
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Any warnings that occurred during export
    /// </summary>
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Interface for exporting data from the document store
/// </summary>
public interface IDataExporter
{
    /// <summary>
    /// Exports all documents from a collection to a file
    /// </summary>
    /// <param name="store">The document store to export from</param>
    /// <param name="collectionName">The collection to export</param>
    /// <param name="outputPath">The output file path</param>
    /// <param name="options">Export options</param>
    /// <returns>Export result with statistics</returns>
    Task<ExportResult> ExportCollectionAsync(
        IDocumentStore store,
        string collectionName,
        string outputPath,
        ExportOptions? options = null);

    /// <summary>
    /// Exports multiple collections to a directory
    /// </summary>
    /// <param name="store">The document store to export from</param>
    /// <param name="collectionNames">The collections to export</param>
    /// <param name="outputDirectory">The output directory path</param>
    /// <param name="options">Export options</param>
    /// <returns>Dictionary of collection names to export results</returns>
    Task<Dictionary<string, ExportResult>> ExportCollectionsAsync(
        IDocumentStore store,
        IEnumerable<string> collectionNames,
        string outputDirectory,
        ExportOptions? options = null);

    /// <summary>
    /// Exports all collections to a directory
    /// </summary>
    /// <param name="store">The document store to export from</param>
    /// <param name="outputDirectory">The output directory path</param>
    /// <param name="options">Export options</param>
    /// <returns>Dictionary of collection names to export results</returns>
    Task<Dictionary<string, ExportResult>> ExportAllCollectionsAsync(
        IDocumentStore store,
        string outputDirectory,
        ExportOptions? options = null);
}
