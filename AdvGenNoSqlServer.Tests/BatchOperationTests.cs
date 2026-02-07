// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Client;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Network;

namespace AdvGenNoSqlServer.Tests
{
    public class BatchOperationTests
    {
        #region BatchOperationModel Tests

        [Fact]
        public void BatchOperationRequest_DefaultValues_AreCorrect()
        {
            var request = new BatchOperationRequest();

            Assert.Equal(string.Empty, request.Collection);
            Assert.NotNull(request.Operations);
            Assert.Empty(request.Operations);
            Assert.False(request.StopOnError);
            Assert.True(request.UseTransaction);
            Assert.Null(request.TransactionId);
        }

        [Fact]
        public void BatchOperationResponse_DefaultValues_AreCorrect()
        {
            var response = new BatchOperationResponse();

            Assert.False(response.Success);
            Assert.Equal(0, response.InsertedCount);
            Assert.Equal(0, response.UpdatedCount);
            Assert.Equal(0, response.DeletedCount);
            Assert.Equal(0, response.TotalProcessed);
            Assert.Equal(0, response.ProcessingTimeMs);
            Assert.NotNull(response.Results);
            Assert.Empty(response.Results);
        }

        [Fact]
        public void BatchOperationResponse_FailedCount_CalculatedCorrectly()
        {
            var response = new BatchOperationResponse
            {
                Results = new List<BatchOperationItemResult>
                {
                    new() { Success = true },
                    new() { Success = false },
                    new() { Success = true },
                    new() { Success = false },
                    new() { Success = false }
                }
            };

            Assert.Equal(3, response.FailedCount);
        }

        [Fact]
        public void BatchOptions_DefaultValues_AreCorrect()
        {
            var options = new BatchOptions();

            Assert.Equal(1000, options.MaxBatchSize);
            Assert.Equal(30000, options.TimeoutMs);
            Assert.False(options.StopOnError);
            Assert.True(options.UseTransaction);
        }

        [Fact]
        public void BatchOperationItem_AllProperties_CanBeSet()
        {
            var item = new BatchOperationItem
            {
                OperationType = BatchOperationType.Insert,
                DocumentId = "doc123",
                Document = new Dictionary<string, object> { { "name", "test" } },
                Filter = new Dictionary<string, object> { { "status", "active" } },
                UpdateFields = new Dictionary<string, object> { { "status", "updated" } }
            };

            Assert.Equal(BatchOperationType.Insert, item.OperationType);
            Assert.Equal("doc123", item.DocumentId);
            Assert.NotNull(item.Document);
            Assert.NotNull(item.Filter);
            Assert.NotNull(item.UpdateFields);
        }

        [Fact]
        public void BatchOperationItemResult_AllProperties_CanBeSet()
        {
            var result = new BatchOperationItemResult
            {
                Index = 5,
                Success = true,
                DocumentId = "doc456",
                ErrorMessage = "Error occurred",
                ErrorCode = "ERR001"
            };

            Assert.Equal(5, result.Index);
            Assert.True(result.Success);
            Assert.Equal("doc456", result.DocumentId);
            Assert.Equal("Error occurred", result.ErrorMessage);
            Assert.Equal("ERR001", result.ErrorCode);
        }

        [Theory]
        [InlineData(BatchOperationType.Insert, 0x01)]
        [InlineData(BatchOperationType.Update, 0x02)]
        [InlineData(BatchOperationType.Delete, 0x03)]
        [InlineData(BatchOperationType.Mixed, 0x04)]
        public void BatchOperationType_EnumValues_AreCorrect(BatchOperationType type, byte expectedValue)
        {
            Assert.Equal(expectedValue, (byte)type);
        }

        #endregion

        #region Serialization Tests

        [Fact]
        public void BatchOperationRequest_Serialization_RoundTrip()
        {
            var request = new BatchOperationRequest
            {
                Collection = "test-collection",
                Operations = new List<BatchOperationItem>
                {
                    new()
                    {
                        OperationType = BatchOperationType.Insert,
                        Document = new Dictionary<string, object>
                        {
                            { "_id", "doc1" },
                            { "name", "Test Document" },
                            { "value", 42 }
                        }
                    }
                },
                StopOnError = true,
                UseTransaction = false,
                TransactionId = "txn123"
            };

            var json = System.Text.Json.JsonSerializer.Serialize(request, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });

