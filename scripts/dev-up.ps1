#Requires -Version 5.1
<#
.SYNOPSIS
    One-command local dev launcher: starts the Taskdeck API and the Vue dev server.

.DESCRIPTION
    Brings up the full local development stack with a single command:
      1. Verifies the .NET 8 SDK and Node.js 24.x are on PATH.
      2. Pins the SQLite database to %LOCALAPPDATA%\Taskdeck so it no longer
         lands in whatever directory you happened to launch from (issue #1140).
      3. Starts the API (background) and waits for /health/ready.
      4. Installs frontend deps if missing, then starts the Vite dev server.
      5. Optionally seeds the demo account (demo / demo123) so the first
         sign-in is never an empty board.

    Both processes run in the background; their PIDs are printed at the end
    along with a stop hint. Re-run with -Stop to shut them down.

.PARAMETER Seed
    After the API is ready, run `npm run demo:seed` to create the demo account
    and a populated board. Idempotent; safe to pass on every run.

.PARAMETER Stop
    Stop a stack previously started by this script (reads the PID file) and exit.

.PARAMETER ApiPort
    Port the API listens on. Defaults to 5000.

.EXAMPLE
    .\scripts\dev-up.ps1                # start API + frontend
    .\scripts\dev-up.ps1 -Seed         # start and seed the demo account
    .\scripts\dev-up.ps1 -Stop         # stop the running stack

.NOTES
    Requires: .NET 8 SDK, Node.js 24.x, npm on PATH.
    The dev database lives at %LOCALAPPDATA%\Taskdeck\taskdeck-dev.db.

    -Stop kills each recorded launcher PID with its whole process tree via
    `taskkill /T` (the real Kestrel API and Vite node are children), so the API
    (custom or default port) and 5173 are released. PIDs are stored with the
    process name so a recycled PID is not mistaken for the stack.
#>

[CmdletBinding()]
param(
    [switch]$Seed,
    [switch]$Stop,
    [int]$ApiPort = 5000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------
$ScriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot   = Split-Path -Parent $ScriptDir

$ApiProject  = Join-Path $RepoRoot "backend/src/Taskdeck.Api/Taskdeck.Api.csproj"
$FrontendDir = Join-Path $RepoRoot "frontend/taskdeck-web"

$DataDir    = Join-Path $env:LOCALAPPDATA "Taskdeck"
$DevDbPath  = Join-Path $DataDir "taskdeck-dev.db"
$PidFile    = Join-Path $DataDir "dev-up.pids"

$ReadyUrl   = "http://localhost:$ApiPort/health/ready"

function Write-Step  { param([string]$Msg) Write-Host "[dev-up] $Msg" -ForegroundColor Cyan }
function Write-Info  { param([string]$Msg) Write-Host "[dev-up] $Msg" -ForegroundColor DarkGray }
function Write-Warn  { param([string]$Msg) Write-Warning "[dev-up] $Msg" }
function Write-Fatal { param([string]$Msg) Write-Error "[dev-up] FATAL: $Msg"; exit 1 }

# Resolve a live process for a PID-file line ("<pid> <name>"), guarding against
# PID reuse: returns the process only when it is alive AND (no name was recorded,
# or its current name matches the recorded one). Returns $null for a dead PID or
# a clear name mismatch (a recycled PID now owned by something unrelated), so we
# never taskkill an unrelated process tree.
function Resolve-OurProcess {
    param([string]$Line)
    $parts = $Line.Trim() -split '\s+', 2
    $procId = ($parts[0] -as [int])
    if (-not $procId) { return $null }
    $proc = Get-Process -Id $procId -ErrorAction SilentlyContinue
    if (-not $proc) { return $null }
    $recordedName = if ($parts.Count -gt 1) { $parts[1] } else { "" }
    if ($recordedName -and $proc.ProcessName -ne $recordedName) { return $null }
    return $proc
}

# ---------------------------------------------------------------------------
# Stop mode
# ---------------------------------------------------------------------------
function Stop-Stack {
    if (-not (Test-Path $PidFile)) {
        Write-Info "No PID file at $PidFile - nothing to stop."
        return
    }
    foreach ($line in Get-Content $PidFile) {
        $proc = Resolve-OurProcess -Line $line
        if ($proc) {
            $procId = $proc.Id
            Write-Step "Stopping PID $procId ($($proc.ProcessName))..."
            # `dotnet run` / `npm run dev` are launchers; the real Kestrel API
            # and Vite node processes are children. taskkill /T kills the whole
            # tree so the ports are released, not just the parent launcher (H1).
            $tkOut = & taskkill /T /F /PID $procId 2>&1
            if ($LASTEXITCODE -ne 0) {
                Write-Warn "taskkill failed for PID ${procId}: $tkOut"
            }
        }
    }
    Remove-Item $PidFile -ErrorAction SilentlyContinue
    Write-Step "Stack stopped."
}

# Stop only the API we started in this run (its process tree) and clear the PID
# file. Used to clean up a half-started stack when a later step fails fatally, so
# we never leave the port + pinned SQLite DB held by an orphaned background API.
function Stop-StartedApi {
    param([int]$ApiPid)
    if (-not $ApiPid) { return }
    if (Get-Process -Id $ApiPid -ErrorAction SilentlyContinue) {
        Write-Warn "Stopping background API (PID $ApiPid) started by this run..."
        & taskkill /T /F /PID $ApiPid 2>&1 | Out-Null
    }
    Remove-Item $PidFile -ErrorAction SilentlyContinue
}

if ($Stop) {
    Stop-Stack
    exit 0
}

# ---------------------------------------------------------------------------
# Dependency checks
# ---------------------------------------------------------------------------
function Assert-Dependencies {
    $missing = 0
    foreach ($cmd in @("dotnet", "node", "npm")) {
        if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) {
            Write-Warn "Required tool not found on PATH: $cmd"
            $missing++
        }
    }
    if ($missing -gt 0) {
        Write-Fatal "$missing required tool(s) missing. Install the .NET 8 SDK and Node.js 24.x first."
    }

    try {
        $nodeMajor = [int](node -e "process.stdout.write(String(process.versions.node.split('.')[0]))" 2>$null)
    } catch {
        $nodeMajor = 0
    }
    if ($nodeMajor -lt 24) {
        Write-Warn "Node.js 24.x is required; found $(node --version). Continuing, but the dev server may fail."
    }
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------
Assert-Dependencies

if (-not (Test-Path $DataDir)) {
    New-Item -ItemType Directory -Path $DataDir -Force | Out-Null
}

# Refuse to start over a live stack: overwriting the PID file would orphan the
# running API/frontend and lose the PIDs that -Stop needs (H1). A PID file whose
# PIDs are all dead is stale — remove it and continue.
if (Test-Path $PidFile) {
    $running = $false
    foreach ($line in Get-Content $PidFile) {
        if (Resolve-OurProcess -Line $line) {
            $running = $true
        }
    }
    if ($running) {
        Write-Fatal "A stack is already running (PIDs found in $PidFile). Run '.\scripts\dev-up.ps1 -Stop' first."
    } else {
        Remove-Item $PidFile -ErrorAction SilentlyContinue
    }
}

Write-Step "Database: $DevDbPath (pinned via ConnectionStrings__DefaultConnection)"

# Env for the API process. Setting the connection string here beats appsettings,
# so the DB no longer follows the launch directory (#1140 AC1).
$env:ConnectionStrings__DefaultConnection = "Data Source=$DevDbPath"
# --no-launch-profile (below) skips launchSettings.json, which would otherwise
# set ASPNETCORE_ENVIRONMENT=Development, so set it explicitly here.
$env:ASPNETCORE_ENVIRONMENT = "Development"

Write-Step "Starting API (dotnet run) on port $ApiPort..."
# Pass --urls AND --no-launch-profile: the `http` launch profile's applicationUrl
# is fixed at :5000 and would override an inherited ASPNETCORE_URLS, so a custom
# -ApiPort must be applied via --urls with the profile disabled, or the API stays
# on 5000 while only the probe/printed URL move (P2).
$apiProc = Start-Process -FilePath "dotnet" `
    -ArgumentList @("run", "--no-launch-profile", "--project", $ApiProject, "--urls", "http://localhost:$ApiPort") `
    -WorkingDirectory $RepoRoot `
    -PassThru -WindowStyle Minimized
# Record "<pid> <name>" so -Stop can detect PID reuse by comparing names.
"$($apiProc.Id) $($apiProc.ProcessName)" | Set-Content $PidFile

Write-Step "Waiting for $ReadyUrl (up to 90s)..."
$ready = $false
for ($i = 0; $i -lt 45; $i++) {
    if ($apiProc.HasExited) {
        Write-Fatal "API process exited (code $($apiProc.ExitCode)) before becoming ready. Check the API window for errors."
    }
    try {
        $resp = Invoke-WebRequest -Uri $ReadyUrl -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop
        if ([int]$resp.StatusCode -eq 200) { $ready = $true; break }
    } catch {
        # not up yet
    }
    Start-Sleep -Seconds 2
}
if (-not $ready) {
    Write-Warn "API did not report ready within 90s. It may still be migrating; continuing to start the frontend."
} else {
    Write-Step "API is ready."
}

# Frontend deps only if missing. Must run BEFORE seeding: demo:seed is a Node
# script that resolves through the installed toolchain.
if (-not (Test-Path (Join-Path $FrontendDir "node_modules"))) {
    Write-Step "Installing frontend dependencies (npm install)..."
    Push-Location $FrontendDir
    try {
        npm install
        $npmExit = $LASTEXITCODE
    } finally {
        Pop-Location
    }
    if ($npmExit -ne 0) {
        # The API is already running in the background; stop it before exiting or
        # we leave the port + pinned SQLite DB held by an orphaned process (P2).
        Stop-StartedApi -ApiPid $apiProc.Id
        Write-Fatal "npm install failed (code $npmExit)."
    }
}

if ($Seed) {
    Write-Step "Seeding demo account (demo / demo123)..."
    Push-Location $FrontendDir
    try {
        npm run demo:seed
        if ($LASTEXITCODE -ne 0) { Write-Warn "demo:seed exited with code $LASTEXITCODE." }
    } finally {
        Pop-Location
    }
}

Write-Step "Starting Vite dev server (npm run dev)..."
$webProc = Start-Process -FilePath "npm" `
    -ArgumentList @("run", "dev") `
    -WorkingDirectory $FrontendDir `
    -PassThru -WindowStyle Minimized
Add-Content -Path $PidFile -Value "$($webProc.Id) $($webProc.ProcessName)"

Write-Host ""
Write-Step "Stack is up."
Write-Info  "API     : http://localhost:$ApiPort  (Swagger: http://localhost:$ApiPort/swagger)"
Write-Info  "Frontend: http://localhost:5173"
if ($Seed) { Write-Info "Sign in : demo / demo123" }
Write-Info  "PIDs    : API=$($apiProc.Id)  Web=$($webProc.Id)  (saved to $PidFile)"
Write-Info  "Stop    : .\scripts\dev-up.ps1 -Stop"
