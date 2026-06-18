@echo off
setlocal

cd /d "%~dp0"
set ORDERLY_RUNTIME_ENV=development
set ORDERLY_ENABLE_PRIVILEGED_QA_STARTUP=1
dotnet run --project "src\Orderly.App\Orderly.App.csproj" -- --qa-mode

if errorlevel 1 (
  echo.
  echo Orderly.App 启动失败，错误码：%errorlevel%
  pause
)