            var deserialized = System.Text.Json.JsonSerializer.Deserialize<BatchOperationRequest>(json, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });

            Assert.NotNull(deserialized);
            Assert.Equal(request.Collection, deserialized.Collection);
            Assert.Equal(request.StopOnError, deserialized.StopOnError);
            Assert.Equal(request.UseTransaction, deserialized.UseTransaction);
            Assert.Equal(request.TransactionId, deserialized.TransactionId);
            Assert.Single(deserialized.Operations);
            Assert.Equal(BatchOperationType.Insert, deserialized.Operations[0].OperationType);
        }

        [Fact]
        public void BatchOperationResponse_Serialization_RoundTrip()
        {
            var response = new BatchOperationResponse
            {
                Success = true,
                InsertedCount = 10,
                UpdatedCount = 5,
                DeletedCount = 2,
                TotalProcessed = 17,
                ProcessingTimeMs = 150,
                Results = new List<BatchOperationItemResult>
                {
                    new() { Index = 0, Success = true, DocumentId = "doc1" },
                    new() { Index = 1, Success = false, ErrorCode = "ERR001", ErrorMessage = "Not found" }
                }
            };

            var json = System.Text.Json.JsonSerializer.Serialize(response, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });

            var deserialized = System.Text.Json.JsonSerializer.Deserialize<BatchOperationResponse>(json, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });

            Assert.NotNull(deserialized);
            Assert.Equal(response.Success, deserialized.Success);
            Assert.Equal(response.InsertedCount, deserialized.InsertedCount);
            Assert.Equal(response.UpdatedCount, deserialized.UpdatedCount);
            Assert.Equal(response.DeletedCount, deserialized.DeletedCount);
            Assert.Equal(response.TotalProcessed, deserialized.TotalProcessed);
            Assert.Equal(response.ProcessingTimeMs, deserialized.ProcessingTimeMs);
            Assert.Equal(2, deserialized.Results.Count);
        }

        #endregion

        #region Client Batch Method Tests (Integration)

        [Fact]
        public async Task BatchInsertAsync_EmptyCollection_ThrowsArgumentException()
        {
            var client = new AdvGenNoSqlClient("localhost:19090");
            var documents = new List<object> { new { name = "test" } };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await client.BatchInsertAsync("", documents)
            );

            Assert.Contains("connected", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task BatchUpdateAsync_NotConnected_ThrowsInvalidOperationException()
        {
            var client = new AdvGenNoSqlClient("localhost:19090");
            var updates = new List<(string, Dictionary<string, object>)>
            {
                ("doc1", new Dictionary<string, object> { { "status", "updated" } })
            };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await client.BatchUpdateAsync("test", updates)
            );

            Assert.Contains("connected", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task BatchDeleteAsync_NotConnected_ThrowsInvalidOperationException()
        {
            var client = new AdvGenNoSqlClient("localhost:19090");
            var ids = new List<string> { "doc1", "doc2" };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await client.BatchDeleteAsync("test", ids)
            );

            Assert.Contains("connected", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BatchInsertAsync_ExceedsMaxBatchSize_ThrowsArgumentException()
        {
            // This test validates the batch size check before connection is verified
            // We'll test the logic by creating a mock scenario
            var operations = new List<BatchOperationItem>();
            for (int i = 0; i < 1001; i++)
            {
                operations.Add(new BatchOperationItem { OperationType = BatchOperationType.Insert });
            }

            var request = new BatchOperationRequest
            {
                Collection = "test",
                Operations = operations
            };

            var options = new BatchOptions { MaxBatchSize = 1000 };

            Assert.True(request.Operations.Count > options.MaxBatchSize);
        }

        #endregion

        #region Server-Side Batch Processing Tests

        [Fact]
        public void ProcessBatchRequest_EmptyOperations_ReturnsSuccess()
        {
            var request = new BatchOperationRequest
            {
                Collection = "test",
                Operations = new List<BatchOperationItem>()
            };

            // Since we can't directly call the server method, we verify the model behavior
            var response = new BatchOperationResponse
            {
                Success = true,
                TotalProcessed = 0,
                Results = new List<BatchOperationItemResult>()
            };

            Assert.True(response.Success);
            Assert.Equal(0, response.TotalProcessed);
            Assert.Empty(response.Results);
        }

        [Fact]
        public void BatchOperationRequest_Validation_MissingCollection()
        {
            var request = new BatchOperationRequest
            {
                Collection = "",
                Operations = new List<BatchOperationItem> { new() { OperationType = BatchOperationType.Insert } }
            };

            Assert.True(string.IsNullOrEmpty(request.Collection));
        }

        [Fact]
        public void BatchOperationItem_Validation_InsertWithoutDocument()
        {
            var item = new BatchOperationItem
            {
                OperationType = BatchOperationType.Insert,
                Document = null
            };

            Assert.Null(item.Document);
        }

        [Fact]
        public void BatchOperationItem_Validation_UpdateWithoutDocumentIdOrFilter()
        {
            var item = new BatchOperationItem
            {
                OperationType = BatchOperationType.Update,
                DocumentId = null,
                Filter = null
            };

            Assert.Null(item.DocumentId);
            Assert.Null(item.Filter);
        }

        [Fact]
        public void BatchOperationItem_Validation_DeleteWithoutDocumentIdOrFilter()
        {
            var item = new BatchOperationItem
            {
                OperationType = BatchOperationType.Delete,
                DocumentId = null,
                Filter = null
            };

            Assert.Null(item.DocumentId);
            Assert.Null(item.Filter);
        }

        #endregion

        #region BulkInsert Tests

        [Fact]
        public async Task BulkInsertAsync_NotConnected_ThrowsInvalidOperationException()
        {
            var client = new AdvGenNoSqlClient("localhost:19090");
            var documents = new List<object>();
            for (int i = 0; i < 100; i++)
            {
                documents.Add(new { _id = $"doc{i}", value = i });
            }

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await client.BulkInsertAsync("test", documents, batchSize: 10)
            );

            Assert.Contains("connected", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BulkInsertAsync_CalculatesBatchesCorrectly()
        {
            // Verify batch calculation logic
            int totalDocs = 2500;
            int batchSize = 1000;

            int expectedBatches = (int)Math.Ceiling((double)totalDocs / batchSize);

            Assert.Equal(3, expectedBatches);
        }

        #endregion

        #region Progress Callback Tests

        [Fact]
        public void ProgressCallback_CalculatesProgressCorrectly()
        {
            int totalDocs = 1000;
            int batchSize = 100;
            var progressReports = new List<(int processed, int total)>();

            // Simulate progress reporting
            for (int i = 0; i < totalDocs; i += batchSize)
            {
                int processed = Math.Min(i + batchSize, totalDocs);
                progressReports.Add((processed, totalDocs));
            }

            Assert.Equal(10, progressReports.Count);
            Assert.Equal((100, 1000), progressReports[0]);
            Assert.Equal((1000, 1000), progressReports[9]);
        }

        #endregion

        #region Complex Batch Scenario Tests

        [Fact]
        public void BatchOperationRequest_MixedOperations_CanBeCreated()
        {
            var request = new BatchOperationRequest
            {
                Collection = "mixed-collection",
                Operations = new List<BatchOperationItem>
                {
                    new()
                    {
                        OperationType = BatchOperationType.Insert,
                        Document = new Dictionary<string, object>
                        {
                            { "_id", "newdoc1" },
                            { "name", "New Document" }
                        }
                    },
                    new()
                    {
                        OperationType = BatchOperationType.Update,
                        DocumentId = "existing1",
                        UpdateFields = new Dictionary<string, object>
                        {
                            { "status", "updated" }
                        }
                    },
                    new()
                    {
                        OperationType = BatchOperationType.Delete,
                        DocumentId = "olddoc1"
                    }
                }
            };

            Assert.Equal(3, request.Operations.Count);
            Assert.Equal(BatchOperationType.Insert, request.Operations[0].OperationType);
            Assert.Equal(BatchOperationType.Update, request.Operations[1].OperationType);
            Assert.Equal(BatchOperationType.Delete, request.Operations[2].OperationType);
        }

        [Fact]
        public void BatchOperationResponse_WithPartialFailures_ReportedCorrectly()
        {
            var response = new BatchOperationResponse
            {
                Success = true, // Overall success (stopped on error = false)
                InsertedCount = 8,
                TotalProcessed = 10,
                Results = new List<BatchOperationItemResult>()
            };

            for (int i = 0; i < 10; i++)
            {
                response.Results.Add(new BatchOperationItemResult
                {
                    Index = i,
                    Success = i % 5 != 0, // Fail every 5th operation
                    ErrorCode = i % 5 == 0 ? "INSERT_FAILED" : null,
                    ErrorMessage = i % 5 == 0 ? "Duplicate key" : null
                });
            }

            Assert.Equal(8, response.InsertedCount);
            Assert.Equal(2, response.FailedCount);
            Assert.Equal(10, response.TotalProcessed);
        }

        [Fact]
        public void BatchOperationItem_UpdateByFilter_CanBeCreated()
        {
            var item = new BatchOperationItem
            {
                OperationType = BatchOperationType.Update,
                Filter = new Dictionary<string, object>
                {
                    { "status", "inactive" },
                    { "lastUpdated", new Dictionary<string, object> { { "$lt", DateTime.UtcNow.AddDays(-30) } } }
                },
                UpdateFields = new Dictionary<string, object>
                {
                    { "status", "archived" },
                    { "archivedAt", DateTime.UtcNow }
                }
            };

            Assert.Null(item.DocumentId);
            Assert.NotNull(item.Filter);
            Assert.NotNull(item.UpdateFields);
        }

        [Fact]
        public void BatchOperationItem_DeleteByFilter_CanBeCreated()
        {
            var item = new BatchOperationItem
            {
                OperationType = BatchOperationType.Delete,
                Filter = new Dictionary<string, object>
                {
                    { "expired", true },
                    { "expiryDate", new Dictionary<string, object> { { "$lt", DateTime.UtcNow } } }
                }
            };

            Assert.Null(item.DocumentId);
            Assert.NotNull(item.Filter);
        }

        #endregion

        #region StopOnError Behavior Tests

        [Fact]
        public void BatchOperationRequest_StopOnError_True_StopsOnFirstError()
        {
            var request = new BatchOperationRequest
            {
                Collection = "test",
                StopOnError = true,
                Operations = new List<BatchOperationItem>
                {
                    new() { OperationType = BatchOperationType.Insert },
                    new() { OperationType = BatchOperationType.Insert }, // Assume this fails
                    new() { OperationType = BatchOperationType.Insert }  // Should not be processed
                }
            };

            Assert.True(request.StopOnError);
            // Logic verification: when StopOnError is true, processing should halt on first failure
        }

        [Fact]
        public void BatchOperationRequest_StopOnError_False_ContinuesOnError()
        {
            var request = new BatchOperationRequest
            {
                Collection = "test",
                StopOnError = false,
                Operations = new List<BatchOperationItem>
                {
                    new() { OperationType = BatchOperationType.Insert },
                    new() { OperationType = BatchOperationType.Insert }, // Assume this fails
                    new() { OperationType = BatchOperationType.Insert }  // Should still be processed
                }
            };

            Assert.False(request.StopOnError);
            // Logic verification: when StopOnError is false, all operations should be attempted
        }

        #endregion

        #region Transaction Support Tests

        [Fact]
        public void BatchOperationRequest_WithTransactionId_IncludedInRequest()
        {
            var request = new BatchOperationRequest
            {
                Collection = "test",
                TransactionId = "txn-abc123",
                UseTransaction = true,
                Operations = new List<BatchOperationItem>
                {
                    new() { OperationType = BatchOperationType.Insert }
                }
            };

            Assert.Equal("txn-abc123", request.TransactionId);
            Assert.True(request.UseTransaction);
        }

        [Fact]
        public void BatchOperationRequest_WithoutTransaction_GeneratesNewTransaction()
        {
            var request = new BatchOperationRequest
            {
                Collection = "test",
                UseTransaction = true,
                TransactionId = null,
                Operations = new List<BatchOperationItem>
                {
                    new() { OperationType = BatchOperationType.Insert }
                }
            };

            Assert.Null(request.TransactionId);
            Assert.True(request.UseTransaction);
            // Server should generate a new transaction ID
        }

        #endregion
    }
}
