@echo off
title Launching Yamaha PortaSound Studio...
cd /d "%~dp0"

if exist "PSS-Studio.exe" (
    powershell -NoProfile -Command "Unblock-File -Path '.\PSS-Studio.exe' -ErrorAction SilentlyContinue" >nul 2>&1
    start "" "PSS-Studio.exe"
    exit
)

if exist "dist\PSS-Studio.exe" (
    powershell -NoProfile -Command "Unblock-File -Path '.\dist\PSS-Studio.exe' -ErrorAction SilentlyContinue" >nul 2>&1
    start "" "dist\PSS-Studio.exe"
    exit
)

if exist "bin\Publish\PSS-Studio.exe" (
    start "" "bin\Publish\PSS-Studio.exe"
    exit
)

if exist "src\PSS790Player\bin\Release\net8.0-windows\win-x64\PSS-Studio.exe" (
    start "" "src\PSS790Player\bin\Release\net8.0-windows\win-x64\PSS-Studio.exe"
    exit
)

if exist "src\PSS790Player\bin\Debug\net8.0-windows\win-x64\PSS-Studio.exe" (
    start "" "src\PSS790Player\bin\Debug\net8.0-windows\win-x64\PSS-Studio.exe"
    exit
)

if exist "build.bat" (
    echo Build not found. Running build.bat first...
    call build.bat
    if exist "bin\Publish\PSS-Studio.exe" (
        start "" "bin\Publish\PSS-Studio.exe"
    )
    exit
)

echo PSS-Studio.exe was not found in "%~dp0".
pause
exit
