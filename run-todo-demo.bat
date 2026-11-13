@echo off
setlocal
rem Copyright (c) 2026 AdvanGeneration Pty. Ltd.
rem Licensed under the MIT License.
rem See LICENSE.txt for license information.

rem Runs the AdvGenNoSQL Todo demo end to end:
rem   Host  : TCP 9191, Development, SSL off, admin seeded from MasterPassword
rem   App   : Blazor TodoApp on http://localhost:5090 (auto-login admin/admin123)
rem Close the two spawned windows to stop the demo.

title AdvGenNoSQL Todo Demo
cd /d "%~dp0"

echo ============================================================
echo  AdvGenNoSQL Todo Demo
echo  Host : TCP 9191 (Development, SSL off)
echo  App  : http://localhost:5090
echo ============================================================
echo.

rem --- Build once so startup is fast and errors surface early ---
echo Building TodoApp...
dotnet build AdvGenNoSqlServer.Examples.TodoApp\AdvGenNoSqlServer.Examples.TodoApp.csproj -c Release --nologo -v q
if errorlevel 1 goto :build_failed
echo Building Host...
dotnet build AdvGenNoSqlServer.Host\AdvGenNoSqlServer.Host.csproj -c Release --nologo -v q
if errorlevel 1 goto :build_failed

rem --- Start the server in its own window (demo settings, Development only) ---
start "AdvGenNoSQL Host" cmd /k "set DOTNET_ENVIRONMENT=Development&& set NOSQL_MASTER_PASSWORD=admin123&& set NOSQL_ENABLE_SSL=false&& dotnet run --project AdvGenNoSqlServer.Host -c Release --no-build"

echo Waiting for the server on TCP port 9191...
powershell -NoProfile -Command "$ok=$false; for($i=0;$i -lt 60;$i++){ try { $c=New-Object Net.Sockets.TcpClient('localhost',9191); $c.Close(); $ok=$true; break } catch { Start-Sleep -Milliseconds 500 } }; if(-not $ok){ exit 1 }"
if errorlevel 1 goto :host_failed

rem --- Start the Blazor app in its own window ---
start "AdvGenNoSQL TodoApp" cmd /k "dotnet run --project AdvGenNoSqlServer.Examples.TodoApp -c Release --no-build --launch-profile http"

echo Waiting for the app on http://localhost:5090 ...
powershell -NoProfile -Command "$ok=$false; for($i=0;$i -lt 60;$i++){ try { $r=Invoke-WebRequest -Uri 'http://localhost:5090' -UseBasicParsing -TimeoutSec 2; if($r.StatusCode -eq 200){ $ok=$true; break } } catch { Start-Sleep -Milliseconds 500 } }; if(-not $ok){ exit 1 }"
if errorlevel 1 goto :app_failed

start "" "http://localhost:5090"
echo.
echo Demo is running at http://localhost:5090
echo The app connects automatically (admin / admin123, Development demo only).
echo Close the "AdvGenNoSQL Host" and "AdvGenNoSQL TodoApp" windows to stop.
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
echo Check the "AdvGenNoSQL Host" window (port 9191 may already be in use).
pause
exit /b 1

:app_failed
echo.
echo The app did not become ready at http://localhost:5090 within 30s.
echo Check the "AdvGenNoSQL TodoApp" window.
pause
exit /b 1