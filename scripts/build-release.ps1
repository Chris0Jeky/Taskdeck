#Requires -Version 5.1
<#
.SYNOPSIS
    Unified release build: npm run build -> copy dist/ to wwwroot/ -> dotnet publish

.DESCRIPTION
    Builds the Taskdeck Vue SPA, copies the output to the ASP.NET Core wwwroot/
    directory, then produces a self-contained single-file executable for the
    target platform.

.PARAMETER Rid
    .NET Runtime Identifier. Defaults to win-x64 on Windows.
    Supported values: win-x64  linux-x64  osx-x64  osx-arm64

.EXAMPLE
    .\scripts\build-release.ps1                   # defaults to win-x64
    .\scripts\build-release.ps1 -Rid linux-x64    # cross-compile for Linux
    .\scripts\build-release.ps1 -Rid osx-arm64    # cross-compile for macOS ARM

.NOTES
    Requires: Node.js 24.x, npm, and the .NET 8 SDK on PATH.
#>

[CmdletBinding()]
param(
    [ValidateSet("win-x64", "linux-x64", "osx-x64", "osx-arm64")]
    [string]$Rid = "win-x64"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------
$ScriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot    = Split-Path -Parent $ScriptDir

$FrontendDir  = Join-Path $RepoRoot "frontend/taskdeck-web"
$FrontendDist = Join-Path $FrontendDir "dist"

$ApiProject  = Join-Path $RepoRoot "backend/src/Taskdeck.Api/Taskdeck.Api.csproj"
# NOTE: PKG-01 (#533) must be merged before UseStaticFiles / wwwroot serving is configured
# in the .NET API (Program.cs / PipelineConfiguration.cs). Until that PR lands, the published
# binary will NOT serve the SPA — it will return 404 for the frontend routes. Do not ship
# a release artifact built from main until PKG-01 is merged.
$Wwwroot     = Join-Path $RepoRoot "backend/src/Taskdeck.Api/wwwroot"

$OutputBase  = Join-Path $RepoRoot "artifacts\publish"
$OutputDir   = Join-Path $OutputBase $Rid

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
function Write-Step { param([string]$Msg) Write-Host "[build-release] $Msg" -ForegroundColor Cyan }
function Write-Warn  { param([string]$Msg) Write-Warning "[build-release] $Msg" }
function Write-Fatal { param([string]$Msg) Write-Error "[build-release] FATAL: $Msg"; exit 1 }

# ---------------------------------------------------------------------------
# Dependency checks
# ---------------------------------------------------------------------------
function Assert-Dependencies {
    $missing = 0
    foreach ($cmd in @("node", "npm", "dotnet")) {
        if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) {
            Write-Warn "Required tool not found on PATH: $cmd"
            $missing++
        }
    }
    if ($missing -gt 0) {
        Write-Fatal "$missing required tool(s) not found. Install Node.js 24.x and the .NET 8 SDK before running this script."
    }

    # Node version guard (must be 24.x)
    try {
        $nodeVersion = node -e "process.stdout.write(String(process.versions.node.split('.')[0]))" 2>$null
        $nodeMajor   = [int]$nodeVersion
    } catch {
        $nodeMajor = 0
    }
    if ($nodeMajor -lt 24) {
        Write-Warn "Node.js 24.x is required; found $(node --version). Continuing, but the build may fail."
    }
}

# ---------------------------------------------------------------------------
# Step 1 - Frontend build
# ---------------------------------------------------------------------------
function Build-Frontend {
    Write-Step "Step 1/3 - Building Vue SPA (npm run build)..."

    if (-not (Test-Path $FrontendDir)) {
        Write-Fatal "Frontend directory not found: $FrontendDir"
    }

    $nodeModules = Join-Path $FrontendDir "node_modules"
    if (-not (Test-Path $nodeModules)) {
        Write-Step "node_modules not found - running npm install..."
        & npm install --prefix $FrontendDir
        if ($LASTEXITCODE -ne 0) { Write-Fatal "npm install failed (exit $LASTEXITCODE)." }
    }

    & npm run build --prefix $FrontendDir
    if ($LASTEXITCODE -ne 0) { Write-Fatal "npm run build failed (exit $LASTEXITCODE)." }

    if (-not (Test-Path $FrontendDist)) {
        Write-Fatal "Expected dist/ directory not produced at: $FrontendDist"
    }
    Write-Step "Frontend build complete: $FrontendDist"
}

# ---------------------------------------------------------------------------
# Step 2 - Copy dist/ to wwwroot/
# ---------------------------------------------------------------------------
function Copy-ToWwwroot {
    Write-Step "Step 2/3 - Copying dist/ -> wwwroot/..."

    # Wipe and recreate to avoid stale files accumulating across builds
    if (Test-Path $Wwwroot) {
        Remove-Item -Path $Wwwroot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $Wwwroot -Force | Out-Null

    Copy-Item -Path (Join-Path $FrontendDist '*') -Destination $Wwwroot -Recurse -Force
    Write-Step "Copied to wwwroot: $Wwwroot"
}

# ---------------------------------------------------------------------------
# Step 3 - dotnet publish
# ---------------------------------------------------------------------------
function Publish-Backend {
    Write-Step "Step 3/3 - Publishing .NET API (RID=$Rid)..."
    Write-Step "Output directory: $OutputDir"

    if (-not (Test-Path $ApiProject)) {
        Write-Fatal "API project file not found: $ApiProject"
    }

    # TRIM WARNING: PublishTrimmed=true can silently break reflection-heavy code paths
    # (EF Core migrations, ASP.NET DI conventions, System.Text.Json, SignalR).
    # Validate the trimmed artifact with a smoke test before shipping.
    & dotnet publish $ApiProject `
        -c Release `
        -r $Rid `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:PublishTrimmed=true `
        -p:TrimmerRootAssembly=Taskdeck.Api `
        -o $OutputDir

    if ($LASTEXITCODE -ne 0) { Write-Fatal "dotnet publish failed (exit $LASTEXITCODE)." }

    Write-Step "Publish complete: $OutputDir"
}

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
function Write-Summary {
    Write-Step ""
    Write-Step "Build complete."
    Write-Step "  RID         : $Rid"
    Write-Step "  Artifact    : $OutputDir"

    $exeName = if ($Rid -like "win-*") { "Taskdeck.Api.exe" } else { "Taskdeck.Api" }
    $exePath = Join-Path $OutputDir $exeName

    if (Test-Path $exePath) {
        $sizeKB = [math]::Round((Get-Item $exePath).Length / 1KB)
        Write-Step "  Executable  : $exePath (~$sizeKB KB)"
        if ($sizeKB -gt 102400) {
            Write-Warn "Executable is larger than 100 MB (~$sizeKB KB). Consider reviewing trim settings."
        }
    }
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------
Write-Step "=== Taskdeck release build ==="
Write-Step "RID: $Rid"
Write-Step "Repo root: $RepoRoot"

Assert-Dependencies
Build-Frontend
Copy-ToWwwroot
Publish-Backend
Write-Summary
