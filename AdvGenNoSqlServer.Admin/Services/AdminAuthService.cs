// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Admin.Services;

using System;
using System.Threading.Tasks;

/// <summary>
/// Service for managing admin authentication state.
/// </summary>
public class AdminAuthService
{
    private string? _authToken;
    private string? _currentUser;
    private bool _isAuthenticated;

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
    /// Gets the authentication token.
    /// </summary>
    public string? AuthToken => _authToken;

    /// <summary>
    /// Logs in the user with the specified credentials.
    /// </summary>
    public Task<bool> LoginAsync(string username, string password, string serverUrl)
    {
        // In a real implementation, this would call the server API
        // For now, we'll simulate a successful login
        if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
        {
            _currentUser = username;
            _authToken = $"simulated_token_{Guid.NewGuid()}";
            _isAuthenticated = true;
            
            AuthenticationStateChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(true);
        }
        
        return Task.FromResult(false);
    }

    /// <summary>
    /// Logs out the current user.
    /// </summary>
    public void Logout()
    {
        _authToken = null;
        _currentUser = null;
        _isAuthenticated = false;
        
        AuthenticationStateChanged?.Invoke(this, EventArgs.Empty);
    }
}
