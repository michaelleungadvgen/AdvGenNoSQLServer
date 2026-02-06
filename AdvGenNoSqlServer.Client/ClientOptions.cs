namespace AdvGenNoSqlServer.Client;

/// <summary>
/// Options for configuring the NoSQL client.
/// </summary>
public class AdvGenNoSqlClientOptions
{
    /// <summary>
    /// Gets or sets the server address.
    /// </summary>
    public string ServerAddress { get; set; } = "localhost:8080";
    
    /// <summary>
    /// Gets or sets the connection timeout in milliseconds.
    /// </summary>
    public int ConnectionTimeout { get; set; } = 5000;
    
    /// <summary>
    /// Gets or sets a value indicating whether to use SSL.
    /// </summary>
    public bool UseSsl { get; set; } = false;
}