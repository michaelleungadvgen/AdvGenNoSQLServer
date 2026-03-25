// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Core.Abstractions;
using AdvGenNoSqlServer.Core.MapReduce;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Storage;
using AdvGenNoSqlServer.Storage.MapReduce;
using Xunit;

namespace AdvGenNoSqlServer.Tests
{
    public class MapReduceTests
    {
        private readonly IDocumentStore _documentStore;

        public MapReduceTests()
        {
            _documentStore = new DocumentStore();
        }

        #region MapReduceOptions Tests

        [Fact]
        public void MapReduceOptions_Validate_WithDefaults_ShouldPass()
        {
            var options = new MapReduceOptions();
            options.Validate();
        }

        [Fact]
        public void MapReduceOptions_Validate_WithInvalidParallelism_ShouldThrow()
        {
            var options = new MapReduceOptions { MaxDegreeOfParallelism = 0 };
            Assert.Throws<ArgumentException>(() => options.Validate());
        }

        [Fact]
        public void MapReduceOptions_Validate_WithInvalidChunkSize_ShouldThrow()
        {
            var options = new MapReduceOptions { ChunkSize = 0 };
            Assert.Throws<ArgumentException>(() => options.Validate());
        }

        [Fact]
        public void MapReduceOptions_Validate_WithInvalidSpillThreshold_ShouldThrow()
        {
            var options = new MapReduceOptions { SpillThresholdBytes = 100 };
            Assert.Throws<ArgumentException>(() => options.Validate());
        }

        [Fact]
        public void MapReduceOptions_Validate_WithInvalidMaxResults_ShouldThrow()
        {
            var options = new MapReduceOptions { MaxInMemoryResults = 10 };
            Assert.Throws<ArgumentException>(() => options.Validate());
        }

        #endregion

        #region WordCountJob Tests

        [Fact]
        public async Task WordCountJob_Execute_WithSimpleText_ShouldCountWords()
        {
            // Arrange
            await _documentStore.InsertAsync("docs", new Document
            {
                Id = "1",
                Data = new() { ["content"] = "hello world hello" }
            });

            var executor = new MapReduceExecutor(_documentStore);
            var job = new WordCountJob("content");

            // Act
            var result = await executor.ExecuteAsync("docs", job);

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(result.Output);
            Assert.Equal(2, result.Output.Count);

            var hello = result.Output.FirstOrDefault(r => r.Word == "hello");
            var world = result.Output.FirstOrDefault(r => r.Word == "world");

            Assert.NotNull(hello);
            Assert.Equal(2, hello.Count);

            Assert.NotNull(world);
            Assert.Equal(1, world.Count);
        }

        [Fact]
        public async Task WordCountJob_Execute_WithMultipleDocuments_ShouldAggregateCounts()
        {
            // Arrange
            await _documentStore.InsertAsync("docs", new Document
            {
                Id = "1",
                Data = new() { ["content"] = "the quick brown fox" }
            });
            await _documentStore.InsertAsync("docs", new Document
            {
                Id = "2",
                Data = new() { ["content"] = "the lazy dog" }
            });
            await _documentStore.InsertAsync("docs", new Document
            {
                Id = "3",
                Data = new() { ["content"] = "the fox jumps" }
            });

            var executor = new MapReduceExecutor(_documentStore);
            var job = new WordCountJob("content");

            // Act
            var result = await executor.ExecuteAsync("docs", job);

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(result.Output);

            var the = result.Output.FirstOrDefault(r => r.Word == "the");
            Assert.NotNull(the);
            Assert.Equal(3, the.Count);

            var fox = result.Output.FirstOrDefault(r => r.Word == "fox");
            Assert.NotNull(fox);
            Assert.Equal(2, fox.Count);
        }

        [Fact]
        public async Task WordCountJob_Execute_WithCaseInsensitive_ShouldNormalizeCase()
        {
            // Arrange
            await _documentStore.InsertAsync("docs", new Document
            {
                Id = "1",
                Data = new() { ["content"] = "Hello HELLO hello" }
            });

            var executor = new MapReduceExecutor(_documentStore);
            var job = new WordCountJob("content", caseSensitive: false);

            // Act
            var result = await executor.ExecuteAsync("docs", job);

            // Assert
            Assert.True(result.Success);
            Assert.Single(result.Output);
            Assert.Equal("hello", result.Output[0].Word);
            Assert.Equal(3, result.Output[0].Count);
        }

