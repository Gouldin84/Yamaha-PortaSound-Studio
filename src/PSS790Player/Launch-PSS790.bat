@echo off
title Launching Yamaha PSS-790 Player...
cd /d "%~dp0"

:: Remove Mark of the Web (Zone.Identifier) if present
powershell -NoProfile -Command "if (Test-Path 'PSS790Player.exe') { Unblock-File -Path 'PSS790Player.exe' }"

:: Launch the workstation
start "" "PSS790Player.exe"
exit
