@echo off
chcp 65001 >nul
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-dist.ps1"
exit /b %ERRORLEVEL%
