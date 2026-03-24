# VRChatOSC Plugin

OSC send/receive integration for VRChat using FastOSC.

## Installation

1. Download the latest release from the [Releases](../../releases) page
2. Extract the zip to MultiShock's `Plugins` folder
3. Restart MultiShock

## Development

### Build from source

From repo root:
```powershell
# Debug build
.\scripts\build-plugins.ps1 -Plugin VRChatOSC -Configuration Debug

# Release build
.\scripts\build-plugins.ps1 -Plugin VRChatOSC -Configuration Release
```

### Create release archive

```powershell
.\scripts\release-plugins.ps1 -Plugin VRChatOSC
```

## Project Structure

```
VRChatOSC/
├── Plugin.cs              # Main plugin class (entry point)
├── VRChatOSC.csproj   # Project file
├── _Imports.razor         # Global Razor imports
├── HomePage.razor         # Main plugin page
├── Nodes/                 # Custom flow nodes
│   └── ExampleNode.cs     # Example flow node
├── Generated/             # Auto-generated (don't edit)
└── CHANGELOG.md           # Version history
```

## Plugin Info

| Property | Value |
|----------|-------|
| **ID** | `com.multishock.vrchatosc` |
| **Name** | VRChatOSC |
| **Version** | 1.0.0 |
| **Route** | /plugins/com-multishock-vrchatosc/vrchatosc |

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for version history.
