# Jellyfin Webhook Examples Index

This is the central index for all webhook examples collected from Jellyfin. Each webhook type is organized by `NotificationType` with real examples and documentation.

## 📁 Available Webhook Types

### 🟢 [UserDataSaved](./UserDataSaved/)
Triggered when user interaction data is saved (e.g., marking as played/unplayed, rating, etc.)

**Examples Available:**
- [`episode-marked-played.json`](./UserDataSaved/episode-marked-played.json) - Episode marked as watched/unwatched

### 🟡 [PlaybackStart](./PlaybackStart/)
Triggered when media playback begins

**Examples Available:**
- *Waiting for examples - trigger by starting media playback*

### 🟢 [PlaybackStop](./PlaybackStop/)  
Triggered when media playback ends or is stopped

**Examples Available:**
- [`episode-playback-stopped.json`](./PlaybackStop/episode-playback-stopped.json) - Episode stopped after 5 seconds

### 🟡 [ItemAdded](./ItemAdded/)
Triggered when new items are added to the Jellyfin library

**Examples Available:**
- *Waiting for examples - trigger by adding new media and scanning library*

### 🟢 [AuthenticationSuccess](./AuthenticationSuccess/)
Triggered when a user successfully logs into Jellyfin

**Examples Available:**
- [`user-login.json`](./AuthenticationSuccess/user-login.json) - User login event

### 🟢 [SessionStart](./SessionStart/)
Triggered when a new client session starts

**Examples Available:**
- [`fire-tv-session.json`](./SessionStart/fire-tv-session.json) - Fire TV client connected

## 🔑 Key Fields for AniList Sync

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

## 🚀 How to Add New Examples

When new webhook events are received:

1. **Identify** the `NotificationType` from the JSON
2. **Navigate** to the appropriate folder (create if needed)
3. **Save** the webhook as `[descriptive-name].json`
4. **Update** the folder's README.md
5. **Update** this index with the new example

## ⚙️ Webhook Server Configuration

- **Endpoint**: `http://192.168.1.27:5001/`
- **Method**: POST  
- **Content-Type**: text/plain;charset=utf-8
- **Status**: ✅ Active and receiving webhooks

## 🧪 Testing

```bash
# Test webhook endpoint
curl -X POST http://localhost:5001/webhook -H "Content-Type: application/json" -d '{"test": "data"}'

# Check server status  
netstat -ano | findstr :5001
```

---

*This index is automatically updated as new webhook types are discovered and documented.*
