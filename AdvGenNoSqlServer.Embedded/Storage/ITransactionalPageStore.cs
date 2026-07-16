// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Embedded.Storage;

/// <summary>A page store whose writes are grouped into durable transactions (WAL-backed).</summary>
public interface ITransactionalPageStore : IPageStore
{
    /// <summary>Durably commits all page writes buffered since the last commit/rollback.</summary>
    void CommitTransaction();

    /// <summary>Discards all page writes buffered since the last commit.</summary>
    void RollbackTransaction();

    /// <summary>Flushes committed pages to the main file and truncates the log.</summary>
    void Checkpoint();
}

/// <summary>A store that persists a catalog root page id (file header field).</summary>
public interface ICatalogRootStore
{
    /// <summary>Root page id of the catalog chain (0 = none).</summary>
    uint CatalogRootPageId { get; set; }
}
