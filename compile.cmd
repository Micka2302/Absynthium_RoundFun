@echo off
setlocal

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0compile.ps1" %*

endlocal
