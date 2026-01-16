<# 
    MultiShock Build All Plugins Script
    Builds all plugin projects found in the workspace
    
    Usage: .\build-plugins.ps1 [-Configuration "Debug"|"Release"] [-Plugin "PluginName"]
#>

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$Plugin = ""
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " MultiShock Plugin Builder" -ForegroundColor Cyan
Write-Host "" -ForegroundColor Cyan
Write-Host " Configuration: $($Configuration)" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""

# Get the repo root (parent of scripts folder, then Plugins subfolder)
$ScriptsDir = $PSScriptRoot
$PluginsRepoRoot = Split-Path -Parent $ScriptsDir
$RepoRoot = Join-Path $PluginsRepoRoot "Plugins"

$BuiltPlugins = @()
$FailedPlugins = @()

# Find all plugin projects (directories ending in "Plugin" with a .csproj file)
$PluginFolders = Get-ChildItem -Path $RepoRoot -Directory | Where-Object {
    $_.Name -like "*Plugin" -and
    $_.Name -ne "PluginTemplate" -and
    (Test-Path (Join-Path $_.FullName "$($_.Name).csproj"))
}

if ($Plugin) {
    # Filter to specific plugin
    $PluginFolders = $PluginFolders | Where-Object { $_.Name -eq $Plugin -or $_.Name -eq "${Plugin}Plugin" }
    if ($PluginFolders.Count -eq 0) {
        Write-Host "Error: Plugin '$Plugin' not found!" -ForegroundColor Red
        Write-Host "Available plugins:" -ForegroundColor Yellow
        Get-ChildItem -Path $RepoRoot -Directory | Where-Object {
            $_.Name -like "*Plugin" -and
            $_.Name -ne "PluginTemplate" -and
            (Test-Path (Join-Path $_.FullName "$($_.Name).csproj"))
        } | ForEach-Object {
            Write-Host "  - $($_.Name)" -ForegroundColor Gray
        }
        exit 1
    }
}

if ($PluginFolders.Count -eq 0) {
    Write-Host "No plugins found to build." -ForegroundColor Yellow
    Write-Host "Use .\scripts\new-plugin.ps1 to create a new plugin." -ForegroundColor Gray
    exit 0
}

Write-Host "Found $($PluginFolders.Count) plugin(s) to build:" -ForegroundColor White
$PluginFolders | ForEach-Object {
    Write-Host "  - $($_.Name)" -ForegroundColor Gray
}
Write-Host ""

$i = 0
foreach ($folder in $PluginFolders) {
    $i++
    $pluginName = $folder.Name
    $csprojPath = Join-Path $folder.FullName "$pluginName.csproj"
    
    Write-Host "[$i/$($PluginFolders.Count)] Building $pluginName..." -ForegroundColor Yellow
    
    Push-Location $folder.FullName
    try {
        dotnet build $csprojPath -c $Configuration --nologo -v q
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  OK $pluginName built successfully" -ForegroundColor Green
            $BuiltPlugins += $pluginName
        } else {
            Write-Host "  FAIL $pluginName build failed" -ForegroundColor Red
            $FailedPlugins += $pluginName
        }
    } catch {
        Write-Host "  FAIL $pluginName build error: $_" -ForegroundColor Red
        $FailedPlugins += $pluginName
    } finally {
        Pop-Location
    }
}

# Summary
Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "                     Build Summary" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""

if ($BuiltPlugins.Count -gt 0) {
    Write-Host "  Successful ($($BuiltPlugins.Count)):" -ForegroundColor Green
    $BuiltPlugins | ForEach-Object {
        Write-Host "    OK $_" -ForegroundColor Green
    }
}

if ($FailedPlugins.Count -gt 0) {
    Write-Host ""
    Write-Host "  Failed ($($FailedPlugins.Count)):" -ForegroundColor Red
    $FailedPlugins | ForEach-Object {
        Write-Host "    FAIL $_" -ForegroundColor Red
    }
    Write-Host ""
    exit 1
}

Write-Host ""
Write-Host "All plugins built successfully!" -ForegroundColor Green
Write-Host ""
