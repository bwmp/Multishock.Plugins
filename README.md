# MultiShock Plugins

Community and official plugins for MultiShock.

## Quick Start

### Create a New Plugin

```powershell
.\scripts\new-plugin.ps1
```

This will:
1. Prompt for plugin name, ID, and description
2. Create the plugin folder with all necessary files
3. Automatically configure release-please for the new plugin

### Build Plugins

```powershell
# Build all plugins
.\scripts\build-plugins.ps1

# Build specific plugin
.\scripts\build-plugins.ps1 -Plugin "TwitchPlugin"
```

### Create Release Archives

```powershell
.\scripts\release-plugins.ps1
```

## Plugin Structure

Each plugin follows this structure:

```
MyPlugin/
├── Plugin.cs              # Main plugin entry point
├── MyPlugin.csproj        # Project file
├── _Imports.razor         # Global Razor imports
├── HomePage.razor         # Plugin's main page
├── Nodes/                 # Custom flow nodes
│   └── ExampleNode.cs
├── Generated/             # Auto-generated (don't edit)
├── CHANGELOG.md           # Version history (managed by release-please)
└── README.md
```

## Releases

This repository uses [Release Please](https://github.com/googleapis/release-please) for automated versioning and releases.

### Conventional Commits

Use conventional commits to trigger releases:

- `feat: add new feature` → Minor version bump
- `fix: fix a bug` → Patch version bump
- `feat!: breaking change` → Major version bump

Scope your commits to the plugin:

```
feat(TwitchPlugin): add channel point redemptions
fix(DiscordPlugin): fix connection timeout
```

### Release Tags

Each plugin gets its own release tag: `plugin-name-plugin-v1.0.0`

## Installing Plugins

1. Download the plugin zip from [Releases](releases)
2. Extract to MultiShock's `Plugins` folder
3. Restart MultiShock

## Development

### Requirements

- .NET 10.0 SDK
- PowerShell 7+

### Building from Source

```powershell
git clone https://github.com/bwmp/Multishock.Plugins.git
cd Multishock.Plugins
.\scripts\build-plugins.ps1
```

## Contributing

1. Fork this repository
2. Create a new plugin using `.\scripts\new-plugin.ps1`
3. Develop your plugin
4. Submit a pull request

## License

See individual plugin folders for licensing information.
