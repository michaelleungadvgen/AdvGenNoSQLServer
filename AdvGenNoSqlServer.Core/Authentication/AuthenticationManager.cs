// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using AdvGenNoSqlServer.Core.Configuration;

namespace AdvGenNoSqlServer.Core.Authentication;

/// <summary>
/// Manages user authentication with secure password hashing using PBKDF2.
/// </summary>
public class AuthenticationManager
{
    private readonly ConcurrentDictionary<string, UserCredentials> _users = new();
    private readonly ConcurrentDictionary<string, AuthToken> _activeSessions = new();
    private readonly ConcurrentDictionary<string, (int attempts, DateTime lockoutEnd)> _failedAttempts = new();
    private readonly TimeSpan _tokenExpiration;
    private readonly ServerConfiguration _configuration;

    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    // PBKDF2 configuration - OWASP recommends 600k iterations for SHA256 in 2023
    private const int Pbkdf2Iterations = 100000;
    private const int SaltSizeBytes = 32;
    private const int HashSizeBytes = 32;

    public AuthenticationManager(ServerConfiguration configuration)
    {
        _configuration = configuration;
        _tokenExpiration = TimeSpan.FromHours(configuration.TokenExpirationHours);

        // Initialize master admin user if master password is set
        if (!string.IsNullOrEmpty(configuration.MasterPassword))
        {
            RegisterUser("admin", configuration.MasterPassword);
        }
    }

    /// <summary>
    /// Registers a new user with secure password hashing.
    /// </summary>
    public bool RegisterUser(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return false;

        if (_users.ContainsKey(username))
            return false;

        var (salt, hashedPassword) = HashPassword(password);

        _users[username] = new UserCredentials
        {
            Username = username,
            PasswordHash = hashedPassword,
            Salt = salt,
            CreatedAt = DateTime.UtcNow
        };

        return true;
    }

    /// <summary>
    /// Authenticates a user and returns an auth token if successful.
    /// </summary>
    public AuthToken? Authenticate(string username, string password)
    {
        if (_failedAttempts.TryGetValue(username, out var info))
        {
            if (DateTime.UtcNow < info.lockoutEnd)
            {
                return null;
            }

            // If the lockout has expired, we only remove it if the user successfully authenticates.
            // If we remove it here, the next failed attempt will start from 1 again instead of
            // continuing to track failures or triggering a new lockout immediately.
        }

        if (!_users.TryGetValue(username, out var credentials))
        {
            TrackFailedAttempt(username);
            return null;
        }

        // Verify password using constant-time comparison to prevent timing attacks
        if (!VerifyPassword(password, credentials.Salt, credentials.PasswordHash))
        {
            TrackFailedAttempt(username);
            return null;
        }

        // Only clear failed attempts on successful login
        _failedAttempts.TryRemove(username, out _);

        var token = new AuthToken
        {
            TokenId = Guid.NewGuid().ToString(),
            Username = username,
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(_tokenExpiration)
        };

        _activeSessions[token.TokenId] = token;
        return token;
    }

    private void TrackFailedAttempt(string username)
    {
        _failedAttempts.AddOrUpdate(
            username,
            _ => (1, DateTime.MinValue),
            (_, info) =>
            {
                // Reset counter if they were previously locked out and the lockout expired
                var newAttempts = (DateTime.UtcNow > info.lockoutEnd && info.lockoutEnd != DateTime.MinValue)
                    ? 1
                    : info.attempts + 1;

                var lockoutEnd = newAttempts >= MaxFailedAttempts
                    ? DateTime.UtcNow.Add(LockoutDuration)
                    : DateTime.MinValue;
                return (newAttempts, lockoutEnd);
            });
    }

    /// <summary>
    /// Gets a token by its ID.
    /// </summary>
    public AuthToken? GetToken(string tokenId)
    {
        _activeSessions.TryGetValue(tokenId, out var token);
        return token;
    }

