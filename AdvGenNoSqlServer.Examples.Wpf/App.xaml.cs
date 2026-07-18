// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.IO;
using System.Windows;
using AdvGenNoSqlServer.Embedded;
using AdvGenNoSqlServer.Examples.Wpf.Models;
using AdvGenNoSqlServer.Examples.Wpf.ViewModels;

namespace AdvGenNoSqlServer.Examples.Wpf;

/// <summary>
/// Application entry point. Opens the embedded database once (an open database file is
/// locked exclusively, so a single shared instance is the intended pattern) and hands
/// the typed todo collection to the main window. The database is disposed on exit,
/// which checkpoints the WAL.
/// </summary>
public partial class App : Application
{
    private AdvGenDatabase? _database;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AdvGenNoSqlServer", "WpfTodoExample");
        Directory.CreateDirectory(dataDir);

        _database = new AdvGenDatabase(Path.Combine(dataDir, "todos.agdb"));
        var todos = _database.GetCollection<TodoItem>("todos");
        todos.EnsureIndex(x => x.IsCompleted);

        var window = new MainWindow { DataContext = new MainViewModel(todos) };
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _database?.Dispose();
        base.OnExit(e);
    }
}
