@echo off
setlocal

cd /d "D:\Dev\Orderly"
dotnet run --project "src\Orderly.App"

if errorlevel 1 (
  echo.
  echo Orderly.App 启动失败，错误码：%errorlevel%
  pause
)
