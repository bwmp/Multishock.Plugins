<# 
    MultiShock List Plugins Script
    Lists all plugin projects in the workspace
    
    Usage: .\list-plugins.ps1
#>

# Get the repo root (parent of scripts folder, then Plugins subfolder)
$ScriptsDir = $PSScriptRoot
$PluginsRepoRoot = Split-Path -Parent $ScriptsDir
$RepoRoot = Join-Path $PluginsRepoRoot "Plugins"

Write-Host ""
Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║                MultiShock Plugin List                      ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Find all plugin projects
$PluginFolders = Get-ChildItem -Path $RepoRoot -Directory | Where-Object {
    $_.Name -like "*Plugin" -and
    (Test-Path (Join-Path $_.FullName "$($_.Name).csproj"))
}

if ($PluginFolders.Count -eq 0) {
    Write-Host "  No plugins found." -ForegroundColor Yellow
    Write-Host "  Use .\scripts\new-plugin.ps1 to create a new plugin." -ForegroundColor Gray
    Write-Host ""
    exit 0
}

# Group into template and actual plugins
$Template = $PluginFolders | Where-Object { $_.Name -eq "PluginTemplate" }
$Plugins = $PluginFolders | Where-Object { $_.Name -ne "PluginTemplate" }

if ($Template) {
    Write-Host "  Template:" -ForegroundColor Cyan
    Write-Host "    📋 PluginTemplate (use as reference or .\scripts\new-plugin.ps1)" -ForegroundColor Gray
    Write-Host ""
}

if ($Plugins.Count -gt 0) {
    Write-Host "  Plugins ($($Plugins.Count)):" -ForegroundColor White
    
    foreach ($folder in $Plugins) {
        $pluginName = $folder.Name
        $csprojPath = Join-Path $folder.FullName "$pluginName.csproj"
        $pluginCs = Join-Path $folder.FullName "Plugin.cs"
        
        # Try to read metadata from Plugin.cs
        $id = "unknown"
        $description = ""
        
        if (Test-Path $pluginCs) {
            $content = Get-Content $pluginCs -Raw
            
            $idMatch = [regex]::Match($content, 'Id\s*=>\s*"([^"]+)"')
            if ($idMatch.Success) { $id = $idMatch.Groups[1].Value }
            
            $descMatch = [regex]::Match($content, 'Description\s*=>\s*"([^"]+)"')
            if ($descMatch.Success) { $description = $descMatch.Groups[1].Value }
        }
        
        # Read version from manifest or csproj
        $version = "1.0.0"
        $manifestPath = Join-Path $PluginsRepoRoot ".release-please-manifest.json"
        if (Test-Path $manifestPath) {
            $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
            $pluginPackagePath = "Plugins/$pluginName"
            if ($manifest.$pluginPackagePath) {
                $version = $manifest.$pluginPackagePath
            }
        }
        
        # Check if built
        $dllPath = Join-Path $folder.FullName "bin\Release\net10.0\$pluginName.dll"
        $builtIndicator = if (Test-Path $dllPath) { "✓" } else { "○" }
        $builtColor = if (Test-Path $dllPath) { "Green" } else { "DarkGray" }
        
        Write-Host "    $builtIndicator " -ForegroundColor $builtColor -NoNewline
        Write-Host "$pluginName" -ForegroundColor White -NoNewline
        Write-Host " v$version" -ForegroundColor Gray
        Write-Host "      ID: $id" -ForegroundColor DarkGray
        if ($description) {
            Write-Host "      $description" -ForegroundColor DarkGray
        }
    }
}

Write-Host ""
Write-Host "  Legend: ✓ Built  ○ Not built" -ForegroundColor DarkGray
Write-Host ""
Write-Host "  Commands (run from repo root):" -ForegroundColor Cyan
Write-Host "    .\scripts\new-plugin.ps1        Create a new plugin" -ForegroundColor Gray
Write-Host "    .\scripts\build-plugins.ps1     Build all plugins" -ForegroundColor Gray
Write-Host "    .\scripts\release-plugins.ps1   Create release archives" -ForegroundColor Gray
Write-Host ""
