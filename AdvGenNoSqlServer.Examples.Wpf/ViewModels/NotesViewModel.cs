// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Embedded;

namespace AdvGenNoSqlServer.Examples.Wpf.ViewModels;

/// <summary>
/// Drives the untyped-API (notes) tab. Demonstrates the document-oriented side of the
/// embedded engine: schema-less <see cref="Document"/> records with a string id and a
/// dictionary payload, stored in the same database file as the typed collections.
/// </summary>
public sealed class NotesViewModel : INotifyPropertyChanged
{
    private readonly EmbeddedCollection _notes;
    private string _newTitle = string.Empty;
    private string _newBody = string.Empty;
    private Document? _selectedItem;
    private string _statusText = string.Empty;

    public NotesViewModel(AdvGenDatabase db)
    {
        _notes = db.GetCollection("notes");

        AddCommand = new RelayCommand(AddAsync, () => !string.IsNullOrWhiteSpace(NewTitle));
        DeleteCommand = new RelayCommand(DeleteAsync, () => SelectedItem is not null);
        RefreshCommand = new RelayCommand(LoadAsync);

        _ = LoadAsync();
    }

    public ObservableCollection<Document> Items { get; } = new();

    public string NewTitle
    {
        get => _newTitle;
        set { _newTitle = value; OnPropertyChanged(); }
    }

    public string NewBody
    {
        get => _newBody;
        set { _newBody = value; OnPropertyChanged(); }
    }

    public Document? SelectedItem
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
    public ICommand RefreshCommand { get; }

    private async Task LoadAsync()
    {
        try
        {
            // FindAsync with no filter returns every document in the collection.
            var docs = await _notes.FindAsync();

            Items.Clear();
            foreach (var doc in docs.OrderByDescending(d => d.CreatedAt))
                Items.Add(doc);

            StatusText = $"{await _notes.CountAsync()} note(s) stored";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Failed to load notes", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task AddAsync()
    {
        var title = NewTitle.Trim();
        if (title.Length == 0) return;

        // Untyped documents need an explicit id; the payload is a plain dictionary.
        var doc = new Document
        {
            Id = Guid.NewGuid().ToString(),
            Data = new Dictionary<string, object>
            {
                ["title"] = title,
                ["body"] = NewBody.Trim(),
                ["createdAt"] = DateTime.Now.ToString("g"),
            },
        };
        await _notes.InsertAsync(doc);

        NewTitle = string.Empty;
        NewBody = string.Empty;
        await LoadAsync();
    }

    private async Task DeleteAsync()
    {
        if (SelectedItem is null) return;
        await _notes.DeleteAsync(SelectedItem.Id);
        await LoadAsync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
