@echo off
title Jellyfin AniList Sync - Service Uninstaller

echo.
echo ================================================================================
echo                   Jellyfin AniList Sync - Service Uninstaller
echo ================================================================================
echo.

REM Check if NSSM is available
nssm version >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: NSSM not found in PATH.
    echo The service may still be installed but cannot be managed without NSSM.
    echo.
    pause
    exit /b 1
)

REM Check if service exists
nssm status JellyfinAnilistSync >nul 2>&1
if %errorlevel% neq 0 (
    echo Service 'JellyfinAnilistSync' is not installed.
    echo.
    pause
    exit /b 0
)

echo Current service status:
nssm status JellyfinAnilistSync
echo.

set /p confirm="Are you sure you want to remove the JellyfinAnilistSync service? (y/n): "
if /i "%confirm%" neq "y" (
    echo Uninstall cancelled.
    pause
    exit /b 0
)

echo.
echo Stopping service...
nssm stop JellyfinAnilistSync

echo Removing service...
nssm remove JellyfinAnilistSync confirm

if %errorlevel% equ 0 (
    echo.
    echo ================================================================================
    echo                            Service Removed Successfully
    echo ================================================================================
    echo.
    echo The JellyfinAnilistSync service has been removed.
    echo.
    echo Note: The following files are NOT automatically deleted:
    echo   - config.json (your configuration)
    echo   - Logs\ directory (your log files)
    echo   - JellyfinAnilistSync.exe (the application)
    echo.
    echo You can manually delete these files if you no longer need them.
    echo.
) else (
    echo.
    echo ERROR: Failed to remove service. You may need to remove it manually.
    echo Try: nssm remove JellyfinAnilistSync confirm
    echo.
)

pause
