@echo off
setlocal
cd /d "%~dp0"
dotnet build tools\Repeat8\Repeat8.csproj --nologo -v quiet
if errorlevel 1 goto failed
tools\Repeat8\bin\Debug\net8.0-windows\Repeat8.exe "%~dp0dependencies\dd\hid.72000.dll" %*
if errorlevel 1 goto failed
exit /b 0
:failed
echo Failed. See the error above.
pause
exit /b 1
