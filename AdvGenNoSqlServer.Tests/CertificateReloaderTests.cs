// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Configuration;
using AdvGenNoSqlServer.Network;
using System.Security.Cryptography.X509Certificates;

namespace AdvGenNoSqlServer.Tests
{
    /// <summary>
    /// Unit tests for CertificateReloader functionality
    /// </summary>
    public class CertificateReloaderTests : IDisposable
    {
        private readonly string _testCertPath;
        private readonly string _testCertPath2;
        private readonly string _testCertPassword = "TestPassword123!";
        private X509Certificate2? _testCertificate;
        private X509Certificate2? _testCertificate2;

        public CertificateReloaderTests()
        {
            var testDir = Path.Combine(Path.GetTempPath(), $"cert_reload_tests_{Guid.NewGuid()}");
            Directory.CreateDirectory(testDir);
            _testCertPath = Path.Combine(testDir, "test_cert.pfx");
            _testCertPath2 = Path.Combine(testDir, "test_cert2.pfx");
        }

        public void Dispose()
        {
            // Cleanup test certificates
            try
            {
                var testDir = Path.GetDirectoryName(_testCertPath);
                if (testDir != null && Directory.Exists(testDir))
                {
                    Directory.Delete(testDir, true);
                }
            }
            catch { /* Best effort */ }

            _testCertificate?.Dispose();
            _testCertificate2?.Dispose();
        }

        #region Helper Methods

        private void CreateTestCertificate(string path)
        {
            _testCertificate = TlsStreamHelper.CreateSelfSignedCertificate("CN=localhost", 365);
            TlsStreamHelper.SaveCertificateToFile(_testCertificate, path, _testCertPassword);
        }

        private void CreateSecondTestCertificate(string path)
        {
            _testCertificate2 = TlsStreamHelper.CreateSelfSignedCertificate("CN=localhost-new", 365);
            TlsStreamHelper.SaveCertificateToFile(_testCertificate2, path, _testCertPassword);
        }

        private ServerConfiguration CreateConfiguration(string certPath)
        {
            return new ServerConfiguration
            {
                EnableSsl = true,
                SslCertificatePath = certPath,
                SslCertificatePassword = _testCertPassword,
                UseCertificateStore = false
            };
        }

        #endregion

        #region Constructor Tests

        [Fact]
        public void Constructor_WithValidConfiguration_CreatesInstance()
        {
            // Arrange
            CreateTestCertificate(_testCertPath);
            var config = CreateConfiguration(_testCertPath);

            // Act
            using var reloader = new CertificateReloader(config);

            // Assert
            Assert.NotNull(reloader);
            Assert.NotNull(reloader.Statistics);
            Assert.True(reloader.IsHotReloadEnabled); // Default options have hot-reload enabled
            Assert.Equal(_testCertPath, reloader.MonitoredPath);
        }

