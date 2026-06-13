#Requires -Version 5.1
<#
.SYNOPSIS
    Removes stray local-run artifacts from the working tree (issue #1140).

.DESCRIPTION
    Development runs of the API/MCP/CLI drop a SQLite database plus its -shm/-wal
    and .migrate.lock sidecars into whatever directory they were launched from,
    so copies accumulate at the repo root and under backend/src/Taskdeck.Api/.
    This script deletes those stray artifacts (and api-tests.log / .tmp) so the
    tree is clean again.

    It refuses to delete a database file that is currently locked by a running
    process, so it will never corrupt live data — stop the stack first
    (`.\scripts\dev-up.ps1 -Stop`) if it reports a file in use.

    The canonical dev database at %LOCALAPPDATA%\Taskdeck is NOT touched.

.PARAMETER WhatIf
    Show what would be deleted without deleting anything.

.EXAMPLE
    .\scripts\clean-workspace.ps1
    .\scripts\clean-workspace.ps1 -WhatIf
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot  = Split-Path -Parent $ScriptDir

function Write-Step { param([string]$Msg) Write-Host "[clean-workspace] $Msg" -ForegroundColor Cyan }
function Write-Info { param([string]$Msg) Write-Host "[clean-workspace] $Msg" -ForegroundColor DarkGray }
function Write-Warn { param([string]$Msg) Write-Warning "[clean-workspace] $Msg" }

# Directories that accumulate stray SQLite artifacts from CWD-relative runs.
$ScanDirs = @(
    $RepoRoot,
    (Join-Path $RepoRoot "backend/src/Taskdeck.Api")
)

# Glob patterns of disposable local-run artifacts.
$Patterns = @(
    "taskdeck.db", "taskdeck.db-shm", "taskdeck.db-wal",
    "*.db-shm", "*.db-wal", "*.migrate.lock",
    "api-tests.log"
)

function Test-FileLocked {
    param([string]$Path)
    try {
        $stream = [System.IO.File]::Open($Path, 'Open', 'ReadWrite', 'None')
        $stream.Close()
        return $false
    } catch {
        return $true
    }
}

$removed = 0
$skipped = 0

foreach ($dir in $ScanDirs) {
    if (-not (Test-Path $dir)) { continue }
    foreach ($pattern in $Patterns) {
        $matches = Get-ChildItem -Path $dir -Filter $pattern -File -ErrorAction SilentlyContinue
        foreach ($file in $matches) {
            # Never delete a .db that is locked by a live process.
            if ($file.Extension -eq ".db" -and (Test-FileLocked -Path $file.FullName)) {
                Write-Warn "In use, skipping: $($file.FullName) (stop the stack first)"
                $skipped++
                continue
            }
            if ($PSCmdlet.ShouldProcess($file.FullName, "Delete")) {
                Remove-Item -Path $file.FullName -Force -ErrorAction SilentlyContinue
                Write-Info "Removed: $($file.FullName)"
                $removed++
            }
        }
    }
}

# .tmp directory at the repo root, if present.
$tmpDir = Join-Path $RepoRoot ".tmp"
if (Test-Path $tmpDir) {
    if ($PSCmdlet.ShouldProcess($tmpDir, "Delete directory")) {
        Remove-Item -Path $tmpDir -Recurse -Force -ErrorAction SilentlyContinue
        Write-Info "Removed: $tmpDir"
        $removed++
    }
}

Write-Step "Done. Removed $removed item(s); skipped $skipped locked file(s)."
if ($skipped -gt 0) {
    Write-Warn "Some files were in use. Stop running Taskdeck processes (e.g. '.\scripts\dev-up.ps1 -Stop') and re-run."
}
