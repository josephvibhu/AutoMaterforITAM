@echo off
Title ITAM Hardware QC Launcher

:: 1. Request Admin Rights
>nul 2>&1 "%SYSTEMROOT%\system32\cacls.exe" "%SYSTEMROOT%\system32\config\system"
if '%errorlevel%' NEQ '0' (
    powershell.exe -Command "Start-Process '%~dpnx0' -Verb RunAs"
    exit /B
)

:: 2. Force the command prompt back to the USB drive
cd /d "%~dp0"

:: 3. Run the script using an absolute path to prevent it from getting lost
powershell.exe -ExecutionPolicy Bypass -NoProfile -File "%~dp0AutoQC.ps1"

:: 4. If the script crashes, keep the window open so we can read the error
echo.
echo If you see this, the PowerShell script crashed or finished without waiting.
pause
