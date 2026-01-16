# Image Detection Plugin

Image detection Module

## Installation

1. Download the latest release from the [Releases](../../releases) page
2. Extract the zip to MultiShock's `Plugins` folder
3. Restart MultiShock

## Development

### Build from source

From repo root:
```powershell
# Debug build
.\scripts\build-plugins.ps1 -Plugin ImageDetection -Configuration Debug

# Release build
.\scripts\build-plugins.ps1 -Plugin ImageDetection -Configuration Release
```

### Create release archive

```powershell
.\scripts\release-plugins.ps1 -Plugin ImageDetection
```

## Project Structure

```
ImageDetection/
├── Plugin.cs              # Main plugin class (entry point)
├── ImageDetection.csproj   # Project file
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
| **ID** | `com.multishock.imagedetection` |
| **Name** | Image Detection |
| **Version** | 1.0.0 |
| **Route** | /plugins/com-multishock-imagedetection/imagedetection |

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for version history.
