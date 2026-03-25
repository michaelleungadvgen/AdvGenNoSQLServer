// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Configuration;
using AdvGenNoSqlServer.Network;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace AdvGenNoSqlServer.Tests
{
    /// <summary>
    /// Unit tests for certificate pinning functionality
    /// </summary>
    public class CertificatePinningTests
    {
        #region CertificatePin Tests

        [Fact]
        public void CertificatePin_Constructor_WithDefaults()
        {
            var pin = new CertificatePin();

            Assert.Equal(string.Empty, pin.Thumbprint);
            Assert.Null(pin.ExpiresAt);
            Assert.Null(pin.Description);
            Assert.False(pin.IsExpired);
        }

        [Fact]
        public void CertificatePin_Constructor_WithValues()
        {
            var expiresAt = DateTime.UtcNow.AddYears(1);
            var pin = new CertificatePin("ABCDEF123456", expiresAt, "Test Pin");

            Assert.Equal("ABCDEF123456", pin.Thumbprint);
            Assert.Equal(expiresAt, pin.ExpiresAt);
            Assert.Equal("Test Pin", pin.Description);
            Assert.False(pin.IsExpired);
        }

        [Fact]
        public void CertificatePin_IsExpired_WhenExpired()
        {
            var expiresAt = DateTime.UtcNow.AddDays(-1);
            var pin = new CertificatePin("ABCDEF123456", expiresAt);

            Assert.True(pin.IsExpired);
        }

        [Fact]
        public void CertificatePin_Matches_CaseInsensitive()
        {
            var pin = new CertificatePin("ABCDEF123456");

            Assert.True(pin.Matches("abcdef123456"));
            Assert.True(pin.Matches("ABCDEF123456"));
            Assert.True(pin.Matches("AbCdEf123456"));
            Assert.False(pin.Matches("XYZ789"));
        }

        [Fact]
        public void CertificatePin_Matches_WithSeparators()
        {
            var pin = new CertificatePin("ABCDEF1234567890");

            Assert.True(pin.Matches("AB:CD:EF:12:34:56:78:90"));
            Assert.True(pin.Matches("AB-CD-EF-12-34-56-78-90"));
            Assert.True(pin.Matches("AB CD EF 12 34 56 78 90"));
        }

        [Fact]
        public void CertificatePin_Validate_ValidThumbprint()
        {
            // SHA-256 thumbprint is 64 hex characters
            var validThumbprint = "A" + new string('B', 63);
            var pin = new CertificatePin(validThumbprint);

            Assert.True(pin.Validate());
        }

        [Fact]
        public void CertificatePin_Validate_InvalidThumbprint()
        {
            var pin = new CertificatePin("TOOSHORT");
            Assert.False(pin.Validate());

            var emptyPin = new CertificatePin("");
            Assert.False(emptyPin.Validate());

            var whitespacePin = new CertificatePin("   ");
            Assert.False(whitespacePin.Validate());
        }

        [Fact]
        public void CertificatePin_Clone_CreatesCopy()
        {
            var expiresAt = DateTime.UtcNow.AddYears(1);
            var original = new CertificatePin("ABCDEF123456", expiresAt, "Original");
            var clone = original.Clone();

            Assert.Equal(original.Thumbprint, clone.Thumbprint);
            Assert.Equal(original.ExpiresAt, clone.ExpiresAt);
            Assert.Equal(original.Description, clone.Description);

            // Verify it's a copy, not the same reference
            clone.Thumbprint = "MODIFIED";
            Assert.Equal("ABCDEF123456", original.Thumbprint);
        }

        [Fact]
        public void CertificatePin_NormalizeThumbprint_RemovesSeparators()
        {
            Assert.Equal("ABCDEF123456", CertificatePin.NormalizeThumbprint("AB:CD:EF:12:34:56"));
            Assert.Equal("ABCDEF123456", CertificatePin.NormalizeThumbprint("AB-CD-EF-12-34-56"));
            Assert.Equal("ABCDEF123456", CertificatePin.NormalizeThumbprint("AB CD EF 12 34 56"));
            Assert.Equal("ABCDEF123456", CertificatePin.NormalizeThumbprint("  AB CD EF 12 34 56  "));
        }

        [Fact]
        public void CertificatePin_IsValidThumbprintFormat_ValidatesSHA256()
        {
            // Valid SHA-256 is 64 hex characters
            var validThumbprint = new string('A', 64);
            Assert.True(CertificatePin.IsValidThumbprintFormat(validThumbprint));

            // Too short
            Assert.False(CertificatePin.IsValidThumbprintFormat(new string('A', 32)));

            // Too long
            Assert.False(CertificatePin.IsValidThumbprintFormat(new string('A', 65)));

            // Invalid characters
            Assert.False(CertificatePin.IsValidThumbprintFormat(new string('G', 64)));

            // Empty
            Assert.False(CertificatePin.IsValidThumbprintFormat(""));
            Assert.False(CertificatePin.IsValidThumbprintFormat(null!));
        }

        #endregion

        #region CertificatePinningOptions Tests

        [Fact]
        public void CertificatePinningOptions_Constructor_Defaults()
        {
            var options = new CertificatePinningOptions();

            Assert.False(options.Enabled);
            Assert.Empty(options.Pins);
            Assert.True(options.EnforceStrict);
            Assert.False(options.IgnoreExpiredPins);
        }

        [Fact]
        public void CertificatePinningOptions_Constructor_SinglePin()
        {
            var options = new CertificatePinningOptions("THUMBPRINT123");

            Assert.True(options.Enabled);
            Assert.Single(options.Pins);
            Assert.Equal("THUMBPRINT123", options.Pins[0].Thumbprint);
        }

        [Fact]
        public void CertificatePinningOptions_Constructor_MultiplePins()
        {
            var options = new CertificatePinningOptions("PIN1", "PIN2", "PIN3");

            Assert.True(options.Enabled);
            Assert.Equal(3, options.Pins.Count);
        }

        [Fact]
        public void CertificatePinningOptions_AddPin()
        {
            var options = new CertificatePinningOptions();
            var expiresAt = DateTime.UtcNow.AddYears(1);

            options.AddPin("PIN1", expiresAt);

            Assert.True(options.Enabled);
            Assert.Single(options.Pins);
            Assert.Equal("PIN1", options.Pins[0].Thumbprint);
            Assert.Equal(expiresAt, options.Pins[0].ExpiresAt);
        }

        [Fact]
        public void CertificatePinningOptions_AddPin_WithExpiration()
        {
            var options = new CertificatePinningOptions();

            options.AddPinWithExpiration("PIN1", TimeSpan.FromDays(30));

            Assert.True(options.Enabled);
            Assert.Single(options.Pins);
            Assert.True(options.Pins[0].ExpiresAt > DateTime.UtcNow.AddDays(29));
            Assert.True(options.Pins[0].ExpiresAt < DateTime.UtcNow.AddDays(31));
        }

        [Fact]
        public void CertificatePinningOptions_Validate_Disabled()
        {
            var options = new CertificatePinningOptions { Enabled = false };
            Assert.True(options.Validate());
        }

        [Fact]
        public void CertificatePinningOptions_Validate_EnabledNoPins()
        {
            var options = new CertificatePinningOptions { Enabled = true };
            Assert.False(options.Validate());
        }

        [Fact]
        public void CertificatePinningOptions_Validate_EnabledWithPins()
        {
            // Use valid 64-character SHA-256 thumbprints
            var options = new CertificatePinningOptions(
                "A" + new string('B', 63), 
                "C" + new string('D', 63)) { Enabled = true };
            Assert.True(options.Validate());
        }

        [Fact]
        public void CertificatePinningOptions_RemoveExpiredPins()
        {
            var options = new CertificatePinningOptions();
            options.Pins.Add(new CertificatePin("VALID", DateTime.UtcNow.AddDays(1)));
            options.Pins.Add(new CertificatePin("EXPIRED1", DateTime.UtcNow.AddDays(-1)));
            options.Pins.Add(new CertificatePin("EXPIRED2", DateTime.UtcNow.AddDays(-2)));

            var removed = options.RemoveExpiredPins();

            Assert.Equal(2, removed);
            Assert.Single(options.Pins);
            Assert.Equal("VALID", options.Pins[0].Thumbprint);
        }

        [Fact]
        public void CertificatePinningOptions_ValidPinCount()
        {
            var options = new CertificatePinningOptions();
            options.Pins.Add(new CertificatePin("VALID1", DateTime.UtcNow.AddDays(1)));
            options.Pins.Add(new CertificatePin("VALID2", null));
            options.Pins.Add(new CertificatePin("EXPIRED", DateTime.UtcNow.AddDays(-1)));

            Assert.Equal(2, options.ValidPinCount);
        }

        [Fact]
        public void CertificatePinningOptions_Clone()
        {
            var original = new CertificatePinningOptions("PIN1", "PIN2")
            {
                Enabled = true,
                EnforceStrict = false,
                IgnoreExpiredPins = true
            };

            var clone = original.Clone();

            Assert.Equal(original.Enabled, clone.Enabled);
            Assert.Equal(original.EnforceStrict, clone.EnforceStrict);
            Assert.Equal(original.IgnoreExpiredPins, clone.IgnoreExpiredPins);
            Assert.Equal(original.Pins.Count, clone.Pins.Count);

            // Verify deep copy
            clone.Pins[0].Thumbprint = "MODIFIED";
            Assert.NotEqual(original.Pins[0].Thumbprint, clone.Pins[0].Thumbprint);
        }

        #endregion

        #region CertificatePinValidator Tests

        [Fact]
        public void CertificatePinValidator_ValidateCertificate_Disabled()
        {
            var options = new CertificatePinningOptions { Enabled = false };
            var cert = TlsStreamHelper.CreateSelfSignedCertificate();

            Assert.True(CertificatePinValidator.ValidateCertificate(cert, options));
        }

        [Fact]
        public void CertificatePinValidator_ValidateCertificate_NoCertificate()
        {
            var options = new CertificatePinningOptions(new string('A', 64)) { Enabled = true };

            // Should fail in strict mode
            Assert.False(CertificatePinValidator.ValidateCertificate(null, options));

            // Should pass in non-strict mode
            options.EnforceStrict = false;
            Assert.True(CertificatePinValidator.ValidateCertificate(null, options));
        }

        [Fact]
        public void CertificatePinValidator_ValidateCertificate_MatchingPin()
        {
            var cert = TlsStreamHelper.CreateSelfSignedCertificate();
            var thumbprint = CertificatePinValidator.ComputeSha256Thumbprint(cert);
            var options = new CertificatePinningOptions(thumbprint) { Enabled = true };

            Assert.True(CertificatePinValidator.ValidateCertificate(cert, options));
        }

        [Fact]
        public void CertificatePinValidator_ValidateCertificate_NonMatchingPin()
        {
            var cert = TlsStreamHelper.CreateSelfSignedCertificate();
            // Use valid 64-character SHA-256 thumbprint format
            var options = new CertificatePinningOptions(new string('0', 64)) 
            { 
                Enabled = true,
                EnforceStrict = true
            };

            Assert.False(CertificatePinValidator.ValidateCertificate(cert, options));
        }

        [Fact]
        public void CertificatePinValidator_ValidateCertificate_ExpiredPin()
        {
            var cert = TlsStreamHelper.CreateSelfSignedCertificate();
            var thumbprint = CertificatePinValidator.ComputeSha256Thumbprint(cert);
            
            var options = new CertificatePinningOptions { Enabled = true, EnforceStrict = true };
            options.Pins.Add(new CertificatePin(thumbprint, DateTime.UtcNow.AddDays(-1))); // Expired

            // When expired pins are not ignored, the expired pin is still checked and matches
            // So validation should pass (pin matches even though it's expired)
            Assert.True(CertificatePinValidator.ValidateCertificate(cert, options));

            // When expired pins are ignored, the pin should not be checked
            // So validation should fail (no valid pins match)
            options.IgnoreExpiredPins = true;
            Assert.False(CertificatePinValidator.ValidateCertificate(cert, options));
        }

        [Fact]
        public void CertificatePinValidator_ValidateCertificate_MultiplePins()
        {
            var cert = TlsStreamHelper.CreateSelfSignedCertificate();
            var thumbprint = CertificatePinValidator.ComputeSha256Thumbprint(cert);

            var options = new CertificatePinningOptions("OTHERPIN123456789012345678901234567890123456789012345678901234", thumbprint)
            {
                Enabled = true
            };

            Assert.True(CertificatePinValidator.ValidateCertificate(cert, options));
        }

        [Fact]
        public void CertificatePinValidator_ValidateAndEnforce_Success()
        {
            var cert = TlsStreamHelper.CreateSelfSignedCertificate();
            var thumbprint = CertificatePinValidator.ComputeSha256Thumbprint(cert);
            var options = new CertificatePinningOptions(thumbprint) { Enabled = true };

            // Should not throw
            CertificatePinValidator.ValidateAndEnforce(cert, options);
        }

        [Fact]
        public void CertificatePinValidator_ValidateAndEnforce_Failure()
        {
            var cert = TlsStreamHelper.CreateSelfSignedCertificate();
            var options = new CertificatePinningOptions("NONMATCHINGPIN12345678901234567890123456789012345678901234567890")
            {
                Enabled = true,
                EnforceStrict = true
            };

            Assert.Throws<CertificatePinningException>(() => 
                CertificatePinValidator.ValidateAndEnforce(cert, options));
        }

        [Fact]
        public void CertificatePinValidator_ValidateAndEnforce_NonStrict()
        {
            var cert = TlsStreamHelper.CreateSelfSignedCertificate();
            var options = new CertificatePinningOptions("NONMATCHINGPIN12345678901234567890123456789012345678901234567890")
            {
                Enabled = true,
                EnforceStrict = false
            };

            // Should not throw in non-strict mode
            CertificatePinValidator.ValidateAndEnforce(cert, options);
        }

        [Fact]
        public void CertificatePinValidator_IsCertificatePinned_Match()
        {
            var cert = TlsStreamHelper.CreateSelfSignedCertificate();
            var thumbprint = CertificatePinValidator.ComputeSha256Thumbprint(cert);
            var options = new CertificatePinningOptions(thumbprint) { Enabled = true };

            Assert.True(CertificatePinValidator.IsCertificatePinned(cert, options, out var matchedPin));
            Assert.NotNull(matchedPin);
            Assert.Equal(thumbprint, matchedPin.Thumbprint);
        }

        [Fact]
        public void CertificatePinValidator_IsCertificatePinned_NoMatch()
        {
            var cert = TlsStreamHelper.CreateSelfSignedCertificate();
            var options = new CertificatePinningOptions("NONMATCHINGPIN12345678901234567890123456789012345678901234567890")
            {
                Enabled = true
            };

            Assert.False(CertificatePinValidator.IsCertificatePinned(cert, options, out var matchedPin));
            Assert.Null(matchedPin);
        }

        [Fact]
        public void CertificatePinValidator_IsCertificatePinned_Disabled()
        {
            var cert = TlsStreamHelper.CreateSelfSignedCertificate();
            var options = new CertificatePinningOptions { Enabled = false };

            // Returns true when disabled
            Assert.True(CertificatePinValidator.IsCertificatePinned(cert, options, out var matchedPin));
            Assert.Null(matchedPin);
        }

        [Fact]
        public void CertificatePinValidator_ComputeSha256Thumbprint_Consistent()
        {
            var cert = TlsStreamHelper.CreateSelfSignedCertificate();
            
            var thumbprint1 = CertificatePinValidator.ComputeSha256Thumbprint(cert);
            var thumbprint2 = CertificatePinValidator.ComputeSha256Thumbprint(cert);

            Assert.Equal(thumbprint1, thumbprint2);
            Assert.Equal(64, thumbprint1.Length); // SHA-256 is 64 hex chars
            Assert.True(thumbprint1.All(c => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F')));
        }

        [Fact]
        public void CertificatePinValidator_ComputeSha256Thumbprint_FromBytes()
        {
            var cert = TlsStreamHelper.CreateSelfSignedCertificate();
            var rawData = cert.GetRawCertData();
            
            var thumbprint1 = CertificatePinValidator.ComputeSha256Thumbprint(cert);
            var thumbprint2 = CertificatePinValidator.ComputeSha256Thumbprint(rawData);

            Assert.Equal(thumbprint1, thumbprint2);
        }

        [Fact]
        public void CertificatePinValidator_GetValidPins()
        {
            var options = new CertificatePinningOptions { Enabled = true };
            options.Pins.Add(new CertificatePin("VALID1", DateTime.UtcNow.AddDays(1)));
            options.Pins.Add(new CertificatePin("VALID2", null));
            options.Pins.Add(new CertificatePin("EXPIRED", DateTime.UtcNow.AddDays(-1)));

            var validPins = CertificatePinValidator.GetValidPins(options).ToList();

            Assert.Equal(2, validPins.Count);
            Assert.Contains(validPins, p => p.Thumbprint == "VALID1");
            Assert.Contains(validPins, p => p.Thumbprint == "VALID2");
        }

        [Fact]
        public void CertificatePinValidator_HasValidPins()
        {
            Assert.False(CertificatePinValidator.HasValidPins(null));

            var disabled = new CertificatePinningOptions("PIN") { Enabled = false };
            Assert.False(CertificatePinValidator.HasValidPins(disabled));

            var enabled = new CertificatePinningOptions("PIN") { Enabled = true };
            Assert.True(CertificatePinValidator.HasValidPins(enabled));

            var withExpired = new CertificatePinningOptions { Enabled = true };
            withExpired.Pins.Add(new CertificatePin("EXPIRED", DateTime.UtcNow.AddDays(-1)));
            Assert.False(CertificatePinValidator.HasValidPins(withExpired));
        }

        [Fact]
        public void CertificatePinValidator_CreatePinFromCertificate()
        {
            var cert = TlsStreamHelper.CreateSelfSignedCertificate();
            var expiresAt = DateTime.UtcNow.AddYears(1);

            var pin = CertificatePinValidator.CreatePinFromCertificate(cert, expiresAt);

            Assert.NotNull(pin);
            Assert.Equal(64, pin.Thumbprint.Length);
            Assert.Equal(expiresAt, pin.ExpiresAt);
            Assert.Equal(cert.Subject, pin.Description);
        }

        #endregion

        #region PinValidationEventArgs Tests

        [Fact]
        public void PinValidationEventArgs_Constructor()
        {
            var cert = TlsStreamHelper.CreateSelfSignedCertificate();
            var thumbprint = "TESTTHUMBPRINT";

            var args = new PinValidationEventArgs(cert, thumbprint, true, 5);

            Assert.Equal(cert, args.Certificate);
            Assert.Equal(thumbprint, args.CertificateThumbprint);
            Assert.True(args.IsPinned);
            Assert.Equal(5, args.PinCount);
            Assert.False(args.ShouldFail); // IsPinned is true
        }

        [Fact]
        public void PinValidationEventArgs_ShouldFail_WhenNotPinned()
        {
            var args = new PinValidationEventArgs(null, "THUMB", false, 3);

            Assert.True(args.ShouldFail);
        }

        [Fact]
        public void PinValidationEventArgs_MutableProperties()
        {
            var cert = TlsStreamHelper.CreateSelfSignedCertificate();
            var args = new PinValidationEventArgs(cert, "THUMB", true, 3);

            args.ShouldFail = true;
            args.FailureMessage = "Custom failure";
            args.MatchedPin = new CertificatePin("PIN");

            Assert.True(args.ShouldFail);
            Assert.Equal("Custom failure", args.FailureMessage);
            Assert.NotNull(args.MatchedPin);
        }

        #endregion

        #region CertificatePinningException Tests

        [Fact]
        public void CertificatePinningException_Constructor_Message()
        {
            var ex = new CertificatePinningException("Test message");

            Assert.Equal("Test message", ex.Message);
        }

        [Fact]
        public void CertificatePinningException_Constructor_WithInner()
        {
            var inner = new InvalidOperationException("Inner");
            var ex = new CertificatePinningException("Outer", inner);

            Assert.Equal("Outer", ex.Message);
            Assert.Equal(inner, ex.InnerException);
        }

        [Fact]
        public void CertificatePinningException_Constructor_Full()
        {
            var cert = TlsStreamHelper.CreateSelfSignedCertificate();
            var ex = new CertificatePinningException(
                "Full message",
                cert,
                "THUMBPRINT",
                5,
                true);

            Assert.Equal("Full message", ex.Message);
            Assert.Equal(cert, ex.Certificate);
            Assert.Equal("THUMBPRINT", ex.CertificateThumbprint);
            Assert.Equal(5, ex.PinCount);
            Assert.True(ex.StrictMode);
        }

        [Fact]
        public void CertificatePinningException_PinValidationFailed()
        {
            var cert = TlsStreamHelper.CreateSelfSignedCertificate();
            var ex = CertificatePinningException.PinValidationFailed(cert, "BADTHUMB", 3, true);

            Assert.NotNull(ex);
            Assert.Contains("BADTHUMB", ex.Message);
            Assert.Contains("3", ex.Message);
            Assert.True(ex.StrictMode);
        }

        [Fact]
        public void CertificatePinningException_PinValidationFailed_NonStrict()
        {
            var cert = TlsStreamHelper.CreateSelfSignedCertificate();
            var ex = CertificatePinningException.PinValidationFailed(cert, "BADTHUMB", 3, false);

            Assert.NotNull(ex);
            Assert.Contains("warning", ex.Message);
            Assert.Contains("non-strict mode", ex.Message);
            Assert.False(ex.StrictMode);
        }

        [Fact]
        public void CertificatePinningException_NoValidPinsConfigured()
        {
            var ex = CertificatePinningException.NoValidPinsConfigured();

            Assert.NotNull(ex);
            Assert.Contains("no valid pins", ex.Message);
        }

        [Fact]
        public void CertificatePinningException_InvalidThumbprintFormat()
        {
            var ex = CertificatePinningException.InvalidThumbprintFormat("badthumb");

            Assert.NotNull(ex);
            Assert.Contains("badthumb", ex.Message);
            Assert.Contains("64-character", ex.Message);
        }

        #endregion

        #region ServerConfiguration Integration Tests

        [Fact]
        public void ServerConfiguration_CertificatePinningConfig_Default()
        {
            var config = new ServerConfiguration();

            Assert.Null(config.CertificatePinningConfig);
        }

        [Fact]
        public void ServerConfiguration_CertificatePinningConfig_SetAndGet()
        {
            var config = new ServerConfiguration
            {
                CertificatePinningConfig = new CertificatePinningConfiguration
                {
                    Enabled = true,
                    Thumbprints = new List<string> { "PIN1", "PIN2" },
                    EnforceStrict = false
                }
            };

            Assert.NotNull(config.CertificatePinningConfig);
            Assert.True(config.CertificatePinningConfig.Enabled);
            Assert.Equal(2, config.CertificatePinningConfig.Thumbprints.Count);
            Assert.False(config.CertificatePinningConfig.EnforceStrict);
        }

        [Fact]
        public void CertificatePinningConfiguration_Validate_Disabled()
        {
            var config = new CertificatePinningConfiguration { Enabled = false };
            Assert.True(config.Validate());
        }

        [Fact]
        public void CertificatePinningConfiguration_Validate_EnabledNoPins()
        {
            var config = new CertificatePinningConfiguration { Enabled = true };
            Assert.False(config.Validate());
        }

        [Fact]
        public void CertificatePinningConfiguration_Validate_EnabledWithPins()
        {
            var config = new CertificatePinningConfiguration
            {
                Enabled = true,
                Thumbprints = new List<string> { "PIN1", "PIN2" }
            };
            Assert.True(config.Validate());
        }

        [Fact]
        public void CertificatePinningConfiguration_PinExpirations()
        {
            var expiresAt = DateTime.UtcNow.AddYears(1);
            var config = new CertificatePinningConfiguration
            {
                Enabled = true,
                Thumbprints = new List<string> { "PIN1" },
                PinExpirations = new Dictionary<string, DateTime>
                {
                    { "PIN1", expiresAt }
                }
            };

            Assert.NotNull(config.PinExpirations);
            Assert.Single(config.PinExpirations);
            Assert.Equal(expiresAt, config.PinExpirations["PIN1"]);
        }

        #endregion

        #region TlsStreamHelper Integration Tests

        [Fact]
        public void TlsStreamHelper_ToPinningOptions_NullConfig()
        {
            var options = TlsStreamHelper.ToPinningOptions(null);

            Assert.NotNull(options);
            Assert.False(options.Enabled);
        }

        [Fact]
        public void TlsStreamHelper_ToPinningOptions_Disabled()
        {
            var config = new CertificatePinningConfiguration { Enabled = false };
            var options = TlsStreamHelper.ToPinningOptions(config);

            Assert.NotNull(options);
            Assert.False(options.Enabled);
        }

        [Fact]
        public void TlsStreamHelper_ToPinningOptions_Enabled()
        {
            var config = new CertificatePinningConfiguration
            {
                Enabled = true,
                Thumbprints = new List<string> { "PIN1", "PIN2" },
                EnforceStrict = false,
                IgnoreExpiredPins = true
            };

            var options = TlsStreamHelper.ToPinningOptions(config);

            Assert.NotNull(options);
            Assert.True(options.Enabled);
            Assert.Equal(2, options.Pins.Count);
            Assert.False(options.EnforceStrict);
            Assert.True(options.IgnoreExpiredPins);
        }

        [Fact]
        public void TlsStreamHelper_ToPinningOptions_WithExpirations()
        {
            var expiresAt = DateTime.UtcNow.AddDays(30);
            var config = new CertificatePinningConfiguration
            {
                Enabled = true,
                Thumbprints = new List<string> { "PIN1", "PIN2" },
                PinExpirations = new Dictionary<string, DateTime>
                {
                    { "PIN1", expiresAt }
                }
            };

            var options = TlsStreamHelper.ToPinningOptions(config);

            Assert.NotNull(options);
            Assert.Equal(2, options.Pins.Count);
            
            var pin1 = options.Pins.First(p => p.Thumbprint == "PIN1");
            var pin2 = options.Pins.First(p => p.Thumbprint == "PIN2");
            
            Assert.Equal(expiresAt, pin1.ExpiresAt);
            Assert.Null(pin2.ExpiresAt);
        }

        #endregion

        #region Event Tests

        [Fact]
        public void CertificatePinValidator_PinValidated_EventRaised()
        {
            var cert = TlsStreamHelper.CreateSelfSignedCertificate();
            var thumbprint = CertificatePinValidator.ComputeSha256Thumbprint(cert);
            var options = new CertificatePinningOptions(thumbprint) { Enabled = true };

            PinValidationEventArgs? capturedArgs = null;
            PinValidationEventHandler handler = (sender, args) =>
            {
                capturedArgs = args;
            };
            CertificatePinValidator.PinValidated += handler;

            try
            {
                CertificatePinValidator.ValidateCertificate(cert, options);

                Assert.NotNull(capturedArgs);
                Assert.True(capturedArgs.IsPinned);
                Assert.Equal(thumbprint, capturedArgs.CertificateThumbprint);
            }
            finally
            {
                CertificatePinValidator.PinValidated -= handler;
            }
        }

        [Fact]
        public void CertificatePinValidator_PinValidated_EventAllowsOverride()
        {
            var cert = TlsStreamHelper.CreateSelfSignedCertificate();
            var options = new CertificatePinningOptions("WRONGPIN123456789012345678901234567890123456789012345678901234")
            {
                Enabled = true,
                EnforceStrict = true
            };

            // Event handler can override the failure
            PinValidationEventHandler handler = (sender, args) =>
            {
                args.ShouldFail = false;
            };
            CertificatePinValidator.PinValidated += handler;

            try
            {
                // Should pass because event handler overrides
                Assert.True(CertificatePinValidator.ValidateCertificate(cert, options));
            }
            finally
            {
                CertificatePinValidator.PinValidated -= handler;
            }
        }

        #endregion
    }
}
