@echo off
setlocal

cd /d "%~dp0"

set "PROJECT=Launcher.App\Launcher.App.csproj"
set "PROFILE=WinX64FrameworkDependentSingleFile"
set "OUTPUT=publish\BlockHelm-Launcher-win-x64-fdd-single"

echo Publishing %PROJECT%...
dotnet publish "%PROJECT%" -c Release -p:PublishProfile=%PROFILE%
if errorlevel 1 (
    echo.
    echo Publish failed.
    pause
    exit /b 1
)

echo.
echo Publish completed.
echo Output: %OUTPUT%
exit /b 0
