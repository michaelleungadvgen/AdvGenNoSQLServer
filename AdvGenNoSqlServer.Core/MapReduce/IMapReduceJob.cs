// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Core.MapReduce
{
    /// <summary>
    /// Delegate for emitting intermediate key-value pairs during the map phase.
    /// </summary>
    /// <typeparam name="TIntermediateKey">The type of the intermediate key.</typeparam>
    /// <typeparam name="TIntermediateValue">The type of the intermediate value.</typeparam>
    /// <param name="key">The intermediate key.</param>
    /// <param name="value">The intermediate value.</param>
    public delegate void EmitFunction<TIntermediateKey, TIntermediateValue>(TIntermediateKey key, TIntermediateValue value);

    /// <summary>
    /// Context provided to map and reduce operations.
    /// </summary>
    /// <typeparam name="TIntermediateKey">The type of the intermediate key.</typeparam>
    /// <typeparam name="TIntermediateValue">The type of the intermediate value.</typeparam>
    public class MapReduceContext<TIntermediateKey, TIntermediateValue>
    {
        private readonly EmitFunction<TIntermediateKey, TIntermediateValue> _emit;

        /// <summary>
        /// Gets the collection name being processed.
        /// </summary>
        public string CollectionName { get; }

        /// <summary>
        /// Gets the total number of documents to process.
        /// </summary>
        public long TotalDocuments { get; }

        /// <summary>
        /// Gets or sets the current document index being processed.
        /// </summary>
        public long CurrentDocumentIndex { get; set; }

        /// <summary>
        /// Gets the job configuration options.
        /// </summary>
        public MapReduceOptions Options { get; }

        /// <summary>
        /// Gets the cancellation token for the operation.
        /// </summary>
        public CancellationToken CancellationToken { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="MapReduceContext{TIntermediateKey, TIntermediateValue}"/> class.
        /// </summary>
        /// <param name="collectionName">The collection name.</param>
        /// <param name="totalDocuments">The total number of documents.</param>
        /// <param name="emit">The emit function for intermediate results.</param>
        /// <param name="options">The job options.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public MapReduceContext(
            string collectionName,
            long totalDocuments,
            EmitFunction<TIntermediateKey, TIntermediateValue> emit,
            MapReduceOptions options,
            CancellationToken cancellationToken)
        {
            CollectionName = collectionName ?? throw new ArgumentNullException(nameof(collectionName));
            TotalDocuments = totalDocuments;
            _emit = emit ?? throw new ArgumentNullException(nameof(emit));
            Options = options ?? throw new ArgumentNullException(nameof(options));
            CancellationToken = cancellationToken;
        }

        /// <summary>
        /// Emits an intermediate key-value pair.
        /// </summary>
        /// <param name="key">The intermediate key.</param>
        /// <param name="value">The intermediate value.</param>
        public void Emit(TIntermediateKey key, TIntermediateValue value)
        {
            _emit(key, value);
        }
    }

    /// <summary>
    /// Configuration options for map-reduce job execution.
    /// </summary>
    public class MapReduceOptions
    {
        /// <summary>
        /// Gets or sets the maximum degree of parallelism for the map phase.
        /// </summary>
        public int MaxDegreeOfParallelism { get; set; } = Environment.ProcessorCount;

        /// <summary>
        /// Gets or sets the chunk size for parallel processing.
        /// </summary>
        public int ChunkSize { get; set; } = 1000;

        /// <summary>
        /// Gets or sets a value indicating whether to spill intermediate results to disk.
        /// </summary>
        public bool EnableSpilling { get; set; } = false;

        /// <summary>
        /// Gets or sets the memory threshold (in bytes) before spilling intermediate results.
        /// </summary>
        public long SpillThresholdBytes { get; set; } = 100 * 1024 * 1024; // 100MB

        /// <summary>
        /// Gets or sets a value indicating whether to sort intermediate keys.
        /// </summary>
        public bool SortIntermediateKeys { get; set; } = true;

        /// <summary>
        /// Gets or sets the maximum number of intermediate results to keep in memory.
        /// </summary>
        public int MaxInMemoryResults { get; set; } = 100000;

        /// <summary>
        /// Gets or sets a value indicating whether progress should be reported.
        /// </summary>
        public bool ReportProgress { get; set; } = true;

        /// <summary>
        /// Gets or sets the progress reporting interval (in milliseconds).
        /// </summary>
        public int ProgressIntervalMs { get; set; } = 1000;

        /// <summary>
        /// Validates the options.
        /// </summary>
        public void Validate()
        {
            if (MaxDegreeOfParallelism < 1)
                throw new ArgumentException("MaxDegreeOfParallelism must be at least 1", nameof(MaxDegreeOfParallelism));

            if (ChunkSize < 1)
                throw new ArgumentException("ChunkSize must be at least 1", nameof(ChunkSize));

            if (SpillThresholdBytes < 1024 * 1024)
                throw new ArgumentException("SpillThresholdBytes must be at least 1MB", nameof(SpillThresholdBytes));

            if (MaxInMemoryResults < 100)
                throw new ArgumentException("MaxInMemoryResults must be at least 100", nameof(MaxInMemoryResults));

            if (ProgressIntervalMs < 100)
                throw new ArgumentException("ProgressIntervalMs must be at least 100", nameof(ProgressIntervalMs));
        }
    }

    /// <summary>
    /// Statistics for map-reduce job execution.
    /// </summary>
    public class MapReduceStatistics
    {
        /// <summary>
        /// Gets or sets the number of documents processed in the map phase.
        /// </summary>
        public long DocumentsProcessed { get; set; }

        /// <summary>
        /// Gets or sets the number of intermediate key-value pairs emitted.
        /// </summary>
        public long IntermediatePairsEmitted { get; set; }

        /// <summary>
        /// Gets or sets the number of unique intermediate keys.
        /// </summary>
        public long UniqueKeys { get; set; }

        /// <summary>
        /// Gets or sets the number of output documents produced.
        /// </summary>
        public long OutputDocuments { get; set; }

        /// <summary>
        /// Gets or sets the map phase duration.
        /// </summary>
        public TimeSpan MapDuration { get; set; }

        /// <summary>
        /// Gets or sets the shuffle/sort phase duration.
        /// </summary>
        public TimeSpan ShuffleDuration { get; set; }

        /// <summary>
        /// Gets or sets the reduce phase duration.
        /// </summary>
        public TimeSpan ReduceDuration { get; set; }

        /// <summary>
        /// Gets the total execution duration.
        /// </summary>
        public TimeSpan TotalDuration => MapDuration + ShuffleDuration + ReduceDuration;

        /// <summary>
        /// Gets or sets the memory used during execution (in bytes).
        /// </summary>
        public long MemoryUsedBytes { get; set; }

        /// <summary>
        /// Gets or sets the number of chunks processed in parallel.
        /// </summary>
        public int ChunksProcessed { get; set; }
    }

    /// <summary>
    /// Result of a map-reduce job execution.
    /// </summary>
    /// <typeparam name="TResult">The type of the result documents.</typeparam>
    public class MapReduceResult<TResult>
    {
        /// <summary>
        /// Gets or sets a value indicating whether the job completed successfully.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Gets or sets the error message if the job failed.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Gets or sets the output documents.
        /// </summary>
        public IReadOnlyList<TResult> Output { get; set; } = Array.Empty<TResult>();

        /// <summary>
        /// Gets or sets the execution statistics.
        /// </summary>
        public MapReduceStatistics Statistics { get; set; } = new MapReduceStatistics();

        /// <summary>
        /// Creates a successful result.
        /// </summary>
        /// <param name="output">The output documents.</param>
        /// <param name="statistics">The execution statistics.</param>
        /// <returns>A successful map-reduce result.</returns>
        public static MapReduceResult<TResult> SuccessResult(IReadOnlyList<TResult> output, MapReduceStatistics statistics)
        {
            return new MapReduceResult<TResult>
            {
                Success = true,
                Output = output,
                Statistics = statistics
            };
        }

        /// <summary>
        /// Creates a failed result.
        /// </summary>
        /// <param name="errorMessage">The error message.</param>
        /// <returns>A failed map-reduce result.</returns>
        public static MapReduceResult<TResult> FailureResult(string errorMessage)
        {
            return new MapReduceResult<TResult>
            {
                Success = false,
                ErrorMessage = errorMessage
            };
        }
    }

    /// <summary>
    /// Interface for map-reduce jobs.
    /// </summary>
    /// <typeparam name="TIntermediateKey">The type of the intermediate key.</typeparam>
    /// <typeparam name="TIntermediateValue">The type of the intermediate value.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    public interface IMapReduceJob<TIntermediateKey, TIntermediateValue, TResult>
    {
        /// <summary>
        /// Gets the job name.
        /// </summary>
        string JobName { get; }

        /// <summary>
        /// Maps a document to intermediate key-value pairs.
        /// </summary>
        /// <param name="document">The document to map.</param>
        /// <param name="context">The execution context.</param>
        void Map(Document document, MapReduceContext<TIntermediateKey, TIntermediateValue> context);

        /// <summary>
        /// Reduces intermediate values for a key to a final result.
        /// </summary>
        /// <param name="key">The intermediate key.</param>
        /// <param name="values">The values for this key.</param>
        /// <returns>The final reduced result.</returns>
        TResult Reduce(TIntermediateKey key, IEnumerable<TIntermediateValue> values);
    }

    /// <summary>
    /// Event args for map-reduce progress updates.
    /// </summary>
    public class MapReduceProgressEventArgs : EventArgs
    {
        /// <summary>
        /// Gets or sets the job name.
        /// </summary>
        public string JobName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the current phase (Map, Shuffle, Reduce).
        /// </summary>
        public string Phase { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the progress percentage (0-100).
        /// </summary>
        public double ProgressPercent { get; set; }

        /// <summary>
        /// Gets or sets the number of items processed.
        /// </summary>
        public long ItemsProcessed { get; set; }

        /// <summary>
        /// Gets or sets the total number of items to process.
        /// </summary>
        public long TotalItems { get; set; }

        /// <summary>
        /// Gets or sets the elapsed time.
        /// </summary>
        public TimeSpan Elapsed { get; set; }

        /// <summary>
        /// Gets or sets the estimated remaining time.
        /// </summary>
        public TimeSpan? EstimatedRemaining { get; set; }
    }

    /// <summary>
    /// Delegate for progress event handling.
    /// </summary>
    public delegate void MapReduceProgressEventHandler(object sender, MapReduceProgressEventArgs e);
}
