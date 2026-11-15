@echo off
setlocal
set PROJ=%~dp0src\XiaomiBuds\XiaomiBuds.csproj
set EXE=%~dp0src\XiaomiBuds\bin\Release\net10.0-windows10.0.26100.0\win-x64\XiaomiBuds.exe

if "%1"=="--rebuild" (shift & goto build)
if exist "%EXE%" goto run

:build
where dotnet >nul 2>nul || (
  echo The .NET SDK is not installed. Install it with:  winget install Microsoft.DotNet.SDK.10
  pause & exit /b 1
)
taskkill /im XiaomiBuds.exe /f >nul 2>nul
dotnet build "%PROJ%" -c Release -nologo -v q || (pause & exit /b 1)

:run
start "" "%EXE%" %1 %2 %3
