# JellyfinAnilistSync

A .NET application that automatically syncs your Jellyfin anime watching progress to AniList.
This can be run has a cmd app or a windows service using NSSM.

## Table of Contents

- [Features](#features)
- [Installation](#installation)
- [Configuration](#configuration)
- [How It Works](#how-it-works)
- [Sonarr Integration (Optional)](#sonarr-integration-optional)
- [Running as a Service](#running-as-a-service)
- [File Structure](#file-structure)
- [Contributing](#contributing)
- [License](#license)

## Features

- 🎬 **Real-time sync** - Updates AniList when episodes are marked as watched/unwatched in Jellyfin
- 👥 **Multi-user support** - Configure different AniList accounts for each Jellyfin user
- 🔍 **Smart matching** - Finds anime by AniList provider ID or falls back to name search
- 📚 **Bulk sync** - Sync entire anime library on user login
- 📝 **Missing series tracking** - Tracks anime that couldn't be found on AniList for manual review
- ⚡ **Rate limiting** - Handles AniList API rate limits gracefully
- 🎯 **Sonarr integration** - Automatically refreshes Jellyfin when new episodes are imported
- 🔧 **Easy configuration** - JSON-based configuration with automatic setup

## Installation

### Prerequisites

- .NET 8.0 or later
- Jellyfin server with webhook plugin installed
- AniList account(s) with API access tokens
- (Optional) Sonarr instance for automatic library refreshes
- (Optional) FFmpeg and FFprobe for H.265 video conversion

### Setup

1. **Clone the repository**
   ```bash
   git clone https://github.com/AtaraxyState/JellyfinAnilistSync.git
   cd JellyfinAnilistSync
   ```

2. **Build the application**
   ```bash
   dotnet build
   ```

3. **Run for first time to generate config**
   ```bash
   dotnet run
   ```

4. **Configure the application**
   - Edit `%USERPROFILE%\Documents\JellyfinAnilistSync\config.json`
   - Add your Jellyfin server details and AniList tokens

5. **Set up Jellyfin webhooks**
   - Install the webhook plugin in Jellyfin
   - Configure webhooks to send to your application URL
   - Enable events: `UserDataSaved`, `AuthenticationSuccess`

6. **Install FFmpeg (for H.265 conversion)**
   - Download FFmpeg from [ffmpeg.org](https://ffmpeg.org/download.html)
   - Add FFmpeg and FFprobe to your system PATH
   - Verify installation: `ffmpeg -version` and `ffprobe -version`

## Configuration

### Configuration File Location

The application automatically determines the best location for the configuration file:

1. **Custom Path**: If `CONFIG_PATH` environment variable is set, uses that location
2. **User Documents**: `%USERPROFILE%\Documents\JellyfinAnilistSync\config.json` (default)
3. **USERPROFILE Fallback**: `%USERPROFILE%\Documents\JellyfinAnilistSync\config.json` (for services)
4. **Application Directory**: `[app-directory]\config\config.json` (last resort)

The application will create a default configuration file if none exists and log which location it's using.

### Configuration Structure

```json
{
  "jellyfin": {
    "serverUrl": "http://your-jellyfin-server:8096",
    "apiKey": "your-jellyfin-api-key"
  },
  "conversion": {
    "autoConvertToHEVC": false,
    "hevcPreset": "medium",
    "useGPUAcceleration": false,
    "gpuEncoder": "auto"
  },
  "aniList": {
    "globalToken": "fallback-anilist-token",
    "userTokens": {
      "JellyfinUsername1": "anilist-token-1",
      "JellyfinUsername2": "anilist-token-2"
    },
    "userAutoAdd": {
      "JellyfinUsername1": true,
      "JellyfinUsername2": false
    },
    "userBulkUpdate": {
      "JellyfinUsername1": true,
      "JellyfinUsername2": false
    }
  },
  "webhook": {
    "host": "localhost",
    "port": 5000
  },
  "sonarr": {
    "enabled": false,
    "refreshJellyfinOnImport": true,
    "apiKey": ""
  },
  "libraryNames": ["Animes", "Anime"]
}
```

### Getting AniList API Tokens

1. Go to [AniList Developer Settings](https://anilist.co/settings/developer)
2. Create a new API client
3. Generate access tokens for each user

### User Settings

#### Auto-Add Settings (`userAutoAdd`)

Each user can control whether anime should be automatically added to their AniList:

- `true` (default): Automatically adds anime to AniList when first encountered
- `false`: Only updates progress for anime already in the user's list

If auto-add is disabled and an anime isn't in the user's list, the sync will fail with a helpful error message.

#### Bulk Update Settings (`userBulkUpdate`)

Each user can control whether their entire library should be synced on login:

- `true`: Triggers full library sync when user logs into Jellyfin
- `false` (default): Only individual episode changes are synced

Bulk updates can be resource-intensive and take time, so they default to disabled.

### Environment Variables

The application supports these environment variables:

- `CONFIG_PATH`: Custom path to config.json file (useful for services)
- `ASPNETCORE_URLS`: Override webhook listening URLs (e.g., `http://0.0.0.0:5001`)

Examples:
```bash
# Set custom config location
set CONFIG_PATH=C:\MyConfigs\jellyfin-anilist-sync.json

# Override listening address
set ASPNETCORE_URLS=http://0.0.0.0:5001
```

### Getting Jellyfin API Key

1. Go to Jellyfin Admin Dashboard
2. Navigate to API Keys
3. Create a new API key for the sync application

## How It Works

1. **Episode Watched**: When you mark an episode as watched in Jellyfin, it updates your AniList progress
2. **Episode Unwatched**: When you unmark an episode, it syncs your overall series progress to AniList
3. **User Login**: When a configured user logs into Jellyfin, it performs a bulk sync of their entire anime library
4. **Smart Matching**: Uses AniList provider IDs from Jellyfin metadata, or falls back to name-based search
5. **Missing Series**: Tracks anime that couldn't be matched for manual review in `missing_anilist_series.json`

## H.265 Auto-Conversion (Optional)

The application can automatically convert H.264 video files to H.265 (HEVC) when new episodes are imported via Sonarr. This feature helps reduce storage space while maintaining video quality.

### Prerequisites

- **FFmpeg** installed and available in system PATH
- **FFprobe** for video codec detection
- **AutoConvertToHEVC** enabled in configuration

### How It Works

1. **Sonarr imports episode** → Sends webhook to service
2. **Service notifies Jellyfin** → Always refreshes series metadata to pick up new episodes
3. **Service detects H.264 file** → Uses FFprobe to check video codec
4. **Starts conversion** → Runs FFmpeg in background using H.265 preset (if H.264 detected)
5. **Monitors progress** → Tracks conversion status without blocking other operations
6. **Notifies Jellyfin again** → Refreshes series metadata when conversion completes (if conversion occurred)
7. **Cleans up** → Removes original H.264 file after successful conversion

### Configuration

Enable H.265 conversion in your `config.json`:

```json
{
  "conversion": {
    "autoConvertToHEVC": true,
    "hevcPreset": "medium",
    "useGPUAcceleration": false,
    "gpuEncoder": "auto"
  }
}
```

#### GPU Acceleration Options

The `useGPUAcceleration` setting enables hardware-accelerated encoding for faster conversions:

- **`useGPUAcceleration: false`** (default): Uses CPU encoding with `libx265`
- **`useGPUAcceleration: true`**: Uses GPU encoding for 3-10x faster conversion

The `gpuEncoder` setting specifies which GPU encoder to use:

- **`"auto"`** (default): Automatically detects and uses the best available GPU encoder
- **`"nvidia"`**: Forces NVIDIA NVENC encoder (requires NVIDIA GPU with NVENC support)
- **`"amd"`**: Forces AMD AMF encoder (requires AMD GPU with AMF support)
- **`"intel"`**: Forces Intel QSV encoder (requires Intel GPU with QSV support)

**Note**: GPU acceleration requires FFmpeg to be compiled with the appropriate hardware encoder support.

#### Configurable Preset Options

The `hevcPreset` setting controls the conversion speed vs. quality trade-off:

| Preset | Speed | Quality | File Size | Use Case |
|--------|-------|---------|-----------|----------|
| `ultrafast` | ⚡ Fastest | 📉 Lowest | 📦 Largest | Quick testing, low quality needed |
| `superfast` | 🏃 Very Fast | 📉 Low | 📦 Large | Real-time encoding |
| `veryfast` | 🏃 Fast | 📉 Fair | 📦 Large | Quick conversions |
| `faster` | 🏃‍♂️ Fast | ✅ Good | 📦 Medium-Large | Balanced speed/quality |
| `fast` | 🚶 Fast | ✅ Good | 📦 Medium | Good balance |
| `medium` | 🚶‍♂️ Medium | ✅ Good | 📦 Medium | **Default - Good balance** |
| `slow` | 🐌 Slow | ✅ High | 📦 Small | High quality |
| `slower` | 🐌 Slower | ✅ Very High | 📦 Smaller | Very high quality |
| `veryslow` | 🐌 Slowest | ✅ Highest | 📦 Smallest | Best quality, longest time |

### Conversion Settings

- **Codec**: H.265 (HEVC) using libx265
- **Preset**: Medium (balanced speed/quality)
- **Quality**: CRF 23 (good quality, reasonable file size)
- **Audio**: Copied without re-encoding
- **Output**: `{filename}_H265.{extension}`

### Monitoring Conversions

When enabled, the service provides status endpoints:

- **GET `/conversions`** - List all active conversions
- **GET `/conversions/{jobId}`** - Get status of specific conversion

### Conversion Logging

All conversion activities are automatically logged to `conversion.log` in the same directory as your `config.json` file. This log includes:

- 🎬 **Conversion start** - When a file begins conversion
- 🔄 **Progress updates** - FFmpeg execution and status
- ✅ **Successful completions** - When conversions finish successfully
- ❌ **Failed conversions** - Error details and reasons
- 🗑️ **File cleanup** - When original files are deleted
- 💥 **Unexpected errors** - Detailed error information

**Log file location**: Same directory as your `config.json` (typically `%USERPROFILE%\Documents\JellyfinAnilistSync\conversion.log`)

**Progress Logging Features**:
- **📊 Progress updates**: Logged every 2 minutes during conversion
- **Percentage calculation**: Based on current video time vs. estimated total duration
- **Time tracking**: Shows current progress time and estimated total duration
- **Smart estimation**: Uses FFprobe to get actual video duration when possible
- **Fallback duration**: Defaults to 20 minutes if duration cannot be determined

**Example log entries**:
```
[2024-01-15 14:30:15] 🔄 Notified Jellyfin of Sonarr import completed for series: Anime Series Name
[2024-01-15 14:30:16] 🎬 Starting H.265 conversion for: anime_episode_01.mkv (Job ID: abc123)
[2024-01-15 14:30:17] 🔄 Conversion started for: anime_episode_01.mkv (Job ID: abc123)
[2024-01-15 14:30:18] 🎬 Running FFmpeg conversion for: anime_episode_01.mkv with preset: medium
[2024-01-15 14:32:20] 📊 Progress: 25.3% complete (00:02:02 / ~00:08:00) for anime_episode_01.mkv
[2024-01-15 14:34:22] 📊 Progress: 50.1% complete (00:04:05 / ~00:08:00) for anime_episode_01.mkv
[2024-01-15 14:36:24] 📊 Progress: 75.2% complete (00:06:07 / ~00:08:00) for anime_episode_01.mkv
[2024-01-15 14:38:26] 📊 Progress: 100.0% complete for anime_episode_01.mkv
[2024-01-15 14:38:26] ✅ FFmpeg conversion completed successfully: anime_episode_01_H265.mkv
[2024-01-15 14:38:27] ✅ Conversion completed successfully: anime_episode_01.mkv -> anime_episode_01_H265.mkv (Duration: 00:08:10)
[2024-01-15 14:38:28] 🔄 Notified Jellyfin of conversion completion for series: Anime Series Name
[2024-01-15 14:38:29] 🗑️ Deleted original file after successful conversion: anime_episode_01.mkv
```

**Example log entries (no conversion needed)**:
```
[2024-01-15 14:30:15] 🔄 Notified Jellyfin of Sonarr import completed for series: Anime Series Name
[2024-01-15 14:30:16] 💤 File already H.265, no conversion needed: anime_episode_01.mkv
```

### Benefits

- **Storage savings**: H.265 typically reduces file size by 30-50%
- **Quality preservation**: Maintains visual quality while reducing bandwidth
- **Non-blocking**: Conversions run in background, don't affect other operations
- **Automatic cleanup**: Removes original files after successful conversion
- **Jellyfin integration**: Always refreshes metadata for new episodes, with additional refresh after conversion
- **Universal notification**: Jellyfin is notified regardless of whether conversion is needed or performed

## Sonarr Integration (Optional)

Sonarr integration enables automatic Jellyfin library refreshes when new episodes are imported, ensuring immediate availability without waiting for scheduled scans.

### Sonarr Configuration

1. **Enable in Config**:
   ```json
   {
     "sonarr": {
       "enabled": true,
       "refreshJellyfinOnImport": true,
       "apiKey": ""  // Optional for authentication /!\ not working yet /!\
     }
   }
   ```

2. **Configure Sonarr Webhook**:
   - Go to Sonarr Settings → Connect
   - Add new Webhook connection
   - **URL**: `http://your-service-host:5000/sonarr`
   - **Method**: POST
   - **Triggers**: Select "On Import/Upgrade"
   - **Authentication**: Leave empty (unless you set an API key)

### How It Works

1. **Sonarr imports episode** → Sends webhook to service
2. **Service receives webhook** → Extracts TVDB ID from series info
3. **Find matching series** → Searches Jellyfin for series with same TVDB ID  
4. **Trigger refresh** → Calls Jellyfin `/Items/{seriesId}/Refresh` API
5. **Jellyfin scans for new episode** → Episode appears immediately in library

### Benefits

- **Immediate availability**: New episodes appear instantly in Jellyfin
- **Reduced server load**: Only refreshes specific series, not entire library
- **Seamless workflow**: Sonarr → Jellyfin → AniList sync happens automatically

## Running as a Service

Using the .bat should work atm, it will add service, and then ask you for config variables, like API key, Users and adresses.

Else you can manually install the service following these steps:

```bash
# Build and publish
dotnet publish -c Release -o ./publish

# Install as service with NSSM (update paths to match your system)
nssm install JellyfinAnilistSync "C:\Program Files\dotnet\dotnet.exe" "C:\path\to\your\publish\JellyfinAnilistSync.dll"

# Set working directory to where config.json is located
nssm set JellyfinAnilistSync AppDirectory "C:\path\to\your\publish"

# Optional: Set startup type to automatic
nssm set JellyfinAnilistSync Start SERVICE_AUTO_START

# Start the service
nssm start JellyfinAnilistSync
```

The application will automatically find your configuration using the location priority described above.

### Service Configuration Options

```bash
# Optional: Override URL via environment variable
nssm set JellyfinAnilistSync AppEnvironmentExtra ASPNETCORE_URLS=http://0.0.0.0:5001

# Optional: Set custom config location (useful for services)
nssm set JellyfinAnilistSync AppEnvironmentExtra CONFIG_PATH="C:\custom\path\config.json"
```

### Troubleshooting Service Configuration

If the service can't find your configuration file, check the Windows Event Log or service logs. The application will log which configuration path it's using:

- ✅ `📁 Using Documents folder: C:\Users\[User]\Documents\JellyfinAnilistSync`
- ✅ `📁 Using USERPROFILE Documents: C:\Users\[User]\Documents\JellyfinAnilistSync`
- ⚠️ `📁 Using application directory fallback: D:\Path\To\App\config`

For services that can't access user profiles, you can:

1. **Use custom config path**: Set `CONFIG_PATH` environment variable
2. **Copy config to app directory**: Place `config.json` in the publish folder

### Service Logging

Configure NSSM to capture application logs for easier debugging:

```bash
# Create the log directory first
mkdir "C:\Logs\JellyfinAnilistSync"

# Set up log files (run as Administrator)
nssm set JellyfinAnilistSync AppStdout "C:\Logs\JellyfinAnilistSync\stdout.log"
nssm set JellyfinAnilistSync AppStderr "C:\Logs\JellyfinAnilistSync\stderr.log"
```

### Monitoring Logs

Monitor service logs in real-time using PowerShell:

```powershell
# Monitor stdout log (application output)
Get-Content "C:\Logs\JellyfinAnilistSync\stdout.log" -Wait -Tail 20

# Monitor stderr log (errors)
Get-Content "C:\Logs\JellyfinAnilistSync\stderr.log" -Wait -Tail 20

# Monitor both logs simultaneously (requires PowerShell 7+)
Get-Content "C:\Logs\JellyfinAnilistSync\stdout.log", "C:\Logs\JellyfinAnilistSync\stderr.log" -Wait -Tail 10
```

**Alternative**: The application also logs to Windows Event Log, viewable in Event Viewer under **Windows Logs → Application**.

### Service Management

```bash
# Check service status
nssm status JellyfinAnilistSync

# Stop the service
nssm stop JellyfinAnilistSync

# Remove the service
nssm remove JellyfinAnilistSync confirm
```

## File Structure

- `Program.cs` - Main application and webhook handlers
- `Configuration.cs` - Configuration management and missing series tracking
- `JellyfinClient.cs` - Jellyfin API client with sync logic
- `AnilistClient.cs` - AniList GraphQL API client
- `webhook-examples/` - Example webhook payloads for documentation

## Contributing

Contributions are welcome! Please feel free to submit issues and pull requests.

## License

This project is open source. See LICENSE file for details.
