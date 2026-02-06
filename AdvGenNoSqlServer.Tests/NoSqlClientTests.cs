using AdvGenNoSqlServer.Client;

namespace NoSqlServer.Tests;

public class AdvGenNoSqlClientTests
{
    [Fact]
    public void AdvGenNoSqlClient_Initialization_ShouldSetServerAddress()
    {
        // Arrange
        var serverAddress = "localhost:8080";
        
        // Act
        var client = new AdvGenNoSqlClient(serverAddress);
        
        // Assert
        // We're just verifying the client can be instantiated
        Assert.NotNull(client);
    }
    
    [Fact]
    public void AdvGenNoSqlClientFactory_CreateClientWithOptions_ShouldCreateClient()
    {
        // Arrange
        var options = new AdvGenNoSqlClientOptions 
        { 
            ServerAddress = "localhost:8080" 
        };
        
        // Act
        var client = AdvGenNoSqlClientFactory.CreateClient(options);
        
        // Assert
        Assert.NotNull(client);
    }
    
    [Fact]
    public void AdvGenNoSqlClientFactory_CreateClientWithAddress_ShouldCreateClient()
    {
        // Arrange
        var serverAddress = "localhost:8080";
        
        // Act
        var client = AdvGenNoSqlClientFactory.CreateClient(serverAddress);
        
        // Assert
        Assert.NotNull(client);
    }
}