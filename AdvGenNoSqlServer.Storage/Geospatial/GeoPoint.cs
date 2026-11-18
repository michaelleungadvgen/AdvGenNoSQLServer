// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Storage.Geospatial;

/// <summary>
/// Represents a 2D geospatial point with longitude and latitude coordinates.
/// </summary>
public readonly struct GeoPoint : IEquatable<GeoPoint>
{
    /// <summary>
    /// The longitude coordinate (X axis, range: -180 to 180)
    /// </summary>
    public double Longitude { get; }

    /// <summary>
    /// The latitude coordinate (Y axis, range: -90 to 90)
    /// </summary>
    public double Latitude { get; }

    /// <summary>
    /// Creates a new GeoPoint with the specified coordinates.
    /// </summary>
    /// <param name="longitude">The longitude (-180 to 180)</param>
    /// <param name="latitude">The latitude (-90 to 90)</param>
    public GeoPoint(double longitude, double latitude)
    {
        Longitude = longitude;
        Latitude = latitude;
    }

    /// <summary>
    /// Validates that the coordinates are within valid ranges.
    /// </summary>
    public bool IsValid => Longitude >= -180 && Longitude <= 180 && Latitude >= -90 && Latitude <= 90;

    /// <summary>
    /// Earth's radius in kilometers for distance calculations.
    /// </summary>
    public const double EarthRadiusKm = 6371.0;

    /// <summary>
    /// Earth's radius in miles for distance calculations.
    /// </summary>
    public const double EarthRadiusMiles = 3959.0;

    /// <summary>
    /// Calculates the Haversine distance to another point in kilometers.
    /// </summary>
    public double DistanceTo(GeoPoint other)
    {
        return DistanceTo(other, DistanceUnit.Kilometers);
    }

    /// <summary>
    /// Calculates the Haversine distance to another point in the specified unit.
    /// </summary>
    public double DistanceTo(GeoPoint other, DistanceUnit unit)
    {
        var radius = unit == DistanceUnit.Kilometers ? EarthRadiusKm : EarthRadiusMiles;
        
        var dLat = ToRadians(other.Latitude - Latitude);
        var dLon = ToRadians(other.Longitude - Longitude);
        
        var lat1 = ToRadians(Latitude);
        var lat2 = ToRadians(other.Latitude);
        
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2) * Math.Cos(lat1) * Math.Cos(lat2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        
        return radius * c;
    }

    /// <summary>
    /// Converts degrees to radians.
    /// </summary>
    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;

    /// <summary>
    /// Creates a GeoPoint from a Document's location field.
    /// Expects field to be an array [longitude, latitude] or object { lng/lon: x, lat: y }.
    /// </summary>
    public static GeoPoint? FromObject(object? locationData)
    {
        if (locationData is System.Text.Json.JsonElement jsonElement)
        {
            return FromJsonElement(jsonElement);
        }
        
        if (locationData is Dictionary<string, object> dict)
        {
            if (dict.TryGetValue("lng", out var lng) && dict.TryGetValue("lat", out var lat))
                return new GeoPoint(Convert.ToDouble(lng), Convert.ToDouble(lat));
            if (dict.TryGetValue("lon", out var lon) && dict.TryGetValue("lat", out var lat2))
                return new GeoPoint(Convert.ToDouble(lon), Convert.ToDouble(lat2));
            if (dict.TryGetValue("longitude", out var longitude) && dict.TryGetValue("latitude", out var latitude))
                return new GeoPoint(Convert.ToDouble(longitude), Convert.ToDouble(latitude));
        }
        
        if (locationData is IEnumerable<object> array)
        {
            var items = array.ToList();
            if (items.Count >= 2)
                return new GeoPoint(Convert.ToDouble(items[0]), Convert.ToDouble(items[1]));
        }
        
        if (locationData is double[] doubleArray && doubleArray.Length >= 2)
        {
            return new GeoPoint(doubleArray[0], doubleArray[1]);
        }
        
        return null;
    }

    private static GeoPoint? FromJsonElement(System.Text.Json.JsonElement element)
    {
        if (element.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            var array = element.EnumerateArray().ToList();
            if (array.Count >= 2)
                return new GeoPoint(array[0].GetDouble(), array[1].GetDouble());
        }
        
        if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            if (element.TryGetProperty("lng", out var lng) && element.TryGetProperty("lat", out var lat))
                return new GeoPoint(lng.GetDouble(), lat.GetDouble());
            if (element.TryGetProperty("lon", out var lon) && element.TryGetProperty("lat", out var lat2))
                return new GeoPoint(lon.GetDouble(), lat2.GetDouble());
            if (element.TryGetProperty("longitude", out var longitude) && element.TryGetProperty("latitude", out var latitude))
                return new GeoPoint(longitude.GetDouble(), latitude.GetDouble());
        }
        
        return null;
    }

    /// <summary>
    /// Determines whether another point has the same coordinates within a 1e-9 tolerance.
    /// </summary>
    /// <param name="other">The point to compare with.</param>
    /// <returns>True if both coordinates match within tolerance; otherwise false.</returns>
    public bool Equals(GeoPoint other) => 
        Math.Abs(Longitude - other.Longitude) < 1e-9 && 
        Math.Abs(Latitude - other.Latitude) < 1e-9;

    /// <summary>
    /// Determines whether the object is a GeoPoint with equal coordinates.
    /// </summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns>True if the object is an equal GeoPoint; otherwise false.</returns>
    public override bool Equals(object? obj) => obj is GeoPoint other && Equals(other);

    /// <summary>
    /// Gets a hash code computed from the longitude and latitude.
    /// </summary>
    /// <returns>The hash code for this point.</returns>
    public override int GetHashCode() => HashCode.Combine(Longitude, Latitude);

    /// <summary>
    /// Returns the point formatted as "[longitude, latitude]" with six decimal places.
    /// </summary>
    /// <returns>A string representation of the coordinates.</returns>
    public override string ToString() => $"[{Longitude:F6}, {Latitude:F6}]";

    /// <summary>
    /// Determines whether two points have equal coordinates.
    /// </summary>
    /// <param name="left">The first point.</param>
    /// <param name="right">The second point.</param>
    /// <returns>True if the points are equal; otherwise false.</returns>
    public static bool operator ==(GeoPoint left, GeoPoint right) => left.Equals(right);

    /// <summary>
    /// Determines whether two points have different coordinates.
    /// </summary>
    /// <param name="left">The first point.</param>
    /// <param name="right">The second point.</param>
    /// <returns>True if the points differ; otherwise false.</returns>
    public static bool operator !=(GeoPoint left, GeoPoint right) => !left.Equals(right);
}