        [Fact]
        public void Constructor_WithNullConfiguration_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new CertificateReloader(null!));
        }

        [Fact]
        public void Constructor_WithCertificateStore_MonitoredPathIsNull()
        {
            // Arrange
            var config = new ServerConfiguration
            {
                UseCertificateStore = true,
                SslCertificateThumbprint = "ABCDEF123456"
            };

            // Act
            using var reloader = new CertificateReloader(config);

            // Assert
            Assert.Null(reloader.MonitoredPath);
        }

        [Fact]
        public void Constructor_WithHotReloadEnabled_SetsIsHotReloadEnabled()
        {
            // Arrange
            CreateTestCertificate(_testCertPath);
            var config = CreateConfiguration(_testCertPath);
            var options = new CertificateReloadOptions { EnableHotReload = true };

            // Act
            using var reloader = new CertificateReloader(config, options);

            // Assert
            Assert.True(reloader.IsHotReloadEnabled);
        }

        [Fact]
        public void Constructor_WithInvalidDebounceInterval_ThrowsArgumentException()
        {
            // Arrange
            CreateTestCertificate(_testCertPath);
            var config = CreateConfiguration(_testCertPath);
            var options = new CertificateReloadOptions { DebounceIntervalMs = -1 };

            // Act & Assert
            Assert.Throws<ArgumentException>(() => new CertificateReloader(config, options));
        }

        [Fact]
        public void Constructor_WithExcessiveDebounceInterval_ThrowsArgumentException()
        {
            // Arrange
            CreateTestCertificate(_testCertPath);
            var config = CreateConfiguration(_testCertPath);
            var options = new CertificateReloadOptions { DebounceIntervalMs = 120000 }; // 2 minutes

            // Act & Assert
            Assert.Throws<ArgumentException>(() => new CertificateReloader(config, options));
        }

        #endregion

        #region StartAsync Tests

        [Fact]
        public async Task StartAsync_WithValidCertificate_LoadsCertificate()
        {
            // Arrange
            CreateTestCertificate(_testCertPath);
            var config = CreateConfiguration(_testCertPath);
            using var reloader = new CertificateReloader(config);

            // Act
            await reloader.StartAsync();

            // Assert
            Assert.NotNull(reloader.CurrentCertificate);
            Assert.Equal(_testCertificate!.Thumbprint, reloader.CurrentCertificate.Thumbprint);
        }

        [Fact]
        public async Task StartAsync_WithInvalidCertificatePath_CurrentCertificateIsNull()
        {
            // Arrange
            var config = CreateConfiguration("/nonexistent/path/cert.pfx");
            using var reloader = new CertificateReloader(config);

            // Act
            await reloader.StartAsync();

            // Assert
            Assert.Null(reloader.CurrentCertificate);
        }

        [Fact]
        public async Task StartAsync_UpdatesStatistics()
        {
            // Arrange
            CreateTestCertificate(_testCertPath);
            var config = CreateConfiguration(_testCertPath);
            using var reloader = new CertificateReloader(config);

            // Act
            await reloader.StartAsync();

            // Assert
            Assert.Equal(1, reloader.Statistics.ReloadCount);
            Assert.NotNull(reloader.Statistics.LastReloadTime);
            Assert.NotNull(reloader.Statistics.CurrentCertificateThumbprint);
            Assert.NotNull(reloader.Statistics.CurrentCertificateExpiry);
        }

        [Fact]
        public async Task StartAsync_RaisesCertificateReloadedEvent()
        {
            // Arrange
            CreateTestCertificate(_testCertPath);
            var config = CreateConfiguration(_testCertPath);
            using var reloader = new CertificateReloader(config);
            
            CertificateReloadedEventArgs? eventArgs = null;
            reloader.CertificateReloaded += (s, e) => eventArgs = e;

            // Act
            await reloader.StartAsync();

            // Assert
            Assert.NotNull(eventArgs);
            Assert.Equal(ReloadTriggerType.InitialLoad, eventArgs!.Trigger);
            Assert.NotNull(eventArgs.NewCertificate);
            Assert.Null(eventArgs.PreviousCertificate);
        }

        [Fact]
        public async Task StartAsync_WhenAlreadyRunning_DoesNothing()
        {
            // Arrange
            CreateTestCertificate(_testCertPath);
            var config = CreateConfiguration(_testCertPath);
            using var reloader = new CertificateReloader(config);
            await reloader.StartAsync();
            var firstCert = reloader.CurrentCertificate;

            // Act - Start again
            await reloader.StartAsync();

            // Assert - Should still have the same certificate
            Assert.Equal(firstCert, reloader.CurrentCertificate);
            Assert.Equal(1, reloader.Statistics.ReloadCount);
        }

        #endregion

        #region StopAsync Tests

        [Fact]
        public async Task StopAsync_WhenRunning_StopsFileWatcher()
        {
            // Arrange
            CreateTestCertificate(_testCertPath);
            var config = CreateConfiguration(_testCertPath);
            var options = new CertificateReloadOptions { EnableHotReload = true };
            using var reloader = new CertificateReloader(config, options);
            await reloader.StartAsync();

            // Act
            await reloader.StopAsync();

            // Assert - No exception should be thrown
            Assert.True(true);
        }

        [Fact]
        public async Task StopAsync_WhenNotRunning_DoesNothing()
        {
            // Arrange
            CreateTestCertificate(_testCertPath);
            var config = CreateConfiguration(_testCertPath);
            using var reloader = new CertificateReloader(config);

            // Act & Assert - Should not throw
            await reloader.StopAsync();
        }

        #endregion

        #region Manual Reload Tests

        [Fact]
        public async Task ReloadAsync_WithValidCertificate_ReloadsSuccessfully()
        {
            // Arrange
            CreateTestCertificate(_testCertPath);
            CreateSecondTestCertificate(_testCertPath2);
            
            var config = CreateConfiguration(_testCertPath);
            using var reloader = new CertificateReloader(config);
            await reloader.StartAsync();
            var originalThumbprint = reloader.CurrentCertificate!.Thumbprint;

            // Update config to point to second certificate
            config.SslCertificatePath = _testCertPath2;

            // Act
            var result = await reloader.ReloadAsync();

            // Assert
            Assert.True(result);
            Assert.NotEqual(originalThumbprint, reloader.CurrentCertificate.Thumbprint);
            Assert.Equal(2, reloader.Statistics.ReloadCount);
        }

        [Fact]
        public async Task ReloadAsync_WithInvalidCertificate_ReturnsFalse()
        {
            // Arrange
            CreateTestCertificate(_testCertPath);
            var config = CreateConfiguration(_testCertPath);
            config.SslCertificatePath = "/nonexistent/cert.pfx";
            
            using var reloader = new CertificateReloader(config);
            await reloader.StartAsync();

            // Act
            var result = await reloader.ReloadAsync();

            // Assert
            Assert.False(result);
            Assert.Equal(1, reloader.Statistics.FailureCount);
        }

        [Fact]
        public async Task ReloadAsync_RaisesReloadedEvent()
        {
            // Arrange
            CreateTestCertificate(_testCertPath);
            CreateSecondTestCertificate(_testCertPath2);
            
            var config = CreateConfiguration(_testCertPath);
            using var reloader = new CertificateReloader(config);
            await reloader.StartAsync();

            CertificateReloadedEventArgs? eventArgs = null;
            reloader.CertificateReloaded += (s, e) => eventArgs = e;

            config.SslCertificatePath = _testCertPath2;

            // Act
            await reloader.ReloadAsync();

            // Assert
            Assert.NotNull(eventArgs);
            Assert.Equal(ReloadTriggerType.Manual, eventArgs!.Trigger);
            Assert.NotNull(eventArgs.PreviousCertificate);
            Assert.NotNull(eventArgs.NewCertificate);
        }

        [Fact]
        public async Task ReloadAsync_WithExpiredCertificate_ReturnsFalse()
        {
            // Arrange
            // Create an expired certificate
            var expiredCert = TlsStreamHelper.CreateSelfSignedCertificate("CN=expired", -1);
            TlsStreamHelper.SaveCertificateToFile(expiredCert, _testCertPath, _testCertPassword);
            
            var config = CreateConfiguration(_testCertPath);
            using var reloader = new CertificateReloader(config);

            // Act
            var result = await reloader.ReloadAsync();

            // Assert
            Assert.False(result);
        }

        #endregion

        #region Event Tests

        [Fact]
        public async Task CertificateReloadFailed_RaisedOnInvalidReload()
        {
            // Arrange
            CreateTestCertificate(_testCertPath);
            var config = CreateConfiguration("/nonexistent/cert.pfx");
            using var reloader = new CertificateReloader(config);

            CertificateReloadFailedEventArgs? eventArgs = null;
            reloader.CertificateReloadFailed += (s, e) => eventArgs = e;

            // Act
            await reloader.ReloadAsync();

            // Assert
            Assert.NotNull(eventArgs);
            Assert.Equal(ReloadTriggerType.Manual, eventArgs!.Trigger);
            Assert.False(eventArgs.DidFallback);
            Assert.NotNull(eventArgs.Error);
        }

        [Fact]
        public async Task CertificateReloaded_ContainsCorrectTimestamps()
        {
            // Arrange
            CreateTestCertificate(_testCertPath);
            var config = CreateConfiguration(_testCertPath);
            using var reloader = new CertificateReloader(config);

            CertificateReloadedEventArgs? eventArgs = null;
            reloader.CertificateReloaded += (s, e) => eventArgs = e;

            var beforeStart = DateTimeOffset.UtcNow;

            // Act
            await reloader.StartAsync();

            var afterStart = DateTimeOffset.UtcNow;

            // Assert
            Assert.NotNull(eventArgs);
            Assert.True(eventArgs!.ReloadTime >= beforeStart);
            Assert.True(eventArgs.ReloadTime <= afterStart);
        }

        #endregion

        #region Statistics Tests

        [Fact]
        public async Task Statistics_TracksReloadCount()
        {
            // Arrange
            CreateTestCertificate(_testCertPath);
            CreateSecondTestCertificate(_testCertPath2);
            var config = CreateConfiguration(_testCertPath);
            using var reloader = new CertificateReloader(config);

            // Act - Initial load
            await reloader.StartAsync();
            Assert.Equal(1, reloader.Statistics.ReloadCount);

            // Act - Manual reload with different cert
            config.SslCertificatePath = _testCertPath2;
            await reloader.ReloadAsync();
            Assert.Equal(2, reloader.Statistics.ReloadCount);
        }

        [Fact]
        public async Task Statistics_TracksFailureCount()
        {
            // Arrange
            CreateTestCertificate(_testCertPath);
            var config = CreateConfiguration(_testCertPath);
            using var reloader = new CertificateReloader(config);
            await reloader.StartAsync();

            // Act
            config.SslCertificatePath = "/nonexistent/cert.pfx";
            await reloader.ReloadAsync();

            // Assert
            Assert.Equal(1, reloader.Statistics.FailureCount);
            Assert.NotNull(reloader.Statistics.LastFailureTime);
        }

        [Fact]
        public async Task Statistics_TracksCertificateInfo()
        {
            // Arrange
            CreateTestCertificate(_testCertPath);
            var config = CreateConfiguration(_testCertPath);
            using var reloader = new CertificateReloader(config);

            // Act
            await reloader.StartAsync();

            // Assert
            Assert.Equal(_testCertificate!.Thumbprint, reloader.Statistics.CurrentCertificateThumbprint);
            Assert.True(reloader.Statistics.CurrentCertificateExpiry > DateTimeOffset.UtcNow);
        }

        [Fact]
        public void Statistics_GetSnapshot_ReturnsImmutableCopy()
        {
            // Arrange
            CreateTestCertificate(_testCertPath);
            var config = CreateConfiguration(_testCertPath);
            using var reloader = new CertificateReloader(config);

            // Act
            var snapshot = reloader.Statistics.GetSnapshot();

            // Assert
            Assert.NotNull(snapshot);
            Assert.Equal(reloader.Statistics.ReloadCount, snapshot.ReloadCount);
            Assert.Equal(reloader.Statistics.FailureCount, snapshot.FailureCount);
        }

        [Fact]
        public void StatisticsSnapshot_IsNearExpiry_ReturnsCorrectValue()
        {
            // Arrange
            var stats = new CertificateReloadStatisticsSnapshot
            {
                CurrentCertificateExpiry = DateTimeOffset.UtcNow.AddDays(3) // Within 7 days
            };

            // Act & Assert
            Assert.True(stats.IsNearExpiry);
        }

        [Fact]
        public void StatisticsSnapshot_IsNearExpiry_ReturnsFalseWhenNotNear()
        {
            // Arrange
            var stats = new CertificateReloadStatisticsSnapshot
            {
                CurrentCertificateExpiry = DateTimeOffset.UtcNow.AddDays(30) // More than 7 days
            };

            // Act & Assert
            Assert.False(stats.IsNearExpiry);
        }

        #endregion

        #region Dispose Tests

        [Fact]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            // Arrange
            CreateTestCertificate(_testCertPath);
            var config = CreateConfiguration(_testCertPath);
            var reloader = new CertificateReloader(config);

            // Act & Assert - Should not throw
            reloader.Dispose();
            reloader.Dispose();
        }

        [Fact]
        public async Task Operations_AfterDispose_ThrowObjectDisposedException()
        {
            // Arrange
            CreateTestCertificate(_testCertPath);
            var config = CreateConfiguration(_testCertPath);
            var reloader = new CertificateReloader(config);
            await reloader.StartAsync();
            reloader.Dispose();

            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() => reloader.CurrentCertificate);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => reloader.StartAsync());
            await Assert.ThrowsAsync<ObjectDisposedException>(() => reloader.ReloadAsync());
        }

        #endregion

        #region CertificateReloadOptions Tests

        [Fact]
        public void CertificateReloadOptions_DefaultValues_AreCorrect()
        {
            // Act
            var options = new CertificateReloadOptions();

            // Assert
            Assert.True(options.EnableHotReload);
            Assert.Equal(1000, options.DebounceIntervalMs);
            Assert.True(options.ValidateBeforeSwitch);
            Assert.True(options.FallbackOnFailure);
        }

        [Fact]
        public void CertificateReloadOptions_Validate_DoesNotThrowWithValidOptions()
        {
            // Arrange
            var options = new CertificateReloadOptions
            {
                DebounceIntervalMs = 500
            };

            // Act & Assert - Should not throw
            options.Validate();
        }

        #endregion
    }
}
