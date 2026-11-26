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
"%DOTNET_BIN%" test tests\PortaSoundStudio.Tests\PortaSoundStudio.Tests.csproj -c Release
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Tests failed! Build aborted.
    pause
    exit /b %ERRORLEVEL%
)

echo [2/2] Publishing standalone PSS-Studio.exe to dist\...
if exist ".tmp_publish" rmdir /s /q ".tmp_publish"
"%DOTNET_BIN%" publish src\PortaSoundStudio\PortaSoundStudio.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .tmp_publish
if %ERRORLEVEL% neq 0 (
    if exist ".tmp_publish" rmdir /s /q ".tmp_publish"
    echo [ERROR] Publish failed!
    pause
    exit /b %ERRORLEVEL%
)

if not exist dist mkdir dist
copy /y .tmp_publish\PSS-Studio.exe dist\PSS-Studio.exe >nul
copy /y Launch-Studio.bat dist\Launch-Studio.bat >nul
copy /y ASUS_ROG_ALLY_SETUP.txt dist\ASUS_ROG_ALLY_SETUP.txt >nul
rmdir /s /q ".tmp_publish"
powershell -NoProfile -Command "Compress-Archive -Path dist\PSS-Studio.exe, dist\Launch-Studio.bat, dist\ASUS_ROG_ALLY_SETUP.txt -DestinationPath dist\PSS-Studio-win-x64.zip -Force" >nul

echo.
echo ======================================================
echo   SUCCESS: Standalone executable created at:
echo   dist\PSS-Studio.exe
echo   dist\PSS-Studio-win-x64.zip
echo ======================================================
pause
