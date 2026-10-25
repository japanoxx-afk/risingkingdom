@echo off
cd /d "%~dp0"
if exist "build\RisingKingdomsKO.exe" goto launch
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
if errorlevel 1 goto failed
:launch
start "" "%~dp0build\RisingKingdomsKO.exe"
exit /b 0
:failed
echo Build failed. Please check build.ps1 output.
pause
exit /b 1
