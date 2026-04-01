#Requires -Version 5.1
<#
.SYNOPSIS
    Restore the Taskdeck SQLite database from a backup file.

.DESCRIPTION
    Before overwriting the live database the script:
      1. Verifies the backup is a valid SQLite file (magic bytes + PRAGMA integrity_check).
      2. Creates a timestamped safety copy of the current live database.
      3. Replaces the live database with the backup.
      4. Runs a post-restore integrity check.

.PARAMETER BackupFile
    Path to the backup .db file to restore from. REQUIRED.

.PARAMETER DbPath
    Path to the live database to overwrite.
    Default: resolved from ConnectionStrings__DefaultConnection env var,
    then "$env:USERPROFILE\.taskdeck\taskdeck.db".

.PARAMETER SafetyDir
    Directory to write the pre-restore safety copy into.
    Default: same directory as -DbPath, or "$env:USERPROFILE\.taskdeck\backups".

.PARAMETER Yes
    Skip the interactive confirmation prompt.

.EXAMPLE
    .\scripts\restore.ps1 -BackupFile "$env:USERPROFILE\.taskdeck\backups\taskdeck-backup-2026-04-01-120000.db"
    .\scripts\restore.ps1 -BackupFile "D:\backups\taskdeck-backup-2026-04-01-120000.db" `
        -DbPath "C:\app\data\taskdeck.db" -Yes
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [string]$BackupFile,

    [string]$DbPath    = "",
    [string]$SafetyDir = "",
    [switch]$Yes
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# Validate backup file exists
# ---------------------------------------------------------------------------
if (-not (Test-Path $BackupFile -PathType Leaf)) {
    Write-Error "Backup file not found: $BackupFile"
}

# ---------------------------------------------------------------------------
# Resolve DB path
# ---------------------------------------------------------------------------
if ([string]::IsNullOrWhiteSpace($DbPath)) {
    $csEnv = $env:ConnectionStrings__DefaultConnection
    if (-not [string]::IsNullOrWhiteSpace($csEnv)) {
        $DbPath = ($csEnv -split ';' | Where-Object { $_ -match 'Data Source=' } | ForEach-Object { $_ -replace '.*Data Source=', '' }).Trim()
    } elseif (-not [string]::IsNullOrWhiteSpace($env:TASKDECK_DB_PATH)) {
        $DbPath = $env:TASKDECK_DB_PATH
    } else {
        $DbPath = Join-Path $env:USERPROFILE ".taskdeck\taskdeck.db"
    }
}

# ---------------------------------------------------------------------------
# Resolve safety copy directory
# ---------------------------------------------------------------------------
if ([string]::IsNullOrWhiteSpace($SafetyDir)) {
    $DbDir = Split-Path $DbPath -Parent
    if (Test-Path $DbDir -PathType Container) {
        $SafetyDir = $DbDir
    } else {
        $SafetyDir = Join-Path $env:USERPROFILE ".taskdeck\backups"
    }
}

# ---------------------------------------------------------------------------
# Step 1: Verify backup is a valid SQLite database
# ---------------------------------------------------------------------------
Write-Host "Verifying backup file: $BackupFile"

# Check SQLite magic bytes: first 15 bytes must be "SQLite format 3" (the full
# header is 16 bytes including the null terminator, but we compare the text only)
$SqliteMagic = [System.Text.Encoding]::ASCII.GetBytes("SQLite format 3")

# Read only the first 16 bytes instead of the entire file
$HeaderBytes = [byte[]]::new(16)
$stream = [System.IO.File]::OpenRead($BackupFile)
try { [void]$stream.Read($HeaderBytes, 0, 16) } finally { $stream.Close() }

if ((Get-Item $BackupFile).Length -lt 16) {
    Write-Error "Backup file is too small to be a valid SQLite database."
}

$MagicMatch = $true
for ($i = 0; $i -lt $SqliteMagic.Length; $i++) {
    if ($HeaderBytes[$i] -ne $SqliteMagic[$i]) {
        $MagicMatch = $false
        break
    }
}

if (-not $MagicMatch) {
    Write-Error "Backup file does not have the SQLite magic header. This is not a valid SQLite database."
}
Write-Host "File type check: SQLite magic bytes verified"

$Sqlite3 = Get-Command sqlite3.exe -ErrorAction SilentlyContinue

if ($Sqlite3) {
    Write-Host "Running integrity check on backup..."
    $Integrity = & $Sqlite3.Source $BackupFile "PRAGMA integrity_check;" 2>&1
    if ($Integrity -ne "ok") {
        Write-Error "Backup integrity check failed: $Integrity"
    }
    Write-Host "Integrity check: ok"

    # Sanity-check the schema: verify the backup looks like a Taskdeck database
    $Tables = (& $Sqlite3.Source $BackupFile ".tables" 2>&1) -join " "
    if ([string]::IsNullOrWhiteSpace($Tables)) {
        Write-Warning "Backup database is empty (no tables found)."
        if (-not $Yes) {
            $Confirm = Read-Host "Restore an empty database? [y/N]"
            if ($Confirm -notmatch '^[Yy]$') { Write-Host "Aborted."; exit 1 }
        }
    } elseif ($Tables -notmatch 'Boards') {
        Write-Warning "Backup does not contain a 'Boards' table. Tables: $Tables"
        Write-Warning "This may not be a Taskdeck database."
        if (-not $Yes) {
            $Confirm = Read-Host "Restore anyway? [y/N]"
            if ($Confirm -notmatch '^[Yy]$') { Write-Host "Aborted."; exit 1 }
        }
    }
} else {
    Write-Warning "sqlite3.exe not found. Skipping PRAGMA integrity_check."
    Write-Warning "Install sqlite3 for full validation."
}

# ---------------------------------------------------------------------------
# Step 2: Interactive confirmation (unless -Yes)
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "  Backup file : $BackupFile"
Write-Host "  Live DB     : $DbPath"
Write-Host "  Safety copy : $SafetyDir\"
Write-Host ""

if (-not $Yes) {
    $Confirm = Read-Host "WARNING: this will overwrite the live database. Proceed? [y/N]"
    if ($Confirm -notmatch '^[Yy]$') { Write-Host "Aborted."; exit 1 }
}

# ---------------------------------------------------------------------------
# Step 3: Create safety copy of the current live database
# ---------------------------------------------------------------------------
if (-not (Test-Path $SafetyDir)) {
    New-Item -ItemType Directory -Path $SafetyDir -Force | Out-Null
}

$Timestamp  = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd-HHmmss")
$SafetyFile = $null

if (Test-Path $DbPath -PathType Leaf) {
    $SafetyFile = Join-Path $SafetyDir "taskdeck-pre-restore-$Timestamp.db"
    if ($Sqlite3) {
        $SafeSafetyFile = $SafetyFile -replace "'", "''"
        & $Sqlite3.Source $DbPath ".backup '$SafeSafetyFile'"
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to create safety copy (sqlite3 exit code $LASTEXITCODE)."
        }
    } else {
        Copy-Item $DbPath $SafetyFile
    }
    # Restrict safety copy permissions
    try {
        $acl = Get-Acl $SafetyFile
        $acl.SetAccessRuleProtection($true, $false)
        $acl.Access | ForEach-Object { $acl.RemoveAccessRule($_) | Out-Null }
        $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
            [System.Security.Principal.WindowsIdentity]::GetCurrent().Name,
            "FullControl", "None", "None", "Allow"
        )
        $acl.AddAccessRule($rule)
        Set-Acl $SafetyFile $acl
    } catch {
        Write-Warning "Could not restrict safety copy ACL: $_"
    }
    Write-Host "Safety copy created: $SafetyFile"
} else {
    Write-Host "INFO: no existing database at $DbPath — skipping safety copy."
}

# ---------------------------------------------------------------------------
# Step 4: Restore
# ---------------------------------------------------------------------------
$DbDir = Split-Path $DbPath -Parent
if (-not (Test-Path $DbDir)) {
    New-Item -ItemType Directory -Path $DbDir -Force | Out-Null
}

# Remove stale WAL/SHM files to prevent replay against restored DB.
# EF Core uses WAL mode by default; leftover -wal/-shm from the previous
# database would be replayed on first open, silently corrupting the restore.
Remove-Item -Force -ErrorAction SilentlyContinue "${DbPath}-wal"
Remove-Item -Force -ErrorAction SilentlyContinue "${DbPath}-shm"

if ($Sqlite3) {
    $SafeBackupFile = $BackupFile -replace "'", "''"
    & $Sqlite3.Source $DbPath ".restore '$SafeBackupFile'"
    if ($LASTEXITCODE -ne 0) {
        Write-Error "sqlite3 .restore failed (exit code $LASTEXITCODE). Safety copy: $SafetyFile"
    }
} else {
    Copy-Item $BackupFile $DbPath -Force
}

# Restrict restored DB permissions
try {
    $acl = Get-Acl $DbPath
    $acl.SetAccessRuleProtection($true, $false)
    $acl.Access | ForEach-Object { $acl.RemoveAccessRule($_) | Out-Null }
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        [System.Security.Principal.WindowsIdentity]::GetCurrent().Name,
        "FullControl", "None", "None", "Allow"
    )
    $acl.AddAccessRule($rule)
    Set-Acl $DbPath $acl
} catch {
    Write-Warning "Could not restrict restored DB ACL: $_"
}

Write-Host "Restored: $BackupFile -> $DbPath"

# ---------------------------------------------------------------------------
# Step 5: Post-restore integrity verification
# ---------------------------------------------------------------------------
if ($Sqlite3) {
    $Integrity = & $Sqlite3.Source $DbPath "PRAGMA integrity_check;" 2>&1
    if ($Integrity -ne "ok") {
        Write-Error "Post-restore integrity check FAILED: $Integrity`n  Safety copy is at: $SafetyFile"
    }
    Write-Host "Post-restore integrity check: ok"
}

Write-Host "Done. Restart the Taskdeck API to pick up the restored database."
