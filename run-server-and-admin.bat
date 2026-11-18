@echo off
REM =====================================================================
REM  AdvGenNoSQL - launch the Host server and the Admin UI together.
REM  Each runs in its own console window; close a window (or Ctrl+C)
REM  to stop that process.
REM
REM    Host server : TCP  localhost:9191 (SSL)  +  HTTPS API :9192
REM    Admin UI    : https://localhost:7210
REM =====================================================================

setlocal
set ROOT=%~dp0
set CONFIG=Release

echo.
echo === AdvGenNoSQL launcher ===
echo   Config      : %CONFIG%
echo   Host server : TCP localhost:9191 (SSL), HTTPS :9192
echo   Admin UI    : https://localhost:7210
echo.

REM --- Free the ports from any stale run (Host: 9191/9192, Admin UI: 7210) ---
echo Releasing ports 9191, 9192, 7210 from any previous run...
for %%P in (9191 9192 7210) do (
    for /f "tokens=5" %%K in ('netstat -ano ^| findstr /r /c:":%%P .*LISTENING"') do (
        echo   stopping PID %%K on port %%P
        taskkill /F /PID %%K >nul 2>&1
    )
)

REM --- Ensure the ASP.NET Core dev certificate is trusted (safe to run repeatedly) ---
dotnet dev-certs https --check --trust >nul 2>&1
if errorlevel 1 (
    echo Trusting the ASP.NET Core HTTPS dev certificate...
    dotnet dev-certs https --trust
)

REM --- Start the NoSQL Host server in its own window ---
echo Starting Host server...
start "AdvGenNoSQL Host" cmd /k "cd /d "%ROOT%" && dotnet run --project AdvGenNoSqlServer.Host\AdvGenNoSqlServer.Host.csproj -c %CONFIG% --no-launch-profile"

REM --- Give the server a head start so the Admin UI can connect on first try ---
echo Waiting for the server to come up...
timeout /t 8 /nobreak >nul

REM --- Start the Admin UI in its own window ---
echo Starting Admin UI...
start "AdvGenNoSQL Admin UI" cmd /k "cd /d "%ROOT%" && dotnet run --project AdvGenNoSqlServer.AdminClient\AdvGenNoSqlServer.AdminClient.csproj -c %CONFIG%"

REM --- Open the Admin UI in the default browser ---
timeout /t 6 /nobreak >nul
start "" "https://localhost:7210"

echo.
echo Both processes launched in separate windows.
echo   Log in with host=localhost  port=9191  user=admin  (master password from Host appsettings.json)
echo Close this window; the two service windows keep running.
echo.
endlocal
