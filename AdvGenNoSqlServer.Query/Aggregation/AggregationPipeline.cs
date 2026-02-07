// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Diagnostics;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Query.Aggregation;

/// <summary>
/// Represents an aggregation pipeline that processes documents through multiple stages
/// </summary>
public class AggregationPipeline
{
    private readonly List<IAggregationStage> _stages;

    /// <summary>
    /// The stages in this pipeline
    /// </summary>
    public IReadOnlyList<IAggregationStage> Stages => _stages.AsReadOnly();

    /// <summary>
    /// Creates a new empty aggregation pipeline
    /// </summary>
    public AggregationPipeline()
    {
        _stages = new List<IAggregationStage>();
    }

    /// <summary>
    /// Creates an aggregation pipeline with the given stages
    /// </summary>
    public AggregationPipeline(IEnumerable<IAggregationStage> stages)
    {
        _stages = new List<IAggregationStage>(stages);
    }

    /// <summary>
    /// Adds a stage to the pipeline
    /// </summary>
    /// <param name="stage">The stage to add</param>
    /// <returns>This pipeline for method chaining</returns>
    public AggregationPipeline AddStage(IAggregationStage stage)
    {
        _stages.Add(stage ?? throw new ArgumentNullException(nameof(stage)));
        return this;
    }

    /// <summary>
    /// Adds multiple stages to the pipeline
    /// </summary>
    /// <param name="stages">The stages to add</param>
    /// <returns>This pipeline for method chaining</returns>
    public AggregationPipeline AddStages(IEnumerable<IAggregationStage> stages)
    {
        if (stages == null) throw new ArgumentNullException(nameof(stages));
        
        foreach (var stage in stages)
        {
            _stages.Add(stage);
        }
        return this;
    }

    /// <summary>
    /// Executes the pipeline on a sequence of documents
    /// </summary>
    /// <param name="documents">Input documents</param>
    /// <returns>The aggregation result</returns>
    public AggregationResult Execute(IEnumerable<Document> documents)
    {
        if (documents == null)
            throw new ArgumentNullException(nameof(documents));

        var stopwatch = Stopwatch.StartNew();

        try
        {

            var currentDocuments = documents;

            foreach (var stage in _stages)
            {
                currentDocuments = stage.Execute(currentDocuments);
            }

            stopwatch.Stop();

            return AggregationResult.SuccessResult(
                currentDocuments.ToList(),
                _stages.Count,
                stopwatch.ElapsedMilliseconds
            );
        }
        catch (AggregationStageException ex)
        {
            stopwatch.Stop();
            return AggregationResult.FailureResult(
                $"Aggregation failed at stage '{ex.StageType}': {ex.Message}"
            );
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return AggregationResult.FailureResult($"Aggregation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Executes the pipeline on a sequence of documents asynchronously
    /// </summary>
    /// <param name="documents">Input documents</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The aggregation result</returns>
    public async Task<AggregationResult> ExecuteAsync(IEnumerable<Document> documents, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (documents == null)
                throw new ArgumentNullException(nameof(documents));

            var currentDocuments = documents;

            foreach (var stage in _stages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                currentDocuments = await stage.ExecuteAsync(currentDocuments, cancellationToken);
            }

            stopwatch.Stop();

            return AggregationResult.SuccessResult(
                currentDocuments.ToList(),
                _stages.Count,
                stopwatch.ElapsedMilliseconds
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AggregationStageException ex)
        {
            stopwatch.Stop();
            return AggregationResult.FailureResult(
                $"Aggregation failed at stage '{ex.StageType}': {ex.Message}"
            );
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return AggregationResult.FailureResult($"Aggregation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Clears all stages from the pipeline
    /// </summary>
    public void Clear()
    {
        _stages.Clear();
    }

    /// <summary>
    /// Returns the number of stages in the pipeline
    /// </summary>
    public int StageCount => _stages.Count;
}
