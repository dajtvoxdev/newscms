@echo off
setlocal

set PORT=5000
set PROJECT_PATH=%~dp0NewsCMS.Core\src\NewsCMS.Web\NewsCMS.Web.csproj
set SOLUTION_PATH=%~dp0NewsCMS.Core\NewsCMS.Core.sln

echo ============================================
echo   NewsCMS - Restart App
echo ============================================

echo.
echo [1/3] Dang tat ung dung tren port %PORT% ...
for /f "tokens=5" %%a in ('netstat -ano ^| findstr ":%PORT%.*LISTENING"') do (
    echo      Tim thay PID: %%a - Dang kill...
    taskkill /PID %%a /F >nul 2>&1
)
echo      Done.

echo.
echo [2/3] Dang build lai solution...
REM Bo --no-restore: khi csproj thay doi (them PackageReference/ProjectReference)
REM thi build se fail neu chua restore.
dotnet build "%SOLUTION_PATH%" --configuration Release
if %ERRORLEVEL% NEQ 0 (
    echo      Build that bai!
    pause
    exit /b 1
)
echo      Build thanh cong!

echo.
echo [3/3] Dang khoi chay lai ung dung...
start "NewsCMS.Web" dotnet run --project "%PROJECT_PATH%" --configuration Release
echo      App dang khoi dong tren port %PORT%...

echo.
echo ============================================
echo   Hoan tat! Mo trinh duyet tai http://localhost:%PORT%
echo ============================================
timeout /t 3 >nul
