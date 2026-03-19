// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Storage.Geospatial;

/// <summary>
/// Manages multiple geospatial indexes across collections.
/// </summary>
public sealed class GeospatialIndexManager
{
    private readonly ConcurrentDictionary<string, IGeospatialIndex> _indexes = new();

    /// <summary>
    /// Creates a new geospatial index for a collection field.
    /// </summary>
    public IGeospatialIndex CreateIndex(string collectionName, string fieldName, double? cellSizeDegrees = null)
    {
        var key = GetIndexKey(collectionName, fieldName);
        var index = new GeospatialIndex(collectionName, fieldName, cellSizeDegrees);
        _indexes[key] = index;
        return index;
    }

    /// <summary>
    /// Gets an existing geospatial index.
    /// </summary>
    public IGeospatialIndex? GetIndex(string collectionName, string fieldName)
    {
        var key = GetIndexKey(collectionName, fieldName);
        _indexes.TryGetValue(key, out var index);
        return index;
    }

    /// <summary>
    /// Drops a geospatial index.
    /// </summary>
    public bool DropIndex(string collectionName, string fieldName)
    {
        var key = GetIndexKey(collectionName, fieldName);
        return _indexes.TryRemove(key, out _);
    }

    /// <summary>
    /// Checks if an index exists.
    /// </summary>
    public bool HasIndex(string collectionName, string fieldName)
    {
        var key = GetIndexKey(collectionName, fieldName);
        return _indexes.ContainsKey(key);
    }

    /// <summary>
    /// Gets all indexes for a collection.
    /// </summary>
    public IEnumerable<IGeospatialIndex> GetCollectionIndexes(string collectionName)
    {
        return _indexes.Values.Where(i => i.CollectionName == collectionName);
    }

    /// <summary>
    /// Indexes a document's location field.
    /// </summary>
    public void IndexDocument(string collectionName, string fieldName, Document document)
    {
        if (!_indexes.TryGetValue(GetIndexKey(collectionName, fieldName), out var index))
            return;

        if (!document.Data.TryGetValue(fieldName, out var locationData))
            return;

        var point = GeoPoint.FromObject(locationData);
        if (point.HasValue)
        {
            index.Index(document.Id, point.Value);
        }
    }

    /// <summary>
    /// Removes a document from an index.
    /// </summary>
    public void RemoveDocument(string collectionName, string fieldName, string documentId)
    {
        if (_indexes.TryGetValue(GetIndexKey(collectionName, fieldName), out var index))
        {
            index.Remove(documentId);
        }
    }

    /// <summary>
    /// Clears all indexes.
    /// </summary>
    public void ClearAll()
    {
        foreach (var index in _indexes.Values)
        {
            index.Clear();
        }
        _indexes.Clear();
    }

    /// <summary>
    /// Gets statistics for all indexes.
    /// </summary>
    public IEnumerable<GeospatialIndexStats> GetAllStats()
    {
        return _indexes.Values.Select(i => i.GetStats());
    }

    private static string GetIndexKey(string collectionName, string fieldName)
    {
        return $"{collectionName}.{fieldName}";
    }
}
