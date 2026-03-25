// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using AdvGenNoSqlServer.Core.Models;

namespace AdvGenNoSqlServer.Core.MapReduce
{
    /// <summary>
    /// Result for word count map-reduce job.
    /// </summary>
    public class WordCountResult
    {
        /// <summary>
        /// Gets or sets the word.
        /// </summary>
        public string Word { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the count.
        /// </summary>
        public int Count { get; set; }
    }

    /// <summary>
    /// Map-reduce job for counting words in document fields.
    /// </summary>
    public class WordCountJob : IMapReduceJob<string, int, WordCountResult>
    {
        private readonly string _fieldName;
        private readonly char[] _separators;
        private readonly StringComparison _comparison;

        /// <summary>
        /// Gets the job name.
        /// </summary>
        public string JobName => "WordCount";

        /// <summary>
        /// Initializes a new instance of the <see cref="WordCountJob"/> class.
        /// </summary>
        /// <param name="fieldName">The field name containing text to count.</param>
        /// <param name="separators">The word separators (default: space, comma, period, etc.).</param>
        /// <param name="caseSensitive">Whether to treat words as case-sensitive.</param>
        public WordCountJob(
            string fieldName,
            char[]? separators = null,
            bool caseSensitive = false)
        {
            _fieldName = fieldName ?? throw new ArgumentNullException(nameof(fieldName));
            _separators = separators ?? new[] { ' ', '\t', '\n', '\r', '.', ',', '!', '?', ';', ':', '-', '_', '(', ')', '[', ']', '{', '}', '"', '\'' };
            _comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        }

        /// <inheritdoc />
        public void Map(Document document, MapReduceContext<string, int> context)
        {
            if (!document.Data.TryGetValue(_fieldName, out var value))
                return;

            var text = value?.ToString();
            if (string.IsNullOrWhiteSpace(text))
                return;

            var words = text.Split(_separators, StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in words)
            {
                var normalized = _comparison == StringComparison.OrdinalIgnoreCase
                    ? word.ToLowerInvariant()
                    : word;

                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    context.Emit(normalized, 1);
                }
            }
        }

        /// <inheritdoc />
        public WordCountResult Reduce(string key, IEnumerable<int> values)
        {
            return new WordCountResult
            {
                Word = key,
                Count = values.Sum()
            };
        }
    }

    /// <summary>
    /// Result for sum by key map-reduce job.
    /// </summary>
    public class SumByKeyResult<TKey>
    {
        /// <summary>
        /// Gets or sets the key.
        /// </summary>
        public TKey Key { get; set; } = default!;

        /// <summary>
        /// Gets or sets the sum.
        /// </summary>
        public double Sum { get; set; }

        /// <summary>
        /// Gets or sets the count of values.
        /// </summary>
        public int Count { get; set; }
    }

    /// <summary>
    /// Map-reduce job for summing numeric values by key.
    /// </summary>
    public class SumByKeyJob : IMapReduceJob<string, double, SumByKeyResult<string>>
    {
        private readonly string _keyField;
        private readonly string _valueField;

        /// <summary>
        /// Gets the job name.
        /// </summary>
        public string JobName => "SumByKey";

        /// <summary>
        /// Initializes a new instance of the <see cref="SumByKeyJob"/> class.
        /// </summary>
        /// <param name="keyField">The field to use as the grouping key.</param>
        /// <param name="valueField">The numeric field to sum.</param>
        public SumByKeyJob(string keyField, string valueField)
        {
            _keyField = keyField ?? throw new ArgumentNullException(nameof(keyField));
            _valueField = valueField ?? throw new ArgumentNullException(nameof(valueField));
        }

        /// <inheritdoc />
        public void Map(Document document, MapReduceContext<string, double> context)
        {
            if (!document.Data.TryGetValue(_keyField, out var keyValue))
                return;

            if (!document.Data.TryGetValue(_valueField, out var numericValue))
                return;

            var key = keyValue?.ToString();
            if (string.IsNullOrWhiteSpace(key))
                return;

            if (!TryConvertToDouble(numericValue, out var value))
                return;

            context.Emit(key, value);
        }

        /// <inheritdoc />
        public SumByKeyResult<string> Reduce(string key, IEnumerable<double> values)
        {
            var valueList = values.ToList();
            return new SumByKeyResult<string>
            {
                Key = key,
                Sum = valueList.Sum(),
                Count = valueList.Count
            };
        }

        private static bool TryConvertToDouble(object? value, out double result)
        {
            result = 0;
            if (value == null) return false;

            if (value is double d) { result = d; return true; }
            if (value is float f) { result = f; return true; }
            if (value is int i) { result = i; return true; }
            if (value is long l) { result = l; return true; }
            if (value is decimal dec) { result = (double)dec; return true; }
            if (value is short s) { result = s; return true; }

            return double.TryParse(value.ToString(), out result);
        }
    }

