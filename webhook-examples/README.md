# Jellyfin Webhook Examples

This folder contains real webhook examples organized by `NotificationType` to help understand the data structure and implement event handling for the JellyfinAnilistSync project.

📖 **[View the complete index →](./index.md)**

## Folder Structure

```
webhook-examples/
├── README.md                          # This file
├── UserDataSaved/                     # User interaction events
│   └── episode-marked-played.json     # Episode marked as watched
├── PlaybackStart/                     # Playback started events
├── PlaybackStop/                      # Playback stopped events  
├── ItemAdded/                         # New items added to library
└── [Other NotificationTypes]/         # Additional event types as discovered
```

## Current Examples

### UserDataSaved
- **episode-marked-played.json** - Episode marked as watched/unwatched
  - Series: "86 Eighty-Six"
  - Episode: S01E01 "Undertaker"
  - AniDB ID: 238801
  - Action: TogglePlayed

## Key Fields for AniList Sync

Each webhook contains these important fields:

| Field | Description | Example |
|-------|-------------|---------|
| `NotificationType` | Type of event | "UserDataSaved" |
| `ItemType` | Content type | "Episode", "Movie", "Series" |
| `SeriesName` | Anime title | "86 Eighty-Six" |
| `SeasonNumber` | Season number | 1 |
| `EpisodeNumber` | Episode number | 1 |
| `Provider_anidb` | AniDB ID | "238801" |
| `Played` | Watch status | true/false |
| `SaveReason` | Why saved | "TogglePlayed" |
| `UserId` | User identifier | "02700c8344d542619b39319296c0d4c7" |
| `NotificationUsername` | Username | "Rain" |

## Adding New Examples

When new webhook events are received:

1. Create appropriate subfolder if it doesn't exist: `mkdir [NotificationType]`
2. Save the JSON with descriptive filename: `[action-description].json`
3. Update this README with the new example

## Usage

These examples can be used for:
- Understanding Jellyfin webhook structure
- Testing webhook processing logic
- Implementing AniList synchronization
- Debugging webhook handling

## Webhook Server

The webhook server receives these at:
- **Endpoint**: `http://192.168.1.27:5001/`
- **Method**: POST
- **Content-Type**: text/plain;charset=utf-8