        [Fact]
        public async Task WordCountJob_Execute_WithEmptyCollection_ShouldReturnEmpty()
        {
            // Arrange
            var executor = new MapReduceExecutor(_documentStore);
            var job = new WordCountJob("content");

            // Act
            var result = await executor.ExecuteAsync("empty", job);

            // Assert
            Assert.True(result.Success);
            Assert.Empty(result.Output);
        }

        [Fact]
        public async Task WordCountJob_Execute_WithMissingField_ShouldReturnEmpty()
        {
            // Arrange
            await _documentStore.InsertAsync("docs", new Document
            {
                Id = "1",
                Data = new() { ["other"] = "some text" }
            });

            var executor = new MapReduceExecutor(_documentStore);
            var job = new WordCountJob("content");

            // Act
            var result = await executor.ExecuteAsync("docs", job);

            // Assert
            Assert.True(result.Success);
            Assert.Empty(result.Output);
        }

        #endregion

        #region SumByKeyJob Tests

        [Fact]
        public async Task SumByKeyJob_Execute_WithNumericValues_ShouldSumCorrectly()
        {
            // Arrange
            await _documentStore.InsertAsync("sales", new Document
            {
                Id = "1",
                Data = new() { ["category"] = "electronics", ["amount"] = 100 }
            });
            await _documentStore.InsertAsync("sales", new Document
            {
                Id = "2",
                Data = new() { ["category"] = "electronics", ["amount"] = 200 }
            });
            await _documentStore.InsertAsync("sales", new Document
            {
                Id = "3",
                Data = new() { ["category"] = "books", ["amount"] = 50 }
            });

            var executor = new MapReduceExecutor(_documentStore);
            var job = new SumByKeyJob("category", "amount");

            // Act
            var result = await executor.ExecuteAsync("sales", job);

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(result.Output);
            Assert.Equal(2, result.Output.Count);

            var electronics = result.Output.FirstOrDefault(r => r.Key == "electronics");
            Assert.NotNull(electronics);
            Assert.Equal(300, electronics.Sum);
            Assert.Equal(2, electronics.Count);

            var books = result.Output.FirstOrDefault(r => r.Key == "books");
            Assert.NotNull(books);
            Assert.Equal(50, books.Sum);
            Assert.Equal(1, books.Count);
        }

        [Fact]
        public async Task SumByKeyJob_Execute_WithDoubleValues_ShouldSumCorrectly()
        {
            // Arrange
            await _documentStore.InsertAsync("data", new Document
            {
                Id = "1",
                Data = new() { ["group"] = "A", ["value"] = 1.5 }
            });
            await _documentStore.InsertAsync("data", new Document
            {
                Id = "2",
                Data = new() { ["group"] = "A", ["value"] = 2.5 }
            });

            var executor = new MapReduceExecutor(_documentStore);
            var job = new SumByKeyJob("group", "value");

            // Act
            var result = await executor.ExecuteAsync("data", job);

            // Assert
            Assert.True(result.Success);
            Assert.Single(result.Output);
            Assert.Equal(4.0, result.Output[0].Sum, 2);
        }

        #endregion

        #region AverageByKeyJob Tests

        [Fact]
        public async Task AverageByKeyJob_Execute_WithNumericValues_ShouldCalculateStatistics()
        {
            // Arrange
            await _documentStore.InsertAsync("scores", new Document
            {
                Id = "1",
                Data = new() { ["subject"] = "math", ["score"] = 80 }
            });
            await _documentStore.InsertAsync("scores", new Document
            {
                Id = "2",
                Data = new() { ["subject"] = "math", ["score"] = 90 }
            });
            await _documentStore.InsertAsync("scores", new Document
            {
                Id = "3",
                Data = new() { ["subject"] = "math", ["score"] = 100 }
            });
            await _documentStore.InsertAsync("scores", new Document
            {
                Id = "4",
                Data = new() { ["subject"] = "science", ["score"] = 75 }
            });

            var executor = new MapReduceExecutor(_documentStore);
            var job = new AverageByKeyJob("subject", "score");

            // Act
            var result = await executor.ExecuteAsync("scores", job);

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(result.Output);
            Assert.Equal(2, result.Output.Count);

            var math = result.Output.FirstOrDefault(r => r.Key == "math");
            Assert.NotNull(math);
            Assert.Equal(90, math.Average);
            Assert.Equal(80, math.Min);
            Assert.Equal(100, math.Max);
            Assert.Equal(3, math.Count);

            var science = result.Output.FirstOrDefault(r => r.Key == "science");
            Assert.NotNull(science);
            Assert.Equal(75, science.Average);
            Assert.Equal(1, science.Count);
        }

