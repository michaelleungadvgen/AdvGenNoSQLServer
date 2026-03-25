// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;

namespace AdvGenNoSqlServer.Core.Authentication;

/// <summary>
/// Manages roles and permissions for the NoSQL server
/// </summary>
public class RoleManager
{
    private readonly ConcurrentDictionary<string, Role> _roles = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _userRoles = new();
    private readonly PermissionRegistry _permissionRegistry = new();

    public RoleManager()
    {
        // Initialize default roles
        InitializeDefaultRoles();
    }

    #region Role Management

    /// <summary>
    /// Creates a new role with the specified permissions
    /// </summary>
    public bool CreateRole(string roleName, string? description = null, IEnumerable<string>? permissions = null)
    {
        if (string.IsNullOrWhiteSpace(roleName))
            throw new ArgumentException("Role name cannot be empty", nameof(roleName));

        var role = new Role
        {
            Name = roleName,
            Description = description,
            Permissions = new ConcurrentDictionary<string, byte>(
                (permissions ?? Enumerable.Empty<string>()).Select(p => new KeyValuePair<string, byte>(p, 1))),
            CreatedAt = DateTime.UtcNow
        };

        return _roles.TryAdd(roleName, role);
    }

    /// <summary>
    /// Deletes a role and removes it from all users
    /// </summary>
    public bool DeleteRole(string roleName)
    {
        if (!_roles.TryRemove(roleName, out _))
            return false;

        // Remove role from all users
        foreach (var userRoles in _userRoles.Values)
        {
            userRoles.TryRemove(roleName, out _);
        }

        return true;
    }

    /// <summary>
    /// Gets a role by name
    /// </summary>
    public Role? GetRole(string roleName)
    {
        _roles.TryGetValue(roleName, out var role);
        return role;
    }

    /// <summary>
    /// Gets all defined roles
    /// </summary>
    public IReadOnlyCollection<Role> GetAllRoles()
    {
        return _roles.Values.ToList();
    }

    /// <summary>
    /// Checks if a role exists
    /// </summary>
    public bool RoleExists(string roleName)
    {
        return _roles.ContainsKey(roleName);
    }

    #endregion

    #region Permission Management

    /// <summary>
    /// Adds a permission to a role
    /// </summary>
    public bool AddPermissionToRole(string roleName, string permission)
    {
        if (!_roles.TryGetValue(roleName, out var role))
            return false;

        if (!_permissionRegistry.IsValidPermission(permission))
            throw new ArgumentException($"Invalid permission: {permission}", nameof(permission));

        var added = role.Permissions.TryAdd(permission, 1);
        if (added)
            role.UpdatedAt = DateTime.UtcNow;
        return added;
    }

    /// <summary>
    /// Removes a permission from a role
    /// </summary>
    public bool RemovePermissionFromRole(string roleName, string permission)
    {
        if (!_roles.TryGetValue(roleName, out var role))
            return false;

        var removed = role.Permissions.TryRemove(permission, out _);
        if (removed)
            role.UpdatedAt = DateTime.UtcNow;
        return removed;
    }

    /// <summary>
    /// Gets all permissions for a role
    /// </summary>
    public IReadOnlyCollection<string> GetRolePermissions(string roleName)
    {
        if (!_roles.TryGetValue(roleName, out var role))
            return Array.Empty<string>();

        return role.Permissions.Keys.ToList();
    }

    /// <summary>
    /// Checks if a role has a specific permission
    /// </summary>
    public bool RoleHasPermission(string roleName, string permission)
    {
        if (!_roles.TryGetValue(roleName, out var role))
            return false;

        return role.Permissions.ContainsKey(permission);
    }

    /// <summary>
    /// Gets all available permissions in the system
    /// </summary>
    public IReadOnlyCollection<string> GetAllPermissions()
    {
        return _permissionRegistry.GetAllPermissions();
    }

    #endregion

    #region User Role Management

    /// <summary>
    /// Assigns a role to a user
    /// </summary>
    public bool AssignRoleToUser(string username, string roleName)
    {
        if (!_roles.ContainsKey(roleName))
            return false;

        var userRoles = _userRoles.GetOrAdd(username, _ => new ConcurrentDictionary<string, byte>());
        return userRoles.TryAdd(roleName, 1);
    }

    /// <summary>
    /// Removes a role from a user
    /// </summary>
    public bool RemoveRoleFromUser(string username, string roleName)
    {
        if (!_userRoles.TryGetValue(username, out var roles))
            return false;

        return roles.TryRemove(roleName, out _);
    }

    /// <summary>
    /// Gets all roles assigned to a user
    /// </summary>
    public IReadOnlyCollection<string> GetUserRoles(string username)
    {
        if (!_userRoles.TryGetValue(username, out var roles))
            return Array.Empty<string>();

        return roles.Keys.ToList();
    }

    /// <summary>
    /// Checks if a user has a specific role
    /// </summary>
    public bool UserHasRole(string username, string roleName)
    {
        if (!_userRoles.TryGetValue(username, out var roles))
            return false;

        return roles.ContainsKey(roleName);
    }

    /// <summary>
    /// Clears all roles from a user
    /// </summary>
    public void ClearUserRoles(string username)
    {
        _userRoles.TryRemove(username, out _);
    }

    #endregion

    #region Permission Checking

