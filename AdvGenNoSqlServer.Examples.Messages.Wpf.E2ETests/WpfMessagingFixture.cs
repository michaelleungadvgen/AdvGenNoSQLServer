// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace AdvGenNoSqlServer.Examples.Messages.Wpf.E2ETests;

/// <summary>
/// Self-hosting E2E fixture for the WPF cache-mode messaging client. Builds and launches
/// the AdvGenNoSQL Host in StorageMode=Cache and the WPF app on isolated ports, then drives
/// the WPF UI through Windows UI Automation (the WPF analogue of the Playwright suites).
/// Requires an interactive Windows desktop session; not runnable in Linux CI.
/// </summary>
public sealed class WpfMessagingFixture : IAsyncLifetime
{
    public const int TcpPort = 19991;
    private const string WindowTitle = "AdvGenNoSQL Cache-Mode Messages";

    private Process? _host;
    private Process? _wpf;
    private string _tempDir = "";

    public AutomationElement MainWindow { get; private set; } = null!;
    public string StorageDir => Path.Combine(_tempDir, "data");

    public async Task InitializeAsync()
    {
        var root = FindRepoRoot();
        _tempDir = Path.Combine(Path.GetTempPath(), "wpfmsg-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(StorageDir);

        var hostOut = Path.Combine(_tempDir, "host");
        var wpfOut = Path.Combine(_tempDir, "wpf");
        await RunDotnetAsync($"build \"{Path.Combine(root, "AdvGenNoSqlServer.Host")}\" -c Release --nologo -v q -o \"{hostOut}\"", root);
        await RunDotnetAsync($"build \"{Path.Combine(root, "AdvGenNoSqlServer.Examples.Messages.Wpf")}\" -c Release --nologo -v q -o \"{wpfOut}\"", root);

        _host = Launch(Path.Combine(hostOut, "AdvGenNoSqlServer.Host.dll"), Path.Combine(root, "AdvGenNoSqlServer.Host"), new Dictionary<string, string>
        {
            ["DOTNET_ENVIRONMENT"] = "Development",
            ["NOSQL_STORAGE_MODE"] = "Cache",
            ["NOSQL_PORT"] = TcpPort.ToString(),
            ["NOSQL_ADMIN_HTTP_PORT"] = (TcpPort + 1).ToString(),
            ["NOSQL_ADMIN_HTTP_USE_HTTPS"] = "false",
            ["NOSQL_MASTER_PASSWORD"] = "admin123",
            ["NOSQL_REQUIRE_AUTHENTICATION"] = "true",
            ["NOSQL_ENABLE_SSL"] = "false",
            ["NOSQL_STORAGE_PATH"] = StorageDir,
            ["NOSQL_PBKDF2_ITERATIONS"] = "10000"
        });
        await WaitTcpAsync(TcpPort, TimeSpan.FromSeconds(60));

        var wpfExe = Path.Combine(wpfOut, "AdvGenNoSqlServer.Examples.Messages.Wpf.exe");
        _wpf = Process.Start(new ProcessStartInfo(wpfExe) { UseShellExecute = true })!;
        MainWindow = await WaitForWindowAsync(WindowTitle, TimeSpan.FromSeconds(30));
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (MainWindow != null && MainWindow.TryGetCurrentPattern(WindowPattern.Pattern, out var pattern))
                ((WindowPattern)pattern).Close();
        }
        catch { /* window may already be gone */ }

        await Task.Delay(500);
        Kill(_wpf);
        Kill(_host);
        try { Directory.Delete(_tempDir, true); } catch (IOException) { }
    }

    // ---------- UI Automation helpers ----------

