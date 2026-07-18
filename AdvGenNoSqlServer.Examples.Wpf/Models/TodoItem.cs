// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AdvGenNoSqlServer.Examples.Wpf.Models;

/// <summary>
/// Todo entity stored in the embedded database. The typed mapper only requires a public
/// string <see cref="Id"/> property (assigned automatically on insert when empty); every
/// other public property round-trips through System.Text.Json. INotifyPropertyChanged
/// lets the DataGrid push edits straight into the entity so the view model can persist
/// them; the event itself is not serialized.
/// </summary>
public class TodoItem : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private bool _isCompleted;

    public string Id { get; set; } = string.Empty;

    public string Title
    {
        get => _title;
        set
        {
            if (_title == value) return;
            _title = value;
            OnPropertyChanged();
        }
    }

    public bool IsCompleted
    {
        get => _isCompleted;
        set
        {
            if (_isCompleted == value) return;
            _isCompleted = value;
            OnPropertyChanged();
        }
    }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
