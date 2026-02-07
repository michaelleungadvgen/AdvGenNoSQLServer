// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections;
using System.Text.Json;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Query.Models;

namespace AdvGenNoSqlServer.Query.Filtering;

/// <summary>
/// Implementation of the filter engine for evaluating documents against query criteria
/// </summary>
public class FilterEngine : IFilterEngine
{
    /// <inheritdoc />
    public bool Matches(Document document, QueryFilter? filter)
    {
        if (filter == null || filter.Conditions.Count == 0)
            return true;

        return EvaluateConditions(document, filter.Conditions);
    }

    /// <inheritdoc />
    public IEnumerable<Document> Filter(IEnumerable<Document> documents, QueryFilter? filter)
    {
        if (filter == null || filter.Conditions.Count == 0)
            return documents;

        return documents.Where(d => Matches(d, filter));
    }

    /// <inheritdoc />
    public object? GetFieldValue(Document document, string fieldPath)
    {
        if (document.Data == null)
            return null;

        var pathParts = fieldPath.Split('.');
        var current = (object)document.Data;

        foreach (var part in pathParts)
        {
            if (current is Dictionary<string, object> dict)
            {
                if (!dict.TryGetValue(part, out current))
                    return null;
            }
            else if (current is JsonElement jsonElement)
            {
                current = GetValueFromJsonElement(jsonElement, part);
                if (current == null)
                    return null;
            }
            else
            {
                return null;
            }
        }

        return current;
    }

    private bool EvaluateConditions(Document document, Dictionary<string, object> conditions)
    {
        foreach (var condition in conditions)
        {
            if (!EvaluateCondition(document, condition.Key, condition.Value))
                return false;
        }
        return true;
    }

    private bool EvaluateCondition(Document document, string key, object value)
    {
        // Handle logical operators
        if (key.StartsWith('$'))
        {
            return EvaluateLogicalOperator(document, key, value);
        }

        // Handle field conditions
        var fieldValue = GetFieldValue(document, key);

        // Simple equality check
        if (value is not Dictionary<string, object> operators)
        {
            return AreEqual(fieldValue, value);
        }

        // Handle comparison operators
        return EvaluateComparisonOperators(fieldValue, operators);
    }

    private bool EvaluateLogicalOperator(Document document, string operatorName, object value)
    {
        switch (operatorName.ToLowerInvariant())
        {
            case "$and":
                return EvaluateAndOperator(document, value);

            case "$or":
                return EvaluateOrOperator(document, value);

            case "$not":
                return EvaluateNotOperator(document, value);

            case "$nor":
                return !EvaluateOrOperator(document, value);

            default:
                throw new FilterEvaluationException($"Unknown logical operator: {operatorName}");
        }
    }

    private bool EvaluateAndOperator(Document document, object value)
    {
        if (value is not List<object> conditions)
        {
            throw new FilterEvaluationException("$and operator requires an array of conditions");
        }

        foreach (var condition in conditions)
        {
            var dict = ConvertToDictionary(condition);
            if (dict == null)
            {
                throw new FilterEvaluationException("Each $and condition must be an object");
            }

            if (!EvaluateConditions(document, dict))
                return false;
        }

        return true;
    }

    private bool EvaluateOrOperator(Document document, object value)
    {
        if (value is not List<object> conditions)
        {
            throw new FilterEvaluationException("$or operator requires an array of conditions");
        }

        foreach (var condition in conditions)
        {
            var dict = ConvertToDictionary(condition);
            if (dict == null)
            {
                throw new FilterEvaluationException("Each $or condition must be an object");
            }

            if (EvaluateConditions(document, dict))
                return true;
        }

        return false;
    }

    private bool EvaluateNotOperator(Document document, object value)
    {
        if (value is not Dictionary<string, object> dict)
        {
            throw new FilterEvaluationException("$not operator requires an object");
        }

        return !EvaluateConditions(document, dict);
    }

    private bool EvaluateComparisonOperators(object? fieldValue, Dictionary<string, object> operators)
    {
        foreach (var op in operators)
        {
            if (!EvaluateComparisonOperator(fieldValue, op.Key, op.Value))
                return false;
        }
        return true;
    }

