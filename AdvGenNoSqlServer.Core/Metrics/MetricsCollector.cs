// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AdvGenNoSqlServer.Core.Metrics
{
    /// <summary>
    /// Thread-safe implementation of IMetricsCollector with Prometheus-compatible metrics.
    /// </summary>
    public class MetricsCollector : IMetricsCollector
    {
        private readonly ConcurrentDictionary<string, MetricEntry> _metrics = new();
        private readonly MetricsCollectorOptions _options;
        private readonly Timer? _snapshotTimer;
        private readonly string _namespace;
        private bool _disposed;

        /// <summary>
        /// Creates a new metrics collector with default options.
        /// </summary>
        public MetricsCollector() : this(new MetricsCollectorOptions()) { }

        /// <summary>
        /// Creates a new metrics collector with specified options.
        /// </summary>
        public MetricsCollector(MetricsCollectorOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _namespace = options.Namespace ?? string.Empty;

            if (_options.EnableAutomaticSnapshots && _options.SnapshotInterval > TimeSpan.Zero)
            {
                _snapshotTimer = new Timer(_ => TakeSnapshot(), null, _options.SnapshotInterval, _options.SnapshotInterval);
            }
        }

        /// <inheritdoc />
        public MetricsCollectorOptions Options => _options;

        /// <inheritdoc />
        public void IncrementCounter(string name, params MetricLabel[] labels)
        {
            EnsureNotDisposed();
            if (!_options.Enabled) return;

            var fullName = GetFullMetricName(name);
            var entry = GetOrCreateMetric(fullName, MetricType.Counter, labels);
            entry.Increment(1.0);
        }

        /// <inheritdoc />
        public void IncrementCounter(string name, double value, params MetricLabel[] labels)
        {
            EnsureNotDisposed();
            if (!_options.Enabled) return;
            if (value < 0) throw new ArgumentException("Counter increment value cannot be negative", nameof(value));

            var fullName = GetFullMetricName(name);
            var entry = GetOrCreateMetric(fullName, MetricType.Counter, labels);
            entry.Increment(value);
        }

        /// <inheritdoc />
        public void SetGauge(string name, double value, params MetricLabel[] labels)
        {
            EnsureNotDisposed();
            if (!_options.Enabled) return;

            var fullName = GetFullMetricName(name);
            var entry = GetOrCreateMetric(fullName, MetricType.Gauge, labels);
            entry.Set(value);
        }

        /// <inheritdoc />
        public void IncrementGauge(string name, double value, params MetricLabel[] labels)
        {
            EnsureNotDisposed();
            if (!_options.Enabled) return;

            var fullName = GetFullMetricName(name);
            var entry = GetOrCreateMetric(fullName, MetricType.Gauge, labels);
            entry.Increment(value);
        }

        /// <inheritdoc />
        public void DecrementGauge(string name, double value, params MetricLabel[] labels)
        {
            EnsureNotDisposed();
            if (!_options.Enabled) return;

            var fullName = GetFullMetricName(name);
            var entry = GetOrCreateMetric(fullName, MetricType.Gauge, labels);
            entry.Increment(-value);
        }

        /// <inheritdoc />
        public void RecordHistogram(string name, double value, params MetricLabel[] labels)
        {
            RecordHistogram(name, value, _options.DefaultHistogramBuckets, labels);
        }

        /// <inheritdoc />
        public void RecordHistogram(string name, double value, double[] buckets, params MetricLabel[] labels)
        {
            EnsureNotDisposed();
            if (!_options.Enabled) return;
            if (buckets == null) throw new ArgumentNullException(nameof(buckets));

            var fullName = GetFullMetricName(name);
            var entry = GetOrCreateHistogram(fullName, buckets, labels);
            entry.RecordHistogram(value);
        }

        /// <inheritdoc />
        public void MeasureTime(string name, Action action, params MetricLabel[] labels)
        {
            EnsureNotDisposed();
            if (!_options.Enabled)
            {
                action();
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                action();
            }
            finally
            {
                stopwatch.Stop();
                RecordHistogram(name, stopwatch.Elapsed.TotalSeconds, labels);
            }
        }

        /// <inheritdoc />
        public async Task MeasureTimeAsync(string name, Func<Task> action, params MetricLabel[] labels)
        {
            EnsureNotDisposed();
            if (!_options.Enabled)
            {
                await action();
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                await action();
            }
            finally
            {
                stopwatch.Stop();
                RecordHistogram(name, stopwatch.Elapsed.TotalSeconds, labels);
            }
        }

        /// <inheritdoc />
        public double? GetValue(string name, params MetricLabel[] labels)
        {
            EnsureNotDisposed();
            if (!_options.Enabled) return null;

            var fullName = GetFullMetricName(name);
            var key = BuildMetricKey(fullName, labels);

            if (_metrics.TryGetValue(key, out var entry))
            {
                return entry.GetValue();
            }
            return null;
        }

        /// <inheritdoc />
        public MetricsSnapshot GetSnapshot()
        {
            EnsureNotDisposed();
            if (!_options.Enabled)
            {
                return new MetricsSnapshot(DateTime.UtcNow, Array.Empty<MetricValue>());
            }

            var metrics = _metrics.Values
                .Select(m => m.ToMetricValue())
                .ToList();

            return new MetricsSnapshot(DateTime.UtcNow, metrics);
        }

        /// <inheritdoc />
        public IReadOnlyCollection<string> GetMetricNames()
        {
            EnsureNotDisposed();
            return _metrics.Values
                .Select(m => m.Name)
                .Distinct()
                .ToList();
        }

        /// <inheritdoc />
        public void Clear()
        {
            EnsureNotDisposed();
            _metrics.Clear();
        }

        /// <inheritdoc />
        public void ClearMetric(string name)
        {
            EnsureNotDisposed();
            var fullName = GetFullMetricName(name);
            
            var keysToRemove = _metrics
                .Where(kvp => kvp.Value.Name == fullName)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _metrics.TryRemove(key, out _);
            }
        }

        /// <summary>
        /// Event raised when a snapshot is taken (if automatic snapshots are enabled).
        /// </summary>
        public event EventHandler<MetricsSnapshot>? SnapshotTaken;

        private void TakeSnapshot()
        {
            var snapshot = GetSnapshot();
            SnapshotTaken?.Invoke(this, snapshot);
        }

        private string GetFullMetricName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Metric name cannot be empty", nameof(name));

            return string.IsNullOrEmpty(_namespace) 
                ? name 
                : $"{_namespace}_{name}";
        }

        private MetricEntry GetOrCreateMetric(string name, MetricType type, MetricLabel[] labels)
        {
            var key = BuildMetricKey(name, labels);
            
            if (_metrics.TryGetValue(key, out var existing))
            {
                return existing;
            }

            if (_metrics.Count >= _options.MaxMetrics)
            {
                throw new InvalidOperationException(
                    $"Maximum number of metrics ({_options.MaxMetrics}) exceeded. " +
                    "Consider increasing MaxMetrics or clearing unused metrics.");
            }

            var newEntry = new MetricEntry(name, type, labels.ToList());
            return _metrics.GetOrAdd(key, newEntry);
        }

        private MetricEntry GetOrCreateHistogram(string name, double[] buckets, MetricLabel[] labels)
        {
            var key = BuildMetricKey(name, labels);
            
            if (_metrics.TryGetValue(key, out var existing))
            {
                return existing;
            }

            if (_metrics.Count >= _options.MaxMetrics)
            {
                throw new InvalidOperationException(
                    $"Maximum number of metrics ({_options.MaxMetrics}) exceeded.");
            }

            var newEntry = new MetricEntry(name, MetricType.Histogram, labels.ToList(), buckets);
            return _metrics.GetOrAdd(key, newEntry);
        }

        private static string BuildMetricKey(string name, MetricLabel[] labels)
        {
            if (labels == null || labels.Length == 0)
                return name;

            var labelPart = string.Join(",", 
                labels.OrderBy(l => l.Key)
                      .Select(l => $"{l.Key}={l.Value}"));
            
            return $"{name}|{labelPart}";
        }

        private void EnsureNotDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(MetricsCollector));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            
            _snapshotTimer?.Dispose();
            Clear();
            _disposed = true;
        }

        /// <summary>
        /// Internal class representing a single metric entry.
        /// </summary>
        private class MetricEntry
        {
            private readonly object _lock = new();
            private double _value;
            private long _count;
            private double _sum;
            private readonly long[] _bucketCounts;

            public string Name { get; }
            public MetricType Type { get; }
            public IReadOnlyList<MetricLabel> Labels { get; }
            public double[]? Buckets { get; }

            public MetricEntry(string name, MetricType type, IReadOnlyList<MetricLabel> labels, double[]? buckets = null)
            {
                Name = name;
                Type = type;
                Labels = labels;
                Buckets = buckets;
                _bucketCounts = buckets != null ? new long[buckets.Length + 1] : Array.Empty<long>();
            }

            public void Increment(double amount)
            {
                lock (_lock)
                {
                    _value += amount;
                }
            }

            public void Set(double value)
            {
                lock (_lock)
                {
                    _value = value;
                }
            }

            public void RecordHistogram(double value)
            {
                if (Buckets == null) return;

                lock (_lock)
                {
                    _count++;
                    _sum += value;

                    // Find the bucket
                    for (int i = 0; i < Buckets.Length; i++)
                    {
                        if (value <= Buckets[i])
                        {
                            _bucketCounts[i]++;
                            return;
                        }
                    }
                    // Value exceeds all buckets, goes to +Inf bucket
                    _bucketCounts[Buckets.Length]++;
                }
            }

            public double GetValue()
            {
                lock (_lock)
                {
                    return _value;
                }
            }

            public MetricValue ToMetricValue()
            {
                lock (_lock)
                {
                    HistogramData? histogramData = null;
                    
                    if (Type == MetricType.Histogram && Buckets != null)
                    {
                        histogramData = new HistogramData(
                            Buckets.ToArray(),
                            _bucketCounts.ToArray(),
                            _sum,
                            _count
                        );
                    }

                    return new MetricValue(
                        Name,
                        Type,
                        _value,
                        Labels.ToArray(),
                        DateTime.UtcNow,
                        histogramData
                    );
                }
            }
        }
    }
}
