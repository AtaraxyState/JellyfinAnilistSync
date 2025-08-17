@echo off
setlocal EnableDelayedExpansion
title Jellyfin AniList Sync - Service Installer

echo.
echo ================================================================================
echo                    Jellyfin AniList Sync - Service Installer
echo ================================================================================
echo.
echo This script will help you configure and install the Jellyfin AniList Sync service.
echo Make sure you have NSSM (Non-Sucking Service Manager) installed and in your PATH.
echo.
pause

REM Check if NSSM is available
nssm version >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: NSSM not found in PATH. Please install NSSM first.
    echo Download from: https://nssm.cc/download
    echo.
    pause
    exit /b 1
)

REM Check if service already exists
nssm status JellyfinAnilistSync >nul 2>&1
if %errorlevel% equ 0 (
    echo.
    echo WARNING: Service 'JellyfinAnilistSync' already exists!
    set /p continue="Do you want to remove and reinstall? (y/n): "
    if /i "!continue!" neq "y" (
        echo Installation cancelled.
        pause
        exit /b 0
    )
    echo Stopping and removing existing service...
    nssm stop JellyfinAnilistSync
    nssm remove JellyfinAnilistSync confirm
)

echo.
echo ================================================================================
echo                            Configuration Setup
echo ================================================================================

REM Jellyfin Configuration
echo.
echo --- Jellyfin Server Configuration ---
:get_jellyfin_url
set /p jellyfin_url="Enter Jellyfin server URL (e.g., http://localhost:8096): "
if "!jellyfin_url!"=="" (
    echo ERROR: Jellyfin server URL cannot be empty.
    goto get_jellyfin_url
)

:get_jellyfin_api_key
set /p jellyfin_api_key="Enter Jellyfin API key: "
if "!jellyfin_api_key!"=="" (
    echo ERROR: Jellyfin API key cannot be empty.
    goto get_jellyfin_api_key
)

REM Webhook Configuration
echo.
echo --- Webhook Configuration ---
set /p webhook_host="Enter webhook host (default: localhost): "
if "!webhook_host!"=="" set webhook_host=localhost

set /p webhook_port="Enter webhook port (default: 5000): "
if "!webhook_port!"=="" set webhook_port=5000

REM Library Configuration
echo.
echo --- Library Configuration ---
echo Enter library names that contain anime (one per line, press Enter on empty line to finish):
set library_names=
set library_count=0

:library_loop
set /p library_name="Library name (or press Enter to finish): "
if "!library_name!"=="" goto library_done
REM Trim spaces and validate the library name isn't just spaces
set "library_name=!library_name: =!"
if "!library_name!"=="" (
    echo ERROR: Library name cannot be empty or just spaces.
    goto library_loop
)
if !library_count! equ 0 (
    set library_names="!library_name!"
) else (
    set library_names=!library_names!, "!library_name!"
)
set /a library_count+=1
if !library_count! geq 10 (
    echo Maximum 10 libraries reached.
    goto library_done
)
goto library_loop

:library_done
if !library_count! equ 0 (
    set library_names="Anime", "TV Shows"
    echo No libraries specified. Using defaults: Anime, TV Shows
)

REM User Configuration
echo.
echo --- User Configuration ---
echo Now we'll configure AniList users. You'll need AniList access tokens for each user.
echo To get an access token, visit: https://anilist.co/api/v2/oauth/authorize?client_id=YOUR_CLIENT_ID&response_type=token
echo.

set user_tokens=
set user_count=0

:user_loop
echo.
if !user_count! equ 0 (
    echo Adding first user:
) else (
    set /p add_user="Add another user? (y/n): "
    if /i "!add_user!" neq "y" goto user_done
)

:get_username
set /p username="Enter Jellyfin username: "
if "!username!"=="" (
    echo ERROR: Username cannot be empty.
    goto get_username
)

:get_token
set /p access_token="Enter AniList access token for !username!: "
if "!access_token!"=="" (
    echo ERROR: Access token cannot be empty.
    goto get_token
)

set /p auto_add="Auto-add new anime to AniList for !username!? (y/n): "
if /i "!auto_add!"=="y" (
    set auto_add_val=true
) else (
    set auto_add_val=false
)

set /p bulk_update="Enable bulk sync on login for !username!? (y/n): "
if /i "!bulk_update!"=="y" (
    set bulk_update_val=true
) else (
    set bulk_update_val=false
)

