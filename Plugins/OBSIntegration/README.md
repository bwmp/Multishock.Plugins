# OBS Integration Plugin

Control OBS Studio through MultiShock using the OBS WebSocket protocol (v5.x).

## Features

### Action Nodes (for flow editor)
- **Set Scene** - Switch to a specific scene
- **Source Visibility** - Show, hide, or toggle a source in a scene
- **Filter Enabled** - Enable, disable, or toggle a filter on a source
- **Stream** - Start, stop, or toggle streaming
- **Record** - Start, stop, or toggle recording
- **Input Mute** - Mute, unmute, or toggle an audio input
- **Trigger Hotkey** - Trigger an OBS hotkey by its internal name

### Trigger Nodes (for flow editor)
- **Scene Changed** - Triggers when the current program scene changes
- **Source Visibility Changed** - Triggers when a source is shown/hidden
- **Stream State Changed** - Triggers when stream starts/stops
- **Record State Changed** - Triggers when recording starts/stops/pauses
- **Input Mute Changed** - Triggers when an audio input is muted/unmuted
- **Filter Enabled Changed** - Triggers when a filter is enabled/disabled

## Requirements

- OBS Studio 28.0 or later (includes OBS WebSocket v5)
- OBS WebSocket server enabled in OBS settings

## OBS Setup

1. Open OBS Studio
2. Go to **Tools** → **WebSocket Server Settings**
3. Check **"Enable WebSocket server"**
4. Set a password if desired (recommended)
5. Note the port number (default: 4455)
6. Click **Apply** or **OK**

## Installation

1. Download the latest release from the [Releases](../../releases) page
2. Extract the zip to MultiShock's `Plugins` folder
3. Restart MultiShock

## Configuration

In MultiShock, navigate to the OBS Integration page:

1. Enter the host (default: `localhost`)
2. Enter the port (default: `4455`)
3. Enter the password if you set one in OBS
4. Click **Connect**

Enable **Auto-connect on startup** to automatically connect when MultiShock launches.

## Example Use Cases

- Toggle an overlay source when a Twitch event occurs
- Switch scenes based on channel point redemptions
- Mute/unmute sources based on external triggers
- Start/stop recording when certain conditions are met

## Development

### Build from source

From repo root:
```powershell
# Debug build
.\scripts\build-plugins.ps1 -Plugin OBSIntegration -Configuration Debug

# Release build
.\scripts\build-plugins.ps1 -Plugin OBSIntegration -Configuration Release
```

### Create release archive

```powershell
.\scripts\release-plugins.ps1 -Plugin OBSIntegration
```

## Project Structure

```
OBSIntegration/
├── Plugin.cs              # Main plugin class (entry point)
├── OBSIntegration.csproj  # Project file
├── _Imports.razor         # Global Razor imports
├── HomePage.razor         # Main plugin page
├── Models/                # OBS WebSocket message models
├── Services/              # OBS WebSocket service and config
├── Nodes/                 # Flow action and trigger nodes
├── Generated/             # Auto-generated (don't edit)
└── CHANGELOG.md           # Version history
```

## Plugin Info

| Property | Value |
|----------|-------|
| **ID** | `com.multishock.obsintegration` |
| **Name** | OBS Integration |
| **Version** | 1.0.0 |
| **Route** | /plugins/com-multishock-obsintegration/obsintegration |

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for version history.
