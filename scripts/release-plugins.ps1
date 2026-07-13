<#
    MultiShock Release All Plugins Script
    Builds each plugin, generates/validates a Manifest v2 plugin.json, packages it as a
    .msplugin (zip) archive, and computes a SHA-256 checksum for the plugin index.

    Usage: .\release-plugins.ps1 [-Plugin "PluginName"] [-SdkVersion "1.12.1"] [-MinAppVersion "4.0.0"]
#>

param(
    [string]$Plugin = "",
    [string]$SdkVersion = "",
    [string]$MinAppVersion = "",
    [string]$CatalogBaseUrl = "https://github.com/bwmp/Multishock.Plugins/releases/download/plugin-catalog"
)

$ErrorActionPreference = "Stop"

# Reads a property value out of a .csproj (first match wins).
function Get-CsprojProperty {
    param([string]$CsprojPath, [string]$Name)
    $content = Get-Content $CsprojPath -Raw
    $match = [regex]::Match($content, "<$Name>(.*?)</$Name>")
    if ($match.Success) { return $match.Groups[1].Value.Trim() }
    return $null
}

# Builds a Manifest v2 object from csproj metadata + the plugin's IPlugin id.
function New-PluginManifestV2 {
    param(
        [string]$PluginName,
        [string]$Version,
        [string]$CsprojPath,
        [string]$PluginId,
        [string]$SdkVersion,
        [string]$MinAppVersion
    )

    $existing = $null
    $existingPath = Join-Path (Split-Path $CsprojPath -Parent) "plugin.json"
    if (Test-Path $existingPath) {
        try { $existing = Get-Content $existingPath -Raw | ConvertFrom-Json } catch { }
    }

    $description = Get-CsprojProperty $CsprojPath "Description"
    if (-not $description) { $description = $existing.description }
    $authors = Get-CsprojProperty $CsprojPath "Authors"
    if (-not $authors) { $authors = $existing.authors }
    $sourceUrl = Get-CsprojProperty $CsprojPath "PackageProjectUrl"
    if (-not $sourceUrl) { $sourceUrl = $existing.sourceUrl }

    $manifest = [ordered]@{
        manifestVersion = 2
        id              = $PluginId
        name            = if ($existing.name) { $existing.name } else { $PluginName }
        version         = $Version
        entryPoint      = "$PluginName.dll"
    }
    if ($description) { $manifest.description = $description }
    if ($authors) {
        $manifest.authors = if ($authors -is [string]) {
            @($authors -split ';' | ForEach-Object { $_.Trim() })
        } else {
            @($authors)
        }
    }
    if ($sourceUrl)   { $manifest.sourceUrl = $sourceUrl }
    if ($MinAppVersion) { $manifest.minAppVersion = $MinAppVersion }
    elseif ($existing.minAppVersion) { $manifest.minAppVersion = $existing.minAppVersion }
    if ($SdkVersion)  { $manifest.sdkVersion = $SdkVersion }
    elseif ($existing.sdkVersion) { $manifest.sdkVersion = $existing.sdkVersion }
    $manifest.tags = @($existing.tags)
    $manifest.platforms = if ($existing.platforms) { @($existing.platforms) } else { @("win-x64") }

    return $manifest
}

# Minimal Manifest v2 validation mirroring PluginSdk/Core/PluginManifest.Validate().
function Test-PluginManifestV2 {
    param($Manifest)
    $errors = @()
    $semver = '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z\-.]+)?(?:\+[0-9A-Za-z\-.]+)?$'

    if (-not $Manifest.id)   { $errors += "missing id" }
    if (-not $Manifest.name) { $errors += "missing name" }
    if (-not $Manifest.entryPoint) { $errors += "missing entryPoint" }
    if (-not $Manifest.version) { $errors += "missing version" }
    elseif ($Manifest.version -notmatch $semver) { $errors += "version '$($Manifest.version)' is not semver" }
    if ($Manifest.minAppVersion -and ($Manifest.minAppVersion -notmatch $semver)) { $errors += "minAppVersion not semver" }
    if ($Manifest.sdkVersion -and ($Manifest.sdkVersion -notmatch $semver)) { $errors += "sdkVersion not semver" }

    return $errors
}

# Reads the plugin id from the plugin's Plugin.cs (public ... PluginId = "...").
function Get-PluginId {
    param([string]$PluginFolder, [string]$PluginName)
    $pluginCs = Get-ChildItem -Path $PluginFolder -Filter "Plugin.cs" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($pluginCs) {
        $content = Get-Content $pluginCs.FullName -Raw
        $m = [regex]::Match($content, 'PluginId\s*=\s*"([^"]+)"')
        if ($m.Success) { return $m.Groups[1].Value }
    }
    # Fall back to an existing plugin.json id, then a derived id.
    $existing = Join-Path $PluginFolder "plugin.json"
    if (Test-Path $existing) {
        try {
            $j = Get-Content $existing -Raw | ConvertFrom-Json
            if ($j.id) { return $j.id }
        } catch { }
    }
    return "com.multishock." + $PluginName.ToLowerInvariant()
}

Write-Host ""
Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Magenta
Write-Host "║            MultiShock Plugin Release Builder               ║" -ForegroundColor Magenta
Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Magenta
Write-Host ""

# Get the repo root (parent of scripts folder, then Plugins subfolder)
$ScriptsDir = $PSScriptRoot
$PluginsRepoRoot = Split-Path -Parent $ScriptsDir
$RepoRoot = Join-Path $PluginsRepoRoot "Plugins"

$ReleaseOutputDir = Join-Path $PluginsRepoRoot "releases"

