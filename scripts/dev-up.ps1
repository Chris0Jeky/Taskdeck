#Requires -Version 5.1
<#
.SYNOPSIS
    One-command local dev launcher: starts the Taskdeck API and the Vue dev server.

.DESCRIPTION
    Brings up the full local development stack with a single command:
      1. Verifies the .NET 8 SDK, npm.cmd, and the supported Node.js range.
      2. Reconciles frontend dependencies exactly from package-lock.json.
      3. Pins the SQLite database to %LOCALAPPDATA%\Taskdeck so it no longer
         lands in whatever directory you happened to launch from (issue #1140).
      4. Starts the API (background) and waits for /health/ready.
      5. Starts the Vite dev server through the resolved npm.cmd executable.
      6. Optionally seeds the demo account (demo / demo123) so the first
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
    Requires: .NET 8 SDK, Node.js >=24.13.1 <25, npm.cmd on PATH.
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

$MinimumNodeVersion = [version]"24.13.1"
$MaximumNodeVersion = [version]"25.0.0"
$script:DotnetExe = $null
$script:NodeExe = $null
$script:NpmCmd = $null

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
function Resolve-RequiredApplication {
    param([string]$Name)
    return Get-Command $Name -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
}

function Assert-Dependencies {
    $dotnetCommand = Resolve-RequiredApplication -Name "dotnet"
    $nodeCommand = Resolve-RequiredApplication -Name "node"
    # Resolve npm.cmd by its complete Windows launcher name. An extensionless
    # `npm` can resolve through a text-editor association instead of creating a
    # controllable npm process tree on Windows.
    $npmCommand = Resolve-RequiredApplication -Name "npm.cmd"

    $missing = 0
    foreach ($tool in @(
        @{ Name = "dotnet"; Command = $dotnetCommand },
        @{ Name = "node"; Command = $nodeCommand },
        @{ Name = "npm.cmd"; Command = $npmCommand }
    )) {
        if (-not $tool.Command) {
            Write-Warn "Required tool not found on PATH: $($tool.Name)"
            $missing++
        }
    }
    if ($missing -gt 0) {
        Write-Fatal "$missing required tool(s) missing. Install the .NET 8 SDK and Node.js >=24.13.1 <25 first."
    }

    $script:DotnetExe = $dotnetCommand.Source
    $script:NodeExe = $nodeCommand.Source
    $script:NpmCmd = $npmCommand.Source

    try {
        $nodeVersionText = ((& $script:NodeExe -p "process.versions.node" 2>$null) |
            Select-Object -First 1).Trim()
    } catch {
        Write-Fatal "Could not read the Node.js version from $script:NodeExe."
    }

    if ($nodeVersionText -notmatch '^\d+\.\d+\.\d+$') {
        Write-Fatal "Node.js returned an unsupported version string: '$nodeVersionText'. Required: >=24.13.1 <25."
    }

    $nodeVersion = [version]$nodeVersionText
    if ($nodeVersion -lt $MinimumNodeVersion -or $nodeVersion -ge $MaximumNodeVersion) {
        Write-Fatal "Node.js >=24.13.1 <25 is required; found v$nodeVersionText. No server was started."
    }
}

function Sync-FrontendDependencies {
    $lockFile = Join-Path $FrontendDir "package-lock.json"
    if (-not (Test-Path -LiteralPath $lockFile -PathType Leaf)) {
        Write-Fatal "Frontend lockfile not found: $lockFile. No server was started."
    }

    Write-Step "Reconciling frontend dependencies from package-lock.json (npm ci)..."
    $npmExit = 1
    Push-Location $FrontendDir
    try {
        & $script:NpmCmd ci --no-audit --no-fund
        $npmExit = $LASTEXITCODE
    } finally {
        Pop-Location
    }
    if ($npmExit -ne 0) {
        Write-Fatal "Frontend dependency reconciliation failed (npm ci exit $npmExit). No server was started. Run: Set-Location `"$FrontendDir`"; & `"$script:NpmCmd`" ci --no-audit --no-fund"
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

# Reconcile the complete dependency tree before either server starts. `npm ci`
# removes a stale node_modules tree and installs exactly package-lock.json, so a
# newly locked direct dependency cannot be skipped just because the directory
# already exists.
Sync-FrontendDependencies

Write-Step "Database: $DevDbPath (pinned via ConnectionStrings__DefaultConnection)"

Write-Step "Starting API (dotnet run) on port $ApiPort..."
# The API needs ConnectionStrings__DefaultConnection (pins the DB so it no longer
# follows the launch directory, #1140 AC1) and ASPNETCORE_ENVIRONMENT. On PS 5.1
# Start-Process has no -Environment param, so the child inherits the parent's
# env — set it just for the spawn, then RESTORE so we don't leak these into an
# interactive session that outlives the launcher (P2).
$prevConn = $env:ConnectionStrings__DefaultConnection
$prevEnvName = $env:ASPNETCORE_ENVIRONMENT
$env:ConnectionStrings__DefaultConnection = "Data Source=$DevDbPath"
# --no-launch-profile (below) skips launchSettings.json, which would otherwise
# set ASPNETCORE_ENVIRONMENT=Development, so set it explicitly here.
$env:ASPNETCORE_ENVIRONMENT = "Development"
try {
    # Pass --urls AND --no-launch-profile: the `http` launch profile's applicationUrl
    # is fixed at :5000 and would override an inherited ASPNETCORE_URLS, so a custom
    # -ApiPort must be applied via --urls with the profile disabled, or the API stays
    # on 5000 while only the probe/printed URL move (P2).
    $apiProc = Start-Process -FilePath $script:DotnetExe `
        -ArgumentList @("run", "--no-launch-profile", "--project", $ApiProject, "--urls", "http://localhost:$ApiPort") `
        -WorkingDirectory $RepoRoot `
        -PassThru -WindowStyle Minimized
} finally {
    $env:ConnectionStrings__DefaultConnection = $prevConn
    $env:ASPNETCORE_ENVIRONMENT = $prevEnvName
}
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

if ($Seed) {
    Write-Step "Seeding demo account (demo / demo123)..."
    Push-Location $FrontendDir
    try {
        & $script:NpmCmd run demo:seed
        if ($LASTEXITCODE -ne 0) { Write-Warn "demo:seed exited with code $LASTEXITCODE." }
    } finally {
        Pop-Location
    }
}

Write-Step "Starting Vite dev server (npm run dev)..."
$webProc = Start-Process -FilePath $script:NpmCmd `
    -ArgumentList @("run", "dev") `
    -WorkingDirectory $FrontendDir `
    -PassThru -WindowStyle Minimized
Add-Content -Path $PidFile -Value "$($webProc.Id) $($webProc.ProcessName)"

# Confirm the dev server didn't exit immediately (missing/broken Vite, bad Node,
# unbindable port) before declaring success (P2).
Start-Sleep -Seconds 2
if ($webProc.HasExited) {
    Write-Warn "The Vite dev server exited immediately (code $($webProc.ExitCode)). Check 'cd $FrontendDir; npm run dev' manually."
}

Write-Host ""
Write-Step "Stack is up."
Write-Info  "API     : http://localhost:$ApiPort  (Swagger: http://localhost:$ApiPort/swagger)"
# Vite uses 5173 if free, else falls back (4173/5001 — see run-vite-dev.mjs);
# check the dev-server output for the actual URL if 5173 was occupied.
Write-Info  "Frontend: http://localhost:5173 (or the next free port if 5173 was taken)"
if ($Seed) { Write-Info "Sign in : demo / demo123" }
Write-Info  "PIDs    : API=$($apiProc.Id)  Web=$($webProc.Id)  (saved to $PidFile)"
Write-Info  "Stop    : .\scripts\dev-up.ps1 -Stop"
