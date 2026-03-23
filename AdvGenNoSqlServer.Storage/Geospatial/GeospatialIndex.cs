// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;

namespace AdvGenNoSqlServer.Storage.Geospatial;

/// <summary>
/// A geospatial index for efficient 2D spatial queries.
/// Uses a dictionary-based storage with linear scanning for simplicity and reliability.
/// </summary>
public sealed class GeospatialIndex : IGeospatialIndex
{
    private readonly ConcurrentDictionary<string, GeospatialIndexEntry> _entries = new();

    public string CollectionName { get; }
    public string FieldName { get; }
    public int Count => _entries.Count;

    /// <summary>
    /// Creates a new geospatial index.
    /// </summary>
    public GeospatialIndex(string collectionName, string fieldName, double? cellSizeDegrees = null)
    {
        CollectionName = collectionName ?? throw new ArgumentNullException(nameof(collectionName));
        FieldName = fieldName ?? throw new ArgumentNullException(nameof(fieldName));
    }

    public void Index(string documentId, GeoPoint location, object? metadata = null)
    {
        if (string.IsNullOrEmpty(documentId))
            throw new ArgumentNullException(nameof(documentId));

        var entry = new GeospatialIndexEntry(documentId, location, metadata);
        _entries[documentId] = entry;
    }

    public bool Remove(string documentId)
    {
        if (string.IsNullOrEmpty(documentId))
            return false;

        return _entries.TryRemove(documentId, out _);
    }

    public IEnumerable<GeospatialQueryResult> FindNear(GeoPoint center, double maxDistance, GeospatialQueryOptions? options = null)
    {
        options ??= GeospatialQueryOptions.Default;

        var results = new List<GeospatialQueryResult>();

        foreach (var entry in _entries.Values)
        {
            var distance = center.DistanceTo(entry.Location, options.DistanceUnit);

            // Check min distance
            if (options.MinDistance.HasValue && distance < options.MinDistance.Value)
                continue;

            if (distance <= maxDistance)
            {
                results.Add(new GeospatialQueryResult(
                    entry.DocumentId,
                    entry.Location,
                    options.IncludeDistance ? distance : 0,
                    entry.Metadata));
            }
        }

        // Sort by distance if requested
        if (options.SortByDistance)
        {
            results = results.OrderBy(r => r.Distance).ToList();
        }

        // Apply skip and limit
        return ApplyPagination(results, options);
    }

    public IEnumerable<GeospatialIndexEntry> FindWithinBox(GeoBoundingBox box, GeospatialQueryOptions? options = null)
    {
        options ??= GeospatialQueryOptions.Default;

        var results = _entries.Values
            .Where(e => box.Contains(e.Location))
            .ToList();

        return ApplyPagination(results, options);
    }

    public IEnumerable<GeospatialQueryResult> FindWithinCircle(GeoCircle circle, GeospatialQueryOptions? options = null)
    {
        options ??= GeospatialQueryOptions.Default;

        var results = new List<GeospatialQueryResult>();

        foreach (var entry in _entries.Values)
        {
            var distance = circle.Center.DistanceTo(entry.Location, circle.Unit);

            if (distance <= circle.Radius)
            {
                results.Add(new GeospatialQueryResult(
                    entry.DocumentId,
                    entry.Location,
                    options.IncludeDistance ? distance : 0,
                    entry.Metadata));
            }
        }

        if (options.SortByDistance)
        {
            results = results.OrderBy(r => r.Distance).ToList();
        }

        return ApplyPagination(results, options);
    }

    public IEnumerable<GeospatialIndexEntry> FindWithinPolygon(GeoPolygon polygon, GeospatialQueryOptions? options = null)
    {
        options ??= GeospatialQueryOptions.Default;

        var results = _entries.Values
            .Where(e => polygon.Contains(e.Location))
            .ToList();

        return ApplyPagination(results, options);
    }

    public GeoPoint? GetLocation(string documentId)
    {
        if (_entries.TryGetValue(documentId, out var entry))
            return entry.Location;
        return null;
    }

    public bool Contains(string documentId) => _entries.ContainsKey(documentId);

    public void Clear()
    {
        _entries.Clear();
    }

    public GeospatialIndexStats GetStats()
    {
        GeoBoundingBox? boundingBox = null;

        if (_entries.Count > 0)
        {
            var locations = _entries.Values.Select(e => e.Location).ToList();
            boundingBox = new GeoBoundingBox(
                locations.Min(l => l.Longitude),
                locations.Min(l => l.Latitude),
                locations.Max(l => l.Longitude),
                locations.Max(l => l.Latitude)
            );
        }

        return new GeospatialIndexStats
        {
            CollectionName = CollectionName,
            FieldName = FieldName,
            TotalDocuments = _entries.Count,
            BoundingBox = boundingBox,
            LastUpdated = DateTime.UtcNow
        };
    }

    private static IEnumerable<T> ApplyPagination<T>(List<T> results, GeospatialQueryOptions options)
    {
        if (options.Skip > 0)
        {
            results = results.Skip(options.Skip).ToList();
        }

        if (options.Limit > 0)
        {
            results = results.Take(options.Limit).ToList();
        }

        return results;
    }
}
