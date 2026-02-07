// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Authentication;
using AdvGenNoSqlServer.Core.Configuration;

namespace AdvGenNoSqlServer.Tests;

public class AuthenticationServiceTests
{
    private readonly AuthenticationService _authService;
    private readonly ServerConfiguration _config;

    public AuthenticationServiceTests()
    {
        _config = new ServerConfiguration
        {
            RequireAuthentication = true,
            TokenExpirationHours = 1
        };
        _authService = new AuthenticationService(_config);
    }

    #region User Registration Tests

    [Fact]
    public void RegisterUser_ValidCredentials_ReturnsTrue()
    {
        // Act
        var result = _authService.RegisterUser("testuser", "password123");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void RegisterUser_WithInitialRole_AssignsRole()
    {
        // Act
        _authService.RegisterUser("testuser", "password123", RoleNames.Admin);
        var roles = _authService.GetUserRoles("testuser");

        // Assert
        Assert.Contains(RoleNames.Admin, roles);
    }

    [Fact]
    public void RegisterUser_WithoutInitialRole_AssignsDefaultUserRole()
    {
        // Act
        _authService.RegisterUser("testuser", "password123");
        var roles = _authService.GetUserRoles("testuser");

        // Assert
        Assert.Contains(RoleNames.User, roles);
    }

    [Fact]
    public void RegisterUser_DuplicateUsername_ReturnsFalse()
    {
        // Arrange
        _authService.RegisterUser("testuser", "password123");

        // Act
        var result = _authService.RegisterUser("testuser", "password456");

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Authentication Tests

    [Fact]
    public void Authenticate_ValidCredentials_ReturnsToken()
    {
        // Arrange
        _authService.RegisterUser("testuser", "password123");

        // Act
        var token = _authService.Authenticate("testuser", "password123");

        // Assert
        Assert.NotNull(token);
        Assert.Equal("testuser", token.Username);
        Assert.NotNull(token.TokenId);
    }

    [Fact]
    public void Authenticate_InvalidUsername_ReturnsNull()
    {
        // Act
        var token = _authService.Authenticate("nonexistent", "password123");

        // Assert
        Assert.Null(token);
    }

    [Fact]
    public void Authenticate_InvalidPassword_ReturnsNull()
    {
        // Arrange
        _authService.RegisterUser("testuser", "password123");

        // Act
        var token = _authService.Authenticate("testuser", "wrongpassword");

        // Assert
        Assert.Null(token);
    }

    [Fact]
    public void ValidateToken_ValidToken_ReturnsTrue()
    {
        // Arrange
        _authService.RegisterUser("testuser", "password123");
        var token = _authService.Authenticate("testuser", "password123");

        // Act
        var isValid = _authService.ValidateToken(token!.TokenId);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void ValidateToken_InvalidToken_ReturnsFalse()
    {
        // Act
        var isValid = _authService.ValidateToken("invalid-token-id");

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void RevokeToken_ValidToken_RemovesToken()
    {
        // Arrange
        _authService.RegisterUser("testuser", "password123");
        var token = _authService.Authenticate("testuser", "password123");

        // Act
        _authService.RevokeToken(token!.TokenId);
        var isValid = _authService.ValidateToken(token.TokenId);

        // Assert
        Assert.False(isValid);
    }

    #endregion

    #region Password Management Tests

    [Fact]
    public void ChangePassword_ValidOldPassword_ReturnsTrue()
    {
        // Arrange
        _authService.RegisterUser("testuser", "oldpassword");

        // Act
        var result = _authService.ChangePassword("testuser", "oldpassword", "newpassword");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ChangePassword_InvalidOldPassword_ReturnsFalse()
    {
        // Arrange
        _authService.RegisterUser("testuser", "oldpassword");

        // Act
        var result = _authService.ChangePassword("testuser", "wrongpassword", "newpassword");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ChangePassword_AfterChange_OldPasswordInvalid()
    {
        // Arrange
        _authService.RegisterUser("testuser", "oldpassword");
        _authService.ChangePassword("testuser", "oldpassword", "newpassword");

        // Act
        var token = _authService.Authenticate("testuser", "oldpassword");

        // Assert
        Assert.Null(token);
    }

    [Fact]
    public void ChangePassword_AfterChange_NewPasswordWorks()
    {
        // Arrange
        _authService.RegisterUser("testuser", "oldpassword");
        _authService.ChangePassword("testuser", "oldpassword", "newpassword");

        // Act
        var token = _authService.Authenticate("testuser", "newpassword");

        // Assert
        Assert.NotNull(token);
    }

    #endregion

    #region Role Management Integration Tests

    [Fact]
    public void AssignRoleToUser_ValidRole_ReturnsTrue()
    {
        // Arrange
        _authService.RegisterUser("testuser", "password123");

        // Act
        var result = _authService.AssignRoleToUser("testuser", RoleNames.Admin);

        // Assert
        Assert.True(result);
        Assert.True(_authService.UserHasRole("testuser", RoleNames.Admin));
    }

    [Fact]
    public void RemoveRoleFromUser_ExistingRole_ReturnsTrue()
    {
        // Arrange
        _authService.RegisterUser("testuser", "password123", RoleNames.User);

        // Act
        var result = _authService.RemoveRoleFromUser("testuser", RoleNames.User);

        // Assert
        Assert.True(result);
        Assert.False(_authService.UserHasRole("testuser", RoleNames.User));
    }

    [Fact]
    public void UserHasPermission_WithRolePermission_ReturnsTrue()
    {
        // Arrange
        _authService.RegisterUser("testuser", "password123", RoleNames.User);

        // Act
        var hasPermission = _authService.UserHasPermission("testuser", Permissions.DocumentRead);

        // Assert
        Assert.True(hasPermission);
    }

    [Fact]
    public void UserHasPermission_WithoutRolePermission_ReturnsFalse()
    {
        // Arrange
        _authService.RegisterUser("testuser", "password123", RoleNames.ReadOnly);

        // Act
        var hasPermission = _authService.UserHasPermission("testuser", Permissions.DocumentWrite);

        // Assert
        Assert.False(hasPermission);
    }

    [Fact]
    public void GetUserPermissions_ReturnsAllPermissions()
    {
        // Arrange
        _authService.RegisterUser("testuser", "password123", RoleNames.User);

        // Act
        var permissions = _authService.GetUserPermissions("testuser");

        // Assert
        Assert.NotEmpty(permissions);
    }

    [Fact]
    public void CreateRole_CustomRole_CanAssignToUser()
    {
        // Arrange
        _authService.CreateRole("CustomRole", "Custom description", new[] { Permissions.DocumentRead });
        _authService.RegisterUser("testuser", "password123");

        // Act
        var result = _authService.AssignRoleToUser("testuser", "CustomRole");

        // Assert
        Assert.True(result);
        Assert.True(_authService.UserHasRole("testuser", "CustomRole"));
    }

    #endregion

    #region User Removal Tests

    [Fact]
    public void RemoveUser_ExistingUser_ReturnsTrue()
    {
        // Arrange
        _authService.RegisterUser("testuser", "password123");

        // Act
        var result = _authService.RemoveUser("testuser");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void RemoveUser_RemovesRoles()
    {
        // Arrange
        _authService.RegisterUser("testuser", "password123", RoleNames.Admin);

        // Act
        _authService.RemoveUser("testuser");
        var roles = _authService.GetUserRoles("testuser");

        // Assert
        Assert.Empty(roles);
    }

    [Fact]
    public void RemoveUser_NonExistentUser_ReturnsFalse()
    {
        // Act
        var result = _authService.RemoveUser("nonexistent");

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Role Creation Tests

    [Fact]
    public void CreateRole_NewRole_ReturnsTrue()
    {
        // Act
        var result = _authService.CreateRole("NewRole", "New role description");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void DeleteRole_ExistingRole_ReturnsTrue()
    {
        // Arrange
        _authService.CreateRole("TempRole");

        // Act
        var result = _authService.DeleteRole("TempRole");

        // Assert
        Assert.True(result);
    }

    #endregion

    #region Authorization Tests

    [Fact]
    public void Authorize_ValidToken_ReturnsSuccess()
    {
        // Arrange
        _authService.RegisterUser("testuser", "password123");
        var token = _authService.Authenticate("testuser", "password123");

        // Act
        var result = _authService.Authorize(token!.TokenId, Permissions.DocumentRead);

        // Assert
        Assert.True(result.IsAuthorized);
    }

    [Fact]
    public void Authorize_InvalidToken_ReturnsFailed()
    {
        // Act
        var result = _authService.Authorize("invalid-token", Permissions.DocumentRead);

        // Assert
        Assert.False(result.IsAuthorized);
        Assert.NotNull(result.FailureReason);
    }

    #endregion

    #region Get All Roles Tests

    [Fact]
    public void GetAllRoles_ReturnsDefaultRoles()
    {
        // Act
        var roles = _authService.GetAllRoles();

        // Assert
        Assert.True(roles.Count >= 5);
        var roleNames = roles.Select(r => r.Name).ToList();
        Assert.Contains(RoleNames.Admin, roleNames);
        Assert.Contains(RoleNames.User, roleNames);
    }

    #endregion
}
