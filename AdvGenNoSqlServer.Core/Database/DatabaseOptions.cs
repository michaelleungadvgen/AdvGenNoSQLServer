// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.Database
{
    /// <summary>
    /// Configuration options for a database.
    /// </summary>
    public class DatabaseOptions
    {
        /// <summary>
        /// Gets or sets the maximum number of collections allowed in this database.
        /// Default is 100.
        /// </summary>
        public int MaxCollections { get; set; } = 100;

        /// <summary>
        /// Gets or sets the maximum size of the database in bytes.
        /// Default is 10GB.
        /// </summary>
        public long MaxSizeBytes { get; set; } = 10_737_418_240; // 10GB

        /// <summary>
        /// Gets or sets whether to allow anonymous read access to this database.
        /// Default is false.
        /// </summary>
        public bool AllowAnonymousRead { get; set; } = false;

        /// <summary>
        /// Gets or sets whether to enable write-ahead logging for this database.
        /// Default is true.
        /// </summary>
        public bool EnableWAL { get; set; } = true;

        /// <summary>
        /// Gets or sets the default time-to-live (TTL) for documents in seconds.
        /// Null means no default TTL.
        /// </summary>
        public int? DefaultDocumentTTL { get; set; }

        /// <summary>
        /// Gets or sets whether to compress document data.
        /// Default is false.
        /// </summary>
        public bool CompressionEnabled { get; set; } = false;

        /// <summary>
        /// Gets or sets whether authentication is required to access this database.
        /// </summary>
        public bool RequireAuthentication { get; set; } = true;

        /// <summary>
        /// Creates a copy of these options.
        /// </summary>
        public DatabaseOptions Clone()
        {
            return new DatabaseOptions
            {
                MaxCollections = this.MaxCollections,
                MaxSizeBytes = this.MaxSizeBytes,
                AllowAnonymousRead = this.AllowAnonymousRead,
                EnableWAL = this.EnableWAL,
                DefaultDocumentTTL = this.DefaultDocumentTTL,
                CompressionEnabled = this.CompressionEnabled
            };
        }

        /// <summary>
        /// Validates the options and returns any validation errors.
        /// </summary>
        public List<string> Validate()
        {
            var errors = new List<string>();

            if (MaxCollections < 1)
                errors.Add("MaxCollections must be at least 1");

            if (MaxCollections > 10000)
                errors.Add("MaxCollections cannot exceed 10000");

            if (MaxSizeBytes < 1024 * 1024) // 1MB minimum
                errors.Add("MaxSizeBytes must be at least 1MB");

            if (DefaultDocumentTTL.HasValue && DefaultDocumentTTL.Value < 1)
                errors.Add("DefaultDocumentTTL must be at least 1 second");

            return errors;
        }

        /// <summary>
        /// Returns a value indicating whether the options are valid.
        /// </summary>
        public bool IsValid => Validate().Count == 0;
    }
}
