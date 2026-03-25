// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Query.Models;

namespace AdvGenNoSqlServer.Query.Optimization;

/// <summary>
/// Represents the level of query optimization to apply
/// </summary>
public enum OptimizationLevel
{
    /// <summary>
    /// No optimization - execute query as-is
    /// </summary>
    None,

    /// <summary>
    /// Basic optimizations - index selection and simple rewrites
    /// </summary>
    Basic,

    /// <summary>
    /// Full optimization - all available optimization rules
    /// </summary>
    Full
}

/// <summary>
/// Configuration options for the query optimizer
/// </summary>
public class QueryOptimizerOptions
{
    /// <summary>
    /// The optimization level to apply
    /// </summary>
    public OptimizationLevel Level { get; set; } = OptimizationLevel.Full;

    /// <summary>
    /// Maximum number of alternative plans to consider
    /// </summary>
    public int MaxAlternativePlans { get; set; } = 10;

    /// <summary>
    /// Timeout for optimization in milliseconds
    /// </summary>
    public int OptimizationTimeoutMs { get; set; } = 1000;

    /// <summary>
    /// Whether to cache optimized query plans
    /// </summary>
    public bool EnablePlanCache { get; set; } = true;

    /// <summary>
    /// Maximum number of cached plans
    /// </summary>
    public int MaxCachedPlans { get; set; } = 1000;

    /// <summary>
    /// Whether to enable filter pushdown optimization
    /// </summary>
    public bool EnableFilterPushdown { get; set; } = true;

    /// <summary>
    /// Whether to enable index selection optimization
    /// </summary>
    public bool EnableIndexSelection { get; set; } = true;

    /// <summary>
    /// Whether to enable sort elimination optimization
    /// </summary>
    public bool EnableSortElimination { get; set; } = true;

    /// <summary>
    /// Whether to enable projection pushdown optimization
    /// </summary>
    public bool EnableProjectionPushdown { get; set; } = true;

    /// <summary>
    /// Creates a copy of these options
    /// </summary>
    public QueryOptimizerOptions Clone()
    {
        return new QueryOptimizerOptions
        {
            Level = this.Level,
            MaxAlternativePlans = this.MaxAlternativePlans,
            OptimizationTimeoutMs = this.OptimizationTimeoutMs,
            EnablePlanCache = this.EnablePlanCache,
            MaxCachedPlans = this.MaxCachedPlans,
            EnableFilterPushdown = this.EnableFilterPushdown,
            EnableIndexSelection = this.EnableIndexSelection,
            EnableSortElimination = this.EnableSortElimination,
            EnableProjectionPushdown = this.EnableProjectionPushdown
        };
    }
}

/// <summary>
/// Represents an optimized query execution plan
/// </summary>
public class OptimizedQueryPlan
{
    /// <summary>
    /// Unique identifier for this plan
    /// </summary>
    public required string PlanId { get; set; }

    /// <summary>
    /// The original query
    /// </summary>
    public required Query.Models.Query OriginalQuery { get; set; }

    /// <summary>
    /// The optimized query (may be transformed)
    /// </summary>
    public required Query.Models.Query OptimizedQuery { get; set; }

    /// <summary>
    /// The root node of the execution plan tree
    /// </summary>
    public required PlanNode RootNode { get; set; }

    /// <summary>
    /// Estimated cost of executing this plan
    /// </summary>
    public double EstimatedCost { get; set; }

    /// <summary>
    /// Estimated number of documents to scan
    /// </summary>
    public long EstimatedDocumentsToScan { get; set; }

    /// <summary>
    /// Estimated execution time in milliseconds
    /// </summary>
    public double EstimatedExecutionTimeMs { get; set; }

    /// <summary>
    /// Whether this plan uses an index
    /// </summary>
    public bool UsesIndex { get; set; }

    /// <summary>
    /// Names of indexes used by this plan
    /// </summary>
    public List<string> IndexNames { get; set; } = new();

    /// <summary>
    /// List of optimization rules applied
    /// </summary>
    public List<AppliedOptimization> AppliedOptimizations { get; set; } = new();

    /// <summary>
    /// When this plan was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Plan statistics and metadata
    /// </summary>
    public PlanStatistics Statistics { get; set; } = new();
}

/// <summary>
/// Base class for nodes in an execution plan tree
/// </summary>
public abstract class PlanNode
{
    /// <summary>
    /// Node type identifier
    /// </summary>
    public abstract string NodeType { get; }

    /// <summary>
    /// Estimated cost for this node
    /// </summary>
    public double EstimatedCost { get; set; }

    /// <summary>
    /// Estimated number of documents output by this node
    /// </summary>
    public long EstimatedOutputCardinality { get; set; }

    /// <summary>
    /// Child nodes (if any)
    /// </summary>
    public List<PlanNode> Children { get; set; } = new();

