@echo off
setlocal
set "APP=%~dp0src\InputProbe.App\bin\Debug\net8.0-windows\InputProbe.App.exe"
if not exist "%APP%" (
  echo Application is not built. Run: dotnet build InputProbe.sln
  pause
  exit /b 1
)
start "Input Probe" "%APP%"
endlocal
