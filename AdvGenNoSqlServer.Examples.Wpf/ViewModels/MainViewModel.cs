// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using AdvGenNoSqlServer.Embedded.Typed;
using AdvGenNoSqlServer.Examples.Wpf.Models;

namespace AdvGenNoSqlServer.Examples.Wpf.ViewModels;

/// <summary>
/// Drives the todo window against a typed embedded collection. All database work goes
/// through the async API variants so the UI thread stays responsive; the sync members
/// (<see cref="IEmbeddedCollection{T}.Count()"/>, DeleteMany) are documented safe
/// blocking wrappers and are used only for micro-operations.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private enum Filter { All, Active, Completed }

    private readonly IEmbeddedCollection<TodoItem> _todos;
    private Filter _filter = Filter.All;
    private string _newTitle = string.Empty;
    private string _searchText = string.Empty;
    private TodoItem? _selectedItem;
    private string _statusText = string.Empty;

    public MainViewModel(IEmbeddedCollection<TodoItem> todos)
    {
        _todos = todos;

        AddCommand = new RelayCommand(AddAsync, () => !string.IsNullOrWhiteSpace(NewTitle));
        DeleteCommand = new RelayCommand(DeleteAsync, () => SelectedItem is not null);
        ClearCompletedCommand = new RelayCommand(ClearCompletedAsync);
        RefreshCommand = new RelayCommand(LoadAsync);

        _ = LoadAsync();
    }

    /// <summary>The currently visible items, matching the active filter and search text.</summary>
    public ObservableCollection<TodoItem> Items { get; } = new();

    public string NewTitle
    {
        get => _newTitle;
        set { _newTitle = value; OnPropertyChanged(); }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value) return;
            _searchText = value;
            OnPropertyChanged();
            _ = LoadAsync();
        }
    }

    public bool FilterAll
    {
        get => _filter == Filter.All;
        set { if (value) SetFilter(Filter.All); }
    }

    public bool FilterActive
    {
        get => _filter == Filter.Active;
        set { if (value) SetFilter(Filter.Active); }
    }

    public bool FilterCompleted
    {
        get => _filter == Filter.Completed;
        set { if (value) SetFilter(Filter.Completed); }
    }

    public TodoItem? SelectedItem
    {
        get => _selectedItem;
        set { _selectedItem = value; OnPropertyChanged(); }
    }

    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; OnPropertyChanged(); }
    }

    public ICommand AddCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand ClearCompletedCommand { get; }
    public ICommand RefreshCommand { get; }

    /// <summary>Reloads the visible items using a fluent query against the collection.</summary>
    private async Task LoadAsync()
    {
        try
        {
            var query = _todos.Query();

            if (_filter == Filter.Active)
                query = query.Where(x => !x.IsCompleted);
            else if (_filter == Filter.Completed)
                query = query.Where(x => x.IsCompleted);

            var search = SearchText.Trim();
            if (search.Length > 0)
                query = query.Where(x => x.Title.Contains(search));

            var items = await query.OrderByDescending(x => x.CreatedAt).ToListAsync();

            foreach (var item in Items)
                item.PropertyChanged -= OnItemPropertyChanged;
            Items.Clear();
            foreach (var item in items)
            {
                item.PropertyChanged += OnItemPropertyChanged;
                Items.Add(item);
            }

            UpdateStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Failed to load items", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task AddAsync()
    {
        var title = NewTitle.Trim();
        if (title.Length == 0) return;

        // Id is left empty: InsertAsync assigns it and writes it back onto the entity.
        var item = new TodoItem { Title = title, CreatedAt = DateTime.Now };
        await _todos.InsertAsync(item);

        NewTitle = string.Empty;
        await LoadAsync();
    }

    private async Task DeleteAsync()
    {
        if (SelectedItem is null) return;
        await _todos.DeleteAsync(SelectedItem.Id);
        await LoadAsync();
    }

    private async Task ClearCompletedAsync()
    {
        _todos.DeleteMany(x => x.IsCompleted);
        await LoadAsync();
    }

    /// <summary>Persists edits pushed from the DataGrid (checkbox toggles, title edits).</summary>
    private async void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not TodoItem item) return;
        if (e.PropertyName is not (nameof(TodoItem.IsCompleted) or nameof(TodoItem.Title))) return;

        try
        {
            await _todos.UpdateAsync(item);
            UpdateStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Failed to save change", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetFilter(Filter filter)
    {
        if (_filter == filter) return;
        _filter = filter;
        OnPropertyChanged(nameof(FilterAll));
        OnPropertyChanged(nameof(FilterActive));
        OnPropertyChanged(nameof(FilterCompleted));
        _ = LoadAsync();
    }

    private void UpdateStatus()
    {
        var total = _todos.Count();
        var active = _todos.Count(x => !x.IsCompleted);
        StatusText = $"{total} item(s) stored, {active} active, {total - active} completed";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
