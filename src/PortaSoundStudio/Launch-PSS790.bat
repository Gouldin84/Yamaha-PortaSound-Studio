@echo off
title Launching Yamaha PortaSound Studio...
cd /d "%~dp0"

:: Remove Mark of the Web (Zone.Identifier) if present
powershell -NoProfile -Command "if (Test-Path 'PSS-Studio.exe') { Unblock-File -Path 'PSS-Studio.exe' } elseif (Test-Path 'PSS790Player.exe') { Unblock-File -Path 'PSS790Player.exe' }"

:: Launch the workstation
if exist "PSS-Studio.exe" (
    start "" "PSS-Studio.exe"
) else (
    start "" "PSS790Player.exe"
)
exit
