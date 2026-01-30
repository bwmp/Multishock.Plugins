<#
.SYNOPSIS
    Creates a new MultiShock plugin from templates.

.DESCRIPTION
    Interactive wizard that creates a fully-scaffolded plugin with:
    - Project file (.csproj)
    - Main plugin class (Plugin.cs)
    - Home page (HomePage.razor)
    - Example flow node (ExampleNode.cs)
    - README and CHANGELOG
    - Release-please integration

    Templates are stored in scripts/templates/ for easy maintenance.

.EXAMPLE
    .\new-plugin.ps1
    # Runs the interactive wizard

.EXAMPLE
    .\new-plugin.ps1 -Name "My Plugin" -Id "com.example.myplugin" -Description "Does cool things"
    # Creates plugin with specified parameters (no prompts)
#>
param(
    [string]$Name,
    [string]$Id,
    [string]$Description
)

$ErrorActionPreference = "Stop"

# Colors
function Write-Header { param($text) Write-Host "`n$text" -ForegroundColor Cyan }
function Write-Success { param($text) Write-Host $text -ForegroundColor Green }
function Write-Info { param($text) Write-Host $text -ForegroundColor Yellow }
function Write-Err { param($text) Write-Host $text -ForegroundColor Red }

# Resolve paths
$ScriptsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$PluginsRepoRoot = Split-Path -Parent $ScriptsDir
$RepoRoot = Join-Path $PluginsRepoRoot "Plugins"
$TemplatesDir = Join-Path $ScriptsDir "templates"

# Verify templates exist
if (-not (Test-Path $TemplatesDir)) {
    Write-Err "Templates directory not found: $TemplatesDir"
    exit 1
}

Write-Header "=== MultiShock Plugin Creator ==="
Write-Host "Templates: $TemplatesDir"
Write-Host ""

# ───────────────────────────────────────────────────────────────
# GATHER INPUT
# ───────────────────────────────────────────────────────────────

if (-not $Name) {
    $Name = Read-Host "Plugin display name (e.g. 'Twitch Integration')"
}
if ([string]::IsNullOrWhiteSpace($Name)) {
    Write-Err "Name is required."
    exit 1
}

if (-not $Id) {
    $defaultId = "com.multishock." + ($Name -replace '[^a-zA-Z0-9]', '').ToLower()
    $Id = Read-Host "Plugin ID [$defaultId]"
    if ([string]::IsNullOrWhiteSpace($Id)) { $Id = $defaultId }
}

if (-not $Description) {
    $Description = Read-Host "Short description"
    if ([string]::IsNullOrWhiteSpace($Description)) {
        $Description = "$Name plugin for MultiShock"
    }
}

# Derived values
$SafeName = ($Name -replace '[^a-zA-Z0-9]', '')
$PluginFolder = "${SafeName}"
$RouteName = $SafeName.ToLower()
$NormalizedId = $Id -replace '\.', '-'
$Date = Get-Date -Format "yyyy-MM-dd"
$PluginPath = Join-Path $RepoRoot $PluginFolder

# ───────────────────────────────────────────────────────────────
# CHECK FOR EXISTING PLUGIN
# ───────────────────────────────────────────────────────────────

if (Test-Path $PluginPath) {
    Write-Err "Plugin folder already exists: $PluginPath"
    exit 1
}

# ───────────────────────────────────────────────────────────────
# TEMPLATE PROCESSING
# ───────────────────────────────────────────────────────────────

