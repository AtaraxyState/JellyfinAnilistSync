# JellyfinAnilistSync

A .NET application that automatically syncs your Jellyfin anime watching progress to AniList.

## Features

- 🎬 **Real-time sync** - Updates AniList when episodes are marked as watched/unwatched in Jellyfin
- 👥 **Multi-user support** - Configure different AniList accounts for each Jellyfin user
- 🔍 **Smart matching** - Finds anime by AniList provider ID or falls back to name search
- 📚 **Bulk sync** - Sync entire anime library on user login
- 📝 **Missing series tracking** - Tracks anime that couldn't be found on AniList for manual review
- ⚡ **Rate limiting** - Handles AniList API rate limits gracefully
- 🔧 **Easy configuration** - JSON-based configuration with automatic setup

## Installation

### Prerequisites

- .NET 8.0 or later
- Jellyfin server with webhook plugin installed
- AniList account(s) with API access tokens

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

## Configuration

The application creates a configuration file at `%USERPROFILE%\Documents\JellyfinAnilistSync\config.json`:

```json
{
  "jellyfin": {
    "serverUrl": "http://your-jellyfin-server:8096",
    "apiKey": "your-jellyfin-api-key"
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
    "port": 5000,
    "url": "http://localhost:5000"
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

## Running as a Service

You can run this as a Windows service using NSSM or similar tools:

```bash
# Build and publish
dotnet publish -c Release -o ./publish

# Install as service with NSSM
nssm install JellyfinAnilistSync "C:\path\to\dotnet.exe" "C:\path\to\publish\JellyfinAnilistSync.dll"
nssm start JellyfinAnilistSync
```

The application will read the host and port from your `config.json` file. If you need to override the configuration, you can still use environment variables:

```bash
# Optional: Override URL via environment variable
nssm set JellyfinAnilistSync AppEnvironmentExtra ASPNETCORE_URLS=http://0.0.0.0:5001
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
