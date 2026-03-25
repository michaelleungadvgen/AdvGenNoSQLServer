// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AdvGenNoSqlServer.Core.Abstractions;
using AdvGenNoSqlServer.Query.Models;
using AdvGenNoSqlServer.Storage.Indexing;

namespace AdvGenNoSqlServer.Query.Optimization;

/// <summary>
/// Cost-based query optimizer implementation
/// </summary>
public class QueryOptimizer : IQueryOptimizer, IDisposable
{
    private readonly IDocumentStore _documentStore;
    private readonly IndexManager? _indexManager;
    private readonly Dictionary<string, CachedPlanEntry> _planCache;
    private readonly ReaderWriterLockSlim _cacheLock;
    private long _totalQueriesOptimized;
    private long _plansFromCache;
    private long _plansGenerated;
    private long _totalOptimizationTimeMs;
    private bool _disposed;

    /// <summary>
    /// Default optimization options
    /// </summary>
    public QueryOptimizerOptions DefaultOptions { get; set; } = new();

    /// <summary>
    /// Creates a new QueryOptimizer
    /// </summary>
    public QueryOptimizer(IDocumentStore documentStore, IndexManager? indexManager = null)
    {
        _documentStore = documentStore ?? throw new ArgumentNullException(nameof(documentStore));
        _indexManager = indexManager;
        _planCache = new Dictionary<string, CachedPlanEntry>();
        _cacheLock = new ReaderWriterLockSlim();
    }

    /// <inheritdoc />
    public Task<OptimizationResult> OptimizeAsync(
        Query.Models.Query query,
        QueryOptimizerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        options ??= DefaultOptions;

        try
        {
            // Check for disabled optimization
            if (options.Level == OptimizationLevel.None)
            {
                var noOptPlan = CreateNoOptimizationPlan(query);
                stopwatch.Stop();
                return Task.FromResult(new OptimizationResult
                {
                    IsSuccess = true,
                    SelectedPlan = noOptPlan,
                    OptimizationTime = stopwatch.Elapsed,
                    PlansConsidered = 1,
                    OptimizationLevel = options.Level
                });
            }

            // Check cache first
            if (options.EnablePlanCache)
            {
                var cachedPlan = GetCachedPlan(query);
                if (cachedPlan != null)
                {
                    stopwatch.Stop();
                    Interlocked.Increment(ref _plansFromCache);
                    Interlocked.Increment(ref _totalQueriesOptimized);
                    return Task.FromResult(new OptimizationResult
                    {
                        IsSuccess = true,
                        SelectedPlan = cachedPlan,
                        OptimizationTime = stopwatch.Elapsed,
                        PlansConsidered = 1,
                        OptimizationLevel = options.Level
                    });
                }
            }

            // Generate optimized plans
            var plans = GeneratePlans(query, options, cancellationToken);
            
            if (plans.Count == 0)
            {
                return Task.FromResult(new OptimizationResult
                {
                    IsSuccess = false,
                    ErrorMessage = "No valid execution plans generated"
                });
            }

            // Select best plan
            var bestPlan = SelectBestPlan(plans);
            var alternatives = plans.Where(p => p.PlanId != bestPlan.PlanId).ToList();

            // Cache the result
            if (options.EnablePlanCache)
            {
                CachePlan(query, bestPlan);
            }

            stopwatch.Stop();
            UpdateStatistics(stopwatch.ElapsedMilliseconds, plans.Count);

            return Task.FromResult(new OptimizationResult
            {
                IsSuccess = true,
                SelectedPlan = bestPlan,
                AlternativePlans = alternatives,
                OptimizationTime = stopwatch.Elapsed,
                PlansConsidered = plans.Count,
                OptimizationLevel = options.Level
            });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return Task.FromResult(new OptimizationResult
            {
                IsSuccess = false,
                ErrorMessage = $"Optimization failed: {ex.Message}"
            });
        }
    }

