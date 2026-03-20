// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Configuration;
using System.Security.Cryptography.X509Certificates;

namespace AdvGenNoSqlServer.Network
{
    /// <summary>
    /// Interface for certificate reloading services that monitor and reload SSL/TLS certificates
    /// </summary>
    public interface ICertificateReloader : IDisposable
    {
        /// <summary>
        /// Gets the current certificate
        /// </summary>
        X509Certificate2? CurrentCertificate { get; }

        /// <summary>
        /// Gets the statistics for certificate reload operations
        /// </summary>
        CertificateReloadStatistics Statistics { get; }

        /// <summary>
        /// Gets a value indicating whether hot-reload is enabled
        /// </summary>
        bool IsHotReloadEnabled { get; }

        /// <summary>
        /// Event raised when the certificate is reloaded
        /// </summary>
        event EventHandler<CertificateReloadedEventArgs>? CertificateReloaded;

        /// <summary>
        /// Event raised when certificate reload fails
        /// </summary>
        event EventHandler<CertificateReloadFailedEventArgs>? CertificateReloadFailed;

        /// <summary>
        /// Starts monitoring for certificate changes
        /// </summary>
        Task StartAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops monitoring for certificate changes
        /// </summary>
        Task StopAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Manually triggers a certificate reload
        /// </summary>
        Task<bool> ReloadAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the certificate path being monitored (if file-based)
        /// </summary>
        string? MonitoredPath { get; }
    }

    /// <summary>
    /// Configuration options for certificate hot-reload
    /// </summary>
    public class CertificateReloadOptions
    {
        /// <summary>
        /// Gets or sets whether certificate hot-reload is enabled
        /// </summary>
        public bool EnableHotReload { get; set; } = true;

        /// <summary>
        /// Gets or sets the debounce interval in milliseconds for file change events
        /// </summary>
        public int DebounceIntervalMs { get; set; } = 1000;

        /// <summary>
        /// Gets or sets whether to validate the new certificate before switching
        /// </summary>
        public bool ValidateBeforeSwitch { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to fall back to the previous certificate on validation failure
        /// </summary>
        public bool FallbackOnFailure { get; set; } = true;

        /// <summary>
        /// Validates the options
        /// </summary>
        public void Validate()
        {
            if (DebounceIntervalMs < 0)
                throw new ArgumentException("Debounce interval must be non-negative", nameof(DebounceIntervalMs));

            if (DebounceIntervalMs > 60000)
                throw new ArgumentException("Debounce interval must not exceed 60 seconds", nameof(DebounceIntervalMs));
        }
    }

    /// <summary>
    /// Event arguments for certificate reload events
    /// </summary>
    public class CertificateReloadedEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the previous certificate (may be null on initial load)
        /// </summary>
        public X509Certificate2? PreviousCertificate { get; }

        /// <summary>
        /// Gets the new certificate
        /// </summary>
        public X509Certificate2 NewCertificate { get; }

        /// <summary>
        /// Gets the timestamp of the reload
        /// </summary>
        public DateTimeOffset ReloadTime { get; }

        /// <summary>
        /// Gets the reload trigger type
        /// </summary>
        public ReloadTriggerType Trigger { get; }

