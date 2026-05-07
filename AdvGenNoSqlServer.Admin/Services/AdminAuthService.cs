// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Admin.Services;

using System.Net.Http.Json;

/// <summary>
/// Service for managing admin authentication state.
/// </summary>
public class AdminAuthService
{
    private readonly ServerConnectionService _connectionService;
    private string? _currentUser;
    private bool _isAuthenticated;

    public AdminAuthService(ServerConnectionService connectionService)
    {
        _connectionService = connectionService;
    }

    /// <summary>
    /// Event raised when authentication state changes.
    /// </summary>
    public event EventHandler? AuthenticationStateChanged;

    /// <summary>
    /// Gets whether the user is currently authenticated.
    /// </summary>
    public bool IsAuthenticated => _isAuthenticated;

    /// <summary>
    /// Gets the current username.
    /// </summary>
    public string CurrentUser => _currentUser ?? "Anonymous";

    /// <summary>
    /// Logs in the user with the specified credentials.
    /// </summary>
    public async Task<bool> LoginAsync(string username, string password, string serverUrl)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return false;
        }

        var apiUrl = DeriveApiUrl(serverUrl);
        using var http = new HttpClient();

        try
        {
            Console.WriteLine($"[Login] POST {apiUrl}/api/auth/login");
            var response = await http.PostAsJsonAsync($"{apiUrl}/api/auth/login", new { username, password });

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
                if (result?.Success == true && !string.IsNullOrEmpty(result.Token))
                {
                    _currentUser = username;
                    _isAuthenticated = true;
                    _connectionService.AuthToken = result.Token;
                    AuthenticationStateChanged?.Invoke(this, EventArgs.Empty);
                    Console.WriteLine($"[Login] Login successful");
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Login] Failed: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Logs out the current user.
    /// </summary>
    public void Logout()
    {
        _currentUser = null;
        _isAuthenticated = false;
        _connectionService.AuthToken = null;
        
        AuthenticationStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string DeriveApiUrl(string serverUrl)
    {
        var url = serverUrl.Replace("http://", "").Replace("https://", "").TrimEnd('/');
        var lastColon = url.LastIndexOf(':');
        if (lastColon >= 0 && int.TryParse(url[(lastColon + 1)..], out var port))
            return $"https://{url[..lastColon]}:{port + 1}";
        return $"https://{url}";
    }

    private record LoginResponse(bool Success, string? Token, string? Username, DateTime? ExpiresAt);
}
