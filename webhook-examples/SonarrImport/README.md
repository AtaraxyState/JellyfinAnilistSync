# Sonarr Import Complete Webhook

This webhook is triggered when Sonarr successfully imports/downloads a new episode.

## Configuration

### Sonarr Setup
1. Go to Sonarr Settings → Connect
2. Add new Webhook connection:
   - **Name**: JellyfinAnilistSync
   - **URL**: `http://your-service-host:5001/sonarr`
   - **Method**: POST
   - **Triggers**: Select "On Import/Upgrade"
   - **API Key**: (Optional) Your configured API key from config.json

### Service Configuration
```json
{
  "Sonarr": {
    "Enabled": true,
    "RefreshJellyfinOnImport": true,
    "ApiKey": "your-optional-api-key"
  }
}
```

## Webhook Payload

### Key Fields
- `eventType`: "Download" or "ImportComplete"
- `series.tvdbId`: Used to find matching series in Jellyfin
- `series.title`: Series name for logging
- `episodes[]`: Array of imported episodes with season/episode numbers

### What Happens
1. **Sonarr downloads episode** → Sends webhook to `/sonarr`
2. **Service receives webhook** → Extracts TVDB ID (453127)
3. **Find series in Jellyfin** → Searches for series with matching TVDB ID
4. **Trigger Jellyfin refresh** → Calls `/Items/{seriesId}/Refresh`
5. **Jellyfin scans for new episode** → Updates library automatically

### Example Log Output
```
📺 Sonarr Event Type: Download
📥 Processing Sonarr ImportComplete/Download event
   📺 Series: WITCH WATCH (Sonarr ID: 133)
   🆔 TVDB ID: 453127
   📋 Episodes imported:
      • S01E20 - The Lost Wolf and the Stray Cat (WEBDL-1080p)
🔄 Refreshing Jellyfin series with TVDB ID 453127
🔍 Searching for series with TVDB ID: 453127
✅ Found series: WITCH WATCH (ID: abc123)
🔄 Refreshing series metadata: abc123
✅ Successfully triggered Jellyfin refresh for WITCH WATCH
```

## Benefits
- **Immediate availability**: New episodes appear in Jellyfin without waiting for scheduled library scans
- **Automated workflow**: Sonarr → Jellyfin → AniList sync happens automatically
- **Reduced system load**: Only refreshes specific series instead of entire library