function Expand-Template {
    param(
        [string]$TemplateFile,
        [string]$OutputFile
    )
    
    if (-not (Test-Path $TemplateFile)) {
        Write-Err "Template not found: $TemplateFile"
        return $false
    }
    
    $content = Get-Content $TemplateFile -Raw -Encoding UTF8
    
    # Replace all placeholders
    $content = $content -replace '\{\{PLUGIN_FOLDER\}\}', $PluginFolder
    $content = $content -replace '\{\{PLUGIN_ID\}\}', $Id
    $content = $content -replace '\{\{NORMALIZED_ID\}\}', $NormalizedId
    $content = $content -replace '\{\{DISPLAY_NAME\}\}', $Name
    $content = $content -replace '\{\{DESCRIPTION\}\}', $Description
    $content = $content -replace '\{\{SAFE_NAME\}\}', $SafeName
    $content = $content -replace '\{\{ROUTE_NAME\}\}', $RouteName
    $content = $content -replace '\{\{DATE\}\}', $Date
    
    # Ensure directory exists
    $dir = Split-Path -Parent $OutputFile
    if ($dir -and -not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    
    Set-Content -Path $OutputFile -Value $content -Encoding UTF8 -NoNewline
    return $true
}

# ───────────────────────────────────────────────────────────────
# CREATE PLUGIN STRUCTURE
# ───────────────────────────────────────────────────────────────

Write-Header "Creating plugin: $PluginFolder"

# Create directories
$directories = @(
    $PluginPath,
    (Join-Path $PluginPath "Nodes"),
    (Join-Path $PluginPath "Services"),
    (Join-Path $PluginPath "Components\Config"),
    (Join-Path $PluginPath "Generated")
)
foreach ($dir in $directories) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    Write-Info "  Created: $dir"
}

# Template mapping: template file -> output file
$templateMappings = @{
    "Plugin.csproj.template" = "$PluginFolder.csproj"
    "Plugin.cs.template" = "Plugin.cs"
    "_Imports.razor.template" = "_Imports.razor"
    "HomePage.razor.template" = "HomePage.razor"
    "CHANGELOG.md.template" = "CHANGELOG.md"
    "README.md.template" = "README.md"
}

# Process main templates
foreach ($mapping in $templateMappings.GetEnumerator()) {
    $templatePath = Join-Path $TemplatesDir $mapping.Key
    $outputPath = Join-Path $PluginPath $mapping.Value
    
    if (Expand-Template -TemplateFile $templatePath -OutputFile $outputPath) {
        Write-Success "  Created: $($mapping.Value)"
    } else {
        Write-Err "  Failed: $($mapping.Value)"
    }
}

# Process Nodes templates
$nodesTemplates = @{
    "EventNodeBase.cs.template" = "Nodes\$($SafeName)EventNodeBase.cs"
    "ExampleNode.cs.template" = "Nodes\ExampleTriggerNode.cs"
}
foreach ($mapping in $nodesTemplates.GetEnumerator()) {
    $templatePath = Join-Path $TemplatesDir $mapping.Key
    $outputPath = Join-Path $PluginPath $mapping.Value
    if (Expand-Template -TemplateFile $templatePath -OutputFile $outputPath) {
        Write-Success "  Created: $($mapping.Value)"
    } else {
        Write-Err "  Failed: $($mapping.Value)"
    }
}

# Process Services templates
$servicesTemplatePath = Join-Path $TemplatesDir "TriggerManager.cs.template"
$servicesOutputPath = Join-Path $PluginPath "Services\$($SafeName)TriggerManager.cs"
if (Expand-Template -TemplateFile $servicesTemplatePath -OutputFile $servicesOutputPath) {
    Write-Success "  Created: Services\$($SafeName)TriggerManager.cs"
} else {
    Write-Err "  Failed: Services\$($SafeName)TriggerManager.cs"
}

# Process Components templates
$configTemplatePath = Join-Path $TemplatesDir "PluginConfigComponent.razor.template"
$configOutputPath = Join-Path $PluginPath "Components\Config\PluginConfigComponent.razor"
if (Expand-Template -TemplateFile $configTemplatePath -OutputFile $configOutputPath) {
    Write-Success "  Created: Components\Config\PluginConfigComponent.razor"
} else {
    Write-Err "  Failed: Components\Config\PluginConfigComponent.razor"
}

