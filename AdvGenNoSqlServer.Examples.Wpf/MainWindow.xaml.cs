// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Windows;

namespace AdvGenNoSqlServer.Examples.Wpf;

/// <summary>
/// Main window. All logic lives in <see cref="ViewModels.MainViewModel"/>; the window
/// only exists to host the view.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
