@echo off
setlocal
rem Copyright (c) 2026 AdvanGeneration Pty. Ltd.
rem Licensed under the MIT License.
rem See LICENSE.txt for license information.

rem Runs the AdvGenNoSQL cache-mode messaging demo end to end:
rem   Host : TCP 9191, Development, StorageMode=Cache (memory-only, no persistence)
rem   App  : console message board (interactive) connected over TCP
rem Messages live only in server memory and are lost when the Host window closes.
rem Close the two spawned windows to stop the demo.

title AdvGenNoSQL Cache Messaging Demo
cd /d "%~dp0"

echo ============================================================
echo  AdvGenNoSQL Cache-Mode Messaging Demo
echo  Host : TCP 9191 (StorageMode=Cache, memory-only)
echo  App  : console message board (type to send, /help)
echo ============================================================
echo.

rem --- Build once so startup is fast and errors surface early ---
echo Building Messages example...
dotnet build AdvGenNoSqlServer.Examples.Messages\AdvGenNoSqlServer.Examples.Messages.csproj -c Release --nologo -v q
if errorlevel 1 goto :build_failed
echo Building Host...
dotnet build AdvGenNoSqlServer.Host\AdvGenNoSqlServer.Host.csproj -c Release --nologo -v q
if errorlevel 1 goto :build_failed

rem --- Start the server in CACHE mode in its own window ---
start "AdvGenNoSQL Host (Cache)" cmd /k "set DOTNET_ENVIRONMENT=Development&& set NOSQL_STORAGE_MODE=Cache&& set NOSQL_MASTER_PASSWORD=admin123&& set NOSQL_ENABLE_SSL=false&& set NOSQL_REQUIRE_AUTHENTICATION=true&& dotnet run --project AdvGenNoSqlServer.Host -c Release --no-build"

echo Waiting for the server on TCP port 9191...
powershell -NoProfile -Command "$ok=$false; for($i=0;$i -lt 60;$i++){ try { $c=New-Object Net.Sockets.TcpClient('localhost',9191); $c.Close(); $ok=$true; break } catch { Start-Sleep -Milliseconds 500 } }; if(-not $ok){ exit 1 }"
if errorlevel 1 goto :host_failed

rem --- Start the interactive message board in its own window ---
start "AdvGenNoSQL Messages" cmd /k "dotnet run --project AdvGenNoSqlServer.Examples.Messages -c Release --no-build"

echo.
echo Demo is running. Use the "AdvGenNoSQL Messages" window:
echo   type a message and press Enter to send, /list, /count, /stats, /quit
echo Messages are stored ONLY in server memory (cache mode) - closing the
echo "AdvGenNoSQL Host (Cache)" window discards them. Nothing is written to disk.
echo.
echo For a scripted tour instead, run:
echo   dotnet run --project AdvGenNoSqlServer.Examples.Messages -c Release -- --demo
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