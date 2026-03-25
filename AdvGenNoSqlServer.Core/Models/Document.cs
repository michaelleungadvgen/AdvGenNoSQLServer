// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AdvGenNoSqlServer.Core.Models;

public class Document
{
    public required string Id { get; set; }
    public Dictionary<string, object>? Data { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public long Version { get; set; }

    /// <summary>
    /// Creates a deep clone of this document.
    /// </summary>
    public Document Clone()
    {
        var cloned = new Document
        {
            Id = this.Id,
            CreatedAt = this.CreatedAt,
            UpdatedAt = this.UpdatedAt,
            Version = this.Version
        };

        if (this.Data != null)
        {
            cloned.Data = new Dictionary<string, object>();
            foreach (var kvp in this.Data)
            {
                cloned.Data[kvp.Key] = DeepCloneValue(kvp.Value);
            }
        }

        return cloned;
    }

    /// <summary>
    /// Deep clones a value.
    /// </summary>
    private object? DeepCloneValue(object? value)
    {
        if (value == null) return null;

        switch (value)
        {
            case Dictionary<string, object> dict:
                var newDict = new Dictionary<string, object>();
                foreach (var kvp in dict)
                {
                    newDict[kvp.Key] = DeepCloneValue(kvp.Value)!;
                }
                return newDict;
            case IList list:
                // Handle any list type
                var newList = new List<object?>();
                foreach (var item in list)
                {
                    newList.Add(DeepCloneValue(item));
                }
                return newList;
            case string s:
                return s; // strings are immutable
            case DateTime dt:
                return dt; // DateTime is a value type
            case ICloneable cloneable:
                return cloneable.Clone();
            default:
                // For primitive types and other value types, return as-is
                if (value.GetType().IsValueType)
                    return value;
                // For reference types, try to return as-is (may be shallow)
                return value;
        }
    }
}