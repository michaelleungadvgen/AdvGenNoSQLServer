namespace AdvGenNoSqlServer.Client;

/// <summary>
/// Factory for creating NoSQL client instances.
/// </summary>
public static class AdvGenNoSqlClientFactory
{
    /// <summary>
    /// Creates a new instance of the AdvGenNoSqlClient with the specified options.
    /// </summary>
    /// <param name="options">The client options.</param>
    /// <returns>A new AdvGenNoSqlClient instance.</returns>
    public static AdvGenNoSqlClient CreateClient(AdvGenNoSqlClientOptions options)
    {
        return new AdvGenNoSqlClient(options.ServerAddress);
    }
    
    /// <summary>
    /// Creates a new instance of the AdvGenNoSqlClient with the specified server address.
    /// </summary>
    /// <param name="serverAddress">The server address.</param>
    /// <returns>A new AdvGenNoSqlClient instance.</returns>
    public static AdvGenNoSqlClient CreateClient(string serverAddress)
    {
        return new AdvGenNoSqlClient(serverAddress);
    }
}