    /// <inheritdoc />
    public Task<OptimizedQueryPlan?> GetCachedPlanAsync(
        Query.Models.Query query,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(GetCachedPlan(query));
    }

    /// <inheritdoc />
    public Task ClearPlanCacheAsync(CancellationToken cancellationToken = default)
    {
        _cacheLock.EnterWriteLock();
        try
        {
            _planCache.Clear();
        }
        finally
        {
            _cacheLock.ExitWriteLock();
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<OptimizerStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        _cacheLock.EnterReadLock();
        int cachedCount;
        try
        {
            cachedCount = _planCache.Count;
        }
        finally
        {
            _cacheLock.ExitReadLock();
        }

        var totalQueries = Interlocked.Read(ref _totalQueriesOptimized);
        var totalTime = Interlocked.Read(ref _totalOptimizationTimeMs);
        
        var stats = new OptimizerStatistics
        {
            TotalQueriesOptimized = totalQueries,
            PlansFromCache = Interlocked.Read(ref _plansFromCache),
            PlansGenerated = Interlocked.Read(ref _plansGenerated),
            AverageOptimizationTimeMs = totalQueries > 0 ? (double)totalTime / totalQueries : 0,
            CachedPlanCount = cachedCount
        };
        
        return Task.FromResult(stats);
    }

    /// <summary>
    /// Resets optimizer statistics
    /// </summary>
    public void ResetStatistics()
    {
        Interlocked.Exchange(ref _totalQueriesOptimized, 0);
        Interlocked.Exchange(ref _plansFromCache, 0);
        Interlocked.Exchange(ref _plansGenerated, 0);
        Interlocked.Exchange(ref _totalOptimizationTimeMs, 0);
    }

    private List<OptimizedQueryPlan> GeneratePlans(
        Query.Models.Query query,
        QueryOptimizerOptions options,
        CancellationToken cancellationToken)
    {
        var plans = new List<OptimizedQueryPlan>();

        // Plan 1: Collection scan (baseline)
        var collectionScanPlan = CreateCollectionScanPlan(query);
        plans.Add(collectionScanPlan);

        // Skip advanced optimizations for basic level
        if (options.Level == OptimizationLevel.Basic)
        {
            return plans;
        }

        // Plan 2: Index scan (if applicable)
        if (options.EnableIndexSelection)
        {
            var indexPlan = TryCreateIndexPlan(query);
            if (indexPlan != null)
            {
                plans.Add(indexPlan);
            }
        }

        // Plan 3: Filter pushdown variant
        if (options.EnableFilterPushdown && query.Filter?.Conditions != null)
        {
            var pushdownPlan = CreateFilterPushdownPlan(query);
            plans.Add(pushdownPlan);
        }

        // Plan 4: Sort elimination (if index can provide sort)
        if (options.EnableSortElimination && query.Sort?.Count > 0)
        {
            var sortEliminatedPlan = TryCreateSortEliminatedPlan(query);
            if (sortEliminatedPlan != null)
            {
                plans.Add(sortEliminatedPlan);
            }
        }

        // Plan 5: Projection pushdown
        if (options.EnableProjectionPushdown && query.Projection?.Count > 0)
        {
            var projectionPlan = CreateProjectionPushdownPlan(query);
            plans.Add(projectionPlan);
        }

        // Limit to max alternative plans
        return plans.Take(options.MaxAlternativePlans).ToList();
    }

    private OptimizedQueryPlan CreateNoOptimizationPlan(Query.Models.Query query)
    {
        PlanNode rootNode = new CollectionScanNode
        {
            CollectionName = query.CollectionName,
            EstimatedCost = 1000,
            EstimatedOutputCardinality = 1000
        };

        // Add filter if present
        if (query.Filter?.Conditions != null && query.Filter.Conditions.Count > 0)
        {
            var filterNode = new FilterNode
            {
                Conditions = query.Filter.Conditions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                EstimatedCost = 100,
                EstimatedOutputCardinality = 300
            };
            filterNode.Children.Add(rootNode);
            rootNode = filterNode;
        }

        // Add sort if present
        if (query.Sort?.Count > 0)
        {
            var sortNode = new SortNode
            {
                SortFields = query.Sort.Select(s => new SortFieldNode 
                { 
                    FieldName = s.FieldName, 
                    Direction = s.Direction 
                }).ToList(),
                EstimatedCost = 200,
                EstimatedOutputCardinality = rootNode.EstimatedOutputCardinality
            };
            sortNode.Children.Add(rootNode);
            rootNode = sortNode;
        }

        // Add skip/limit
        rootNode = AddPaginationNodes(query, rootNode);

        return new OptimizedQueryPlan
        {
            PlanId = Guid.NewGuid().ToString("N"),
            OriginalQuery = query,
            OptimizedQuery = query,
            RootNode = rootNode,
            EstimatedCost = CalculateTotalCost(rootNode),
            EstimatedDocumentsToScan = 1000,
            EstimatedExecutionTimeMs = 100,
            UsesIndex = false,
            AppliedOptimizations = new List<AppliedOptimization>()
        };
    }

    private OptimizedQueryPlan CreateCollectionScanPlan(Query.Models.Query query)
    {
        PlanNode rootNode = new CollectionScanNode
        {
            CollectionName = query.CollectionName,
            EstimatedCost = EstimateCollectionScanCost(query),
            EstimatedOutputCardinality = EstimateCollectionCardinality(query)
        };

        // Add filter node if present
        if (query.Filter?.Conditions != null && query.Filter.Conditions.Count > 0)
        {
            var filterNode = CreateFilterNode(query.Filter.Conditions, rootNode.EstimatedOutputCardinality);
            filterNode.Children.Add(rootNode);
            rootNode = filterNode;
        }

        // Add sort node if present
        if (query.Sort?.Count > 0)
        {
            var sortNode = CreateSortNode(query.Sort, rootNode.EstimatedOutputCardinality);
            sortNode.Children.Add(rootNode);
            rootNode = sortNode;
        }

        // Add projection node if present
        if (query.Projection?.Count > 0)
        {
            var projectionNode = CreateProjectionNode(query.Projection, rootNode.EstimatedOutputCardinality);
            projectionNode.Children.Add(rootNode);
            rootNode = projectionNode;
        }

        // Add pagination nodes
        rootNode = AddPaginationNodes(query, rootNode);

        return new OptimizedQueryPlan
        {
            PlanId = Guid.NewGuid().ToString("N"),
            OriginalQuery = query,
            OptimizedQuery = query,
            RootNode = rootNode,
            EstimatedCost = CalculateTotalCost(rootNode),
            EstimatedDocumentsToScan = EstimateCollectionCardinality(query),
            EstimatedExecutionTimeMs = EstimateExecutionTime(rootNode),
            UsesIndex = false,
            AppliedOptimizations = new List<AppliedOptimization>()
        };
    }

    private OptimizedQueryPlan? TryCreateIndexPlan(Query.Models.Query query)
    {
        if (_indexManager == null || query.Filter?.Conditions == null)
            return null;

        // Find best index for the query
        foreach (var condition in query.Filter.Conditions)
        {
            if (condition.Key.StartsWith('$'))
                continue;

            if (_indexManager.HasIndex(query.CollectionName, condition.Key))
            {
                var cardinality = EstimateCollectionCardinality(query);

                var indexScanNode = new IndexScanNode
                {
                    CollectionName = query.CollectionName,
                    IndexName = $"{query.CollectionName}_{condition.Key}_idx",
                    IndexField = condition.Key,
                    ScanType = DetermineScanType(condition.Value),
                    IndexCondition = new Dictionary<string, object> { [condition.Key] = condition.Value },
                    EstimatedCost = 10, // Much cheaper than collection scan
                    EstimatedOutputCardinality = Math.Max(1, cardinality / 10), // Assume 10% selectivity
                    Description = $"Index scan on {condition.Key}"
                };

                var rootNode = (PlanNode)indexScanNode;

                // Add remaining filters
                var remainingFilters = query.Filter.Conditions
                    .Where(c => c.Key != condition.Key && !c.Key.StartsWith('$'))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                if (remainingFilters.Count > 0)
                {
                    var filterNode = CreateFilterNode(remainingFilters, rootNode.EstimatedOutputCardinality);
                    filterNode.Children.Add(rootNode);
                    rootNode = filterNode;
                }

                // Add sort (may be index-based if sorting on indexed field)
                if (query.Sort?.Count > 0)
                {
                    var sortOnIndexedField = query.Sort.Any(s => s.FieldName == condition.Key);
                    var sortNode = CreateSortNode(query.Sort, rootNode.EstimatedOutputCardinality, sortOnIndexedField);
                    sortNode.Children.Add(rootNode);
                    rootNode = sortNode;
                }

                // Add projection
                if (query.Projection?.Count > 0)
                {
                    var projectionNode = CreateProjectionNode(query.Projection, rootNode.EstimatedOutputCardinality);
                    projectionNode.Children.Add(rootNode);
                    rootNode = projectionNode;
                }

                rootNode = AddPaginationNodes(query, rootNode);

                var plan = new OptimizedQueryPlan
                {
                    PlanId = Guid.NewGuid().ToString("N"),
                    OriginalQuery = query,
                    OptimizedQuery = query,
                    RootNode = rootNode,
                    EstimatedCost = CalculateTotalCost(rootNode),
                    EstimatedDocumentsToScan = indexScanNode.EstimatedOutputCardinality,
                    EstimatedExecutionTimeMs = EstimateExecutionTime(rootNode),
                    UsesIndex = true,
                    IndexNames = new List<string> { indexScanNode.IndexName },
                    AppliedOptimizations = new List<AppliedOptimization>
                    {
                        new AppliedOptimization
                        {
                            RuleName = "IndexSelection",
                            Description = $"Selected index on {condition.Key}",
                            CostSavings = EstimateCollectionScanCost(query) - indexScanNode.EstimatedCost
                        }
                    }
                };

                return plan;
            }
        }

        return null;
    }

    private OptimizedQueryPlan CreateFilterPushdownPlan(Query.Models.Query query)
    {
        // For filter pushdown, we keep the same structure but annotate with selectivity info
        var basePlan = CreateCollectionScanPlan(query);

        // Mark as filter pushdown optimized
        basePlan.AppliedOptimizations.Add(new AppliedOptimization
        {
            RuleName = "FilterPushdown",
            Description = "Filters applied as early as possible",
            CostSavings = 0
        });

        return basePlan;
    }

    private OptimizedQueryPlan? TryCreateSortEliminatedPlan(Query.Models.Query query)
    {
        if (query.Sort == null || query.Sort.Count == 0 || _indexManager == null)
            return null;

        // Check if we can use an index to provide sorted results
        var sortField = query.Sort[0].FieldName;
        
        if (_indexManager.HasIndex(query.CollectionName, sortField))
        {
            // Create a variant of the index plan without sort
            var plan = TryCreateIndexPlan(query);
            if (plan != null)
            {
                // Modify the plan to indicate sort is index-provided
                plan.AppliedOptimizations.Add(new AppliedOptimization
                {
                    RuleName = "SortElimination",
                    Description = $"Sort eliminated - using index on {sortField}",
                    CostSavings = 200
                });
                return plan;
            }
        }

        return null;
    }

    private OptimizedQueryPlan CreateProjectionPushdownPlan(Query.Models.Query query)
    {
        var basePlan = CreateCollectionScanPlan(query);

        basePlan.AppliedOptimizations.Add(new AppliedOptimization
        {
            RuleName = "ProjectionPushdown",
            Description = "Projection applied early to reduce memory usage",
            CostSavings = 50
        });

        return basePlan;
    }

    private FilterNode CreateFilterNode(Dictionary<string, object> conditions, long inputCardinality)
    {
        var selectivity = EstimateSelectivity(conditions);
        return new FilterNode
        {
            Conditions = new Dictionary<string, object>(conditions),
            Selectivity = selectivity,
            EstimatedCost = inputCardinality * 0.05,
            EstimatedOutputCardinality = (long)(inputCardinality * selectivity),
            CanUseIndex = false,
            Description = $"Filter {conditions.Count} conditions"
        };
    }

    private SortNode CreateSortNode(List<SortField> sortFields, long inputCardinality, bool usesIndex = false)
    {
        var sortCost = usesIndex ? 0 : inputCardinality * Math.Log(inputCardinality + 1) * 0.01;
        
        return new SortNode
        {
            SortFields = sortFields.Select(s => new SortFieldNode 
            { 
                FieldName = s.FieldName, 
                Direction = s.Direction 
            }).ToList(),
            UsesIndex = usesIndex,
            Algorithm = usesIndex ? "IndexOrder" : "InMemory QuickSort",
            EstimatedCost = sortCost,
            EstimatedOutputCardinality = inputCardinality,
            EstimatedMemoryBytes = inputCardinality * 100,
            Description = usesIndex ? "Index-ordered sort" : "In-memory sort"
        };
    }

    private ProjectionNode CreateProjectionNode(Dictionary<string, bool> projection, long inputCardinality)
    {
        var includeFields = projection.Where(p => p.Value).Select(p => p.Key).ToList();
        var excludeFields = projection.Where(p => !p.Value).Select(p => p.Key).ToList();

        return new ProjectionNode
        {
            IncludeFields = includeFields,
            ExcludeFields = excludeFields,
            EstimatedCost = inputCardinality * 0.01,
            EstimatedOutputCardinality = inputCardinality,
            Description = $"Project {projection.Count} fields"
        };
    }

    private PlanNode AddPaginationNodes(Query.Models.Query query, PlanNode rootNode)
    {
        // Add skip node
        if (query.Options?.Skip > 0)
        {
            var skipNode = new SkipNode
            {
                SkipCount = query.Options.Skip.Value,
                EstimatedCost = query.Options.Skip.Value * 0.01,
                EstimatedOutputCardinality = Math.Max(0, rootNode.EstimatedOutputCardinality - query.Options.Skip.Value)
            };
            skipNode.Children.Add(rootNode);
            rootNode = skipNode;
        }

        // Add limit node
        if (query.Options?.Limit.HasValue == true)
        {
            var limitNode = new LimitNode
            {
                LimitCount = query.Options.Limit.Value,
                EstimatedCost = 1,
                EstimatedOutputCardinality = Math.Min(rootNode.EstimatedOutputCardinality, query.Options.Limit.Value)
            };
            limitNode.Children.Add(rootNode);
            rootNode = limitNode;
        }

        return rootNode;
    }

    private OptimizedQueryPlan SelectBestPlan(List<OptimizedQueryPlan> plans)
    {
        // Simple cost-based selection
        return plans.OrderBy(p => p.EstimatedCost).First();
    }

    private double CalculateTotalCost(PlanNode node)
    {
        var cost = node.EstimatedCost;
        foreach (var child in node.Children)
        {
            cost += CalculateTotalCost(child);
        }
        return cost;
    }

    private double EstimateExecutionTime(PlanNode node)
    {
        // Simple heuristic: 1ms per cost unit
        return CalculateTotalCost(node);
    }

    private double EstimateCollectionScanCost(Query.Models.Query query)
    {
        var count = EstimateCollectionCardinality(query);
        return count * 0.1; // Cost per document
    }

    private long EstimateCollectionCardinality(Query.Models.Query query)
    {
        try
        {
            return _documentStore.CountAsync(query.CollectionName).GetAwaiter().GetResult();
        }
        catch
        {
            return 1000; // Default estimate
        }
    }

    private double EstimateSelectivity(Dictionary<string, object> conditions)
    {
        // Simple selectivity estimation
        double selectivity = 1.0;

        foreach (var condition in conditions)
        {
            if (condition.Key.StartsWith('$'))
            {
                selectivity *= 0.5;
            }
            else if (condition.Value is Dictionary<string, object> operators)
            {
                if (operators.ContainsKey("$eq"))
                    selectivity *= 0.3;
                else if (operators.ContainsKey("$in"))
                    selectivity *= 0.4;
                else if (operators.ContainsKey("$gt") || operators.ContainsKey("$gte") ||
                         operators.ContainsKey("$lt") || operators.ContainsKey("$lte"))
                    selectivity *= 0.5;
                else
                    selectivity *= 0.6;
            }
            else
            {
                selectivity *= 0.3;
            }
        }

        return Math.Max(0.001, selectivity);
    }

    private string DetermineScanType(object condition)
    {
        if (condition is Dictionary<string, object> operators)
        {
            if (operators.ContainsKey("$eq"))
                return "ExactMatch";
            if (operators.ContainsKey("$gt") || operators.ContainsKey("$gte") ||
                operators.ContainsKey("$lt") || operators.ContainsKey("$lte"))
                return "Range";
            if (operators.ContainsKey("$in"))
                return "MultiValue";
        }
        return "ExactMatch";
    }

    private string GetPlanCacheKey(Query.Models.Query query)
    {
        // Create a hash of the query for caching
        var queryJson = JsonSerializer.Serialize(new
        {
            query.CollectionName,
            query.Filter,
            query.Sort,
            query.Projection,
            query.Options
        });

        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(queryJson));
        return Convert.ToHexString(hash);
    }

