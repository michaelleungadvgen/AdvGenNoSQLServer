// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Text.Json;
using AdvGenNoSqlServer.Examples.Messages;

// AdvGenNoSQL cache-mode messaging demo.
//
// A small message board / chat log backed by an AdvGenNoSQL server running in
// StorageMode=Cache. Messages are stored only in server memory (Redis-style) and
// disappear when the server restarts - nothing is written to disk.
//
// Run the server in cache mode first, e.g.:
//   NOSQL_STORAGE_MODE=Cache NOSQL_ENABLE_SSL=false dotnet run --project AdvGenNoSqlServer.Host
// or use run-messages-demo.bat, which starts both.
//
// Usage:
//   dotnet run --project AdvGenNoSqlServer.Examples.Messages            (interactive)
//   dotnet run --project AdvGenNoSqlServer.Examples.Messages -- --demo  (scripted tour)
// Options: --host --port --user --password --collection --ssl

var host = "localhost";
var port = 9191;
var user = "admin";
var password = "admin123";
var collection = "messages";
var useSsl = false;
var demo = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--host": host = args[++i]; break;
        case "--port": port = int.Parse(args[++i]); break;
        case "--user": user = args[++i]; break;
        case "--password": password = args[++i]; break;
        case "--collection": collection = args[++i]; break;
        case "--ssl": useSsl = true; break;
        case "--demo": demo = true; break;
        case "--help":
            Console.WriteLine("Options: --host <h> --port <p> --user <u> --password <pw> --collection <c> --ssl --demo");
            return 0;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]} (use --help)");
            return 1;
    }
}

await using var service = new MessageService(host, port, user, password, useSsl, collection);

try
{
    await service.ConnectAsync();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed to connect to {service.ConnectionDescription}: {ex.Message}");
    Console.Error.WriteLine("Start the server in cache mode first, e.g. run-messages-demo.bat, or:");
    Console.Error.WriteLine("  NOSQL_STORAGE_MODE=Cache NOSQL_ENABLE_SSL=false dotnet run --project AdvGenNoSqlServer.Host");
    return 1;
}

PrintBanner(service);

if (demo)
{
    await RunDemoAsync(service);
    return 0;
}

await RunInteractiveAsync(service);
return 0;

static void PrintBanner(MessageService service)
{
    Console.WriteLine("==============================================================");
    Console.WriteLine(" AdvGenNoSQL - Cache-Mode Messaging Demo");
    Console.WriteLine("==============================================================");
    Console.WriteLine($" Server     : {service.ConnectionDescription}");
    Console.WriteLine($" Collection : {service.Collection}");
    Console.WriteLine($" Logged in  : {service.Username} (role: {service.Role ?? "?"})");
    Console.WriteLine();
    Console.WriteLine(" The server runs in StorageMode=Cache: messages live ONLY in");
    Console.WriteLine(" server memory (Redis-style). Nothing is written to disk, and");
    Console.WriteLine(" all messages are lost when the server restarts.");
    Console.WriteLine();
}

static async Task RunDemoAsync(MessageService service)
{
    Console.WriteLine("--- Scripted demo (--demo) ---");
    Console.WriteLine("Clearing any previous messages...");
    await service.ClearAsync();

    Console.WriteLine("Sending messages as alice and bob...");
    await service.SendAsync("alice", "Hey bob, welcome to the cache-mode demo!");
    await service.SendAsync("bob", "Hi alice! These messages are stored in server RAM only.");
    await service.SendAsync("alice", "Exactly - restart the server and they vanish. No disk writes.");

    await PrintMessagesAsync(service);
    Console.WriteLine($"Message count: {await service.CountAsync()}");

    var stats = await service.StatsAsync();
    if (stats is JsonElement s)
    {
        Console.WriteLine("Server stats:");
        foreach (var key in new[] { "version", "uptimeSeconds", "totalDocuments", "totalCollections", "memoryUsageMB", "activeConnections" })
            if (s.TryGetProperty(key, out var value))
                Console.WriteLine($"  {key}: {value}");
    }

    Console.WriteLine();
    Console.WriteLine("Done. These messages exist only in the server's memory (cache mode).");
}

static async Task RunInteractiveAsync(MessageService service)
{
    var nick = service.Username;
    PrintHelp();
    await PrintMessagesAsync(service);

    while (true)
    {
        Console.Write($"{nick}> ");
        var line = Console.ReadLine();
        if (line is null) break; // EOF
        line = line.Trim();
        if (line.Length == 0) continue;

        try
        {
            if (line.StartsWith('/'))
            {
                var parts = line.Split(' ', 2);
                var cmd = parts[0].ToLowerInvariant();
                var arg = parts.Length > 1 ? parts[1].Trim() : "";
                switch (cmd)
                {
                    case "/quit":
                    case "/exit":
                        Console.WriteLine("Bye! (Messages remain in server memory until it restarts.)");
                        return;
                    case "/help":
                        PrintHelp();
                        break;
                    case "/nick":
                        if (arg.Length > 0) { nick = arg; Console.WriteLine($"You are now '{nick}'."); }
                        break;
                    case "/list":
                        await PrintMessagesAsync(service);
                        break;
                    case "/count":
                        Console.WriteLine($"{await service.CountAsync()} message(s) in '{service.Collection}'.");
                        break;
                    case "/clear":
                        await service.ClearAsync();
                        Console.WriteLine("Cleared all messages.");
                        break;
                    case "/stats":
                        var stats = await service.StatsAsync();
                        Console.WriteLine(stats is JsonElement st ? st.ToString() : "(no stats)");
                        break;
                    default:
                        Console.WriteLine($"Unknown command '{cmd}'. Type /help.");
                        break;
                }
            }
            else
            {
                await service.SendAsync(nick, line);
                Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] {nick}: {line}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
        }
    }
}

static async Task PrintMessagesAsync(MessageService service)
{
    var messages = await service.ListAsync();
    Console.WriteLine($"--- {messages.Count} message(s) in '{service.Collection}' (server memory) ---");
    foreach (var m in messages)
        Console.WriteLine($"[{m.Timestamp:HH:mm:ss}] {m.User}: {m.Text}");
    if (messages.Count == 0) Console.WriteLine("(empty)");
    Console.WriteLine();
}

static void PrintHelp()
{
    Console.WriteLine("Commands:");
    Console.WriteLine("  <text>        send a message");
    Console.WriteLine("  /nick <name>  change your display name");
    Console.WriteLine("  /list         show all messages");
    Console.WriteLine("  /count        count messages");
    Console.WriteLine("  /clear        delete all messages");
    Console.WriteLine("  /stats        server statistics");
    Console.WriteLine("  /quit         exit");
    Console.WriteLine();
}