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

    /// <summary>
    /// The name of the collection this index belongs to.
    /// </summary>
    public string CollectionName { get; }

    /// <summary>
    /// The document field containing the indexed location data.
    /// </summary>
    public string FieldName { get; }

    /// <summary>
    /// The number of documents currently indexed.
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Creates a new geospatial index.
    /// </summary>
    public GeospatialIndex(string collectionName, string fieldName, double? cellSizeDegrees = null)
    {
        CollectionName = collectionName ?? throw new ArgumentNullException(nameof(collectionName));
        FieldName = fieldName ?? throw new ArgumentNullException(nameof(fieldName));
    }

    /// <summary>
    /// Adds or replaces the indexed location for a document.
    /// </summary>
    /// <param name="documentId">The document identifier.</param>
    /// <param name="location">The document's location point.</param>
    /// <param name="metadata">Optional metadata to store with the entry.</param>
    public void Index(string documentId, GeoPoint location, object? metadata = null)
    {
        if (string.IsNullOrEmpty(documentId))
            throw new ArgumentNullException(nameof(documentId));

        var entry = new GeospatialIndexEntry(documentId, location, metadata);
        _entries[documentId] = entry;
    }

    /// <summary>
    /// Removes a document's entry from the index.
    /// </summary>
    /// <param name="documentId">The document identifier.</param>
    /// <returns>True if the entry was removed; false if it was not indexed.</returns>
    public bool Remove(string documentId)
    {
        if (string.IsNullOrEmpty(documentId))
            return false;

        return _entries.TryRemove(documentId, out _);
    }

    /// <summary>
    /// Finds documents within a maximum distance of a center point, optionally sorted by distance and paginated.
    /// </summary>
    /// <param name="center">The point to search around.</param>
    /// <param name="maxDistance">The maximum distance in the options' distance unit.</param>
    /// <param name="options">Query options (distance unit, min distance, sorting, pagination); defaults when null.</param>
    /// <returns>Matching documents with their locations and optional distances.</returns>
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

    /// <summary>
    /// Finds indexed entries whose locations fall inside a bounding box.
    /// </summary>
    /// <param name="box">The bounding box to search within.</param>
    /// <param name="options">Query options (pagination); defaults when null.</param>
    /// <returns>The matching index entries.</returns>
    public IEnumerable<GeospatialIndexEntry> FindWithinBox(GeoBoundingBox box, GeospatialQueryOptions? options = null)
    {
        options ??= GeospatialQueryOptions.Default;

        var results = _entries.Values
            .Where(e => box.Contains(e.Location))
            .ToList();

        return ApplyPagination(results, options);
    }

    /// <summary>
    /// Finds documents whose locations fall inside a circular region, optionally sorted by distance.
    /// </summary>
    /// <param name="circle">The circular region to search within.</param>
    /// <param name="options">Query options (distance inclusion, sorting, pagination); defaults when null.</param>
    /// <returns>Matching documents with their locations and optional distances.</returns>
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

    /// <summary>
    /// Finds indexed entries whose locations fall inside a polygon.
    /// </summary>
    /// <param name="polygon">The polygon to search within.</param>
    /// <param name="options">Query options (pagination); defaults when null.</param>
    /// <returns>The matching index entries.</returns>
    public IEnumerable<GeospatialIndexEntry> FindWithinPolygon(GeoPolygon polygon, GeospatialQueryOptions? options = null)
    {
        options ??= GeospatialQueryOptions.Default;
        
        var results = _entries.Values
            .Where(e => polygon.Contains(e.Location))
            .ToList();

        return ApplyPagination(results, options);
    }

    /// <summary>
    /// Gets the indexed location of a document.
    /// </summary>
    /// <param name="documentId">The document identifier.</param>
    /// <returns>The indexed point, or null if the document is not indexed.</returns>
    public GeoPoint? GetLocation(string documentId)
    {
        if (_entries.TryGetValue(documentId, out var entry))
            return entry.Location;
        return null;
    }

    /// <summary>
    /// Checks whether a document is present in the index.
    /// </summary>
    /// <param name="documentId">The document identifier.</param>
    /// <returns>True if the document is indexed; otherwise false.</returns>
    public bool Contains(string documentId) => _entries.ContainsKey(documentId);

    /// <summary>
    /// Removes all entries from the index.
    /// </summary>
    public void Clear()
    {
        _entries.Clear();
    }

    /// <summary>
    /// Computes index statistics, including the document count and overall bounding box, in a single pass.
    /// </summary>
    /// <returns>Statistics describing the current index contents.</returns>
    public GeospatialIndexStats GetStats()
    {
        GeoBoundingBox? boundingBox = null;
        
        if (!_entries.IsEmpty)
        {
            // Bolt Performance Optimization:
            // Avoid calling _entries.Values which copies data and locks the ConcurrentDictionary.
            // Avoid .Select().ToList() which causes O(N) memory allocations.
            // Avoid multiple .Min() and .Max() calls which cause multiple O(N) passes.
            // We do a single pass over the dictionary directly.
            double minLon = double.MaxValue;
            double minLat = double.MaxValue;
            double maxLon = double.MinValue;
            double maxLat = double.MinValue;

            foreach (var kvp in _entries)
            {
                var loc = kvp.Value.Location;
                if (loc.Longitude < minLon) minLon = loc.Longitude;
                if (loc.Latitude < minLat) minLat = loc.Latitude;
                if (loc.Longitude > maxLon) maxLon = loc.Longitude;
                if (loc.Latitude > maxLat) maxLat = loc.Latitude;
            }

            // Only create if we actually processed elements
            if (minLon <= maxLon && minLat <= maxLat)
            {
                boundingBox = new GeoBoundingBox(minLon, minLat, maxLon, maxLat);
            }
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
