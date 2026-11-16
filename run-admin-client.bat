@echo off
setlocal
rem Copyright (c) 2026 AdvanGeneration Pty. Ltd.
rem Licensed under the MIT License.
rem See LICENSE.txt for license information.

rem Runs the AdvGenNoSQL Host + Admin Client (Blazor Server, TCP) end to end:
rem   Host         : TCP 9191 (SSL), HTTPS REST 9192, Development, admin/admin123
rem   Admin Client : https://localhost:7210
rem The Admin Client connects to the Host over TCP with SSL (always required).
rem Close the two spawned windows to stop.

title AdvGenNoSQL Admin Client
cd /d "%~dp0"

echo ============================================================
echo  AdvGenNoSQL Admin Client (Blazor Server / TCP)
echo  Host         : TCP 9191 (SSL), REST 9192
echo  Admin Client : https://localhost:7210
echo  Login        : host=localhost  port=9191  admin / admin123
echo ============================================================
echo.

set CONFIG=Release

rem --- Build once so startup is fast and errors surface early ---
echo Building Host...
dotnet build AdvGenNoSqlServer.Host\AdvGenNoSqlServer.Host.csproj -c %CONFIG% --nologo -v q
if errorlevel 1 goto :build_failed
echo Building Admin Client...
dotnet build AdvGenNoSqlServer.AdminClient\AdvGenNoSqlServer.AdminClient.csproj -c %CONFIG% --nologo -v q
if errorlevel 1 goto :build_failed

rem --- Trust the ASP.NET Core dev certificate (browser HTTPS + TCP TLS) ---
dotnet dev-certs https --check --trust >nul 2>&1
if errorlevel 1 (
    echo Trusting the ASP.NET Core HTTPS dev certificate...
    dotnet dev-certs https --trust
)

rem --- Ensure the Host TCP SSL certificate exists (Admin Client always uses SSL) ---
if not exist "AdvGenNoSqlServer.Host\certs\advgen.pfx" (
    echo Exporting dev certificate for Host TCP SSL...
    if not exist "AdvGenNoSqlServer.Host\certs" mkdir "AdvGenNoSqlServer.Host\certs"
    dotnet dev-certs https -ep "AdvGenNoSqlServer.Host\certs\advgen.pfx" -p devpassword
)

rem --- Start the Host (Development, TCP SSL on via appsettings, admin/admin123) ---
start "AdvGenNoSQL Host" cmd /k "set DOTNET_ENVIRONMENT=Development&& set NOSQL_MASTER_PASSWORD=admin123&& dotnet run --project AdvGenNoSqlServer.Host -c %CONFIG% --no-build --no-launch-profile"

echo Waiting for the server on TCP port 9191...
powershell -NoProfile -Command "$ok=$false; for($i=0;$i -lt 60;$i++){ try { $c=New-Object Net.Sockets.TcpClient('localhost',9191); $c.Close(); $ok=$true; break } catch { Start-Sleep -Milliseconds 500 } }; if(-not $ok){ exit 1 }"
if errorlevel 1 goto :host_failed

rem --- Start the Admin Client (HTTPS 7210) ---
start "AdvGenNoSQL Admin Client" cmd /k "set ASPNETCORE_ENVIRONMENT=Development&& set ASPNETCORE_URLS=https://localhost:7210&& dotnet run --project AdvGenNoSqlServer.AdminClient -c %CONFIG% --no-build --no-launch-profile"

echo Waiting for the Admin Client on https://localhost:7210 ...
powershell -NoProfile -Command "$ok=$false; for($i=0;$i -lt 60;$i++){ try { $c=New-Object Net.Sockets.TcpClient('localhost',7210); $c.Close(); $ok=$true; break } catch { Start-Sleep -Milliseconds 500 } }; if(-not $ok){ exit 1 }"
if errorlevel 1 goto :app_failed

start "" "https://localhost:7210"
echo.
echo Admin Client is running at https://localhost:7210
echo Log in with host=localhost  port=9191  user=admin  password=admin123
echo Close the "AdvGenNoSQL Host" and "AdvGenNoSQL Admin Client" windows to stop.
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
echo Check the "AdvGenNoSQL Host" window. If port 9191 is already in use,
echo close the previous run's Host window and run this script again.
echo Also verify the SSL certificate in AdvGenNoSqlServer.Host\certs is valid.
pause
exit /b 1

:app_failed
echo.
echo The Admin Client did not open port 7210 within 30s.
echo Check the "AdvGenNoSQL Admin Client" window.
pause
exit /b 1