    public AutomationElement[] GetTextBoxes()
        => MainWindow.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit))
            .Cast<AutomationElement>().ToArray();

    public void SetTextBox(int index, string value)
    {
        var box = GetTextBoxes()[index];
        ((ValuePattern)box.GetCurrentPattern(ValuePattern.Pattern)).SetValue(value);
    }

    public void ClickButton(string name)
    {
        var button = MainWindow.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, name))
            ?? throw new InvalidOperationException($"Button '{name}' not found.");
        ((InvokePattern)button.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
    }

    public bool TextExists(string text)
    {
        var texts = MainWindow.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
        foreach (AutomationElement t in texts)
        {
            if (t.Current.Name.Contains(text))
                return true;
        }
        return false;
    }

    public string GetStatusText()
    {
        var texts = MainWindow.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
        foreach (AutomationElement t in texts)
        {
            var name = t.Current.Name;
            if (name.Contains("Connected to") || name.Contains("Connect failed") || name.Contains("message(s)"))
                return name;
        }
        return "";
    }

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>Brings the WPF window to the foreground so bindings/commands requery reliably.</summary>
    public void FocusWindow()
    {
        var hwnd = new IntPtr(MainWindow.Current.NativeWindowHandle);
        ShowWindow(hwnd, 9); // SW_RESTORE
        SetForegroundWindow(hwnd);
        try { MainWindow.SetFocus(); } catch { /* best effort */ }
    }

    /// <summary>Points the client at the fixture's cache-mode server and connects.</summary>
    public async Task ConnectAsync()
    {
        FocusWindow();
        var target = $"localhost:{TcpPort}";

        // Idempotent: if already connected to the target, do not reconnect (repeated
        // reconnects race with the 2s poll timer and destabilize the client).
        var current = GetStatusText();
        if (current.Contains(target) && (current.Contains("Connected to") || current.Contains("message(s) in server memory")))
            return;

        SetTextBox(1, TcpPort.ToString()); // Port textbox
        ClickButton("Connect");
        // Wait until the status reflects THIS server (localhost:TcpPort). The app may
        // auto-connect to another server on load, so a generic "connected" status is not
        // enough - we must see the target host:port to know the reconnect completed.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var status = GetStatusText();
            if (status.Contains(target) && (status.Contains("Connected to") || status.Contains("message(s) in server memory")))
            {
                await Task.Delay(800); // let the reconnect / first refresh settle
                return;
            }
            await Task.Delay(250);
        }
        throw new TimeoutException($"WPF client did not connect to {target}: {GetStatusText()}");
    }

    public async Task WaitForTextAsync(string text, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (TextExists(text)) return;
            await Task.Delay(200);
        }
        throw new TimeoutException($"Text '{text}' did not appear in the WPF window within {timeout}.");
    }

    public async Task WaitForTextGoneAsync(string text, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!TextExists(text)) return;
            await Task.Delay(200);
        }
        throw new TimeoutException($"Text '{text}' did not disappear from the WPF window within {timeout}.");
    }

    /// <summary>Types a message into the NewMessage box and clicks Send.</summary>
    public async Task SendMessageAsync(string text)
    {
        FocusWindow();
        SetTextBox(5, text); // NewMessage textbox
        await Task.Delay(600);

        var send = MainWindow.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Send"))
            ?? throw new InvalidOperationException("Send button not found.");
        var enableDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!send.Current.IsEnabled && DateTime.UtcNow < enableDeadline)
            await Task.Delay(200);

        // SendAsync clears NewMessage on success, so an empty input confirms the send ran.
        // Retry the click if the input still holds the text.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            FocusWindow();
            ((InvokePattern)send.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            await Task.Delay(500);
            var value = ((ValuePattern)GetTextBoxes()[5].GetCurrentPattern(ValuePattern.Pattern)).Current.Value;
            if (string.IsNullOrEmpty(value))
                return;
        }
        throw new TimeoutException($"Send did not clear the input; message '{text}' may not have been sent.");
    }

    // ---------- process helpers ----------

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AdvGenNoSqlServer.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }

    private static async Task RunDotnetAsync(string arguments, string workingDir)
    {
        var psi = new ProcessStartInfo("dotnet", arguments)
        {
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var process = Process.Start(psi)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"dotnet {arguments} failed ({process.ExitCode}): {stdout} {stderr}");
    }

    private static Process Launch(string dll, string workingDir, Dictionary<string, string> env)
    {
        var psi = new ProcessStartInfo("dotnet", $"\"{dll}\"")
        {
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var (key, value) in env)
            psi.Environment[key] = value;
        return Process.Start(psi)!;
    }

    private static async Task WaitTcpAsync(int port, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync("localhost", port);
                return;
            }
            catch (SocketException)
            {
                await Task.Delay(250);
            }
        }
        throw new TimeoutException($"Host did not open TCP port {port} within {timeout}.");
    }

    private static async Task<AutomationElement> WaitForWindowAsync(string titlePrefix, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var condition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window);
        while (DateTime.UtcNow < deadline)
        {
            foreach (AutomationElement window in AutomationElement.RootElement.FindAll(TreeScope.Children, condition))
            {
                if (window.Current.Name.StartsWith(titlePrefix))
                    return window;
            }
            await Task.Delay(300);
        }
        throw new TimeoutException($"WPF window '{titlePrefix}' did not appear within {timeout}.");
    }

    private static void Kill(Process? process)
    {
        try
        {
            if (process is { HasExited: false })
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
    }
}