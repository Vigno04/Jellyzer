<#
.SYNOPSIS
    Build script for the Jellyzer Jellyfin plugin.

.DESCRIPTION
    Builds the plugin in Release mode and optionally copies the DLL to your
    local Jellyfin plugin directory so you can test immediately.

.PARAMETER JellyfinPluginDir
    Path to Jellyfin's plugins folder. The script will create a subfolder
    "Jellyzer" inside it and drop the DLL there.
    Defaults to: %APPDATA%\jellyfin\plugins\Jellyzer

.PARAMETER NoCopy
    If set, skip copying the output to Jellyfin's plugin directory.

.EXAMPLE
    # Build and auto-install to default Jellyfin plugin directory
    .\build.ps1

    # Build only, no copy
    .\build.ps1 -NoCopy

    # Build and install to a custom Jellyfin data path
    .\build.ps1 -JellyfinPluginDir "D:\jellyfin\plugins\Jellyzer"
#>

param(
    [string]$JellyfinPluginDir = "$env:LOCALAPPDATA\jellyfin\plugins\Jellyzer",
    [switch]$NoCopy
)

$ErrorActionPreference = "Stop"
$ProjectDir  = Join-Path $PSScriptRoot "Jellyzer"
$ProjectFile = Join-Path $ProjectDir   "Jellyzer.csproj"
$OutDir      = Join-Path $ProjectDir   "bin\Release\net9.0"

Write-Host ""
Write-Host "╔══════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║        Jellyzer — Build Script       ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# ── Build ────────────────────────────────────────────────────────────────────
Write-Host "► Building project..." -ForegroundColor Yellow
dotnet build $ProjectFile -c Release --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed (exit code $LASTEXITCODE)."
    exit $LASTEXITCODE
}
Write-Host "✔ Build succeeded." -ForegroundColor Green
Write-Host ""

# ── Copy to Jellyfin plugin folder ───────────────────────────────────────────
if ($NoCopy) {
    Write-Host "Skipping copy (-NoCopy specified)." -ForegroundColor DarkGray
} else {
    $DllSrc = Join-Path $OutDir "Jellyzer.dll"
    $MetaSrc = Join-Path $ProjectDir "meta.json"
    $ImageSrc = Join-Path $ProjectDir "logo.png"
    
    if (-not (Test-Path $DllSrc)) {
        Write-Error "Output DLL not found: $DllSrc"
        exit 1
    }

    Write-Host "► Copying plugin to: $JellyfinPluginDir" -ForegroundColor Yellow
    New-Item -ItemType Directory -Force -Path $JellyfinPluginDir | Out-Null
    Copy-Item -Force $DllSrc $JellyfinPluginDir
    Copy-Item -Force $MetaSrc $JellyfinPluginDir
    
    if (Test-Path $ImageSrc) {
        Copy-Item -Force $ImageSrc $JellyfinPluginDir
        Write-Host "✔ Copied Jellyzer.dll, meta.json, and plugin icon → $JellyfinPluginDir" -ForegroundColor Green
    } else {
        Write-Host "✔ Copied Jellyzer.dll and meta.json → $JellyfinPluginDir" -ForegroundColor Green
    }
    Write-Host ""
    Write-Host "Restart Jellyfin to load the updated plugin." -ForegroundColor Magenta
}

Write-Host ""
Write-Host "Done." -ForegroundColor Cyan
