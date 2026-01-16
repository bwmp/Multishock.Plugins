# MultiShock Plugin Scripts

Scripts for creating, building, and managing MultiShock plugins.

## Quick Start

```powershell
# From repo root:
.\scripts\new-plugin.ps1
```

## Available Scripts

| Script | Description |
|--------|-------------|
| `new-plugin.ps1` | Interactive wizard to create a new plugin project |
| `build-plugins.ps1` | Build all plugins (or a specific one) |
| `release-plugins.ps1` | Create release archives for distribution |
| `list-plugins.ps1` | List all plugins with their status |

## Usage Examples

### Create a New Plugin

```powershell
# Interactive mode - prompts for name, ID, and description
.\scripts\new-plugin.ps1

# With parameters
.\scripts\new-plugin.ps1 -Name "TwitchIntegration" -Id "com.yourname.twitch" -Description "Twitch chat integration"
```

This will:
1. Create the plugin folder with all necessary files
2. Add the plugin to `release-please-config.json`
3. Add the plugin to `.release-please-manifest.json`

### Build Plugins

```powershell
# Build all plugins (Release)
.\scripts\build-plugins.ps1

# Build specific plugin
.\scripts\build-plugins.ps1 -Plugin "TwitchIntegration"

# Build in Debug mode
.\scripts\build-plugins.ps1 -Configuration Debug
```

### Create Release Archives

```powershell
# Release all plugins
.\scripts\release-plugins.ps1

# Release specific plugin
.\scripts\release-plugins.ps1 -Plugin "TwitchIntegration"
```

Release archives are created in the `releases/` folder at the repo root.

### List Plugins

```powershell
.\scripts\list-plugins.ps1
```

Shows all plugins with their ID, version, description, and build status.

## GitHub Actions

The repository includes automated workflows:

| Workflow | Trigger | Description |
|----------|---------|-------------|
| `build.yml` | Push/PR to main | Builds all changed plugins |
| `release-please.yml` | Push to main | Creates release PRs and publishes releases |
| `manual-release.yml` | Manual | Manually trigger a release for any plugin |

## Release Please Integration

When you create a new plugin, it's automatically added to the release-please configuration. Each plugin has:
- Its own `CHANGELOG.md` managed by release-please
- Unique release tags: `plugin-name-plugin-v1.0.0`
- Separate version tracking in `.release-please-manifest.json`
