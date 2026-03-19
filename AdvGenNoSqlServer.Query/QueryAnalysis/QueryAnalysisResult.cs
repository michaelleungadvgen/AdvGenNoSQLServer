// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Text.Json;
using AdvGenNoSqlServer.Query.Models;

namespace AdvGenNoSqlServer.Query.QueryAnalysis;

/// <summary>
/// Represents the result of an EXPLAIN query analysis
/// </summary>
public class QueryAnalysisResult
{
    /// <summary>
    /// The analyzed query
    /// </summary>
    public required QueryInfo Query { get; set; }

    /// <summary>
    /// Overall query execution summary
    /// </summary>
    public required ExecutionSummary Summary { get; set; }

    /// <summary>
    /// Detailed execution plan with cost estimates
    /// </summary>
    public required List<PlanStage> ExecutionPlan { get; set; }

    /// <summary>
    /// Index recommendations for query optimization
    /// </summary>
    public List<IndexRecommendation> IndexRecommendations { get; set; } = new();

    /// <summary>
    /// Query optimization suggestions
    /// </summary>
    public List<OptimizationSuggestion> Suggestions { get; set; } = new();

    /// <summary>
    /// Alternative query plans considered
    /// </summary>
    public List<AlternativePlan> AlternativePlans { get; set; } = new();

    /// <summary>
    /// Whether this query is considered a slow query
    /// </summary>
    public bool IsSlowQuery { get; set; }

    /// <summary>
    /// The query complexity score (0-100, higher = more complex)
    /// </summary>
    public int ComplexityScore { get; set; }

    /// <summary>
    /// Query analysis timestamp
    /// </summary>
    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Information about the analyzed query
/// </summary>
public class QueryInfo
{
    /// <summary>
    /// Collection being queried
    /// </summary>
    public required string Collection { get; set; }

    /// <summary>
    /// Query filter conditions
    /// </summary>
    public Dictionary<string, object>? Filter { get; set; }

    /// <summary>
    /// Sort fields
    /// </summary>
    public List<SortFieldInfo>? SortFields { get; set; }

    /// <summary>
    /// Projection fields
    /// </summary>
    public Dictionary<string, bool>? Projection { get; set; }

    /// <summary>
    /// Pagination options
    /// </summary>
    public PaginationInfo? Pagination { get; set; }

    /// <summary>
    /// Query as JSON string
    /// </summary>
    public string? QueryJson { get; set; }
}

/// <summary>
/// Sort field information
/// </summary>
public class SortFieldInfo
{
    public required string Field { get; set; }
    public string Direction { get; set; } = "ascending";
}

/// <summary>
/// Pagination information
/// </summary>
public class PaginationInfo
{
    public int Skip { get; set; }
    public int? Limit { get; set; }
}

/// <summary>
/// Execution summary statistics
/// </summary>
public class ExecutionSummary
{
    /// <summary>
    /// Estimated execution time in milliseconds
    /// </summary>
    public long EstimatedExecutionTimeMs { get; set; }

    /// <summary>
    /// Estimated number of documents to scan
    /// </summary>
    public long EstimatedDocumentsToScan { get; set; }

    /// <summary>
    /// Estimated number of documents to return
    /// </summary>
    public long EstimatedDocumentsToReturn { get; set; }

    /// <summary>
    /// Whether an index will be used
    /// </summary>
    public bool WillUseIndex { get; set; }

    /// <summary>
    /// Name of the index that will be used
    /// </summary>
    public string? IndexName { get; set; }

    /// <summary>
    /// Type of index usage (Exact, Range, MultiKey, etc.)
    /// </summary>
    public string? IndexUsageType { get; set; }

    /// <summary>
    /// Whether the query can be covered by an index
    /// </summary>
    public bool IsIndexCovered { get; set; }

    /// <summary>
    /// Overall execution strategy
    /// </summary>
    public required string ExecutionStrategy { get; set; }