    /// <summary>
    /// Validates if a token is still active and not expired.
    /// </summary>
    public bool ValidateToken(string tokenId)
    {
        if (!_activeSessions.TryGetValue(tokenId, out var token))
            return false;

        if (DateTime.UtcNow > token.ExpiresAt)
        {
            _activeSessions.TryRemove(tokenId, out _);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Revokes a specific token.
    /// </summary>
    public void RevokeToken(string tokenId)
    {
        _activeSessions.TryRemove(tokenId, out _);
    }

    /// <summary>
    /// Revokes all tokens for a specific user.
    /// </summary>
    public void RevokeAllUserTokens(string username)
    {
        var tokensToRemove = _activeSessions
            .Where(kvp => kvp.Value.Username == username)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var tokenId in tokensToRemove)
        {
            _activeSessions.TryRemove(tokenId, out _);
        }
    }

    /// <summary>
    /// Changes a user's password after verifying the old password.
    /// </summary>
    public bool ChangePassword(string username, string oldPassword, string newPassword)
    {
        if (!_users.TryGetValue(username, out var credentials))
            return false;

        if (!VerifyPassword(oldPassword, credentials.Salt, credentials.PasswordHash))
            return false;

        var (newSalt, hashedNewPassword) = HashPassword(newPassword);

        credentials.PasswordHash = hashedNewPassword;
        credentials.Salt = newSalt;

        RevokeAllUserTokens(username);
        return true;
    }

    /// <summary>
    /// Removes a user and all their tokens.
    /// </summary>
    public bool RemoveUser(string username)
    {
        if (!_users.TryRemove(username, out _))
            return false;

        RevokeAllUserTokens(username);
        return true;
    }

    /// <summary>
    /// Gets a copy of all registered users (for testing/admin purposes).
    /// </summary>
    public IReadOnlyDictionary<string, UserCredentials> GetUsers()
    {
        return new Dictionary<string, UserCredentials>(_users);
    }

    /// <summary>
    /// Hashes a password using PBKDF2 with HMAC-SHA256.
    /// Returns the salt and hashed password as base64 strings.
    /// </summary>
    private static (string Salt, string Hash) HashPassword(string password)
    {
        // Generate a cryptographically secure random salt
        var saltBytes = new byte[SaltSizeBytes];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(saltBytes);
        }

        // Hash the password using PBKDF2
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(
            password: passwordBytes,
            salt: saltBytes,
            iterations: Pbkdf2Iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: HashSizeBytes);

        // Clear password bytes from memory
        CryptographicOperations.ZeroMemory(passwordBytes);

        return (Convert.ToBase64String(saltBytes), Convert.ToBase64String(hashBytes));
    }

    /// <summary>
    /// Verifies a password against a stored salt and hash using constant-time comparison.
    /// This prevents timing attacks that could reveal information about the password.
    /// </summary>
    private static bool VerifyPassword(string password, string salt, string hash)
    {
        try
        {
            var saltBytes = Convert.FromBase64String(salt);
            var expectedHashBytes = Convert.FromBase64String(hash);
            var passwordBytes = Encoding.UTF8.GetBytes(password);

            // Compute hash of provided password
            var actualHashBytes = Rfc2898DeriveBytes.Pbkdf2(
                password: passwordBytes,
                salt: saltBytes,
                iterations: Pbkdf2Iterations,
                hashAlgorithm: HashAlgorithmName.SHA256,
                outputLength: expectedHashBytes.Length);

            // Clear password bytes from memory
            CryptographicOperations.ZeroMemory(passwordBytes);

            // Use constant-time comparison to prevent timing attacks
            return CryptographicOperations.FixedTimeEquals(actualHashBytes, expectedHashBytes);
        }
        catch (FormatException)
        {
            // Invalid base64 format
            return false;
        }
    }
}

/// <summary>
/// Represents a user's credentials.
/// </summary>
public class UserCredentials
{
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    public required string Salt { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Represents an authentication token.
/// </summary>
public class AuthToken
{
    public required string TokenId { get; set; }
    public required string Username { get; set; }
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
