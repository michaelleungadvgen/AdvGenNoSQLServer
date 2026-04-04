// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using AdvGenNoSqlServer.Core.MemoryManagement;

namespace AdvGenNoSqlServer.Core.Metrics
{
    /// <summary>
    /// Represents the type of metric being collected.
    /// </summary>
    public enum MetricType
    {
        /// <summary>A counter that can only increase or be reset to zero.</summary>
        Counter,
        
        /// <summary>A gauge that can go up and down.</summary>
        Gauge,
        
        /// <summary>A histogram that samples observations into configurable buckets.</summary>
        Histogram
    }

    /// <summary>
    /// Represents a label (dimension) for a metric.
    /// </summary>
    public readonly struct MetricLabel : IEquatable<MetricLabel>
    {
        /// <summary>Gets the label key/name.</summary>
        public string Key { get; }
        
        /// <summary>Gets the label value.</summary>
        public string Value { get; }

        /// <summary>
        /// Creates a new metric label.
        /// </summary>
        public MetricLabel(string key, string value)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Deconstructs the label into its key and value.
        /// </summary>
        public void Deconstruct(out string key, out string value)
        {
            key = Key;
            value = Value;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is MetricLabel label && Equals(label);

        /// <inheritdoc />
        public bool Equals(MetricLabel other) => Key == other.Key && Value == other.Value;

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(Key, Value);

        /// <summary>Equality operator.</summary>
        public static bool operator ==(MetricLabel left, MetricLabel right) => left.Equals(right);

        /// <summary>Inequality operator.</summary>
        public static bool operator !=(MetricLabel left, MetricLabel right) => !left.Equals(right);

        /// <summary>Creates a new label.</summary>
        public static MetricLabel Create(string key, string value) => new(key, value);
    }

    /// <summary>
    /// Represents a metric value at a point in time.
    /// </summary>
    public readonly struct MetricValue
    {
        /// <summary>Gets the metric name.</summary>
        public string Name { get; }
        
        /// <summary>Gets the metric type.</summary>
        public MetricType Type { get; }
        
        /// <summary>Gets the current value.</summary>
        public double Value { get; }
        
        /// <summary>Gets the labels associated with this metric.</summary>
        public IReadOnlyList<MetricLabel> Labels { get; }
        
        /// <summary>Gets the timestamp when the value was recorded.</summary>
        public DateTime Timestamp { get; }
        
        /// <summary>Gets additional metadata for histogram metrics.</summary>
        public HistogramData? HistogramInfo { get; }

        /// <summary>
        /// Creates a new metric value.
        /// </summary>
        public MetricValue(string name, MetricType type, double value, 
            IReadOnlyList<MetricLabel> labels, DateTime timestamp, HistogramData? histogramInfo = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Type = type;
            Value = value;
            Labels = labels ?? Array.Empty<MetricLabel>();
            Timestamp = timestamp;
            HistogramInfo = histogramInfo;
        }
    }

    /// <summary>
    /// Contains histogram-specific data including bucket counts.
    /// </summary>
    public readonly struct HistogramData
    {
        /// <summary>Gets the bucket boundaries.</summary>
        public double[] Buckets { get; }
        
        /// <summary>Gets the count in each bucket.</summary>
        public long[] BucketCounts { get; }
        
        /// <summary>Gets the sum of all observed values.</summary>
        public double Sum { get; }
        
        /// <summary>Gets the total count of observations.</summary>
        public long Count { get; }

        /// <summary>
        /// Creates histogram data.
        /// </summary>
        public HistogramData(double[] buckets, long[] bucketCounts, double sum, long count)
        {
            Buckets = buckets ?? throw new ArgumentNullException(nameof(buckets));
            BucketCounts = bucketCounts ?? throw new ArgumentNullException(nameof(bucketCounts));
            Sum = sum;
            Count = count;
        }
    }

    /// <summary>
    /// Configuration options for the metrics collector.
    /// </summary>
    public class MetricsCollectorOptions
    {
        /// <summary>Gets or sets whether metrics collection is enabled.</summary>
        public bool Enabled { get; set; } = true;
        
        /// <summary>Gets or sets the default histogram buckets in seconds.</summary>
        public double[] DefaultHistogramBuckets { get; set; } = new[] 
        { 
            0.001, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1.0, 2.5, 5.0, 10.0 
        };
        
        /// <summary>Gets or sets the maximum number of metrics to retain.</summary>
        public int MaxMetrics { get; set; } = 10000;
        
        /// <summary>Gets or sets the interval for automatic metric snapshots.</summary>
        public TimeSpan SnapshotInterval { get; set; } = TimeSpan.FromMinutes(1);
        
        /// <summary>Gets or sets whether to enable automatic snapshots.</summary>
        public bool EnableAutomaticSnapshots { get; set; } = false;
        
        /// <summary>Gets or sets the namespace prefix for all metrics.</summary>
        public string? Namespace { get; set; }
    }

    /// <summary>
    /// Snapshot of all current metric values.
    /// </summary>
    public class MetricsSnapshot
    {
        /// <summary>Gets the timestamp when the snapshot was taken.</summary>
        public DateTime Timestamp { get; }
        
        /// <summary>Gets all metric values in the snapshot.</summary>
        public IReadOnlyList<MetricValue> Metrics { get; }
        
        /// <summary>Gets the total number of unique metrics.</summary>
        public int TotalMetrics => Metrics.Count;

        /// <summary>
        /// Creates a metrics snapshot.
        /// </summary>
        public MetricsSnapshot(DateTime timestamp, IReadOnlyList<MetricValue> metrics)
        {
            Timestamp = timestamp;
            Metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        }
    }

    /// <summary>
    /// Core interface for collecting metrics in a Prometheus-compatible format.
    /// </summary>
    public interface IMetricsCollector : IDisposable
    {
        /// <summary>
        /// Increments a counter metric by 1.
        /// </summary>
        /// <param name="name">The metric name.</param>
        /// <param name="labels">Optional label key-value pairs.</param>
        void IncrementCounter(string name, params MetricLabel[] labels);

        /// <summary>
        /// Increments a counter metric by a specified value.
        /// </summary>
        /// <param name="name">The metric name.</param>
        /// <param name="value">The amount to increment by.</param>
        /// <param name="labels">Optional label key-value pairs.</param>
        void IncrementCounter(string name, double value, params MetricLabel[] labels);

        /// <summary>
        /// Sets a gauge metric to a specific value.
        /// </summary>
        /// <param name="name">The metric name.</param>
        /// <param name="value">The value to set.</param>
        /// <param name="labels">Optional label key-value pairs.</param>
        void SetGauge(string name, double value, params MetricLabel[] labels);

        /// <summary>
        /// Increments a gauge metric by a specified value.
        /// </summary>
        /// <param name="name">The metric name.</param>
        /// <param name="value">The amount to increment by.</param>
        /// <param name="labels">Optional label key-value pairs.</param>
        void IncrementGauge(string name, double value, params MetricLabel[] labels);

        /// <summary>
        /// Decrements a gauge metric by a specified value.
        /// </summary>
        /// <param name="name">The metric name.</param>
        /// <param name="value">The amount to decrement by.</param>
        /// <param name="labels">Optional label key-value pairs.</param>
        void DecrementGauge(string name, double value, params MetricLabel[] labels);

        /// <summary>
        /// Records a value in a histogram metric.
        /// </summary>
        /// <param name="name">The metric name.</param>
        /// <param name="value">The observed value.</param>
        /// <param name="labels">Optional label key-value pairs.</param>
        void RecordHistogram(string name, double value, params MetricLabel[] labels);

        /// <summary>
        /// Records a value in a histogram metric with custom buckets.
        /// </summary>
        /// <param name="name">The metric name.</param>
        /// <param name="value">The observed value.</param>
        /// <param name="buckets">Custom bucket boundaries.</param>
        /// <param name="labels">Optional label key-value pairs.</param>
        void RecordHistogram(string name, double value, double[] buckets, params MetricLabel[] labels);

        /// <summary>
        /// Measures the execution time of an action and records it as a histogram.
        /// </summary>
        /// <param name="name">The metric name.</param>
        /// <param name="action">The action to measure.</param>
        /// <param name="labels">Optional label key-value pairs.</param>
        void MeasureTime(string name, Action action, params MetricLabel[] labels);

        /// <summary>
        /// Measures the execution time of an async action and records it as a histogram.
        /// </summary>
        /// <param name="name">The metric name.</param>
        /// <param name="action">The async action to measure.</param>
        /// <param name="labels">Optional label key-value pairs.</param>
        /// <returns>A task representing the async operation.</returns>
        Task MeasureTimeAsync(string name, Func<Task> action, params MetricLabel[] labels);

        /// <summary>
        /// Gets the current value of a metric.
        /// </summary>
        /// <param name="name">The metric name.</param>
        /// <param name="labels">Optional label key-value pairs.</param>
        /// <returns>The current value, or null if not found.</returns>
        double? GetValue(string name, params MetricLabel[] labels);

        /// <summary>
        /// Takes a snapshot of all current metric values.
        /// </summary>
        /// <returns>A snapshot containing all current metrics.</returns>
        MetricsSnapshot GetSnapshot();

        /// <summary>
        /// Gets all metric names currently being tracked.
        /// </summary>
        /// <returns>A collection of metric names.</returns>
        IReadOnlyCollection<string> GetMetricNames();

        /// <summary>
        /// Clears all metrics.
        /// </summary>
        void Clear();

        /// <summary>
        /// Clears a specific metric.
        /// </summary>
        /// <param name="name">The metric name to clear.</param>
        void ClearMetric(string name);

        /// <summary>
        /// Gets the collector options.
        /// </summary>
        MetricsCollectorOptions Options { get; }

        /// <summary>
        /// Records a snapshot of cache engine statistics as Prometheus-compatible metrics.
        /// Writes: cache_used_bytes (gauge), cache_entry_count (gauge),
        ///         cache_hit_total (counter), cache_miss_total (counter),
        ///         cache_eviction_total (counter). All labelled plan=&lt;Plan&gt;.
        /// </summary>
        void RecordCacheStats(MemoryEngineStats stats);
    }
}
