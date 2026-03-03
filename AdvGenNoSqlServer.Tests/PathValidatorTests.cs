// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.IO;
using AdvGenNoSqlServer.Core.Security;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for the PathValidator class.
/// </summary>
public class PathValidatorTests
{
    private readonly string _basePath;

    public PathValidatorTests()
    {
        // Use a consistent base path for testing. In Windows it might be "C:\test\data", in Linux "/test/data".
        // Using Path.Combine with a root-like structure ensures it's treated as absolute.
        _basePath = Path.Combine(Path.GetTempPath(), "AdvGenNoSql_Tests_Data");
    }

    [Fact]
    public void GetSafePath_ValidChildPath_ReturnsFullPath()
    {
        // Arrange
        var childFile = "config.json";
        var pathToCheck = Path.Combine(_basePath, childFile);

        // Act
        var result = PathValidator.GetSafePath(_basePath, pathToCheck);

        // Assert
        Assert.Equal(Path.GetFullPath(pathToCheck), result);
    }

    [Fact]
    public void GetSafePath_ExactMatch_ReturnsFullPath()
    {
        // Act
        var result = PathValidator.GetSafePath(_basePath, _basePath);

        // Assert
        Assert.Equal(Path.GetFullPath(_basePath), result);
    }

    [Fact]
    public void GetSafePath_TraversalEscapingBasePath_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var pathToCheck = Path.Combine(_basePath, "..", "secrets.txt");

        // Act & Assert
        var ex = Assert.Throws<UnauthorizedAccessException>(() => PathValidator.GetSafePath(_basePath, pathToCheck));
        Assert.Contains("Path traversal attempt detected", ex.Message);
    }

    [Fact]
    public void GetSafePath_TraversalWithinBasePath_ReturnsNormalizedFullPath()
    {
        // Arrange
        var pathToCheck = Path.Combine(_basePath, "folder", "..", "file.txt");
        var expectedPath = Path.GetFullPath(Path.Combine(_basePath, "file.txt"));

        // Act
        var result = PathValidator.GetSafePath(_basePath, pathToCheck);

        // Assert
        Assert.Equal(expectedPath, result);
    }

    [Fact]
    public void GetSafePath_PrefixBasedTraversal_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        // If _basePath is "/tmp/AdvGenNoSql_Tests_Data", a prefix traversal attempts to access
        // "/tmp/AdvGenNoSql_Tests_Data_secrets"
        var pathToCheck = _basePath + "_secrets";

        // Act & Assert
        var ex = Assert.Throws<UnauthorizedAccessException>(() => PathValidator.GetSafePath(_basePath, pathToCheck));
        Assert.Contains("Path traversal attempt detected", ex.Message);
    }

    [Fact]
    public void GetSafePath_AbsolutePathOutsideBasePath_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var pathToCheck = Path.Combine(Path.GetTempPath(), "completely_outside.txt");

        // Act & Assert
        var ex = Assert.Throws<UnauthorizedAccessException>(() => PathValidator.GetSafePath(_basePath, pathToCheck));
        Assert.Contains("Path traversal attempt detected", ex.Message);
    }
}