if !user_count! equ 0 (
    set user_tokens=      "!username!": "!access_token!"
    set user_settings=      "!username!": { "userAutoAdd": !auto_add_val!, "userBulkUpdate": !bulk_update_val! }
) else (
    set user_tokens=!user_tokens!,^

      "!username!": "!access_token!"
    set user_settings=!user_settings!,^

      "!username!": { "userAutoAdd": !auto_add_val!, "userBulkUpdate": !bulk_update_val! }
)

set /a user_count+=1
if !user_count! geq 20 (
    echo Maximum 20 users reached.
    goto user_done
)
goto user_loop

:user_done

if !user_count! equ 0 (
    echo ERROR: At least one user must be configured.
    pause
    exit /b 1
)

REM Create config.json
echo.
echo Creating config.json...

(
echo {
echo   "Jellyfin": {
echo     "ServerUrl": "!jellyfin_url!",
echo     "ApiKey": "!jellyfin_api_key!"
echo   },
echo   "AniList": {
echo     "UserTokens": {
echo !user_tokens!
echo     }
echo   },
echo   "Webhook": {
echo     "Host": "!webhook_host!",
echo     "Port": !webhook_port!
echo   },
echo   "LibraryNames": [!library_names!],
echo   "UserSettings": {
echo !user_settings!
echo   }
echo }
) > config.json

echo config.json created successfully!

REM Create Logs directory
if not exist "Logs" mkdir Logs

REM Install the service
echo.
echo ================================================================================
echo                            Installing Service
echo ================================================================================
echo.

echo Installing JellyfinAnilistSync service...
nssm install JellyfinAnilistSync "%~dp0JellyfinAnilistSync.exe"

if %errorlevel% neq 0 (
    echo ERROR: Failed to install service. Make sure JellyfinAnilistSync.exe exists.
    pause
    exit /b 1
)

echo Configuring service parameters...
nssm set JellyfinAnilistSync AppDirectory "%~dp0"
nssm set JellyfinAnilistSync DisplayName "Jellyfin AniList Sync Service"
nssm set JellyfinAnilistSync Description "Syncs Jellyfin anime progress with AniList"
nssm set JellyfinAnilistSync Start SERVICE_AUTO_START

REM Set up logging
nssm set JellyfinAnilistSync AppStdout "%~dp0Logs\service-output.log"
nssm set JellyfinAnilistSync AppStderr "%~dp0Logs\service-error.log"
nssm set JellyfinAnilistSync AppRotateFiles 1
nssm set JellyfinAnilistSync AppRotateOnline 1
nssm set JellyfinAnilistSync AppRotateSeconds 86400
nssm set JellyfinAnilistSync AppRotateBytes 1048576

REM Set environment variables
nssm set JellyfinAnilistSync AppEnvironmentExtra CONFIG_PATH="%~dp0config.json" ASPNETCORE_URLS="http://!webhook_host!:!webhook_port!"

echo.
echo ================================================================================
echo                            Installation Complete!
echo ================================================================================
echo.
echo Service 'JellyfinAnilistSync' has been installed successfully!
echo.
echo Configuration:
echo   - Jellyfin URL: !jellyfin_url!
echo   - Webhook: http://!webhook_host!:!webhook_port!
echo   - Users configured: !user_count!
echo   - Config file: %~dp0config.json
echo   - Logs directory: %~dp0Logs\
echo.
echo Next steps:
echo   1. Start the service: nssm start JellyfinAnilistSync
echo   2. Configure Jellyfin webhooks to point to: http://!webhook_host!:!webhook_port!
echo   3. Monitor logs in the Logs directory
echo.
echo Service management commands:
echo   - Start:   nssm start JellyfinAnilistSync
echo   - Stop:    nssm stop JellyfinAnilistSync
echo   - Status:  nssm status JellyfinAnilistSync
echo   - Remove:  nssm remove JellyfinAnilistSync confirm
echo.

set /p start_now="Start the service now? (y/n): "
if /i "!start_now!"=="y" (
    echo Starting service...
    nssm start JellyfinAnilistSync
    if %errorlevel% equ 0 (
        echo Service started successfully!
        echo Check logs in %~dp0Logs\ for any issues.
    ) else (
        echo ERROR: Failed to start service. Check the configuration and logs.
    )
)

echo.
echo Installation complete! Press any key to exit.
pause >nul
