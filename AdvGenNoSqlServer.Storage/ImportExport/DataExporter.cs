// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Storage.ImportExport;

/// <summary>
/// Implementation of data export functionality for the document store
/// </summary>
public class DataExporter : IDataExporter
{
    private static readonly JsonSerializerOptions DefaultJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions PrettyJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <inheritdoc />
    public async Task<ExportResult> ExportCollectionAsync(
        IDocumentStore store,
        string collectionName,
        string outputPath,
        ExportOptions? options = null)
    {
        options ??= new ExportOptions();
        var startTime = DateTime.UtcNow;
        var warnings = new List<string>();

        // Ensure directory exists
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Get all documents from collection
        var documents = await store.GetAllAsync(collectionName);
        var documentList = documents.ToList();

        // Apply max documents limit
        if (options.MaxDocuments.HasValue && documentList.Count > options.MaxDocuments.Value)
        {
            documentList = documentList.Take(options.MaxDocuments.Value).ToList();
            warnings.Add($"Export limited to {options.MaxDocuments.Value} documents");
        }

        // Export based on format
        switch (options.Format)
        {
            case ExportFormat.JsonLines:
                await ExportJsonLinesAsync(documentList, outputPath, options, warnings);
                break;
            case ExportFormat.JsonArray:
                await ExportJsonArrayAsync(documentList, outputPath, options, warnings);
                break;
            case ExportFormat.Csv:
                await ExportCsvAsync(documentList, outputPath, options, warnings);
                break;
            case ExportFormat.Bson:
                throw new NotSupportedException("BSON format is not yet supported");
            default:
                throw new ArgumentException($"Unsupported export format: {options.Format}");
        }

        var fileInfo = new FileInfo(outputPath);

        return new ExportResult
        {
            ExportedCount = documentList.Count,
            OutputPath = outputPath,
            Format = options.Format,
            FileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0,
            Duration = DateTime.UtcNow - startTime,
            Warnings = warnings
        };
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, ExportResult>> ExportCollectionsAsync(
        IDocumentStore store,
        IEnumerable<string> collectionNames,
        string outputDirectory,
        ExportOptions? options = null)
    {
        options ??= new ExportOptions();
        var results = new Dictionary<string, ExportResult>();

        // Ensure directory exists
        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var extension = GetFileExtension(options.Format);
        var collections = collectionNames.ToList();
        var totalCollections = collections.Count;
        var processedCollections = 0;

        foreach (var collectionName in collections)
        {
            options.CancellationToken.ThrowIfCancellationRequested();

            var outputPath = Path.Combine(outputDirectory, $"{collectionName}{extension}");
            var result = await ExportCollectionAsync(store, collectionName, outputPath, options);
            results[collectionName] = result;

            processedCollections++;
            options.Progress?.Report((double)processedCollections / totalCollections);
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, ExportResult>> ExportAllCollectionsAsync(
        IDocumentStore store,
        string outputDirectory,
        ExportOptions? options = null)
    {
        var collections = await store.GetCollectionsAsync();
        return await ExportCollectionsAsync(store, collections, outputDirectory, options);
    }

    #region Private Export Methods

    private async Task ExportJsonLinesAsync(
        List<Document> documents,
        string outputPath,
        ExportOptions options,
        List<string> warnings)
    {
        var jsonOptions = options.PrettyPrint ? PrettyJsonOptions : DefaultJsonOptions;
        var totalDocuments = documents.Count;
        var processedDocuments = 0;

        await using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        await using var writer = new StreamWriter(stream, Encoding.UTF8);

        foreach (var document in documents)
        {
            options.CancellationToken.ThrowIfCancellationRequested();

            var exportObject = CreateExportObject(document, options.IncludeMetadata);
            var json = JsonSerializer.Serialize(exportObject, jsonOptions);
            await writer.WriteLineAsync(json);

            processedDocuments++;
            options.Progress?.Report((double)processedDocuments / totalDocuments);
        }

        await writer.FlushAsync();
    }

    private async Task ExportJsonArrayAsync(
        List<Document> documents,
        string outputPath,
        ExportOptions options,
        List<string> warnings)
    {
        var jsonOptions = options.PrettyPrint ? PrettyJsonOptions : DefaultJsonOptions;
        var totalDocuments = documents.Count;
        var processedDocuments = 0;

        await using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        await using var writer = new StreamWriter(stream, Encoding.UTF8);

        await writer.WriteLineAsync("[");

        for (int i = 0; i < documents.Count; i++)
        {
            options.CancellationToken.ThrowIfCancellationRequested();

            var document = documents[i];
            var exportObject = CreateExportObject(document, options.IncludeMetadata);
            var json = JsonSerializer.Serialize(exportObject, jsonOptions);

            // Add comma for all but the last item
            if (i < documents.Count - 1)
            {
                json += ",";
            }

            if (options.PrettyPrint)
            {
                // Indent each line
                var lines = json.Split('\n');
                foreach (var line in lines)
                {
                    await writer.WriteLineAsync("  " + line);
                }
            }
            else
            {
                await writer.WriteLineAsync(json);
            }

            processedDocuments++;
            options.Progress?.Report((double)processedDocuments / totalDocuments);
        }

        await writer.WriteLineAsync("]");
        await writer.FlushAsync();
    }

    private async Task ExportCsvAsync(
        List<Document> documents,
        string outputPath,
        ExportOptions options,
        List<string> warnings)
    {
        if (documents.Count == 0)
        {
            // Create empty file with headers
            await File.WriteAllTextAsync(outputPath, "_id\n", Encoding.UTF8, options.CancellationToken);
            return;
        }

        // Collect all unique field names from all documents
        var allFields = new HashSet<string> { "_id" };
        foreach (var doc in documents)
        {
            if (doc.Data != null)
            {
                foreach (var key in doc.Data.Keys)
                {
                    allFields.Add(key);
                }
            }
        }

        // Add metadata fields if requested
        if (options.IncludeMetadata)
        {
            allFields.Add("_createdAt");
            allFields.Add("_updatedAt");
            allFields.Add("_version");
        }

        var fields = allFields.ToList();
        var totalDocuments = documents.Count;
        var processedDocuments = 0;

        await using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        await using var writer = new StreamWriter(stream, Encoding.UTF8);

        // Write header
        await writer.WriteLineAsync(string.Join(",", fields.Select(EscapeCsvField)));

        // Write data rows
        foreach (var document in documents)
        {
            options.CancellationToken.ThrowIfCancellationRequested();

            var values = new List<string>();
            foreach (var field in fields)
            {
                var value = GetFieldValue(document, field, options.IncludeMetadata);
                values.Add(EscapeCsvField(ConvertToString(value)));
            }

            await writer.WriteLineAsync(string.Join(",", values));

            processedDocuments++;
            options.Progress?.Report((double)processedDocuments / totalDocuments);
        }

        await writer.FlushAsync();
    }

    #endregion

    #region Helper Methods

    private object CreateExportObject(Document document, bool includeMetadata)
    {
        var result = new Dictionary<string, object?>
        {
            ["_id"] = document.Id
        };

        // Add document data
        if (document.Data != null)
        {
            foreach (var kvp in document.Data)
            {
                result[kvp.Key] = kvp.Value;
            }
        }

        // Add metadata if requested
        if (includeMetadata)
        {
            result["_createdAt"] = document.CreatedAt;
            result["_updatedAt"] = document.UpdatedAt;
            result["_version"] = document.Version;
        }

        return result;
    }

    private object? GetFieldValue(Document document, string field, bool includeMetadata)
    {
        return field switch
        {
            "_id" => document.Id,
            "_createdAt" => includeMetadata ? (object?)document.CreatedAt : null,
            "_updatedAt" => includeMetadata ? (object?)document.UpdatedAt : null,
            "_version" => includeMetadata ? (object?)document.Version : null,
            _ => document.Data?.TryGetValue(field, out var value) == true ? value : null
        };
    }

    private string ConvertToString(object? value)
    {
        if (value == null) return "";
        if (value is DateTime dt) return dt.ToString("O", CultureInfo.InvariantCulture);
        if (value is DateTimeOffset dto) return dto.ToString("O", CultureInfo.InvariantCulture);
        return value.ToString() ?? "";
    }

    private string EscapeCsvField(string field)
    {
        // Check if escaping is needed
        if (field.Contains('"') || field.Contains(',') || field.Contains('\n') || field.Contains('\r'))
        {
            // Double up quotes and wrap in quotes
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        }
        return field;
    }

    private string GetFileExtension(ExportFormat format)
    {
        return format switch
        {
            ExportFormat.JsonLines => ".jsonl",
            ExportFormat.JsonArray => ".json",
            ExportFormat.Csv => ".csv",
            ExportFormat.Bson => ".bson",
            _ => ".dat"
        };
    }

    #endregion
}
