using System;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Client;

namespace ConnectionTest
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Testing connection to AdvGenNoSQL Server on localhost:9091");
            
            var client = new AdvGenNoSqlClient("localhost:9091");
            
            try
            {
                Console.WriteLine("Attempting to connect to server...");
                await client.ConnectAsync();
                
                Console.WriteLine("Connected successfully!");
                
                Console.WriteLine("Testing ping...");
                var pingResult = await client.PingAsync();
                Console.WriteLine($"Ping result: {pingResult}");
                
                Console.WriteLine("Disconnecting...");
                await client.DisconnectAsync();
                Console.WriteLine("Disconnected successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Connection failed: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            
            Console.WriteLine("Test completed.");
        }
    }
}