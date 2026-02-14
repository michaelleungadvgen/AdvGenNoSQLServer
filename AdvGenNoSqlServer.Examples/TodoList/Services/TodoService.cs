using AdvGenNoSqlServer.Client;
using AdvGenNoSqlServer.Examples.TodoList.Models;
using System.Text.Json;

namespace AdvGenNoSqlServer.Examples.TodoList.Services
{
    public class TodoService
    {
        private readonly AdvGenNoSqlClient _client;
        private const string CollectionName = "todos";
        private readonly HashSet<string> _knownIds = new HashSet<string>();

        public TodoService(AdvGenNoSqlClient client)
        {
            _client = client;
        }

        public async Task<List<TodoItem>> GetAllAsync()
        {
            var todos = new List<TodoItem>();
            
            // Get all known IDs and fetch each one individually
            // This is a workaround for the lack of a "list all" command in the server
            foreach (var id in _knownIds.ToList()) // Use ToList() to avoid modification during iteration
            {
                var todo = await GetByIdAsync(id);
                if (todo != null)
                {
                    todos.Add(todo);
                }
                else
                {
                    // If the todo doesn't exist anymore, remove from known IDs
                    _knownIds.Remove(id);
                }
            }
            
            return todos;
        }

        public async Task<TodoItem?> GetByIdAsync(string id)
        {
            var result = await _client.GetAsync(CollectionName, id);
            
            if (result != null)
            {
                // Convert dictionary to TodoItem
                var json = JsonSerializer.Serialize(result);
                return JsonSerializer.Deserialize<TodoItem>(json);
            }

            return null;
        }

        public async Task<string> AddAsync(TodoItem item)
        {
            // Ensure the ID is set properly
            if (string.IsNullOrEmpty(item.Id))
            {
                item.Id = Guid.NewGuid().ToString();
            }
            
            var result = await _client.SetAsync(CollectionName, item);
            _knownIds.Add(result); // Track the ID
            return result;
        }

        public async Task<string> UpdateAsync(TodoItem item)
        {
            var result = await _client.SetAsync(CollectionName, item);
            return result;
        }

        public async Task<bool> ToggleCompleteAsync(string id)
        {
            var item = await GetByIdAsync(id);
            if (item != null)
            {
                item.IsCompleted = !item.IsCompleted;
                await UpdateAsync(item);
                return true;
            }
            return false;
        }

        public async Task<bool> DeleteAsync(string id)
        {
            var result = await _client.DeleteAsync(CollectionName, id);
            if (result)
            {
                _knownIds.Remove(id); // Remove from tracking
            }
            return result;
        }
    }
}