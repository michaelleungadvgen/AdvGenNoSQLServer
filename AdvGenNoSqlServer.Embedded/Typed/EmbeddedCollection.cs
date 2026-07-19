// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Linq.Expressions;
using System.Reflection;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Query.Execution;
using AdvGenNoSqlServer.Query.Models;

namespace AdvGenNoSqlServer.Embedded.Typed;

/// <summary>Typed collection implementation composing the mapper, translator, and document store.</summary>
internal sealed class EmbeddedCollection<T> : IEmbeddedCollection<T> where T : class
{
    private readonly EmbeddedDocumentStore _store;
    private readonly QueryExecutor _executor;
    private readonly DocumentMapper<T> _mapper;
    private readonly EmbeddedDiagnostics _diagnostics;

    public EmbeddedCollection(string name, EmbeddedDocumentStore store, QueryExecutor executor,
        EmbeddedDatabaseOptions options, EmbeddedDiagnostics diagnostics)
    {
        Name = name;
        _store = store;
        _executor = executor;
        _mapper = new DocumentMapper<T>(options.SerializerOptions);
        _diagnostics = diagnostics;
    }

    public string Name { get; }

    // --- writes ---

    public async Task<string> InsertAsync(T entity, CancellationToken ct = default)
    {
        var doc = _mapper.ToDocument(entity);
        var stored = await _store.InsertAsync(Name, doc, ct);
        _mapper.SetId(entity, stored.Id);
        return stored.Id;
    }

    public string Insert(T entity) => InsertAsync(entity).GetAwaiter().GetResult();

    public int InsertBulk(IEnumerable<T> entities)
    {
        int count = 0;
        foreach (var e in entities) { Insert(e); count++; }
        return count;
    }

    public async Task<bool> UpdateAsync(T entity, CancellationToken ct = default)
    {
        var doc = _mapper.ToDocument(entity);
        try { await _store.UpdateAsync(Name, doc, ct); return true; }
        catch (Core.Abstractions.DocumentNotFoundException) { return false; }
        catch (Core.Abstractions.CollectionNotFoundException) { return false; }
    }

    public bool Update(T entity) => UpdateAsync(entity).GetAwaiter().GetResult();

    public async Task<bool> UpsertAsync(T entity, CancellationToken ct = default)
    {
        var id = _mapper.GetId(entity);
        if (!string.IsNullOrEmpty(id) && await _store.ExistsAsync(Name, id, ct))
        {
            await _store.UpdateAsync(Name, _mapper.ToDocument(entity), ct);
            return false; // updated existing
        }
        await InsertAsync(entity, ct);
        return true; // inserted new
    }

    public bool Upsert(T entity) => UpsertAsync(entity).GetAwaiter().GetResult();

    public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => _store.DeleteAsync(Name, id, ct);
    public bool Delete(string id) => DeleteAsync(id).GetAwaiter().GetResult();

    public async Task<int> DeleteManyAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
    {
        var matches = await FindAsync(predicate, ct);
        int count = 0;
        foreach (var e in matches)
        {
            var id = _mapper.GetId(e);
            if (!string.IsNullOrEmpty(id) && await DeleteAsync(id, ct)) count++;
        }
        return count;
    }

    public int DeleteMany(Expression<Func<T, bool>> predicate)
        => DeleteManyAsync(predicate).GetAwaiter().GetResult();

    // --- reads ---

    public async Task<T?> FindByIdAsync(string id, CancellationToken ct = default)
    {
        var doc = await _store.GetAsync(Name, id, ct);
        return doc == null ? null : _mapper.ToEntity(doc);
    }

    public T? FindById(string id) => FindByIdAsync(id).GetAwaiter().GetResult();

    public async Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
    {
        var filter = ExpressionTranslator.TryTranslate(predicate);
        if (filter != null)
        {
            var docs = await ExecuteQueryAsync(filter, null, null, null, ct);
            return docs.Select(_mapper.ToEntity).ToList();
        }

        // Fallback: stream all, filter with the compiled predicate.
        _diagnostics.IncrementFallback();
        var compiled = predicate.Compile();
        var all = await _store.GetAllAsync(Name, ct);
        return all.Select(_mapper.ToEntity).Where(compiled).ToList();
    }

    public IEnumerable<T> Find(Expression<Func<T, bool>> predicate)
        => FindAsync(predicate).GetAwaiter().GetResult();

    public T? FindOne(Expression<Func<T, bool>> predicate) => Find(predicate).FirstOrDefault();

    public IEnumerable<T> FindAll()
        => _store.GetAllAsync(Name).GetAwaiter().GetResult().Select(_mapper.ToEntity).ToList();

    public Task<long> CountAsync(CancellationToken ct = default) => _store.CountAsync(Name, ct);

    public async Task<long> CountAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
        => (await FindAsync(predicate, ct)).Count;

    public long Count() => CountAsync().GetAwaiter().GetResult();

    public long Count(Expression<Func<T, bool>> predicate) => CountAsync(predicate).GetAwaiter().GetResult();

    // --- indexing ---

    public Task<bool> EnsureIndexAsync<TField>(Expression<Func<T, TField>> field, bool unique = false, CancellationToken ct = default)
        => _store.EnsureIndexAsync(Name, MemberName(field), unique, ct);

    public bool EnsureIndex<TField>(Expression<Func<T, TField>> field, bool unique = false)
        => EnsureIndexAsync(field, unique).GetAwaiter().GetResult();

    // --- fluent ---

    public IEmbeddedQueryable<T> Query() => new EmbeddedQueryable<T>(this, _mapper);

    // --- helpers shared with the queryable ---

    internal async Task<IReadOnlyList<Document>> ExecuteQueryAsync(
        QueryFilter? filter, List<SortField>? sort, int? skip, int? limit, CancellationToken ct)
    {
        var query = new Query.Models.Query
        {
            CollectionName = Name,
            Filter = filter,
            Sort = sort,
            Options = (skip.HasValue || limit.HasValue) ? new QueryOptions { Skip = skip, Limit = limit } : null,
        };
        var result = await _executor.ExecuteAsync(query, ct);
        return result.Documents;
    }

    internal DocumentMapper<T> Mapper => _mapper;
    internal EmbeddedDocumentStore Store => _store;
    internal EmbeddedDiagnostics Diagnostics => _diagnostics;

    internal static string MemberName<TField>(Expression<Func<T, TField>> field)
    {
        var body = field.Body;
        while (body is UnaryExpression u) body = u.Operand;
        if (body is MemberExpression m && m.Member is PropertyInfo)
            return m.Member.Name;
        throw new ArgumentException("Index expression must be a simple property access, e.g. x => x.Barcode", nameof(field));
    }
}
