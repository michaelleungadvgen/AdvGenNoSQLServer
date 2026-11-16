@echo off
setlocal
rem Copyright (c) 2026 AdvanGeneration Pty. Ltd.
rem Licensed under the MIT License.
rem See LICENSE.txt for license information.

rem Runs the AdvGenNoSQL Host + Web Admin (Blazor WASM, REST) end to end:
rem   Host      : TCP 9191, HTTPS REST 9192, Development, admin/admin123
rem   Web Admin : http://localhost:62960
rem The Web Admin talks to the HTTPS REST API on TCP port + 1 (9192); it does
rem not use the TCP binary protocol, so Host TCP SSL is turned off here.
rem Close the two spawned windows to stop.

title AdvGenNoSQL Web Admin
cd /d "%~dp0"

echo ============================================================
echo  AdvGenNoSQL Web Admin (Blazor WASM / REST)
echo  Host      : TCP 9191, HTTPS REST 9192
echo  Web Admin : http://localhost:62960
echo  Login     : Server URL=localhost:9191  admin / admin123
echo ============================================================
echo.

set CONFIG=Release

rem --- Build once so startup is fast and errors surface early ---
echo Building Host...
dotnet build AdvGenNoSqlServer.Host\AdvGenNoSqlServer.Host.csproj -c %CONFIG% --nologo -v q
if errorlevel 1 goto :build_failed
echo Building Web Admin...
dotnet build AdvGenNoSqlServer.Admin\AdvGenNoSqlServer.Admin.csproj -c %CONFIG% --nologo -v q
if errorlevel 1 goto :build_failed

rem --- Trust the ASP.NET Core dev certificate (the REST API is HTTPS) ---
dotnet dev-certs https --check --trust >nul 2>&1
if errorlevel 1 (
    echo Trusting the ASP.NET Core HTTPS dev certificate...
    dotnet dev-certs https --trust
)

rem --- Start the Host (Development, TCP SSL off, HTTPS REST on 9192, admin/admin123) ---
start "AdvGenNoSQL Host" cmd /k "set DOTNET_ENVIRONMENT=Development&& set NOSQL_MASTER_PASSWORD=admin123&& set NOSQL_ENABLE_SSL=false&& set NOSQL_ADMIN_HTTP_USE_HTTPS=true&& dotnet run --project AdvGenNoSqlServer.Host -c %CONFIG% --no-build --no-launch-profile"

echo Waiting for the server on TCP port 9191...
powershell -NoProfile -Command "$ok=$false; for($i=0;$i -lt 60;$i++){ try { $c=New-Object Net.Sockets.TcpClient('localhost',9191); $c.Close(); $ok=$true; break } catch { Start-Sleep -Milliseconds 500 } }; if(-not $ok){ exit 1 }"
if errorlevel 1 goto :host_failed

rem --- Start the Web Admin (Blazor WASM DevServer on http 62960) ---
start "AdvGenNoSQL Web Admin" cmd /k "set ASPNETCORE_ENVIRONMENT=Development&& set ASPNETCORE_URLS=http://localhost:62960&& dotnet run --project AdvGenNoSqlServer.Admin -c %CONFIG% --no-build --no-launch-profile"

echo Waiting for the Web Admin on http://localhost:62960 ...
powershell -NoProfile -Command "$ok=$false; for($i=0;$i -lt 90;$i++){ try { $r=Invoke-WebRequest -Uri 'http://localhost:62960' -UseBasicParsing -TimeoutSec 2; if($r.StatusCode -eq 200){ $ok=$true; break } } catch { Start-Sleep -Milliseconds 500 } }; if(-not $ok){ exit 1 }"
if errorlevel 1 goto :app_failed

start "" "http://localhost:62960"
echo.
echo Web Admin is running at http://localhost:62960
echo Log in with Server URL=localhost:9191  user=admin  password=admin123
echo Close the "AdvGenNoSQL Host" and "AdvGenNoSQL Web Admin" windows to stop.
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
pause
exit /b 1

:app_failed
echo.
echo The Web Admin did not become ready at http://localhost:62960 within 45s.
echo Check the "AdvGenNoSQL Web Admin" window.
pause
exit /b 1