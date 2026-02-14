# Todo List Application

A console-based Todo List application using AdvGenNoSqlClient to interact with the AdvGenNoSQL server.

## Prerequisites

- .NET 9.0 SDK
- AdvGenNoSQL Server running on localhost:9090 (default)

## Setup

1. Ensure the AdvGenNoSQL Server is running:
   ```bash
   cd AdvGenNoSqlServer.Host
   dotnet run
   ```

2. Build the Todo List application:
   ```bash
   cd AdvGenNoSqlServer.Examples\TodoList
   dotnet build
   ```

## Configuration

The application uses `appsettings.json` for configuration:

```json
{
  "ConnectionSettings": {
    "ServerAddress": "localhost",
    "Port": 9090
  }
}
```

Adjust the server address and port as needed.

## Features

- Add new todo items
- View specific todo items by ID
- Update todo item details
- Delete todo items
- Mark todo items as complete/incomplete

## Usage

Run the application:

```bash
dotnet run
```

Follow the on-screen menu prompts to interact with your todo list.

## Limitations

- The current server implementation does not support listing all documents in a collection
- To view todos, you need to know their specific IDs
- This is a limitation of the current server implementation, not the client

## Project Structure

```
TodoListApp/
├── TodoListApp.csproj          # Project file
├── appsettings.json           # Configuration file
├── Program.cs                 # Main application entry point
├── Models/
│   └── TodoItem.cs            # Todo item model
└── Services/
    └── TodoService.cs         # Data access layer
```