@echo off
setlocal
title FaceGuard

rem System mode: stop the boot task first.
schtasks /query /tn "FaceGuard Watch" >nul 2>&1
if not errorlevel 1 (
    schtasks /end /tn "FaceGuard Watch" >nul 2>&1
)

rem Then kill the background process (works in both modes).
tasklist /fi "imagename eq FaceGuardDaemon.exe" 2>nul | find /i "FaceGuardDaemon.exe" >nul
if errorlevel 1 (
    echo FaceGuard is not running.
) else (
    taskkill /f /im FaceGuardDaemon.exe >nul 2>&1
    echo FaceGuard stopped.
)

ping -n 3 127.0.0.1 >nul
exit /b 0
