// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Core.Metrics;
using Xunit;

namespace AdvGenNoSqlServer.Tests
{
    /// <summary>
    /// Unit tests for the MetricsCollector implementation.
    /// </summary>
    public class MetricsCollectorTests : IDisposable
    {
        private readonly MetricsCollector _collector;

        public MetricsCollectorTests()
        {
            _collector = new MetricsCollector();
        }

        public void Dispose()
        {
            _collector.Dispose();
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_DefaultOptions_CreatesCollector()
        {
            using var collector = new MetricsCollector();
            Assert.NotNull(collector);
            Assert.True(collector.Options.Enabled);
        }

        [Fact]
        public void Constructor_WithOptions_CreatesCollector()
        {
            var options = new MetricsCollectorOptions
            {
                Enabled = false,
                MaxMetrics = 100,
                Namespace = "test"
            };
            using var collector = new MetricsCollector(options);
            
            Assert.False(collector.Options.Enabled);
            Assert.Equal(100, collector.Options.MaxMetrics);
            Assert.Equal("test", collector.Options.Namespace);
        }

        [Fact]
        public void Constructor_NullOptions_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new MetricsCollector(null!));
        }

        #endregion

        #region Counter Tests

        [Fact]
        public void IncrementCounter_NoLabels_IncrementsByOne()
        {
            _collector.IncrementCounter("test_counter");
            
            var value = _collector.GetValue("test_counter");
            Assert.Equal(1.0, value);
        }

        [Fact]
        public void IncrementCounter_WithValue_IncrementsByValue()
        {
            _collector.IncrementCounter("test_counter", 5.0);
            
            var value = _collector.GetValue("test_counter");
            Assert.Equal(5.0, value);
        }

        [Fact]
        public void IncrementCounter_WithLabels_IncrementsWithLabels()
        {
            _collector.IncrementCounter("test_counter", 
                new MetricLabel("method", "GET"),
                new MetricLabel("status", "200"));
            
            var value = _collector.GetValue("test_counter", 
                new MetricLabel("method", "GET"),
                new MetricLabel("status", "200"));
            
            Assert.Equal(1.0, value);
        }

        [Fact]
        public void IncrementCounter_MultipleCalls_Accumulates()
        {
            _collector.IncrementCounter("test_counter", 1.0);
            _collector.IncrementCounter("test_counter", 2.0);
            _collector.IncrementCounter("test_counter", 3.0);
            
            var value = _collector.GetValue("test_counter");
            Assert.Equal(6.0, value);
        }

