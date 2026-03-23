// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Storage.Geospatial;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Storage;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for geospatial indexing functionality.
/// </summary>
public class GeospatialIndexTests
{
    #region GeoPoint Tests

    [Fact]
    public void GeoPoint_Constructor_ShouldSetCoordinates()
    {
        var point = new GeoPoint(144.9631, -37.8136);

        Assert.Equal(144.9631, point.Longitude);
        Assert.Equal(-37.8136, point.Latitude);
    }

    [Fact]
    public void GeoPoint_IsValid_ValidCoordinates_ReturnsTrue()
    {
        var point = new GeoPoint(144.9631, -37.8136);
        Assert.True(point.IsValid);
    }

    [Fact]
    public void GeoPoint_IsValid_InvalidLongitude_ReturnsFalse()
    {
        var point = new GeoPoint(200, 0);
        Assert.False(point.IsValid);
    }

    [Fact]
    public void GeoPoint_IsValid_InvalidLatitude_ReturnsFalse()
    {
        var point = new GeoPoint(0, 100);
        Assert.False(point.IsValid);
    }

    [Fact]
    public void GeoPoint_DistanceTo_SamePoint_ReturnsZero()
    {
        var point = new GeoPoint(144.9631, -37.8136);
        var distance = point.DistanceTo(point);

        Assert.Equal(0, distance, 6);
    }

    [Fact]
    public void GeoPoint_DistanceTo_KnownDistance_IsAccurate()
    {
        // Melbourne to Sydney is approximately 713 km
        var melbourne = new GeoPoint(144.9631, -37.8136);
        var sydney = new GeoPoint(151.2093, -33.8688);

        var distance = melbourne.DistanceTo(sydney);

        Assert.True(distance > 700 && distance < 730, $"Expected ~713km, got {distance}km");
    }

    [Fact]
    public void GeoPoint_DistanceTo_Miles_ConvertsCorrectly()
    {
        var melbourne = new GeoPoint(144.9631, -37.8136);
        var sydney = new GeoPoint(151.2093, -33.8688);

        var distanceKm = melbourne.DistanceTo(sydney, DistanceUnit.Kilometers);
        var distanceMiles = melbourne.DistanceTo(sydney, DistanceUnit.Miles);

        var expectedMiles = distanceKm * 0.621371;
        Assert.Equal(expectedMiles, distanceMiles, 1);
    }

    [Fact]
    public void GeoPoint_Equality_SameCoordinates_AreEqual()
    {
        var point1 = new GeoPoint(144.9631, -37.8136);
        var point2 = new GeoPoint(144.9631, -37.8136);

        Assert.Equal(point1, point2);
        Assert.True(point1 == point2);
    }

    [Fact]
    public void GeoPoint_Equality_DifferentCoordinates_AreNotEqual()
    {
        var point1 = new GeoPoint(144.9631, -37.8136);
        var point2 = new GeoPoint(151.2093, -33.8688);

        Assert.NotEqual(point1, point2);
        Assert.True(point1 != point2);
    }

    [Fact]
    public void GeoPoint_FromObject_DoubleArray_ReturnsPoint()
    {
        var array = new[] { 144.9631, -37.8136 };
        var point = GeoPoint.FromObject(array);

        Assert.NotNull(point);
        Assert.Equal(144.9631, point.Value.Longitude);
        Assert.Equal(-37.8136, point.Value.Latitude);
    }

    [Fact]
    public void GeoPoint_FromObject_ObjectList_ReturnsPoint()
    {
        var list = new List<object> { 144.9631, -37.8136 };
        var point = GeoPoint.FromObject(list);

        Assert.NotNull(point);
        Assert.Equal(144.9631, point.Value.Longitude);
        Assert.Equal(-37.8136, point.Value.Latitude);
    }

