// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.Specialized;
using System.Windows;
using System.Windows.Threading;

namespace AdvGenNoSqlServer.Examples.Messages.Wpf;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += (_, _) => _viewModel.ConnectCommand.Execute(null);
        Closed += async (_, _) => await _viewModel.DisposeAsync();

        // Keep the newest message in view (chat-style auto-scroll).
        _viewModel.Messages.CollectionChanged += OnMessagesChanged;
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_viewModel.Messages.Count == 0) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            var last = _viewModel.Messages[^1];
            MessagesList.ScrollIntoView(last);
        });
    }
}