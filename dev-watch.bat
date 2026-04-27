@echo off
cd /d D:\Dev\Orderly
dotnet watch run --project src\Orderly.App\Orderly.App.csproj
if errorlevel 1 pause