# Create placeholder in Generated folder
$placeholderPath = Join-Path $PluginPath "Generated\.gitkeep"
"# Auto-generated files go here" | Set-Content $placeholderPath -Encoding UTF8

# ───────────────────────────────────────────────────────────────
# UPDATE RELEASE-PLEASE CONFIG
# ───────────────────────────────────────────────────────────────

Write-Header "Updating release-please configuration..."

$releasePleaseConfigPath = Join-Path $PluginsRepoRoot "release-please-config.json"
$manifestPath = Join-Path $PluginsRepoRoot ".release-please-manifest.json"

# Update release-please-config.json
if (Test-Path $releasePleaseConfigPath) {
    try {
        $config = Get-Content $releasePleaseConfigPath -Raw | ConvertFrom-Json
        
        # Add new package
        $newPackage = [PSCustomObject]@{
            "release-type" = "simple"
            "component" = "$PluginFolder"
            "changelog-path" = "CHANGELOG.md"
            "extra-files" = @(
                "$PluginFolder.csproj"
            )
        }
        
        # Add to packages
        if (-not $config.packages) {
            $config | Add-Member -NotePropertyName "packages" -NotePropertyValue ([PSCustomObject]@{}) -Force
        }
        $pluginPackagePath = "Plugins/$PluginFolder"
        $config.packages | Add-Member -NotePropertyName $pluginPackagePath -NotePropertyValue $newPackage -Force
        
        $config | ConvertTo-Json -Depth 10 | Set-Content $releasePleaseConfigPath -Encoding UTF8
        Write-Success "  Updated: release-please-config.json"
    }
    catch {
        Write-Err "  Failed to update release-please-config.json: $_"
    }
}
else {
    Write-Info "  Skipped: release-please-config.json (not found)"
}

# Update .release-please-manifest.json
if (Test-Path $manifestPath) {
    try {
        $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
        $pluginPackagePath = "Plugins/$PluginFolder"
        $manifest | Add-Member -NotePropertyName $pluginPackagePath -NotePropertyValue "1.0.0" -Force
        $manifest | ConvertTo-Json -Depth 10 | Set-Content $manifestPath -Encoding UTF8
        Write-Success "  Updated: .release-please-manifest.json"
    }
    catch {
        Write-Err "  Failed to update .release-please-manifest.json: $_"
    }
}
else {
    Write-Info "  Skipped: .release-please-manifest.json (not found)"
}

# ───────────────────────────────────────────────────────────────
# SUMMARY
# ───────────────────────────────────────────────────────────────

Write-Header "=== Plugin Created Successfully ==="
Write-Host ""
Write-Host "  Name:        " -NoNewline; Write-Success $Name
Write-Host "  ID:          " -NoNewline; Write-Success $Id
Write-Host "  Folder:      " -NoNewline; Write-Success $PluginFolder
Write-Host "  Route:       " -NoNewline; Write-Success "/plugins/$NormalizedId/$RouteName"
Write-Host "  Description: " -NoNewline; Write-Success $Description
Write-Host ""

Write-Header "Structure:"
Write-Host @"
  $PluginFolder/
  ├── $PluginFolder.csproj   # Project file
  ├── Plugin.cs              # Main plugin class
  ├── _Imports.razor         # Razor imports
  ├── HomePage.razor         # Main page
  ├── CHANGELOG.md           # Version history
  ├── README.md              # Documentation
  ├── Nodes/
  │   └── ExampleNode.cs     # Example flow node
  └── Generated/             # Build-time generated
"@

Write-Header "Next steps:"
Write-Host "  1. cd $PluginFolder"
Write-Host "  2. dotnet build"
Write-Host "  3. Customize Plugin.cs, HomePage.razor, and ExampleNode.cs"
Write-Host ""

Write-Header "Build commands:"
Write-Host "  .\scripts\build-plugins.ps1 -Plugin $PluginFolder"
Write-Host "  .\scripts\release-plugins.ps1 -Plugin $PluginFolder"
Write-Host ""
