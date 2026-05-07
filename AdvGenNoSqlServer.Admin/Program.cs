// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using AdvGenNoSqlServer.Admin;
using AdvGenNoSqlServer.Admin.Services;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Add MudBlazor services
builder.Services.AddMudServices();

// Configure HttpClient for API calls
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Add custom services — ServerConnectionService must be singleton so connection state persists across pages
builder.Services.AddSingleton<ServerConnectionService>(_ => new ServerConnectionService(new HttpClient()));
builder.Services.AddSingleton<AdminAuthService>();
builder.Services.AddSingleton<INoSqlServerClient>(sp => sp.GetRequiredService<ServerConnectionService>());
builder.Services.AddSingleton<NotificationService>();
builder.Services.AddScoped<LocalStorageService>();

await builder.Build().RunAsync();
