// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.Database
{
    /// <summary>
    /// Security settings for a database.
    /// </summary>
    public class DatabaseSecurity
    {
        /// <summary>
        /// Gets or sets the owner of the database.
        /// </summary>
        public string? Owner { get; set; }

        /// <summary>
        /// Gets the mapping of usernames to their roles in this database.
        /// </summary>
        public Dictionary<string, DatabaseRole> UserAccess { get; } = new();

        /// <summary>
        /// Gets or sets whether anonymous read access is allowed.
        /// </summary>
        public bool AllowAnonymousRead { get; set; } = false;

        /// <summary>
        /// Gets or sets the list of roles that can read from this database.
        /// </summary>
        public List<DatabaseRole> AllowedReadRoles { get; set; } = new() { DatabaseRole.Admin, DatabaseRole.Member, DatabaseRole.Reader };

        /// <summary>
        /// Gets or sets the list of roles that can write to this database.
        /// </summary>
        public List<DatabaseRole> AllowedWriteRoles { get; set; } = new() { DatabaseRole.Admin, DatabaseRole.Member };

        /// <summary>
        /// Gets or sets the list of roles that can manage this database.
        /// </summary>
        public List<DatabaseRole> AllowedAdminRoles { get; set; } = new() { DatabaseRole.Admin };

        /// <summary>
        /// Initializes a new instance of the <see cref="DatabaseSecurity"/> class.
        /// </summary>
        public DatabaseSecurity()
        {
        }

        /// <summary>
        /// Grants access to a user with the specified role.
        /// </summary>
        /// <param name="username">The username.</param>
        /// <param name="role">The role to grant.</param>
        public void GrantAccess(string username, DatabaseRole role)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentException("Username cannot be null or empty", nameof(username));

            UserAccess[username] = role;
        }

        /// <summary>
        /// Revokes access from a user.
        /// </summary>
        /// <param name="username">The username.</param>
        /// <returns>True if the user had access and was removed; otherwise, false.</returns>
        public bool RevokeAccess(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return false;

            return UserAccess.Remove(username);
        }

        /// <summary>
        /// Gets the role for a user.
        /// </summary>
        /// <param name="username">The username.</param>
        /// <returns>The user's role, or DatabaseRole.None if not found.</returns>
        public DatabaseRole GetUserRole(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return DatabaseRole.None;

            return UserAccess.TryGetValue(username, out var role) ? role : DatabaseRole.None;
        }

        /// <summary>
        /// Checks if a user has the specified role or higher.
        /// </summary>
        /// <param name="username">The username.</param>
        /// <param name="requiredRole">The required role.</param>
        /// <returns>True if the user has the required role; otherwise, false.</returns>
        public bool HasRole(string username, DatabaseRole requiredRole)
        {
            var userRole = GetUserRole(username);
            return HasRoleLevel(userRole, requiredRole);
        }

        /// <summary>
        /// Checks if a user can read from this database.
        /// </summary>
        /// <param name="username">The username.</param>
        /// <returns>True if the user can read; otherwise, false.</returns>
        public bool CanRead(string username)
        {
            var role = GetUserRole(username);
            return AllowedReadRoles.Contains(role);
        }

        /// <summary>
        /// Checks if a user can write to this database.
        /// </summary>
        /// <param name="username">The username.</param>
        /// <returns>True if the user can write; otherwise, false.</returns>
        public bool CanWrite(string username)
        {
            var role = GetUserRole(username);
            return AllowedWriteRoles.Contains(role);
        }

        /// <summary>
        /// Checks if a user can administer this database.
        /// </summary>
        /// <param name="username">The username.</param>
        /// <returns>True if the user can administer; otherwise, false.</returns>
        public bool CanAdminister(string username)
        {
            var role = GetUserRole(username);
            return AllowedAdminRoles.Contains(role);
        }

        /// <summary>
        /// Gets all users with access to this database.
        /// </summary>
        /// <returns>A dictionary of usernames and their roles.</returns>
        public IReadOnlyDictionary<string, DatabaseRole> GetAllUsers()
        {
            return UserAccess;
        }

        /// <summary>
        /// Creates a copy of these security settings.
        /// </summary>
        public DatabaseSecurity Clone()
        {
            var clone = new DatabaseSecurity
            {
                Owner = this.Owner,
                AllowAnonymousRead = this.AllowAnonymousRead,
                AllowedReadRoles = new List<DatabaseRole>(this.AllowedReadRoles),
                AllowedWriteRoles = new List<DatabaseRole>(this.AllowedWriteRoles),
                AllowedAdminRoles = new List<DatabaseRole>(this.AllowedAdminRoles)
            };

            foreach (var kvp in this.UserAccess)
            {
                clone.UserAccess[kvp.Key] = kvp.Value;
            }

            return clone;
        }

        private static bool HasRoleLevel(DatabaseRole userRole, DatabaseRole requiredRole)
        {
            // Admin has all permissions
            if (userRole == DatabaseRole.Admin)
                return true;

            // Member can do Member and Reader operations
            if (userRole == DatabaseRole.Member)
                return requiredRole == DatabaseRole.Member || requiredRole == DatabaseRole.Reader;

            // Reader can only do Reader operations
            if (userRole == DatabaseRole.Reader)
                return requiredRole == DatabaseRole.Reader;

            // None has no permissions
            return false;
        }
    }
}