    /// <summary>
    /// Result for average by key map-reduce job.
    /// </summary>
    public class AverageByKeyResult<TKey>
    {
        /// <summary>
        /// Gets or sets the key.
        /// </summary>
        public TKey Key { get; set; } = default!;

        /// <summary>
        /// Gets or sets the average.
        /// </summary>
        public double Average { get; set; }

        /// <summary>
        /// Gets or sets the count of values.
        /// </summary>
        public int Count { get; set; }

        /// <summary>
        /// Gets or sets the sum of values.
        /// </summary>
        public double Sum { get; set; }

        /// <summary>
        /// Gets or sets the minimum value.
        /// </summary>
        public double Min { get; set; }

        /// <summary>
        /// Gets or sets the maximum value.
        /// </summary>
        public double Max { get; set; }
    }

    /// <summary>
    /// Map-reduce job for calculating averages and statistics by key.
    /// </summary>
    public class AverageByKeyJob : IMapReduceJob<string, double, AverageByKeyResult<string>>
    {
        private readonly string _keyField;
        private readonly string _valueField;

        /// <summary>
        /// Gets the job name.
        /// </summary>
        public string JobName => "AverageByKey";

        /// <summary>
        /// Initializes a new instance of the <see cref="AverageByKeyJob"/> class.
        /// </summary>
        /// <param name="keyField">The field to use as the grouping key.</param>
        /// <param name="valueField">The numeric field to average.</param>
        public AverageByKeyJob(string keyField, string valueField)
        {
            _keyField = keyField ?? throw new ArgumentNullException(nameof(keyField));
            _valueField = valueField ?? throw new ArgumentNullException(nameof(valueField));
        }

        /// <inheritdoc />
        public void Map(Document document, MapReduceContext<string, double> context)
        {
            if (!document.Data.TryGetValue(_keyField, out var keyValue))
                return;

            if (!document.Data.TryGetValue(_valueField, out var numericValue))
                return;

            var key = keyValue?.ToString();
            if (string.IsNullOrWhiteSpace(key))
                return;

            if (!TryConvertToDouble(numericValue, out var value))
                return;

            context.Emit(key, value);
        }

        /// <inheritdoc />
        public AverageByKeyResult<string> Reduce(string key, IEnumerable<double> values)
        {
            var valueList = values.ToList();
            var sum = valueList.Sum();
            var count = valueList.Count;

            return new AverageByKeyResult<string>
            {
                Key = key,
                Sum = sum,
                Count = count,
                Average = count > 0 ? sum / count : 0,
                Min = count > 0 ? valueList.Min() : 0,
                Max = count > 0 ? valueList.Max() : 0
            };
        }

        private static bool TryConvertToDouble(object? value, out double result)
        {
            result = 0;
            if (value == null) return false;

            if (value is double d) { result = d; return true; }
            if (value is float f) { result = f; return true; }
            if (value is int i) { result = i; return true; }
            if (value is long l) { result = l; return true; }
            if (value is decimal dec) { result = (double)dec; return true; }
            if (value is short s) { result = s; return true; }

            return double.TryParse(value.ToString(), out result);
        }
    }

    /// <summary>
    /// Result for count by key map-reduce job.
    /// </summary>
    public class CountByKeyResult<TKey>
    {
        /// <summary>
        /// Gets or sets the key.
        /// </summary>
        public TKey Key { get; set; } = default!;

        /// <summary>
        /// Gets or sets the count.
        /// </summary>
        public int Count { get; set; }
    }

    /// <summary>
    /// Map-reduce job for counting documents by key.
    /// </summary>
    public class CountByKeyJob : IMapReduceJob<string, int, CountByKeyResult<string>>
    {
        private readonly string _keyField;

        /// <summary>
        /// Gets the job name.
        /// </summary>
        public string JobName => "CountByKey";

        /// <summary>
        /// Initializes a new instance of the <see cref="CountByKeyJob"/> class.
        /// </summary>
        /// <param name="keyField">The field to use as the grouping key.</param>
        public CountByKeyJob(string keyField)
        {
            _keyField = keyField ?? throw new ArgumentNullException(nameof(keyField));
        }

        /// <inheritdoc />
        public void Map(Document document, MapReduceContext<string, int> context)
        {
            if (!document.Data.TryGetValue(_keyField, out var keyValue))
                return;

            var key = keyValue?.ToString();
            if (string.IsNullOrWhiteSpace(key))
                return;

            context.Emit(key, 1);
        }

        /// <inheritdoc />
        public CountByKeyResult<string> Reduce(string key, IEnumerable<int> values)
        {
            return new CountByKeyResult<string>
            {
                Key = key,
                Count = values.Sum()
            };
        }
    }
}
