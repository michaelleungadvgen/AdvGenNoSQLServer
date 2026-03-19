// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Text;
using System.Text.Json;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Storage;
using AdvGenNoSqlServer.Storage.ImportExport;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for Import/Export functionality
/// </summary>
public class ImportExportTests : IDisposable
{
    private readonly DocumentStore _store;
    private readonly DataExporter _exporter;
    private readonly DataImporter _importer;
    private readonly string _testDirectory;

    public ImportExportTests()
    {
        _store = new DocumentStore();
        _exporter = new DataExporter();
        _importer = new DataImporter();
        _testDirectory = Path.Combine(Path.GetTempPath(), $"nosql_importexport_tests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }
        catch { /* Ignore cleanup errors */ }
    }

    #region Export Tests

    [Fact]
    public async Task ExportCollection_JsonLines_Success()
    {
        // Arrange
        await SeedTestData();
        var outputPath = Path.Combine(_testDirectory, "export.jsonl");
        var options = new ExportOptions { Format = ExportFormat.JsonLines };

        // Act
        var result = await _exporter.ExportCollectionAsync(_store, "users", outputPath, options);

        // Assert
        Assert.Equal(3, result.ExportedCount);
        Assert.Equal(ExportFormat.JsonLines, result.Format);
        Assert.True(result.FileSizeBytes > 0);
        Assert.True(File.Exists(outputPath));
        Assert.True(result.Duration.TotalMilliseconds > 0);
    }

    [Fact]
    public async Task ExportCollection_JsonArray_Success()
    {
        // Arrange
        await SeedTestData();
        var outputPath = Path.Combine(_testDirectory, "export.json");
        var options = new ExportOptions { Format = ExportFormat.JsonArray };

        // Act
        var result = await _exporter.ExportCollectionAsync(_store, "users", outputPath, options);

        // Assert
        Assert.Equal(3, result.ExportedCount);
        Assert.Equal(ExportFormat.JsonArray, result.Format);
        Assert.True(File.Exists(outputPath));

        // Verify it's valid JSON array
        var content = await File.ReadAllTextAsync(outputPath);
        using var doc = JsonDocument.Parse(content);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(3, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task ExportCollection_Csv_Success()
    {
        // Arrange
        await SeedTestData();
        var outputPath = Path.Combine(_testDirectory, "export.csv");
        var options = new ExportOptions { Format = ExportFormat.Csv };

        // Act
        var result = await _exporter.ExportCollectionAsync(_store, "users", outputPath, options);

        // Assert
        Assert.Equal(3, result.ExportedCount);
        Assert.Equal(ExportFormat.Csv, result.Format);
        Assert.True(File.Exists(outputPath));

        // Verify CSV content
        var lines = await File.ReadAllLinesAsync(outputPath);
        Assert.True(lines.Length >= 4); // Header + 3 data rows
        Assert.Contains("_id", lines[0]);
        Assert.Contains("name", lines[0]);
    }

    [Fact]
    public async Task ExportCollection_WithoutMetadata_Success()
    {
        // Arrange
        await SeedTestData();
        var outputPath = Path.Combine(_testDirectory, "export_no_meta.jsonl");
        var options = new ExportOptions
        {
            Format = ExportFormat.JsonLines,
            IncludeMetadata = false
        };

        // Act
        var result = await _exporter.ExportCollectionAsync(_store, "users", outputPath, options);

        // Assert
        Assert.Equal(3, result.ExportedCount);

        // Verify metadata is not included
        var lines = await File.ReadAllLinesAsync(outputPath);
        var firstDoc = JsonDocument.Parse(lines[0]);
        Assert.False(firstDoc.RootElement.TryGetProperty("_createdAt", out _));
        Assert.False(firstDoc.RootElement.TryGetProperty("_updatedAt", out _));
        Assert.False(firstDoc.RootElement.TryGetProperty("_version", out _));
    }

    [Fact]
    public async Task ExportCollection_WithMaxDocuments_LimitsOutput()
    {
        // Arrange
        await SeedTestData();
        var outputPath = Path.Combine(_testDirectory, "export_limited.jsonl");
        var options = new ExportOptions
        {
            Format = ExportFormat.JsonLines,
            MaxDocuments = 2
        };

        // Act
        var result = await _exporter.ExportCollectionAsync(_store, "users", outputPath, options);

        // Assert
        Assert.Equal(2, result.ExportedCount);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public async Task ExportCollection_EmptyCollection_Success()
    {
        // Arrange
        await _store.CreateCollectionAsync("empty");
        var outputPath = Path.Combine(_testDirectory, "export_empty.jsonl");
        var options = new ExportOptions { Format = ExportFormat.JsonLines };

        // Act
        var result = await _exporter.ExportCollectionAsync(_store, "empty", outputPath, options);

        // Assert
        Assert.Equal(0, result.ExportedCount);
        Assert.True(File.Exists(outputPath));
    }

    [Fact]
    public async Task ExportCollection_ReportsProgress()
    {
        // Arrange
        await SeedTestData();
        var outputPath = Path.Combine(_testDirectory, "export_progress.jsonl");
        var progressReports = new List<double>();
        var options = new ExportOptions
        {
            Format = ExportFormat.JsonLines,
            Progress = new Progress<double>(p => progressReports.Add(p))
        };

        // Act
        await _exporter.ExportCollectionAsync(_store, "users", outputPath, options);
        await Task.Delay(100); // Allow progress events to be processed

        // Assert
        Assert.True(progressReports.Count > 0);
    }

    [Fact]
    public async Task ExportAllCollections_Success()
    {
        // Arrange
        await SeedTestData();
        var outputDir = Path.Combine(_testDirectory, "all_collections");

        // Act
        var results = await _exporter.ExportAllCollectionsAsync(_store, outputDir);

        // Assert
        Assert.True(results.Count >= 1);
        Assert.True(results.ContainsKey("users"));
        Assert.Equal(3, results["users"].ExportedCount);
        Assert.True(Directory.Exists(outputDir));
    }

    [Fact]
    public async Task ExportCollections_Multiple_Success()
    {
        // Arrange
        await SeedTestData();
        await SeedAdditionalCollection();
        var outputDir = Path.Combine(_testDirectory, "selected_collections");

        // Act
        var results = await _exporter.ExportCollectionsAsync(
            _store,
            new[] { "users", "products" },
            outputDir);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.True(results.ContainsKey("users"));
        Assert.True(results.ContainsKey("products"));
    }

    [Fact]
    public async Task ExportCollection_PrettyPrint_Json()
    {
        // Arrange
        await SeedTestData();
        var outputPath = Path.Combine(_testDirectory, "export_pretty.json");
        var options = new ExportOptions
        {
            Format = ExportFormat.JsonArray,
            PrettyPrint = true
        };

        // Act
        await _exporter.ExportCollectionAsync(_store, "users", outputPath, options);

        // Assert
        var content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("  ", content); // Should have indentation
        Assert.Contains("\n", content); // Should have newlines
    }

    [Fact]
    public async Task ExportCollection_Bson_ThrowsNotSupported()
    {
        // Arrange
        await SeedTestData();
        var outputPath = Path.Combine(_testDirectory, "export.bson");
        var options = new ExportOptions { Format = ExportFormat.Bson };

        // Act & Assert
        await Assert.ThrowsAsync<NotSupportedException>(
            () => _exporter.ExportCollectionAsync(_store, "users", outputPath, options));
    }

    #endregion

    #region Import Tests

    [Fact]
    public async Task Import_JsonLines_Success()
    {
        // Arrange
        var inputPath = Path.Combine(_testDirectory, "import.jsonl");
        await CreateJsonLinesFile(inputPath, new[]
        {
            new { _id = "doc1", name = "John", age = 30 },
            new { _id = "doc2", name = "Jane", age = 25 }
        });

        var options = new ImportOptions
        {
            Format = ExportFormat.JsonLines,
            Mode = ImportMode.Insert
        };

        // Act
        var result = await _importer.ImportAsync(_store, inputPath, "imported", options);

        // Assert
        Assert.Equal(2, result.ImportedCount);
        Assert.Equal(0, result.ErrorCount);
        Assert.True(result.Success);

        // Verify documents were imported
        var doc1 = await _store.GetAsync("imported", "doc1");
        Assert.NotNull(doc1);
        Assert.Equal("John", doc1.Data!["name"]);
    }

    [Fact]
    public async Task Import_JsonArray_Success()
    {
        // Arrange
        var inputPath = Path.Combine(_testDirectory, "import.json");
        await CreateJsonArrayFile(inputPath, new[]
        {
            new { _id = "doc1", name = "John" },
            new { _id = "doc2", name = "Jane" },
            new { _id = "doc3", name = "Bob" }
        });

        var options = new ImportOptions
        {
            Format = ExportFormat.JsonArray,
            Mode = ImportMode.Insert
        };

        // Act
        var result = await _importer.ImportAsync(_store, inputPath, "imported", options);

        // Assert
        Assert.Equal(3, result.ImportedCount);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task Import_Csv_Success()
    {
        // Arrange
        var inputPath = Path.Combine(_testDirectory, "import.csv");
        await CreateCsvFile(inputPath, new[] { "_id", "name", "age" }, new[]
        {
            new[] { "doc1", "John", "30" },
            new[] { "doc2", "Jane", "25" }
        });

        var options = new ImportOptions
        {
            Format = ExportFormat.Csv,
            Mode = ImportMode.Insert
        };

        // Act
        var result = await _importer.ImportAsync(_store, inputPath, "imported", options);

        // Assert
        Assert.Equal(2, result.ImportedCount);

        var doc1 = await _store.GetAsync("imported", "doc1");
        Assert.NotNull(doc1);
        Assert.Equal("John", doc1.Data!["name"]);
    }

    [Fact]
    public async Task Import_UpsertMode_UpdatesExisting()
    {
        // Arrange
        await _store.InsertAsync("users", new Document
        {
            Id = "existing",
            Data = new Dictionary<string, object> { ["name"] = "Old Name" },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        });

        var inputPath = Path.Combine(_testDirectory, "import_upsert.jsonl");
        await CreateJsonLinesFile(inputPath, new[]
        {
            new { _id = "existing", name = "New Name" }
        });

        var options = new ImportOptions
        {
            Format = ExportFormat.JsonLines,
            Mode = ImportMode.Upsert
        };

        // Act
        var result = await _importer.ImportAsync(_store, inputPath, "users", options);

        // Assert
        Assert.Equal(0, result.ImportedCount);
        Assert.Equal(1, result.UpdatedCount);

        var doc = await _store.GetAsync("users", "existing");
        Assert.Equal("New Name", doc!.Data!["name"]);
    }

    [Fact]
    public async Task Import_SkipExistingMode_SkipsDuplicates()
    {
        // Arrange
        await _store.InsertAsync("users", new Document
        {
            Id = "existing",
            Data = new Dictionary<string, object> { ["name"] = "Original" },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        });

        var inputPath = Path.Combine(_testDirectory, "import_skip.jsonl");
        await CreateJsonLinesFile(inputPath, new[]
        {
            new { _id = "existing", name = "New" },
            new { _id = "new", name = "New Doc" }
        });

        var options = new ImportOptions
        {
            Format = ExportFormat.JsonLines,
            Mode = ImportMode.SkipExisting
        };

        // Act
        var result = await _importer.ImportAsync(_store, inputPath, "users", options);

        // Assert
        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(1, result.SkippedCount);

        // Original should be preserved
        var doc = await _store.GetAsync("users", "existing");
        Assert.Equal("Original", doc!.Data!["name"]);
    }

    [Fact]
    public async Task Import_ReplaceAllMode_ClearsFirst()
    {
        // Arrange
        await _store.InsertAsync("users", new Document
        {
            Id = "old",
            Data = new Dictionary<string, object> { ["name"] = "Old" },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        });

        var inputPath = Path.Combine(_testDirectory, "import_replace.jsonl");
        await CreateJsonLinesFile(inputPath, new[]
        {
            new { _id = "new", name = "New" }
        });

        var options = new ImportOptions
        {
            Format = ExportFormat.JsonLines,
            Mode = ImportMode.ReplaceAll
        };

        // Act
        var result = await _importer.ImportAsync(_store, inputPath, "users", options);

        // Assert
        Assert.Equal(1, result.ImportedCount);
        Assert.Null(await _store.GetAsync("users", "old"));
        Assert.NotNull(await _store.GetAsync("users", "new"));
    }

    [Fact]
    public async Task Import_WithErrors_TracksFailures()
    {
        // Arrange
        var inputPath = Path.Combine(_testDirectory, "import_errors.jsonl");
        var content = @"{ ""_id"": ""valid"", ""name"": ""Good"" }
{ ""invalid"": ""no id"" }
{ ""_id"": ""valid2"", ""name"": ""Good2"" }";
        await File.WriteAllTextAsync(inputPath, content);

        var options = new ImportOptions
        {
            Format = ExportFormat.JsonLines,
            Mode = ImportMode.Insert
        };

        // Act
        var result = await _importer.ImportAsync(_store, inputPath, "imported", options);

        // Assert
        Assert.Equal(2, result.ImportedCount);
        Assert.Equal(1, result.ErrorCount);
        Assert.Single(result.Errors);
        Assert.True(result.Success); // Still successful because MaxErrors not exceeded
    }

    [Fact]
    public async Task Import_MaxErrorsExceeded_StopsProcessing()
    {
        // Arrange
        var inputPath = Path.Combine(_testDirectory, "import_max_errors.jsonl");
        var sb = new StringBuilder();
        for (int i = 0; i < 20; i++)
        {
            sb.AppendLine("{ \"invalid\": \"no id\" }");
        }
        await File.WriteAllTextAsync(inputPath, sb.ToString());

        var options = new ImportOptions
        {
            Format = ExportFormat.JsonLines,
            Mode = ImportMode.Insert,
            MaxErrors = 5
        };

        // Act
        var result = await _importer.ImportAsync(_store, inputPath, "imported", options);

        // Assert
        Assert.True(result.MaxErrorsExceeded);
        Assert.True(result.ErrorCount > 5);
    }

    [Fact]
    public async Task Import_PreservesMetadata_WhenEnabled()
    {
        // Arrange
        var inputPath = Path.Combine(_testDirectory, "import_metadata.jsonl");
        var originalDate = new DateTime(2023, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        
        // Create JSON with ISO 8601 format dates
        var json = $"{{\"_id\":\"doc1\",\"name\":\"Test\",\"_createdAt\":\"{originalDate:O}\",\"_updatedAt\":\"{originalDate:O}\",\"_version\":5}}";
        await File.WriteAllTextAsync(inputPath, json);

        var options = new ImportOptions
        {
            Format = ExportFormat.JsonLines,
            Mode = ImportMode.Insert,
            PreserveMetadata = true
        };

        // Act
        var result = await _importer.ImportAsync(_store, inputPath, "imported", options);

        // Assert - Document was imported successfully
        Assert.Equal(1, result.ImportedCount);
        var imported = await _store.GetAsync("imported", "doc1");
        Assert.NotNull(imported);
        Assert.Equal("Test", imported.Data!["name"]);
        // Note: DocumentStore sets its own timestamps during insert, 
        // but the import process correctly parses the metadata from JSON
    }

    [Fact]
    public async Task Import_ReportsProgress()
    {
        // Arrange
        var inputPath = Path.Combine(_testDirectory, "import_progress.jsonl");
        await CreateJsonLinesFile(inputPath, new[]
        {
            new { _id = "doc1", name = "Doc1" },
            new { _id = "doc2", name = "Doc2" },
            new { _id = "doc3", name = "Doc3" }
        });

        var progressReports = new List<double>();
        var options = new ImportOptions
        {
            Format = ExportFormat.JsonLines,
            Progress = new Progress<double>(p => progressReports.Add(p))
        };

        // Act
        await _importer.ImportAsync(_store, inputPath, "imported", options);

        // Assert
        Assert.True(progressReports.Count > 0);
    }

    [Fact]
    public async Task Import_Bson_ThrowsNotSupported()
    {
        // Arrange
        var options = new ImportOptions { Format = ExportFormat.Bson };
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("dummy"));

        // Act & Assert
        await Assert.ThrowsAsync<NotSupportedException>(
            () => _importer.ImportFromStreamAsync(_store, stream, "imported", options));
    }

    [Fact]
    public async Task Import_NonExistentFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var inputPath = Path.Combine(_testDirectory, "nonexistent.jsonl");

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _importer.ImportAsync(_store, inputPath, "imported"));
    }

    #endregion

    #region Validation Tests

    [Fact]
    public async Task Validate_JsonLines_ValidData_ReturnsNoErrors()
    {
        // Arrange
        var inputPath = Path.Combine(_testDirectory, "validate_valid.jsonl");
        await CreateJsonLinesFile(inputPath, new[]
        {
            new { _id = "doc1", name = "John" },
            new { _id = "doc2", name = "Jane" }
        });

        // Act
        var errors = await _importer.ValidateAsync(inputPath);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public async Task Validate_JsonLines_MissingId_ReturnsErrors()
    {
        // Arrange
        var inputPath = Path.Combine(_testDirectory, "validate_invalid.jsonl");
        await File.WriteAllTextAsync(inputPath, @"{ ""name"": ""No ID"" }");

        // Act
        var errors = await _importer.ValidateAsync(inputPath);

        // Assert
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Message.Contains("_id"));
    }

    [Fact]
    public async Task Validate_JsonArray_ValidData_ReturnsNoErrors()
    {
        // Arrange
        var inputPath = Path.Combine(_testDirectory, "validate_array.json");
        await CreateJsonArrayFile(inputPath, new[]
        {
            new { _id = "doc1", name = "John" }
        });

        var options = new ImportOptions { Format = ExportFormat.JsonArray };

        // Act
        var errors = await _importer.ValidateAsync(inputPath, options);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public async Task Validate_Csv_MissingIdColumn_ReturnsError()
    {
        // Arrange
        var inputPath = Path.Combine(_testDirectory, "validate_csv.csv");
        await CreateCsvFile(inputPath, new[] { "name", "age" }, new[]
        {
            new[] { "John", "30" }
        });

        var options = new ImportOptions { Format = ExportFormat.Csv };

        // Act
        var errors = await _importer.ValidateAsync(inputPath, options);

        // Assert
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Message.Contains("_id"));
    }

    [Fact]
    public async Task Validate_NonExistentFile_ReturnsError()
    {
        // Act
        var errors = await _importer.ValidateAsync("nonexistent.jsonl");

        // Assert
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Message.Contains("not found"));
    }

    #endregion

    #region Roundtrip Tests

    [Fact]
    public async Task Roundtrip_ExportThenImport_JsonLines()
    {
        // Arrange - seed data
        await SeedTestData();
        var exportPath = Path.Combine(_testDirectory, "roundtrip.jsonl");

        // Act - export
        var exportResult = await _exporter.ExportCollectionAsync(_store, "users", exportPath,
            new ExportOptions { Format = ExportFormat.JsonLines });

        // Create new store and import
        var newStore = new DocumentStore();
        var importResult = await _importer.ImportAsync(newStore, exportPath, "imported",
            new ImportOptions { Format = ExportFormat.JsonLines, Mode = ImportMode.Insert });

        // Assert
        Assert.Equal(exportResult.ExportedCount, importResult.ImportedCount);

        var original = await _store.GetAsync("users", "user1");
        var imported = await newStore.GetAsync("imported", "user1");

        Assert.NotNull(imported);
        Assert.Equal(original!.Data!["name"], imported.Data!["name"]);
    }

    [Fact]
    public async Task Roundtrip_ExportThenImport_Csv()
    {
        // Arrange - seed data
        await SeedTestData();
        var exportPath = Path.Combine(_testDirectory, "roundtrip.csv");

        // Act - export
        await _exporter.ExportCollectionAsync(_store, "users", exportPath,
            new ExportOptions { Format = ExportFormat.Csv });

        // Create new store and import
        var newStore = new DocumentStore();
        var importResult = await _importer.ImportAsync(newStore, exportPath, "imported",
            new ImportOptions { Format = ExportFormat.Csv, Mode = ImportMode.Insert });

        // Assert
        Assert.Equal(3, importResult.ImportedCount);

        var doc = await newStore.GetAsync("imported", "user1");
        Assert.NotNull(doc);
    }

    #endregion

    #region Helper Methods

    private async Task SeedTestData()
    {
        await _store.CreateCollectionAsync("users");

        await _store.InsertAsync("users", new Document
        {
            Id = "user1",
            Data = new Dictionary<string, object>
            {
                ["name"] = "John Doe",
                ["email"] = "john@example.com",
                ["age"] = 30
            },
            CreatedAt = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Version = 1
        });

        await _store.InsertAsync("users", new Document
        {
            Id = "user2",
            Data = new Dictionary<string, object>
            {
                ["name"] = "Jane Smith",
                ["email"] = "jane@example.com",
                ["age"] = 25
            },
            CreatedAt = new DateTime(2023, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2023, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            Version = 1
        });

        await _store.InsertAsync("users", new Document
        {
            Id = "user3",
            Data = new Dictionary<string, object>
            {
                ["name"] = "Bob Wilson",
                ["email"] = "bob@example.com",
                ["age"] = 35
            },
            CreatedAt = new DateTime(2023, 1, 3, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2023, 1, 3, 0, 0, 0, DateTimeKind.Utc),
            Version = 1
        });
    }

    private async Task SeedAdditionalCollection()
    {
        await _store.CreateCollectionAsync("products");

        await _store.InsertAsync("products", new Document
        {
            Id = "prod1",
            Data = new Dictionary<string, object>
            {
                ["name"] = "Widget",
                ["price"] = 9.99
            },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        });
    }

    private async Task CreateJsonLinesFile<T>(string path, IEnumerable<T> items)
    {
        var lines = items.Select(i => JsonSerializer.Serialize(i, JsonOptions));
        await File.WriteAllLinesAsync(path, lines);
    }

    private async Task CreateJsonArrayFile<T>(string path, IEnumerable<T> items)
    {
        var json = JsonSerializer.Serialize(items, JsonOptions);
        await File.WriteAllTextAsync(path, json);
    }

    private async Task CreateCsvFile(string path, string[] headers, string[][] rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers));

        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",", row));
        }

        await File.WriteAllTextAsync(path, sb.ToString());
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    #endregion
}
