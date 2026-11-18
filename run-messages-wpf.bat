@echo off
setlocal
rem Copyright (c) 2026 AdvanGeneration Pty. Ltd.
rem Licensed under the MIT License.
rem See LICENSE.txt for license information.

rem Runs the AdvGenNoSQL cache-mode messaging WPF demo end to end:
rem   Host : TCP 9191, Development, StorageMode=Cache (memory-only)
rem   App  : WPF message board (auto-connects on startup)
rem Messages live only in server memory and are lost when the Host window closes.
rem Close the spawned windows to stop the demo.

title AdvGenNoSQL Cache Messaging WPF Demo
cd /d "%~dp0"

echo ============================================================
echo  AdvGenNoSQL Cache-Mode Messaging - WPF Client
echo  Host : TCP 9191 (StorageMode=Cache, memory-only)
echo  App  : WPF message board (auto-connect)
echo ============================================================
echo.

rem --- Build once so startup is fast and errors surface early ---
echo Building WPF client...
dotnet build AdvGenNoSqlServer.Examples.Messages.Wpf\AdvGenNoSqlServer.Examples.Messages.Wpf.csproj -c Release --nologo -v q
if errorlevel 1 goto :build_failed
echo Building Host...
dotnet build AdvGenNoSqlServer.Host\AdvGenNoSqlServer.Host.csproj -c Release --nologo -v q
if errorlevel 1 goto :build_failed

rem --- Start the server in CACHE mode in its own window ---
start "AdvGenNoSQL Host (Cache)" cmd /k "set DOTNET_ENVIRONMENT=Development&& set NOSQL_STORAGE_MODE=Cache&& set NOSQL_MASTER_PASSWORD=admin123&& set NOSQL_ENABLE_SSL=false&& set NOSQL_REQUIRE_AUTHENTICATION=true&& dotnet run --project AdvGenNoSqlServer.Host -c Release --no-build"

echo Waiting for the server on TCP port 9191...
powershell -NoProfile -Command "$ok=$false; for($i=0;$i -lt 60;$i++){ try { $c=New-Object Net.Sockets.TcpClient('localhost',9191); $c.Close(); $ok=$true; break } catch { Start-Sleep -Milliseconds 500 } }; if(-not $ok){ exit 1 }"
if errorlevel 1 goto :host_failed

rem --- Start the WPF client (auto-connects to localhost:9191) ---
start "AdvGenNoSQL Messages WPF" cmd /k "dotnet run --project AdvGenNoSqlServer.Examples.Messages.Wpf -c Release --no-build"

echo.
echo WPF demo launched. The client auto-connects to the cache-mode server.
echo Type a message and press Send (or Enter). Messages are stored ONLY in server
echo memory (cache mode) - closing the Host window discards them.
echo.
pause
exit /b 0

:build_failed
echo.
echo Build failed. Fix the errors above and run this script again.
pause
exit /b 1

:host_failed
echo.
echo The server did not open port 9191 within 30s.
echo Check the "AdvGenNoSQL Host (Cache)" window (port 9191 may already be in use -
echo close any previous Host window and run this script again).
pause
exit /b 1