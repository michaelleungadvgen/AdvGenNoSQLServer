// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Text;

namespace AdvGenNoSqlServer.Core.Pooling;

/// <summary>
/// A specialized pool for StringBuilder instances.
/// Provides efficient string building operations with minimal allocations.
/// </summary>
public sealed class StringBuilderPool : IDisposable
{
    private static readonly Lazy<StringBuilderPool> _default = new(() => new StringBuilderPool());
    private readonly ObjectPool<StringBuilder> _pool;

    /// <summary>
    /// Gets the default global StringBuilder pool instance.
    /// </summary>
    public static StringBuilderPool Default => _default.Value;

    /// <summary>
    /// Gets the default initial capacity for StringBuilder instances.
    /// </summary>
    public const int DefaultCapacity = 256;

    /// <summary>
    /// Gets the maximum capacity for StringBuilder instances.
    /// </summary>
    public const int MaxCapacity = 4096;

    /// <summary>
    /// Gets the statistics for the pool.
    /// </summary>
    public PoolStatistics Statistics => _pool.Statistics;

    /// <summary>
    /// Initializes a new instance of the <see cref="StringBuilderPool"/> class.
    /// </summary>
    /// <param name="maxPoolSize">The maximum number of StringBuilders to keep in the pool.</param>
    public StringBuilderPool(int maxPoolSize = 100)
    {
        _pool = new ObjectPool<StringBuilder>(
            maxCapacity: maxPoolSize,
            factory: () => new StringBuilder(DefaultCapacity),
            resetAction: sb =>
            {
                sb.Clear();
                // Only reduce capacity if it exceeds max and buffer is empty
                if (sb.Capacity > MaxCapacity)
                {
                    sb.Capacity = DefaultCapacity;
                }
            });
    }

    /// <summary>
    /// Rents a StringBuilder from the pool.
    /// </summary>
    /// <returns>A StringBuilder instance.</returns>
    public StringBuilder Rent()
    {
        return _pool.Rent();
    }

    /// <summary>
    /// Returns a StringBuilder to the pool.
    /// </summary>
    /// <param name="builder">The StringBuilder to return.</param>
    public void Return(StringBuilder builder)
    {
        if (builder != null)
        {
            _pool.Return(builder);
        }
    }

    /// <summary>
    /// Rents a StringBuilder, executes an action, and returns it to the pool.
    /// </summary>
    /// <param name="action">The action to execute with the StringBuilder.</param>
    /// <returns>The resulting string.</returns>
    public string RentAndExecute(Action<StringBuilder> action)
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        var sb = Rent();
        try
        {
            action(sb);
            return sb.ToString();
        }
        finally
        {
            Return(sb);
        }
    }

    /// <summary>
    /// Rents a StringBuilder wrapped in a disposable struct for automatic return.
    /// </summary>
    /// <returns>A pooled StringBuilder wrapper.</returns>
    /// <example>
    /// using var sb = StringBuilderPool.Default.RentDisposable();
    /// sb.Append("Hello");
    /// var result = sb.ToString();
    /// </example>
    public PooledStringBuilder RentDisposable()
    {
        return new PooledStringBuilder(this);
    }

    /// <summary>
    /// Disposes the pool.
    /// </summary>
    public void Dispose()
    {
        _pool.Dispose();
    }
}

/// <summary>
/// Represents a pooled StringBuilder that automatically returns to the pool when disposed.
/// </summary>
public readonly struct PooledStringBuilder : IDisposable
{
    private readonly StringBuilderPool? _pool;

    /// <summary>
    /// Gets the StringBuilder instance.
    /// </summary>
    public StringBuilder Builder { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PooledStringBuilder"/> struct.
    /// </summary>
    /// <param name="pool">The pool to return to.</param>
    public PooledStringBuilder(StringBuilderPool pool)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        Builder = pool.Rent();
    }

    /// <summary>
    /// Appends a string to the builder.
    /// </summary>
    public PooledStringBuilder Append(string? value)
    {
        Builder.Append(value);
        return this;
    }

    /// <summary>
    /// Appends a character to the builder.
    /// </summary>
    public PooledStringBuilder Append(char value)
    {
        Builder.Append(value);
        return this;
    }

    /// <summary>
    /// Appends a formatted string to the builder.
    /// </summary>
    public PooledStringBuilder AppendFormat(string format, params object?[] args)
    {
        Builder.AppendFormat(format, args);
        return this;
    }

    /// <summary>
    /// Appends a line to the builder.
    /// </summary>
    public PooledStringBuilder AppendLine(string? value = null)
    {
        Builder.AppendLine(value);
        return this;
    }

    /// <summary>
    /// Clears the builder.
    /// </summary>
    public PooledStringBuilder Clear()
    {
        Builder.Clear();
        return this;
    }

    /// <summary>
    /// Returns the builder to the pool.
    /// </summary>
    public void Dispose()
    {
        _pool?.Return(Builder);
    }

    /// <summary>
    /// Returns the string representation of the builder.
    /// </summary>
    public override string ToString() => Builder.ToString();

    /// <summary>
    /// Implicitly converts to string.
    /// </summary>
    public static implicit operator string(PooledStringBuilder pooled) => pooled.ToString();
}
