# Random Nodes Plugin

A grab bag of random utility nodes for MultiShock.

## Nodes

### Media

- **Spotify Now Playing** - reads the local Spotify/Windows media session and outputs song name, artist, album, pause/play status, current playtime, length, progress percent, source app, and any error.
- **Active Media Session** - reads the currently active Windows media session for any local player.

### Utility

- **Random Number** - outputs a random decimal and rounded integer between min/max.
- **Random Choice** - chooses one item from a comma- or line-separated list.
- **Chance Gate** - routes flow through success/failure by percentage chance.
- **Text Contains** - branches based on a case-insensitive contains check.
- **Time Now** - outputs the current time string, local time, UTC time, Unix seconds, and day of week.
- **Format Time** - converts seconds to `mm:ss` or a Unix timestamp to `hh:mm AM/PM` using local, UTC, or a manual UTC offset.
- **Random Delay** - waits a random number of milliseconds before continuing.

The Spotify/media nodes use Windows' Global System Media Transport Controls API, so they work best when the local player is running and publishing media controls.

## Installation

1. Download the latest release from the [Releases](../../releases) page
2. Extract the zip to MultiShock's `Plugins` folder
3. Restart MultiShock

## Development

### Build from source

From repo root:
```powershell
# Debug build
.\scripts\build-plugins.ps1 -Plugin RandomNodes -Configuration Debug

# Release build
.\scripts\build-plugins.ps1 -Plugin RandomNodes -Configuration Release
```

### Create release archive

```powershell
.\scripts\release-plugins.ps1 -Plugin RandomNodes
```

## Project Structure

```
RandomNodes/
├── Plugin.cs              # Main plugin class (entry point)
├── RandomNodes.csproj   # Project file
├── _Imports.razor         # Global Razor imports
├── HomePage.razor         # Main plugin page
├── Nodes/                 # Custom flow nodes
│   ├── GetSpotifyNowPlayingNode.cs
│   ├── GetActiveMediaSessionNode.cs
│   └── Utility nodes
├── Generated/             # Auto-generated (don't edit)
└── CHANGELOG.md           # Version history
```

## Plugin Info

| Property | Value |
|----------|-------|
| **ID** | `com.multishock.randomnodes` |
| **Name** | Random Nodes |
| **Version** | 1.0.0 |
| **Route** | /plugins/com-multishock-randomnodes/randomnodes |

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for version history.
