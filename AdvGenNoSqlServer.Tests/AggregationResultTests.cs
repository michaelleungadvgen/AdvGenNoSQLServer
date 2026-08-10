// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Query.Aggregation;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for the <see cref="AggregationResult"/> class
/// </summary>
public class AggregationResultTests
{
    [Fact]
    public void SuccessResult_CreatesValidResult()
    {
        // Arrange
        var documents = new List<Document>
        {
            new() { Id = "1", Data = new() { ["key"] = "value1" } },
            new() { Id = "2", Data = new() { ["key"] = "value2" } }
        };
        var stagesExecuted = 3;
        var executionTimeMs = 150L;

        // Act
        var result = AggregationResult.SuccessResult(documents, stagesExecuted, executionTimeMs);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(documents, result.Documents);
        Assert.Equal(documents.Count, result.Count);
        Assert.Equal(stagesExecuted, result.StagesExecuted);
        Assert.Equal(executionTimeMs, result.ExecutionTimeMs);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void FailureResult_CreatesValidResult()
    {
        // Arrange
        var errorMessage = "Pipeline execution failed";

        // Act
        var result = AggregationResult.FailureResult(errorMessage);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal(errorMessage, result.ErrorMessage);
        Assert.NotNull(result.Documents);
        Assert.Empty(result.Documents);
        Assert.Equal(0, result.Count);
        Assert.Equal(0, result.StagesExecuted);
        Assert.Equal(0, result.ExecutionTimeMs);
    }
}
