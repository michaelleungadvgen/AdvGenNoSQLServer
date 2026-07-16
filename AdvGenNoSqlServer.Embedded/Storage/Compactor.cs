// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Embedded.Storage;

/// <summary>
/// Rewrites a database file dropping tombstoned records and free pages by streaming the live
/// documents of a source store into a brand-new file through the normal insert path.
/// </summary>
internal static class Compactor
{
    /// <summary>
    /// Writes a compacted copy of <paramref name="source"/> to <paramref name="destPath"/>.
    /// The source store remains open (read-only) throughout.
    /// </summary>
    public static async Task WriteCompactedCopyAsync(
        EmbeddedDocumentStore source, string destPath, long walThreshold, CancellationToken ct = default)
    {
        DeleteIfExists(destPath);
        DeleteIfExists(destPath + ".wal");

        var dest = new EmbeddedDocumentStore(new WalPageStore(new FilePageStore(destPath), destPath + ".wal", walThreshold));
        try
        {
            await dest.InitializeAsync();

            var collections = (await source.GetCollectionsAsync(ct)).ToList();
            foreach (var name in collections)
                await dest.CreateCollectionAsync(name, ct);

            // Recreate index definitions on the (empty) collections first so inserts maintain them.
            foreach (var (collection, field, unique) in source.GetIndexDefinitions())
                await dest.EnsureIndexAsync(collection, field, unique, ct);

            foreach (var name in collections)
            {
                foreach (var doc in await source.GetAllAsync(name, ct))
                {
                    // Preserve id, data, and timestamps as a fresh insert (id is non-empty so it is kept).
                    await dest.InsertAsync(name, new Document
                    {
                        Id = doc.Id,
                        Data = doc.Data,
                    }, ct);
                }
            }
        }
        finally
        {
            dest.Dispose(); // checkpoints and truncates the dest WAL
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