    [Fact]
    public void GeoPoint_FromObject_DictionaryWithLngLat_ReturnsPoint()
    {
        var dict = new Dictionary<string, object>
        {
            ["lng"] = 144.9631,
            ["lat"] = -37.8136
        };
        var point = GeoPoint.FromObject(dict);

        Assert.NotNull(point);
        Assert.Equal(144.9631, point.Value.Longitude);
        Assert.Equal(-37.8136, point.Value.Latitude);
    }

    [Fact]
    public void GeoPoint_FromObject_DictionaryWithLonLat_ReturnsPoint()
    {
        var dict = new Dictionary<string, object>
        {
            ["lon"] = 144.9631,
            ["lat"] = -37.8136
        };
        var point = GeoPoint.FromObject(dict);

        Assert.NotNull(point);
        Assert.Equal(144.9631, point.Value.Longitude);
    }

    [Fact]
    public void GeoPoint_FromObject_DictionaryWithLongitudeLatitude_ReturnsPoint()
    {
        var dict = new Dictionary<string, object>
        {
            ["longitude"] = 144.9631,
            ["latitude"] = -37.8136
        };
        var point = GeoPoint.FromObject(dict);

        Assert.NotNull(point);
        Assert.Equal(144.9631, point.Value.Longitude);
        Assert.Equal(-37.8136, point.Value.Latitude);
    }

    [Fact]
    public void GeoPoint_FromObject_Null_ReturnsNull()
    {
        var point = GeoPoint.FromObject(null);
        Assert.Null(point);
    }

    [Fact]
    public void GeoPoint_FromObject_InvalidType_ReturnsNull()
    {
        var point = GeoPoint.FromObject("not a location");
        Assert.Null(point);
    }

    [Fact]
    public void GeoPoint_ToString_FormatsCorrectly()
    {
        var point = new GeoPoint(144.9631, -37.8136);
        var str = point.ToString();

        Assert.Contains("144.9631", str);
        Assert.Contains("-37.8136", str);
    }

    #endregion

    #region GeoBoundingBox Tests

    [Fact]
    public void GeoBoundingBox_Contains_PointInside_ReturnsTrue()
    {
        var box = new GeoBoundingBox(144.0, -38.0, 145.0, -37.0);
        var point = new GeoPoint(144.5, -37.5);

        Assert.True(box.Contains(point));
    }

    [Fact]
    public void GeoBoundingBox_Contains_PointOutside_ReturnsFalse()
    {
        var box = new GeoBoundingBox(144.0, -38.0, 145.0, -37.0);
        var point = new GeoPoint(150.0, -37.5);

        Assert.False(box.Contains(point));
    }

    [Fact]
    public void GeoBoundingBox_Center_CalculatesCorrectly()
    {
        var box = new GeoBoundingBox(144.0, -38.0, 146.0, -36.0);
        var center = box.Center;

        Assert.Equal(145.0, center.Longitude);
        Assert.Equal(-37.0, center.Latitude);
    }

    [Fact]
    public void GeoBoundingBox_IsValid_ValidBox_ReturnsTrue()
    {
        var box = new GeoBoundingBox(144.0, -38.0, 145.0, -37.0);
        Assert.True(box.IsValid);
    }

    [Fact]
    public void GeoBoundingBox_IsValid_InvalidBox_ReturnsFalse()
    {
        var box = new GeoBoundingBox(145.0, -38.0, 144.0, -37.0);
        Assert.False(box.IsValid);
    }

    [Fact]
    public void GeoBoundingBox_Constructor_FromPoints_CreatesCorrectBox()
    {
        var bottomLeft = new GeoPoint(144.0, -38.0);
        var topRight = new GeoPoint(145.0, -37.0);
        var box = new GeoBoundingBox(bottomLeft, topRight);

        Assert.Equal(144.0, box.MinLongitude);
        Assert.Equal(-38.0, box.MinLatitude);
        Assert.Equal(145.0, box.MaxLongitude);
        Assert.Equal(-37.0, box.MaxLatitude);
    }

    #endregion

