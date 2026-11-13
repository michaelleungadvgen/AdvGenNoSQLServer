@echo off
setlocal
rem Copyright (c) 2026 AdvanGeneration Pty. Ltd.
rem Licensed under the MIT License.
rem See LICENSE.txt for license information.

rem Runs the AdvGenNoSQL Server (Host) on its own:
rem   TCP 9191, Development, SSL off, admin seeded from MasterPassword (admin123).
rem Press Ctrl+C in this window to stop the server.

title AdvGenNoSQL Server
cd /d "%~dp0"

echo ============================================================
echo  AdvGenNoSQL Server (Host)
echo  TCP  : 9191   Admin API: 9192
echo  Mode : Development, SSL off, admin / admin123 (demo only)
echo ============================================================
echo.

echo Building Host...
dotnet build AdvGenNoSqlServer.Host\AdvGenNoSqlServer.Host.csproj -c Release --nologo -v q
if errorlevel 1 (
    echo Build failed. Fix the errors above and run this script again.
    pause
    exit /b 1
)

set DOTNET_ENVIRONMENT=Development
set NOSQL_MASTER_PASSWORD=admin123
set NOSQL_ENABLE_SSL=false

echo Starting server (Ctrl+C to stop)...
echo.
dotnet run --project AdvGenNoSqlServer.Host -c Release --no-build
pause