<# 
    MultiShock Release All Plugins Script
    Creates release builds and archives for all plugins
    
    Usage: .\release-plugins.ps1 [-Plugin "PluginName"]
#>

param(
    [string]$Plugin = ""
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Magenta
Write-Host "║            MultiShock Plugin Release Builder               ║" -ForegroundColor Magenta
Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Magenta
Write-Host ""

# Get the repo root (parent of scripts folder)
$ScriptsDir = $PSScriptRoot
$RepoRoot = Split-Path -Parent $ScriptsDir

$ReleaseOutputDir = Join-Path $RepoRoot "releases"

# Create release output directory
New-Item -ItemType Directory -Force -Path $ReleaseOutputDir | Out-Null

# Find all plugin projects
$PluginFolders = Get-ChildItem -Path $RepoRoot -Directory | Where-Object {
    $_.Name -like "*Plugin" -and
    $_.Name -ne "PluginTemplate" -and
    (Test-Path (Join-Path $_.FullName "$($_.Name).csproj"))
}

if ($Plugin) {
    $PluginFolders = $PluginFolders | Where-Object { $_.Name -eq $Plugin -or $_.Name -eq "${Plugin}Plugin" }
    if ($PluginFolders.Count -eq 0) {
        Write-Host "Error: Plugin '$Plugin' not found!" -ForegroundColor Red
        exit 1
    }
}

if ($PluginFolders.Count -eq 0) {
    Write-Host "No plugins found to release." -ForegroundColor Yellow
    exit 0
}

Write-Host "Creating release builds for $($PluginFolders.Count) plugin(s)..." -ForegroundColor White
Write-Host ""

$ReleasedPlugins = @()
$FailedPlugins = @()

foreach ($folder in $PluginFolders) {
    $pluginName = $folder.Name
    $csprojPath = Join-Path $folder.FullName "$pluginName.csproj"
    
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray
    Write-Host "  Building: $pluginName" -ForegroundColor Cyan
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray
    
    try {
        # Read version from manifest or csproj
        $version = "1.0.0"
        $manifestPath = Join-Path $RepoRoot ".release-please-manifest.json"
        if (Test-Path $manifestPath) {
            $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
            if ($manifest.$pluginName) {
                $version = $manifest.$pluginName
            }
        }
        
        Write-Host "  Version: $version" -ForegroundColor Gray
        
        # Build
        Write-Host "  → Building..." -ForegroundColor Yellow
        dotnet build $csprojPath -c Release --nologo -v q
        
        if ($LASTEXITCODE -ne 0) { throw "Build failed" }
        
        # Copy files
        $buildOutput = Join-Path $folder.FullName "bin\Release\net10.0"
        $tempDir = Join-Path $folder.FullName "bin\Publish"
        
        if (Test-Path $tempDir) { Remove-Item -Recurse -Force $tempDir }
        New-Item -ItemType Directory -Force -Path $tempDir | Out-Null
        
        Get-ChildItem -Path $buildOutput -Filter "$pluginName.*" | Where-Object {
            $_.Extension -in @(".dll", ".pdb")
        } | ForEach-Object {
            Copy-Item $_.FullName -Destination $tempDir
        }
        
        # Create archive
        $zipName = "$pluginName-$version.zip"
        $zipPath = Join-Path $ReleaseOutputDir $zipName
        
        if (Test-Path $zipPath) { Remove-Item $zipPath }
        Compress-Archive -Path "$tempDir\*" -DestinationPath $zipPath -CompressionLevel Optimal
        
        $size = [math]::Round((Get-Item $zipPath).Length / 1KB, 2)
        Write-Host "  ✓ Created: $zipName ($size KB)" -ForegroundColor Green
        
        $ReleasedPlugins += @{
            Name = $pluginName
            Version = $version
            ZipPath = $zipPath
            Size = $size
        }
        
        # Cleanup temp dir
        Remove-Item -Recurse -Force $tempDir
    }
    catch {
        Write-Host "  ✗ Failed: $_" -ForegroundColor Red
        $FailedPlugins += $pluginName
    }
    
    Write-Host ""
}

# Summary
Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║                    Release Summary                         ║" -ForegroundColor Green
Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""
Write-Host "  Output directory: $ReleaseOutputDir" -ForegroundColor Gray
Write-Host ""

if ($ReleasedPlugins.Count -gt 0) {
    Write-Host "  Released plugins:" -ForegroundColor White
    $ReleasedPlugins | ForEach-Object {
        Write-Host "    ✓ $($_.Name) v$($_.Version) ($($_.Size) KB)" -ForegroundColor Green
    }
}

if ($FailedPlugins.Count -gt 0) {
    Write-Host ""
    Write-Host "  Failed:" -ForegroundColor Red
    $FailedPlugins | ForEach-Object {
        Write-Host "    ✗ $_" -ForegroundColor Red
    }
}

Write-Host ""