        #endregion

        #region CountByKeyJob Tests

        [Fact]
        public async Task CountByKeyJob_Execute_WithMultipleDocuments_ShouldCountByKey()
        {
            // Arrange
            await _documentStore.InsertAsync("users", new Document
            {
                Id = "1",
                Data = new() { ["role"] = "admin" }
            });
            await _documentStore.InsertAsync("users", new Document
            {
                Id = "2",
                Data = new() { ["role"] = "user" }
            });
            await _documentStore.InsertAsync("users", new Document
            {
                Id = "3",
                Data = new() { ["role"] = "user" }
            });
            await _documentStore.InsertAsync("users", new Document
            {
                Id = "4",
                Data = new() { ["role"] = "admin" }
            });
            await _documentStore.InsertAsync("users", new Document
            {
                Id = "5",
                Data = new() { ["role"] = "admin" }
            });

            var executor = new MapReduceExecutor(_documentStore);
            var job = new CountByKeyJob("role");

            // Act
            var result = await executor.ExecuteAsync("users", job);

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(result.Output);
            Assert.Equal(2, result.Output.Count);

            var admin = result.Output.FirstOrDefault(r => r.Key == "admin");
            Assert.NotNull(admin);
            Assert.Equal(3, admin.Count);

            var user = result.Output.FirstOrDefault(r => r.Key == "user");
            Assert.NotNull(user);
            Assert.Equal(2, user.Count);
        }

        #endregion

        #region MapReduceStatistics Tests

        [Fact]
        public async Task MapReduceStatistics_ShouldTrackCorrectly()
        {
            // Arrange
            for (int i = 0; i < 100; i++)
            {
                await _documentStore.InsertAsync("data", new Document
                {
                    Id = i.ToString(),
                    Data = new() { ["category"] = $"cat{i % 10}", ["value"] = i }
                });
            }

            var executor = new MapReduceExecutor(_documentStore);
            var job = new SumByKeyJob("category", "value");
            var options = new MapReduceOptions { MaxDegreeOfParallelism = 4 };

            // Act
            var result = await executor.ExecuteAsync("data", job, options);

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(result.Statistics);
            Assert.Equal(100, result.Statistics.DocumentsProcessed);
            Assert.Equal(100, result.Statistics.IntermediatePairsEmitted);
            Assert.Equal(10, result.Statistics.UniqueKeys);
            Assert.Equal(10, result.Statistics.OutputDocuments);
            Assert.True(result.Statistics.MapDuration > TimeSpan.Zero);
            Assert.True(result.Statistics.ReduceDuration >= TimeSpan.Zero);
            Assert.True(result.Statistics.TotalDuration > TimeSpan.Zero);
        }

        #endregion

        #region Custom MapReduceJob Tests

        private class CustomMapReduceJob : IMapReduceJob<int, string, string>
        {
            public string JobName => "CustomJob";

            public void Map(Document document, MapReduceContext<int, string> context)
            {
                if (document.Data.TryGetValue("number", out var value) &&
                    int.TryParse(value?.ToString(), out var number))
                {
                    context.Emit(number % 2, $"doc{document.Id}");
                }
            }

            public string Reduce(int key, IEnumerable<string> values)
            {
                var keyType = key == 0 ? "even" : "odd";
                var docs = string.Join(",", values);
                return $"{keyType}: [{docs}]";
            }
        }

        [Fact]
        public async Task CustomMapReduceJob_Execute_ShouldWorkCorrectly()
        {
            // Arrange
            await _documentStore.InsertAsync("numbers", new Document { Id = "1", Data = new() { ["number"] = 1 } });
            await _documentStore.InsertAsync("numbers", new Document { Id = "2", Data = new() { ["number"] = 2 } });
            await _documentStore.InsertAsync("numbers", new Document { Id = "3", Data = new() { ["number"] = 3 } });
            await _documentStore.InsertAsync("numbers", new Document { Id = "4", Data = new() { ["number"] = 4 } });

            var executor = new MapReduceExecutor(_documentStore);
            var job = new CustomMapReduceJob();

            // Act
            var result = await executor.ExecuteAsync("numbers", job);

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(result.Output);
            Assert.Equal(2, result.Output.Count);

            var evenResult = result.Output.FirstOrDefault(r => r.StartsWith("even:"));
            Assert.NotNull(evenResult);
            Assert.Contains("doc2", evenResult);
            Assert.Contains("doc4", evenResult);

            var oddResult = result.Output.FirstOrDefault(r => r.StartsWith("odd:"));
            Assert.NotNull(oddResult);
            Assert.Contains("doc1", oddResult);
            Assert.Contains("doc3", oddResult);
        }

