@echo off
cd /d "%~dp0"
del /q "src\Orderly.App\*_wpftmp.csproj" 2>nul
dotnet watch run --project "src\Orderly.App\Orderly.App.csproj"
if errorlevel 1 pause
