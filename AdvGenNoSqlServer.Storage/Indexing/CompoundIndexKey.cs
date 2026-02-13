// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Text;

namespace AdvGenNoSqlServer.Storage.Indexing;

/// <summary>
/// Represents a compound (multi-field) index key that supports lexicographical comparison.
/// Used for creating indexes on multiple fields with proper ordering.
/// </summary>
public readonly struct CompoundIndexKey : IComparable<CompoundIndexKey>, IEquatable<CompoundIndexKey>
{
    private readonly object?[] _values;

    /// <summary>
    /// Gets the field values that make up this compound key
    /// </summary>
    public IReadOnlyList<object?> Values => _values;

    /// <summary>
    /// Gets the number of fields in this compound key
    /// </summary>
    public int FieldCount => _values.Length;

    /// <summary>
    /// Creates a new compound index key from the specified field values
    /// </summary>
    /// <param name="values">The field values in order of index definition</param>
    public CompoundIndexKey(params object?[] values)
    {
        _values = values ?? Array.Empty<object?>();
    }

    /// <summary>
    /// Creates a new compound index key from a list of field values
    /// </summary>
    /// <param name="values">The field values in order of index definition</param>
    public CompoundIndexKey(IEnumerable<object?> values)
    {
        _values = values?.ToArray() ?? Array.Empty<object?>();
    }

    /// <summary>
    /// Compares this compound key with another using lexicographical ordering.
    /// Fields are compared in order until a difference is found.
    /// </summary>
    /// <param name="other">The other compound key to compare against</param>
    /// <returns>
    /// Negative if this key is less than other, zero if equal, positive if greater
    /// </returns>
    public int CompareTo(CompoundIndexKey other)
    {
        int minLength = Math.Min(_values.Length, other._values.Length);

        for (int i = 0; i < minLength; i++)
        {
            int comparison = CompareValues(_values[i], other._values[i]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        // If all compared fields are equal, the key with fewer fields is "less"
        return _values.Length.CompareTo(other._values.Length);
    }

    /// <summary>
    /// Compares two values of potentially different types
    /// </summary>
    private static int CompareValues(object? a, object? b)
    {
        // Handle null cases
        if (a == null && b == null) return 0;
        if (a == null) return -1;
        if (b == null) return 1;

        // If types match and implement IComparable, use that
        if (a.GetType() == b.GetType())
        {
            if (a is IComparable comparableA)
            {
                return comparableA.CompareTo(b);
            }
        }

        // Try numeric comparison
        if (IsNumeric(a) && IsNumeric(b))
        {
            return Convert.ToDouble(a).CompareTo(Convert.ToDouble(b));
        }

        // Fall back to string comparison
        return string.Compare(a.ToString(), b.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Checks if a value is a numeric type
    /// </summary>
    private static bool IsNumeric(object value)
    {
        return value is sbyte or byte or short or ushort or int or uint or long or ulong 
            or float or double or decimal;
    }

    /// <summary>
    /// Determines whether this compound key equals another
    /// </summary>
    public bool Equals(CompoundIndexKey other)
    {
        if (_values.Length != other._values.Length)
            return false;

        for (int i = 0; i < _values.Length; i++)
        {
            if (!Equals(_values[i], other._values[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Determines whether this compound key equals another object
    /// </summary>
    public override bool Equals(object? obj)
    {
        return obj is CompoundIndexKey other && Equals(other);
    }

    /// <summary>
    /// Gets the hash code for this compound key
    /// </summary>
    public override int GetHashCode()
    {
        HashCode hash = new();
        foreach (var value in _values)
        {
            hash.Add(value);
        }
        return hash.ToHashCode();
    }

    /// <summary>
    /// Returns a string representation of this compound key
    /// </summary>
    public override string ToString()
    {
        var sb = new StringBuilder("(");
        for (int i = 0; i < _values.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(_values[i]?.ToString() ?? "null");
        }
        sb.Append(')');
        return sb.ToString();
    }

    /// <summary>
    /// Gets the value at the specified field index
    /// </summary>
    /// <param name="index">The field index</param>
    /// <returns>The field value</returns>
    public object? this[int index] => _values[index];

    /// <summary>
    /// Equality operator
    /// </summary>
    public static bool operator ==(CompoundIndexKey left, CompoundIndexKey right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Inequality operator
    /// </summary>
    public static bool operator !=(CompoundIndexKey left, CompoundIndexKey right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    /// Less than operator
    /// </summary>
    public static bool operator <(CompoundIndexKey left, CompoundIndexKey right)
    {
        return left.CompareTo(right) < 0;
    }

    /// <summary>
    /// Greater than operator
    /// </summary>
    public static bool operator >(CompoundIndexKey left, CompoundIndexKey right)
    {
        return left.CompareTo(right) > 0;
    }

    /// <summary>
    /// Less than or equal operator
    /// </summary>
    public static bool operator <=(CompoundIndexKey left, CompoundIndexKey right)
    {
        return left.CompareTo(right) <= 0;
    }

    /// <summary>
    /// Greater than or equal operator
    /// </summary>
    public static bool operator >=(CompoundIndexKey left, CompoundIndexKey right)
    {
        return left.CompareTo(right) >= 0;
    }
}

/// <summary>
/// Extension methods for creating compound keys
/// </summary>
public static class CompoundKeyExtensions
{
    /// <summary>
    /// Creates a compound key from individual values
    /// </summary>
    public static CompoundIndexKey ToCompoundKey(this object?[] values)
    {
        return new CompoundIndexKey(values);
    }

    /// <summary>
    /// Creates a compound key from a tuple of two values
    /// </summary>
    public static CompoundIndexKey ToCompoundKey<T1, T2>((T1, T2) tuple)
    {
        return new CompoundIndexKey(tuple.Item1, tuple.Item2);
    }

    /// <summary>
    /// Creates a compound key from a tuple of three values
    /// </summary>
    public static CompoundIndexKey ToCompoundKey<T1, T2, T3>((T1, T2, T3) tuple)
    {
        return new CompoundIndexKey(tuple.Item1, tuple.Item2, tuple.Item3);
    }
}
