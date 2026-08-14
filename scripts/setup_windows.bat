@echo off
setlocal

where powershell.exe >nul 2>nul
if errorlevel 1 (
  echo Windows PowerShell is required but was not found.
  exit /b 1
)

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup_windows.ps1" %*
exit /b %errorlevel%
