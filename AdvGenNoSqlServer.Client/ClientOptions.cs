// Copyright (c) 2026 [Your Organization]
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Client
{
    /// <summary>
    /// Options for configuring the NoSQL client.
    /// </summary>
    public class AdvGenNoSqlClientOptions
    {
        /// <summary>
        /// Gets or sets the server address.
        /// </summary>
        public string ServerAddress { get; set; } = "localhost:9090";

        /// <summary>
        /// Gets or sets the connection timeout in milliseconds.
        /// </summary>
        public int ConnectionTimeout { get; set; } = 5000;

        /// <summary>
        /// Gets or sets a value indicating whether to use SSL.
        /// </summary>
        public bool UseSsl { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether keep-alive is enabled.
        /// </summary>
        public bool EnableKeepAlive { get; set; } = true;

        /// <summary>
        /// Gets or sets the keep-alive interval in milliseconds.
        /// </summary>
        public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets or sets the maximum retry attempts for failed operations.
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>
        /// Gets or sets the retry delay in milliseconds.
        /// </summary>
        public int RetryDelayMs { get; set; } = 1000;

        /// <summary>
        /// Gets or sets a value indicating whether to auto-reconnect on connection loss.
        /// </summary>
        public bool AutoReconnect { get; set; } = false;
    }
}
