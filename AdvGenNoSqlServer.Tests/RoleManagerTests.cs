// Copyright (c) 2026 [Your Organization]
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Authentication;

namespace AdvGenNoSqlServer.Tests;

public class RoleManagerTests
{
    private readonly RoleManager _roleManager;

    public RoleManagerTests()
    {
        _roleManager = new RoleManager();
    }

    #region Role Creation Tests

    [Fact]
    public void CreateRole_ValidRole_ReturnsTrue()
    {
        // Act
        var result = _roleManager.CreateRole("TestRole", "Test description");

        // Assert
        Assert.True(result);
        Assert.True(_roleManager.RoleExists("TestRole"));
    }

    [Fact]
    public void CreateRole_DuplicateRole_ReturnsFalse()
    {
        // Arrange
        _roleManager.CreateRole("TestRole");

        // Act
        var result = _roleManager.CreateRole("TestRole");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CreateRole_EmptyName_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _roleManager.CreateRole(""));
    }

    [Fact]
    public void CreateRole_WithPermissions_PermissionsAssigned()
    {
        // Arrange
        var permissions = new[] { Permissions.DocumentRead, Permissions.DocumentWrite };

        // Act
        _roleManager.CreateRole("TestRole", permissions: permissions);
        var rolePermissions = _roleManager.GetRolePermissions("TestRole");

        // Assert
        Assert.Contains(Permissions.DocumentRead, rolePermissions);
        Assert.Contains(Permissions.DocumentWrite, rolePermissions);
    }

    #endregion

    #region Role Deletion Tests

    [Fact]
    public void DeleteRole_ExistingRole_ReturnsTrue()
    {
        // Arrange
        _roleManager.CreateRole("TestRole");

        // Act
        var result = _roleManager.DeleteRole("TestRole");

        // Assert
        Assert.True(result);
        Assert.False(_roleManager.RoleExists("TestRole"));
    }

    [Fact]
    public void DeleteRole_NonExistentRole_ReturnsFalse()
    {
        // Act
        var result = _roleManager.DeleteRole("NonExistent");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void DeleteRole_RemovesRoleFromUsers()
    {
        // Arrange
        _roleManager.CreateRole("TestRole");
        _roleManager.AssignRoleToUser("user1", "TestRole");

        // Act
        _roleManager.DeleteRole("TestRole");
        var userRoles = _roleManager.GetUserRoles("user1");

        // Assert
        Assert.DoesNotContain("TestRole", userRoles);
    }

    #endregion

    #region Default Roles Tests

    [Theory]
    [InlineData(RoleNames.Admin)]
    [InlineData(RoleNames.PowerUser)]
    [InlineData(RoleNames.User)]
    [InlineData(RoleNames.ReadOnly)]
    [InlineData(RoleNames.Guest)]
    public void Constructor_CreatesDefaultRoles(string roleName)
    {
        // Assert
        Assert.True(_roleManager.RoleExists(roleName));
    }

    [Fact]
    public void AdminRole_HasAllPermissions()
    {
        // Arrange
        var allPermissions = _roleManager.GetAllPermissions();

        // Act
        var adminPermissions = _roleManager.GetRolePermissions(RoleNames.Admin);

        // Assert
        Assert.Equal(allPermissions.Count, adminPermissions.Count);
        foreach (var permission in allPermissions)
        {
            Assert.Contains(permission, adminPermissions);
        }
    }

    [Fact]
    public void ReadOnlyRole_HasOnlyReadPermissions()
    {
        // Act
        var permissions = _roleManager.GetRolePermissions(RoleNames.ReadOnly);

        // Assert
        Assert.Contains(Permissions.DocumentRead, permissions);
        Assert.Contains(Permissions.QueryExecute, permissions);
        Assert.DoesNotContain(Permissions.DocumentWrite, permissions);
        Assert.DoesNotContain(Permissions.DocumentDelete, permissions);
    }

    #endregion

    #region User Role Assignment Tests

    [Fact]
    public void AssignRoleToUser_ValidRole_ReturnsTrue()
    {
        // Act
        var result = _roleManager.AssignRoleToUser("user1", RoleNames.User);

        // Assert
        Assert.True(result);
        Assert.True(_roleManager.UserHasRole("user1", RoleNames.User));
    }

    [Fact]
    public void AssignRoleToUser_InvalidRole_ReturnsFalse()
    {
        // Act
        var result = _roleManager.AssignRoleToUser("user1", "NonExistentRole");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void AssignRoleToUser_MultipleRoles_AllAssigned()
    {
        // Act
        _roleManager.AssignRoleToUser("user1", RoleNames.User);
        _roleManager.AssignRoleToUser("user1", RoleNames.ReadOnly);
        var roles = _roleManager.GetUserRoles("user1");

        // Assert
        Assert.Equal(2, roles.Count);
        Assert.Contains(RoleNames.User, roles);
        Assert.Contains(RoleNames.ReadOnly, roles);
    }

    [Fact]
    public void RemoveRoleFromUser_ExistingRole_ReturnsTrue()
    {
        // Arrange
        _roleManager.AssignRoleToUser("user1", RoleNames.User);

        // Act
        var result = _roleManager.RemoveRoleFromUser("user1", RoleNames.User);

        // Assert
        Assert.True(result);
        Assert.False(_roleManager.UserHasRole("user1", RoleNames.User));
    }

    [Fact]
    public void RemoveRoleFromUser_NonExistentAssignment_ReturnsFalse()
    {
        // Act
        var result = _roleManager.RemoveRoleFromUser("user1", RoleNames.User);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ClearUserRoles_RemovesAllRoles()
    {
        // Arrange
        _roleManager.AssignRoleToUser("user1", RoleNames.User);
        _roleManager.AssignRoleToUser("user1", RoleNames.ReadOnly);

        // Act
        _roleManager.ClearUserRoles("user1");
        var roles = _roleManager.GetUserRoles("user1");

        // Assert
        Assert.Empty(roles);
    }

    #endregion

    #region Permission Tests

    [Fact]
    public void AddPermissionToRole_ValidPermission_ReturnsTrue()
    {
        // Arrange
        _roleManager.CreateRole("TestRole");

        // Act
        var result = _roleManager.AddPermissionToRole("TestRole", Permissions.DocumentDelete);
        var permissions = _roleManager.GetRolePermissions("TestRole");

        // Assert
        Assert.True(result);
        Assert.Contains(Permissions.DocumentDelete, permissions);
    }

    [Fact]
    public void AddPermissionToRole_InvalidPermission_ThrowsException()
    {
        // Arrange
        _roleManager.CreateRole("TestRole");

        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            _roleManager.AddPermissionToRole("TestRole", "invalid:permission"));
    }

    [Fact]
    public void RemovePermissionFromRole_ExistingPermission_ReturnsTrue()
    {
        // Arrange
        _roleManager.CreateRole("TestRole", permissions: new[] { Permissions.DocumentRead });

        // Act
        var result = _roleManager.RemovePermissionFromRole("TestRole", Permissions.DocumentRead);
        var permissions = _roleManager.GetRolePermissions("TestRole");

        // Assert
        Assert.True(result);
        Assert.DoesNotContain(Permissions.DocumentRead, permissions);
    }

    [Fact]
    public void UserHasPermission_WithPermission_ReturnsTrue()
    {
        // Arrange
        _roleManager.AssignRoleToUser("user1", RoleNames.User);

        // Act
        var hasPermission = _roleManager.UserHasPermission("user1", Permissions.DocumentRead);

        // Assert
        Assert.True(hasPermission);
    }

    [Fact]
    public void UserHasPermission_WithoutPermission_ReturnsFalse()
    {
        // Arrange
        _roleManager.AssignRoleToUser("user1", RoleNames.ReadOnly);

        // Act
        var hasPermission = _roleManager.UserHasPermission("user1", Permissions.DocumentWrite);

        // Assert
        Assert.False(hasPermission);
    }

    [Fact]
    public void UserHasPermission_MultipleRoles_AggregatesPermissions()
    {
        // Arrange
        _roleManager.CreateRole("Role1", permissions: new[] { Permissions.DocumentRead });
        _roleManager.CreateRole("Role2", permissions: new[] { Permissions.DocumentWrite });
        _roleManager.AssignRoleToUser("user1", "Role1");
        _roleManager.AssignRoleToUser("user1", "Role2");

        // Act
        var canRead = _roleManager.UserHasPermission("user1", Permissions.DocumentRead);
        var canWrite = _roleManager.UserHasPermission("user1", Permissions.DocumentWrite);

        // Assert
        Assert.True(canRead);
        Assert.True(canWrite);
    }

    [Fact]
    public void GetUserPermissions_AggregatesFromAllRoles()
    {
        // Arrange
        _roleManager.CreateRole("Role1", permissions: new[] { Permissions.DocumentRead });
        _roleManager.CreateRole("Role2", permissions: new[] { Permissions.DocumentWrite });
        _roleManager.AssignRoleToUser("user1", "Role1");
        _roleManager.AssignRoleToUser("user1", "Role2");

        // Act
        var permissions = _roleManager.GetUserPermissions("user1");

        // Assert
        Assert.Equal(2, permissions.Count);
        Assert.Contains(Permissions.DocumentRead, permissions);
        Assert.Contains(Permissions.DocumentWrite, permissions);
    }

    #endregion

    #region Get Methods Tests

    [Fact]
    public void GetRole_ExistingRole_ReturnsRole()
    {
        // Act
        var role = _roleManager.GetRole(RoleNames.Admin);

        // Assert
        Assert.NotNull(role);
        Assert.Equal(RoleNames.Admin, role.Name);
    }

    [Fact]
    public void GetRole_NonExistentRole_ReturnsNull()
    {
        // Act
        var role = _roleManager.GetRole("NonExistent");

        // Assert
        Assert.Null(role);
    }

    [Fact]
    public void GetAllRoles_ReturnsAllRoles()
    {
        // Act
        var roles = _roleManager.GetAllRoles();

        // Assert
        Assert.True(roles.Count >= 5); // At least the 5 default roles
    }

    [Fact]
    public void GetAllPermissions_ReturnsValidPermissions()
    {
        // Act
        var permissions = _roleManager.GetAllPermissions();

        // Assert
        Assert.NotEmpty(permissions);
        Assert.Contains(Permissions.DocumentRead, permissions);
        Assert.Contains(Permissions.DocumentWrite, permissions);
    }

    #endregion
}
