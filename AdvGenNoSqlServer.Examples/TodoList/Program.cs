using AdvGenNoSqlServer.Client;
using AdvGenNoSqlServer.Examples.TodoList.Models;
using AdvGenNoSqlServer.Examples.TodoList.Services;
using Microsoft.Extensions.Configuration;

// Load configuration
var builder = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
var configuration = builder.Build();

var serverAddress = configuration["ConnectionSettings:ServerAddress"] ?? "localhost";
var port = int.Parse(configuration["ConnectionSettings:Port"] ?? "9090");
var fullServerAddress = $"{serverAddress}:{port}";

Console.WriteLine("Todo List Application");
Console.WriteLine("=====================");

try
{
    // Initialize the NoSQL client
    using var client = new AdvGenNoSqlClient(fullServerAddress);
    await client.ConnectAsync();
    
    var todoService = new TodoService(client);
    
    bool exit = false;
    while (!exit)
    {
        Console.WriteLine("\nSelect an option:");
        Console.WriteLine("1. View all todos");
        Console.WriteLine("2. Add new todo");
        Console.WriteLine("3. Update todo");
        Console.WriteLine("4. Delete todo");
        Console.WriteLine("5. Mark todo as complete/incomplete");
        Console.WriteLine("6. Exit");
        Console.Write("Enter your choice (1-6): ");

        var choice = Console.ReadLine();
        
        switch (choice)
        {
            case "1":
                await ShowAllTodos(todoService);
                break;
            case "2":
                await AddNewTodo(todoService);
                break;
            case "3":
                await UpdateTodo(todoService);
                break;
            case "4":
                await DeleteTodo(todoService);
                break;
            case "5":
                await ToggleTodoCompletion(todoService);
                break;
            case "6":
                exit = true;
                Console.WriteLine("Goodbye!");
                break;
            default:
                Console.WriteLine("Invalid choice. Please enter a number between 1-6.");
                break;
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"An error occurred: {ex.Message}");
}

// Helper methods
static async Task ShowAllTodos(TodoService service)
{
    Console.WriteLine("\n--- All Todos ---");
    Console.WriteLine("Note: The current server implementation does not support listing all documents.");
    Console.WriteLine("To view a specific todo, you need to know its ID.");
    
    Console.Write("Would you like to search for a specific todo by ID? (y/N): ");
    var response = Console.ReadLine();
    
    if (response?.ToLower() == "y" || response?.ToLower() == "yes")
    {
        Console.Write("Enter the todo ID: ");
        var id = Console.ReadLine();
        
        if (!string.IsNullOrEmpty(id))
        {
            var todo = await service.GetByIdAsync(id);
            if (todo != null)
            {
                var status = todo.IsCompleted ? "[COMPLETED]" : "[PENDING]";
                Console.WriteLine($"{status} ID: {todo.Id}");
                Console.WriteLine($"  Title: {todo.Title}");
                Console.WriteLine($"  Description: {todo.Description}");
                Console.WriteLine($"  Created: {todo.CreatedDate:yyyy-MM-dd HH:mm:ss}");
                if (todo.DueDate.HasValue)
                {
                    Console.WriteLine($"  Due: {todo.DueDate:yyyy-MM-dd HH:mm:ss}");
                }
            }
            else
            {
                Console.WriteLine("Todo not found.");
            }
        }
    }
    else
    {
        Console.WriteLine("Add some todos first, then you can view them individually by ID.");
    }
}

static async Task AddNewTodo(TodoService service)
{
    Console.WriteLine("\n--- Add New Todo ---");
    Console.Write("Enter title: ");
    var title = Console.ReadLine() ?? string.Empty;
    
    Console.Write("Enter description: ");
    var description = Console.ReadLine() ?? string.Empty;
    
    Console.Write("Enter due date (yyyy-mm-dd) or press Enter to skip: ");
    var dueDateInput = Console.ReadLine();
    DateTime? dueDate = null;
    
    if (!string.IsNullOrEmpty(dueDateInput) && DateTime.TryParse(dueDateInput, out var parsedDate))
    {
        dueDate = parsedDate;
    }
    
    var newTodo = new TodoItem
    {
        Title = title,
        Description = description,
        DueDate = dueDate
    };
    
    await service.AddAsync(newTodo);
    Console.WriteLine("Todo added successfully!");
}

static async Task UpdateTodo(TodoService service)
{
    Console.WriteLine("\n--- Update Todo ---");
    Console.Write("Enter the ID of the todo to update: ");
    var id = Console.ReadLine() ?? string.Empty;
    
    var existingTodo = await service.GetByIdAsync(id);
    if (existingTodo == null)
    {
        Console.WriteLine("Todo not found.");
        return;
    }
    
    Console.WriteLine($"Current title: {existingTodo.Title}");
    Console.Write("Enter new title (or press Enter to keep current): ");
    var titleInput = Console.ReadLine();
    if (!string.IsNullOrEmpty(titleInput))
    {
        existingTodo.Title = titleInput;
    }
    
    Console.WriteLine($"Current description: {existingTodo.Description}");
    Console.Write("Enter new description (or press Enter to keep current): ");
    var descriptionInput = Console.ReadLine();
    if (!string.IsNullOrEmpty(descriptionInput))
    {
        existingTodo.Description = descriptionInput;
    }
    
    Console.Write("Enter new due date (yyyy-mm-dd) or press Enter to keep current: ");
    var dueDateInput = Console.ReadLine();
    if (!string.IsNullOrEmpty(dueDateInput))
    {
        if (DateTime.TryParse(dueDateInput, out var parsedDate))
        {
            existingTodo.DueDate = parsedDate;
        }
        else if (dueDateInput.ToLower() == "")
        {
            // Keep current value
        }
        else
        {
            Console.WriteLine("Invalid date format. Keeping current due date.");
        }
    }
    
    await service.UpdateAsync(existingTodo);
    Console.WriteLine("Todo updated successfully!");
}

static async Task DeleteTodo(TodoService service)
{
    Console.WriteLine("\n--- Delete Todo ---");
    Console.Write("Enter the ID of the todo to delete: ");
    var id = Console.ReadLine() ?? string.Empty;
    
    var existingTodo = await service.GetByIdAsync(id);
    if (existingTodo == null)
    {
        Console.WriteLine("Todo not found.");
        return;
    }
    
    Console.WriteLine($"Are you sure you want to delete '{existingTodo.Title}'? (y/N): ");
    var confirmation = Console.ReadLine();
    
    if (confirmation?.ToLower() == "y" || confirmation?.ToLower() == "yes")
    {
        var result = await service.DeleteAsync(id);
        if (result)
        {
            Console.WriteLine("Todo deleted successfully!");
        }
        else
        {
            Console.WriteLine("Failed to delete todo.");
        }
    }
    else
    {
        Console.WriteLine("Deletion cancelled.");
    }
}

static async Task ToggleTodoCompletion(TodoService service)
{
    Console.WriteLine("\n--- Toggle Todo Completion ---");
    Console.Write("Enter the ID of the todo to toggle: ");
    var id = Console.ReadLine() ?? string.Empty;
    
    var success = await service.ToggleCompleteAsync(id);
    if (success)
    {
        var updatedTodo = await service.GetByIdAsync(id);
        var status = updatedTodo?.IsCompleted == true ? "completed" : "pending";
        Console.WriteLine($"Todo marked as {status}!");
    }
    else
    {
        Console.WriteLine("Todo not found.");
    }
}