@echo off
title Launching Yamaha PortaSound Studio...
cd /d "%~dp0"

if exist "bin\Publish\PSS-Studio.exe" (
    start "" "bin\Publish\PSS-Studio.exe"
    exit
)

if exist "src\PSS790Player\bin\Debug\net8.0-windows\win-x64\PSS-Studio.exe" (
    start "" "src\PSS790Player\bin\Debug\net8.0-windows\win-x64\PSS-Studio.exe"
    exit
)

echo Build not found. Running build.bat first...
call build.bat
if exist "bin\Publish\PSS-Studio.exe" (
    start "" "bin\Publish\PSS-Studio.exe"
)
exit