    /// <summary>
    /// Node-specific properties
    /// </summary>
    public Dictionary<string, object> Properties { get; set; } = new();

    /// <summary>
    /// Description of what this node does
    /// </summary>
    public string? Description { get; set; }
}

/// <summary>
/// Collection scan node - scans all documents in a collection
/// </summary>
public class CollectionScanNode : PlanNode
{
    /// <inheritdoc />
    public override string NodeType => "CollectionScan";

    /// <summary>
    /// Collection name to scan
    /// </summary>
    public required string CollectionName { get; set; }

    /// <summary>
    /// Whether this is a full collection scan
    /// </summary>
    public bool IsFullScan { get; set; } = true;
}

/// <summary>
/// Index scan node - uses an index to find documents
/// </summary>
public class IndexScanNode : PlanNode
{
    /// <inheritdoc />
    public override string NodeType => "IndexScan";

    /// <summary>
    /// Collection name
    /// </summary>
    public required string CollectionName { get; set; }

    /// <summary>
    /// Index name to use
    /// </summary>
    public required string IndexName { get; set; }

    /// <summary>
    /// Field being indexed
    /// </summary>
    public required string IndexField { get; set; }

    /// <summary>
    /// Type of index scan (ExactMatch, Range, Full)
    /// </summary>
    public string ScanType { get; set; } = "ExactMatch";

    /// <summary>
    /// Filter condition using the index
    /// </summary>
    public Dictionary<string, object>? IndexCondition { get; set; }
}

/// <summary>
/// Index seek node - directly seeks to specific index entries
/// </summary>
public class IndexSeekNode : PlanNode
{
    /// <inheritdoc />
    public override string NodeType => "IndexSeek";

    /// <summary>
    /// Collection name
    /// </summary>
    public required string CollectionName { get; set; }

    /// <summary>
    /// Index name to use
    /// </summary>
    public required string IndexName { get; set; }

    /// <summary>
    /// Field being indexed
    /// </summary>
    public required string IndexField { get; set; }

    /// <summary>
    /// Values to seek (for IN queries)
    /// </summary>
    public List<object>? SeekValues { get; set; }

    /// <summary>
    /// Single value to seek (for equality queries)
    /// </summary>
    public object? SeekValue { get; set; }
}

/// <summary>
/// Filter node - applies filter conditions to documents
/// </summary>
public class FilterNode : PlanNode
{
    /// <inheritdoc />
    public override string NodeType => "Filter";

    /// <summary>
    /// Filter conditions to apply
    /// </summary>
    public required Dictionary<string, object> Conditions { get; set; }

    /// <summary>
    /// Selectivity estimate (0.0 to 1.0)
    /// </summary>
    public double Selectivity { get; set; } = 0.3;

    /// <summary>
    /// Whether this filter can use an index
    /// </summary>
    public bool CanUseIndex { get; set; }

    /// <summary>
    /// Recommended index for this filter (if any)
    /// </summary>
    public string? RecommendedIndex { get; set; }
}

/// <summary>
/// Sort node - sorts documents
/// </summary>
public class SortNode : PlanNode
{
    /// <inheritdoc />
    public override string NodeType => "Sort";

    /// <summary>
    /// Sort fields with direction
    /// </summary>
    public required List<SortFieldNode> SortFields { get; set; }

    /// <summary>
    /// Whether sort uses an index
    /// </summary>
    public bool UsesIndex { get; set; }

    /// <summary>
    /// Sort algorithm used
    /// </summary>
    public string Algorithm { get; set; } = "InMemory";

    /// <summary>
    /// Estimated memory required for sort
    /// </summary>
    public long EstimatedMemoryBytes { get; set; }
}

/// <summary>
/// Sort field specification for plan nodes
/// </summary>
public class SortFieldNode
{
    /// <summary>
    /// Field name
    /// </summary>
    public required string FieldName { get; set; }

    /// <summary>
    /// Sort direction
    /// </summary>
    public SortDirection Direction { get; set; } = SortDirection.Ascending;
}

/// <summary>
/// Projection node - limits fields returned
/// </summary>
public class ProjectionNode : PlanNode
{
    /// <inheritdoc />
    public override string NodeType => "Projection";

    /// <summary>
    /// Fields to include (if non-empty)
    /// </summary>
    public List<string> IncludeFields { get; set; } = new();

    /// <summary>
    /// Fields to exclude (if non-empty)
    /// </summary>
    public List<string> ExcludeFields { get; set; } = new();

    /// <summary>
    /// Whether this is an inclusion or exclusion projection
    /// </summary>
    public bool IsInclusion => IncludeFields.Count > 0;
}

