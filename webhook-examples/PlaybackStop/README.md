# PlaybackStop Events

This folder contains webhook examples for when media playback stops.

## Key Fields for Playback Tracking
- `NotificationType`: "PlaybackStop"
- `ItemType`: Content type ("Episode", "Movie", etc.)
- `PlaybackPositionTicks`: Playback position in ticks
- `PlaybackPosition`: Human-readable position ("00:00:05")
- `PlayedToCompletion`: Whether episode was completed
- `DeviceName`: Device used for playback
- `ClientName`: Client application ("Jellyfin Web")

## Examples

### [`episode-playback-stopped.json`](./episode-playback-stopped.json)
**Episode playback stopped early**
- **Series**: "86 Eighty-Six" 
- **Episode**: S01E01 "Undertaker"
- **Position**: 00:00:05 (stopped after 5 seconds)
- **Completed**: false
- **Device**: Chrome browser
- **AniDB ID**: 238801

## Triggering This Event
Stop playing any media in Jellyfin before it completes.
