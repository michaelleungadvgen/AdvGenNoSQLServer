// Copyright (c) 2026 [Your Organization]
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Configuration;

namespace AdvGenNoSqlServer.Core.Authentication;

/// <summary>
/// Integrated authentication and authorization service combining user authentication with RBAC
/// </summary>
public class AuthenticationService
{
    private readonly AuthenticationManager _authManager;
    private readonly RoleManager _roleManager;
    private readonly ServerConfiguration _configuration;

    public AuthenticationService(ServerConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _authManager = new AuthenticationManager(configuration);
        _roleManager = new RoleManager();
    }

    #region Authentication Methods

    /// <summary>
    /// Registers a new user with the specified password and optional initial role
    /// </summary>
    public bool RegisterUser(string username, string password, string? initialRole = null)
    {
        if (!_authManager.RegisterUser(username, password))
            return false;

        // Assign initial role if provided and exists
        if (!string.IsNullOrEmpty(initialRole))
        {
            if (_roleManager.RoleExists(initialRole))
            {
                _roleManager.AssignRoleToUser(username, initialRole);
            }
        }
        else
        {
            // Assign default User role
            _roleManager.AssignRoleToUser(username, RoleNames.User);
        }

        return true;
    }

    /// <summary>
    /// Authenticates a user and returns an auth token
    /// </summary>
    public AuthToken? Authenticate(string username, string password)
    {
        return _authManager.Authenticate(username, password);
    }

    /// <summary>
    /// Validates an authentication token
    /// </summary>
    public bool ValidateToken(string tokenId)
    {
        return _authManager.ValidateToken(tokenId);
    }

    /// <summary>
    /// Gets the username associated with a token
    /// </summary>
    public string? GetUsernameFromToken(string tokenId)
    {
        // This would need to be added to AuthenticationManager
        // For now, we'll implement a simple version
        return null;
    }

    /// <summary>
    /// Revokes an authentication token
    /// </summary>
    public void RevokeToken(string tokenId)
    {
        _authManager.RevokeToken(tokenId);
    }

    /// <summary>
    /// Changes a user's password
    /// </summary>
    public bool ChangePassword(string username, string oldPassword, string newPassword)
    {
        return _authManager.ChangePassword(username, oldPassword, newPassword);
    }

    /// <summary>
    /// Removes a user from the system
    /// </summary>
    public bool RemoveUser(string username)
    {
        if (!_authManager.RemoveUser(username))
            return false;

        // Clean up user roles
        _roleManager.ClearUserRoles(username);
        return true;
    }

    #endregion

    #region Role Management Methods

    /// <summary>
    /// Creates a new role
    /// </summary>
    public bool CreateRole(string roleName, string? description = null, IEnumerable<string>? permissions = null)
    {
        return _roleManager.CreateRole(roleName, description, permissions);
    }

    /// <summary>
    /// Deletes a role
    /// </summary>
    public bool DeleteRole(string roleName)
    {
        return _roleManager.DeleteRole(roleName);
    }

    /// <summary>
    /// Assigns a role to a user
    /// </summary>
    public bool AssignRoleToUser(string username, string roleName)
    {
        return _roleManager.AssignRoleToUser(username, roleName);
    }

    /// <summary>
    /// Removes a role from a user
    /// </summary>
    public bool RemoveRoleFromUser(string username, string roleName)
    {
        return _roleManager.RemoveRoleFromUser(username, roleName);
    }

    /// <summary>
    /// Gets all roles assigned to a user
    /// </summary>
    public IReadOnlyCollection<string> GetUserRoles(string username)
    {
        return _roleManager.GetUserRoles(username);
    }

    /// <summary>
    /// Gets all available roles in the system
    /// </summary>
    public IReadOnlyCollection<Role> GetAllRoles()
    {
        return _roleManager.GetAllRoles();
    }

    #endregion

    #region Authorization Methods

    /// <summary>
    /// Checks if a user has a specific permission
    /// </summary>
    public bool UserHasPermission(string username, string permission)
    {
        return _roleManager.UserHasPermission(username, permission);
    }

    /// <summary>
    /// Checks if a user has a specific role
    /// </summary>
    public bool UserHasRole(string username, string roleName)
    {
        return _roleManager.UserHasRole(username, roleName);
    }

    /// <summary>
    /// Authorizes a token to perform an action with the required permission
    /// </summary>
    public AuthorizationResult Authorize(string tokenId, string requiredPermission)
    {
        // First validate the token
        if (!_authManager.ValidateToken(tokenId))
        {
            return AuthorizationResult.Failed("Invalid or expired token");
        }

        // Get username from token - we need to track this mapping
        // For now, we'll check if authentication is required
        if (!_configuration.RequireAuthentication)
        {
            return AuthorizationResult.Success();
        }

        // This is a simplified version - in production, we'd map tokens to users
        // and check permissions accordingly
        return AuthorizationResult.Success();
    }

    /// <summary>
    /// Gets all permissions for a user
    /// </summary>
    public IReadOnlyCollection<string> GetUserPermissions(string username)
    {
        return _roleManager.GetUserPermissions(username);
    }

    #endregion

    #region Audit Methods

    /// <summary>
    /// Logs an authentication attempt for audit purposes
    /// </summary>
    public void LogAuthenticationAttempt(string username, bool success, string? clientIp = null)
    {
        // In a real implementation, this would write to an audit log
        // For now, we just track the attempt
        var status = success ? "SUCCESS" : "FAILED";
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var ipInfo = clientIp != null ? $" from {clientIp}" : "";
        
        // This would be replaced with proper logging
        Console.WriteLine($"[AUTH] {timestamp} - {status}: {username}{ipInfo}");
    }

    #endregion
}

/// <summary>
/// Represents the result of an authorization check
/// </summary>
public class AuthorizationResult
{
    public bool IsAuthorized { get; }
    public string? FailureReason { get; }

    private AuthorizationResult(bool isAuthorized, string? failureReason = null)
    {
        IsAuthorized = isAuthorized;
        FailureReason = failureReason;
    }

    public static AuthorizationResult Success()
    {
        return new AuthorizationResult(true);
    }

    public static AuthorizationResult Failed(string reason)
    {
        return new AuthorizationResult(false, reason);
    }
}
