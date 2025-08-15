# ItemAdded Events

This folder contains webhook examples for when new items are added to the Jellyfin library.

## Expected Fields
- `NotificationType`: "ItemAdded"
- `ItemType`: "Episode", "Movie", "Series", etc.
- Library information
- Media metadata
- Series/season details (for episodes)

## Examples
*Examples will be added when ItemAdded webhooks are received*

To trigger this event: Add new media files to your Jellyfin library and run a library scan.
