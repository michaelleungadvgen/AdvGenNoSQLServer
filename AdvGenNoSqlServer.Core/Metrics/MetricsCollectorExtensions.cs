// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace AdvGenNoSqlServer.Core.Metrics
{
    /// <summary>
    /// Extension methods for configuring and using the metrics collector.
    /// </summary>
    public static class MetricsCollectorExtensions
    {
        /// <summary>
        /// Adds the metrics collector to the service collection.
        /// </summary>
        public static IServiceCollection AddMetricsCollector(this IServiceCollection services)
        {
            services.AddSingleton<IMetricsCollector>(new MetricsCollector());
            return services;
        }

        /// <summary>
        /// Adds the metrics collector with custom options to the service collection.
        /// </summary>
        public static IServiceCollection AddMetricsCollector(
            this IServiceCollection services, 
            Action<MetricsCollectorOptions> configureOptions)
        {
            var options = new MetricsCollectorOptions();
            configureOptions(options);
            services.AddSingleton<IMetricsCollector>(new MetricsCollector(options));
            return services;
        }

        /// <summary>
        /// Adds the metrics collector with pre-configured options.
        /// </summary>
        public static IServiceCollection AddMetricsCollector(
            this IServiceCollection services, 
            MetricsCollectorOptions options)
        {
            services.AddSingleton<IMetricsCollector>(new MetricsCollector(options));
            return services;
        }

        /// <summary>
        /// Adds a no-op metrics collector (for when metrics are disabled).
        /// </summary>
        public static IServiceCollection AddNoOpMetricsCollector(this IServiceCollection services)
        {
            services.AddSingleton<IMetricsCollector>(new NoOpMetricsCollector());
            return services;
        }

        /// <summary>
        /// Formats a metric value for Prometheus exposition format.
        /// </summary>
        public static string ToPrometheusFormat(this MetricValue metric)
        {
            var labels = metric.Labels.Count > 0
                ? "{" + string.Join(",", metric.Labels.Select(l => $"{l.Key}=\"{l.Value}\"")) + "}"
                : string.Empty;

            var name = metric.Name.Replace('-', '_').Replace('.', '_');

            switch (metric.Type)
            {
                case MetricType.Counter:
                    return $"# TYPE {name} counter\n{name}{labels} {metric.Value}";
                
                case MetricType.Gauge:
                    return $"# TYPE {name} gauge\n{name}{labels} {metric.Value}";
                
                case MetricType.Histogram:
                    return FormatHistogramForPrometheus(name, labels, metric);
                
                default:
                    return $"{name}{labels} {metric.Value}";
            }
        }

        /// <summary>
        /// Exports all metrics to Prometheus exposition format.
        /// </summary>
        public static string ToPrometheusFormat(this MetricsSnapshot snapshot)
        {
            var lines = new List<string>();
            var groupedMetrics = snapshot.Metrics.GroupBy(m => m.Name);

            foreach (var group in groupedMetrics)
            {
                var first = group.First();
                var name = first.Name.Replace('-', '_').Replace('.', '_');

                // Add type annotation
                lines.Add($"# TYPE {name} {first.Type.ToString().ToLowerInvariant()}");

                foreach (var metric in group)
                {
                    if (metric.Type == MetricType.Histogram && metric.HistogramInfo.HasValue)
                    {
                        lines.AddRange(FormatHistogramLines(metric));
                    }
                    else
                    {
                        var labels = metric.Labels.Count > 0
                            ? "{" + string.Join(",", metric.Labels.Select(l => $"{l.Key}=\"{l.Value}\"")) + "}"
                            : string.Empty;
                        lines.Add($"{name}{labels} {metric.Value}");
                    }
                }

                lines.Add(string.Empty); // Empty line between metrics
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Gets counters with the specified name.
        /// </summary>
        public static IEnumerable<MetricValue> GetCounters(this MetricsSnapshot snapshot, string name)
        {
            return snapshot.Metrics
                .Where(m => m.Name == name && m.Type == MetricType.Counter);
        }

        /// <summary>
        /// Gets gauges with the specified name.
        /// </summary>
        public static IEnumerable<MetricValue> GetGauges(this MetricsSnapshot snapshot, string name)
        {
            return snapshot.Metrics
                .Where(m => m.Name == name && m.Type == MetricType.Gauge);
        }

        /// <summary>
        /// Gets histograms with the specified name.
        /// </summary>
        public static IEnumerable<MetricValue> GetHistograms(this MetricsSnapshot snapshot, string name)
        {
            return snapshot.Metrics
                .Where(m => m.Name == name && m.Type == MetricType.Histogram);
        }

        /// <summary>
        /// Gets the sum of all counter values with the specified name.
        /// </summary>
        public static double GetCounterSum(this MetricsSnapshot snapshot, string name)
        {
            return snapshot.GetCounters(name).Sum(m => m.Value);
        }

        /// <summary>
        /// Gets the average of all gauge values with the specified name.
        /// </summary>
        public static double GetGaugeAverage(this MetricsSnapshot snapshot, string name)
        {
            var gauges = snapshot.GetGauges(name).ToList();
            if (gauges.Count == 0) return 0;
            return gauges.Average(m => m.Value);
        }

        private static string FormatHistogramForPrometheus(string name, string labels, MetricValue metric)
        {
            if (!metric.HistogramInfo.HasValue) return string.Empty;

            var info = metric.HistogramInfo.Value;
            var lines = new List<string>();

            // Bucket lines
            for (int i = 0; i < info.Buckets.Length; i++)
            {
                var bucketLabels = labels.TrimEnd('}') + 
                    (string.IsNullOrEmpty(labels) ? "{" : ",") + 
                    $"le=\"{info.Buckets[i]}\"}}";
                lines.Add($"{name}_bucket{bucketLabels} {info.BucketCounts[i]}");
            }

            // +Inf bucket
            var infLabels = labels.TrimEnd('}') + 
                (string.IsNullOrEmpty(labels) ? "{" : ",") + 
                "le=\"+Inf\"}";
            lines.Add($"{name}_bucket{infLabels} {info.Count}");

            // Sum and count
            lines.Add($"{name}_sum{labels} {info.Sum}");
            lines.Add($"{name}_count{labels} {info.Count}");

            return string.Join("\n", lines);
        }

        private static IEnumerable<string> FormatHistogramLines(MetricValue metric)
        {
            if (!metric.HistogramInfo.HasValue) yield break;

            var info = metric.HistogramInfo.Value;
            var labels = metric.Labels.Count > 0
                ? "{" + string.Join(",", metric.Labels.Select(l => $"{l.Key}=\"{l.Value}\"")) + "}"
                : string.Empty;

            var name = metric.Name.Replace('-', '_').Replace('.', '_');

            // Bucket lines
            for (int i = 0; i < info.Buckets.Length; i++)
            {
                var bucketLabels = labels.TrimEnd('}') + 
                    (string.IsNullOrEmpty(labels) ? "{" : ",") + 
                    $"le=\"{info.Buckets[i]}\"}}";
                yield return $"{name}_bucket{bucketLabels} {info.BucketCounts[i]}";
            }

            // +Inf bucket
            var infLabels = labels.TrimEnd('}') + 
                (string.IsNullOrEmpty(labels) ? "{" : ",") + 
                "le=\"+Inf\"}";
            yield return $"{name}_bucket{infLabels} {info.Count}";

            // Sum and count
            yield return $"{name}_sum{labels} {info.Sum}";
            yield return $"{name}_count{labels} {info.Count}";
        }
    }
}
