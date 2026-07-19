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

        try
        {
            _database = new AdvGenDatabase(Path.Combine(dataDir, "todos.agdb"));
            _database.GetCollection<TodoItem>("todos").EnsureIndex(x => x.IsCompleted);
        }
        catch (EmbeddedDatabaseLockedException)
        {
            // An open database file is locked exclusively - a second instance cannot open it.
            MessageBox.Show(
                "The database file is locked by another process. Is another instance of the app already running?",
                "Database locked", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1);
            return;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open the database:\n{ex.Message}",
                "Startup error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var window = new MainWindow { DataContext = new MainViewModel(_database) };
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _database?.Dispose();
        base.OnExit(e);
    }
}
