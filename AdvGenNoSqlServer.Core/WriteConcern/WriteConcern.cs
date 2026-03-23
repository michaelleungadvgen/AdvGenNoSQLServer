// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.WriteConcern;

/// <summary>
/// Defines the level of acknowledgment requested from the server for write operations.
/// Similar to MongoDB's write concern, this controls the durability guarantees for write operations.
/// </summary>
public class WriteConcern : IEquatable<WriteConcern>
{
    /// <summary>
    /// Gets or sets the acknowledgment level.
    /// Can be 0 (unacknowledged), 1 (acknowledged by primary), a number (acknowledged by N nodes), or "majority".
    /// </summary>
    public object W { get; set; } = 1;

    /// <summary>
    /// Gets or sets whether to wait for the write to be flushed to the journal before returning.
    /// </summary>
    public bool Journal { get; set; } = false;

    /// <summary>
    /// Gets or sets the timeout for waiting for acknowledgment.
    /// If null, uses the default timeout.
    /// </summary>
    public TimeSpan? WTimeout { get; set; }

    /// <summary>
    /// Unacknowledged write concern - requests no acknowledgment of the write operation.
    /// Use with caution as data loss may occur.
    /// </summary>
    public static WriteConcern Unacknowledged => new() { W = 0 };

    /// <summary>
    /// Acknowledged write concern - waits for acknowledgment from the primary/standalone server (default).
    /// </summary>
    public static WriteConcern Acknowledged => new() { W = 1 };

    /// <summary>
    /// Journaled write concern - waits for acknowledgment and ensures the write is flushed to the journal.
    /// Provides crash recovery guarantees.
    /// </summary>
    public static WriteConcern Journaled => new() { W = 1, Journal = true };

    /// <summary>
    /// Majority write concern - waits for the write to be acknowledged by a majority of nodes.
    /// Provides the strongest durability guarantee in a clustered environment.
    /// </summary>
    public static WriteConcern Majority => new() { W = "majority" };

    /// <summary>
    /// Creates a write concern that waits for acknowledgment from a specific number of nodes.
    /// </summary>
    /// <param name="nodeCount">The number of nodes that must acknowledge the write.</param>
    /// <returns>A new WriteConcern instance.</returns>
    public static WriteConcern Nodes(int nodeCount)
    {
        if (nodeCount < 0)
            throw new ArgumentException("Node count must be non-negative", nameof(nodeCount));

        return new WriteConcern { W = nodeCount };
    }

    /// <summary>
    /// Creates a write concern with a custom timeout.
    /// </summary>
    /// <param name="timeout">The timeout for acknowledgment.</param>
    /// <returns>A new WriteConcern instance with the specified timeout.</returns>
    public WriteConcern WithTimeout(TimeSpan timeout)
    {
        return new WriteConcern
        {
            W = W,
            Journal = Journal,
            WTimeout = timeout
        };
    }

    /// <summary>
    /// Gets a value indicating whether this write concern requires acknowledgment.
    /// </summary>
    public bool IsAcknowledged => W is not 0 and not "0";

    /// <summary>
    /// Gets a value indicating whether this write concern requires journal write.
    /// </summary>
    public bool IsJournaled => Journal;

    /// <summary>
    /// Gets a value indicating whether this write concern requires majority acknowledgment.
    /// </summary>
    public bool IsMajority => W is "majority";

    /// <summary>
    /// Validates the write concern configuration.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the configuration is invalid.</exception>
    public void Validate()
    {
        // Validate W value
        if (W is int intW)
        {
            if (intW < 0)
                throw new ArgumentException("W value must be non-negative", nameof(W));
        }
        else if (W is string strW)
        {
            if (strW != "majority" && !int.TryParse(strW, out _))
                throw new ArgumentException("W string value must be 'majority' or a number", nameof(W));
        }
        else
        {
            throw new ArgumentException("W must be an integer or the string 'majority'", nameof(W));
        }

        // Validate timeout
        if (WTimeout.HasValue && WTimeout.Value <= TimeSpan.Zero)
            throw new ArgumentException("Timeout must be positive", nameof(WTimeout));
    }

    /// <inheritdoc />
    public bool Equals(WriteConcern? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return W.Equals(other.W) &&
               Journal == other.Journal &&
               WTimeout == other.WTimeout;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return Equals(obj as WriteConcern);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(W, Journal, WTimeout);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        var wStr = W is string ? $"\"{W}\"" : W.ToString();
        var parts = new List<string> { $"w: {wStr}" };

        if (Journal)
            parts.Add("j: true");

        if (WTimeout.HasValue)
            parts.Add($"wtimeout: {WTimeout.Value.TotalMilliseconds}ms");

        return $"{{ {string.Join(", ", parts)} }}";
    }

    /// <summary>
    /// Implicit conversion from int to WriteConcern (Acknowledged level).
    /// </summary>
    public static implicit operator WriteConcern(int w)
    {
        return new WriteConcern { W = w };
    }

    /// <summary>
    /// Implicit conversion from string to WriteConcern (for "majority").
    /// </summary>
    public static implicit operator WriteConcern(string w)
    {
        if (w != "majority")
            throw new ArgumentException("String must be 'majority'", nameof(w));
        return new WriteConcern { W = w };
    }
}

/// <summary>
/// Extension methods for WriteConcern.
/// </summary>
public static class WriteConcernExtensions
{
    /// <summary>
    /// Gets the numeric W value for comparison purposes.
    /// </summary>
    public static int GetWValue(this WriteConcern concern)
    {
        return concern.W switch
        {
            int i => i,
            string s when s == "majority" => -1, // Special value for majority
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => 1
        };
    }
}
