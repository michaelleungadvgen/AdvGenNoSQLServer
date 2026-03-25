// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.Database
{
    /// <summary>
    /// Defines the roles a user can have within a database.
    /// </summary>
    public enum DatabaseRole
    {
        /// <summary>
        /// Full access including security changes, user management, and database configuration.
        /// </summary>
        Admin,

        /// <summary>
        /// Read/write access to documents and indexes, but no security changes.
        /// </summary>
        Member,

        /// <summary>
        /// Read-only access to documents.
        /// </summary>
        Reader,

        /// <summary>
        /// No access to the database.
        /// </summary>
        None
    }
}
