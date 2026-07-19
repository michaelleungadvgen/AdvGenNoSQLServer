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
    private readonly ConcurrentDictionary<string, LoginLockoutState> _lockouts = new();
    private readonly TimeSpan _tokenExpiration;
    private readonly ServerConfiguration _configuration;
    private readonly IUserStore? _userStore;
    private readonly object _mutationLock = new();
    private readonly int _pbkdf2Iterations;
    private readonly int _maxFailedLoginAttempts;
    private readonly TimeSpan _lockoutDuration;

    // Iteration count assumed for hashes stored before the count was recorded per user.
    private const int LegacyPbkdf2Iterations = 100000;
    private const int SaltSizeBytes = 32;
    private const int HashSizeBytes = 32;

    public AuthenticationManager(ServerConfiguration configuration)
        : this(configuration, null)
    {
    }

    public AuthenticationManager(ServerConfiguration configuration, IUserStore? userStore)
    {
        _configuration = configuration;
        _tokenExpiration = TimeSpan.FromHours(configuration.TokenExpirationHours);
        _userStore = userStore;
        _pbkdf2Iterations = Math.Max(10_000, configuration.Pbkdf2Iterations);
        _maxFailedLoginAttempts = Math.Max(0, configuration.MaxFailedLoginAttempts);
        _lockoutDuration = TimeSpan.FromMinutes(Math.Max(0, configuration.LockoutMinutes));

        // Load persisted users first
        if (_userStore != null)
        {
            foreach (var u in _userStore.Load())
            {
                _users[u.Username] = new UserCredentials
                {
                    Username = u.Username,
                    PasswordHash = u.PasswordHash,
                    Salt = u.Salt,
                    Role = u.Role,
                    CreatedAt = u.CreatedAt,
                    Iterations = u.Iterations
                };
            }
        }

        // Seed admin from MasterPassword only if no admin-role user exists
        if (!string.IsNullOrEmpty(configuration.MasterPassword) &&
            !_users.Values.Any(c => c.Role == UserRole.Admin))
        {
            RegisterUser("admin", configuration.MasterPassword, UserRole.Admin);
        }
    }

    /// <summary>
    /// Registers a new user with the default (readwrite) role.
    /// </summary>
    public bool RegisterUser(string username, string password)
        => RegisterUser(username, password, UserRole.ReadWrite);

    /// <summary>
    /// Registers a new user with secure password hashing and the given role.
    /// </summary>
    public bool RegisterUser(string username, string password, string role)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return false;

        lock (_mutationLock)
        {
            if (_users.ContainsKey(username))
                return false;

            var (salt, hashedPassword) = HashPassword(password);

            _users[username] = new UserCredentials
            {
                Username = username,
                PasswordHash = hashedPassword,
                Salt = salt,
                Role = UserRole.IsValid(role) ? role : UserRole.ReadWrite,
                CreatedAt = DateTime.UtcNow,
                Iterations = _pbkdf2Iterations
            };

            Persist();
            return true;
        }
    }

    /// <summary>
    /// Authenticates a user and returns an auth token if successful. Subject to
    /// per-account lockout after too many failed attempts; unknown usernames cost
    /// the same derivation work as known ones to blunt user enumeration.
    /// </summary>
    public AuthToken? Authenticate(string username, string password)
    {
        if (IsLockedOut(username))
            return null;

        if (!_users.TryGetValue(username, out var credentials))
        {
            // Dummy derivation: unknown users must not be distinguishable by response time.
            Derive(password, new byte[SaltSizeBytes], _pbkdf2Iterations, HashSizeBytes);
            return null;
        }

        // Verify password using constant-time comparison to prevent timing attacks
        if (!VerifyPassword(password, credentials.Salt, credentials.PasswordHash, EffectiveIterations(credentials)))
        {
            RecordLoginFailure(username);
            return null;
        }

        _lockouts.TryRemove(username, out _);

        var token = new AuthToken
        {
            TokenId = Guid.NewGuid().ToString(),
            Username = username,
            Role = credentials.Role,
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(_tokenExpiration)
        };

        _activeSessions[token.TokenId] = token;
        return token;
    }

    private bool IsLockedOut(string username)
    {
        if (_maxFailedLoginAttempts <= 0)
            return false;

        // States without an active lock (still counting failures) are left alone.
        if (!_lockouts.TryGetValue(username, out var state) || state.LockedUntil == default)
            return false;

        if (state.LockedUntil > DateTime.UtcNow)
            return true;

        // Expired lockout: clear lazily, allowing a fresh set of attempts.
        _lockouts.TryRemove(username, out _);
        return false;
    }

    private void RecordLoginFailure(string username)
    {
        if (_maxFailedLoginAttempts <= 0)
            return;

        // Only existing accounts are tracked, so attackers cannot grow this map
        // with random usernames.
        _lockouts.AddOrUpdate(
            username,
            _ => new LoginLockoutState { FailedAttempts = 1 },
            (_, state) =>
            {
                state.FailedAttempts++;
                if (state.FailedAttempts >= _maxFailedLoginAttempts)
                {
                    state.LockedUntil = DateTime.UtcNow.Add(_lockoutDuration);
                    state.FailedAttempts = 0;
                }
                return state;
            });
    }

    private sealed class LoginLockoutState
    {
        public int FailedAttempts;
        public DateTime LockedUntil;
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

        if (!VerifyPassword(oldPassword, credentials.Salt, credentials.PasswordHash, EffectiveIterations(credentials)))
            return false;

        var (newSalt, hashedNewPassword) = HashPassword(newPassword);

        credentials.PasswordHash = hashedNewPassword;
        credentials.Salt = newSalt;
        credentials.Iterations = _pbkdf2Iterations;

        RevokeAllUserTokens(username);
        Persist();
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
        Persist();
        return true;
    }

    /// <summary>
    /// Sets a user's password without requiring the old password (admin reset).
    /// Revokes the user's active tokens.
    /// </summary>
    public bool SetPassword(string username, string newPassword)
    {
        lock (_mutationLock)
        {
            if (!_users.TryGetValue(username, out var c)) return false;
            var (salt, hash) = HashPassword(newPassword);
            c.Salt = salt;
            c.PasswordHash = hash;
            c.Iterations = _pbkdf2Iterations;
            RevokeAllUserTokens(username);
            Persist();
            return true;
        }
    }

    /// <summary>
    /// Changes a user's role. Takes effect on the user's next authentication.
    /// </summary>
    public bool SetRole(string username, string role)
    {
        lock (_mutationLock)
        {
            if (!UserRole.IsValid(role)) return false;
            if (!_users.TryGetValue(username, out var c)) return false;
            c.Role = role;
            Persist();
            return true;
        }
    }

    /// <summary>
    /// Lists users as (username, role, createdAt) projections. Never exposes hashes.
    /// </summary>
    public IReadOnlyList<(string Username, string Role, DateTime CreatedAt)> ListUsers()
        => _users.Values.Select(c => (c.Username, c.Role, c.CreatedAt)).ToList();

    /// <summary>
    /// Removes a user, refusing to delete the last admin. Returns a precise result code.
    /// </summary>
    public UserOperationResult RemoveUserGuarded(string username)
    {
        lock (_mutationLock)
        {
            if (!_users.ContainsKey(username)) return UserOperationResult.NotFound;
            if (IsLastAdmin(username)) return UserOperationResult.LastAdmin;
            _users.TryRemove(username, out _);
            RevokeAllUserTokens(username);
            Persist();
            return UserOperationResult.Ok;
        }
    }

    /// <summary>
    /// Changes a user's role, refusing to demote the last admin. Returns a precise result code.
    /// </summary>
    public UserOperationResult SetRoleGuarded(string username, string role)
    {
        lock (_mutationLock)
        {
            if (!UserRole.IsValid(role)) return UserOperationResult.InvalidRole;
            if (!_users.TryGetValue(username, out var c)) return UserOperationResult.NotFound;
            if (c.Role == UserRole.Admin && role != UserRole.Admin && IsLastAdmin(username))
                return UserOperationResult.LastAdmin;
            c.Role = role;
            Persist();
            return UserOperationResult.Ok;
        }
    }

    private bool IsLastAdmin(string username)
        => _users.TryGetValue(username, out var c) && c.Role == UserRole.Admin
           && _users.Values.Count(x => x.Role == UserRole.Admin) == 1;

    private void Persist()
    {
        _userStore?.Save(_users.Values.Select(c => new PersistedUser
        {
            Username = c.Username,
            PasswordHash = c.PasswordHash,
            Salt = c.Salt,
            Role = c.Role,
            CreatedAt = c.CreatedAt,
            Iterations = c.Iterations
        }));
    }

    /// <summary>
    /// Gets a copy of all registered users (for testing/admin purposes).
    /// </summary>
    public IReadOnlyDictionary<string, UserCredentials> GetUsers()
    {
        return new Dictionary<string, UserCredentials>(_users);
    }

    /// <summary>
    /// Hashes a password using PBKDF2 with HMAC-SHA256 at the configured iteration count.
    /// Returns the salt and hashed password as base64 strings.
    /// </summary>
    private (string Salt, string Hash) HashPassword(string password)
    {
        // Generate a cryptographically secure random salt
        var saltBytes = new byte[SaltSizeBytes];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(saltBytes);
        }

        var hashBytes = Derive(password, saltBytes, _pbkdf2Iterations, HashSizeBytes);

        return (Convert.ToBase64String(saltBytes), Convert.ToBase64String(hashBytes));
    }

    /// <summary>
    /// Iteration count for verifying a stored hash. Hashes written before the count was
    /// recorded per user (Iterations = 0) are verified with the legacy 100k count.
    /// </summary>
    private static int EffectiveIterations(UserCredentials credentials)
        => credentials.Iterations > 0 ? credentials.Iterations : LegacyPbkdf2Iterations;

    private static byte[] Derive(string password, byte[] saltBytes, int iterations, int outputLength)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(
            password: passwordBytes,
            salt: saltBytes,
            iterations: iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: outputLength);

        // Clear password bytes from memory
        CryptographicOperations.ZeroMemory(passwordBytes);
        return hashBytes;
    }

    /// <summary>
    /// Verifies a password against a stored salt and hash using constant-time comparison.
    /// This prevents timing attacks that could reveal information about the password.
    /// </summary>
    private static bool VerifyPassword(string password, string salt, string hash, int iterations)
    {
        try
        {
            var saltBytes = Convert.FromBase64String(salt);
            var expectedHashBytes = Convert.FromBase64String(hash);

            // Compute hash of provided password
            var actualHashBytes = Derive(password, saltBytes, iterations, expectedHashBytes.Length);

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
    public string Role { get; set; } = UserRole.ReadWrite;
    public DateTime CreatedAt { get; set; }

    /// <summary>PBKDF2 iteration count used for this hash; 0 = legacy 100k.</summary>
    public int Iterations { get; set; }
}

/// <summary>
/// Result of a guarded user-management operation.
/// </summary>
public enum UserOperationResult { Ok, NotFound, LastAdmin, InvalidRole }

/// <summary>
/// Represents an authentication token.
/// </summary>
public class AuthToken
{
    public required string TokenId { get; set; }
    public required string Username { get; set; }
    public string Role { get; set; } = UserRole.ReadWrite;
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