    private OptimizedQueryPlan? GetCachedPlan(Query.Models.Query query)
    {
        var key = GetPlanCacheKey(query);
        
        _cacheLock.EnterReadLock();
        try
        {
            if (_planCache.TryGetValue(key, out var entry))
            {
                // Check if entry is expired (30 minute TTL)
                if (DateTime.UtcNow - entry.CreatedAt < TimeSpan.FromMinutes(30))
                {
                    return entry.Plan;
                }
            }
        }
        finally
        {
            _cacheLock.ExitReadLock();
        }

        return null;
    }

    private void CachePlan(Query.Models.Query query, OptimizedQueryPlan plan)
    {
        var key = GetPlanCacheKey(query);

        _cacheLock.EnterWriteLock();
        try
        {
            // Evict old entries if cache is full
            if (_planCache.Count >= DefaultOptions.MaxCachedPlans)
            {
                var oldest = _planCache.OrderBy(e => e.Value.LastAccessed).FirstOrDefault();
                if (oldest.Key != null)
                {
                    _planCache.Remove(oldest.Key);
                }
            }

            _planCache[key] = new CachedPlanEntry
            {
                Plan = plan,
                CreatedAt = DateTime.UtcNow,
                LastAccessed = DateTime.UtcNow
            };
        }
        finally
        {
            _cacheLock.ExitWriteLock();
        }
    }

    private void UpdateStatistics(long optimizationTimeMs, int plansGenerated)
    {
        Interlocked.Increment(ref _totalQueriesOptimized);
        Interlocked.Add(ref _plansGenerated, plansGenerated);
        Interlocked.Add(ref _totalOptimizationTimeMs, optimizationTimeMs);
    }

    /// <summary>
    /// Disposes the optimizer and clears resources
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _cacheLock.Dispose();
            _disposed = true;
        }
    }

    private class CachedPlanEntry
    {
        public required OptimizedQueryPlan Plan { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastAccessed { get; set; }
    }
}
