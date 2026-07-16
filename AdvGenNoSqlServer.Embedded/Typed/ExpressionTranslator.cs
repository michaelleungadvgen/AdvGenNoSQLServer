// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using AdvGenNoSqlServer.Query.Models;

namespace AdvGenNoSqlServer.Embedded.Typed;

/// <summary>
/// Translates a LINQ predicate into a <see cref="QueryFilter"/> where the supported subset
/// allows it, returning null otherwise so the caller can fall back to in-memory evaluation.
/// Supported: property comparisons (== != &lt; &lt;= &gt; &gt;=) against a constant/closure,
/// &amp;&amp; and ||, bare/negated bool properties, and <c>collection.Contains(x.Prop)</c> → $in.
/// String methods (Contains/StartsWith/EndsWith) are intentionally NOT translated because the
/// filter engine's regex is case-insensitive and would diverge from the compiled predicate.
/// </summary>
internal static class ExpressionTranslator
{
    public static QueryFilter? TryTranslate<T>(Expression<Func<T, bool>> predicate)
    {
        try
        {
            return Translate(predicate.Body, predicate.Parameters[0]);
        }
        catch
        {
            return null;
        }
    }

    private static QueryFilter? Translate(Expression expr, ParameterExpression param)
    {
        switch (expr)
        {
            case BinaryExpression b when b.NodeType == ExpressionType.AndAlso:
            {
                var l = Translate(b.Left, param);
                var r = Translate(b.Right, param);
                return (l == null || r == null) ? null : l.And(r);
            }
            case BinaryExpression b when b.NodeType == ExpressionType.OrElse:
            {
                var l = Translate(b.Left, param);
                var r = Translate(b.Right, param);
                return (l == null || r == null) ? null : l.Or(r);
            }
            case BinaryExpression b when IsComparison(b.NodeType):
                return TranslateComparison(b, param);

            case UnaryExpression u when u.NodeType == ExpressionType.Not:
                // !x.Bool  ->  Bool == false
                if (TryGetMemberField(u.Operand, param, out var negField))
                    return QueryFilter.Eq(negField, false);
                return null;

            case MemberExpression m when m.Type == typeof(bool) && IsParameterMember(m, param):
                // bare x.Bool  ->  Bool == true
                return QueryFilter.Eq(m.Member.Name, true);

            case MethodCallExpression call:
                return TranslateMethodCall(call, param);

            default:
                return null;
        }
    }

    private static QueryFilter? TranslateComparison(BinaryExpression b, ParameterExpression param)
    {
        // Identify which side is the parameter member and which is the constant.
        if (TryGetMemberField(b.Left, param, out var fieldL) && !ReferencesParameter(b.Right, param))
        {
            var value = Normalize(Evaluate(b.Right));
            return BuildComparison(fieldL, b.NodeType, value);
        }
        if (TryGetMemberField(b.Right, param, out var fieldR) && !ReferencesParameter(b.Left, param))
        {
            var value = Normalize(Evaluate(b.Left));
            return BuildComparison(fieldR, Flip(b.NodeType), value);
        }
        return null;
    }

    private static QueryFilter? BuildComparison(string field, ExpressionType op, object? value)
    {
        if (value == null && op != ExpressionType.Equal && op != ExpressionType.NotEqual)
            return null;
        return op switch
        {
            ExpressionType.Equal => QueryFilter.Eq(field, value!),
            ExpressionType.NotEqual => QueryFilter.Ne(field, value!),
            ExpressionType.GreaterThan => QueryFilter.Gt(field, value!),
            ExpressionType.GreaterThanOrEqual => QueryFilter.Gte(field, value!),
            ExpressionType.LessThan => QueryFilter.Lt(field, value!),
            ExpressionType.LessThanOrEqual => QueryFilter.Lte(field, value!),
            _ => null
        };
    }

    private static QueryFilter? TranslateMethodCall(MethodCallExpression call, ParameterExpression param)
    {
        // collection.Contains(x.Prop)  ->  $in
        if (call.Method.Name == "Contains")
        {
            // Instance form: list.Contains(member)
            if (call.Object != null && call.Arguments.Count == 1 &&
                TryGetMemberField(call.Arguments[0], param, out var field1) &&
                !ReferencesParameter(call.Object, param))
            {
                return BuildIn(field1, Evaluate(call.Object));
            }
            // Static Enumerable.Contains(source, member)
            if (call.Object == null && call.Arguments.Count == 2 &&
                TryGetMemberField(call.Arguments[1], param, out var field2) &&
                !ReferencesParameter(call.Arguments[0], param))
            {
                return BuildIn(field2, Evaluate(call.Arguments[0]));
            }
        }
        return null;
    }

    private static QueryFilter? BuildIn(string field, object? collection)
    {
        if (collection is not IEnumerable e || collection is string)
            return null;
        var values = new List<object>();
        foreach (var item in e)
            values.Add(Normalize(item)!);
        return QueryFilter.In(field, values);
    }

    private static bool IsComparison(ExpressionType t) => t is
        ExpressionType.Equal or ExpressionType.NotEqual or
        ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual or
        ExpressionType.LessThan or ExpressionType.LessThanOrEqual;

    private static ExpressionType Flip(ExpressionType t) => t switch
    {
        ExpressionType.GreaterThan => ExpressionType.LessThan,
        ExpressionType.GreaterThanOrEqual => ExpressionType.LessThanOrEqual,
        ExpressionType.LessThan => ExpressionType.GreaterThan,
        ExpressionType.LessThanOrEqual => ExpressionType.GreaterThanOrEqual,
        _ => t
    };

    private static bool TryGetMemberField(Expression expr, ParameterExpression param, out string field)
    {
        // Unwrap conversions (e.g. enum/nullable boxing).
        while (expr is UnaryExpression u && (u.NodeType == ExpressionType.Convert || u.NodeType == ExpressionType.ConvertChecked))
            expr = u.Operand;

        if (expr is MemberExpression m && m.Expression == param && m.Member is PropertyInfo)
        {
            field = m.Member.Name;
            return true;
        }
        field = string.Empty;
        return false;
    }

    private static bool IsParameterMember(MemberExpression m, ParameterExpression param)
        => m.Expression == param && m.Member is PropertyInfo;

    private static bool ReferencesParameter(Expression expr, ParameterExpression param)
    {
        bool found = false;
        new ParameterFinder(param, () => found = true).Visit(expr);
        return found;
    }

    private static object? Evaluate(Expression expr)
    {
        if (expr is ConstantExpression c) return c.Value;
        var lambda = Expression.Lambda(Expression.Convert(expr, typeof(object)));
        return lambda.Compile().DynamicInvoke();
    }

    private static object? Normalize(object? value) => value switch
    {
        null => null,
        byte or sbyte or short or ushort or int or uint or long or ulong => Convert.ToInt64(value),
        float or double or decimal => Convert.ToDouble(value),
        bool b => b,
        string s => s,
        Enum e => e.ToString(),
        _ => value
    };

    private sealed class ParameterFinder : ExpressionVisitor
    {
        private readonly ParameterExpression _param;
        private readonly Action _onFound;
        public ParameterFinder(ParameterExpression param, Action onFound) { _param = param; _onFound = onFound; }
        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (node == _param) _onFound();
            return base.VisitParameter(node);
        }
    }
}
