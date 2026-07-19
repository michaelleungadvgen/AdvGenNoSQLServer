// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Linq.Expressions;

namespace AdvGenNoSqlServer.Embedded.Typed;

/// <summary>
/// Typed, LiteDB-style collection API. Sync methods are safe blocking wrappers over the
/// natively-async engine (matches LiteDB's sync ergonomics).
/// </summary>
public interface IEmbeddedCollection<T> where T : class
{
    /// <summary>The collection name.</summary>
    string Name { get; }

    /// <summary>Inserts an entity and returns its id (assigned if empty).</summary>
    string Insert(T entity);

    /// <summary>Inserts many entities; returns the count inserted.</summary>
    int InsertBulk(IEnumerable<T> entities);

    /// <summary>Updates an existing entity. Returns false if it does not exist.</summary>
    bool Update(T entity);

    /// <summary>Inserts the entity if absent, otherwise updates it.</summary>
    bool Upsert(T entity);

    /// <summary>Deletes by id. Returns false if not found.</summary>
    bool Delete(string id);

    /// <summary>Deletes all entities matching the predicate; returns the count deleted.</summary>
    int DeleteMany(Expression<Func<T, bool>> predicate);

    /// <summary>Finds by id, or null.</summary>
    T? FindById(string id);

    /// <summary>Finds the first entity matching the predicate, or null.</summary>
    T? FindOne(Expression<Func<T, bool>> predicate);

    /// <summary>Finds all entities matching the predicate.</summary>
    IEnumerable<T> Find(Expression<Func<T, bool>> predicate);

    /// <summary>Returns all entities.</summary>
    IEnumerable<T> FindAll();

    /// <summary>Counts all entities.</summary>
    long Count();

    /// <summary>Counts entities matching the predicate.</summary>
    long Count(Expression<Func<T, bool>> predicate);

    /// <summary>Ensures a secondary index on a member. Returns true if created.</summary>
    bool EnsureIndex<TField>(Expression<Func<T, TField>> field, bool unique = false);

    /// <summary>Starts a fluent query.</summary>
    IEmbeddedQueryable<T> Query();

    // Async variants
    /// <summary>Async <see cref="Insert"/>.</summary>
    Task<string> InsertAsync(T entity, CancellationToken ct = default);
    /// <summary>Async <see cref="Update"/>.</summary>
    Task<bool> UpdateAsync(T entity, CancellationToken ct = default);
    /// <summary>Async <see cref="Upsert"/>.</summary>
    Task<bool> UpsertAsync(T entity, CancellationToken ct = default);
    /// <summary>Async <see cref="Delete"/>.</summary>
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
    /// <summary>Async <see cref="DeleteMany"/>.</summary>
    Task<int> DeleteManyAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);
    /// <summary>Async <see cref="FindById"/>.</summary>
    Task<T?> FindByIdAsync(string id, CancellationToken ct = default);
    /// <summary>Async <see cref="Find"/>.</summary>
    Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);
    /// <summary>Async <see cref="Count()"/>.</summary>
    Task<long> CountAsync(CancellationToken ct = default);
    /// <summary>Async <see cref="Count(Expression{Func{T, bool}})"/>.</summary>
    Task<long> CountAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);
    /// <summary>Async <see cref="EnsureIndex"/>.</summary>
    Task<bool> EnsureIndexAsync<TField>(Expression<Func<T, TField>> field, bool unique = false, CancellationToken ct = default);
}

/// <summary>Fluent query builder over a typed collection (implemented in Task 16).</summary>
public interface IEmbeddedQueryable<T> where T : class
{
    /// <summary>Adds a filter predicate (ANDed with any others).</summary>
    IEmbeddedQueryable<T> Where(Expression<Func<T, bool>> predicate);
    /// <summary>Adds an ascending sort key.</summary>
    IEmbeddedQueryable<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector);
    /// <summary>Adds a descending sort key.</summary>
    IEmbeddedQueryable<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector);
    /// <summary>Skips the first n results.</summary>
    IEmbeddedQueryable<T> Skip(int count);
    /// <summary>Limits the number of results.</summary>
    IEmbeddedQueryable<T> Limit(int count);
    /// <summary>Materializes the results.</summary>
    List<T> ToList();
    /// <summary>Async <see cref="ToList"/>.</summary>
    Task<List<T>> ToListAsync(CancellationToken ct = default);
    /// <summary>First result (throws if none).</summary>
    T First();
    /// <summary>First result or null.</summary>
    T? FirstOrDefault();
    /// <summary>Number of matching results.</summary>
    int Count();
}
