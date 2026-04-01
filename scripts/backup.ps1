#Requires -Version 5.1
<#
.SYNOPSIS
    Create a timestamped hot backup of the Taskdeck SQLite database.

.DESCRIPTION
    Uses sqlite3.exe's .backup command for a consistent online backup (safe while the DB is
    being written). Falls back to Copy-Item with a warning if sqlite3.exe is not on PATH.

    Retention: keeps the N most-recent backup files and deletes older ones.

.PARAMETER DbPath
    Path to the SQLite database file.
    Default: resolved from ConnectionStrings__DefaultConnection env var, then
    "$env:USERPROFILE\.taskdeck\taskdeck.db".

.PARAMETER OutputDir
    Directory to write backup files into.
    Default: "$env:USERPROFILE\.taskdeck\backups".

.PARAMETER Retain
    Number of most-recent backups to keep. Default: 7.

.EXAMPLE
    .\scripts\backup.ps1
    .\scripts\backup.ps1 -DbPath "C:\app\data\taskdeck.db" -OutputDir "D:\backups"
    .\scripts\backup.ps1 -Retain 14
#>

[CmdletBinding()]
param(
    [string]$DbPath    = "",
    [string]$OutputDir = "",
    [int]   $Retain    = 7
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# Resolve DB path
# ---------------------------------------------------------------------------
if ([string]::IsNullOrWhiteSpace($DbPath)) {
    $csEnv = $env:ConnectionStrings__DefaultConnection
    if (-not [string]::IsNullOrWhiteSpace($csEnv)) {
        # Parse "Data Source=/path/to/taskdeck.db" (handles extra parameters like ";Pooling=true")
        $DbPath = ($csEnv -split ';' | Where-Object { $_ -match 'Data Source=' } | ForEach-Object { $_ -replace '.*Data Source=', '' }).Trim()
    } elseif (-not [string]::IsNullOrWhiteSpace($env:TASKDECK_DB_PATH)) {
        $DbPath = $env:TASKDECK_DB_PATH
    } else {
        $DbPath = Join-Path $env:USERPROFILE ".taskdeck\taskdeck.db"
    }
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $env:USERPROFILE ".taskdeck\backups"
}

# ---------------------------------------------------------------------------
# Validate inputs
# ---------------------------------------------------------------------------
if (-not (Test-Path $DbPath -PathType Leaf)) {
    Write-Error "Database file not found: $DbPath`n  Set -DbPath, TASKDECK_DB_PATH, or ConnectionStrings__DefaultConnection."
}

if ($Retain -lt 1) {
    Write-Error "-Retain must be >= 1 (got: $Retain)"
}

# ---------------------------------------------------------------------------
# Create output directory with restricted ACL (owner-only)
# ---------------------------------------------------------------------------
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

# Restrict directory to current user only
try {
    $acl = Get-Acl $OutputDir
    $acl.SetAccessRuleProtection($true, $false)
    $acl.Access | ForEach-Object { $acl.RemoveAccessRule($_) | Out-Null }
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        [System.Security.Principal.WindowsIdentity]::GetCurrent().Name,
        "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow"
    )
    $acl.AddAccessRule($rule)
    Set-Acl $OutputDir $acl
} catch {
    Write-Warning "Could not restrict backup directory ACL: $_"
}

# ---------------------------------------------------------------------------
# Build backup filename
# ---------------------------------------------------------------------------
$Timestamp   = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd-HHmmss")
$BackupFile  = Join-Path $OutputDir "taskdeck-backup-$Timestamp.db"

Write-Host "Backing up: $DbPath"
Write-Host "       to:  $BackupFile"

# ---------------------------------------------------------------------------
# Perform backup
# ---------------------------------------------------------------------------
$Sqlite3 = Get-Command sqlite3.exe -ErrorAction SilentlyContinue

if ($Sqlite3) {
    # sqlite3 .backup is a hot backup: copies pages under a shared lock,
    # flushing WAL frames first. Safe with active readers and writers.
    $SafeBackupFile = $BackupFile -replace "'", "''"
    & $Sqlite3.Source $DbPath ".backup '$SafeBackupFile'"
    if ($LASTEXITCODE -ne 0) {
        Write-Error "sqlite3 .backup failed (exit code $LASTEXITCODE)."
    }
    Write-Host "Method: sqlite3 hot backup (safe with active writers)"
} else {
    Write-Warning "sqlite3.exe not found on PATH. Falling back to Copy-Item."
    Write-Warning "Copy-Item is NOT safe if the database has active writers."
    Write-Warning "Install sqlite3 for production use."
    Copy-Item $DbPath $BackupFile
}

# Restrict backup file to current user only
try {
    $acl = Get-Acl $BackupFile
    $acl.SetAccessRuleProtection($true, $false)
    $acl.Access | ForEach-Object { $acl.RemoveAccessRule($_) | Out-Null }
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        [System.Security.Principal.WindowsIdentity]::GetCurrent().Name,
        "FullControl", "None", "None", "Allow"
    )
    $acl.AddAccessRule($rule)
    Set-Acl $BackupFile $acl
} catch {
    Write-Warning "Could not restrict backup file ACL: $_"
}

# ---------------------------------------------------------------------------
# Quick integrity check on the backup
# ---------------------------------------------------------------------------
if ($Sqlite3) {
    $Integrity = & $Sqlite3.Source $BackupFile "PRAGMA integrity_check;" 2>&1
    if ($Integrity -ne "ok") {
        Remove-Item $BackupFile -Force -ErrorAction SilentlyContinue
        Write-Error "Backup integrity check failed: $Integrity"
    }
    Write-Host "Integrity: ok"
}

Write-Host "Backup written: $BackupFile"

# ---------------------------------------------------------------------------
# Retention: keep only the N most-recent backups; delete older ones
# ---------------------------------------------------------------------------
$AllBackups = @(Get-ChildItem -Path $OutputDir -Filter "taskdeck-backup-*.db" |
    Sort-Object LastWriteTime -Descending)

$Total = $AllBackups.Count
if ($Total -gt $Retain) {
    $DeleteCount = $Total - $Retain
    $ToDelete    = $AllBackups | Select-Object -Skip $Retain
    foreach ($File in $ToDelete) {
        Remove-Item $File.FullName -Force
        Write-Host "Removed old backup: $($File.FullName)"
    }
    Write-Host "Retention: kept $Retain of $Total backups, removed $DeleteCount."
} else {
    Write-Host "Retention: $Total backup(s) kept (limit $Retain)."
}

Write-Host "Done."
