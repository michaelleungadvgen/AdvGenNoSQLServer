@echo off
setlocal
rem Copyright (c) 2026 AdvanGeneration Pty. Ltd.
rem Licensed under the MIT License.
rem See LICENSE.txt for license information.

rem Runs the AdvGenNoSQL cache-mode messaging WEB demo end to end:
rem   Host    : TCP 9191, Development, StorageMode=Cache (memory-only)
rem   Web UI  : Blazor Server message board on http://localhost:5290
rem Messages live only in server memory and are lost when the Host window closes.
rem Close the two spawned windows to stop the demo.

title AdvGenNoSQL Cache Messaging Web Demo
cd /d "%~dp0"

echo ============================================================
echo  AdvGenNoSQL Cache-Mode Messaging - Web UI
echo  Host   : TCP 9191 (StorageMode=Cache, memory-only)
echo  Web UI : http://localhost:5290
echo ============================================================
echo.

rem --- Build once so startup is fast and errors surface early ---
echo Building Web UI...
dotnet build AdvGenNoSqlServer.Examples.Messages.Web\AdvGenNoSqlServer.Examples.Messages.Web.csproj -c Release --nologo -v q
if errorlevel 1 goto :build_failed
echo Building Host...
dotnet build AdvGenNoSqlServer.Host\AdvGenNoSqlServer.Host.csproj -c Release --nologo -v q
if errorlevel 1 goto :build_failed

rem --- Start the server in CACHE mode in its own window ---
start "AdvGenNoSQL Host (Cache)" cmd /k "set DOTNET_ENVIRONMENT=Development&& set NOSQL_STORAGE_MODE=Cache&& set NOSQL_MASTER_PASSWORD=admin123&& set NOSQL_ENABLE_SSL=false&& set NOSQL_REQUIRE_AUTHENTICATION=true&& dotnet run --project AdvGenNoSqlServer.Host -c Release --no-build"

echo Waiting for the server on TCP port 9191...
powershell -NoProfile -Command "$ok=$false; for($i=0;$i -lt 60;$i++){ try { $c=New-Object Net.Sockets.TcpClient('localhost',9191); $c.Close(); $ok=$true; break } catch { Start-Sleep -Milliseconds 500 } }; if(-not $ok){ exit 1 }"
if errorlevel 1 goto :host_failed

rem --- Start the Blazor Server web UI in its own window ---
start "AdvGenNoSQL Messages Web" cmd /k "set ASPNETCORE_ENVIRONMENT=Development&& set ASPNETCORE_URLS=http://localhost:5290&& dotnet run --project AdvGenNoSqlServer.Examples.Messages.Web -c Release --no-build --no-launch-profile"

echo Waiting for the Web UI on http://localhost:5290 ...
powershell -NoProfile -Command "$ok=$false; for($i=0;$i -lt 90;$i++){ try { $r=Invoke-WebRequest -Uri 'http://localhost:5290' -UseBasicParsing -TimeoutSec 2; if($r.StatusCode -eq 200){ $ok=$true; break } } catch { Start-Sleep -Milliseconds 500 } }; if(-not $ok){ exit 1 }"
if errorlevel 1 goto :app_failed

start "" "http://localhost:5290"
echo.
echo Web demo is running at http://localhost:5290
echo Open it in multiple browser tabs to chat - all messages are stored ONLY in
echo server memory (cache mode). Closing the Host window discards them.
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

:app_failed
echo.
echo The Web UI did not become ready at http://localhost:5290 within 45s.
echo Check the "AdvGenNoSQL Messages Web" window.
pause
exit /b 1