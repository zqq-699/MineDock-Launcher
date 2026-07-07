@echo off
setlocal

set "TARGET=%APPDATA%\MDL"

echo This script removes launcher data from:
echo %TARGET%
echo.
echo Make sure MineDock Launcher is closed before continuing.
choice /M "Delete this directory and all of its contents"
if errorlevel 2 (
    echo Cancelled.
    exit /b 0
)

if not exist "%TARGET%" (
    echo Directory does not exist. Nothing to delete.
    exit /b 0
)

rmdir /s /q "%TARGET%"
if exist "%TARGET%" (
    echo Failed to delete %TARGET%
    pause
    exit /b 1
)

echo MineDock Launcher AppData has been cleared.
exit /b 0
