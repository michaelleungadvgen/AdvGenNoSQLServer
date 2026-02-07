// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.Authentication;

/// <summary>
/// Interface for JWT (JSON Web Token) generation and validation
/// </summary>
public interface IJwtTokenProvider
{
    /// <summary>
    /// Generates a JWT token for the specified user with their roles and permissions
    /// </summary>
    /// <param name="username">The username to include in the token</param>
    /// <param name="roles">The roles assigned to the user</param>
    /// <param name="permissions">The permissions granted to the user</param>
    /// <returns>A JWT token string</returns>
    string GenerateToken(string username, IEnumerable<string> roles, IEnumerable<string> permissions);

    /// <summary>
    /// Generates a JWT token with custom expiration time
    /// </summary>
    /// <param name="username">The username to include in the token</param>
    /// <param name="roles">The roles assigned to the user</param>
    /// <param name="permissions">The permissions granted to the user</param>
    /// <param name="expiration">Custom expiration time from now</param>
    /// <returns>A JWT token string</returns>
    string GenerateToken(string username, IEnumerable<string> roles, IEnumerable<string> permissions, TimeSpan expiration);

    /// <summary>
    /// Validates a JWT token and returns the principal if valid
    /// </summary>
    /// <param name="token">The JWT token to validate</param>
    /// <returns>Token validation result containing the principal if valid</returns>
    TokenValidationResult ValidateToken(string token);

    /// <summary>
    /// Refreshes a valid token with a new expiration time
    /// </summary>
    /// <param name="token">The current valid token</param>
    /// <returns>A new token with updated expiration, or null if the original is invalid</returns>
    string? RefreshToken(string token);

    /// <summary>
    /// Extracts the username from a token without full validation (for logging purposes)
    /// </summary>
    /// <param name="token">The JWT token</param>
    /// <returns>The username from the token, or null if malformed</returns>
    string? ExtractUsername(string token);

    /// <summary>
    /// Gets the token expiration time from the token
    /// </summary>
    /// <param name="token">The JWT token</param>
    /// <returns>The expiration time, or null if invalid</returns>
    DateTime? GetExpirationTime(string token);
}

/// <summary>
/// Represents the result of token validation
/// </summary>
public class TokenValidationResult
{
    /// <summary>
    /// Whether the token is valid
    /// </summary>
    public bool IsValid { get; }

    /// <summary>
    /// The username from the token
    /// </summary>
    public string? Username { get; }

    /// <summary>
    /// The roles from the token
    /// </summary>
    public IReadOnlyList<string> Roles { get; }

    /// <summary>
    /// The permissions from the token
    /// </summary>
    public IReadOnlyList<string> Permissions { get; }

    /// <summary>
    /// The error message if validation failed
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// The expiration time of the token
    /// </summary>
    public DateTime? ExpirationTime { get; }

    private TokenValidationResult(bool isValid, string? username, IReadOnlyList<string> roles, 
        IReadOnlyList<string> permissions, string? errorMessage, DateTime? expirationTime)
    {
        IsValid = isValid;
        Username = username;
        Roles = roles ?? Array.Empty<string>();
        Permissions = permissions ?? Array.Empty<string>();
        ErrorMessage = errorMessage;
        ExpirationTime = expirationTime;
    }

    /// <summary>
    /// Creates a successful validation result
    /// </summary>
    public static TokenValidationResult Success(string username, IEnumerable<string> roles, 
        IEnumerable<string> permissions, DateTime expirationTime)
    {
        return new TokenValidationResult(true, username, roles.ToList().AsReadOnly(), 
            permissions.ToList().AsReadOnly(), null, expirationTime);
    }

    /// <summary>
    /// Creates a failed validation result
    /// </summary>
    public static TokenValidationResult Failed(string errorMessage)
    {
        return new TokenValidationResult(false, null, Array.Empty<string>(), 
            Array.Empty<string>(), errorMessage, null);
    }
}

/// <summary>
/// Exception thrown when JWT token processing fails
/// </summary>
public class JwtTokenException : Exception
{
    public JwtTokenException(string message) : base(message) { }
    public JwtTokenException(string message, Exception innerException) : base(message, innerException) { }
}
