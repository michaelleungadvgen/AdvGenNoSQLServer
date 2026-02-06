# AdvGenNoSqlServer.Client

This project contains the client library for connecting to the NoSQL server.

## Usage

```csharp
var options = new AdvGenNoSqlClientOptions 
{ 
    ServerAddress = "localhost:8080" 
};

var client = AdvGenNoSqlClientFactory.CreateClient(options);
await client.ConnectAsync();

// Execute queries
var result = await client.ExecuteQueryAsync("SELECT * FROM users");

await client.DisconnectAsync();
```