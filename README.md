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
# Build and test everything
dotnet build MultiShock.Plugins.slnx
dotnet test MultiShock.Plugins.slnx

# Build a single plugin
dotnet build Plugins/TwitchIntegration/TwitchIntegration.csproj
```

Every plugin build drops a fresh `<PluginName>.msplugin` into your local MultiShock plugin folder
(`%LOCALAPPDATA%\PiShock\MultiShock\Plugins` on Windows), so restarting MultiShock picks it up.
Pass `-p:SkipPluginPackaging=true` to skip that, or `-p:PluginPackageDir=<dir>` to put packages elsewhere.

`.\scripts\build-plugins.ps1 [-Plugin TwitchIntegration]` wraps the same build with a per-plugin summary.

### Create Release Archives

```powershell
.\scripts\release-plugins.ps1
```

## Repository Layout

```
MultiShock.Plugins.slnx      # Opens every plugin and test project
Directory.Build.props        # Shared settings: SDK version, TFM, package references
Directory.Build.targets      # Shared targets: build stamp, .msplugin staging and packaging
Directory.Packages.props     # Pinned NuGet versions for every package used by any plugin
Plugins/<PluginName>/        # One folder per plugin
Tests/<PluginName>.Tests/    # xunit projects, one per plugin that has tests
scripts/                     # new-plugin / build / release helpers
```

The PluginSdk version every plugin builds against lives in one place: `PluginSdkVersion` in
`Directory.Build.props`. Bump it there to move all plugins at once.

## Plugin Structure

Each plugin follows this structure:

```
MyPlugin/
├── MyPlugin.csproj        # Only the version line plus plugin-specific packages
├── plugin.json            # Manifest: id, name, description, platforms
├── Plugin.cs              # Main plugin entry point
├── _Imports.razor         # Global Razor imports
├── HomePage.razor         # Plugin's main page
├── Nodes/                 # Custom flow nodes
│   └── ExampleNode.cs
├── Generated/             # Auto-generated (don't edit)
├── CHANGELOG.md           # Version history (managed by release-please)
└── README.md
```

Everything else (target framework, Razor settings, SDK reference, build stamp, packaging) comes from
the shared `Directory.Build.*` files. A csproj only needs to add what is specific to that plugin:

- Extra `<PackageReference Include="..." />` lines (versions go in `Directory.Packages.props`).
- A `<TargetFramework>` override when the plugin needs Windows APIs.
- `<PluginPackageFile Include="..." PackagePath="..." />` items for files that must ship in the
  package but are not part of the normal build output.

## Releases

This repository uses [Release Please](https://github.com/googleapis/release-please) for automated versioning and releases.

### Conventional Commits

Use conventional commits to trigger releases:

- `feat: add new feature` → Minor version bump
- `fix: fix a bug` → Patch version bump
- `feat!: breaking change` → Major version bump

Scope your commits to the plugin:

```
feat(TwitchIntegration): add channel point redemptions
fix(OBSIntegration): fix connection timeout
```

### Release Tags

Each plugin gets its own release tag in the form `<PluginName>-v<version>`, for example `TwitchIntegration-v1.3.0`.

## Installing Plugins

The recommended installation method is **Settings → Plugins → Plugin Repository** in MultiShock.
It checks this repository's published release metadata, verifies package checksums, and also
notifies you when installed plugins have updates.

For manual installation:

1. Download a `.msplugin` asset from the `plugin-catalog` release
2. Copy it into MultiShock's `Plugins` folder
3. Restart MultiShock

Every push that changes a plugin publishes self-contained `.msplugin` packages and a
`plugin-index.json` to the stable `plugin-catalog` GitHub release. The catalog includes
plugin IDs, versions, compatibility metadata, download URLs, file sizes, and SHA-256 hashes.

## Development

### Requirements

- .NET 10.0 SDK
- PowerShell 7+

### Building from Source

```powershell
git clone https://github.com/bwmp/Multishock.Plugins.git
cd Multishock.Plugins
dotnet build MultiShock.Plugins.slnx
```

## Contributing

1. Fork this repository
2. Create a new plugin using `.\scripts\new-plugin.ps1`
3. Develop your plugin
4. Submit a pull request

## License

See individual plugin folders for licensing information.
