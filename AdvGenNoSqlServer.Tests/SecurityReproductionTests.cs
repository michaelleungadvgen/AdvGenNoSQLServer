// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Storage.Storage;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

public class SecurityReproductionTests : IDisposable
{
    private readonly string _testBasePath;
    private readonly FileStorageManager _storageManager;

    public SecurityReproductionTests()
    {
        _testBasePath = Path.Combine(Path.GetTempPath(), $"SecurityRepro_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testBasePath);
        _storageManager = new FileStorageManager(_testBasePath);
    }

    [Fact]
    public async Task FileStorageManager_PathTraversal_DeleteOutsideBaseDirectory()
    {
        // Arrange
        var sensitiveFile = Path.Combine(Path.GetTempPath(), $"sensitive_{Guid.NewGuid()}.txt");
        File.WriteAllText(sensitiveFile, "SENSITIVE DATA");

        // The goal is to delete 'sensitiveFile' via the storage manager.
        // FileStorageManager structure: _basePath / collectionName / documentId.json

        // We need to construct a path that reaches sensitiveFile.
        // _testBasePath is Path.GetTempPath() / SecurityRepro_XXX
        // Path.Combine(_testBasePath, "..", "sensitive_XXX.txt.json") might work if we can control collectionName

        var relativePathToSensitiveFile = Path.GetRelativePath(_testBasePath, sensitiveFile);
        // Remove .json from what we want the final path to be, because FileStorageManager appends .json
        var documentIdWithTraversal = "../../" + Path.GetFileName(sensitiveFile).Replace(".json", "");
        if (documentIdWithTraversal.EndsWith(".txt"))
        {
            documentIdWithTraversal = documentIdWithTraversal.Substring(0, documentIdWithTraversal.Length - 4);
        }

        // Wait, the vulnerable code is:
        // var collectionPath = Path.Combine(_basePath, collectionName);
        // var documentPath = Path.Combine(collectionPath, $"{documentId}.json");

        // If _basePath = /tmp/SecurityRepro_XXX
        // collectionName = ".."
        // documentId = "sensitive_XXX.txt"
        // collectionPath = /tmp/SecurityRepro_XXX/.. -> /tmp
        // documentPath = /tmp/sensitive_XXX.txt.json

        var targetFileName = Path.GetFileName(sensitiveFile);
        var documentId = targetFileName.Replace(".json", "");
        if (documentId.EndsWith(".txt")) documentId = documentId.Substring(0, documentId.Length - 4);

        // We want documentPath to be sensitiveFile.
        // Since it appends .json, let's just try to see if it can delete something if we name it with .json
        var sensitiveFileWithJson = sensitiveFile + ".json";
        File.WriteAllText(sensitiveFileWithJson, "SENSITIVE DATA JSON");

        var collectionName = "..";
        var docId = Path.GetFileName(sensitiveFile); // this will have .json appended by the manager if we are not careful

        // Act & Assert
        // DeleteDocumentAsync appends .json to documentId
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await _storageManager.DeleteDocumentAsync("..", Path.GetFileNameWithoutExtension(sensitiveFileWithJson)));

        // Assert
        Assert.True(File.Exists(sensitiveFileWithJson), "File should NOT have been deleted!");

        // Clean up
        if (File.Exists(sensitiveFile)) File.Delete(sensitiveFile);
        if (File.Exists(sensitiveFileWithJson)) File.Delete(sensitiveFileWithJson);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testBasePath))
        {
            Directory.Delete(_testBasePath, true);
        }
    }
}
