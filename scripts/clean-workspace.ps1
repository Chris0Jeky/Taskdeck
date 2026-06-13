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

# Glob patterns of disposable local-run artifacts. No redundant literals: the
# *.db-shm / *.db-wal globs already cover taskdeck.db-shm / taskdeck.db-wal
# (listing both made a file match twice and double-counted skips). The literal
# taskdeck.db stays — there is intentionally no *.db glob, to avoid deleting
# unrelated .db files.
$Patterns = @(
    "taskdeck.db",
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
            # Never delete a file a running stack holds open: the SQLite WAL/SHM
            # sidecars carry committed-but-uncheckpointed state (guard them as well
            # as the main .db, H1), and SerializedMigrator holds .migrate.lock while
            # applying EF migrations (deleting it mid-migration breaks the guard, P2).
            $needsLockCheck = $file.Name -like "*.db" -or $file.Name -like "*.db-wal" `
                -or $file.Name -like "*.db-shm" -or $file.Name -like "*.migrate.lock"
            if ($needsLockCheck -and (Test-FileLocked -Path $file.FullName)) {
                Write-Warn "In use, skipping: $($file.FullName) (stop the stack first)"
                $skipped++
                continue
            }
            if ($PSCmdlet.ShouldProcess($file.FullName, "Delete")) {
                # Only report/count a deletion that actually succeeded. -ErrorAction
                # Stop surfaces failures (e.g. permission denied) instead of silently
                # claiming success (M1).
                try {
                    Remove-Item -Path $file.FullName -Force -ErrorAction Stop
                    Write-Info "Removed: $($file.FullName)"
                    $removed++
                } catch {
                    Write-Warn "Failed to remove: $($file.FullName). $($_.Exception.Message)"
                }
            }
        }
    }
}

# .tmp directory at the repo root, if present.
$tmpDir = Join-Path $RepoRoot ".tmp"
if (Test-Path $tmpDir) {
    if ($PSCmdlet.ShouldProcess($tmpDir, "Delete directory")) {
        # Same as the file path: only report/count a directory that was actually
        # removed, surfacing failures instead of claiming success (M2).
        try {
            Remove-Item -Path $tmpDir -Recurse -Force -ErrorAction Stop
            Write-Info "Removed: $tmpDir"
            $removed++
        } catch {
            Write-Warn "Failed to remove directory: $tmpDir. $($_.Exception.Message)"
        }
    }
}

Write-Step "Done. Removed $removed item(s); skipped $skipped locked file(s)."
if ($skipped -gt 0) {
    Write-Warn "Some files were in use. Stop running Taskdeck processes (e.g. '.\scripts\dev-up.ps1 -Stop') and re-run."
}
