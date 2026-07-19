# AdvGenNoSQL Embedded - WPF Example Application

WARNING: This example was written as a quick demonstration prototype. Security was NOT a
focus — DO NOT store any sensitive information (passwords, API keys, personal data,
payment details, etc.) in it.

WPF desktop application demonstrating how to embed the AdvGenNoSQL database engine directly
in a .NET application — no server, no network, one database file.

## Features Demonstrated

### 1. Typed Collections (Todos tab)
- `db.GetCollection<TodoItem>("todos")` — LiteDB-style POCO API
- Insert with automatic id assignment (id written back onto the entity)
- Update on grid edits (checkbox toggle, title edit) via `INotifyPropertyChanged`
- `DeleteAsync` by id and `DeleteManyAsync` with a predicate (Clear completed)
- `EnsureIndex` on a member expression

### 2. Fluent Queries & Diagnostics (Todos tab)
- `Query().Where(...).OrderByDescending(...).ToListAsync()` for the All/Active/Completed
  filters and the title search
- `db.Diagnostics.FallbackQueryCount` in the status bar — the search uses
  `string.Contains`, which the engine intentionally evaluates in memory; the counter shows
  the fallback happening live as you type

### 3. Untyped Document API (Notes tab)
- `db.GetCollection("notes")` — schema-less `Document` records (`Id` + `Dictionary` payload)
- `InsertAsync` / `FindAsync()` (no filter = list all) / `DeleteAsync` / `CountAsync`
- Grid columns bind straight into the payload dictionary (`Data[title]`)

### 4. Database Lifecycle
- Single `AdvGenDatabase` opened in `App.OnStartup`, disposed (WAL checkpoint) in `OnExit`
- Exclusive file lock handling: a second instance shows a friendly message instead of crashing
- **Checkpoint** button flushes the write-ahead log into the main database file
- Typed and untyped collections share the same database file

## Running

```powershell
cd "E:\Projects\AdvGenNoSQLServer"
dotnet run --project AdvGenNoSqlServer.Examples.Wpf -c Release
```

Or run the built executable directly:
`AdvGenNoSqlServer.Examples.Wpf\bin\Release\net9.0-windows\AdvGenNoSqlServer.Examples.Wpf.exe`

## Data Location

`%LocalAppData%\AdvGenNoSqlServer\WpfTodoExample\todos.agdb` (plus `todos.agdb.wal` while the
app is running). Add items, close the app, relaunch — everything is still there. Delete the
folder to start over.

## Project Layout

| File | Purpose |
|------|---------|
| `App.xaml.cs` | Opens/disposes the database, lock handling |
| `Models/TodoItem.cs` | POCO entity (only requirement: public `string Id`) |
| `ViewModels/MainViewModel.cs` | Todos tab — typed API, fluent queries, diagnostics |
| `ViewModels/NotesViewModel.cs` | Notes tab — untyped document API |
| `ViewModels/RelayCommand.cs` | Minimal `ICommand` (no external MVVM package) |
