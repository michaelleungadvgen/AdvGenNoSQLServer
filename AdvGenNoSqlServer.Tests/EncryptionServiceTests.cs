// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Security.Cryptography;
using System.Text;
using AdvGenNoSqlServer.Core.Authentication;
using AdvGenNoSqlServer.Core.Configuration;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Unit tests for the EncryptionService
/// </summary>
public class EncryptionServiceTests
{
    private readonly EncryptionService _encryptionService;
    private readonly byte[] _testKey;

    public EncryptionServiceTests()
    {
        _testKey = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(_testKey);
        }
        _encryptionService = new EncryptionService(_testKey);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidKey_ShouldSucceed()
    {
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);

        var service = new EncryptionService(key);

        Assert.NotNull(service);
        Assert.NotNull(service.CurrentKeyId);
    }

    [Fact]
    public void Constructor_WithNullKey_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new EncryptionService((byte[])null!));
    }

    [Fact]
    public void Constructor_WithWrongKeySize_ShouldThrowArgumentException()
    {
        var shortKey = new byte[16]; // 128 bits instead of 256

        var ex = Assert.Throws<ArgumentException>(() => new EncryptionService(shortKey));
        Assert.Contains("256 bits", ex.Message);
    }

    [Fact]
    public void Constructor_WithConfiguration_ShouldSucceed()
    {
        var config = new ServerConfiguration
        {
            EncryptionKey = Convert.ToBase64String(_testKey),
            EncryptionKeyId = "test-key-1"
        };

        var service = new EncryptionService(config);

        Assert.NotNull(service);
        Assert.Equal("test-key-1", service.CurrentKeyId);
    }

    [Fact]
    public void Constructor_WithConfiguration_NoKey_ShouldGenerateKey()
    {
        var config = new ServerConfiguration();

        var service = new EncryptionService(config);

        Assert.NotNull(service);
        Assert.NotNull(service.CurrentKeyId);
    }

    [Fact]
    public void Constructor_WithConfiguration_InvalidKeySize_ShouldThrow()
    {
        var config = new ServerConfiguration
        {
            EncryptionKey = Convert.ToBase64String(new byte[16]) // Wrong size
        };

        Assert.Throws<ArgumentException>(() => new EncryptionService(config));
    }

    #endregion

    #region String Encryption Tests

    [Fact]
    public void Encrypt_String_WithDefaultKey_ShouldReturnNonEmpty()
    {
        var plaintext = "Hello, World!";

        var ciphertext = _encryptionService.Encrypt(plaintext);

        Assert.False(string.IsNullOrEmpty(ciphertext));
        Assert.NotEqual(plaintext, ciphertext);
    }

    [Fact]
    public void Encrypt_EmptyString_ShouldReturnEmptyString()
    {
        var ciphertext = _encryptionService.Encrypt(string.Empty);

        Assert.Equal(string.Empty, ciphertext);
    }

    [Fact]
    public void Encrypt_NullString_ShouldReturnEmptyString()
    {
        var ciphertext = _encryptionService.Encrypt((string)null!);

        Assert.Equal(string.Empty, ciphertext);
    }

    [Fact]
    public void EncryptDecrypt_String_ShouldReturnOriginal()
    {
        var plaintext = "Sensitive data that needs encryption!";

        var ciphertext = _encryptionService.Encrypt(plaintext);
        var decrypted = _encryptionService.Decrypt(ciphertext);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void EncryptDecrypt_StringWithUnicode_ShouldReturnOriginal()
    {
        var plaintext = "Hello 世界 🌍 مرحبا עולם";

        var ciphertext = _encryptionService.Encrypt(plaintext);
        var decrypted = _encryptionService.Decrypt(ciphertext);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void EncryptDecrypt_StringWithSpecialChars_ShouldReturnOriginal()
    {
        var plaintext = "<script>alert('xss')</script> & \"quotes\" 'apostrophes'\n\t\\r";

        var ciphertext = _encryptionService.Encrypt(plaintext);
        var decrypted = _encryptionService.Decrypt(ciphertext);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_SamePlaintext_ShouldProduceDifferentCiphertexts()
    {
        var plaintext = "Test data";

        var ciphertext1 = _encryptionService.Encrypt(plaintext);
        var ciphertext2 = _encryptionService.Encrypt(plaintext);

        Assert.NotEqual(ciphertext1, ciphertext2); // Due to random nonce
    }

    [Fact]
    public void Encrypt_WithDifferentKeys_ShouldProduceDifferentCiphertexts()
    {
        var plaintext = "Test data";
        var key1 = new byte[32];
        var key2 = new byte[32];
        RandomNumberGenerator.Fill(key1);
        RandomNumberGenerator.Fill(key2);

        var ciphertext1 = _encryptionService.Encrypt(plaintext, key1);
        var ciphertext2 = _encryptionService.Encrypt(plaintext, key2);

        Assert.NotEqual(ciphertext1, ciphertext2);
    }

    #endregion

    #region Binary Encryption Tests

    [Fact]
    public void Encrypt_BinaryData_ShouldReturnNonEmpty()
    {
        var plaintext = Encoding.UTF8.GetBytes("Binary data");

        var ciphertext = _encryptionService.Encrypt(plaintext);

        Assert.NotNull(ciphertext);
        Assert.True(ciphertext.Length > 0);
        Assert.NotEqual(plaintext, ciphertext);
    }

    [Fact]
    public void EncryptDecrypt_BinaryData_ShouldReturnOriginal()
    {
        var plaintext = new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE, 0xFD };

        var ciphertext = _encryptionService.Encrypt(plaintext);
        var decrypted = _encryptionService.Decrypt(ciphertext);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_EmptyBinary_ShouldReturnEmptyArray()
    {
        var ciphertext = _encryptionService.Encrypt(Array.Empty<byte>());

        Assert.NotNull(ciphertext);
        Assert.Empty(ciphertext);
    }

    [Fact]
    public void Encrypt_LargeBinaryData_ShouldWork()
    {
        var plaintext = new byte[1024 * 1024]; // 1 MB
        RandomNumberGenerator.Fill(plaintext);

        var ciphertext = _encryptionService.Encrypt(plaintext);
        var decrypted = _encryptionService.Decrypt(ciphertext);

        Assert.Equal(plaintext, decrypted);
    }

    #endregion

    #region Decryption Failure Tests

    [Fact]
    public void Decrypt_WithWrongKey_ShouldThrowEncryptionException()
    {
        var plaintext = "Secret message";
        var wrongKey = new byte[32];
        RandomNumberGenerator.Fill(wrongKey);

        var ciphertext = _encryptionService.Encrypt(plaintext);

        Assert.Throws<EncryptionException>(() => _encryptionService.Decrypt(ciphertext, wrongKey));
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_ShouldThrowEncryptionException()
    {
        var plaintext = "Secret message";
        var ciphertext = _encryptionService.Encrypt(plaintext);
        var bytes = Convert.FromBase64String(ciphertext);

        // Tamper with the ciphertext
        bytes[bytes.Length - 1] ^= 0xFF;
        var tamperedCiphertext = Convert.ToBase64String(bytes);

        Assert.Throws<EncryptionException>(() => _encryptionService.Decrypt(tamperedCiphertext));
    }

    [Fact]
    public void Decrypt_InvalidBase64_ShouldThrowEncryptionException()
    {
        Assert.Throws<EncryptionException>(() => _encryptionService.Decrypt("not-valid-base64!!!"));
    }

    [Fact]
    public void Decrypt_TooShortCiphertext_ShouldThrowEncryptionException()
    {
        var shortCiphertext = Convert.ToBase64String(new byte[10]); // Less than 28 bytes

        var ex = Assert.Throws<EncryptionException>(() => _encryptionService.Decrypt(shortCiphertext));
        Assert.Contains("too short", ex.Message);
    }

    [Fact]
    public void Decrypt_EmptyString_ShouldReturnEmptyString()
    {
        var decrypted = _encryptionService.Decrypt(string.Empty);

        Assert.Equal(string.Empty, decrypted);
    }

    #endregion

    #region Key Derivation Tests

    [Fact]
    public void DeriveKeyFromPassword_WithValidInputs_ShouldReturnKey()
    {
        var password = "MySecurePassword123!";

        var derivedKey = _encryptionService.DeriveKeyFromPassword(password);

        Assert.NotNull(derivedKey);
        Assert.True(derivedKey.Length > 32); // Salt (32) + Key (32)
    }

    [Fact]
    public void DeriveKeyFromPassword_SamePasswordDifferentSalt_ShouldProduceDifferentKeys()
    {
        var password = "MySecurePassword123!";

        var derivedKey1 = _encryptionService.DeriveKeyFromPassword(password);
        var derivedKey2 = _encryptionService.DeriveKeyFromPassword(password);

        Assert.NotEqual(derivedKey1, derivedKey2);
    }

    [Fact]
    public void DeriveKeyFromPassword_WithCustomIterations_ShouldWork()
    {
        var password = "MySecurePassword123!";

        var derivedKey = _encryptionService.DeriveKeyFromPassword(password, iterations: 50000);

        Assert.NotNull(derivedKey);
    }

    [Fact]
    public void DeriveKeyFromPassword_WithCustomSalt_ShouldWork()
    {
        var password = "MySecurePassword123!";
        var salt = new byte[32];
        RandomNumberGenerator.Fill(salt);

        var derivedKey = _encryptionService.DeriveKeyFromPassword(password, salt);

        Assert.NotNull(derivedKey);
    }

    [Fact]
    public void DeriveKeyFromPassword_WithNullPassword_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => _encryptionService.DeriveKeyFromPassword(null!));
    }

    [Fact]
    public void DeriveKeyFromPassword_WithEmptyPassword_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => _encryptionService.DeriveKeyFromPassword(string.Empty));
    }

    [Fact]
    public void DeriveKeyFromPassword_WithTooFewIterations_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => _encryptionService.DeriveKeyFromPassword("password", iterations: 500));
    }

    [Fact]
    public void ExtractSaltAndKey_WithValidInput_ShouldReturnBoth()
    {
        var password = "MySecurePassword123!";
        var derivedKey = _encryptionService.DeriveKeyFromPassword(password);

        var (salt, key) = EncryptionService.ExtractSaltAndKey(derivedKey);

        Assert.NotNull(salt);
        Assert.NotNull(key);
        Assert.Equal(32, salt.Length);
        Assert.Equal(32, key.Length);
    }

    [Fact]
    public void DeriveKey_ThenEncryptDecrypt_ShouldWork()
    {
        var password = "MySecurePassword123!";
        var plaintext = "Secret data";
        var derivedKey = _encryptionService.DeriveKeyFromPassword(password);
        var (_, key) = EncryptionService.ExtractSaltAndKey(derivedKey);

        var ciphertext = _encryptionService.Encrypt(plaintext, key);
        var decrypted = _encryptionService.Decrypt(ciphertext, key);

        Assert.Equal(plaintext, decrypted);
    }

    #endregion

    #region Key Generation Tests

    [Fact]
    public void GenerateKey_Default256Bits_ShouldReturn32Bytes()
    {
        var key = _encryptionService.GenerateKey();

        Assert.NotNull(key);
        Assert.Equal(32, key.Length);
    }

    [Fact]
    public void GenerateKey_128Bits_ShouldReturn16Bytes()
    {
        var key = _encryptionService.GenerateKey(128);

        Assert.NotNull(key);
        Assert.Equal(16, key.Length);
    }

    [Fact]
    public void GenerateKey_192Bits_ShouldReturn24Bytes()
    {
        var key = _encryptionService.GenerateKey(192);

        Assert.NotNull(key);
        Assert.Equal(24, key.Length);
    }

    [Fact]
    public void GenerateKey_MultipleCalls_ShouldReturnDifferentKeys()
    {
        var key1 = _encryptionService.GenerateKey();
        var key2 = _encryptionService.GenerateKey();

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void GenerateKey_InvalidSize_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => _encryptionService.GenerateKey(64));
    }

    #endregion

    #region Key Rotation Tests

    [Fact]
    public void RotateKey_WithValidKeys_ShouldReencrypt()
    {
        var plaintext = "Secret message";
        var oldKey = _testKey;
        var newKey = new byte[32];
        RandomNumberGenerator.Fill(newKey);

        var originalCiphertext = _encryptionService.Encrypt(plaintext, oldKey);
        var rotatedCiphertext = _encryptionService.RotateKey(originalCiphertext, oldKey, newKey);
        var decrypted = _encryptionService.Decrypt(rotatedCiphertext, newKey);

        Assert.NotEqual(originalCiphertext, rotatedCiphertext);
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void RotateKey_EmptyCiphertext_ShouldReturnEmpty()
    {
        var newKey = new byte[32];
        RandomNumberGenerator.Fill(newKey);

        var result = _encryptionService.RotateKey(string.Empty, _testKey, newKey);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void RotateKey_WithWrongOldKey_ShouldThrow()
    {
        var plaintext = "Secret message";
        var wrongKey = new byte[32];
        RandomNumberGenerator.Fill(wrongKey);
        var newKey = new byte[32];
        RandomNumberGenerator.Fill(newKey);

        var ciphertext = _encryptionService.Encrypt(plaintext, _testKey);

        Assert.Throws<EncryptionException>(() => _encryptionService.RotateKey(ciphertext, wrongKey, newKey));
    }

    #endregion

    #region Validation Tests

    [Fact]
    public void IsValidCiphertext_WithValidCiphertext_ShouldReturnTrue()
    {
        var ciphertext = _encryptionService.Encrypt("test");

        Assert.True(_encryptionService.IsValidCiphertext(ciphertext));
    }

    [Fact]
    public void IsValidCiphertext_WithEmptyString_ShouldReturnFalse()
    {
        Assert.False(_encryptionService.IsValidCiphertext(string.Empty));
    }

    [Fact]
    public void IsValidCiphertext_WithNull_ShouldReturnFalse()
    {
        Assert.False(_encryptionService.IsValidCiphertext(null!));
    }

    [Fact]
    public void IsValidCiphertext_WithInvalidBase64_ShouldReturnFalse()
    {
        Assert.False(_encryptionService.IsValidCiphertext("not-valid!!!"));
    }

    [Fact]
    public void IsValidCiphertext_WithTooShortData_ShouldReturnFalse()
    {
        var shortData = Convert.ToBase64String(new byte[10]);
        Assert.False(_encryptionService.IsValidCiphertext(shortData));
    }

    [Fact]
    public void IsValidCiphertext_WithRandomValidBase64_ShouldReturnTrue()
    {
        // 28 bytes = nonce (12) + tag (16) minimum
        var randomData = new byte[28];
        RandomNumberGenerator.Fill(randomData);
        var base64 = Convert.ToBase64String(randomData);

        Assert.True(_encryptionService.IsValidCiphertext(base64));
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void EncryptDecrypt_VeryLongText_ShouldWork()
    {
        var plaintext = new string('A', 1000000); // 1 million characters

        var ciphertext = _encryptionService.Encrypt(plaintext);
        var decrypted = _encryptionService.Decrypt(ciphertext);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void EncryptDecrypt_SingleCharacter_ShouldWork()
    {
        var plaintext = "X";

        var ciphertext = _encryptionService.Encrypt(plaintext);
        var decrypted = _encryptionService.Decrypt(ciphertext);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void EncryptDecrypt_OnlyWhitespace_ShouldWork()
    {
        var plaintext = "   \t\n\r   ";

        var ciphertext = _encryptionService.Encrypt(plaintext);
        var decrypted = _encryptionService.Decrypt(ciphertext);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void EncryptDecrypt_BinaryAsString_ShouldWork()
    {
        var plaintext = Convert.ToBase64String(new byte[] { 0x00, 0x01, 0xFF });

        var ciphertext = _encryptionService.Encrypt(plaintext);
        var decrypted = _encryptionService.Decrypt(ciphertext);

        Assert.Equal(plaintext, decrypted);
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public void EncryptDecrypt_ConcurrentAccess_ShouldWork()
    {
        var plaintexts = Enumerable.Range(0, 100).Select(i => $"Message {i}").ToList();
        var results = new List<string>();

        Parallel.ForEach(plaintexts, plaintext =>
        {
            var ciphertext = _encryptionService.Encrypt(plaintext);
            var decrypted = _encryptionService.Decrypt(ciphertext);
            lock (results)
            {
                results.Add(decrypted);
            }
        });

        Assert.Equal(plaintexts.Count, results.Count);
        foreach (var plaintext in plaintexts)
        {
            Assert.Contains(plaintext, results);
        }
    }

    #endregion
}
