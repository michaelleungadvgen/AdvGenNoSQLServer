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
    private readonly ConcurrentDictionary<string, LoginAttemptTracker> _loginAttempts = new();
    private readonly System.Timers.Timer _cleanupTimer;
    private readonly TimeSpan _tokenExpiration;
    private readonly ServerConfiguration _configuration;

    // PBKDF2 configuration - OWASP recommends 600k iterations for SHA256 in 2023
    private const int Pbkdf2Iterations = 100000;
    private const int SaltSizeBytes = 32;
    private const int HashSizeBytes = 32;

    // Brute force protection constants
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public AuthenticationManager(ServerConfiguration configuration)
    {
        _configuration = configuration;
        _tokenExpiration = TimeSpan.FromHours(configuration.TokenExpirationHours);

        // Initialize master admin user if master password is set
        if (!string.IsNullOrEmpty(configuration.MasterPassword))
        {
            RegisterUser("admin", configuration.MasterPassword);
        }

        // Initialize cleanup timer for login attempts
        _cleanupTimer = new System.Timers.Timer(TimeSpan.FromMinutes(5).TotalMilliseconds);
        _cleanupTimer.Elapsed += (sender, e) => CleanupExpiredLockouts();
        _cleanupTimer.AutoReset = true;
        _cleanupTimer.Start();
    }

    private void CleanupExpiredLockouts()
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in _loginAttempts)
        {
            if (now > kvp.Value.LockoutEnd || (kvp.Value.FailedAttempts == 0))
            {
                _loginAttempts.TryRemove(kvp.Key, out _);
            }
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
        if (!_users.TryGetValue(username, out var credentials))
            return null;

        // Check if user is locked out
        var now = DateTime.UtcNow;
        var attemptTracker = _loginAttempts.GetOrAdd(username, _ => new LoginAttemptTracker());

        bool isLockedOut = false;
        lock (attemptTracker)
        {
            if (attemptTracker.FailedAttempts >= MaxFailedAttempts)
            {
                if (now < attemptTracker.LockoutEnd)
                {
                    isLockedOut = true;
                }
                else
                {
                    // Lockout expired, reset attempts
                    attemptTracker.FailedAttempts = 0;
                }
            }
        }

        // Verify password using constant-time comparison to prevent timing attacks
        // We evaluate this even if the user is locked out to prevent timing attacks from revealing lockout state
        bool isPasswordValid = VerifyPassword(password, credentials.Salt, credentials.PasswordHash);

        if (isLockedOut)
        {
            return null;
        }

        if (!isPasswordValid)
        {
            // Record failed attempt
            lock (attemptTracker)
            {
                attemptTracker.FailedAttempts++;
                if (attemptTracker.FailedAttempts >= MaxFailedAttempts)
                {
                    attemptTracker.LockoutEnd = now.Add(LockoutDuration);
                }
            }
            return null;
        }

        // Reset failed attempts on successful login
        lock (attemptTracker)
        {
            attemptTracker.FailedAttempts = 0;
        }

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

/// <summary>
/// Tracks login attempts for brute force protection.
/// </summary>
public class LoginAttemptTracker
{
    public int FailedAttempts { get; set; }
    public DateTime LockoutEnd { get; set; }
}