/// <summary>
/// Units for distance calculations.
/// </summary>
public enum DistanceUnit
{
    /// <summary>
    /// Distance measured in kilometers.
    /// </summary>
    Kilometers,

    /// <summary>
    /// Distance measured in miles.
    /// </summary>
    Miles
}

/// <summary>
/// Represents a rectangular bounding box for geospatial queries.
/// </summary>
public readonly struct GeoBoundingBox
{
    /// <summary>
    /// The minimum longitude (left edge).
    /// </summary>
    public double MinLongitude { get; }

    /// <summary>
    /// The minimum latitude (bottom edge).
    /// </summary>
    public double MinLatitude { get; }

    /// <summary>
    /// The maximum longitude (right edge).
    /// </summary>
    public double MaxLongitude { get; }

    /// <summary>
    /// The maximum latitude (top edge).
    /// </summary>
    public double MaxLatitude { get; }

    /// <summary>
    /// Creates a bounding box from explicit edge coordinates.
    /// </summary>
    /// <param name="minLongitude">The minimum longitude (left edge).</param>
    /// <param name="minLatitude">The minimum latitude (bottom edge).</param>
    /// <param name="maxLongitude">The maximum longitude (right edge).</param>
    /// <param name="maxLatitude">The maximum latitude (top edge).</param>
    public GeoBoundingBox(double minLongitude, double minLatitude, double maxLongitude, double maxLatitude)
    {
        MinLongitude = minLongitude;
        MinLatitude = minLatitude;
        MaxLongitude = maxLongitude;
        MaxLatitude = maxLatitude;
    }

    /// <summary>
    /// Creates a bounding box from its bottom-left and top-right corner points.
    /// </summary>
    /// <param name="bottomLeft">The bottom-left corner.</param>
    /// <param name="topRight">The top-right corner.</param>
    public GeoBoundingBox(GeoPoint bottomLeft, GeoPoint topRight)
    {
        MinLongitude = bottomLeft.Longitude;
        MinLatitude = bottomLeft.Latitude;
        MaxLongitude = topRight.Longitude;
        MaxLatitude = topRight.Latitude;
    }

    /// <summary>
    /// Checks if a point is within this bounding box.
    /// </summary>
    public bool Contains(GeoPoint point) =>
        point.Longitude >= MinLongitude && point.Longitude <= MaxLongitude &&
        point.Latitude >= MinLatitude && point.Latitude <= MaxLatitude;

    /// <summary>
    /// The center point of this bounding box.
    /// </summary>
    public GeoPoint Center => new(
        (MinLongitude + MaxLongitude) / 2,
        (MinLatitude + MaxLatitude) / 2
    );

    /// <summary>
    /// Validates that the box has valid coordinates.
    /// </summary>
    public bool IsValid => 
        MinLongitude <= MaxLongitude && 
        MinLatitude <= MaxLatitude &&
        MinLongitude >= -180 && MaxLongitude <= 180 &&
        MinLatitude >= -90 && MaxLatitude <= 90;

    /// <summary>
    /// Returns the box formatted as "[[minLon, minLat], [maxLon, maxLat]]".
    /// </summary>
    /// <returns>A string representation of the box corners.</returns>
    public override string ToString() => 
        $"[[{MinLongitude:F6}, {MinLatitude:F6}], [{MaxLongitude:F6}, {MaxLatitude:F6}]]";
}

