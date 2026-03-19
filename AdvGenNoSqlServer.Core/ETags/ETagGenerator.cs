// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Core.ETags;

/// <summary>
/// Implementation of ETag generation using content-based hashing
/// Thread-safe for concurrent use
/// </summary>
public class ETagGenerator : IETagGenerator
{
    private readonly ETagOptions _options;
    private static readonly ThreadLocal<MD5?> Md5Pool = new(() => MD5.Create());
    private static readonly ThreadLocal<SHA256?> Sha256Pool = new(() => SHA256.Create());
    private static readonly ThreadLocal<SHA512?> Sha512Pool = new(() => SHA512.Create());

    /// <summary>
    /// Creates a new ETagGenerator with default options
    /// </summary>
    public ETagGenerator() : this(ETagOptions.Default) { }

    /// <summary>
    /// Creates a new ETagGenerator with specified options
    /// </summary>
    /// <param name="options">ETag generation options</param>
    public ETagGenerator(ETagOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public string GenerateETag(Document document)
    {
        if (document == null)
            throw new ArgumentNullException(nameof(document));

        if (_options.UseWeakETagsByDefault)
        {
            return GenerateWeakETag(document);
        }

        var hash = ComputeHash(document, false);
        return FormatETag(hash, false);
    }

    /// <inheritdoc />
    public string GenerateWeakETag(Document document)
    {
        if (document == null)
            throw new ArgumentNullException(nameof(document));

        var hash = ComputeHash(document, true);
        return FormatETag(hash, true);
    }

    /// <inheritdoc />
    public bool ValidateETag(Document document, string? eTag)
    {
        if (document == null)
            throw new ArgumentNullException(nameof(document));

        if (string.IsNullOrWhiteSpace(eTag))
            return false;

        // Normalize the provided ETag
        var normalizedProvided = NormalizeETag(eTag);
        
        // Check if provided ETag is weak
        var isWeak = IsWeakETag(eTag);
        
        // Generate expected ETag
        var expectedHash = ComputeHash(document, isWeak);
        var normalizedExpected = NormalizeETag(FormatETag(expectedHash, isWeak));

        return string.Equals(normalizedProvided, normalizedExpected, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public bool ValidateWeakETag(Document document, string? eTag)
    {
        if (document == null)
            throw new ArgumentNullException(nameof(document));

        if (string.IsNullOrWhiteSpace(eTag))
            return false;

        // For weak validation, we always use weak ETag generation
        var weakHash = ComputeHash(document, true);
        var normalizedExpected = NormalizeETag(FormatETag(weakHash, true));
        var normalizedProvided = NormalizeETag(eTag);

        return string.Equals(normalizedProvided, normalizedExpected, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public bool IsWeakETag(string? eTag)
    {
        if (string.IsNullOrWhiteSpace(eTag))
            return false;

        var normalized = eTag.Trim();
        return normalized.StartsWith("W/", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public string NormalizeETag(string? eTag)
    {
        if (string.IsNullOrWhiteSpace(eTag))
            return string.Empty;

        var normalized = eTag.Trim();
        
        // Remove weak prefix
        if (normalized.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[2..].Trim();
        }
        
        // Remove quotes
        if (normalized.StartsWith("\"") && normalized.EndsWith("\""))
        {
            normalized = normalized[1..^1];
        }

        return normalized;
    }

    /// <summary>
    /// Computes the hash for a document based on current options
    /// </summary>
    private byte[] ComputeHash(Document document, bool weak)
    {
        var content = BuildContentForHash(document, weak);
        var contentBytes = Encoding.UTF8.GetBytes(content);

        return _options.HashAlgorithm switch
        {
            ETagHashAlgorithm.MD5 => ComputeMd5(contentBytes),
            ETagHashAlgorithm.SHA512 => ComputeSha512(contentBytes),
            ETagHashAlgorithm.CRC32 => ComputeCrc32(contentBytes),
            _ => ComputeSha256(contentBytes)
        };
    }

    /// <summary>
    /// Builds the content string to be hashed
    /// </summary>
    private string BuildContentForHash(Document document, bool weak)
    {
        var sb = new StringBuilder();

        // Always include document ID
        sb.Append(document.Id);
        sb.Append('|');

        if (weak)
        {
            // Weak ETags: version and timestamp only (semantic equivalence)
            if (_options.IncludeVersion)
            {
                sb.Append('v');
                sb.Append(document.Version);
                sb.Append('|');
            }

            if (_options.IncludeUpdatedAt)
            {
                sb.Append('t');
                sb.Append(new DateTimeOffset(document.UpdatedAt).ToUnixTimeMilliseconds());
            }
        }
        else
        {
            // Strong ETags: include content hash
            if (_options.IncludeVersion)
            {
                sb.Append('v');
                sb.Append(document.Version);
                sb.Append('|');
            }

            if (_options.IncludeUpdatedAt)
            {
                sb.Append('t');
                sb.Append(new DateTimeOffset(document.UpdatedAt).ToUnixTimeMilliseconds());
                sb.Append('|');
            }

            if (_options.IncludeContent && document.Data != null)
            {
                sb.Append('c');
                // Use stable JSON serialization for consistent hashing
                var json = JsonSerializer.Serialize(document.Data, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                });
                sb.Append(json);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Formats the hash bytes as an ETag string
    /// </summary>
    private string FormatETag(byte[] hash, bool weak)
    {
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        
        // Truncate if needed
        if (hex.Length > _options.MaxETagLength)
        {
            hex = hex[.._options.MaxETagLength];
        }

        // Add weak prefix if needed
        if (weak)
        {
            hex = "W/" + hex;
        }

        return hex;
    }

    private byte[] ComputeSha256(byte[] data)
    {
        var sha256 = Sha256Pool.Value;
        if (sha256 == null)
        {
            sha256 = SHA256.Create();
            Sha256Pool.Value = sha256;
        }
        return sha256.ComputeHash(data);
    }

    private byte[] ComputeSha512(byte[] data)
    {
        var sha512 = Sha512Pool.Value;
        if (sha512 == null)
        {
            sha512 = SHA512.Create();
            Sha512Pool.Value = sha512;
        }
        return sha512.ComputeHash(data);
    }

    private byte[] ComputeMd5(byte[] data)
    {
        var md5 = Md5Pool.Value;
        if (md5 == null)
        {
            md5 = MD5.Create();
            Md5Pool.Value = md5;
        }
        return md5.ComputeHash(data);
    }

    private byte[] ComputeCrc32(byte[] data)
    {
        // Simple CRC32 implementation
        const uint polynomial = 0xEDB88320;
        uint crc = 0xFFFFFFFF;

        foreach (var b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                crc = (crc >> 1) ^ (polynomial & ~(crc & 1));
            }
        }

        crc ^= 0xFFFFFFFF;
        
        // Convert to bytes (big-endian)
        return new[]
        {
            (byte)((crc >> 24) & 0xFF),
            (byte)((crc >> 16) & 0xFF),
            (byte)((crc >> 8) & 0xFF),
            (byte)(crc & 0xFF)
        };
    }
}

/// <summary>
/// Extension methods for DateTimeOffset
/// </summary>
internal static class DateTimeOffsetExtensions
{
    /// <summary>
    /// Converts DateTimeOffset to Unix time in milliseconds
    /// </summary>
    public static long ToUnixTimeMilliseconds(this DateTimeOffset dateTimeOffset)
    {
        return dateTimeOffset.ToUnixTimeSeconds() * 1000 + dateTimeOffset.Millisecond;
    }
}
