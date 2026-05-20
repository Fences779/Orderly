@echo off
setlocal

cd /d "%~dp0"
dotnet run --project "src\Orderly.App\Orderly.App.csproj"

if errorlevel 1 (
  echo.
  echo Orderly.App launch failed, exit code: %errorlevel%
  pause
)
