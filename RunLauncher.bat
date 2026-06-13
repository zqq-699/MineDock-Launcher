@echo on
cd /d "%~dp0"
dotnet run --project Launcher.App\Launcher.App.csproj
if errorlevel 1 pause
