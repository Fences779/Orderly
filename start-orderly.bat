@echo off
setlocal

cd /d "%~dp0"
dotnet run --project "src\Orderly.App\Orderly.App.csproj"

if errorlevel 1 (
  echo.
  echo Orderly.App 启动失败，错误码：%errorlevel%
  pause
)
