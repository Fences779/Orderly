@echo off
setlocal
cd /d "%~dp0"
del /q "src\Orderly.App\*_wpftmp.csproj" 2>nul
set ORDERLY_RUNTIME_ENV=development
set ORDERLY_ENABLE_PRIVILEGED_QA_STARTUP=1
dotnet watch run --project "src\Orderly.App\Orderly.App.csproj" -- --qa-mode
if errorlevel 1 pause
