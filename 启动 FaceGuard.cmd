@echo off
setlocal
title FaceGuard

set "EXE=%LOCALAPPDATA%\Programs\FaceGuard\FaceGuardDaemon.exe"
if not exist "%EXE%" set "EXE=%~dp0bin\FaceGuardDaemon.exe"
if not exist "%EXE%" (
    echo [ERROR] FaceGuardDaemon.exe not found.
    echo         Run install.ps1 first, or build with build.ps1.
    pause
    exit /b 1
)

rem System mode: prefer the boot task.
schtasks /query /tn "FaceGuard Watch" >nul 2>&1
if not errorlevel 1 (
    schtasks /run /tn "FaceGuard Watch" >nul 2>&1
    if not errorlevel 1 (
        echo FaceGuard started ^(boot task^).
        ping -n 3 127.0.0.1 >nul
        exit /b 0
    )
)

rem User mode: start the background process.
tasklist /fi "imagename eq FaceGuardDaemon.exe" 2>nul | find /i "FaceGuardDaemon.exe" >nul
if not errorlevel 1 (
    echo FaceGuard is already running.
    ping -n 3 127.0.0.1 >nul
    exit /b 0
)

start "" "%EXE%" agent
ping -n 3 127.0.0.1 >nul

tasklist /fi "imagename eq FaceGuardDaemon.exe" 2>nul | find /i "FaceGuardDaemon.exe" >nul
if errorlevel 1 (
    echo [ERROR] failed to start. See %ProgramData%\FaceGuard\FaceGuard.log
    pause
    exit /b 1
)

echo FaceGuard started.
ping -n 3 127.0.0.1 >nul
exit /b 0
