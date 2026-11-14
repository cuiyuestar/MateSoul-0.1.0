@echo off
chcp 65001 >nul
title MateSoul Build and Install APK
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build_and_install_apk.ps1" %*
set "RC=%ERRORLEVEL%"
echo.
if not "%RC%"=="0" echo [FAILED] exit code %RC%
pause
exit /b %RC%