    private bool EvaluateComparisonOperator(object? fieldValue, string operatorName, object operatorValue)
    {
        switch (operatorName.ToLowerInvariant())
        {
            case "$eq":
                return AreEqual(fieldValue, operatorValue);

            case "$ne":
                return !AreEqual(fieldValue, operatorValue);

            case "$gt":
                return CompareValues(fieldValue, operatorValue) > 0;

            case "$gte":
                return CompareValues(fieldValue, operatorValue) >= 0;

            case "$lt":
                return CompareValues(fieldValue, operatorValue) < 0;

            case "$lte":
                return CompareValues(fieldValue, operatorValue) <= 0;

            case "$in":
                return EvaluateInOperator(fieldValue, operatorValue);

            case "$nin":
                return !EvaluateInOperator(fieldValue, operatorValue);

            case "$exists":
                var shouldExist = Convert.ToBoolean(operatorValue);
                return shouldExist ? fieldValue != null : fieldValue == null;

            case "$regex":
                return EvaluateRegexOperator(fieldValue, operatorValue);

            default:
                throw new FilterEvaluationException($"Unknown comparison operator: {operatorName}");
        }
    }

    private bool EvaluateInOperator(object? fieldValue, object operatorValue)
    {
        if (operatorValue is not List<object> values)
        {
            throw new FilterEvaluationException("$in operator requires an array of values");
        }

        foreach (var value in values)
        {
            if (AreEqual(fieldValue, value))
                return true;
        }

        return false;
    }

    private bool EvaluateRegexOperator(object? fieldValue, object pattern)
    {
        if (fieldValue == null)
            return false;

        var fieldString = fieldValue.ToString();
        var patternString = pattern.ToString();

        if (fieldString == null || patternString == null)
            return false;

        // Simple wildcard matching (* and ?)
        if (patternString.Contains('*') || patternString.Contains('?'))
        {
            var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(patternString)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(fieldString, regexPattern, 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        return fieldString.Contains(patternString, StringComparison.OrdinalIgnoreCase);
    }

    private static bool AreEqual(object? a, object? b)
    {
        if (a == null && b == null)
            return true;
        if (a == null || b == null)
            return false;

        // Handle numeric comparisons
        if (IsNumeric(a) && IsNumeric(b))
        {
            return Convert.ToDouble(a) == Convert.ToDouble(b);
        }

        // Handle string comparisons
        if (a is string strA && b is string strB)
        {
            return strA.Equals(strB, StringComparison.Ordinal);
        }

        return a.Equals(b);
    }

    private static int CompareValues(object? a, object? b)
    {
        if (a == null && b == null)
            return 0;
        if (a == null)
            return -1;
        if (b == null)
            return 1;

        // Handle numeric comparisons
        if (IsNumeric(a) && IsNumeric(b))
        {
            var numA = Convert.ToDouble(a);
            var numB = Convert.ToDouble(b);
            return numA.CompareTo(numB);
        }

        // Handle string comparisons
        if (a is string strA && b is string strB)
        {
            return string.Compare(strA, strB, StringComparison.Ordinal);
        }

        // Handle DateTime comparisons
        if (a is DateTime dtA && b is DateTime dtB)
        {
            return dtA.CompareTo(dtB);
        }

        // Default to string comparison
        return string.Compare(a.ToString(), b.ToString(), StringComparison.Ordinal);
    }

    private static bool IsNumeric(object value)
    {
        return value is sbyte or byte or short or ushort or int or uint or long or ulong 
            or float or double or decimal;
    }

    private static Dictionary<string, object>? ConvertToDictionary(object value)
    {
        if (value is Dictionary<string, object> dict)
            return dict;

        if (value is IDictionary idict)
        {
            var result = new Dictionary<string, object>();
            foreach (var key in idict.Keys)
            {
                if (key is string strKey)
                {
                    result[strKey] = idict[key]!;
                }
            }
            return result;
        }

        return null;
    }

    private static object? GetValueFromJsonElement(System.Text.Json.JsonElement element, string propertyName)
    {
        if (element.ValueKind != System.Text.Json.JsonValueKind.Object)
            return null;

        if (element.TryGetProperty(propertyName, out var property))
        {
            return property.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => property.GetString(),
                System.Text.Json.JsonValueKind.Number => property.TryGetInt64(out var l) ? l : property.GetDouble(),
                System.Text.Json.JsonValueKind.True => true,
                System.Text.Json.JsonValueKind.False => false,
                System.Text.Json.JsonValueKind.Object => property,
                System.Text.Json.JsonValueKind.Array => property,
                System.Text.Json.JsonValueKind.Null => null,
                _ => property.ToString()
            };
        }

        return null;
    }
}