        [Fact]
        public void IncrementCounter_NegativeValue_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => _collector.IncrementCounter("test", -1.0));
        }

        [Fact]
        public void IncrementCounter_DifferentLabelCombinations_TracksSeparately()
        {
            _collector.IncrementCounter("requests", 1.0, new MetricLabel("status", "200"));
            _collector.IncrementCounter("requests", 1.0, new MetricLabel("status", "404"));
            _collector.IncrementCounter("requests", 1.0, new MetricLabel("status", "200"));
            
            var value200 = _collector.GetValue("requests", new MetricLabel("status", "200"));
            var value404 = _collector.GetValue("requests", new MetricLabel("status", "404"));
            
            Assert.Equal(2.0, value200);
            Assert.Equal(1.0, value404);
        }

        #endregion

        #region Gauge Tests

        [Fact]
        public void SetGauge_SetsValue()
        {
            _collector.SetGauge("test_gauge", 42.0);
            
            var value = _collector.GetValue("test_gauge");
            Assert.Equal(42.0, value);
        }

        [Fact]
        public void SetGauge_OverwritesPreviousValue()
        {
            _collector.SetGauge("test_gauge", 10.0);
            _collector.SetGauge("test_gauge", 20.0);
            
            var value = _collector.GetValue("test_gauge");
            Assert.Equal(20.0, value);
        }

        [Fact]
        public void IncrementGauge_IncreasesValue()
        {
            _collector.SetGauge("test_gauge", 10.0);
            _collector.IncrementGauge("test_gauge", 5.0);
            
            var value = _collector.GetValue("test_gauge");
            Assert.Equal(15.0, value);
        }

        [Fact]
        public void DecrementGauge_DecreasesValue()
        {
            _collector.SetGauge("test_gauge", 10.0);
            _collector.DecrementGauge("test_gauge", 3.0);
            
            var value = _collector.GetValue("test_gauge");
            Assert.Equal(7.0, value);
        }

        [Fact]
        public void IncrementGauge_NegativeValue_DecreasesValue()
        {
            _collector.SetGauge("test_gauge", 10.0);
            _collector.IncrementGauge("test_gauge", -5.0);
            
            var value = _collector.GetValue("test_gauge");
            Assert.Equal(5.0, value);
        }

        [Fact]
        public void Gauge_CanBeNegative()
        {
            _collector.SetGauge("temperature", -10.0);
            
            var value = _collector.GetValue("temperature");
            Assert.Equal(-10.0, value);
        }

        #endregion

        #region Histogram Tests

        [Fact]
        public void RecordHistogram_RecordsValue()
        {
            _collector.RecordHistogram("request_duration", 0.05);
            
            var snapshot = _collector.GetSnapshot();
            var histogram = snapshot.GetHistograms("request_duration").FirstOrDefault();
            
            Assert.NotNull(histogram);
            Assert.True(histogram.HistogramInfo.HasValue);
            Assert.Equal(1, histogram.HistogramInfo.Value.Count);
            Assert.Equal(0.05, histogram.HistogramInfo.Value.Sum);
        }

        [Fact]
        public void RecordHistogram_MultipleValues_TracksCounts()
        {
            _collector.RecordHistogram("request_duration", 0.005, Array.Empty<MetricLabel>());
            _collector.RecordHistogram("request_duration", 0.05, Array.Empty<MetricLabel>());
            _collector.RecordHistogram("request_duration", 0.5, Array.Empty<MetricLabel>());
            
            var snapshot = _collector.GetSnapshot();
            var histogram = snapshot.GetHistograms("request_duration").FirstOrDefault();
            
            Assert.NotNull(histogram);
            Assert.True(histogram.HistogramInfo.HasValue);
            Assert.Equal(3, histogram.HistogramInfo.Value.Count);
        }

        [Fact]
        public void RecordHistogram_WithCustomBuckets_UsesCustomBuckets()
        {
            var customBuckets = new[] { 0.1, 0.5, 1.0 };
            _collector.RecordHistogram("custom_histogram", 0.05, customBuckets);
            
            var snapshot = _collector.GetSnapshot();
            var histogram = snapshot.GetHistograms("custom_histogram").FirstOrDefault();
            
            Assert.NotNull(histogram);
            Assert.True(histogram.HistogramInfo.HasValue);
            Assert.Equal(3, histogram.HistogramInfo.Value.Buckets.Length);
        }

        [Fact]
        public void RecordHistogram_WithLabels_TracksSeparately()
        {
            _collector.RecordHistogram("duration", 0.1, new MetricLabel("endpoint", "/api/users"));
            _collector.RecordHistogram("duration", 0.2, new MetricLabel("endpoint", "/api/orders"));
            
            var snapshot = _collector.GetSnapshot();
            var histograms = snapshot.GetHistograms("duration").ToList();
            
            Assert.Equal(2, histograms.Count);
        }

        [Fact]
        public void RecordHistogram_NullBuckets_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => 
                _collector.RecordHistogram("test", 1.0, (double[])null!));
        }

        #endregion

        #region MeasureTime Tests

        [Fact]
        public void MeasureTime_RecordsElapsedTime()
        {
            _collector.MeasureTime("operation_time", () =>
            {
                System.Threading.Thread.Sleep(10);
            });
            
            var snapshot = _collector.GetSnapshot();
            var histogram = snapshot.GetHistograms("operation_time").FirstOrDefault();
            
            Assert.NotNull(histogram);
            Assert.True(histogram.HistogramInfo.HasValue);
            Assert.True(histogram.HistogramInfo.Value.Sum > 0);
        }

        [Fact]
        public void MeasureTime_ActionThrows_StillRecordsTime()
        {
            Assert.Throws<InvalidOperationException>(() =>
            {
                _collector.MeasureTime("failing_operation", () =>
                {
                    throw new InvalidOperationException("Test exception");
                });
            });
            
            var snapshot = _collector.GetSnapshot();
            var histogram = snapshot.GetHistograms("failing_operation").FirstOrDefault();
            
            Assert.NotNull(histogram);
        }

        [Fact]
        public async Task MeasureTimeAsync_RecordsElapsedTime()
        {
            await _collector.MeasureTimeAsync("async_operation", async () =>
            {
                await Task.Delay(10);
            });
            
            var snapshot = _collector.GetSnapshot();
            var histogram = snapshot.GetHistograms("async_operation").FirstOrDefault();
            
            Assert.NotNull(histogram);
            Assert.True(histogram.HistogramInfo.HasValue);
        }

        [Fact]
        public async Task MeasureTimeAsync_AsyncActionThrows_StillRecordsTime()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await _collector.MeasureTimeAsync("failing_async_operation", async () =>
                {
                    await Task.Delay(1);
                    throw new InvalidOperationException("Test exception");
                });
            });
            
            var snapshot = _collector.GetSnapshot();
            var histogram = snapshot.GetHistograms("failing_async_operation").FirstOrDefault();
            
            Assert.NotNull(histogram);
        }

        #endregion

        #region GetValue Tests

        [Fact]
        public void GetValue_NonExistentMetric_ReturnsNull()
        {
            var value = _collector.GetValue("non_existent");
            Assert.Null(value);
        }

        [Fact]
        public void GetValue_WrongLabels_ReturnsNull()
        {
            _collector.IncrementCounter("test", new MetricLabel("key", "value1"));
            
            var value = _collector.GetValue("test", new MetricLabel("key", "value2"));
            Assert.Null(value);
        }

        [Fact]
        public void GetValue_WithLabels_ReturnsCorrectValue()
        {
            _collector.IncrementCounter("test", 5.0, new MetricLabel("key", "value"));
            
            var value = _collector.GetValue("test", new MetricLabel("key", "value"));
            Assert.Equal(5.0, value);
        }

        #endregion

        #region GetSnapshot Tests

        [Fact]
        public void GetSnapshot_ReturnsAllMetrics()
        {
            _collector.IncrementCounter("counter1");
            _collector.IncrementCounter("counter2");
            _collector.SetGauge("gauge1", 10.0);
            
            var snapshot = _collector.GetSnapshot();
            
            Assert.Equal(3, snapshot.TotalMetrics);
            Assert.True(snapshot.Timestamp <= DateTime.UtcNow);
        }

        [Fact]
        public void GetSnapshot_EmptyCollector_ReturnsEmptySnapshot()
        {
            var snapshot = _collector.GetSnapshot();
            
            Assert.Equal(0, snapshot.TotalMetrics);
            Assert.Empty(snapshot.Metrics);
        }

        [Fact]
        public void GetSnapshot_IncludesMetricTypes()
        {
            _collector.IncrementCounter("counter");
            _collector.SetGauge("gauge", 1.0);
            _collector.RecordHistogram("histogram", 0.1);
            
            var snapshot = _collector.GetSnapshot();
            
            Assert.Contains(snapshot.Metrics, m => m.Type == MetricType.Counter);
            Assert.Contains(snapshot.Metrics, m => m.Type == MetricType.Gauge);
            Assert.Contains(snapshot.Metrics, m => m.Type == MetricType.Histogram);
        }

        #endregion

        #region GetMetricNames Tests

        [Fact]
        public void GetMetricNames_ReturnsDistinctNames()
        {
            _collector.IncrementCounter("counter1", new MetricLabel("a", "1"));
            _collector.IncrementCounter("counter1", new MetricLabel("a", "2"));
            _collector.IncrementCounter("counter2");
            
            var names = _collector.GetMetricNames();
            
            Assert.Equal(2, names.Count);
            Assert.Contains("counter1", names);
            Assert.Contains("counter2", names);
        }

        [Fact]
        public void GetMetricNames_NoMetrics_ReturnsEmpty()
        {
            var names = _collector.GetMetricNames();
            Assert.Empty(names);
        }

        #endregion

        #region Clear Tests

        [Fact]
        public void Clear_RemovesAllMetrics()
        {
            _collector.IncrementCounter("test");
            _collector.SetGauge("gauge", 1.0);
            
            _collector.Clear();
            
            Assert.Equal(0, _collector.GetSnapshot().TotalMetrics);
            Assert.Null(_collector.GetValue("test"));
        }

        [Fact]
        public void ClearMetric_RemovesSpecificMetric()
        {
            _collector.IncrementCounter("counter1");
            _collector.IncrementCounter("counter2");
            
            _collector.ClearMetric("counter1");
            
            Assert.Null(_collector.GetValue("counter1"));
            Assert.NotNull(_collector.GetValue("counter2"));
        }

        [Fact]
        public void ClearMetric_WithMultipleLabelValues_RemovesAll()
        {
            _collector.IncrementCounter("test", new MetricLabel("key", "1"));
            _collector.IncrementCounter("test", new MetricLabel("key", "2"));
            _collector.IncrementCounter("other");
            
            _collector.ClearMetric("test");
            
            Assert.Null(_collector.GetValue("test", new MetricLabel("key", "1")));
            Assert.Null(_collector.GetValue("test", new MetricLabel("key", "2")));
            Assert.NotNull(_collector.GetValue("other"));
        }

        #endregion

        #region Namespace Tests

        [Fact]
        public void Constructor_WithNamespace_PrefixesMetricNames()
        {
            var options = new MetricsCollectorOptions { Namespace = "myapp" };
            using var collector = new MetricsCollector(options);
            
            collector.IncrementCounter("requests");
            
            var snapshot = collector.GetSnapshot();
            Assert.Contains(snapshot.Metrics, m => m.Name == "myapp_requests");
        }

        [Fact]
        public void Constructor_EmptyNamespace_DoesNotPrefix()
        {
            var options = new MetricsCollectorOptions { Namespace = "" };
            using var collector = new MetricsCollector(options);
            
            collector.IncrementCounter("requests");
            
            var snapshot = collector.GetSnapshot();
            Assert.Contains(snapshot.Metrics, m => m.Name == "requests");
        }

        #endregion

        #region Disabled Tests

        [Fact]
        public void DisabledCollector_DoesNotCollect()
        {
            var options = new MetricsCollectorOptions { Enabled = false };
            using var collector = new MetricsCollector(options);
            
            collector.IncrementCounter("test");
            collector.SetGauge("gauge", 1.0);
            
            Assert.Null(collector.GetValue("test"));
            Assert.Equal(0, collector.GetSnapshot().TotalMetrics);
        }

        [Fact]
        public void DisabledCollector_MeasureTime_StillExecutesAction()
        {
            var options = new MetricsCollectorOptions { Enabled = false };
            using var collector = new MetricsCollector(options);
            
            bool executed = false;
            collector.MeasureTime("test", () => executed = true);
            
            Assert.True(executed);
        }

        [Fact]
        public async Task DisabledCollector_MeasureTimeAsync_StillExecutesAction()
        {
            var options = new MetricsCollectorOptions { Enabled = false };
            using var collector = new MetricsCollector(options);
            
            bool executed = false;
            await collector.MeasureTimeAsync("test", async () =>
            {
                await Task.Delay(1);
                executed = true;
            });
            
            Assert.True(executed);
        }

        #endregion

        #region Validation Tests

        [Fact]
        public void IncrementCounter_EmptyName_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => _collector.IncrementCounter(""));
        }

        [Fact]
        public void IncrementCounter_NullName_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => _collector.IncrementCounter(null!));
        }

        [Fact]
        public void SetGauge_EmptyName_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => _collector.SetGauge("", 1.0));
        }

        [Fact]
        public void RecordHistogram_EmptyName_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => _collector.RecordHistogram("", 1.0));
        }

        #endregion

        #region Prometheus Format Tests

        [Fact]
        public void ToPrometheusFormat_Counter_FormatsCorrectly()
        {
            _collector.IncrementCounter("requests", 42.0, 
                new MetricLabel("method", "GET"),
                new MetricLabel("status", "200"));
            
            var snapshot = _collector.GetSnapshot();
            var prometheus = snapshot.ToPrometheusFormat();
            
            Assert.Contains("# TYPE requests counter", prometheus);
            Assert.Contains("requests{method=\"GET\",status=\"200\"} 42", prometheus);
        }

        [Fact]
        public void ToPrometheusFormat_Gauge_FormatsCorrectly()
        {
            _collector.SetGauge("temperature", 23.5);
            
            var snapshot = _collector.GetSnapshot();
            var prometheus = snapshot.ToPrometheusFormat();
            
            Assert.Contains("# TYPE temperature gauge", prometheus);
            Assert.Contains("temperature 23.5", prometheus);
        }

        [Fact]
        public void ToPrometheusFormat_Histogram_IncludesBuckets()
        {
            _collector.RecordHistogram("duration", 0.05);
            
            var snapshot = _collector.GetSnapshot();
            var prometheus = snapshot.ToPrometheusFormat();
            
            Assert.Contains("# TYPE duration histogram", prometheus);
            Assert.Contains("duration_bucket{le=\"", prometheus);
            Assert.Contains("duration_sum", prometheus);
            Assert.Contains("duration_count", prometheus);
        }

        [Fact]
        public void ToPrometheusFormat_HyphensReplacedWithUnderscores()
        {
            _collector.IncrementCounter("my-counter");
            
            var snapshot = _collector.GetSnapshot();
            var prometheus = snapshot.ToPrometheusFormat();
            
            Assert.Contains("my_counter", prometheus);
        }

        #endregion

        #region Snapshot Helper Tests

        [Fact]
        public void GetCounterSum_SumsAllCounters()
        {
            _collector.IncrementCounter("requests", 1.0, new MetricLabel("status", "200"));
            _collector.IncrementCounter("requests", 1.0, new MetricLabel("status", "404"));
            _collector.IncrementCounter("requests", 1.0, new MetricLabel("status", "200"));
            
            var snapshot = _collector.GetSnapshot();
            var sum = snapshot.GetCounterSum("requests");
            
            Assert.Equal(3.0, sum);
        }

        [Fact]
        public void GetGaugeAverage_AveragesAllGauges()
        {
            _collector.SetGauge("temperature", 10.0, new MetricLabel("zone", "a"));
            _collector.SetGauge("temperature", 20.0, new MetricLabel("zone", "b"));
            _collector.SetGauge("temperature", 30.0, new MetricLabel("zone", "c"));
            
            var snapshot = _collector.GetSnapshot();
            var avg = snapshot.GetGaugeAverage("temperature");
            
            Assert.Equal(20.0, avg);
        }

        [Fact]
        public void GetCounterSum_NoCounters_ReturnsZero()
        {
            var snapshot = _collector.GetSnapshot();
            var sum = snapshot.GetCounterSum("nonexistent");
            
            Assert.Equal(0.0, sum);
        }

        #endregion

        #region NoOpMetricsCollector Tests

        [Fact]
        public void NoOpMetricsCollector_AllOperations_NoOp()
        {
            using var collector = new NoOpMetricsCollector();
            
            collector.IncrementCounter("test");
            collector.SetGauge("gauge", 1.0);
            collector.RecordHistogram("hist", 1.0);
            
            Assert.Equal(0, collector.GetSnapshot().TotalMetrics);
            Assert.Null(collector.GetValue("test"));
            Assert.False(collector.Options.Enabled);
        }

        [Fact]
        public void NoOpMetricsCollector_MeasureTime_ExecutesAction()
        {
            using var collector = new NoOpMetricsCollector();
            
            bool executed = false;
            collector.MeasureTime("test", () => executed = true);
            
            Assert.True(executed);
        }

        [Fact]
        public async Task NoOpMetricsCollector_MeasureTimeAsync_ExecutesAction()
        {
            using var collector = new NoOpMetricsCollector();
            
            bool executed = false;
            await collector.MeasureTimeAsync("test", async () =>
            {
                await Task.Delay(1);
                executed = true;
            });
            
            Assert.True(executed);
        }

        #endregion

        #region MaxMetrics Tests

        [Fact]
        public void MaxMetricsExceeded_ThrowsInvalidOperationException()
        {
            var options = new MetricsCollectorOptions { MaxMetrics = 2 };
            using var collector = new MetricsCollector(options);
            
            collector.IncrementCounter("metric1");
            collector.IncrementCounter("metric2");
            
            Assert.Throws<InvalidOperationException>(() => collector.IncrementCounter("metric3"));
        }

        [Fact]
        public void MaxMetrics_SameMetricDifferentLabels_CountsSeparately()
        {
            var options = new MetricsCollectorOptions { MaxMetrics = 2 };
            using var collector = new MetricsCollector(options);
            
            collector.IncrementCounter("metric", new MetricLabel("key", "1"));
            collector.IncrementCounter("metric", new MetricLabel("key", "2"));
            
            // Third unique label combination should exceed limit
            Assert.Throws<InvalidOperationException>(() => 
                collector.IncrementCounter("metric", new MetricLabel("key", "3")));
        }

        #endregion

        #region Dispose Tests

        [Fact]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            var collector = new MetricsCollector();
            collector.Dispose();
            collector.Dispose(); // Should not throw
        }

        [Fact]
        public void Dispose_AfterDispose_ThrowsObjectDisposedException()
        {
            var collector = new MetricsCollector();
            collector.Dispose();
            
            Assert.Throws<ObjectDisposedException>(() => collector.IncrementCounter("test"));
        }

        #endregion

        #region Label Tests

        [Fact]
        public void MetricLabel_Equality_SameKeyValue_AreEqual()
        {
            var label1 = new MetricLabel("key", "value");
            var label2 = new MetricLabel("key", "value");
            
            Assert.Equal(label1, label2);
            Assert.True(label1 == label2);
        }

        [Fact]
        public void MetricLabel_Equality_DifferentValues_AreNotEqual()
        {
            var label1 = new MetricLabel("key", "value1");
            var label2 = new MetricLabel("key", "value2");
            
            Assert.False(label1.Equals(label2));
        }

        [Fact]
        public void MetricLabel_Create_FactoryMethod()
        {
            var label = MetricLabel.Create("key", "value");
            
            Assert.Equal("key", label.Key);
            Assert.Equal("value", label.Value);
        }

        [Fact]
        public void MetricLabel_Deconstruct_Works()
        {
            var label = new MetricLabel("key", "value");
            var (key, value) = label;
            
            Assert.Equal("key", key);
            Assert.Equal("value", value);
        }

        [Fact]
        public void MetricLabel_Constructor_NullKey_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new MetricLabel(null!, "value"));
        }

        [Fact]
        public void MetricLabel_Constructor_NullValue_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new MetricLabel("key", null!));
        }

        #endregion

        #region Histogram Bucket Tests

        [Fact]
        public void RecordHistogram_ValueInFirstBucket_IncrementsFirstBucket()
        {
            var buckets = new[] { 0.1, 0.5, 1.0 };
            _collector.RecordHistogram("hist", 0.05, buckets);
            
            var snapshot = _collector.GetSnapshot();
            var hist = snapshot.GetHistograms("hist").First();
            var info = hist.HistogramInfo!.Value;
            
            Assert.Equal(1, info.BucketCounts[0]); // <= 0.1
            Assert.Equal(0, info.BucketCounts[1]); // <= 0.5
            Assert.Equal(1, info.Count);
        }

        [Fact]
        public void RecordHistogram_ValueInLastBucket_IncrementsAllBuckets()
        {
            var buckets = new[] { 0.1, 0.5, 1.0 };
            _collector.RecordHistogram("hist", 2.0, buckets);
            
            var snapshot = _collector.GetSnapshot();
            var hist = snapshot.GetHistograms("hist").First();
            var info = hist.HistogramInfo!.Value;
            
            // Value exceeds all buckets, goes to +Inf bucket (last one)
            Assert.Equal(0, info.BucketCounts[0]); // <= 0.1
            Assert.Equal(0, info.BucketCounts[1]); // <= 0.5
            Assert.Equal(0, info.BucketCounts[2]); // <= 1.0
            Assert.Equal(1, info.BucketCounts[3]); // +Inf
        }

        [Fact]
        public void RecordHistogram_MultipleValues_CorrectBucketCounts()
        {
            var buckets = new[] { 0.1, 1.0 };
            _collector.RecordHistogram("hist", 0.05, buckets);
            _collector.RecordHistogram("hist", 0.5, buckets);
            _collector.RecordHistogram("hist", 5.0, buckets);
            
            var snapshot = _collector.GetSnapshot();
            var hist = snapshot.GetHistograms("hist").First();
            var info = hist.HistogramInfo!.Value;
            
            // Bucket counts show values IN that bucket only (not cumulative)
            Assert.Equal(1, info.BucketCounts[0]); // <= 0.1 (only 0.05)
            Assert.Equal(1, info.BucketCounts[1]); // <= 1.0 (0.5 only, 0.05 is in first bucket)
            Assert.Equal(1, info.BucketCounts[2]); // +Inf (5.0)
            Assert.Equal(3, info.Count);
        }

        #endregion
    }
}
