@echo off
setlocal EnableDelayedExpansion
title Jellyfin AniList Sync - Service Installer (TEST MODE)

echo.
echo ================================================================================
echo              Jellyfin AniList Sync - Service Installer (TEST MODE)
echo ================================================================================
echo.
echo This is a TEST VERSION that will create files but NOT install the actual service.
echo It's safe to run alongside your existing installation.
echo.
pause

REM Create test directory
if not exist "test-install" mkdir test-install
cd test-install

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
set /p jellyfin_api_key="Enter Jellyfin API key (TEST - use 'test-key'): "
if "!jellyfin_api_key!"=="" (
    echo ERROR: Jellyfin API key cannot be empty.
    goto get_jellyfin_api_key
)

REM Webhook Configuration
echo.
echo --- Webhook Configuration ---
set /p webhook_host="Enter webhook host (default: localhost): "
if "!webhook_host!"=="" set webhook_host=localhost

set /p webhook_port="Enter webhook port (default: 5001 for testing): "
if "!webhook_port!"=="" set webhook_port=5001

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
echo Now we'll configure AniList users. You can use dummy tokens for testing.
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
set /p username="Enter Jellyfin username (TEST - try 'testuser'): "
if "!username!"=="" (
    echo ERROR: Username cannot be empty.
    goto get_username
)

:get_token
set /p access_token="Enter AniList access token (TEST - use 'test-token-123'): "
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
echo Creating test-config.json...

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
) > test-config.json

echo test-config.json created successfully!

REM Create mock installation commands (but don't execute them)
echo.
echo Creating mock-install-commands.txt with what WOULD be executed...

(
echo === NSSM Commands that WOULD be executed ===
echo.
echo nssm install JellyfinAnilistSyncTEST "%~dp0JellyfinAnilistSync.exe"
echo nssm set JellyfinAnilistSyncTEST AppDirectory "%~dp0"
echo nssm set JellyfinAnilistSyncTEST DisplayName "Jellyfin AniList Sync Service (TEST)"
echo nssm set JellyfinAnilistSyncTEST Description "TEST - Syncs Jellyfin anime progress with AniList"
echo nssm set JellyfinAnilistSyncTEST Start SERVICE_AUTO_START
echo.
echo === Logging Configuration ===
echo nssm set JellyfinAnilistSyncTEST AppStdout "%~dp0Logs\service-output.log"
echo nssm set JellyfinAnilistSyncTEST AppStderr "%~dp0Logs\service-error.log"
echo nssm set JellyfinAnilistSyncTEST AppRotateFiles 1
echo nssm set JellyfinAnilistSyncTEST AppRotateOnline 1
echo nssm set JellyfinAnilistSyncTEST AppRotateSeconds 86400
echo nssm set JellyfinAnilistSyncTEST AppRotateBytes 1048576
echo.
echo === Environment Variables ===
echo nssm set JellyfinAnilistSyncTEST AppEnvironmentExtra CONFIG_PATH="%~dp0test-config.json" ASPNETCORE_URLS="http://!webhook_host!:!webhook_port!"
echo.
echo === Service Management Commands ===
echo nssm start JellyfinAnilistSyncTEST
echo nssm stop JellyfinAnilistSyncTEST
echo nssm status JellyfinAnilistSyncTEST
echo nssm remove JellyfinAnilistSyncTEST confirm
) > mock-install-commands.txt

REM Create test logs directory
if not exist "TestLogs" mkdir TestLogs

echo.
echo ================================================================================
echo                              TEST RESULTS
echo ================================================================================
echo.
echo TEST COMPLETED SUCCESSFULLY!
echo.
echo Files created in test-install\ directory:
echo   - test-config.json          (Generated configuration)
echo   - mock-install-commands.txt (Commands that would be executed)
echo   - TestLogs\                 (Empty test logs directory)
echo.
echo Configuration Summary:
echo   - Jellyfin URL: !jellyfin_url!
echo   - Webhook: http://!webhook_host!:!webhook_port!
echo   - Users configured: !user_count!
echo   - Service name would be: JellyfinAnilistSyncTEST
echo.
echo The configuration looks correct! Here's what the real installation would do:
echo   1. Install service as 'JellyfinAnilistSyncTEST' (different from your current service)
echo   2. Use test-config.json instead of config.json
echo   3. Use port !webhook_port! (different from your current webhook)
echo   4. Set up proper logging and environment variables
echo.

type mock-install-commands.txt

echo.
echo ================================================================================
echo                                VALIDATION
echo ================================================================================
echo.

REM Validate JSON syntax
powershell -Command "try { Get-Content 'test-config.json' | ConvertFrom-Json | Out-Null; Write-Host '✅ JSON syntax is valid' -ForegroundColor Green } catch { Write-Host '❌ JSON syntax error:' $_.Exception.Message -ForegroundColor Red }"

echo.
echo Review the generated files and commands above.
echo If everything looks good, you can use the real install-service.bat safely!
echo.
pause

cd ..
echo Test files are saved in: %~dp0test-install\