/// <summary>
/// Represents a circular region for geospatial queries.
/// </summary>
public readonly struct GeoCircle
{
    /// <summary>
    /// The center point of the circle.
    /// </summary>
    public GeoPoint Center { get; }

    /// <summary>
    /// The radius of the circle.
    /// </summary>
    public double Radius { get; }

    /// <summary>
    /// The unit of the radius.
    /// </summary>
    public DistanceUnit Unit { get; }

    /// <summary>
    /// Creates a circular region with the specified center, radius, and distance unit.
    /// </summary>
    /// <param name="center">The center point of the circle.</param>
    /// <param name="radius">The radius of the circle.</param>
    /// <param name="unit">The unit of the radius (defaults to kilometers).</param>
    public GeoCircle(GeoPoint center, double radius, DistanceUnit unit = DistanceUnit.Kilometers)
    {
        Center = center;
        Radius = radius;
        Unit = unit;
    }

    /// <summary>
    /// Checks if a point is within this circle.
    /// </summary>
    public bool Contains(GeoPoint point) => Center.DistanceTo(point, Unit) <= Radius;

    /// <summary>
    /// Gets the bounding box that contains this circle.
    /// </summary>
    public GeoBoundingBox GetBoundingBox()
    {
        // Approximate degrees from distance
        // At equator: 1 degree longitude = 111.32 km, 1 degree latitude = 110.574 km
        // Adjust longitude for latitude (cosine of latitude)
        var latRadians = Center.Latitude * Math.PI / 180.0;
        var kmPerDegreeLon = 111.32 * Math.Cos(latRadians);
        var kmPerDegreeLat = 110.574;

        var radiusDegreesLon = Unit == DistanceUnit.Kilometers 
            ? Radius / kmPerDegreeLon
            : (Radius * 1.60934) / kmPerDegreeLon; // Convert miles to km first
        
        var radiusDegreesLat = Unit == DistanceUnit.Kilometers
            ? Radius / kmPerDegreeLat
            : (Radius * 1.60934) / kmPerDegreeLat;

        return new GeoBoundingBox(
            Center.Longitude - radiusDegreesLon,
            Center.Latitude - radiusDegreesLat,
            Center.Longitude + radiusDegreesLon,
            Center.Latitude + radiusDegreesLat
        );
    }

    /// <summary>
    /// Returns the circle formatted as "Circle(center, r=radius unit)".
    /// </summary>
    /// <returns>A string representation of the circle.</returns>
    public override string ToString() => $"Circle({Center}, r={Radius:F2}{(Unit == DistanceUnit.Kilometers ? "km" : "mi")})";
}

/// <summary>
/// Represents a polygon for geospatial queries.
/// </summary>
public class GeoPolygon
{
    /// <summary>
    /// The vertices of the polygon in order.
    /// </summary>
    public IReadOnlyList<GeoPoint> Vertices { get; }

    /// <summary>
    /// Creates a polygon from an ordered sequence of vertices.
    /// </summary>
    /// <param name="vertices">The polygon vertices in order.</param>
    public GeoPolygon(IEnumerable<GeoPoint> vertices)
    {
        Vertices = vertices.ToList().AsReadOnly();
    }

    /// <summary>
    /// Creates a polygon from the specified vertices.
    /// </summary>
    /// <param name="vertices">The polygon vertices in order.</param>
    public GeoPolygon(params GeoPoint[] vertices)
    {
        Vertices = vertices.ToList().AsReadOnly();
    }

    /// <summary>
    /// Checks if a point is within this polygon using the ray casting algorithm.
    /// </summary>
    public bool Contains(GeoPoint point)
    {
        if (Vertices.Count < 3) return false;

        bool inside = false;
        int j = Vertices.Count - 1;

        for (int i = 0; i < Vertices.Count; i++)
        {
            var vi = Vertices[i];
            var vj = Vertices[j];

            if (((vi.Latitude > point.Latitude) != (vj.Latitude > point.Latitude)) &&
                (point.Longitude < (vj.Longitude - vi.Longitude) * (point.Latitude - vi.Latitude) / (vj.Latitude - vi.Latitude) + vi.Longitude))
            {
                inside = !inside;
            }

            j = i;
        }

        return inside;
    }

    /// <summary>
    /// Gets the bounding box that contains this polygon.
    /// </summary>
    public GeoBoundingBox GetBoundingBox()
    {
        if (Vertices.Count == 0)
            return new GeoBoundingBox(0, 0, 0, 0);

        var minLon = Vertices.Min(v => v.Longitude);
        var maxLon = Vertices.Max(v => v.Longitude);
        var minLat = Vertices.Min(v => v.Latitude);
        var maxLat = Vertices.Max(v => v.Latitude);

        return new GeoBoundingBox(minLon, minLat, maxLon, maxLat);
    }

    /// <summary>
    /// Returns the polygon formatted as "Polygon(n vertices)".
    /// </summary>
    /// <returns>A string representation including the vertex count.</returns>
    public override string ToString() => $"Polygon({Vertices.Count} vertices)";
}
