// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Globalization;
using System.Text;
using System.Text.Json;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Storage.ImportExport;

/// <summary>
/// Implementation of data import functionality for the document store
/// </summary>
public class DataImporter : IDataImporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <inheritdoc />
    public async Task<ImportResult> ImportAsync(
        IDocumentStore store,
        string inputPath,
        string collectionName,
        ImportOptions? options = null)
    {
        options ??= new ImportOptions();
        var startTime = DateTime.UtcNow;

        await using var stream = File.OpenRead(inputPath);
        var result = await ImportFromStreamAsync(store, stream, collectionName, options);
        result.InputPath = inputPath;
        result.Duration = DateTime.UtcNow - startTime;

        return result;
    }

    /// <inheritdoc />
    public async Task<ImportResult> ImportFromStreamAsync(
        IDocumentStore store,
        Stream stream,
        string collectionName,
        ImportOptions? options = null)
    {
        options ??= new ImportOptions();
        var startTime = DateTime.UtcNow;
        var errors = new List<ImportError>();
        int importedCount = 0;
        int updatedCount = 0;
        int skippedCount = 0;
        int errorCount = 0;
        bool maxErrorsExceeded = false;

        // Ensure collection exists
        await store.CreateCollectionAsync(collectionName);

        // Handle ReplaceAll mode - clear collection first
        if (options.Mode == ImportMode.ReplaceAll)
        {
            await store.ClearCollectionAsync(collectionName);
        }

        switch (options.Format)
        {
            case ExportFormat.JsonLines:
                (importedCount, updatedCount, skippedCount, errorCount, maxErrorsExceeded, errors) =
                    await ImportJsonLinesAsync(store, stream, collectionName, options);
                break;
            case ExportFormat.JsonArray:
                (importedCount, updatedCount, skippedCount, errorCount, maxErrorsExceeded, errors) =
                    await ImportJsonArrayAsync(store, stream, collectionName, options);
                break;
            case ExportFormat.Csv:
                (importedCount, updatedCount, skippedCount, errorCount, maxErrorsExceeded, errors) =
                    await ImportCsvAsync(store, stream, collectionName, options);
                break;
            case ExportFormat.Bson:
                throw new NotSupportedException("BSON format is not yet supported");
            default:
                throw new ArgumentException($"Unsupported import format: {options.Format}");
        }

        return new ImportResult
        {
            ImportedCount = importedCount,
            UpdatedCount = updatedCount,
            SkippedCount = skippedCount,
            ErrorCount = errorCount,
            InputPath = string.Empty,
            Duration = DateTime.UtcNow - startTime,
            Errors = errors,
            MaxErrorsExceeded = maxErrorsExceeded
        };
    }

    /// <inheritdoc />
    public async Task<List<ImportError>> ValidateAsync(
        string inputPath,
        ImportOptions? options = null)
    {
        options ??= new ImportOptions();
        var errors = new List<ImportError>();

        if (!File.Exists(inputPath))
        {
            errors.Add(new ImportError
            {
                LineNumber = 0,
                Message = $"File not found: {inputPath}"
            });
            return errors;
        }

        await using var stream = File.OpenRead(inputPath);

        switch (options.Format)
        {
            case ExportFormat.JsonLines:
                errors = await ValidateJsonLinesAsync(stream, options);
                break;
            case ExportFormat.JsonArray:
                errors = await ValidateJsonArrayAsync(stream, options);
                break;
            case ExportFormat.Csv:
                errors = await ValidateCsvAsync(stream, options);
                break;
            case ExportFormat.Bson:
                throw new NotSupportedException("BSON format is not yet supported");
            default:
                throw new ArgumentException($"Unsupported import format: {options.Format}");
        }

        return errors;
    }

    #region Private Import Methods

    private async Task<(int imported, int updated, int skipped, int errors, bool maxErrorsExceeded, List<ImportError> errorList)>
        ImportJsonLinesAsync(
            IDocumentStore store,
            Stream stream,
            string collectionName,
            ImportOptions options)
    {
        var errors = new List<ImportError>();
        int importedCount = 0;
        int updatedCount = 0;
        int skippedCount = 0;
        int errorCount = 0;
        bool maxErrorsExceeded = false;
        int lineNumber = 0;

        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? line;

        // Pre-count lines for progress reporting
        var totalLines = 0;
        if (options.Progress != null)
        {
            var countReader = new StreamReader(new FileStream(((FileStream)stream).Name, FileMode.Open, FileAccess.Read));
            while (await countReader.ReadLineAsync() != null) totalLines++;
            countReader.Close();
            // Reset stream position
            stream.Position = 0;
        }

        while ((line = await reader.ReadLineAsync()) != null)
        {
            lineNumber++;
            options.CancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var document = ParseJsonDocument(line, lineNumber, options);
                if (document == null)
                {
                    errors.Add(new ImportError
                    {
                        LineNumber = lineNumber,
                        Message = "Failed to parse document",
                        RawData = line.Length > 200 ? line[..200] + "..." : line
                    });
                    errorCount++;
                }
                else
                {
                    var (success, wasUpdated, wasSkipped) = await ImportDocumentAsync(store, collectionName, document, options);

                    if (wasSkipped)
                        skippedCount++;
                    else if (wasUpdated)
                        updatedCount++;
                    else if (success)
                        importedCount++;
                    else
                        errorCount++;
                }
            }
            catch (Exception ex)
            {
                errors.Add(new ImportError
                {
                    LineNumber = lineNumber,
                    Message = ex.Message,
                    RawData = line.Length > 200 ? line[..200] + "..." : line
                });
                errorCount++;
            }

            // Check max errors
            if (options.MaxErrors.HasValue && errorCount > options.MaxErrors.Value)
            {
                maxErrorsExceeded = true;
                break;
            }

            // Report progress
            if (totalLines > 0)
            {
                options.Progress?.Report((double)lineNumber / totalLines);
            }
        }

        return (importedCount, updatedCount, skippedCount, errorCount, maxErrorsExceeded, errors);
    }

    private async Task<(int imported, int updated, int skipped, int errors, bool maxErrorsExceeded, List<ImportError> errorList)>
        ImportJsonArrayAsync(
            IDocumentStore store,
            Stream stream,
            string collectionName,
            ImportOptions options)
    {
        var errors = new List<ImportError>();
        int importedCount = 0;
        int updatedCount = 0;
        int skippedCount = 0;
        int errorCount = 0;
        bool maxErrorsExceeded = false;

        try
        {
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: options.CancellationToken);

            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                errors.Add(new ImportError
                {
                    LineNumber = 0,
                    Message = "JSON array expected but found " + doc.RootElement.ValueKind
                });
                return (0, 0, 0, 1, false, errors);
            }

            var elements = doc.RootElement.EnumerateArray().ToList();
            int totalElements = elements.Count;
            int index = 0;

            foreach (var element in elements)
            {
                options.CancellationToken.ThrowIfCancellationRequested();
                index++;

                try
                {
                    var document = ParseJsonElement(element, index, options);
                    if (document == null)
                    {
                        errors.Add(new ImportError
                        {
                            LineNumber = index,
                            Message = "Failed to parse document"
                        });
                        errorCount++;
                    }
                    else
                    {
                        var (success, wasUpdated, wasSkipped) = await ImportDocumentAsync(store, collectionName, document, options);

                        if (wasSkipped)
                            skippedCount++;
                        else if (wasUpdated)
                            updatedCount++;
                        else if (success)
                            importedCount++;
                        else
                            errorCount++;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(new ImportError
                    {
                        LineNumber = index,
                        Message = ex.Message
                    });
                    errorCount++;
                }

                // Check max errors
                if (options.MaxErrors.HasValue && errorCount > options.MaxErrors.Value)
                {
                    maxErrorsExceeded = true;
                    break;
                }

                // Report progress
                options.Progress?.Report((double)index / totalElements);
            }
        }
        catch (JsonException ex)
        {
            errors.Add(new ImportError
            {
                LineNumber = 0,
                Message = $"Invalid JSON: {ex.Message}"
            });
            errorCount++;
        }

        return (importedCount, updatedCount, skippedCount, errorCount, maxErrorsExceeded, errors);
    }

    private async Task<(int imported, int updated, int skipped, int errors, bool maxErrorsExceeded, List<ImportError> errorList)>
        ImportCsvAsync(
            IDocumentStore store,
            Stream stream,
            string collectionName,
            ImportOptions options)
    {
        var errors = new List<ImportError>();
        int importedCount = 0;
        int updatedCount = 0;
        int skippedCount = 0;
        int errorCount = 0;
        bool maxErrorsExceeded = false;
        int lineNumber = 0;

        using var reader = new StreamReader(stream, Encoding.UTF8);

        // Read header
        var headerLine = await reader.ReadLineAsync();
        if (headerLine == null)
        {
            errors.Add(new ImportError
            {
                LineNumber = 0,
                Message = "CSV file is empty"
            });
            return (0, 0, 0, 1, false, errors);
        }

        lineNumber++;
        var headers = ParseCsvLine(headerLine);

        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            lineNumber++;
            options.CancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var values = ParseCsvLine(line);
                var document = ParseCsvRow(headers, values, lineNumber, options);

                if (document == null)
                {
                    errors.Add(new ImportError
                    {
                        LineNumber = lineNumber,
                        Message = "Failed to parse CSV row",
                        RawData = line.Length > 200 ? line[..200] + "..." : line
                    });
                    errorCount++;
                }
                else
                {
                    var (success, wasUpdated, wasSkipped) = await ImportDocumentAsync(store, collectionName, document, options);

                    if (wasSkipped)
                        skippedCount++;
                    else if (wasUpdated)
                        updatedCount++;
                    else if (success)
                        importedCount++;
                    else
                        errorCount++;
                }
            }
            catch (Exception ex)
            {
                errors.Add(new ImportError
                {
                    LineNumber = lineNumber,
                    Message = ex.Message,
                    RawData = line.Length > 200 ? line[..200] + "..." : line
                });
                errorCount++;
            }

            // Check max errors
            if (options.MaxErrors.HasValue && errorCount > options.MaxErrors.Value)
            {
                maxErrorsExceeded = true;
                break;
            }
        }

        return (importedCount, updatedCount, skippedCount, errorCount, maxErrorsExceeded, errors);
    }

    #endregion

    #region Validation Methods

    private async Task<List<ImportError>> ValidateJsonLinesAsync(Stream stream, ImportOptions options)
    {
        var errors = new List<ImportError>();
        int lineNumber = 0;

        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? line;

        while ((line = await reader.ReadLineAsync()) != null)
        {
            lineNumber++;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var validationErrors = ValidateDocumentStructure(doc.RootElement, lineNumber);
                errors.AddRange(validationErrors);
            }
            catch (JsonException ex)
            {
                errors.Add(new ImportError
                {
                    LineNumber = lineNumber,
                    Message = $"Invalid JSON: {ex.Message}",
                    RawData = line.Length > 200 ? line[..200] + "..." : line
                });
            }
        }

        return errors;
    }

    private async Task<List<ImportError>> ValidateJsonArrayAsync(Stream stream, ImportOptions options)
    {
        var errors = new List<ImportError>();

        try
        {
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: options.CancellationToken);

            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                errors.Add(new ImportError
                {
                    LineNumber = 0,
                    Message = "JSON array expected"
                });
                return errors;
            }

            int index = 0;
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                index++;
                var validationErrors = ValidateDocumentStructure(element, index);
                errors.AddRange(validationErrors);
            }
        }
        catch (JsonException ex)
        {
            errors.Add(new ImportError
            {
                LineNumber = 0,
                Message = $"Invalid JSON: {ex.Message}"
            });
        }

        return errors;
    }

    private async Task<List<ImportError>> ValidateCsvAsync(Stream stream, ImportOptions options)
    {
        var errors = new List<ImportError>();

        using var reader = new StreamReader(stream, Encoding.UTF8);

        var headerLine = await reader.ReadLineAsync();
        if (headerLine == null)
        {
            errors.Add(new ImportError
            {
                LineNumber = 0,
                Message = "CSV file is empty"
            });
            return errors;
        }

        var headers = ParseCsvLine(headerLine);
        if (!headers.Contains("_id", StringComparer.OrdinalIgnoreCase))
        {
            errors.Add(new ImportError
            {
                LineNumber = 1,
                Message = "CSV must contain '_id' column"
            });
        }

        return errors;
    }

    #endregion

    #region Helper Methods

    private async Task<(bool success, bool wasUpdated, bool wasSkipped)> ImportDocumentAsync(
        IDocumentStore store,
        string collectionName,
        Document document,
        ImportOptions options)
    {
        try
        {
            bool exists = await store.ExistsAsync(collectionName, document.Id);

            if (exists)
            {
                switch (options.Mode)
                {
                    case ImportMode.Insert:
                        return (false, false, false); // Error - document exists
                    case ImportMode.SkipExisting:
                        return (true, false, true); // Skipped
                    case ImportMode.Upsert:
                    case ImportMode.ReplaceAll:
                        await store.UpdateAsync(collectionName, document);
                        return (true, true, false); // Updated
                }
            }
            else
            {
                await store.InsertAsync(collectionName, document);
                return (true, false, false); // Imported
            }

            return (false, false, false);
        }
        catch (Exception)
        {
            return (false, false, false);
        }
    }

    private Document? ParseJsonDocument(string json, int lineNumber, ImportOptions options)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return ParseJsonElement(doc.RootElement, lineNumber, options);
        }
        catch
        {
            return null;
        }
    }

    private Document? ParseJsonElement(JsonElement element, int lineNumber, ImportOptions options)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        // Get ID
        if (!element.TryGetProperty("_id", out var idElement) &&
            !element.TryGetProperty("id", out idElement))
        {
            return null;
        }

        var id = idElement.GetString();
        if (string.IsNullOrEmpty(id))
            return null;

        var document = new Document
        {
            Id = id,
            Data = new Dictionary<string, object>()
        };

        // Parse metadata if preserving
        if (options.PreserveMetadata)
        {
            if (element.TryGetProperty("_createdAt", out var createdAtElement) &&
                createdAtElement.TryGetDateTime(out var createdAt))
            {
                document.CreatedAt = createdAt;
            }

            if (element.TryGetProperty("_updatedAt", out var updatedAtElement) &&
                updatedAtElement.TryGetDateTime(out var updatedAt))
            {
                document.UpdatedAt = updatedAt;
            }

            if (element.TryGetProperty("_version", out var versionElement) &&
                versionElement.TryGetInt64(out var version))
            {
                document.Version = version;
            }
        }
        else
        {
            document.CreatedAt = DateTime.UtcNow;
            document.UpdatedAt = DateTime.UtcNow;
            document.Version = 1;
        }

        // Parse data fields
        foreach (var property in element.EnumerateObject())
        {
            var name = property.Name;
            if (name.StartsWith("_") && name != "_id")
                continue; // Skip metadata fields for data

            if (name == "_id" || name == "id")
                continue; // Skip ID, already handled

            document.Data[name] = ConvertJsonElement(property.Value);
        }

        return document;
    }

    private Document? ParseCsvRow(List<string> headers, List<string> values, int lineNumber, ImportOptions options)
    {
        if (headers.Count != values.Count)
        {
            return null;
        }

        // Find _id column
        var idIndex = headers.FindIndex(h => h.Equals("_id", StringComparison.OrdinalIgnoreCase));
        if (idIndex < 0 || idIndex >= values.Count || string.IsNullOrEmpty(values[idIndex]))
        {
            return null;
        }

        var document = new Document
        {
            Id = values[idIndex]!,
            Data = new Dictionary<string, object>(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };

        // Parse metadata if present
        if (options.PreserveMetadata)
        {
            var createdAtIndex = headers.FindIndex(h => h.Equals("_createdAt", StringComparison.OrdinalIgnoreCase));
            if (createdAtIndex >= 0 && createdAtIndex < values.Count &&
                DateTime.TryParse(values[createdAtIndex], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt))
            {
                document.CreatedAt = createdAt;
            }

            var updatedAtIndex = headers.FindIndex(h => h.Equals("_updatedAt", StringComparison.OrdinalIgnoreCase));
            if (updatedAtIndex >= 0 && updatedAtIndex < values.Count &&
                DateTime.TryParse(values[updatedAtIndex], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var updatedAt))
            {
                document.UpdatedAt = updatedAt;
            }

            var versionIndex = headers.FindIndex(h => h.Equals("_version", StringComparison.OrdinalIgnoreCase));
            if (versionIndex >= 0 && versionIndex < values.Count &&
                long.TryParse(values[versionIndex], out var version))
            {
                document.Version = version;
            }
        }

        // Parse data fields
        for (int i = 0; i < headers.Count; i++)
        {
            var header = headers[i];
            if (header.StartsWith("_"))
                continue; // Skip metadata fields

            if (!string.IsNullOrEmpty(values[i]))
            {
                document.Data[header] = values[i]!;
            }
        }

        return document;
    }

    private object ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => ConvertJsonElement(p.Value)),
            _ => element.ToString()
        };
    }

    private List<ImportError> ValidateDocumentStructure(JsonElement element, int lineNumber)
    {
        var errors = new List<ImportError>();

        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new ImportError
            {
                LineNumber = lineNumber,
                Message = "Document must be a JSON object"
            });
            return errors;
        }

        // Check for ID
        if (!element.TryGetProperty("_id", out var idElement) &&
            !element.TryGetProperty("id", out idElement))
        {
            errors.Add(new ImportError
            {
                LineNumber = lineNumber,
                Message = "Document must have '_id' or 'id' field"
            });
        }
        else if (idElement.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(idElement.GetString()))
        {
            errors.Add(new ImportError
            {
                LineNumber = lineNumber,
                Message = "Document '_id' must be a non-empty string"
            });
        }

        return errors;
    }

    private List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    // Escaped quote
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                values.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }

        values.Add(sb.ToString());
        return values;
    }

    #endregion
}
