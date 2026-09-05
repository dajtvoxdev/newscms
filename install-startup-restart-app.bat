@echo off
setlocal

set "APP_SCRIPT=%~dp0restart-app.bat"
set "TASK_NAME=NewsCMS Restart App"
set "OLD_SHORTCUT=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\NewsCMS Restart App.lnk"

if not exist "%APP_SCRIPT%" (
    echo Khong tim thay: %APP_SCRIPT%
    pause
    exit /b 1
)

net session >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo Can chay file nay bang Run as administrator.
    pause
    exit /b 1
)

if exist "%OLD_SHORTCUT%" del "%OLD_SHORTCUT%" >nul 2>&1

schtasks /delete /tn "%TASK_NAME%" /f >nul 2>&1
schtasks /create /tn "%TASK_NAME%" /sc onstart /ru SYSTEM /rl HIGHEST /tr "cmd.exe /c \"%APP_SCRIPT%\""

if %ERRORLEVEL% NEQ 0 (
    echo Cai task startup that bai!
    pause
    exit /b 1
)

echo Da cai auto run khi Windows boot:
schtasks /query /tn "%TASK_NAME%"
pause
