// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AdvGenNoSqlServer.Core.Configuration;

namespace AdvGenNoSqlServer.Core.Authentication;

/// <summary>
/// JWT (JSON Web Token) provider implementation using HMAC-SHA256
/// Follows RFC 7519 specification
/// </summary>
public class JwtTokenProvider : IJwtTokenProvider
{
    private readonly string _secretKey;
    private readonly TimeSpan _defaultExpiration;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Creates a new JwtTokenProvider with the specified configuration
    /// </summary>
    public JwtTokenProvider(ServerConfiguration configuration)
    {
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        // Use configured secret key or generate a warning if using default
        _secretKey = configuration.JwtSecretKey ?? GenerateSecureSecret();
        _defaultExpiration = TimeSpan.FromHours(configuration.TokenExpirationHours);
        _issuer = configuration.JwtIssuer ?? "AdvGenNoSqlServer";
        _audience = configuration.JwtAudience ?? "AdvGenNoSqlClient";

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    /// <summary>
    /// Creates a new JwtTokenProvider with explicit parameters
    /// </summary>
    public JwtTokenProvider(string secretKey, TimeSpan defaultExpiration, string issuer = "AdvGenNoSqlServer", string audience = "AdvGenNoSqlClient")
    {
        if (string.IsNullOrWhiteSpace(secretKey))
            throw new ArgumentException("Secret key cannot be empty", nameof(secretKey));

        _secretKey = secretKey;
        _defaultExpiration = defaultExpiration;
        _issuer = issuer;
        _audience = audience;

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    /// <inheritdoc />
    public string GenerateToken(string username, IEnumerable<string> roles, IEnumerable<string> permissions)
    {
        return GenerateToken(username, roles, permissions, _defaultExpiration);
    }

    /// <inheritdoc />
    public string GenerateToken(string username, IEnumerable<string> roles, IEnumerable<string> permissions, TimeSpan expiration)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username cannot be empty", nameof(username));

        var now = DateTime.UtcNow;
        var expiresAt = now.Add(expiration);

        // Create JWT header
        var header = new JwtHeader
        {
            Alg = "HS256",
            Typ = "JWT"
        };

        // Create JWT payload (claims)
        var payload = new JwtPayload
        {
            Sub = username,
            Iss = _issuer,
            Aud = _audience,
            Iat = ToUnixTimeSeconds(now),
            Exp = ToUnixTimeSeconds(expiresAt),
            Nbf = ToUnixTimeSeconds(now),
            Jti = Guid.NewGuid().ToString("N"),
            Roles = roles.ToList(),
            Permissions = permissions.ToList()
        };

        // Encode header and payload
        var encodedHeader = Base64UrlEncode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(header, _jsonOptions)));
        var encodedPayload = Base64UrlEncode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, _jsonOptions)));

        // Create signature
        var signingInput = $"{encodedHeader}.{encodedPayload}";
        var signature = ComputeHmacSha256Signature(signingInput, _secretKey);
        var encodedSignature = Base64UrlEncode(signature);

        // Combine all parts
        return $"{encodedHeader}.{encodedPayload}.{encodedSignature}";
    }

    /// <inheritdoc />
    public TokenValidationResult ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return TokenValidationResult.Failed("Token is empty");

        try
        {
            // Split token into parts
            var parts = token.Split('.');
            if (parts.Length != 3)
                return TokenValidationResult.Failed("Invalid token format");

            var encodedHeader = parts[0];
            var encodedPayload = parts[1];
            var encodedSignature = parts[2];

            // Verify signature
            var signingInput = $"{encodedHeader}.{encodedPayload}";
            var expectedSignature = ComputeHmacSha256Signature(signingInput, _secretKey);
            var actualSignature = Base64UrlDecode(encodedSignature);

            if (!CryptographicOperations.FixedTimeEquals(expectedSignature, actualSignature))
                return TokenValidationResult.Failed("Invalid token signature");

            // Decode and parse payload
            var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(encodedPayload));
            var payload = JsonSerializer.Deserialize<JwtPayload>(payloadJson, _jsonOptions);

            if (payload == null)
                return TokenValidationResult.Failed("Failed to parse token payload");

            // Validate expiration
            var now = DateTime.UtcNow;
            if (payload.Exp.HasValue && FromUnixTimeSeconds(payload.Exp.Value) < now)
                return TokenValidationResult.Failed("Token has expired");

            // Validate not-before time
            if (payload.Nbf.HasValue && FromUnixTimeSeconds(payload.Nbf.Value) > now)
                return TokenValidationResult.Failed("Token is not yet valid");

            // Validate issuer if configured
            if (!string.IsNullOrEmpty(_issuer) && payload.Iss != _issuer)
                return TokenValidationResult.Failed("Invalid token issuer");

            // Validate audience if configured
            if (!string.IsNullOrEmpty(_audience) && payload.Aud != _audience)
                return TokenValidationResult.Failed("Invalid token audience");

            // Extract expiration time
            DateTime? expirationTime = payload.Exp.HasValue 
                ? FromUnixTimeSeconds(payload.Exp.Value) 
                : null;

            return TokenValidationResult.Success(
                payload.Sub ?? string.Empty,
                payload.Roles ?? new List<string>(),
                payload.Permissions ?? new List<string>(),
                expirationTime ?? DateTime.MaxValue
            );
        }
        catch (FormatException ex)
        {
            return TokenValidationResult.Failed($"Token format error: {ex.Message}");
        }
        catch (JsonException ex)
        {
            return TokenValidationResult.Failed($"Token parsing error: {ex.Message}");
        }
        catch (Exception ex)
        {
            return TokenValidationResult.Failed($"Token validation error: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public string? RefreshToken(string token)
    {
        var validationResult = ValidateToken(token);
        
        if (!validationResult.IsValid)
            return null;

        // Generate new token with same claims but new expiration
        return GenerateToken(
            validationResult.Username!, 
            validationResult.Roles, 
            validationResult.Permissions,
            _defaultExpiration
        );
    }

    /// <inheritdoc />
    public string? ExtractUsername(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
                return null;

            var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            var payload = JsonSerializer.Deserialize<JwtPayload>(payloadJson, _jsonOptions);

            return payload?.Sub;
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public DateTime? GetExpirationTime(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
                return null;

            var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            var payload = JsonSerializer.Deserialize<JwtPayload>(payloadJson, _jsonOptions);

            if (payload?.Exp.HasValue == true)
                return FromUnixTimeSeconds(payload.Exp.Value);

            return null;
        }
        catch
        {
            return null;
        }
    }

    #region Private Helper Methods

    /// <summary>
    /// Generates a secure random secret key
    /// </summary>
    private static string GenerateSecureSecret()
    {
        var bytes = new byte[64];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(bytes);
        }
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Computes HMAC-SHA256 signature
    /// </summary>
    private static byte[] ComputeHmacSha256Signature(string input, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(input));
    }

    /// <summary>
    /// Converts DateTime to Unix time seconds
    /// </summary>
    private static long ToUnixTimeSeconds(DateTime dateTime)
    {
        return new DateTimeOffset(dateTime).ToUnixTimeSeconds();
    }

    /// <summary>
    /// Converts Unix time seconds to DateTime
    /// </summary>
    private static DateTime FromUnixTimeSeconds(long seconds)
    {
        return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
    }

    /// <summary>
    /// Encodes bytes to Base64Url string (RFC 4648)
    /// </summary>
    private static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// Decodes Base64Url string to bytes
    /// </summary>
    private static byte[] Base64UrlDecode(string input)
    {
        var base64 = input
            .Replace('-', '+')
            .Replace('_', '/');

        // Add padding if necessary
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }

        return Convert.FromBase64String(base64);
    }

    #endregion

    #region JWT Data Classes

    /// <summary>
    /// JWT Header
    /// </summary>
    private class JwtHeader
    {
        [JsonPropertyName("alg")]
        public string? Alg { get; set; }

        [JsonPropertyName("typ")]
        public string? Typ { get; set; }
    }

    /// <summary>
    /// JWT Payload (Claims)
    /// </summary>
    private class JwtPayload
    {
        /// <summary>
        /// Subject - username
        /// </summary>
        [JsonPropertyName("sub")]
        public string? Sub { get; set; }

        /// <summary>
        /// Issuer
        /// </summary>
        [JsonPropertyName("iss")]
        public string? Iss { get; set; }

        /// <summary>
        /// Audience
        /// </summary>
        [JsonPropertyName("aud")]
        public string? Aud { get; set; }

        /// <summary>
        /// Issued At
        /// </summary>
        [JsonPropertyName("iat")]
        public long? Iat { get; set; }

        /// <summary>
        /// Expiration Time
        /// </summary>
        [JsonPropertyName("exp")]
        public long? Exp { get; set; }

        /// <summary>
        /// Not Before
        /// </summary>
        [JsonPropertyName("nbf")]
        public long? Nbf { get; set; }

        /// <summary>
        /// JWT ID
        /// </summary>
        [JsonPropertyName("jti")]
        public string? Jti { get; set; }

        /// <summary>
        /// Roles claim
        /// </summary>
        [JsonPropertyName("roles")]
        public List<string>? Roles { get; set; }

        /// <summary>
        /// Permissions claim
        /// </summary>
        [JsonPropertyName("permissions")]
        public List<string>? Permissions { get; set; }
    }

    #endregion
}
