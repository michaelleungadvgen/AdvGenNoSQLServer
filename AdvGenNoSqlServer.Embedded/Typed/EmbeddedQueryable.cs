// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Linq.Expressions;
using System.Reflection;
using AdvGenNoSqlServer.Query.Models;

namespace AdvGenNoSqlServer.Embedded.Typed;

/// <summary>
/// Fluent query builder. Accumulates AND-ed predicates, sort keys, and paging, then executes
/// through the translate-or-fallback path. If any predicate is untranslatable, ALL filtering
/// happens in memory (correctness first) but sort/skip/limit still apply afterward.
/// </summary>
internal sealed class EmbeddedQueryable<T> : IEmbeddedQueryable<T> where T : class
{
    private readonly EmbeddedCollection<T> _collection;
    private readonly DocumentMapper<T> _mapper;
    private readonly List<Expression<Func<T, bool>>> _predicates = new();
    private readonly List<(LambdaExpression Selector, bool Descending)> _sorts = new();
    private int? _skip;
    private int? _limit;

    public EmbeddedQueryable(EmbeddedCollection<T> collection, DocumentMapper<T> mapper)
    {
        _collection = collection;
        _mapper = mapper;
    }

    public IEmbeddedQueryable<T> Where(Expression<Func<T, bool>> predicate)
    {
        _predicates.Add(predicate);
        return this;
    }

    public IEmbeddedQueryable<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        _sorts.Add((keySelector, false));
        return this;
    }

    public IEmbeddedQueryable<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        _sorts.Add((keySelector, true));
        return this;
    }

    public IEmbeddedQueryable<T> Skip(int count) { _skip = count; return this; }
    public IEmbeddedQueryable<T> Limit(int count) { _limit = count; return this; }

    public async Task<List<T>> ToListAsync(CancellationToken ct = default)
    {
        var translated = _predicates.Select(ExpressionTranslator.TryTranslate).ToList();
        bool allTranslatable = translated.All(f => f != null);

        if (allTranslatable)
        {
            QueryFilter? combined = null;
            foreach (var f in translated)
                combined = combined == null ? f : combined.And(f!);

            var sort = _sorts.Count == 0 ? null : _sorts
                .Select(s => new SortField { FieldName = MemberName(s.Selector), Direction = s.Descending ? SortDirection.Descending : SortDirection.Ascending })
                .ToList();

            var docs = await _collection.ExecuteQueryAsync(combined, sort, _skip, _limit, ct);
            return docs.Select(_mapper.ToEntity).ToList();
        }

        // Fallback: in-memory filtering with the compiled predicates; sort + page afterwards.
        _collection.Diagnostics.IncrementFallback();
        var all = await _collection.Store.GetAllAsync(_collection.Name, ct);
        IEnumerable<T> seq = all.Select(_mapper.ToEntity);
        foreach (var p in _predicates)
            seq = seq.Where(p.Compile());

        seq = ApplyInMemorySort(seq);
        if (_skip.HasValue) seq = seq.Skip(_skip.Value);
        if (_limit.HasValue) seq = seq.Take(_limit.Value);
        return seq.ToList();
    }

    public List<T> ToList() => ToListAsync().GetAwaiter().GetResult();

    public T First() => ToList().First();
    public T? FirstOrDefault() => ToList().FirstOrDefault();
    public int Count() => ToList().Count;

    private IEnumerable<T> ApplyInMemorySort(IEnumerable<T> seq)
    {
        if (_sorts.Count == 0) return seq;
        IOrderedEnumerable<T>? ordered = null;
        foreach (var (selector, descending) in _sorts)
        {
            var f = CompileSelector(selector);
            ordered = ordered == null
                ? (descending ? seq.OrderByDescending(f, Comparer<object?>.Default) : seq.OrderBy(f, Comparer<object?>.Default))
                : (descending ? ordered.ThenByDescending(f, Comparer<object?>.Default) : ordered.ThenBy(f, Comparer<object?>.Default));
        }
        return ordered ?? seq;
    }

    private static Func<T, object?> CompileSelector(LambdaExpression selector)
    {
        var param = selector.Parameters[0];
        var body = Expression.Convert(selector.Body, typeof(object));
        return Expression.Lambda<Func<T, object?>>(body, param).Compile();
    }

    private static string MemberName(LambdaExpression selector)
    {
        var body = selector.Body;
        while (body is UnaryExpression u) body = u.Operand;
        if (body is MemberExpression m && m.Member is PropertyInfo)
            return m.Member.Name;
        throw new ArgumentException("OrderBy expression must be a simple property access");
    }
}