    /// <summary>
    /// Total cost estimate (arbitrary units, lower = better)
    /// </summary>
    public double TotalCost { get; set; }
}

/// <summary>
/// A stage in the execution plan with cost estimates
/// </summary>
public class PlanStage
{
    /// <summary>
    /// Stage name (e.g., "IndexScan", "CollectionScan", "Filter", "Sort")
    /// </summary>
    public required string StageName { get; set; }

    /// <summary>
    /// Stage order in execution (1-based)
    /// </summary>
    public int StageOrder { get; set; }

    /// <summary>
    /// Estimated cost for this stage
    /// </summary>
    public double EstimatedCost { get; set; }

    /// <summary>
    /// Estimated documents input to this stage
    /// </summary>
    public long EstimatedInputDocuments { get; set; }

    /// <summary>
    /// Estimated documents output from this stage
    /// </summary>
    public long EstimatedOutputDocuments { get; set; }

    /// <summary>
    /// Estimated execution time for this stage
    /// </summary>
    public long EstimatedTimeMs { get; set; }

    /// <summary>
    /// Stage-specific details
    /// </summary>
    public Dictionary<string, object> Details { get; set; } = new();

    /// <summary>
    /// Sub-stages (for nested operations)
    /// </summary>
    public List<PlanStage>? SubStages { get; set; }
}

/// <summary>
/// Index recommendation for query optimization
/// </summary>
public class IndexRecommendation
{
    /// <summary>
    /// Recommendation priority (High, Medium, Low)
    /// </summary>
    public required string Priority { get; set; }

    /// <summary>
    /// Recommended index fields
    /// </summary>
    public required List<string> Fields { get; set; }

    /// <summary>
    /// Whether the index should be unique
    /// </summary>
    public bool IsUnique { get; set; }

    /// <summary>
    /// Expected performance improvement
    /// </summary>
    public string? PerformanceImprovement { get; set; }

    /// <summary>
    /// Current query impact
    /// </summary>
    public string? CurrentImpact { get; set; }

    /// <summary>
    /// Index creation command
    /// </summary>
    public string? CreateIndexCommand { get; set; }

    /// <summary>
    /// Explanation of why this index is recommended
    /// </summary>
    public string? Explanation { get; set; }
}

/// <summary>
/// Query optimization suggestion
/// </summary>
public class OptimizationSuggestion
{
    /// <summary>
    /// Suggestion type (e.g., "FilterOptimization", "SortOptimization", "Pagination")
    /// </summary>
    public required string Type { get; set; }

    /// <summary>
    /// Suggestion severity (Critical, High, Medium, Low)
    /// </summary>
    public required string Severity { get; set; }

    /// <summary>
    /// Suggestion title
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Detailed description
    /// </summary>
    public required string Description { get; set; }

    /// <summary>
    /// Current implementation
    /// </summary>
    public string? CurrentImplementation { get; set; }

    /// <summary>
    /// Suggested improvement
    /// </summary>
    public string? SuggestedImprovement { get; set; }

    /// <summary>
    /// Expected benefit
    /// </summary>
    public string? ExpectedBenefit { get; set; }
}

/// <summary>
/// Alternative query plan considered
/// </summary>
public class AlternativePlan
{
    /// <summary>
    /// Plan name/description
    /// </summary>
    public required string PlanName { get; set; }

    /// <summary>
    /// Estimated cost (compared to chosen plan)
    /// </summary>
    public double EstimatedCost { get; set; }

    /// <summary>
    /// Whether this plan was rejected
    /// </summary>
    public bool IsRejected { get; set; }

    /// <summary>
    /// Reason for rejection (if rejected)
    /// </summary>
    public string? RejectionReason { get; set; }

    /// <summary>
    /// Brief description of the alternative approach
    /// </summary>
    public string? Description { get; set; }
}

/// <summary>
/// EXPLAIN verbosity levels
/// </summary>
public enum ExplainVerbosity
{
    /// <summary>
    /// Basic plan information only
    /// </summary>
    QueryPlanner,

    /// <summary>
    /// Plan with execution estimates
    /// </summary>
    ExecutionStats,

    /// <summary>
    /// All plans with optimization details
    /// </summary>
    AllPlansExecution
}
