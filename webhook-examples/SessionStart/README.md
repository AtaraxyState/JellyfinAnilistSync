# SessionStart Events

This folder contains webhook examples for when a new user session starts (client connects to Jellyfin).

## Key Fields for Session Tracking
- `NotificationType`: "SessionStart"
- `Id`: Unique session identifier
- `UserId`: User who started the session
- `NotificationUsername`: Username of the session owner
- `Client`: Client application ("Android TV", "Jellyfin Web", etc.)
- `DeviceName`: Human-readable device name
- `DeviceId`: Unique device identifier
- `RemoteEndPoint`: IP address of the client device

## Examples

### [`fire-tv-session.json`](./fire-tv-session.json)
**Fire TV session started**
- **Username**: "Rain"
- **Client**: "Android TV"
- **Device**: "Fire TV - Timothée"
- **IP**: 192.168.1.34 (local network)
- **Session ID**: 0b5422a1f77b5ead8a0a8c2358edd59c

## Use Cases for AniList Sync
Session events can be useful for:
- Multi-device session management
- User activity tracking across different clients
- Understanding viewing patterns (web vs TV vs mobile)
- Device-specific sync preferences

## Triggering This Event
Connect to Jellyfin from any client application (web browser, mobile app, TV app, etc.).