    #region GeoCircle Tests

    [Fact]
    public void GeoCircle_Contains_PointInside_ReturnsTrue()
    {
        var center = new GeoPoint(144.9631, -37.8136);
        var circle = new GeoCircle(center, 15); // 15km radius
        var point = new GeoPoint(144.9631, -37.7236); // About 10km north

        Assert.True(circle.Contains(point));
    }

    [Fact]
    public void GeoCircle_Contains_PointOutside_ReturnsFalse()
    {
        var center = new GeoPoint(144.9631, -37.8136);
        var circle = new GeoCircle(center, 10); // 10km radius
        var point = new GeoPoint(151.2093, -33.8688); // Sydney, far away

        Assert.False(circle.Contains(point));
    }

    [Fact]
    public void GeoCircle_Contains_Miles_ConvertsCorrectly()
    {
        var center = new GeoPoint(144.9631, -37.8136);
        var circleKm = new GeoCircle(center, 15, DistanceUnit.Kilometers);
        var circleMi = new GeoCircle(center, 9.32, DistanceUnit.Miles); // ~15km in miles

        var point = new GeoPoint(144.9631, -37.7236); // ~10km north

        Assert.True(circleKm.Contains(point));
        Assert.True(circleMi.Contains(point));
    }

    #endregion

    #region GeoPolygon Tests

    [Fact]
    public void GeoPolygon_Contains_PointInside_ReturnsTrue()
    {
        // Simple square polygon
        var polygon = new GeoPolygon(
            new GeoPoint(144.0, -38.0),
            new GeoPoint(145.0, -38.0),
            new GeoPoint(145.0, -37.0),
            new GeoPoint(144.0, -37.0),
            new GeoPoint(144.0, -38.0) // Close the polygon
        );

        var point = new GeoPoint(144.5, -37.5);

        Assert.True(polygon.Contains(point));
    }

    [Fact]
    public void GeoPolygon_Contains_PointOutside_ReturnsFalse()
    {
        var polygon = new GeoPolygon(
            new GeoPoint(144.0, -38.0),
            new GeoPoint(145.0, -38.0),
            new GeoPoint(145.0, -37.0),
            new GeoPoint(144.0, -37.0)
        );

        var point = new GeoPoint(146.0, -36.0);

        Assert.False(polygon.Contains(point));
    }

    [Fact]
    public void GeoPolygon_Contains_TooFewVertices_ReturnsFalse()
    {
        var polygon = new GeoPolygon(
            new GeoPoint(144.0, -38.0),
            new GeoPoint(145.0, -37.0)
        );

        var point = new GeoPoint(144.5, -37.5);

        Assert.False(polygon.Contains(point));
    }

    [Fact]
    public void GeoPolygon_GetBoundingBox_CalculatesCorrectly()
    {
        var polygon = new GeoPolygon(
            new GeoPoint(144.0, -38.0),
            new GeoPoint(145.0, -38.0),
            new GeoPoint(145.0, -37.0),
            new GeoPoint(144.0, -37.0)
        );

        var box = polygon.GetBoundingBox();

        Assert.Equal(144.0, box.MinLongitude);
        Assert.Equal(-38.0, box.MinLatitude);
        Assert.Equal(145.0, box.MaxLongitude);
        Assert.Equal(-37.0, box.MaxLatitude);
    }

    [Fact]
    public void GeoPolygon_GetBoundingBox_EmptyPolygon_ReturnsZeroBox()
    {
        var polygon = new GeoPolygon(Array.Empty<GeoPoint>());
        var box = polygon.GetBoundingBox();

        Assert.Equal(0, box.MinLongitude);
        Assert.Equal(0, box.MinLatitude);
        Assert.Equal(0, box.MaxLongitude);
        Assert.Equal(0, box.MaxLatitude);
    }

    #endregion

    #region GeospatialIndex Tests

