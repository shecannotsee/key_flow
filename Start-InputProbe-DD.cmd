@echo off
setlocal
set "APP=%~dp0src\InputProbe.App\bin\Debug\net8.0-windows\InputProbe.App.exe"
set "DD_DLL=%~dp0.artifacts\dd-research\hid.72000.dll"
if not exist "%APP%" (
  echo Build the application first: dotnet build InputProbe.sln
  pause
  exit /b 1
)
if not exist "%DD_DLL%" (
  echo DD HID library is missing. See DD_RESEARCH.md.
  pause
  exit /b 1
)
start "Input Probe DD" "%APP%" --dd "%DD_DLL%"
