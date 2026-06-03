@echo off
REM Convenience wrapper around build.ps1 for cmd / File Explorer users.
REM Passes all arguments through.

setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "build.ps1" %*
exit /b %ERRORLEVEL%