# Create release output directory
New-Item -ItemType Directory -Force -Path $ReleaseOutputDir | Out-Null

# Find all plugin projects: any folder with a matching .csproj (plugins are not
# required to carry a "*Plugin" name suffix - e.g. TwitchIntegration, VRChatOSC)
$PluginFolders = Get-ChildItem -Path $RepoRoot -Directory | Where-Object {
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
        $manifestPath = Join-Path $PluginsRepoRoot ".release-please-manifest.json"
        if (Test-Path $manifestPath) {
            $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
            $pluginPackagePath = "Plugins/$pluginName"
            if ($manifest.$pluginPackagePath) {
                $version = $manifest.$pluginPackagePath
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

        # Include the plugin's own assemblies plus any dependency/native files, so the
        # .msplugin package is self-contained (excluding the SDK, which the host provides).
        Get-ChildItem -Path $buildOutput -Recurse -File | Where-Object {
            $_.Name -ne "MultiShock.PluginSdk.dll" -and
            $_.Extension -in @(".dll", ".pdb", ".json") -or $_.Directory.Name -eq "native" -or $_.Directory.Parent.Name -eq "runtimes"
        } | ForEach-Object {
            $relative = $_.FullName.Substring($buildOutput.Length).TrimStart('\', '/')
            $dest = Join-Path $tempDir $relative
            New-Item -ItemType Directory -Force -Path (Split-Path $dest -Parent) | Out-Null
            Copy-Item $_.FullName -Destination $dest
        }

        # Generate + validate Manifest v2, overwriting any legacy plugin.json in the package.
        $pluginId = Get-PluginId -PluginFolder $folder.FullName -PluginName $pluginName
        $effectiveSdk = if ($SdkVersion) { $SdkVersion } else { Get-CsprojProperty $csprojPath "PluginSdkVersion" }
        $manifest = New-PluginManifestV2 -PluginName $pluginName -Version $version -CsprojPath $csprojPath `
            -PluginId $pluginId -SdkVersion $effectiveSdk -MinAppVersion $MinAppVersion

        $manifestErrors = Test-PluginManifestV2 -Manifest $manifest
        if ($manifestErrors.Count -gt 0) {
            throw "Manifest validation failed: $($manifestErrors -join '; ')"
        }

        $manifestPath = Join-Path $tempDir "plugin.json"
        $manifest | ConvertTo-Json -Depth 5 | Set-Content -Path $manifestPath -Encoding utf8
        Write-Host "  → Manifest v2 written (id: $pluginId)" -ForegroundColor Yellow

        # Create the .msplugin package (zip)
        $packageName = "$pluginName-$version.msplugin"
        $packagePath = Join-Path $ReleaseOutputDir $packageName

        if (Test-Path $packagePath) { Remove-Item $packagePath }
        Compress-Archive -Path "$tempDir\*" -DestinationPath $packagePath -CompressionLevel Optimal

        # Compute SHA-256 checksum for the plugin index / updater verification
        $hash = (Get-FileHash -Path $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
        $checksumPath = "$packagePath.sha256"
        "$hash  $packageName" | Set-Content -Path $checksumPath -Encoding ascii

        $size = [math]::Round((Get-Item $packagePath).Length / 1KB, 2)
        Write-Host "  ✓ Created: $packageName ($size KB)" -ForegroundColor Green
        Write-Host "  ✓ SHA-256: $hash" -ForegroundColor Green

        $ReleasedPlugins += @{
            Name = $pluginName
            Id = $pluginId
            Version = $version
            Description = $manifest.description
            Authors = @($manifest.authors)
            Tags = @($manifest.tags)
            MinAppVersion = $manifest.minAppVersion
            SdkVersion = $manifest.sdkVersion
            Platforms = @($manifest.platforms)
            PackageName = $packageName
            PackagePath = $packagePath
            Sha256 = $hash
            Size = (Get-Item $packagePath).Length
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
    # The app consumes this index directly from the stable plugin-catalog release.
    # Generate it from the exact packages/checksums produced above so metadata cannot drift.
    $catalog = [ordered]@{
        schemaVersion = 1
        generatedAt = [DateTimeOffset]::UtcNow.ToString("o")
        plugins = @($ReleasedPlugins | Sort-Object Name | ForEach-Object {
            [ordered]@{
                id = $_.Id
                name = $_.Name
                version = $_.Version
                description = if ($_.Description) { $_.Description } else { "" }
                authors = @($_.Authors)
                tags = @($_.Tags)
                downloadUrl = "$($CatalogBaseUrl.TrimEnd('/'))/$($_.PackageName)"
                sha256 = $_.Sha256
                size = $_.Size
                minAppVersion = $_.MinAppVersion
                sdkVersion = $_.SdkVersion
                platforms = @($_.Platforms)
            }
        })
    }
    $catalogPath = Join-Path $ReleaseOutputDir "plugin-index.json"
    $catalog | ConvertTo-Json -Depth 8 | Set-Content -Path $catalogPath -Encoding utf8
    Write-Host "  ✓ Catalog: $catalogPath" -ForegroundColor Green
    Write-Host ""

    Write-Host "  Released plugins:" -ForegroundColor White
    $ReleasedPlugins | ForEach-Object {
        $sizeKb = [math]::Round($_.Size / 1KB, 2)
        Write-Host "    ✓ $($_.Name) v$($_.Version) ($sizeKb KB)" -ForegroundColor Green
        Write-Host "        sha256: $($_.Sha256)" -ForegroundColor DarkGray
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

if ($FailedPlugins.Count -gt 0) {
    # CI must never publish a partial catalog: missing entries would make installed
    # plugins appear to vanish from the repository until the next successful run.
    exit 1
}
