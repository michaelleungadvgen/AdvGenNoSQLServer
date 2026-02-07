// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.Configuration;

/// <summary>
/// Interface for configuration management with hot-reload support
/// </summary>
public interface IConfigurationManager
{
    /// <summary>
    /// Gets the current server configuration
    /// </summary>
    ServerConfiguration Configuration { get; }

    /// <summary>
    /// Gets whether hot-reload is currently enabled
    /// </summary>
    bool IsHotReloadEnabled { get; }

    /// <summary>
    /// Event raised when the configuration is changed
    /// </summary>
    event EventHandler<ConfigurationChangedEventArgs>? ConfigurationChanged;

    /// <summary>
    /// Reloads the configuration from source
    /// </summary>
    void ReloadConfiguration();

    /// <summary>
    /// Updates a configuration value
    /// </summary>
    /// <param name="key">The configuration key</param>
    /// <param name="value">The new value</param>
    void UpdateConfiguration(string key, object value);

    /// <summary>
    /// Enables hot-reload monitoring of the configuration file
    /// </summary>
    void EnableHotReload();

    /// <summary>
    /// Disables hot-reload monitoring of the configuration file
    /// </summary>
    void DisableHotReload();
}
