// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using MudBlazor.Services;
using AdvGenNoSqlServer.Examples.Messages;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddMudServices();

// One MessageService (and thus one TCP connection) per Blazor circuit / browser tab.
builder.Services.AddScoped<MessageService>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var host = config["NoSql:Host"] ?? "localhost";
    var port = int.TryParse(config["NoSql:Port"], out var p) ? p : 9191;
    var username = config["NoSql:Username"] ?? "admin";
    var password = config["NoSql:Password"] ?? "admin123";
    var useSsl = bool.TryParse(config["NoSql:UseSsl"], out var s) && s;
    var collection = config["NoSql:Collection"] ?? "messages";
    return new MessageService(host, port, username, password, useSsl, collection);
});

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();