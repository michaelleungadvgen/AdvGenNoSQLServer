namespace AdvGenNoSqlServer.Core.Configuration;

/// <summary>
/// Interface for configuration management
/// </summary>
public interface IConfigurationManager
{
    /// <summary>
    /// Gets the current server configuration
    /// </summary>
    ServerConfiguration Configuration { get; }

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
}