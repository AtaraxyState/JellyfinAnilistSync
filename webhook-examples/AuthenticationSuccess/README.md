# AuthenticationSuccess Events

This folder contains webhook examples for successful user authentication/login events.

## Key Fields for Authentication Tracking
- `NotificationType`: "AuthenticationSuccess"
- `NotificationUsername`: Username that logged in
- `UserId`: Unique user identifier
- `LastLoginDate`: Timestamp of successful login
- `Client`: Client application used
- `DeviceName`: Device used for login
- `DeviceId`: Unique device identifier
- `RemoteEndPoint`: IP address of login attempt

## Examples

### [`user-login.json`](./user-login.json)
**User successful login**
- **Username**: "Rain"
- **Client**: "Jellyfin Web"
- **Device**: Chrome browser
- **IP**: 127.0.0.1 (localhost)
- **Login Time**: 2025-08-15T21:20:24.6586566Z

## Use Cases for AniList Sync
While authentication events may not directly relate to anime tracking, they could be useful for:
- User session management
- Security logging
- Multi-user environment tracking
- Determining when users are active

## Triggering This Event
Log into Jellyfin with any user account.