    [Fact]
    public void GeospatialIndex_Constructor_SetsProperties()
    {
        var index = new GeospatialIndex("places", "location");

        Assert.Equal("places", index.CollectionName);
        Assert.Equal("location", index.FieldName);
        Assert.Equal(0, index.Count);
    }

    [Fact]
    public void GeospatialIndex_Index_AddsDocument()
    {
        var index = new GeospatialIndex("places", "location");
        var point = new GeoPoint(144.9631, -37.8136);

        index.Index("doc1", point);

        Assert.Equal(1, index.Count);
        Assert.True(index.Contains("doc1"));
    }

    [Fact]
    public void GeospatialIndex_Index_UpdatesExistingDocument()
    {
        var index = new GeospatialIndex("places", "location");
        var point1 = new GeoPoint(144.9631, -37.8136);
        var point2 = new GeoPoint(151.2093, -33.8688);

        index.Index("doc1", point1);
        index.Index("doc1", point2);

        Assert.Equal(1, index.Count);
        var location = index.GetLocation("doc1");
        Assert.Equal(point2, location);
    }

    [Fact]
    public void GeospatialIndex_Remove_ExistingDocument_ReturnsTrue()
    {
        var index = new GeospatialIndex("places", "location");
        var point = new GeoPoint(144.9631, -37.8136);

        index.Index("doc1", point);
        var removed = index.Remove("doc1");

        Assert.True(removed);
        Assert.Equal(0, index.Count);
        Assert.False(index.Contains("doc1"));
    }

    [Fact]
    public void GeospatialIndex_Remove_NonExistingDocument_ReturnsFalse()
    {
        var index = new GeospatialIndex("places", "location");

        var removed = index.Remove("doc1");

        Assert.False(removed);
    }

    [Fact]
    public void GeospatialIndex_GetLocation_ExistingDocument_ReturnsPoint()
    {
        var index = new GeospatialIndex("places", "location");
        var point = new GeoPoint(144.9631, -37.8136);

        index.Index("doc1", point);
        var location = index.GetLocation("doc1");

        Assert.Equal(point, location);
    }

    [Fact]
    public void GeospatialIndex_GetLocation_NonExistingDocument_ReturnsNull()
    {
        var index = new GeospatialIndex("places", "location");

        var location = index.GetLocation("doc1");

        Assert.Null(location);
    }

    [Fact]
    public void GeospatialIndex_Clear_RemovesAllDocuments()
    {
        var index = new GeospatialIndex("places", "location");

        index.Index("doc1", new GeoPoint(144.9631, -37.8136));
        index.Index("doc2", new GeoPoint(151.2093, -33.8688));
        index.Clear();

        Assert.Equal(0, index.Count);
        Assert.False(index.Contains("doc1"));
        Assert.False(index.Contains("doc2"));
    }

