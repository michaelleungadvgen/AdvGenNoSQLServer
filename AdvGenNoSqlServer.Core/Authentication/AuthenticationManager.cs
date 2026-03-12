// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using AdvGenNoSqlServer.Core.Configuration;

namespace AdvGenNoSqlServer.Core.Authentication;

public class AuthenticationManager
{
    private readonly ConcurrentDictionary<string, UserCredentials> _users = new();
    private readonly ConcurrentDictionary<string, AuthToken> _activeSessions = new();
    private readonly TimeSpan _tokenExpiration;
    private readonly ServerConfiguration _configuration;

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

    public bool RegisterUser(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return false;

        var salt = GenerateSalt();
        var hashedPassword = HashPassword(password, salt);

        return _users.TryAdd(username, new UserCredentials
        {
            Username = username,
            PasswordHash = hashedPassword,
            Salt = salt,
            CreatedAt = DateTime.UtcNow
        });
    }

    public AuthToken? Authenticate(string username, string password)
    {
        if (!_users.TryGetValue(username, out var credentials))
            return null;

        var hashedBytes = Convert.FromBase64String(HashPassword(password, credentials.Salt));
        var storedBytes = Convert.FromBase64String(credentials.PasswordHash);

        if (!CryptographicOperations.FixedTimeEquals(hashedBytes, storedBytes))
            return null;

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

    public AuthToken? GetToken(string tokenId)
    {
        _activeSessions.TryGetValue(tokenId, out var token);
        return token;
    }

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

    public void RevokeToken(string tokenId)
    {
        _activeSessions.TryRemove(tokenId, out _);
    }

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

    public bool ChangePassword(string username, string oldPassword, string newPassword)
    {
        if (!_users.TryGetValue(username, out var credentials))
            return false;

        var hashedOldBytes = Convert.FromBase64String(HashPassword(oldPassword, credentials.Salt));
        var storedBytes = Convert.FromBase64String(credentials.PasswordHash);

        if (!CryptographicOperations.FixedTimeEquals(hashedOldBytes, storedBytes))
            return false;

        var newSalt = GenerateSalt();
        var hashedNewPassword = HashPassword(newPassword, newSalt);

        credentials.PasswordHash = hashedNewPassword;
        credentials.Salt = newSalt;

        RevokeAllUserTokens(username);
        return true;
    }

    public bool RemoveUser(string username)
    {
        if (!_users.TryRemove(username, out _))
            return false;

        RevokeAllUserTokens(username);
        return true;
    }

    private static string GenerateSalt()
    {
        var saltBytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(saltBytes);
        }
        return Convert.ToBase64String(saltBytes);
    }

    private static string HashPassword(string password, string salt)
    {
        var saltBytes = Convert.FromBase64String(salt);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            saltBytes,
            iterations: 100000,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: 32);
        return Convert.ToBase64String(hash);
    }
}

public class UserCredentials
{
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    public required string Salt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AuthToken
{
    public required string TokenId { get; set; }
    public required string Username { get; set; }
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