        /// <summary>
        /// Creates a new instance of CertificateReloadedEventArgs
        /// </summary>
        public CertificateReloadedEventArgs(
            X509Certificate2? previousCertificate,
            X509Certificate2 newCertificate,
            ReloadTriggerType trigger)
        {
            PreviousCertificate = previousCertificate;
            NewCertificate = newCertificate ?? throw new ArgumentNullException(nameof(newCertificate));
            Trigger = trigger;
            ReloadTime = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Event arguments for certificate reload failure events
    /// </summary>
    public class CertificateReloadFailedEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the error that occurred
        /// </summary>
        public Exception Error { get; }

        /// <summary>
        /// Gets the timestamp of the failure
        /// </summary>
        public DateTimeOffset FailureTime { get; }

        /// <summary>
        /// Gets the reload trigger type
        /// </summary>
        public ReloadTriggerType Trigger { get; }

        /// <summary>
        /// Gets whether the operation fell back to the previous certificate
        /// </summary>
        public bool DidFallback { get; }

        /// <summary>
        /// Creates a new instance of CertificateReloadFailedEventArgs
        /// </summary>
        public CertificateReloadFailedEventArgs(Exception error, ReloadTriggerType trigger, bool didFallback)
        {
            Error = error ?? throw new ArgumentNullException(nameof(error));
            Trigger = trigger;
            DidFallback = didFallback;
            FailureTime = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Type of trigger that caused a certificate reload
    /// </summary>
    public enum ReloadTriggerType
    {
        /// <summary>
        /// Manual reload triggered by API call
        /// </summary>
        Manual,

        /// <summary>
        /// File system watcher detected change
        /// </summary>
        FileWatcher,

        /// <summary>
        /// Initial load on startup
        /// </summary>
        InitialLoad,

        /// <summary>
        /// Scheduled/polling reload
        /// </summary>
        Scheduled
    }

    /// <summary>
    /// Statistics for certificate reload operations
    /// </summary>
    public class CertificateReloadStatistics
    {
        private long _reloadCount;
        private long _failureCount;
        private long _fallbackCount;

        /// <summary>
        /// Gets the total number of successful reloads
        /// </summary>
        public long ReloadCount => Interlocked.Read(ref _reloadCount);

        /// <summary>
        /// Gets the total number of failed reloads
        /// </summary>
        public long FailureCount => Interlocked.Read(ref _failureCount);

        /// <summary>
        /// Gets the total number of fallbacks to previous certificate
        /// </summary>
        public long FallbackCount => Interlocked.Read(ref _fallbackCount);

        /// <summary>
        /// Gets the timestamp of the last successful reload
        /// </summary>
        public DateTimeOffset? LastReloadTime { get; private set; }

        /// <summary>
        /// Gets the timestamp of the last failure
        /// </summary>
        public DateTimeOffset? LastFailureTime { get; private set; }

        /// <summary>
        /// Gets the thumbprint of the current certificate
        /// </summary>
        public string? CurrentCertificateThumbprint { get; set; }

        /// <summary>
        /// Gets the not-after date of the current certificate
        /// </summary>
        public DateTimeOffset? CurrentCertificateExpiry { get; set; }

        /// <summary>
        /// Records a successful reload
        /// </summary>
        public void RecordReload(X509Certificate2 certificate)
        {
            Interlocked.Increment(ref _reloadCount);
            LastReloadTime = DateTimeOffset.UtcNow;
            CurrentCertificateThumbprint = certificate.Thumbprint;
            CurrentCertificateExpiry = certificate.NotAfter;
        }

        /// <summary>
        /// Records a failed reload
        /// </summary>
        public void RecordFailure()
        {
            Interlocked.Increment(ref _failureCount);
            LastFailureTime = DateTimeOffset.UtcNow;
        }

        /// <summary>
        /// Records a fallback to previous certificate
        /// </summary>
        public void RecordFallback()
        {
            Interlocked.Increment(ref _fallbackCount);
        }

        /// <summary>
        /// Creates a snapshot of the current statistics
        /// </summary>
        public CertificateReloadStatisticsSnapshot GetSnapshot()
        {
            return new CertificateReloadStatisticsSnapshot
            {
                ReloadCount = ReloadCount,
                FailureCount = FailureCount,
                FallbackCount = FallbackCount,
                LastReloadTime = LastReloadTime,
                LastFailureTime = LastFailureTime,
                CurrentCertificateThumbprint = CurrentCertificateThumbprint,
                CurrentCertificateExpiry = CurrentCertificateExpiry
            };
        }
    }

    /// <summary>
    /// Immutable snapshot of certificate reload statistics
    /// </summary>
    public class CertificateReloadStatisticsSnapshot
    {
        /// <summary>
        /// Gets the total number of successful reloads
        /// </summary>
        public long ReloadCount { get; init; }

        /// <summary>
        /// Gets the total number of failed reloads
        /// </summary>
        public long FailureCount { get; init; }

        /// <summary>
        /// Gets the total number of fallbacks
        /// </summary>
        public long FallbackCount { get; init; }

        /// <summary>
        /// Gets the timestamp of the last successful reload
        /// </summary>
        public DateTimeOffset? LastReloadTime { get; init; }

        /// <summary>
        /// Gets the timestamp of the last failure
        /// </summary>
        public DateTimeOffset? LastFailureTime { get; init; }

        /// <summary>
        /// Gets the thumbprint of the current certificate
        /// </summary>
        public string? CurrentCertificateThumbprint { get; init; }

        /// <summary>
        /// Gets the not-after date of the current certificate
        /// </summary>
        public DateTimeOffset? CurrentCertificateExpiry { get; init; }

        /// <summary>
        /// Gets a value indicating whether the certificate is expired or near expiry
        /// </summary>
        public bool IsNearExpiry => CurrentCertificateExpiry.HasValue &&
            CurrentCertificateExpiry.Value < DateTimeOffset.UtcNow.AddDays(7);
    }

    /// <summary>
    /// Service that monitors certificate files and reloads them automatically
    /// </summary>
    public class CertificateReloader : ICertificateReloader
    {
        private readonly ServerConfiguration _configuration;
        private readonly CertificateReloadOptions _options;
        private readonly CertificateReloadStatistics _statistics;
        private readonly object _lock = new();
        private FileSystemWatcher? _fileWatcher;
        private Timer? _debounceTimer;
        private X509Certificate2? _currentCertificate;
        private bool _isRunning;
        private bool _disposed;

        /// <inheritdoc />
        public X509Certificate2? CurrentCertificate
        {
            get
            {
                ThrowIfDisposed();
                lock (_lock)
                {
                    return _currentCertificate;
                }
            }
        }

        /// <inheritdoc />
        public CertificateReloadStatistics Statistics => _statistics;

        /// <inheritdoc />
        public bool IsHotReloadEnabled => _options.EnableHotReload;

        /// <inheritdoc />
        public string? MonitoredPath { get; private set; }

        /// <inheritdoc />
        public event EventHandler<CertificateReloadedEventArgs>? CertificateReloaded;

        /// <inheritdoc />
        public event EventHandler<CertificateReloadFailedEventArgs>? CertificateReloadFailed;

        /// <summary>
        /// Creates a new instance of CertificateReloader
        /// </summary>
        public CertificateReloader(
            ServerConfiguration configuration,
            CertificateReloadOptions? options = null)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _options = options ?? new CertificateReloadOptions();
            _options.Validate();
            _statistics = new CertificateReloadStatistics();

            // Set monitored path if file-based certificate
            if (!configuration.UseCertificateStore)
            {
                MonitoredPath = configuration.SslCertificatePath;
            }
        }

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            lock (_lock)
            {
                if (_isRunning)
                    return Task.CompletedTask;

                // Load initial certificate
                var certificate = LoadCertificateInternal();
                if (certificate != null)
                {
                    _currentCertificate = certificate;
                    _statistics.RecordReload(certificate);
                    OnCertificateReloaded(new CertificateReloadedEventArgs(null, certificate, ReloadTriggerType.InitialLoad));
                }

                // Setup file watcher if hot-reload is enabled and using file-based certificate
                if (_options.EnableHotReload && !string.IsNullOrWhiteSpace(MonitoredPath))
                {
                    SetupFileWatcher();
                }

                _isRunning = true;
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            lock (_lock)
            {
                if (!_isRunning)
                    return Task.CompletedTask;

                _debounceTimer?.Dispose();
                _debounceTimer = null;

                _fileWatcher?.Dispose();
                _fileWatcher = null;

                _isRunning = false;
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<bool> ReloadAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return Task.FromResult(ReloadCertificate(ReloadTriggerType.Manual));
        }

        private void SetupFileWatcher()
        {
            if (string.IsNullOrWhiteSpace(MonitoredPath))
                return;

            var directory = Path.GetDirectoryName(MonitoredPath);
            var fileName = Path.GetFileName(MonitoredPath);

            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
                return;

            if (!Directory.Exists(directory))
                return;

            _fileWatcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };

            _fileWatcher.Changed += OnCertificateFileChanged;
            _fileWatcher.Renamed += OnCertificateFileRenamed;
        }

        private void OnCertificateFileChanged(object sender, FileSystemEventArgs e)
        {
            // Debounce rapid successive changes
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(
                _ => ReloadCertificate(ReloadTriggerType.FileWatcher),
                null,
                _options.DebounceIntervalMs,
                Timeout.Infinite);
        }

        private void OnCertificateFileRenamed(object sender, RenamedEventArgs e)
        {
            // Only handle if renamed to our target file
            if (string.Equals(e.FullPath, MonitoredPath, StringComparison.OrdinalIgnoreCase))
            {
                _debounceTimer?.Dispose();
                _debounceTimer = new Timer(
                    _ => ReloadCertificate(ReloadTriggerType.FileWatcher),
                    null,
                    _options.DebounceIntervalMs,
                    Timeout.Infinite);
            }
        }

        private bool ReloadCertificate(ReloadTriggerType trigger)
        {
            try
            {
                lock (_lock)
                {
                    if (_disposed)
                        return false;

                    var newCertificate = LoadCertificateInternal();
                    if (newCertificate == null)
                    {
                        _statistics.RecordFailure();
                        OnCertificateReloadFailed(new CertificateReloadFailedEventArgs(
                            new InvalidOperationException("Failed to load certificate"),
                            trigger,
                            false));
                        return false;
                    }

                    // Validate new certificate if enabled
                    if (_options.ValidateBeforeSwitch && !ValidateCertificate(newCertificate))
                    {
                        newCertificate.Dispose();
                        _statistics.RecordFailure();

                        if (_options.FallbackOnFailure && _currentCertificate != null)
                        {
                            _statistics.RecordFallback();
                            OnCertificateReloadFailed(new CertificateReloadFailedEventArgs(
                                new InvalidOperationException("Certificate validation failed, using previous certificate"),
                                trigger,
                                true));
                            return false;
                        }

                        OnCertificateReloadFailed(new CertificateReloadFailedEventArgs(
                            new InvalidOperationException("Certificate validation failed"),
                            trigger,
                            false));
                        return false;
                    }

                    // Swap certificates
                    var previousCertificate = _currentCertificate;
                    _currentCertificate = newCertificate;
                    _statistics.RecordReload(newCertificate);

                    // Dispose previous certificate after a delay to allow in-flight connections to complete
                    if (previousCertificate != null)
                    {
                        Task.Run(async () =>
                        {
                            await Task.Delay(TimeSpan.FromSeconds(30));
                            previousCertificate.Dispose();
                        });
                    }

                    OnCertificateReloaded(new CertificateReloadedEventArgs(previousCertificate, newCertificate, trigger));
                    return true;
                }
            }
            catch (Exception ex)
            {
                _statistics.RecordFailure();
                OnCertificateReloadFailed(new CertificateReloadFailedEventArgs(ex, trigger, false));
                return false;
            }
        }

        private X509Certificate2? LoadCertificateInternal()
        {
            try
            {
                if (_configuration.UseCertificateStore)
                {
                    return TlsStreamHelper.LoadCertificateFromStore(_configuration.SslCertificateThumbprint);
                }
                else
                {
                    return TlsStreamHelper.LoadCertificateFromFile(
                        _configuration.SslCertificatePath,
                        _configuration.SslCertificatePassword);
                }
            }
            catch
            {
                return null;
            }
        }

        private bool ValidateCertificate(X509Certificate2 certificate)
        {
            // Basic validation: check expiration
            if (certificate.NotAfter < DateTime.UtcNow)
                return false;

            if (certificate.NotBefore > DateTime.UtcNow)
                return false;

            // Check if certificate has private key (required for server)
            if (!certificate.HasPrivateKey)
                return false;

            return true;
        }

        private void OnCertificateReloaded(CertificateReloadedEventArgs e)
        {
            CertificateReloaded?.Invoke(this, e);
        }

        private void OnCertificateReloadFailed(CertificateReloadFailedEventArgs e)
        {
            CertificateReloadFailed?.Invoke(this, e);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CertificateReloader));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
                return;

            lock (_lock)
            {
                if (_disposed)
                    return;

                _debounceTimer?.Dispose();
                _fileWatcher?.Dispose();
                _currentCertificate?.Dispose();

                _disposed = true;
            }
        }
    }
}