/// <summary>
/// Skip node - skips N documents
/// </summary>
public class SkipNode : PlanNode
{
    /// <inheritdoc />
    public override string NodeType => "Skip";

    /// <summary>
    /// Number of documents to skip
    /// </summary>
    public int SkipCount { get; set; }
}

/// <summary>
/// Limit node - limits output to N documents
/// </summary>
public class LimitNode : PlanNode
{
    /// <inheritdoc />
    public override string NodeType => "Limit";

    /// <summary>
    /// Maximum number of documents to return
    /// </summary>
    public int LimitCount { get; set; }
}

/// <summary>
/// Represents an optimization that was applied
/// </summary>
public class AppliedOptimization
{
    /// <summary>
    /// Name of the optimization rule
    /// </summary>
    public required string RuleName { get; set; }

    /// <summary>
    /// Description of what was optimized
    /// </summary>
    public required string Description { get; set; }

    /// <summary>
    /// Estimated cost savings from this optimization
    /// </summary>
    public double CostSavings { get; set; }

    /// <summary>
    /// Original plan node (before optimization)
    /// </summary>
    public string? OriginalPlan { get; set; }

    /// <summary>
    /// Optimized plan node (after optimization)
    /// </summary>
    public string? OptimizedPlan { get; set; }

    /// <summary>
    /// When this optimization was applied
    /// </summary>
    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Statistics for an optimized plan
/// </summary>
public class PlanStatistics
{
    /// <summary>
    /// Number of times this plan has been executed
    /// </summary>
    public long ExecutionCount { get; set; }

    /// <summary>
    /// Total execution time across all executions
    /// </summary>
    public long TotalExecutionTimeMs { get; set; }

    /// <summary>
    /// Average execution time
    /// </summary>
    public double AverageExecutionTimeMs => ExecutionCount > 0 ? (double)TotalExecutionTimeMs / ExecutionCount : 0;

    /// <summary>
    /// Last time this plan was executed
    /// </summary>
    public DateTime? LastExecutedAt { get; set; }

    /// <summary>
    /// Whether actual performance matches estimates
    /// </summary>
    public bool EstimatesAccurate { get; set; } = true;
}

/// <summary>
/// Result of query optimization
/// </summary>
public class OptimizationResult
{
    /// <summary>
    /// Whether optimization was successful
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Error message if optimization failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// The selected optimal plan
    /// </summary>
    public OptimizedQueryPlan? SelectedPlan { get; set; }

    /// <summary>
    /// Alternative plans that were considered
    /// </summary>
    public List<OptimizedQueryPlan> AlternativePlans { get; set; } = new();

    /// <summary>
    /// Time spent optimizing
    /// </summary>
    public TimeSpan OptimizationTime { get; set; }

    /// <summary>
    /// Number of plans considered
    /// </summary>
    public int PlansConsidered { get; set; }

    /// <summary>
    /// Optimization level applied
    /// </summary>
    public OptimizationLevel OptimizationLevel { get; set; }


}

/// <summary>
/// Interface for query optimizer
/// </summary>
public interface IQueryOptimizer
{
    /// <summary>
    /// Optimizes a query and returns the best execution plan
    /// </summary>
    /// <param name="query">The query to optimize</param>
    /// <param name="options">Optimization options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Optimization result with selected plan</returns>
    Task<OptimizationResult> OptimizeAsync(
        Query.Models.Query query,
        QueryOptimizerOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a cached plan if available
    /// </summary>
    /// <param name="query">The query to look up</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Cached plan or null</returns>
    Task<OptimizedQueryPlan?> GetCachedPlanAsync(
        Query.Models.Query query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the plan cache
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ClearPlanCacheAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets optimizer statistics
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Optimizer statistics</returns>
    Task<OptimizerStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Statistics for the query optimizer
/// </summary>
public class OptimizerStatistics
{
    /// <summary>
    /// Total number of queries optimized
    /// </summary>
    public long TotalQueriesOptimized { get; set; }

    /// <summary>
    /// Number of plans retrieved from cache
    /// </summary>
    public long PlansFromCache { get; set; }

    /// <summary>
    /// Number of new plans generated
    /// </summary>
    public long PlansGenerated { get; set; }

    /// <summary>
    /// Average optimization time in milliseconds
    /// </summary>
    public double AverageOptimizationTimeMs { get; set; }

    /// <summary>
    /// Current number of cached plans
    /// </summary>
    public int CachedPlanCount { get; set; }

    /// <summary>
    /// Cache hit rate (0.0 to 1.0)
    /// </summary>
    public double CacheHitRate => TotalQueriesOptimized > 0 ? (double)PlansFromCache / TotalQueriesOptimized : 0;

    /// <summary>
    /// When statistics were last reset
    /// </summary>
    public DateTime LastResetAt { get; set; } = DateTime.UtcNow;
}
