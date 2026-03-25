// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Core.Abstractions;
using AdvGenNoSqlServer.Core.MapReduce;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Storage.MapReduce
{
    /// <summary>
    /// Executor for map-reduce jobs.
    /// </summary>
    public class MapReduceExecutor
    {
        private readonly IDocumentStore _documentStore;

        /// <summary>
        /// Event raised when progress is updated.
        /// </summary>
        public event MapReduceProgressEventHandler? ProgressUpdated;

        /// <summary>
        /// Initializes a new instance of the <see cref="MapReduceExecutor"/> class.
        /// </summary>
        /// <param name="documentStore">The document store to read from.</param>
        public MapReduceExecutor(IDocumentStore documentStore)
        {
            _documentStore = documentStore ?? throw new ArgumentNullException(nameof(documentStore));
        }

        /// <summary>
        /// Executes a map-reduce job on a collection.
        /// </summary>
        /// <typeparam name="TIntermediateKey">The type of the intermediate key.</typeparam>
        /// <typeparam name="TIntermediateValue">The type of the intermediate value.</typeparam>
        /// <typeparam name="TResult">The type of the result.</typeparam>
        /// <param name="collectionName">The collection to process.</param>
        /// <param name="job">The map-reduce job.</param>
        /// <param name="options">The execution options.</param>
        /// <param name="progress">Optional progress reporter.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The map-reduce result.</returns>
        public async Task<MapReduceResult<TResult>> ExecuteAsync<TIntermediateKey, TIntermediateValue, TResult>(
            string collectionName,
            IMapReduceJob<TIntermediateKey, TIntermediateValue, TResult> job,
            MapReduceOptions? options = null,
            IProgress<MapReduceProgressEventArgs>? progress = null,
            CancellationToken cancellationToken = default)
            where TIntermediateKey : notnull
        {
            if (string.IsNullOrWhiteSpace(collectionName))
                throw new ArgumentException("Collection name cannot be empty", nameof(collectionName));
            if (job == null)
                throw new ArgumentNullException(nameof(job));

            options ??= new MapReduceOptions();
            options.Validate();

            var stopwatch = Stopwatch.StartNew();
            var statistics = new MapReduceStatistics();

            try
            {
                // Get all documents from the collection
                var documents = await _documentStore.GetAllAsync(collectionName, cancellationToken);
                var documentList = documents.ToList();
                var totalDocuments = documentList.Count;

                if (totalDocuments == 0)
                {
                    return MapReduceResult<TResult>.SuccessResult(Array.Empty<TResult>(), statistics);
                }

                statistics.DocumentsProcessed = totalDocuments;

                // Phase 1: Map
                var mapStopwatch = Stopwatch.StartNew();
                var intermediateResults = await ExecuteMapPhaseAsync(
                    documentList, job, options, collectionName, totalDocuments, progress, cancellationToken);
                mapStopwatch.Stop();
                statistics.MapDuration = mapStopwatch.Elapsed;
                statistics.IntermediatePairsEmitted = intermediateResults.Count;

                if (intermediateResults.Count == 0)
                {
                    return MapReduceResult<TResult>.SuccessResult(Array.Empty<TResult>(), statistics);
                }

                // Phase 2: Shuffle/Sort
                var shuffleStopwatch = Stopwatch.StartNew();
                var groupedResults = GroupIntermediateResults(intermediateResults, options);
                shuffleStopwatch.Stop();
                statistics.ShuffleDuration = shuffleStopwatch.Elapsed;
                statistics.UniqueKeys = groupedResults.Count;

                // Phase 3: Reduce
                var reduceStopwatch = Stopwatch.StartNew();
                var output = await ExecuteReducePhaseAsync(
                    groupedResults, job, options, collectionName, totalDocuments, progress, cancellationToken);
                reduceStopwatch.Stop();
                statistics.ReduceDuration = reduceStopwatch.Elapsed;
                statistics.OutputDocuments = output.Count;

                stopwatch.Stop();

                return MapReduceResult<TResult>.SuccessResult(output, statistics);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return MapReduceResult<TResult>.FailureResult(ex.Message);
            }
        }

        private async Task<ConcurrentBag<(TIntermediateKey Key, TIntermediateValue Value)>> ExecuteMapPhaseAsync<TIntermediateKey, TIntermediateValue, TResult>(
            List<Document> documents,
            IMapReduceJob<TIntermediateKey, TIntermediateValue, TResult> job,
            MapReduceOptions options,
            string collectionName,
            long totalDocuments,
            IProgress<MapReduceProgressEventArgs>? progress,
            CancellationToken cancellationToken)
            where TIntermediateKey : notnull
        {
            var intermediateResults = new ConcurrentBag<(TIntermediateKey Key, TIntermediateValue Value)>();
            var processedCount = 0L;
            var lockObj = new object();
            var lastProgressReport = DateTime.UtcNow;

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
                CancellationToken = cancellationToken
            };

            await Task.Run(() =>
            {
                Parallel.ForEach(documents, parallelOptions, (document, _, index) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    void Emit(TIntermediateKey key, TIntermediateValue value)
                    {
                        intermediateResults.Add((key, value));
                    }

                    var context = new MapReduceContext<TIntermediateKey, TIntermediateValue>(
                        collectionName,
                        totalDocuments,
                        Emit,
                        options,
                        cancellationToken)
                    {
                        CurrentDocumentIndex = index
                    };

                    job.Map(document, context);

                    var currentProcessed = Interlocked.Increment(ref processedCount);

                    // Report progress
                    if (options.ReportProgress && progress != null)
                    {
                        var now = DateTime.UtcNow;
                        lock (lockObj)
                        {
                            if ((now - lastProgressReport).TotalMilliseconds >= options.ProgressIntervalMs)
                            {
                                lastProgressReport = now;
                                var elapsed = TimeSpan.FromTicks(Stopwatch.GetTimestamp() / TimeSpan.TicksPerMillisecond * 10000);
                                var percentComplete = (double)currentProcessed / totalDocuments * 100;
                                var estimatedRemaining = percentComplete > 0
                                    ? TimeSpan.FromMilliseconds(elapsed.TotalMilliseconds / percentComplete * (100 - percentComplete))
                                    : (TimeSpan?)null;

                                progress.Report(new MapReduceProgressEventArgs
                                {
                                    JobName = job.JobName,
                                    Phase = "Map",
                                    ProgressPercent = percentComplete,
                                    ItemsProcessed = currentProcessed,
                                    TotalItems = totalDocuments,
                                    Elapsed = elapsed,
                                    EstimatedRemaining = estimatedRemaining
                                });
                            }
                        }
                    }
                });
            }, cancellationToken);

            return intermediateResults;
        }

        private Dictionary<TIntermediateKey, List<TIntermediateValue>> GroupIntermediateResults<TIntermediateKey, TIntermediateValue>(
            ConcurrentBag<(TIntermediateKey Key, TIntermediateValue Value)> intermediateResults,
            MapReduceOptions options)
            where TIntermediateKey : notnull
        {
            var grouped = new Dictionary<TIntermediateKey, List<TIntermediateValue>>();

            foreach (var (key, value) in intermediateResults)
            {
                if (!grouped.TryGetValue(key, out var list))
                {
                    list = new List<TIntermediateValue>();
                    grouped[key] = list;
                }
                list.Add(value);
            }

            if (options.SortIntermediateKeys)
            {
                // Sort by key if the key type is comparable
                if (typeof(IComparable<TIntermediateKey>).IsAssignableFrom(typeof(TIntermediateKey)))
                {
                    var sorted = grouped.OrderBy(kvp => kvp.Key).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                    return sorted;
                }
            }

            return grouped;
        }

        private async Task<List<TResult>> ExecuteReducePhaseAsync<TIntermediateKey, TIntermediateValue, TResult>(
            Dictionary<TIntermediateKey, List<TIntermediateValue>> groupedResults,
            IMapReduceJob<TIntermediateKey, TIntermediateValue, TResult> job,
            MapReduceOptions options,
            string collectionName,
            long totalDocuments,
            IProgress<MapReduceProgressEventArgs>? progress,
            CancellationToken cancellationToken)
            where TIntermediateKey : notnull
        {
            var output = new ConcurrentBag<TResult>();
            var processedCount = 0L;
            var lockObj = new object();
            var lastProgressReport = DateTime.UtcNow;
            var totalKeys = groupedResults.Count;

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
                CancellationToken = cancellationToken
            };

            await Task.Run(() =>
            {
                Parallel.ForEach(groupedResults, parallelOptions, kvp =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var result = job.Reduce(kvp.Key, kvp.Value);
                    output.Add(result);

                    var currentProcessed = Interlocked.Increment(ref processedCount);

                    // Report progress
                    if (options.ReportProgress && progress != null)
                    {
                        var now = DateTime.UtcNow;
                        lock (lockObj)
                        {
                            if ((now - lastProgressReport).TotalMilliseconds >= options.ProgressIntervalMs)
                            {
                                lastProgressReport = now;
                                var percentComplete = (double)currentProcessed / totalKeys * 100;

                                progress.Report(new MapReduceProgressEventArgs
                                {
                                    JobName = job.JobName,
                                    Phase = "Reduce",
                                    ProgressPercent = percentComplete,
                                    ItemsProcessed = currentProcessed,
                                    TotalItems = totalKeys
                                });
                            }
                        }
                    }
                });
            }, cancellationToken);

            return output.ToList();
        }
    }
}
