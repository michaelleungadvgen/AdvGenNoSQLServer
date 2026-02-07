// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Security.Cryptography;
using System.Text;
using AdvGenNoSqlServer.Core.Configuration;

namespace AdvGenNoSqlServer.Core.Authentication;

public class AuthenticationManager
{
    private readonly Dictionary<string, UserCredentials> _users = new();
    private readonly Dictionary<string, AuthToken> _activeSessions = new();
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

        if (_users.ContainsKey(username))
            return false;

        var salt = GenerateSalt();
        var hashedPassword = HashPassword(password, salt);

        _users[username] = new UserCredentials
        {
            Username = username,
            PasswordHash = hashedPassword,
            Salt = salt,
            CreatedAt = DateTime.UtcNow
        };

        return true;
    }

    public AuthToken? Authenticate(string username, string password)
    {
        if (!_users.TryGetValue(username, out var credentials))
            return null;

        var hashedPassword = HashPassword(password, credentials.Salt);
        if (hashedPassword != credentials.PasswordHash)
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

    public bool ValidateToken(string tokenId)
    {
        if (!_activeSessions.TryGetValue(tokenId, out var token))
            return false;

        if (DateTime.UtcNow > token.ExpiresAt)
        {
            _activeSessions.Remove(tokenId);
            return false;
        }

        return true;
    }

    public void RevokeToken(string tokenId)
    {
        _activeSessions.Remove(tokenId);
    }

    public void RevokeAllUserTokens(string username)
    {
        var tokensToRemove = _activeSessions
            .Where(kvp => kvp.Value.Username == username)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var tokenId in tokensToRemove)
        {
            _activeSessions.Remove(tokenId);
        }
    }

    public bool ChangePassword(string username, string oldPassword, string newPassword)
    {
        if (!_users.TryGetValue(username, out var credentials))
            return false;

        var hashedOldPassword = HashPassword(oldPassword, credentials.Salt);
        if (hashedOldPassword != credentials.PasswordHash)
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
        if (!_users.Remove(username))
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
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var combined = new byte[saltBytes.Length + passwordBytes.Length];

        Buffer.BlockCopy(saltBytes, 0, combined, 0, saltBytes.Length);
        Buffer.BlockCopy(passwordBytes, 0, combined, saltBytes.Length, passwordBytes.Length);

        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(combined);
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
