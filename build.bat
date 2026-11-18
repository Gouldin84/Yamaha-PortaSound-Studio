@echo off
title Building Yamaha PortaSound Studio (PSS-Studio.exe)...
echo ======================================================
echo   Building Yamaha PortaSound Studio (PSS-Studio.exe)
echo ======================================================

set DOTNET_BIN=dotnet
if exist "%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe" (
    set "DOTNET_BIN=%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe"
)

echo [1/2] Running automated tests...
"%DOTNET_BIN%" test tests\PSS790Player.Tests\PSS790Player.Tests.csproj -c Release
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Tests failed! Build aborted.
    pause
    exit /b %ERRORLEVEL%
)

echo [2/2] Publishing standalone PSS-Studio.exe to bin\Publish...
"%DOTNET_BIN%" publish src\PSS790Player\PSS790Player.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o bin\Publish
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Publish failed!
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo ======================================================
echo   SUCCESS: Standalone executable created at:
echo   bin\Publish\PSS-Studio.exe
echo ======================================================
pause
