// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.WriteConcern;

/// <summary>
/// Manages write concern configuration for the server.
/// Provides default write concern settings and per-collection overrides.
/// </summary>
public interface IWriteConcernManager
{
    /// <summary>
    /// Gets or sets the default write concern for all operations.
    /// </summary>
    WriteConcern DefaultWriteConcern { get; set; }

    /// <summary>
    /// Gets the write concern for a specific collection.
    /// Returns the collection-specific concern if set, otherwise the default.
    /// </summary>
    /// <param name="collectionName">The name of the collection.</param>
    /// <returns>The write concern to use for the collection.</returns>
    WriteConcern GetWriteConcernForCollection(string collectionName);

    /// <summary>
    /// Sets a custom write concern for a specific collection.
    /// </summary>
    /// <param name="collectionName">The name of the collection.</param>
    /// <param name="writeConcern">The write concern to use.</param>
    Task SetCollectionWriteConcernAsync(string collectionName, WriteConcern writeConcern);

    /// <summary>
    /// Removes a custom write concern for a collection, reverting to the default.
    /// </summary>
    /// <param name="collectionName">The name of the collection.</param>
    Task RemoveCollectionWriteConcernAsync(string collectionName);

    /// <summary>
    /// Gets all collections that have custom write concerns.
    /// </summary>
    /// <returns>A dictionary of collection names to their write concerns.</returns>
    Task<IReadOnlyDictionary<string, WriteConcern>> GetCollectionsWithCustomWriteConcernAsync();

    /// <summary>
    /// Validates a write concern configuration.
    /// </summary>
    /// <param name="writeConcern">The write concern to validate.</param>
    /// <returns>True if valid, false otherwise.</returns>
    bool ValidateWriteConcern(WriteConcern writeConcern);

    /// <summary>
    /// Gets statistics about write concern usage.
    /// </summary>
    WriteConcernStatistics GetStatistics();

    /// <summary>
    /// Resets all statistics.
    /// </summary>
    void ResetStatistics();
}

/// <summary>
/// Statistics for write concern operations.
/// </summary>
public class WriteConcernStatistics
{
    /// <summary>
    /// Gets or sets the total number of write operations performed.
    /// </summary>
    public long TotalWriteOperations { get; set; }

    /// <summary>
    /// Gets or sets the number of operations using unacknowledged write concern.
    /// </summary>
    public long UnacknowledgedOperations { get; set; }

    /// <summary>
    /// Gets or sets the number of operations using acknowledged write concern.
    /// </summary>
    public long AcknowledgedOperations { get; set; }

    /// <summary>
    /// Gets or sets the number of operations using journaled write concern.
    /// </summary>
    public long JournaledOperations { get; set; }

    /// <summary>
    /// Gets or sets the number of operations using majority write concern.
    /// </summary>
    public long MajorityOperations { get; set; }

    /// <summary>
    /// Gets or sets the number of write operations that timed out waiting for acknowledgment.
    /// </summary>
    public long TimeoutCount { get; set; }

    /// <summary>
    /// Gets or sets the average acknowledgment time in milliseconds.
    /// </summary>
    public double AverageAcknowledgmentTimeMs { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when statistics were last reset.
    /// </summary>
    public DateTime LastResetAt { get; set; }

    /// <summary>
    /// Gets the distribution of write concern levels as percentages.
    /// </summary>
    public Dictionary<string, double> GetDistributionPercentages()
    {
        if (TotalWriteOperations == 0)
            return new Dictionary<string, double>
            {
                ["unacknowledged"] = 0,
                ["acknowledged"] = 0,
                ["journaled"] = 0,
                ["majority"] = 0
            };

        return new Dictionary<string, double>
        {
            ["unacknowledged"] = Math.Round((double)UnacknowledgedOperations / TotalWriteOperations * 100, 2),
            ["acknowledged"] = Math.Round((double)AcknowledgedOperations / TotalWriteOperations * 100, 2),
            ["journaled"] = Math.Round((double)JournaledOperations / TotalWriteOperations * 100, 2),
            ["majority"] = Math.Round((double)MajorityOperations / TotalWriteOperations * 100, 2)
        };
    }
}

/// <summary>
/// Configuration options for write concern.
/// </summary>
public class WriteConcernOptions
{
    /// <summary>
    /// Gets or sets the default write concern for all operations.
    /// </summary>
    public WriteConcern DefaultWriteConcern { get; set; } = WriteConcern.Acknowledged;

    /// <summary>
    /// Gets or sets the default timeout for write operations.
    /// </summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets a value indicating whether to enforce write concern on all operations.
    /// If true, operations without an explicit write concern will use the default.
    /// </summary>
    public bool EnforceWriteConcern { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to allow unacknowledged writes.
    /// Set to false to prevent data loss in production environments.
    /// </summary>
    public bool AllowUnacknowledgedWrites { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum timeout allowed for write operations.
    /// </summary>
    public TimeSpan MaxTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the per-collection write concern overrides.
    /// </summary>
    public Dictionary<string, WriteConcern> CollectionWriteConcerns { get; set; } = new();
}
