// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Admin.Services;

using Microsoft.JSInterop;

/// <summary>
/// Service for accessing browser localStorage.
/// </summary>
public class LocalStorageService
{
    private readonly IJSRuntime _jsRuntime;

    public LocalStorageService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    /// <summary>
    /// Gets an item from localStorage.
    /// </summary>
    public async Task<string?> GetItemAsync(string key)
    {
        try
        {
            return await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", key);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Sets an item in localStorage.
    /// </summary>
    public async Task SetItemAsync(string key, string value)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", key, value);
        }
        catch
        {
            // Ignore errors (e.g., if localStorage is not available)
        }
    }

    /// <summary>
    /// Removes an item from localStorage.
    /// </summary>
    public async Task RemoveItemAsync(string key)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", key);
        }
        catch
        {
            // Ignore errors
        }
    }

    /// <summary>
    /// Clears all items from localStorage.
    /// </summary>
    public async Task ClearAsync()
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.clear");
        }
        catch
        {
            // Ignore errors
        }
    }
}
