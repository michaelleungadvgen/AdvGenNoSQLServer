// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Embedded;

/// <summary>
/// Thrown when a database file cannot be opened because another process (or another
/// handle in this process) holds the exclusive lock.
/// </summary>
public sealed class EmbeddedDatabaseLockedException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    public EmbeddedDatabaseLockedException(string message) : base(message) { }

    /// <summary>Creates the exception with a message and inner exception.</summary>
    public EmbeddedDatabaseLockedException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Thrown when on-disk data fails integrity validation (bad checksum, torn page, etc.).
/// </summary>
public sealed class EmbeddedDataCorruptionException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    public EmbeddedDataCorruptionException(string message) : base(message) { }

    /// <summary>Creates the exception with a message and inner exception.</summary>
    public EmbeddedDataCorruptionException(string message, Exception inner) : base(message, inner) { }
}