    [Fact]
    public void GeospatialIndex_FindNear_ReturnsNearbyDocuments()
    {
        var index = new GeospatialIndex("places", "location");
        var center = new GeoPoint(144.9631, -37.8136); // Melbourne

        // Index some points
        index.Index("melbourne", center);
        index.Index("sydney", new GeoPoint(151.2093, -33.8688)); // ~713km away
        index.Index("nearby", new GeoPoint(144.9731, -37.8036)); // ~1.4km away

        var results = index.FindNear(center, 10).ToList(); // 10km radius

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.DocumentId == "melbourne");
        Assert.Contains(results, r => r.DocumentId == "nearby");
        Assert.DoesNotContain(results, r => r.DocumentId == "sydney");
    }

    [Fact]
    public void GeospatialIndex_FindNear_SortsByDistance()
    {
        var index = new GeospatialIndex("places", "location");
        var center = new GeoPoint(144.9631, -37.8136);

        index.Index("near", new GeoPoint(144.9731, -37.8036)); // ~1.4km
        index.Index("far", new GeoPoint(144.9631, -37.7236)); // ~10km
        index.Index("medium", new GeoPoint(144.9631, -37.7636)); // ~5.5km

        var results = index.FindNear(center, 15).ToList();

        Assert.Equal("near", results[0].DocumentId);
        Assert.Equal("medium", results[1].DocumentId);
        Assert.Equal("far", results[2].DocumentId);
    }

    [Fact]
    public void GeospatialIndex_FindNear_WithLimit_ReturnsLimitedResults()
    {
        var index = new GeospatialIndex("places", "location");
        var center = new GeoPoint(144.9631, -37.8136);

        index.Index("doc1", new GeoPoint(144.9631, -37.8136));
        index.Index("doc2", new GeoPoint(144.9632, -37.8136));
        index.Index("doc3", new GeoPoint(144.9633, -37.8136));

        var options = new GeospatialQueryOptions { Limit = 2 };
        var results = index.FindNear(center, 100, options).ToList();

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void GeospatialIndex_FindNear_WithMinDistance_ExcludesCloseDocuments()
    {
        var index = new GeospatialIndex("places", "location");
        var center = new GeoPoint(144.9631, -37.8136);

        index.Index("atCenter", center);
        index.Index("nearby", new GeoPoint(144.9731, -37.8036)); // ~1.4km

        var options = new GeospatialQueryOptions { MinDistance = 0.5 };
        var results = index.FindNear(center, 10, options).ToList();

        Assert.DoesNotContain(results, r => r.DocumentId == "atCenter");
        Assert.Contains(results, r => r.DocumentId == "nearby");
    }

    [Fact]
    public void GeospatialIndex_FindWithinBox_ReturnsDocumentsInBox()
    {
        var index = new GeospatialIndex("places", "location");
        var box = new GeoBoundingBox(144.0, -38.0, 145.0, -37.0);

        index.Index("inside", new GeoPoint(144.5, -37.5));
        index.Index("outside", new GeoPoint(150.0, -33.0));

        var results = index.FindWithinBox(box).ToList();

        Assert.Single(results);
        Assert.Equal("inside", results[0].DocumentId);
    }

    [Fact]
    public void GeospatialIndex_FindWithinCircle_ReturnsDocumentsInCircle()
    {
        var index = new GeospatialIndex("places", "location");
        var center = new GeoPoint(144.9631, -37.8136);
        var circle = new GeoCircle(center, 15); // 15km radius

        index.Index("inside", new GeoPoint(144.9631, -37.7236)); // ~10km north
        index.Index("outside", new GeoPoint(151.2093, -33.8688)); // Sydney

        var results = index.FindWithinCircle(circle).ToList();

        Assert.Single(results);
        Assert.Equal("inside", results[0].DocumentId);
    }

    [Fact]
    public void GeospatialIndex_FindWithinPolygon_ReturnsDocumentsInPolygon()
    {
        var index = new GeospatialIndex("places", "location");
        var polygon = new GeoPolygon(
            new GeoPoint(144.0, -38.0),
            new GeoPoint(145.0, -38.0),
            new GeoPoint(145.0, -37.0),
            new GeoPoint(144.0, -37.0)
        );

        index.Index("inside", new GeoPoint(144.5, -37.5));
        index.Index("outside", new GeoPoint(150.0, -33.0));

        var results = index.FindWithinPolygon(polygon).ToList();

        Assert.Single(results);
        Assert.Equal("inside", results[0].DocumentId);
    }

    [Fact]
    public void GeospatialIndex_GetStats_ReturnsCorrectStatistics()
    {
        var index = new GeospatialIndex("places", "location");

        index.Index("doc1", new GeoPoint(144.0, -38.0));
        index.Index("doc2", new GeoPoint(145.0, -37.0));

        var stats = index.GetStats();

        Assert.Equal("places", stats.CollectionName);
        Assert.Equal("location", stats.FieldName);
        Assert.Equal(2, stats.TotalDocuments);
        Assert.NotNull(stats.BoundingBox);
        Assert.True(stats.LastUpdated <= DateTime.UtcNow);
    }

    [Fact]
    public void GeospatialIndex_Concurrent_Index_IsThreadSafe()
    {
        var index = new GeospatialIndex("places", "location");
        var tasks = new List<Task>();

        for (int i = 0; i < 100; i++)
        {
            var id = i;
            tasks.Add(Task.Run(() =>
            {
                index.Index($"doc{id}", new GeoPoint(144.0 + i * 0.001, -37.0 - i * 0.001));
            }));
        }

        Task.WaitAll(tasks.ToArray());

        Assert.Equal(100, index.Count);
    }

    #endregion

    #region GeospatialIndexManager Tests

    [Fact]
    public void GeospatialIndexManager_CreateIndex_ReturnsIndex()
    {
        var manager = new GeospatialIndexManager();

        var index = manager.CreateIndex("places", "location");

        Assert.NotNull(index);
        Assert.Equal("places", index.CollectionName);
        Assert.Equal("location", index.FieldName);
    }

    [Fact]
    public void GeospatialIndexManager_GetIndex_ExistingIndex_ReturnsIndex()
    {
        var manager = new GeospatialIndexManager();
        manager.CreateIndex("places", "location");

        var index = manager.GetIndex("places", "location");

        Assert.NotNull(index);
    }

    [Fact]
    public void GeospatialIndexManager_GetIndex_NonExistingIndex_ReturnsNull()
    {
        var manager = new GeospatialIndexManager();

        var index = manager.GetIndex("places", "location");

        Assert.Null(index);
    }

    [Fact]
    public void GeospatialIndexManager_HasIndex_ExistingIndex_ReturnsTrue()
    {
        var manager = new GeospatialIndexManager();
        manager.CreateIndex("places", "location");

        Assert.True(manager.HasIndex("places", "location"));
    }

    [Fact]
    public void GeospatialIndexManager_DropIndex_ExistingIndex_ReturnsTrue()
    {
        var manager = new GeospatialIndexManager();
        manager.CreateIndex("places", "location");

        var dropped = manager.DropIndex("places", "location");

        Assert.True(dropped);
        Assert.False(manager.HasIndex("places", "location"));
    }

    [Fact]
    public void GeospatialIndexManager_GetCollectionIndexes_ReturnsOnlyCollectionIndexes()
    {
        var manager = new GeospatialIndexManager();
        manager.CreateIndex("places", "location");
        manager.CreateIndex("places", "geo");
        manager.CreateIndex("other", "location");

        var indexes = manager.GetCollectionIndexes("places").ToList();

        Assert.Equal(2, indexes.Count);
        Assert.All(indexes, i => Assert.Equal("places", i.CollectionName));
    }

    [Fact]
    public void GeospatialIndexManager_IndexDocument_IndexesInRelevantIndexes()
    {
        var manager = new GeospatialIndexManager();
        manager.CreateIndex("places", "location");

        var document = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object>
            {
                ["location"] = new[] { 144.9631, -37.8136 },
                ["name"] = "Melbourne"
            }
        };

        manager.IndexDocument("places", "location", document);

        var index = manager.GetIndex("places", "location");
        Assert.True(index?.Contains("doc1"));
    }

    [Fact]
    public void GeospatialIndexManager_RemoveDocument_RemovesFromIndex()
    {
        var manager = new GeospatialIndexManager();
        manager.CreateIndex("places", "location");

        var document = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object>
            {
                ["location"] = new[] { 144.9631, -37.8136 }
            }
        };

        manager.IndexDocument("places", "location", document);
        manager.RemoveDocument("places", "location", "doc1");

        var index = manager.GetIndex("places", "location");
        Assert.False(index?.Contains("doc1"));
    }

    [Fact]
    public void GeospatialIndexManager_ClearAll_RemovesAllIndexes()
    {
        var manager = new GeospatialIndexManager();
        manager.CreateIndex("places", "location");
        manager.CreateIndex("other", "location");

        manager.ClearAll();

        Assert.Null(manager.GetIndex("places", "location"));
        Assert.Null(manager.GetIndex("other", "location"));
    }

    #endregion

    #region GeospatialDocumentStore Tests

    [Fact]
    public async Task GeospatialDocumentStore_InsertAsync_IndexesDocument()
    {
        var innerStore = new DocumentStore();
        var geoStore = new GeospatialDocumentStore(innerStore);
        geoStore.CreateGeospatialIndex("places", "location");

        var document = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object>
            {
                ["location"] = new[] { 144.9631, -37.8136 },
                ["name"] = "Melbourne"
            }
        };

        await geoStore.InsertAsync("places", document);

        var index = geoStore.IndexManager.GetIndex("places", "location");
        Assert.True(index?.Contains("doc1"));
    }

    [Fact]
    public async Task GeospatialDocumentStore_DeleteAsync_RemovesFromIndex()
    {
        var innerStore = new DocumentStore();
        var geoStore = new GeospatialDocumentStore(innerStore);
        geoStore.CreateGeospatialIndex("places", "location");

        var document = new Document
        {
            Id = "doc1",
            Data = new Dictionary<string, object>
            {
                ["location"] = new[] { 144.9631, -37.8136 }
            }
        };

        await geoStore.InsertAsync("places", document);
        await geoStore.DeleteAsync("places", "doc1");

        var index = geoStore.IndexManager.GetIndex("places", "location");
        Assert.False(index?.Contains("doc1"));
    }

    [Fact]
    public async Task GeospatialDocumentStore_FindNear_ReturnsNearbyDocuments()
    {
        var innerStore = new DocumentStore();
        var geoStore = new GeospatialDocumentStore(innerStore);
        geoStore.CreateGeospatialIndex("places", "location");

        // Insert some places
        await geoStore.InsertAsync("places", new Document
        {
            Id = "melbourne",
            Data = new Dictionary<string, object>
            {
                ["location"] = new[] { 144.9631, -37.8136 },
                ["name"] = "Melbourne"
            }
        });

        await geoStore.InsertAsync("places", new Document
        {
            Id = "sydney",
            Data = new Dictionary<string, object>
            {
                ["location"] = new[] { 151.2093, -33.8688 },
                ["name"] = "Sydney"
            }
        });

        var results = geoStore.FindNear("places", "location", new GeoPoint(144.9631, -37.8136), 100).ToList();

        Assert.Single(results);
        Assert.Equal("melbourne", results[0].DocumentId);
    }

    [Fact]
    public void GeospatialDocumentStore_FindNear_NoIndex_ThrowsException()
    {
        var innerStore = new DocumentStore();
        var geoStore = new GeospatialDocumentStore(innerStore);
        // Don't create index

        Assert.Throws<InvalidOperationException>(() =>
        {
            geoStore.FindNear("places", "location", new GeoPoint(144.9631, -37.8136), 100).ToList();
        });
    }

    [Fact]
    public async Task GeospatialDocumentStore_DropCollectionAsync_RemovesIndexes()
    {
        var innerStore = new DocumentStore();
        var geoStore = new GeospatialDocumentStore(innerStore);
        geoStore.CreateGeospatialIndex("places", "location");

        await innerStore.CreateCollectionAsync("places");
        await geoStore.DropCollectionAsync("places");

        Assert.False(geoStore.IndexManager.HasIndex("places", "location"));
    }

    #endregion

    #region Extension Method Tests

    [Fact]
    public void DocumentStore_WithGeospatialSupport_ReturnsGeospatialDocumentStore()
    {
        var innerStore = new DocumentStore();

        var geoStore = innerStore.WithGeospatialSupport();

        Assert.IsType<GeospatialDocumentStore>(geoStore);
        Assert.Equal(innerStore, geoStore.InnerStore);
    }

    #endregion
}
