// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Core.MemoryManagement;

namespace AdvGenNoSqlServer.Core.Metrics
{
    /// <summary>
    /// A no-op implementation of IMetricsCollector that performs no operations.
    /// Useful for disabling metrics collection without changing application code.
    /// </summary>
    public class NoOpMetricsCollector : IMetricsCollector
    {
        private static readonly MetricsCollectorOptions _disabledOptions = new() { Enabled = false };
        private static readonly IReadOnlyCollection<string> _emptyNames = Array.Empty<string>();
        private static readonly MetricsSnapshot _emptySnapshot = new(DateTime.UtcNow, Array.Empty<MetricValue>());

        /// <inheritdoc />
        public MetricsCollectorOptions Options => _disabledOptions;

        /// <inheritdoc />
        public void IncrementCounter(string name, params MetricLabel[] labels) { }

        /// <inheritdoc />
        public void IncrementCounter(string name, double value, params MetricLabel[] labels) { }

        /// <inheritdoc />
        public void SetGauge(string name, double value, params MetricLabel[] labels) { }

        /// <inheritdoc />
        public void IncrementGauge(string name, double value, params MetricLabel[] labels) { }

        /// <inheritdoc />
        public void DecrementGauge(string name, double value, params MetricLabel[] labels) { }

        /// <inheritdoc />
        public void RecordHistogram(string name, double value, params MetricLabel[] labels) { }

        /// <inheritdoc />
        public void RecordHistogram(string name, double value, double[] buckets, params MetricLabel[] labels) { }

        /// <inheritdoc />
        public void MeasureTime(string name, Action action, params MetricLabel[] labels)
        {
            action();
        }

        /// <inheritdoc />
        public Task MeasureTimeAsync(string name, Func<Task> action, params MetricLabel[] labels)
        {
            return action();
        }

        /// <inheritdoc />
        public double? GetValue(string name, params MetricLabel[] labels) => null;

        /// <inheritdoc />
        public MetricsSnapshot GetSnapshot() => _emptySnapshot;

        /// <inheritdoc />
        public IReadOnlyCollection<string> GetMetricNames() => _emptyNames;

        /// <inheritdoc />
        public void Clear() { }

        /// <inheritdoc />
        public void ClearMetric(string name) { }

        /// <inheritdoc />
        public void RecordCacheStats(MemoryEngineStats stats) { }

        /// <inheritdoc />
        public void Dispose() { }
    }
}
