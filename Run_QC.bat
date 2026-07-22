@echo off
Title Hardware QC Launcher

:: Check for Admin rights
>nul 2>&1 "%SYSTEMROOT%\system32\cacls.exe" "%SYSTEMROOT%\system32\config\system"
if '%errorlevel%' NEQ '0' (
    echo Requesting Administrative Privileges for Hardware Scans...
    powershell.exe -Command "Start-Process '%~dpnx0' -Verb RunAs"
    exit /B
)

echo Starting Hardware QC Script...
:: Ensure the script runs from the current USB directory
cd /d "%~dp0"
powershell.exe -ExecutionPolicy Bypass -NoProfile -File "AutoQC.ps1"
