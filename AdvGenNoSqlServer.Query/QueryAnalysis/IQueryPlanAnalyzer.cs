// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Query.Models;

namespace AdvGenNoSqlServer.Query.QueryAnalysis;

/// <summary>
/// Service for analyzing query plans and providing optimization recommendations
/// </summary>
public interface IQueryPlanAnalyzer
{
    /// <summary>
    /// Analyzes a query and returns detailed execution plan information
    /// </summary>
    /// <param name="query">The query to analyze</param>
    /// <param name="verbosity">Level of detail to include</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Detailed query analysis result</returns>
    Task<QueryAnalysisResult> AnalyzeAsync(
        Query.Models.Query query,
        ExplainVerbosity verbosity = ExplainVerbosity.ExecutionStats,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets index recommendations for a query
    /// </summary>
    /// <param name="query">The query to analyze</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of index recommendations</returns>
    Task<List<IndexRecommendation>> GetIndexRecommendationsAsync(
        Query.Models.Query query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets optimization suggestions for a query
    /// </summary>
    /// <param name="query">The query to analyze</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of optimization suggestions</returns>
    Task<List<OptimizationSuggestion>> GetOptimizationSuggestionsAsync(
        Query.Models.Query query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates query complexity score
    /// </summary>
    /// <param name="query">The query to analyze</param>
    /// <returns>Complexity score (0-100)</returns>
    int CalculateComplexityScore(Query.Models.Query query);

    /// <summary>
    /// Estimates the number of documents a query will scan
    /// </summary>
    /// <param name="query">The query to analyze</param>
    /// <returns>Estimated document count</returns>
    Task<long> EstimateDocumentCountAsync(
        Query.Models.Query query,
        CancellationToken cancellationToken = default);
}
