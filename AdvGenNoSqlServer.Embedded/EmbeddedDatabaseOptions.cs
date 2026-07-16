// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Text.Json;

namespace AdvGenNoSqlServer.Embedded;

/// <summary>Configuration for an <see cref="AdvGenDatabase"/>.</summary>
public sealed class EmbeddedDatabaseOptions
{
    /// <summary>WAL size (bytes) that triggers an automatic checkpoint. Default 4 MB.</summary>
    public long WalCheckpointBytes { get; set; } = 4L * 1024 * 1024;

    /// <summary>Advisory page-cache size in pages. Default 1024. (Reserved for future tuning.)</summary>
    public int PageCacheSize { get; set; } = 1024;

    /// <summary>Optional serializer options for the typed POCO layer.</summary>
    public JsonSerializerOptions? SerializerOptions { get; set; }
}

/// <summary>Runtime diagnostics for a database instance.</summary>
public sealed class EmbeddedDiagnostics
{
    private long _fallbackQueryCount;

    /// <summary>Number of typed queries that fell back to in-memory predicate evaluation.</summary>
    public long FallbackQueryCount => Interlocked.Read(ref _fallbackQueryCount);

    internal void IncrementFallback() => Interlocked.Increment(ref _fallbackQueryCount);
}
