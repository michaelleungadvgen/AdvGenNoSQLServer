// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Authentication;
using AdvGenNoSqlServer.Core.Configuration;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for JWT Token Provider implementation
/// </summary>
public class JwtTokenProviderTests
{
    private const string TestSecretKey = "your-256-bit-secret-your-256-bit-secret-key";
    private static readonly TimeSpan DefaultExpiration = TimeSpan.FromHours(24);

    private static JwtTokenProvider CreateProvider(TimeSpan? expiration = null)
    {
        return new JwtTokenProvider(
            TestSecretKey,
            expiration ?? DefaultExpiration,
            "TestIssuer",
            "TestAudience"
        );
    }

    private static ServerConfiguration CreateConfiguration()
    {
        return new ServerConfiguration
        {
            JwtSecretKey = TestSecretKey,
            TokenExpirationHours = 24,
            JwtIssuer = "TestIssuer",
            JwtAudience = "TestAudience"
        };
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        var provider = CreateProvider();
        Assert.NotNull(provider);
    }

    [Fact]
    public void Constructor_WithServerConfiguration_CreatesInstance()
    {
        var config = CreateConfiguration();
        var provider = new JwtTokenProvider(config);
        Assert.NotNull(provider);
    }

    [Fact]
    public void Constructor_WithNullSecretKey_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new JwtTokenProvider(null!, DefaultExpiration));
    }

    [Fact]
    public void Constructor_WithEmptySecretKey_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new JwtTokenProvider("", DefaultExpiration));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new JwtTokenProvider(null!));
    }

    [Fact]
    public void Constructor_WithNullSecretKeyInConfiguration_GeneratesSecureSecret()
    {
        var config = CreateConfiguration();
        config.JwtSecretKey = null;
        var provider = new JwtTokenProvider(config);
        Assert.NotNull(provider);
    }

    #endregion

    #region Token Generation Tests

    [Fact]
    public void GenerateToken_WithValidParameters_ReturnsValidToken()
    {
        var provider = CreateProvider();
        var token = provider.GenerateToken("testuser", new[] { "User" }, new[] { "document.read" });

        Assert.NotNull(token);
        Assert.NotEmpty(token);

        // JWT tokens have 3 parts separated by dots
        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);
    }

    [Fact]
    public void GenerateToken_WithEmptyUsername_ThrowsArgumentException()
    {
        var provider = CreateProvider();
        Assert.Throws<ArgumentException>(() => 
            provider.GenerateToken("", new[] { "User" }, new[] { "document.read" }));
    }

    [Fact]
    public void GenerateToken_WithNullUsername_ThrowsArgumentException()
    {
        var provider = CreateProvider();
        Assert.Throws<ArgumentException>(() => 
            provider.GenerateToken(null!, new[] { "User" }, new[] { "document.read" }));
    }

    [Fact]
    public void GenerateToken_WithMultipleRoles_IncludesAllRoles()
    {
        var provider = CreateProvider();
        var roles = new[] { "User", "Admin", "PowerUser" };
        var token = provider.GenerateToken("testuser", roles, new[] { "document.read" });

        var result = provider.ValidateToken(token);
        Assert.True(result.IsValid);
        Assert.Equal(3, result.Roles.Count);
        Assert.Contains("User", result.Roles);
        Assert.Contains("Admin", result.Roles);
        Assert.Contains("PowerUser", result.Roles);
    }

    [Fact]
    public void GenerateToken_WithMultiplePermissions_IncludesAllPermissions()
    {
        var provider = CreateProvider();
        var permissions = new[] { "document.read", "document.write", "document.delete" };
        var token = provider.GenerateToken("testuser", new[] { "User" }, permissions);

        var result = provider.ValidateToken(token);
        Assert.True(result.IsValid);
        Assert.Equal(3, result.Permissions.Count);
        Assert.Contains("document.read", result.Permissions);
        Assert.Contains("document.write", result.Permissions);
        Assert.Contains("document.delete", result.Permissions);
    }

    [Fact]
    public void GenerateToken_WithCustomExpiration_SetsCorrectExpiration()
    {
        var provider = CreateProvider();
        var customExpiration = TimeSpan.FromMinutes(30);
        
        var beforeGeneration = DateTime.UtcNow;
        var token = provider.GenerateToken("testuser", new[] { "User" }, new[] { "read" }, customExpiration);
        var afterGeneration = DateTime.UtcNow;

        var expirationTime = provider.GetExpirationTime(token);
        Assert.NotNull(expirationTime);

        // Expiration should be approximately 30 minutes from now (with 5 second tolerance)
        var expectedMin = beforeGeneration.Add(customExpiration).AddSeconds(-5);
        var expectedMax = afterGeneration.Add(customExpiration).AddSeconds(5);
        Assert.True(expirationTime >= expectedMin && expirationTime <= expectedMax, 
            $"Expected expiration between {expectedMin} and {expectedMax}, but was {expirationTime}");
    }

    [Fact]
    public void GenerateToken_WithEmptyRolesAndPermissions_CreatesValidToken()
    {
        var provider = CreateProvider();
        var token = provider.GenerateToken("testuser", Array.Empty<string>(), Array.Empty<string>());

        var result = provider.ValidateToken(token);
        Assert.True(result.IsValid);
        Assert.Empty(result.Roles);
        Assert.Empty(result.Permissions);
    }

    [Fact]
    public void GenerateToken_EachTokenHasUniqueJti()
    {
        var provider = CreateProvider();
        var token1 = provider.GenerateToken("testuser", new[] { "User" }, new[] { "read" });
        var token2 = provider.GenerateToken("testuser", new[] { "User" }, new[] { "read" });

        Assert.NotEqual(token1, token2);
    }

    #endregion

    #region Token Validation Tests

    [Fact]
    public void ValidateToken_WithValidToken_ReturnsSuccess()
    {
        var provider = CreateProvider();
        var token = provider.GenerateToken("testuser", new[] { "User" }, new[] { "document.read" });

        var result = provider.ValidateToken(token);

        Assert.True(result.IsValid);
        Assert.Equal("testuser", result.Username);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ValidateToken_WithEmptyToken_ReturnsFailed()
    {
        var provider = CreateProvider();
        var result = provider.ValidateToken("");

        Assert.False(result.IsValid);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void ValidateToken_WithNullToken_ReturnsFailed()
    {
        var provider = CreateProvider();
        var result = provider.ValidateToken(null!);

        Assert.False(result.IsValid);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void ValidateToken_WithInvalidFormat_ReturnsFailed()
    {
        var provider = CreateProvider();
        var result = provider.ValidateToken("invalid.token");

        Assert.False(result.IsValid);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void ValidateToken_WithModifiedSignature_ReturnsFailed()
    {
        var provider = CreateProvider();
        var token = provider.GenerateToken("testuser", new[] { "User" }, new[] { "read" });
        
        // Modify the signature
        var modifiedToken = token.Substring(0, token.LastIndexOf('.') + 1) + "modified";

        var result = provider.ValidateToken(modifiedToken);

        Assert.False(result.IsValid);
        Assert.Contains("signature", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateToken_WithModifiedPayload_ReturnsFailed()
    {
        var provider = CreateProvider();
        var token = provider.GenerateToken("testuser", new[] { "User" }, new[] { "read" });
        
        // Modify the payload
        var parts = token.Split('.');
        parts[1] = "modifiedpayload";
        var modifiedToken = string.Join(".", parts);

        var result = provider.ValidateToken(modifiedToken);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateToken_WithExpiredToken_ReturnsFailed()
    {
        var provider = CreateProvider(TimeSpan.FromMilliseconds(1));
        var token = provider.GenerateToken("testuser", new[] { "User" }, new[] { "read" });

        // Wait for token to expire
        Thread.Sleep(100);

        var result = provider.ValidateToken(token);

        Assert.False(result.IsValid);
        Assert.Contains("expired", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateToken_WithWrongIssuer_ReturnsFailed()
    {
        var config = CreateConfiguration();
        var provider1 = new JwtTokenProvider(config);
        var token = provider1.GenerateToken("testuser", new[] { "User" }, new[] { "read" });

        // Create another provider with different issuer
        config.JwtIssuer = "DifferentIssuer";
        var provider2 = new JwtTokenProvider(config);

        var result = provider2.ValidateToken(token);

        Assert.False(result.IsValid);
        Assert.Contains("issuer", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateToken_WithWrongAudience_ReturnsFailed()
    {
        var config = CreateConfiguration();
        var provider1 = new JwtTokenProvider(config);
        var token = provider1.GenerateToken("testuser", new[] { "User" }, new[] { "read" });

        // Create another provider with different audience
        config.JwtAudience = "DifferentAudience";
        var provider2 = new JwtTokenProvider(config);

        var result = provider2.ValidateToken(token);

        Assert.False(result.IsValid);
        Assert.Contains("audience", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateToken_ExtractsCorrectUsername()
    {
        var provider = CreateProvider();
        var token = provider.GenerateToken("john_doe", new[] { "User" }, new[] { "read" });

        var result = provider.ValidateToken(token);

        Assert.True(result.IsValid);
        Assert.Equal("john_doe", result.Username);
    }

    [Fact]
    public void ValidateToken_ExtractsCorrectExpirationTime()
    {
        var provider = CreateProvider();
        var beforeGeneration = DateTime.UtcNow;
        var token = provider.GenerateToken("testuser", new[] { "User" }, new[] { "read" });

        var result = provider.ValidateToken(token);

        Assert.True(result.IsValid);
        Assert.NotNull(result.ExpirationTime);
        Assert.True(result.ExpirationTime > beforeGeneration);
        Assert.True(result.ExpirationTime <= beforeGeneration.Add(DefaultExpiration).AddSeconds(5));
    }

    #endregion

    #region Token Refresh Tests

    [Fact]
    public void RefreshToken_WithValidToken_ReturnsNewToken()
    {
        var provider = CreateProvider();
        var originalToken = provider.GenerateToken("testuser", new[] { "User" }, new[] { "read" });

        var refreshedToken = provider.RefreshToken(originalToken);

        Assert.NotNull(refreshedToken);
        Assert.NotEqual(originalToken, refreshedToken);

        // Both tokens should be valid
        Assert.True(provider.ValidateToken(originalToken).IsValid);
        Assert.True(provider.ValidateToken(refreshedToken!).IsValid);
    }

    [Fact]
    public void RefreshToken_PreservesUserClaims()
    {
        var provider = CreateProvider();
        var roles = new[] { "Admin", "User" };
        var permissions = new[] { "document.read", "document.write", "admin.access" };
        var originalToken = provider.GenerateToken("testuser", roles, permissions);

        var refreshedToken = provider.RefreshToken(originalToken);
        var result = provider.ValidateToken(refreshedToken!);

        Assert.True(result.IsValid);
        Assert.Equal("testuser", result.Username);
        Assert.Equal(2, result.Roles.Count);
        Assert.Equal(3, result.Permissions.Count);
        Assert.Contains("Admin", result.Roles);
        Assert.Contains("admin.access", result.Permissions);
    }

    [Fact]
    public void RefreshToken_UpdatesExpirationTime()
    {
        var provider = CreateProvider();
        var originalToken = provider.GenerateToken("testuser", new[] { "User" }, new[] { "read" });
        var originalExpiration = provider.GetExpirationTime(originalToken);

        // Wait a moment to ensure time difference (use 2 seconds for reliable difference)
        Thread.Sleep(2000);

        var refreshedToken = provider.RefreshToken(originalToken);
        var newExpiration = provider.GetExpirationTime(refreshedToken!);

        Assert.True(newExpiration > originalExpiration, 
            $"New expiration {newExpiration} should be later than original {originalExpiration}");
    }

    [Fact]
    public void RefreshToken_WithInvalidToken_ReturnsNull()
    {
        var provider = CreateProvider();
        var result = provider.RefreshToken("invalid.token.here");

        Assert.Null(result);
    }

    [Fact]
    public void RefreshToken_WithExpiredToken_ReturnsNull()
    {
        var provider = CreateProvider(TimeSpan.FromMilliseconds(1));
        var token = provider.GenerateToken("testuser", new[] { "User" }, new[] { "read" });

        Thread.Sleep(100);

        var result = provider.RefreshToken(token);
        Assert.Null(result);
    }

    #endregion

    #region Username Extraction Tests

    [Fact]
    public void ExtractUsername_WithValidToken_ReturnsUsername()
    {
        var provider = CreateProvider();
        var token = provider.GenerateToken("alice", new[] { "User" }, new[] { "read" });

        var username = provider.ExtractUsername(token);

        Assert.Equal("alice", username);
    }

    [Fact]
    public void ExtractUsername_WithInvalidToken_ReturnsNull()
    {
        var provider = CreateProvider();
        var username = provider.ExtractUsername("invalid.token");

        Assert.Null(username);
    }

    [Fact]
    public void ExtractUsername_WithEmptyToken_ReturnsNull()
    {
        var provider = CreateProvider();
        var username = provider.ExtractUsername("");

        Assert.Null(username);
    }

    [Fact]
    public void ExtractUsername_WithNullToken_ReturnsNull()
    {
        var provider = CreateProvider();
        var username = provider.ExtractUsername(null!);

        Assert.Null(username);
    }

    [Fact]
    public void ExtractUsername_DoesNotValidateSignature()
    {
        var provider = CreateProvider();
        var token = provider.GenerateToken("testuser", new[] { "User" }, new[] { "read" });
        
        // Modify signature but extraction should still work
        var parts = token.Split('.');
        var modifiedToken = $"{parts[0]}.{parts[1]}.invalid";

        var username = provider.ExtractUsername(modifiedToken);

        Assert.Equal("testuser", username);
    }

    #endregion

    #region Expiration Time Tests

    [Fact]
    public void GetExpirationTime_WithValidToken_ReturnsExpiration()
    {
        var provider = CreateProvider();
        var beforeGeneration = DateTime.UtcNow;
        var token = provider.GenerateToken("testuser", new[] { "User" }, new[] { "read" });

        var expiration = provider.GetExpirationTime(token);

        Assert.NotNull(expiration);
        Assert.True(expiration > beforeGeneration);
        Assert.True(expiration <= beforeGeneration.Add(DefaultExpiration).AddSeconds(5));
    }

    [Fact]
    public void GetExpirationTime_WithInvalidToken_ReturnsNull()
    {
        var provider = CreateProvider();
        var expiration = provider.GetExpirationTime("invalid.token");

        Assert.Null(expiration);
    }

    [Fact]
    public void GetExpirationTime_WithEmptyToken_ReturnsNull()
    {
        var provider = CreateProvider();
        var expiration = provider.GetExpirationTime("");

        Assert.Null(expiration);
    }

    #endregion

    #region Cross-Provider Validation Tests

    [Fact]
    public void Token_FromOneProvider_ValidatedByAnotherWithSameKey()
    {
        var provider1 = CreateProvider();
        var token = provider1.GenerateToken("testuser", new[] { "User" }, new[] { "read" });

        var provider2 = CreateProvider();
        var result = provider2.ValidateToken(token);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Token_FromOneProvider_InvalidWithDifferentKey()
    {
        var provider1 = new JwtTokenProvider("secret-key-one", DefaultExpiration);
        var token = provider1.GenerateToken("testuser", new[] { "User" }, new[] { "read" });

        var provider2 = new JwtTokenProvider("secret-key-two", DefaultExpiration);
        var result = provider2.ValidateToken(token);

        Assert.False(result.IsValid);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void GenerateToken_WithLongUsername_Succeeds()
    {
        var provider = CreateProvider();
        var longUsername = new string('a', 1000);
        var token = provider.GenerateToken(longUsername, new[] { "User" }, new[] { "read" });

        var result = provider.ValidateToken(token);
        Assert.True(result.IsValid);
        Assert.Equal(longUsername, result.Username);
    }

    [Fact]
    public void GenerateToken_WithManyRoles_Succeeds()
    {
        var provider = CreateProvider();
        var roles = Enumerable.Range(1, 100).Select(i => $"Role{i}").ToArray();
        var token = provider.GenerateToken("testuser", roles, new[] { "read" });

        var result = provider.ValidateToken(token);
        Assert.True(result.IsValid);
        Assert.Equal(100, result.Roles.Count);
    }

    [Fact]
    public void GenerateToken_WithSpecialCharactersInUsername_Succeeds()
    {
        var provider = CreateProvider();
        var specialUsername = "user@example.com_user-123.test";
        var token = provider.GenerateToken(specialUsername, new[] { "User" }, new[] { "read" });

        var result = provider.ValidateToken(token);
        Assert.True(result.IsValid);
        Assert.Equal(specialUsername, result.Username);
    }

    [Fact]
    public void GenerateToken_WithUnicodeCharacters_Succeeds()
    {
        var provider = CreateProvider();
        var unicodeUsername = "用户_테스트_ユーザー";
        var token = provider.GenerateToken(unicodeUsername, new[] { "User" }, new[] { "read" });

        var result = provider.ValidateToken(token);
        Assert.True(result.IsValid);
        Assert.Equal(unicodeUsername, result.Username);
    }

    [Fact]
    public void Token_WithVeryShortExpiration_CanBeValidatedBeforeExpiry()
    {
        var provider = CreateProvider(TimeSpan.FromSeconds(10));
        var token = provider.GenerateToken("testuser", new[] { "User" }, new[] { "read" });

        var result = provider.ValidateToken(token);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateToken_MalformedBase64_ReturnsFailed()
    {
        var provider = CreateProvider();
        // Create a token with invalid Base64 characters
        var malformedToken = "header.invalid!chars.signature";

        var result = provider.ValidateToken(malformedToken);

        Assert.False(result.IsValid);
    }

    #endregion
}
