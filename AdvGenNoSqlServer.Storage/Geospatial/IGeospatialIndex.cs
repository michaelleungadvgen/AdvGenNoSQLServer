// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Storage.Geospatial;

/// <summary>
/// Represents an entry in the geospatial index.
/// </summary>
public sealed class GeospatialIndexEntry
{
    /// <summary>
    /// The document ID.
    /// </summary>
    public string DocumentId { get; }

    /// <summary>
    /// The location point.
    /// </summary>
    public GeoPoint Location { get; }

    /// <summary>
    /// Additional data associated with this entry (optional).
    /// </summary>
    public object? Metadata { get; }

    public GeospatialIndexEntry(string documentId, GeoPoint location, object? metadata = null)
    {
        DocumentId = documentId ?? throw new ArgumentNullException(nameof(documentId));
        Location = location;
        Metadata = metadata;
    }
}

/// <summary>
/// Represents the result of a geospatial query.
/// </summary>
public sealed class GeospatialQueryResult
{
    /// <summary>
    /// The document ID.
    /// </summary>
    public string DocumentId { get; }

    /// <summary>
    /// The location point.
    /// </summary>
    public GeoPoint Location { get; }

    /// <summary>
    /// The distance from the query point (in query units).
    /// </summary>
    public double Distance { get; }

    /// <summary>
    /// Additional metadata.
    /// </summary>
    public object? Metadata { get; }

    public GeospatialQueryResult(string documentId, GeoPoint location, double distance, object? metadata = null)
    {
        DocumentId = documentId ?? throw new ArgumentNullException(nameof(documentId));
        Location = location;
        Distance = distance;
        Metadata = metadata;
    }
}

/// <summary>
/// Options for geospatial queries.
/// </summary>
public sealed class GeospatialQueryOptions
{
    /// <summary>
    /// Maximum number of results to return (0 = unlimited).
    /// </summary>
    public int Limit { get; set; } = 0;

    /// <summary>
    /// Skip this many results (for pagination).
    /// </summary>
    public int Skip { get; set; } = 0;

    /// <summary>
    /// Unit for distance calculations.
    /// </summary>
    public DistanceUnit DistanceUnit { get; set; } = DistanceUnit.Kilometers;

    /// <summary>
    /// Include the distance in results.
    /// </summary>
    public bool IncludeDistance { get; set; } = true;

    /// <summary>
    /// Minimum distance for range queries.
    /// </summary>
    public double? MinDistance { get; set; }

    /// <summary>
    /// Sort results by distance (closest first).
    /// </summary>
    public bool SortByDistance { get; set; } = true;

    /// <summary>
    /// Default query options.
    /// </summary>
    public static GeospatialQueryOptions Default => new();
}

/// <summary>
/// Statistics for a geospatial index.
/// </summary>
public sealed class GeospatialIndexStats
{
    /// <summary>
    /// Total number of indexed documents.
    /// </summary>
    public int TotalDocuments { get; set; }

    /// <summary>
    /// The bounding box covering all indexed points.
    /// </summary>
    public GeoBoundingBox? BoundingBox { get; set; }

    /// <summary>
    /// When the index was last updated.
    /// </summary>
    public DateTime LastUpdated { get; set; }

    /// <summary>
    /// The field name being indexed.
    /// </summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// The collection name.
    /// </summary>
    public string CollectionName { get; set; } = string.Empty;
}

/// <summary>
/// Interface for geospatial indexes.
/// </summary>
public interface IGeospatialIndex
{
    /// <summary>
    /// The collection name this index is for.
    /// </summary>
    string CollectionName { get; }

    /// <summary>
    /// The field name being indexed.
    /// </summary>
    string FieldName { get; }

    /// <summary>
    /// Adds or updates a document's location in the index.
    /// </summary>
    void Index(string documentId, GeoPoint location, object? metadata = null);

    /// <summary>
    /// Removes a document from the index.
    /// </summary>
    bool Remove(string documentId);

    /// <summary>
    /// Finds documents near a given point.
    /// </summary>
    IEnumerable<GeospatialQueryResult> FindNear(GeoPoint center, double maxDistance, GeospatialQueryOptions? options = null);

    /// <summary>
    /// Finds documents within a bounding box.
    /// </summary>
    IEnumerable<GeospatialIndexEntry> FindWithinBox(GeoBoundingBox box, GeospatialQueryOptions? options = null);

    /// <summary>
    /// Finds documents within a circle.
    /// </summary>
    IEnumerable<GeospatialQueryResult> FindWithinCircle(GeoCircle circle, GeospatialQueryOptions? options = null);

    /// <summary>
    /// Finds documents within a polygon.
    /// </summary>
    IEnumerable<GeospatialIndexEntry> FindWithinPolygon(GeoPolygon polygon, GeospatialQueryOptions? options = null);

    /// <summary>
    /// Gets a document's location by ID.
    /// </summary>
    GeoPoint? GetLocation(string documentId);

    /// <summary>
    /// Checks if a document is indexed.
    /// </summary>
    bool Contains(string documentId);

    /// <summary>
    /// Clears all entries from the index.
    /// </summary>
    void Clear();

    /// <summary>
    /// Gets index statistics.
    /// </summary>
    GeospatialIndexStats GetStats();

    /// <summary>
    /// Total number of indexed documents.
    /// </summary>
    int Count { get; }
}
