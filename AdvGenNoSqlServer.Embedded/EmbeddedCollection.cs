// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Query.Execution;
using AdvGenNoSqlServer.Query.Models;

namespace AdvGenNoSqlServer.Embedded;

/// <summary>
/// Document-level API for one collection. Thin delegation to <see cref="EmbeddedDocumentStore"/>
/// for CRUD and to the reused <see cref="QueryExecutor"/> for filtered/sorted/paged queries.
/// </summary>
public class EmbeddedCollection
{
    private readonly EmbeddedDocumentStore _store;
    private readonly QueryExecutor _executor;

    internal EmbeddedCollection(string name, EmbeddedDocumentStore store, QueryExecutor executor)
    {
        Name = name;
        _store = store;
        _executor = executor;
    }

    /// <summary>The collection name.</summary>
    public string Name { get; }

    /// <summary>Inserts a document (assigns an id if empty).</summary>
    public Task<Document> InsertAsync(Document doc, CancellationToken ct = default)
        => _store.InsertAsync(Name, doc, ct);

    /// <summary>Updates an existing document.</summary>
    public Task<Document> UpdateAsync(Document doc, CancellationToken ct = default)
        => _store.UpdateAsync(Name, doc, ct);

    /// <summary>Deletes a document by id.</summary>
    public Task<bool> DeleteAsync(string id, CancellationToken ct = default)
        => _store.DeleteAsync(Name, id, ct);

    /// <summary>Gets a document by id, or null.</summary>
    public Task<Document?> FindByIdAsync(string id, CancellationToken ct = default)
        => _store.GetAsync(Name, id, ct);

    /// <summary>Ensures a secondary index on a field.</summary>
    public Task<bool> EnsureIndexAsync(string field, bool unique = false, CancellationToken ct = default)
        => _store.EnsureIndexAsync(Name, field, unique, ct);

    /// <summary>Runs a filtered/sorted/paged query.</summary>
    public async Task<IReadOnlyList<Document>> FindAsync(
        QueryFilter? filter = null,
        List<SortField>? sort = null,
        int? skip = null,
        int? limit = null,
        CancellationToken ct = default)
    {
        var query = new Query.Models.Query
        {
            CollectionName = Name,
            Filter = filter,
            Sort = sort,
            Options = (skip.HasValue || limit.HasValue)
                ? new QueryOptions { Skip = skip, Limit = limit }
                : null,
        };
        var result = await _executor.ExecuteAsync(query, ct);
        return result.Documents;
    }

    /// <summary>Counts documents matching a filter.</summary>
    public async Task<long> CountAsync(QueryFilter? filter = null, CancellationToken ct = default)
    {
        if (filter == null)
            return await _store.CountAsync(Name, ct);
        var query = new Query.Models.Query { CollectionName = Name, Filter = filter };
        return await _executor.CountAsync(query, ct);
    }
}
