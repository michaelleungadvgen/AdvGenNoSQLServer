// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Storage.Revisions
{
    /// <summary>
    /// Compares documents to detect changes.
    /// </summary>
    public interface IDocumentComparer
    {
        /// <summary>
        /// Compares two documents and returns the differences.
        /// </summary>
        DocumentComparisonResult Compare(Document? oldDocument, Document? newDocument);

        /// <summary>
        /// Determines if two documents are equal.
        /// </summary>
        bool AreEqual(Document? doc1, Document? doc2);
    }

    /// <summary>
    /// Default implementation of document comparison using JSON serialization.
    /// </summary>
    public class DocumentComparer : IDocumentComparer
    {
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// Initializes a new instance of the <see cref="DocumentComparer"/> class.
        /// </summary>
        public DocumentComparer(JsonSerializerOptions? jsonOptions = null)
        {
            _jsonOptions = jsonOptions ?? new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };
        }

        /// <summary>
        /// Compares two documents and returns the differences.
        /// </summary>
        public DocumentComparisonResult Compare(Document? oldDocument, Document? newDocument)
        {
            // Handle null cases
            if (oldDocument == null && newDocument == null)
                return DocumentComparisonResult.Equal();

            if (oldDocument == null)
            {
                var addedFields = GetAllFieldPaths(newDocument!.Data);
                return DocumentComparisonResult.Different(addedFields: addedFields);
            }

            if (newDocument == null)
            {
                var removedFields = GetAllFieldPaths(oldDocument.Data);
                return DocumentComparisonResult.Different(removedFields: removedFields);
            }

            // Compare JSON data
            var oldJson = JsonSerializer.Serialize(oldDocument.Data, _jsonOptions);
            var newJson = JsonSerializer.Serialize(newDocument.Data, _jsonOptions);

            if (oldJson == newJson)
                return DocumentComparisonResult.Equal();

            // Deep comparison to find changed fields
            var (changed, added, removed) = CompareDictionaries(oldDocument.Data, newDocument.Data);

            return DocumentComparisonResult.Different(changed, added, removed);
        }

        /// <summary>
        /// Determines if two documents are equal.
        /// </summary>
        public bool AreEqual(Document? doc1, Document? doc2)
        {
            if (doc1 == null && doc2 == null)
                return true;

            if (doc1 == null || doc2 == null)
                return false;

            var oldJson = JsonSerializer.Serialize(doc1.Data, _jsonOptions);
            var newJson = JsonSerializer.Serialize(doc2.Data, _jsonOptions);

            return oldJson == newJson;
        }

        /// <summary>
        /// Recursively compares two dictionaries.
        /// </summary>
        private (List<string> changed, List<string> added, List<string> removed) CompareDictionaries(
            Dictionary<string, object>? oldDict, Dictionary<string, object>? newDict, string path = "")
        {
            var changed = new List<string>();
            var added = new List<string>();
            var removed = new List<string>();

            // Handle null cases
            if (oldDict == null && newDict == null)
                return (changed, added, removed);

            if (oldDict == null)
            {
                added.AddRange(GetAllFieldPaths(newDict, path));
                return (changed, added, removed);
            }

            if (newDict == null)
            {
                removed.AddRange(GetAllFieldPaths(oldDict, path));
                return (changed, added, removed);
            }

            var currentPath = string.IsNullOrEmpty(path) ? "" : path + ".";

            // Find added and changed properties
            foreach (var newProp in newDict)
            {
                var propPath = currentPath + newProp.Key;

                if (!oldDict.TryGetValue(newProp.Key, out var oldValue))
                {
                    added.Add(propPath);
                }
                else
                {
                    var (subChanged, subAdded, subRemoved) = CompareValues(oldValue, newProp.Value, propPath);
                    changed.AddRange(subChanged);
                    added.AddRange(subAdded);
                    removed.AddRange(subRemoved);
                }
            }

            // Find removed properties
            foreach (var oldProp in oldDict)
            {
                if (!newDict.ContainsKey(oldProp.Key))
                {
                    removed.Add(currentPath + oldProp.Key);
                }
            }

            return (changed, added, removed);
        }

        private (List<string> changed, List<string> added, List<string> removed) CompareValues(
            object? oldValue, object? newValue, string path)
        {
            var changed = new List<string>();
            var added = new List<string>();
            var removed = new List<string>();

            // Handle nulls
            if (oldValue == null && newValue == null)
                return (changed, added, removed);

            if (oldValue == null)
            {
                added.Add(path);
                return (changed, added, removed);
            }

            if (newValue == null)
            {
                changed.Add(path);
                return (changed, added, removed);
            }

            // If types differ, it's a change
            if (oldValue.GetType() != newValue.GetType())
            {
                changed.Add(path);
                return (changed, added, removed);
            }

            // Compare dictionaries
            if (oldValue is Dictionary<string, object> oldDict && newValue is Dictionary<string, object> newDict)
            {
                return CompareDictionaries(oldDict, newDict, path);
            }

            // Compare lists/arrays
            if (oldValue is List<object> oldList && newValue is List<object> newList)
            {
                if (oldList.Count != newList.Count)
                {
                    changed.Add(path);
                    return (changed, added, removed);
                }

                for (int i = 0; i < oldList.Count; i++)
                {
                    var (subChanged, subAdded, subRemoved) = CompareValues(oldList[i], newList[i], $"{path}[{i}]");
                    changed.AddRange(subChanged);
                    added.AddRange(subAdded);
                    removed.AddRange(subRemoved);
                }
                return (changed, added, removed);
            }

            // Compare primitive values by serializing
            var oldJson = JsonSerializer.Serialize(oldValue, _jsonOptions);
            var newJson = JsonSerializer.Serialize(newValue, _jsonOptions);

            if (oldJson != newJson)
            {
                changed.Add(path);
            }

            return (changed, added, removed);
        }

        private List<string> GetAllFieldPaths(Dictionary<string, object>? dict, string prefix = "")
        {
            var paths = new List<string>();

            if (dict == null)
                return paths;

            var currentPath = string.IsNullOrEmpty(prefix) ? "" : prefix + ".";

            foreach (var kvp in dict)
            {
                var path = currentPath + kvp.Key;
                paths.Add(path);

                if (kvp.Value is Dictionary<string, object> nestedDict)
                {
                    paths.AddRange(GetAllFieldPaths(nestedDict, path));
                }
            }

            return paths;
        }
    }
}
