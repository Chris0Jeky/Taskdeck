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

# ---------------------------------------------------------------------------
# Stop mode
# ---------------------------------------------------------------------------
function Stop-Stack {
    if (-not (Test-Path $PidFile)) {
        Write-Info "No PID file at $PidFile - nothing to stop."
        return
    }
    foreach ($line in Get-Content $PidFile) {
        $procId = ($line -as [int])
        if (-not $procId) { continue }
        $proc = Get-Process -Id $procId -ErrorAction SilentlyContinue
        if ($proc) {
            Write-Step "Stopping PID $procId ($($proc.ProcessName))..."
            Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue
        }
    }
    Remove-Item $PidFile -ErrorAction SilentlyContinue
    Write-Step "Stack stopped."
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

# A fresh PID file for this run.
if (Test-Path $PidFile) {
    Write-Warn "An existing PID file was found; a stack may already be running. Run '.\scripts\dev-up.ps1 -Stop' first if so."
}

Write-Step "Database: $DevDbPath (pinned via ConnectionStrings__DefaultConnection)"

# Env for the API process. Setting the connection string here beats appsettings,
# so the DB no longer follows the launch directory (#1140 AC1).
$env:ConnectionStrings__DefaultConnection = "Data Source=$DevDbPath"
$env:ASPNETCORE_ENVIRONMENT = "Development"

Write-Step "Starting API (dotnet run) on port $ApiPort..."
$apiProc = Start-Process -FilePath "dotnet" `
    -ArgumentList @("run", "--project", $ApiProject) `
    -WorkingDirectory $RepoRoot `
    -PassThru -WindowStyle Minimized
"$($apiProc.Id)" | Set-Content $PidFile

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

# Frontend deps only if missing.
if (-not (Test-Path (Join-Path $FrontendDir "node_modules"))) {
    Write-Step "Installing frontend dependencies (npm install)..."
    Push-Location $FrontendDir
    try {
        npm install
        if ($LASTEXITCODE -ne 0) { Write-Fatal "npm install failed (code $LASTEXITCODE)." }
    } finally {
        Pop-Location
    }
}

Write-Step "Starting Vite dev server (npm run dev)..."
$webProc = Start-Process -FilePath "npm" `
    -ArgumentList @("run", "dev") `
    -WorkingDirectory $FrontendDir `
    -PassThru -WindowStyle Minimized
Add-Content -Path $PidFile -Value "$($webProc.Id)"

Write-Host ""
Write-Step "Stack is up."
Write-Info  "API     : http://localhost:$ApiPort  (Swagger: http://localhost:$ApiPort/swagger)"
Write-Info  "Frontend: http://localhost:5173"
if ($Seed) { Write-Info "Sign in : demo / demo123" }
Write-Info  "PIDs    : API=$($apiProc.Id)  Web=$($webProc.Id)  (saved to $PidFile)"
Write-Info  "Stop    : .\scripts\dev-up.ps1 -Stop"
