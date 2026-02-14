# Todo List Application Implementation Plan

## Overview
Create a console-based Todo List application using AdvGenNoSqlClient to interact with the AdvGenNoSQL server. The application will provide basic CRUD operations for managing todo items.

## Project Structure
```
AdvGenNoSqlServer.Examples/
└── TodoList/
    ├── TodoListApp.csproj
    ├── Models/
    │   └── TodoItem.cs
    ├── Services/
    │   └── TodoService.cs
    ├── Program.cs
    └── appsettings.json
```

## Features
1. Add new todo items
2. View all todo items
3. Mark todo items as completed
4. Update todo item details
5. Delete todo items
6. Search/filter todo items

## Technical Implementation

### Models
- `TodoItem`: Represents a single todo item with properties:
  - Id (string)
  - Title (string)
  - Description (string)
  - IsCompleted (bool)
  - CreatedDate (DateTime)
  - DueDate (DateTime?)

### Services
- `TodoService`: Handles all database operations using AdvGenNoSqlClient
  - GetAllAsync()
  - GetByIdAsync(string id)
  - AddAsync(TodoItem item)
  - UpdateAsync(TodoItem item)
  - DeleteAsync(string id)
  - ToggleCompleteAsync(string id)

### User Interface
- Console-based menu system
- Input validation
- Error handling

## Implementation Steps
1. Set up the project structure and dependencies
2. Define the TodoItem model
3. Implement the TodoService with AdvGenNoSqlClient
4. Create the console UI with menu options
5. Implement all CRUD operations
6. Add error handling and input validation
7. Test the application

## Dependencies
- AdvGenNoSqlServer.Client
- Microsoft.Extensions.Configuration
- Microsoft.Extensions.DependencyInjection
- Microsoft.Extensions.Logging