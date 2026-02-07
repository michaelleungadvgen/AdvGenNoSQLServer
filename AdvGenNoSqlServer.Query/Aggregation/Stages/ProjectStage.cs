// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Query.Aggregation.Stages;

/// <summary>
/// $project stage - Reshapes documents by including/excluding fields
/// </summary>
public class ProjectStage : IAggregationStage
{
    private readonly Dictionary<string, bool> _projections;
    private readonly Dictionary<string, string>? _fieldMappings;

    /// <inheritdoc />
    public string StageType => "$project";

    /// <summary>
    /// Creates a new ProjectStage with inclusion/exclusion projections
    /// </summary>
    /// <param name="projections">Field name to include (true) or exclude (false) mapping</param>
    public ProjectStage(Dictionary<string, bool> projections)
    {
        _projections = new Dictionary<string, bool>(projections ?? throw new ArgumentNullException(nameof(projections)));
        _fieldMappings = null;
    }

    /// <summary>
    /// Creates a new ProjectStage with field mappings
    /// </summary>
    /// <param name="projections">Field name to include mapping</param>
    /// <param name="fieldMappings">Source field to destination field mappings</param>
    public ProjectStage(Dictionary<string, bool> projections, Dictionary<string, string> fieldMappings)
    {
        _projections = new Dictionary<string, bool>(projections ?? throw new ArgumentNullException(nameof(projections)));
        _fieldMappings = new Dictionary<string, string>(fieldMappings);
    }

    /// <inheritdoc />
    public IEnumerable<Document> Execute(IEnumerable<Document> documents)
    {
        try
        {
            // Determine if this is an inclusion or exclusion projection
            var hasInclusion = _projections.Values.Any(v => v);
            var hasExclusion = _projections.Values.Any(v => !v);

            if (hasInclusion && hasExclusion)
            {
                // MongoDB doesn't allow mixing inclusion and exclusion except for _id
                // Check if the only exclusion is _id (which is allowed with inclusions)
                var inclusions = _projections.Where(p => p.Value).Select(p => p.Key).ToList();
                var exclusions = _projections.Where(p => !p.Value).Select(p => p.Key).ToList();
                
                // Allow mixing only if _id is the only exclusion field
                // OR if _id is the only inclusion field
                var onlyIdIsExcluded = exclusions.Count == 1 && exclusions[0] == "_id";
                var onlyIdIsIncluded = inclusions.Count == 1 && inclusions[0] == "_id";
                
                if (!onlyIdIsExcluded && !onlyIdIsIncluded)
                {
                    throw new AggregationStageException(StageType, 
                        "Cannot mix inclusion and exclusion projections in the same $project stage (except for _id)");
                }
            }

            var results = new List<Document>();

            foreach (var doc in documents)
            {
                var projectedDoc = new Document
                {
                    Id = doc.Id,
                    CreatedAt = doc.CreatedAt,
                    UpdatedAt = doc.UpdatedAt,
                    Version = doc.Version,
                    Data = new Dictionary<string, object?>()
                };

                if (doc.Data != null)
                {
                    if (hasInclusion)
                    {
                        // Inclusion projection - include only specified fields + _id (unless excluded)
                        foreach (var projection in _projections)
                        {
                            var fieldName = projection.Key;
                            var include = projection.Value;

                            if (include)
                            {
                                // Include this field
                                var sourceField = _fieldMappings?.GetValueOrDefault(fieldName) ?? fieldName;
                                var value = GetFieldValue(doc, sourceField);
                                projectedDoc.Data[fieldName] = value;
                            }
                        }

                        // Always include _id unless explicitly excluded (when _id: false is specified)
                        if (!_projections.TryGetValue("_id", out var idIncluded) || idIncluded)
                        {
                            projectedDoc.Data["_id"] = doc.Id;
                        }
                    }
                    else
                    {
                        // Exclusion projection - exclude specified fields
                        foreach (var kvp in doc.Data)
                        {
                            // Include field if: not in projections OR in projections with value=true
                            if (!_projections.TryGetValue(kvp.Key, out var include) || include)
                            {
                                projectedDoc.Data[kvp.Key] = kvp.Value;
                            }
                        }
                        // In exclusion mode, include _id by default unless explicitly excluded
                        if (!_projections.ContainsKey("_id"))
                        {
                            projectedDoc.Data["_id"] = doc.Id;
                        }
                        else if (!_projections["_id"])
                        {
                            // _id: false was specified, so remove it
                            projectedDoc.Data.Remove("_id");
                        }
                    }
                }

                results.Add(projectedDoc);
            }

            return results;
        }
        catch (AggregationStageException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AggregationStageException(StageType, $"Failed to execute project stage: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public Task<IEnumerable<Document>> ExecuteAsync(IEnumerable<Document> documents, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Execute(documents));
    }

    private object? GetFieldValue(Document document, string fieldPath)
    {
        if (document.Data == null) return null;

        // Handle nested paths
        var parts = fieldPath.Split('.');
        object? current = document.Data;

        foreach (var part in parts)
        {
            if (current is Dictionary<string, object?> dict && dict.TryGetValue(part, out var value))
            {
                current = value;
            }
            else if (part == "_id")
            {
                return document.Id;
            }
            else
            {
                return null;
            }
        }

        return current;
    }
}