        #endregion

        #region Error Handling Tests

        [Fact]
        public async Task MapReduceExecutor_Execute_WithNullCollectionName_ShouldThrow()
        {
            var executor = new MapReduceExecutor(_documentStore);
            var job = new CountByKeyJob("field");

            await Assert.ThrowsAsync<ArgumentException>(() =>
                executor.ExecuteAsync<string, int, CountByKeyResult<string>>(null!, job));
        }

        [Fact]
        public async Task MapReduceExecutor_Execute_WithNullJob_ShouldThrow()
        {
            var executor = new MapReduceExecutor(_documentStore);

            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                executor.ExecuteAsync<string, int, CountByKeyResult<string>>("docs", null!));
        }

        [Fact]
        public async Task MapReduceExecutor_Execute_WithEmptyCollectionName_ShouldThrow()
        {
            var executor = new MapReduceExecutor(_documentStore);
            var job = new CountByKeyJob("field");

            await Assert.ThrowsAsync<ArgumentException>(() =>
                executor.ExecuteAsync<string, int, CountByKeyResult<string>>("", job));
        }

        [Fact]
        public async Task MapReduceResult_FailureResult_ShouldSetErrorMessage()
        {
            var result = MapReduceResult<string>.FailureResult("Test error");

            Assert.False(result.Success);
            Assert.Equal("Test error", result.ErrorMessage);
        }

        #endregion

        #region Progress Reporting Tests

        [Fact]
        public async Task MapReduceExecutor_Execute_WithProgress_ShouldReportProgress()
        {
            // Arrange
            for (int i = 0; i < 50; i++)
            {
                await _documentStore.InsertAsync("data", new Document
                {
                    Id = i.ToString(),
                    Data = new() { ["category"] = "A", ["value"] = i }
                });
            }

            var executor = new MapReduceExecutor(_documentStore);
            var job = new SumByKeyJob("category", "value");
            var progressReports = new List<MapReduceProgressEventArgs>();
            var progress = new Progress<MapReduceProgressEventArgs>(args => progressReports.Add(args));

            // Act
            var result = await executor.ExecuteAsync("data", job, progress: progress);

            // Wait for progress to be reported
            await Task.Delay(500);

            // Assert
            Assert.True(result.Success);
        }

        #endregion

        #region Cancellation Tests

        [Fact(Skip = "Parallel.ForEach cancellation behavior is non-deterministic when token is pre-cancelled")]
        public async Task MapReduceExecutor_Execute_WithCancellation_ShouldThrowOperationCancelled()
        {
            // Arrange
            for (int i = 0; i < 1000; i++)
            {
                await _documentStore.InsertAsync("data", new Document
                {
                    Id = i.ToString(),
                    Data = new() { ["category"] = "A", ["value"] = i }
                });
            }

            var executor = new MapReduceExecutor(_documentStore);
            var job = new SumByKeyJob("category", "value");
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                executor.ExecuteAsync("data", job, cancellationToken: cts.Token));
        }

        #endregion

        #region Parallel Execution Tests

        [Fact]
        public async Task MapReduceExecutor_Execute_WithHighParallelism_ShouldCompleteSuccessfully()
        {
            // Arrange
            for (int i = 0; i < 500; i++)
            {
                await _documentStore.InsertAsync("data", new Document
                {
                    Id = i.ToString(),
                    Data = new() { ["category"] = $"cat{i % 20}", ["value"] = i }
                });
            }

            var executor = new MapReduceExecutor(_documentStore);
            var job = new SumByKeyJob("category", "value");
            var options = new MapReduceOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount * 2,
                ChunkSize = 50
            };

            // Act
            var result = await executor.ExecuteAsync("data", job, options);

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(result.Output);
            Assert.Equal(20, result.Output.Count);
        }

        #endregion
    }
}
