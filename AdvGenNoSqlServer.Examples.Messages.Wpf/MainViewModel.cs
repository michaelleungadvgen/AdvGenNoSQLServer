// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using AdvGenNoSqlServer.Examples.Messages;

namespace AdvGenNoSqlServer.Examples.Messages.Wpf;

/// <summary>
/// Drives the WPF cache-mode message board. Connects to the AdvGenNoSQL server over TCP
/// (expected to run in StorageMode=Cache), sends messages and polls for new ones.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private MessageService? _service;
    private readonly DispatcherTimer _timer;
    private bool _polling;
    private bool _refreshing;

    private string _host = "localhost";
    private int _port = 9191;
    private string _username = "admin";
    private string _password = "admin123";
    private string _nick = "wpf-user";
    private string _newMessage = string.Empty;
    private string _statusText = "Not connected. Start the server in cache mode, then Connect.";
    private bool _isConnected;

    public MainViewModel()
    {
        Messages = new ObservableCollection<Message>();
        ConnectCommand = new RelayCommand(async () => await ConnectAsync());
        SendCommand = new RelayCommand(async () => await SendAsync(), () => IsConnected);
        ClearCommand = new RelayCommand(async () => await ClearAsync(), () => IsConnected);
        RefreshCommand = new RelayCommand(async () => await RefreshAsync(), () => IsConnected);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += async (_, _) => await PollAsync();
    }

    public ObservableCollection<Message> Messages { get; }

    public ICommand ConnectCommand { get; }
    public ICommand SendCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand RefreshCommand { get; }

    public string Host { get => _host; set { _host = value; OnPropertyChanged(); } }
    public int Port { get => _port; set { _port = value; OnPropertyChanged(); } }
    public string Username { get => _username; set { _username = value; OnPropertyChanged(); } }
    public string Password { get => _password; set { _password = value; OnPropertyChanged(); } }
    public string Nick { get => _nick; set { _nick = value; OnPropertyChanged(); } }
    public string NewMessage { get => _newMessage; set { _newMessage = value; OnPropertyChanged(); } }
    public string StatusText { get => _statusText; private set { _statusText = value; OnPropertyChanged(); } }
    public bool IsConnected { get => _isConnected; private set { _isConnected = value; OnPropertyChanged(); } }

    private async Task ConnectAsync()
    {
        try
        {
            _timer.Stop();
            if (_service != null)
            {
                await _service.DisposeAsync();
                _service = null;
            }

            var service = new MessageService(Host, Port, Username, Password, useSsl: false, collection: "messages");
            await service.ConnectAsync();
            _service = service;
            IsConnected = true;
            StatusText = $"Connected to {service.ConnectionDescription} (cache mode - messages live in server memory).";
            await RefreshAsync();
            _timer.Start();
        }
        catch (Exception ex)
        {
            IsConnected = false;
            StatusText = $"Connect failed: {ex.Message}";
        }
    }

    private async Task PollAsync()
    {
        if (_polling || !IsConnected) return;
        _polling = true;
        try
        {
            await RefreshAsync();
        }
        finally
        {
            _polling = false;
        }
    }

    private async Task RefreshAsync()
    {
        if (_service == null || _refreshing) return;
        _refreshing = true;
        try
        {
            var messages = await _service.ListAsync();
            Messages.Clear();
            foreach (var m in messages)
                Messages.Add(m);
            StatusText = $"{messages.Count} message(s) in server memory - { _service.ConnectionDescription } (cache mode).";
        }
        catch (Exception ex)
        {
            StatusText = $"Refresh failed: {ex.Message}";
        }
        finally
        {
            _refreshing = false;
        }
    }

    private async Task SendAsync()
    {
        if (_service == null) return;
        var text = NewMessage.Trim();
        if (text.Length == 0) return;
        try
        {
            var nick = string.IsNullOrWhiteSpace(Nick) ? Username : Nick.Trim();
            await _service.SendAsync(nick, text);
            NewMessage = string.Empty;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Send failed: {ex.Message}";
        }
    }

    private async Task ClearAsync()
    {
        if (_service == null) return;
        try
        {
            await _service.ClearAsync();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Clear failed: {ex.Message}";
        }
    }

    public async ValueTask DisposeAsync()
    {
        _timer.Stop();
        if (_service != null)
        {
            await _service.DisposeAsync();
            _service = null;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}