    /// <summary>
    /// Checks if a user has a specific permission (through any of their roles)
    /// </summary>
    public bool UserHasPermission(string username, string permission)
    {
        if (!_userRoles.TryGetValue(username, out var roles))
            return false;

        foreach (var roleName in roles.Keys)
        {
            if (_roles.TryGetValue(roleName, out var role))
            {
                if (role.Permissions.ContainsKey(permission))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets all permissions a user has (aggregated from all roles)
    /// </summary>
    public IReadOnlyCollection<string> GetUserPermissions(string username)
    {
        if (!_userRoles.TryGetValue(username, out var roles))
            return Array.Empty<string>();

        var permissions = new HashSet<string>();
        foreach (var roleName in roles.Keys)
        {
            if (_roles.TryGetValue(roleName, out var role))
            {
                foreach (var perm in role.Permissions.Keys)
                {
                    permissions.Add(perm);
                }
            }
        }

        return permissions.ToList();
    }

    #endregion

    #region Private Methods

    private void InitializeDefaultRoles()
    {
        // Admin role - has all permissions
        CreateRole(
            RoleNames.Admin,
            "Administrator with full system access",
            _permissionRegistry.GetAllPermissions()
        );

        // PowerUser role - can perform most operations
        CreateRole(
            RoleNames.PowerUser,
            "Power user with elevated privileges",
            new[]
            {
                Permissions.DocumentRead,
                Permissions.DocumentWrite,
                Permissions.DocumentDelete,
                Permissions.QueryExecute,
                Permissions.TransactionExecute,
                Permissions.CollectionCreate,
                Permissions.CollectionDelete
            }
        );

        // User role - standard user
        CreateRole(
            RoleNames.User,
            "Standard user with basic access",
            new[]
            {
                Permissions.DocumentRead,
                Permissions.DocumentWrite,
                Permissions.QueryExecute,
                Permissions.TransactionExecute
            }
        );

        // ReadOnly role - can only read
        CreateRole(
            RoleNames.ReadOnly,
            "Read-only access to documents",
            new[]
            {
                Permissions.DocumentRead,
                Permissions.QueryExecute
            }
        );

        // Guest role - minimal access
        CreateRole(
            RoleNames.Guest,
            "Guest user with very limited access",
            new[]
            {
                Permissions.DocumentRead
            }
        );
    }

    #endregion
}

/// <summary>
/// Represents a role in the system
/// </summary>
public class Role
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public ConcurrentDictionary<string, byte> Permissions { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Predefined role names
/// </summary>
public static class RoleNames
{
    public const string Admin = "Admin";
    public const string PowerUser = "PowerUser";
    public const string User = "User";
    public const string ReadOnly = "ReadOnly";
    public const string Guest = "Guest";
}

/// <summary>
/// Predefined permissions
/// </summary>
public static class Permissions
{
    // Document permissions
    public const string DocumentRead = "document:read";
    public const string DocumentWrite = "document:write";
    public const string DocumentDelete = "document:delete";

    // Collection permissions
    public const string CollectionCreate = "collection:create";
    public const string CollectionDelete = "collection:delete";
    public const string CollectionList = "collection:list";

    // Query permissions
    public const string QueryExecute = "query:execute";
    public const string QueryAggregate = "query:aggregate";

    // Transaction permissions
    public const string TransactionExecute = "transaction:execute";

    // Admin permissions
    public const string UserManage = "user:manage";
    public const string RoleManage = "role:manage";
    public const string ServerAdmin = "server:admin";
    public const string AuditRead = "audit:read";
}

/// <summary>
/// Registry of valid permissions in the system
/// </summary>
public class PermissionRegistry
{
    private readonly ConcurrentDictionary<string, byte> _validPermissions;

    public PermissionRegistry()
    {
        _validPermissions = new ConcurrentDictionary<string, byte>(new[]
        {
            // Document permissions
            new KeyValuePair<string, byte>(Permissions.DocumentRead, 1),
            new KeyValuePair<string, byte>(Permissions.DocumentWrite, 1),
            new KeyValuePair<string, byte>(Permissions.DocumentDelete, 1),

            // Collection permissions
            new KeyValuePair<string, byte>(Permissions.CollectionCreate, 1),
            new KeyValuePair<string, byte>(Permissions.CollectionDelete, 1),
            new KeyValuePair<string, byte>(Permissions.CollectionList, 1),

            // Query permissions
            new KeyValuePair<string, byte>(Permissions.QueryExecute, 1),
            new KeyValuePair<string, byte>(Permissions.QueryAggregate, 1),

            // Transaction permissions
            new KeyValuePair<string, byte>(Permissions.TransactionExecute, 1),

            // Admin permissions
            new KeyValuePair<string, byte>(Permissions.UserManage, 1),
            new KeyValuePair<string, byte>(Permissions.RoleManage, 1),
            new KeyValuePair<string, byte>(Permissions.ServerAdmin, 1),
            new KeyValuePair<string, byte>(Permissions.AuditRead, 1)
        });
    }

    public bool IsValidPermission(string permission)
    {
        return _validPermissions.ContainsKey(permission);
    }

    public IReadOnlyCollection<string> GetAllPermissions()
    {
        return _validPermissions.Keys.ToList();
    }

    public void RegisterCustomPermission(string permission)
    {
        _validPermissions.TryAdd(permission, 1);
    }
}
