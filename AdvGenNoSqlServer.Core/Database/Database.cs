// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.Database
{
    /// <summary>
    /// Represents a database in the NoSQL server.
    /// </summary>
    public class Database
    {
        /// <summary>
        /// Gets the unique name of the database.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the path where database data is stored.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Gets the date and time when the database was created.
        /// </summary>
        public DateTime CreatedAt { get; }

        /// <summary>
        /// Gets the configuration options for this database.
        /// </summary>
        public DatabaseOptions Options { get; }

        /// <summary>
        /// Gets the security settings for this database.
        /// </summary>
        public DatabaseSecurity Security { get; }

        /// <summary>
        /// Gets or sets the number of collections in this database.
        /// </summary>
        public int CollectionCount { get; set; }

        /// <summary>
        /// Gets or sets the total size of the database in bytes.
        /// </summary>
        public long SizeBytes { get; set; }

        /// <summary>
        /// Gets the last time the database was accessed.
        /// </summary>
        public DateTime? LastAccessedAt { get; internal set; }

        /// <summary>
        /// Gets a value indicating whether this is a system database.
        /// System databases cannot be dropped.
        /// </summary>
        public bool IsSystemDatabase => Name.StartsWith("_");

        /// <summary>
        /// Initializes a new instance of the <see cref="Database"/> class.
        /// </summary>
        /// <param name="name">The database name.</param>
        /// <param name="path">The storage path.</param>
        /// <param name="options">The database options.</param>
        /// <param name="security">The security settings.</param>
        /// <exception cref="ArgumentException">Thrown when name or path is invalid.</exception>
        public Database(string name, string path, DatabaseOptions? options = null, DatabaseSecurity? security = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Database name cannot be null or empty", nameof(name));

            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Database path cannot be null or empty", nameof(path));

            // Validate database name format
            if (!IsValidDatabaseName(name))
                throw new ArgumentException($"Invalid database name: '{name}'. Names must start with a letter or underscore and contain only alphanumeric characters, underscores, or hyphens.", nameof(name));

            Name = name;
            Path = path;
            CreatedAt = DateTime.UtcNow;
            Options = options ?? new DatabaseOptions();
            Security = security ?? new DatabaseSecurity();
            CollectionCount = 0;
            SizeBytes = 0;
        }

        /// <summary>
        /// Validates a database name.
        /// </summary>
        /// <param name="name">The name to validate.</param>
        /// <returns>True if the name is valid; otherwise, false.</returns>
        public static bool IsValidDatabaseName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            // Must start with letter or underscore
            if (!char.IsLetter(name[0]) && name[0] != '_')
                return false;

            // Can only contain alphanumeric, underscore, or hyphen
            foreach (char c in name)
            {
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '-')
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Updates the last accessed timestamp.
        /// </summary>
        public void Touch()
        {
            LastAccessedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Gets statistics for this database.
        /// </summary>
        public DatabaseStatistics GetStatistics()
        {
            return new DatabaseStatistics
            {
                Name = Name,
                CollectionCount = CollectionCount,
                SizeBytes = SizeBytes,
                CreatedAt = CreatedAt,
                LastAccessedAt = LastAccessedAt,
                IsSystemDatabase = IsSystemDatabase,
                MaxCollections = Options.MaxCollections,
                MaxSizeBytes = Options.MaxSizeBytes
            };
        }

        /// <summary>
        /// Returns a string representation of the database.
        /// </summary>
        public override string ToString()
        {
            return $"Database: {Name} (Collections: {CollectionCount}, Size: {FormatBytes(SizeBytes)})";
        }

        private static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            return $"{number:n1} {suffixes[counter]}";
        }
    }

    /// <summary>
    /// Statistics for a database.
    /// </summary>
    public class DatabaseStatistics
    {
        /// <summary>
        /// Gets the database name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets the number of collections.
        /// </summary>
        public int CollectionCount { get; set; }

        /// <summary>
        /// Gets the total size in bytes.
        /// </summary>
        public long SizeBytes { get; set; }

        /// <summary>
        /// Gets the creation timestamp.
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Gets the last access timestamp.
        /// </summary>
        public DateTime? LastAccessedAt { get; set; }

        /// <summary>
        /// Gets whether this is a system database.
        /// </summary>
        public bool IsSystemDatabase { get; set; }

        /// <summary>
        /// Gets the maximum allowed collections.
        /// </summary>
        public int MaxCollections { get; set; }

        /// <summary>
        /// Gets the maximum allowed size in bytes.
        /// </summary>
        public long MaxSizeBytes { get; set; }

        /// <summary>
        /// Gets the utilization percentage (0-100).
        /// </summary>
        public double SizeUtilizationPercent => MaxSizeBytes > 0 ? (SizeBytes / (double)MaxSizeBytes) * 100 : 0;

        /// <summary>
        /// Gets the collection utilization percentage (0-100).
        /// </summary>
        public double CollectionUtilizationPercent => MaxCollections > 0 ? (CollectionCount / (double)MaxCollections) * 100 : 0;
    }
}
