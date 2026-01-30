# Throne Integration Plugin

A simple multishock integration to add flow trigger nodes that activate when a throne gift is purchased

## Installation

1. Download the latest release from the [Releases](../../releases) page
2. Extract the zip to MultiShock's `Plugins` folder
3. Restart MultiShock

## Development

### Build from source

From repo root:
```powershell
# Debug build
.\scripts\build-plugins.ps1 -Plugin ThroneIntegration -Configuration Debug

# Release build
.\scripts\build-plugins.ps1 -Plugin ThroneIntegration -Configuration Release
```

### Create release archive

```powershell
.\scripts\release-plugins.ps1 -Plugin ThroneIntegration
```

## Project Structure

```
ThroneIntegration/
├── Plugin.cs              # Main plugin class (entry point)
├── ThroneIntegration.csproj   # Project file
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
| **ID** | `com.multishock.throneintegration` |
| **Name** | Throne Integration |
| **Version** | 1.0.0 |
| **Route** | /plugins/com-multishock-throneintegration/throneintegration |

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for version history.
