#Requires -Version 5.1
<#
.SYNOPSIS
    Transactional one-command launcher for the Taskdeck API and Vue frontend.

.DESCRIPTION
    Reconciles the locked frontend dependencies, starts the API, waits for its
    readiness endpoint, optionally seeds the demo account, then starts Vite and
    accepts success only after one exact entry-graph readiness marker. Every
    long-lived child writes to a unique per-run log file, so it stays healthy
    after this launcher exits and no redirected anonymous pipe can deadlock it.

    PID state contains a schema version, random run ID, ports, logs, and each
    root process's PID/name/creation token. -Stop kills only exact matches and
    removes state only after the recorded trees are gone and ports are released.

.PARAMETER Seed
    Seed demo/demo123 after the API is ready. A seed failure is fatal.

.PARAMETER ResetSeed
    With -Seed, delete only preflighted DEMO:* rehearsal boards before reseeding. Any deletion failure is fatal.

.PARAMETER Stop
    Stop the exact process trees recorded by a prior successful invocation.

.PARAMETER ApiPort
    API port. Defaults to 5000.

.EXAMPLE
    .\scripts\dev-up.ps1
    .\scripts\dev-up.ps1 -Seed
    .\scripts\dev-up.ps1 -Seed -ResetSeed
    .\scripts\dev-up.ps1 -Stop
    .\scripts\dev-up.ps1 -ApiPort 5001
#>

[CmdletBinding()]
param(
    [switch]$Seed,
    [switch]$ResetSeed,
    [switch]$Stop,
    [ValidateRange(1, 65535)]
    [int]$ApiPort = 5000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($ResetSeed -and ((-not $Seed) -or $Stop)) {
    throw "-ResetSeed is valid only with -Seed and cannot be combined with -Stop. No process or demo state was changed."
}

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir
$ApiProject = Join-Path $RepoRoot "backend/src/Taskdeck.Api/Taskdeck.Api.csproj"
$FrontendDir = Join-Path $RepoRoot "frontend/taskdeck-web"

$DataDir = Join-Path $env:LOCALAPPDATA "Taskdeck"
$DevDbPath = Join-Path $DataDir "taskdeck-dev.db"
$PidFile = Join-Path $DataDir "dev-up.pids"
$OperationLockFile = Join-Path $DataDir "dev-up.operation.lock"

$MinimumNodeVersion = [version]"24.13.1"
$MaximumNodeVersion = [version]"25.0.0"
$DevRunIdHeaderName = "Taskdeck-Dev-Run-Id"
$ReadyMarker = "TASKDECK_DEV_FRONTEND_READY"
$StateVersion = 1

function Get-PositiveTimeoutSetting {
    param([string]$Name, [int]$DefaultValue)
    $raw = [Environment]::GetEnvironmentVariable($Name, "Process")
    if ([string]::IsNullOrWhiteSpace($raw)) { return $DefaultValue }
    $parsed = 0
    if (-not [int]::TryParse($raw, [ref]$parsed) -or $parsed -lt 1) {
        throw "$Name must be a positive integer."
    }
    return $parsed
}

$ApiReadyTimeoutSeconds = Get-PositiveTimeoutSetting -Name "TASKDECK_DEV_API_READY_TIMEOUT_SECONDS" -DefaultValue 90
$FrontendReadyTimeoutSeconds = Get-PositiveTimeoutSetting -Name "TASKDECK_DEV_FRONTEND_READY_TIMEOUT_SECONDS" -DefaultValue 60
$MarkerSettleSeconds = Get-PositiveTimeoutSetting -Name "TASKDECK_DEV_MARKER_SETTLE_SECONDS" -DefaultValue 1

$script:DotnetExe = $null
$script:NodeExe = $null
$script:NpmCmd = $null
$script:ComSpecExe = $null
$script:OperationLockStream = $null
$script:TransactionActive = $false
$script:State = $null

function Write-Step { param([string]$Message) Write-Host "[dev-up] $Message" -ForegroundColor Cyan }
function Write-Info { param([string]$Message) Write-Host "[dev-up] $Message" -ForegroundColor DarkGray }
function Write-DevWarning { param([string]$Message) Write-Warning "[dev-up] $Message" }

function Resolve-RequiredApplication {
    param([string]$Name)
    Get-Command $Name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
}

function Enter-OperationLock {
    try {
        $script:OperationLockStream = [System.IO.File]::Open(
            $OperationLockFile,
            [System.IO.FileMode]::OpenOrCreate,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None
        )
    } catch {
        throw "Another dev-up start/stop operation is active (lock: $OperationLockFile)."
    }
}

function Exit-OperationLock {
    if ($null -ne $script:OperationLockStream) {
        $script:OperationLockStream.Dispose()
        $script:OperationLockStream = $null
    }
}

function Get-ProcessCreationToken {
    param([System.Diagnostics.Process]$Process)
    try {
        $Process.Refresh()
        return $Process.StartTime.ToUniversalTime().Ticks.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    } catch {
        return $null
    }
}

function New-ProcessRecord {
    param([string]$Role, [System.Diagnostics.Process]$Process)
    $Process.Refresh()
    $name = $Process.ProcessName
    $token = Get-ProcessCreationToken -Process $Process
    if ([string]::IsNullOrWhiteSpace($name) -or [string]::IsNullOrWhiteSpace($token)) {
        throw "Could not capture the $Role process creation identity."
    }
    [pscustomobject]@{
        Role = $Role
        Pid = [int]$Process.Id
        Name = [string]$name
        CreationToken = [string]$token
    }
}

# Returns Missing, Match, Mismatch, or Unknown. Only Match authorizes taskkill.
function Get-ProcessIdentityStatus {
    param($Record)
    $process = Get-Process -Id ([int]$Record.Pid) -ErrorAction SilentlyContinue
    if ($null -eq $process) { return "Missing" }
    $token = Get-ProcessCreationToken -Process $process
    if ([string]::IsNullOrWhiteSpace($token)) { return "Unknown" }
    if ($process.ProcessName -ieq [string]$Record.Name -and $token -eq [string]$Record.CreationToken) {
        return "Match"
    }
    return "Mismatch"
}

function Assert-ProcessIdentityMatch {
    param($Record, [string]$Context)
    $status = Get-ProcessIdentityStatus -Record $Record
    if ($status -ne "Match") {
        throw "$($Record.Role) process identity became $($status.ToLowerInvariant()) $Context."
    }
}

function Test-ExactProperties {
    param($Object, [string[]]$Names)
    if ($null -eq $Object) { return $false }
    $actual = @($Object.PSObject.Properties | ForEach-Object { $_.Name } | Sort-Object)
    $expected = @($Names | Sort-Object)
    if ($actual.Count -ne $expected.Count) { return $false }
    for ($index = 0; $index -lt $actual.Count; $index++) {
        if ($actual[$index] -cne $expected[$index]) { return $false }
    }
    return $true
}

function Get-ExpectedLogPaths {
    param([string]$RunId)
    [pscustomobject]@{
        ApiStdout = Join-Path $DataDir "dev-up-$RunId-api.stdout.log"
        ApiStderr = Join-Path $DataDir "dev-up-$RunId-api.stderr.log"
        FrontendStdout = Join-Path $DataDir "dev-up-$RunId-frontend.stdout.log"
        FrontendStderr = Join-Path $DataDir "dev-up-$RunId-frontend.stderr.log"
    }
}

function Read-StateFile {
    if (-not (Test-Path -LiteralPath $PidFile -PathType Leaf)) { return $null }
    try {
        $state = [System.IO.File]::ReadAllText($PidFile) | ConvertFrom-Json
    } catch {
        throw "PID state at $PidFile is malformed or unsupported."
    }
    if (-not (Test-ExactProperties $state @("schemaVersion", "runId", "apiPort", "frontend", "logs", "processes"))) {
        throw "PID state at $PidFile has an unsupported schema."
    }
    $runGuid = [guid]::Empty
    if ([int]$state.schemaVersion -ne $StateVersion -or
        -not [guid]::TryParse([string]$state.runId, [ref]$runGuid) -or
        [int]$state.apiPort -lt 1 -or [int]$state.apiPort -gt 65535) {
        throw "PID state at $PidFile has invalid version, run ID, or API port data."
    }
    if (-not (Test-ExactProperties $state.logs @("apiStdout", "apiStderr", "frontendStdout", "frontendStderr"))) {
        throw "PID state at $PidFile has invalid log bindings."
    }
    $expectedLogs = Get-ExpectedLogPaths -RunId ([string]$state.runId)
    foreach ($name in @("ApiStdout", "ApiStderr", "FrontendStdout", "FrontendStderr")) {
        $jsonName = $name.Substring(0, 1).ToLowerInvariant() + $name.Substring(1)
        $actualPath = [System.IO.Path]::GetFullPath([string]$state.logs.$jsonName)
        $expectedPath = [System.IO.Path]::GetFullPath([string]$expectedLogs.$name)
        if (-not [string]::Equals($actualPath, $expectedPath, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "PID state at $PidFile has an unexpected $jsonName path."
        }
    }

    $frontend = $null
    if ($null -ne $state.frontend) {
        if (-not (Test-ExactProperties $state.frontend @("url", "port")) -or
            [string]::IsNullOrWhiteSpace([string]$state.frontend.url) -or
            [int]$state.frontend.port -lt 1 -or [int]$state.frontend.port -gt 65535) {
            throw "PID state at $PidFile has invalid frontend endpoint data."
        }
        $frontend = [pscustomobject]@{ Url = [string]$state.frontend.url; Port = [int]$state.frontend.port }
    }

    $apiRecord = $null
    $frontendRecord = $null
    $records = @($state.processes)
    if ($records.Count -lt 1 -or $records.Count -gt 2) { throw "PID state at $PidFile has an invalid process count." }
    foreach ($record in $records) {
        if (-not (Test-ExactProperties $record @("role", "pid", "name", "creationToken")) -or
            [int64]$record.pid -lt 1 -or
            [string]::IsNullOrWhiteSpace([string]$record.name) -or
            [string]::IsNullOrWhiteSpace([string]$record.creationToken)) {
            throw "PID state at $PidFile has an invalid process record."
        }
        $parsed = [pscustomobject]@{
            Role = [string]$record.role
            Pid = [int]$record.pid
            Name = [string]$record.name
            CreationToken = [string]$record.creationToken
        }
        if ($parsed.Role -eq "api" -and $null -eq $apiRecord) { $apiRecord = $parsed }
        elseif ($parsed.Role -eq "frontend" -and $null -eq $frontendRecord) { $frontendRecord = $parsed }
        else { throw "PID state at $PidFile has duplicate or unknown process roles." }
    }
    if ($null -eq $apiRecord -or ($null -ne $frontend -and $null -eq $frontendRecord)) {
        throw "PID state at $PidFile is missing a required process identity."
    }

    [pscustomobject]@{
        RunId = [string]$state.runId
        ApiPort = [int]$state.apiPort
        Frontend = $frontend
        Logs = $expectedLogs
        ApiRecord = $apiRecord
        FrontendRecord = $frontendRecord
    }
}

function Write-StateFile {
    $processes = @(
        [ordered]@{
            role = "api"
            pid = [int]$script:State.ApiRecord.Pid
            name = [string]$script:State.ApiRecord.Name
            creationToken = [string]$script:State.ApiRecord.CreationToken
        }
    )
    if ($null -ne $script:State.FrontendRecord) {
        $processes += [ordered]@{
            role = "frontend"
            pid = [int]$script:State.FrontendRecord.Pid
            name = [string]$script:State.FrontendRecord.Name
            creationToken = [string]$script:State.FrontendRecord.CreationToken
        }
    }
    $frontend = $null
    if ($null -ne $script:State.Frontend) {
        $frontend = [ordered]@{ url = [string]$script:State.Frontend.Url; port = [int]$script:State.Frontend.Port }
    }
    $document = [ordered]@{
        schemaVersion = $StateVersion
        runId = [string]$script:State.RunId
        apiPort = [int]$script:State.ApiPort
        frontend = $frontend
        logs = [ordered]@{
            apiStdout = [string]$script:State.Logs.ApiStdout
            apiStderr = [string]$script:State.Logs.ApiStderr
            frontendStdout = [string]$script:State.Logs.FrontendStdout
            frontendStderr = [string]$script:State.Logs.FrontendStderr
        }
        processes = $processes
    }
    $temporary = "$PidFile.$($script:State.RunId).tmp"
    $json = ($document | ConvertTo-Json -Depth 6) + [Environment]::NewLine
    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($temporary, $json, $encoding)
    if (Test-Path -LiteralPath $PidFile) {
        $backup = "$PidFile.$($script:State.RunId).backup"
        [System.IO.File]::Replace($temporary, $PidFile, $backup, $true)
        Remove-Item -LiteralPath $backup -ErrorAction SilentlyContinue
    } else {
        [System.IO.File]::Move($temporary, $PidFile)
    }
}

# Legacy "<pid> <name>" state has no creation token. It is never trusted for a
# kill and is discarded only when every referenced PID is absent.
function Remove-DeadLegacyState {
    if (-not (Test-Path -LiteralPath $PidFile -PathType Leaf)) { return $false }
    $raw = [System.IO.File]::ReadAllText($PidFile)
    if ($raw.TrimStart().StartsWith("{")) { return $false }
    $sawLine = $false
    foreach ($line in [System.IO.File]::ReadAllLines($PidFile)) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $sawLine = $true
        if ($line -notmatch '^\s*(?<pid>[1-9]\d*)\s+\S+\s*$') { return $false }
        if ($null -ne (Get-Process -Id ([int]$Matches.pid) -ErrorAction SilentlyContinue)) { return $false }
    }
    if (-not $sawLine) { return $false }
    Remove-Item -LiteralPath $PidFile
    Write-Info "Removed legacy PID state only after every referenced PID was absent."
    return $true
}

function Test-AddressBindable {
    param(
        [System.Net.IPAddress]$Address,
        [int]$Port,
        [switch]$AllowUnavailableAddress
    )
    $listener = $null
    try {
        $listener = New-Object System.Net.Sockets.TcpListener($Address, $Port)
        $listener.Server.ExclusiveAddressUse = $true
        $listener.Start()
        return $true
    } catch [System.Net.Sockets.SocketException] {
        if ($AllowUnavailableAddress -and
            ($_.Exception.SocketErrorCode -eq [System.Net.Sockets.SocketError]::AddressFamilyNotSupported -or
             $_.Exception.SocketErrorCode -eq [System.Net.Sockets.SocketError]::AddressNotAvailable)) {
            return $true
        }
        return $false
    } catch {
        return $false
    } finally {
        if ($null -ne $listener) { try { $listener.Stop() } catch { } }
    }
}

function Test-PortBindable {
    param([int]$Port)
    if (-not (Test-AddressBindable -Address ([System.Net.IPAddress]::Loopback) -Port $Port)) { return $false }
    if ([System.Net.Sockets.Socket]::OSSupportsIPv6 -and
        -not (Test-AddressBindable -Address ([System.Net.IPAddress]::IPv6Loopback) -Port $Port -AllowUnavailableAddress)) {
        return $false
    }
    return $true
}

function Wait-PortRelease {
    param([int]$Port)
    for ($attempt = 0; $attempt -lt 50; $attempt++) {
        if (Test-PortBindable -Port $Port) { return $true }
        Start-Sleep -Milliseconds 100
    }
    return $false
}

function Find-SafeApiPort {
    for ($offset = 1; $offset -le 100; $offset++) {
        $candidate = $ApiPort + $offset
        if ($candidate -gt 65535) { $candidate = 1024 + $offset }
        if (Test-PortBindable -Port $candidate) { return $candidate }
    }
    return $null
}

function Get-PortOwnerDescription {
    param([int]$Port)
    try {
        $connection = Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction Stop | Select-Object -First 1
        if ($null -ne $connection) {
            $owner = Get-Process -Id ([int]$connection.OwningProcess) -ErrorAction SilentlyContinue
            if ($null -ne $owner) { return "PID $($owner.Id) ($($owner.ProcessName))" }
            return "PID $($connection.OwningProcess)"
        }
    } catch { }
    return "an unidentified process"
}

function Stop-RecordedProcess {
    param($Record)
    if ($null -eq $Record) { return $true }
    $status = Get-ProcessIdentityStatus -Record $Record
    if ($status -eq "Missing") { return $true }
    if ($status -ne "Match") {
        Write-DevWarning "Recorded $($Record.Role) PID $($Record.Pid) identity is $($status.ToLowerInvariant()). It was not killed; PID state is retained."
        return $false
    }
    Write-Step "Stopping recorded $($Record.Role) tree at PID $($Record.Pid) ($($Record.Name))..."
    $taskkill = Join-Path $env:SystemRoot "System32/taskkill.exe"
    $output = & $taskkill /T /F /PID ([int]$Record.Pid) 2>&1
    $taskkillExit = $LASTEXITCODE
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        $status = Get-ProcessIdentityStatus -Record $Record
        if ($status -eq "Missing") { return $true }
        if ($status -ne "Match") { break }
        Start-Sleep -Milliseconds 100
    }
    Write-DevWarning "Recorded $($Record.Role) PID $($Record.Pid) did not exit cleanly (taskkill $taskkillExit): $output"
    return $false
}

function Stop-LoadedStack {
    $clean = $true
    if (-not (Stop-RecordedProcess -Record $script:State.FrontendRecord)) { $clean = $false }
    if (-not (Stop-RecordedProcess -Record $script:State.ApiRecord)) { $clean = $false }
    if ($clean -and -not (Wait-PortRelease -Port ([int]$script:State.ApiPort))) {
        Write-DevWarning "API port $($script:State.ApiPort) is still occupied. No foreign listener was killed; PID state is retained."
        $clean = $false
    }
    if ($clean -and $null -ne $script:State.Frontend -and -not (Wait-PortRelease -Port ([int]$script:State.Frontend.Port))) {
        Write-DevWarning "Frontend port $($script:State.Frontend.Port) is still occupied. No foreign listener was killed; PID state is retained."
        $clean = $false
    }
    if ($clean) {
        try {
            Remove-Item -LiteralPath $PidFile -ErrorAction Stop
        } catch {
            Write-DevWarning "Recorded processes exited, but PID state could not be removed: $($_.Exception.Message)"
            return $false
        }
        if (Test-Path -LiteralPath $PidFile) {
            Write-DevWarning "Recorded processes exited, but PID state still exists at $PidFile."
            return $false
        }
        return $true
    }
    return $false
}

function New-EmptyLogFiles {
    param([string[]]$Paths)
    $encoding = New-Object System.Text.UTF8Encoding($false)
    foreach ($path in $Paths) {
        if (Test-Path -LiteralPath $path) { throw "Unexpected pre-existing run log at $path; no process was started for this stage." }
        [System.IO.File]::WriteAllText($path, "", $encoding)
    }
}

# PS 5.1 has no ProcessStartInfo.ArgumentList or Start-Process -Environment.
# Every command is therefore passed to the resolved ComSpec through fixed
# environment-variable tokens. Long-lived stdout/stderr are redirected by cmd
# into unique files; no anonymous pipe remains after this launcher exits.
function Start-LoggedCommand {
    param(
        [string]$Executable,
        [string[]]$Arguments,
        [string]$WorkingDirectory,
        [string]$StdoutLog,
        [string]$StderrLog,
        [hashtable]$EnvironmentOverrides = @{}
    )
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $script:ComSpecExe
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WorkingDirectory = $WorkingDirectory
    # Isolate the broker cmd.exe from this launcher's own stdout/stderr handles,
    # which may themselves be captured by a caller such as Node's spawnSync.
    # Product output is redirected by the command into durable files below; the
    # broker pipes carry no npm/dotnet output and are drained while we validate.
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.EnvironmentVariables["TASKDECK_DEV_EXECUTABLE"] = $Executable
    $startInfo.EnvironmentVariables["TASKDECK_DEV_STDOUT_LOG"] = $StdoutLog
    $startInfo.EnvironmentVariables["TASKDECK_DEV_STDERR_LOG"] = $StderrLog
    $commandTokens = New-Object 'System.Collections.Generic.List[string]'
    $commandTokens.Add('"%TASKDECK_DEV_EXECUTABLE%"')
    for ($index = 0; $index -lt $Arguments.Count; $index++) {
        $name = "TASKDECK_DEV_ARG_$index"
        $startInfo.EnvironmentVariables[$name] = [string]$Arguments[$index]
        $commandTokens.Add("`"%$name%`"")
    }
    foreach ($entry in $EnvironmentOverrides.GetEnumerator()) {
        $startInfo.EnvironmentVariables[[string]$entry.Key] = [string]$entry.Value
    }
    $command = ([string]::Join(" ", $commandTokens)) + ' 1>>"%TASKDECK_DEV_STDOUT_LOG%" 2>>"%TASKDECK_DEV_STDERR_LOG%"'
    $startInfo.Arguments = '/d /s /c "' + $command + '"'
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    if (-not $process.Start()) { throw "Failed to start $Executable." }
    $process.StandardInput.Close()
    $process.BeginOutputReadLine()
    $process.BeginErrorReadLine()
    return $process
}

function Get-LogTail {
    param([string]$Path, [int]$Lines = 20)
    if (-not (Test-Path -LiteralPath $Path)) { return "" }
    return ((Get-Content -LiteralPath $Path -Tail $Lines -ErrorAction SilentlyContinue) -join [Environment]::NewLine)
}

function Disconnect-LoggedBroker {
    param([System.Diagnostics.Process]$Process)
    # Product stdout/stderr already point at durable files. Stop the temporary
    # broker-pipe readers so they cannot keep this PowerShell launcher alive.
    try { $Process.CancelOutputRead() } catch { }
    try { $Process.CancelErrorRead() } catch { }
    try { $Process.StandardOutput.Close() } catch { }
    try { $Process.StandardError.Close() } catch { }
}

function Invoke-NpmStage {
    param(
        [string[]]$Arguments,
        [string]$Stage,
        [hashtable]$EnvironmentOverrides = @{}
    )
    $stdout = Join-Path $DataDir "dev-up-$($script:State.RunId)-$Stage.stdout.log"
    $stderr = Join-Path $DataDir "dev-up-$($script:State.RunId)-$Stage.stderr.log"
    New-EmptyLogFiles -Paths @($stdout, $stderr)
    $process = Start-LoggedCommand -Executable $script:NpmCmd -Arguments $Arguments -WorkingDirectory $FrontendDir -StdoutLog $stdout -StderrLog $stderr -EnvironmentOverrides $EnvironmentOverrides
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) {
        $tail = Get-LogTail -Path $stderr
        if (-not [string]::IsNullOrWhiteSpace($tail)) { [Console]::Error.WriteLine($tail) }
    }
    return [int]$process.ExitCode
}

function Test-HttpReady {
    param([string]$Url, [string]$ExpectedRunId)
    $response = $null
    try {
        $request = [System.Net.HttpWebRequest]::Create($Url)
        $request.Proxy = $null
        $request.AllowAutoRedirect = $false
        $request.Timeout = 1000
        $request.ReadWriteTimeout = 1000
        $response = $request.GetResponse()
        if ([int]$response.StatusCode -ne 200) { return $false }
        $runIdValues = $response.Headers.GetValues($DevRunIdHeaderName)
        return $null -ne $runIdValues -and
            $runIdValues.Count -eq 1 -and
            [string]::Equals($runIdValues[0], $ExpectedRunId, [System.StringComparison]::Ordinal)
    } catch {
        return $false
    } finally {
        if ($null -ne $response) { $response.Dispose() }
    }
}

function ConvertFrom-ReadyMarkerLine {
    param([string]$Line)
    if ($Line -notmatch ('^' + [regex]::Escape($ReadyMarker) + ' (?<json>\{.*\})$')) {
        throw "Vite emitted a spoofed readiness marker on stdout."
    }
    $json = $Matches.json
    foreach ($property in @("schemaVersion", "url", "port")) {
        if ([regex]::Matches($json, '"' + $property + '"\s*:').Count -ne 1) {
            throw "Vite emitted a malformed readiness marker."
        }
    }
    try { $marker = $json | ConvertFrom-Json } catch { throw "Vite emitted a malformed readiness marker." }
    if (-not (Test-ExactProperties $marker @("schemaVersion", "url", "port")) -or
        [int]$marker.schemaVersion -ne 1 -or
        [string]::IsNullOrWhiteSpace([string]$marker.url) -or
        [int64]$marker.port -lt 1 -or [int64]$marker.port -gt 65535) {
        throw "Vite emitted a malformed readiness marker."
    }
    $uri = $null
    if (-not [uri]::TryCreate([string]$marker.url, [System.UriKind]::Absolute, [ref]$uri) -or
        ($uri.Scheme -ne "http" -and $uri.Scheme -ne "https") -or
        -not [string]::IsNullOrEmpty($uri.UserInfo) -or
        -not [string]::IsNullOrEmpty($uri.Query) -or
        -not [string]::IsNullOrEmpty($uri.Fragment) -or
        $uri.Port -ne [int]$marker.port) {
        throw "Vite emitted a malformed readiness marker URL."
    }
    [pscustomobject]@{ Url = [string]$marker.url; Port = [int]$marker.port }
}

function Update-MarkerScan {
    param($Scan, [System.Diagnostics.Stopwatch]$Timer)
    $stdoutLines = @()
    $stderrLines = @()
    try { $stdoutLines = @(Get-Content -LiteralPath $script:State.Logs.FrontendStdout -ErrorAction Stop) } catch { }
    try { $stderrLines = @(Get-Content -LiteralPath $script:State.Logs.FrontendStderr -ErrorAction Stop) } catch { }
    for ($index = $Scan.StdoutCount; $index -lt $stdoutLines.Count; $index++) {
        $line = [string]$stdoutLines[$index]
        if ($line.Contains($ReadyMarker)) {
            if ($Timer.Elapsed.TotalSeconds -ge $FrontendReadyTimeoutSeconds) { throw "Vite emitted a late readiness marker." }
            $marker = ConvertFrom-ReadyMarkerLine -Line $line
            $Scan.MarkerCount++
            if ($Scan.MarkerCount -gt 1) { throw "Vite emitted a duplicate readiness marker." }
            $Scan.Marker = $marker
            $Scan.FirstMarkerSeconds = $Timer.Elapsed.TotalSeconds
        }
    }
    $Scan.StdoutCount = $stdoutLines.Count
    for ($index = $Scan.StderrCount; $index -lt $stderrLines.Count; $index++) {
        if ([string]$stderrLines[$index] -like "*$ReadyMarker*") { throw "Vite emitted a readiness marker on stderr; it was rejected." }
    }
    $Scan.StderrCount = $stderrLines.Count
}

function Test-FrontendEndpoint {
    param([string]$Url)
    $response = $null
    try {
        $request = [System.Net.HttpWebRequest]::Create($Url)
        $request.Proxy = $null
        $request.Timeout = 2000
        $request.ReadWriteTimeout = 2000
        $response = $request.GetResponse()
        if ([int]$response.StatusCode -ne 200) { return $false }
        $reader = New-Object System.IO.StreamReader($response.GetResponseStream())
        try { $body = $reader.ReadToEnd() } finally { $reader.Dispose() }
        return $body.Contains("/src/main.ts") -and $body.Contains("<title>Taskdeck</title>")
    } catch {
        return $false
    } finally {
        if ($null -ne $response) { $response.Dispose() }
    }
}

function Invoke-TransactionCleanup {
    if (-not $script:TransactionActive -or $null -eq $script:State) { return $true }
    Write-DevWarning "Startup failed; cleaning only process trees created by this invocation."
    $clean = Stop-LoadedStack
    if ($clean) { $script:TransactionActive = $false; return $true }
    Write-DevWarning "Startup cleanup was incomplete; PID state is retained at $PidFile."
    return $false
}

function Invoke-Main {
    if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) { throw "LOCALAPPDATA is required for Taskdeck development state." }
    if (-not (Test-Path -LiteralPath $DataDir)) { New-Item -ItemType Directory -Path $DataDir -Force | Out-Null }
    Enter-OperationLock

    if ($Stop) {
        if (-not (Test-Path -LiteralPath $PidFile -PathType Leaf)) {
            Write-Info "No PID state at $PidFile - nothing to stop."
            return
        }
        try { $script:State = Read-StateFile } catch {
            if (Remove-DeadLegacyState) { return }
            throw "$($_.Exception.Message) This may be legacy state without creation tokens. Stop any listed processes manually, then rerun -Stop. Nothing was killed; state was retained."
        }
        if (Stop-LoadedStack) {
            Write-Step "Stack stopped; recorded processes exited and saved ports were released."
            return
        }
        throw "Stack cleanup was incomplete. Inspect the retained PID state at $PidFile."
    }

    $dotnetCommand = Resolve-RequiredApplication -Name "dotnet"
    $nodeCommand = Resolve-RequiredApplication -Name "node"
    $npmCommand = Resolve-RequiredApplication -Name "npm.cmd"
    $comSpecCommand = Resolve-RequiredApplication -Name $env:ComSpec
    $missing = @()
    if ($null -eq $dotnetCommand) { $missing += "dotnet" }
    if ($null -eq $nodeCommand) { $missing += "node" }
    if ($null -eq $npmCommand) { $missing += "npm.cmd" }
    if ($null -eq $comSpecCommand) { $missing += "%ComSpec%" }
    if ($missing.Count -gt 0) { throw "$($missing.Count) required tool(s) missing ($($missing -join ', ')). Install the .NET 8 SDK and Node.js >=24.13.1 <25 first." }
    $script:DotnetExe = $dotnetCommand.Source
    $script:NodeExe = $nodeCommand.Source
    $script:NpmCmd = $npmCommand.Source
    $script:ComSpecExe = $comSpecCommand.Source

    $nodeVersionText = ((& $script:NodeExe -p "process.versions.node" 2>$null) | Select-Object -First 1)
    $nodeProbeExit = $LASTEXITCODE
    if ($nodeProbeExit -ne 0 -or [string]::IsNullOrWhiteSpace($nodeVersionText) -or $nodeVersionText.Trim() -notmatch '^\d+\.\d+\.\d+$') {
        throw "Could not read a supported Node.js version from $script:NodeExe. Required: >=24.13.1 <25."
    }
    $nodeVersionText = $nodeVersionText.Trim()
    $nodeVersion = [version]$nodeVersionText
    if ($nodeVersion -lt $MinimumNodeVersion -or $nodeVersion -ge $MaximumNodeVersion) {
        throw "Node.js >=24.13.1 <25 is required; found v$nodeVersionText. No server was started."
    }

    if (Test-Path -LiteralPath $PidFile -PathType Leaf) {
        try { $script:State = Read-StateFile } catch {
            if (Remove-DeadLegacyState) { $script:State = $null }
            else { throw "$($_.Exception.Message) This may be legacy state without creation tokens. Stop any listed processes manually before removing it; the state was retained because the identity cannot be trusted." }
        }
        if ($null -ne $script:State) {
            $apiStatus = Get-ProcessIdentityStatus -Record $script:State.ApiRecord
            $frontendStatus = if ($null -eq $script:State.FrontendRecord) { "Missing" } else { Get-ProcessIdentityStatus -Record $script:State.FrontendRecord }
            if ($apiStatus -in @("Mismatch", "Unknown") -or $frontendStatus -in @("Mismatch", "Unknown")) {
                throw "Recorded stack identity does not match the live process table. Nothing was killed and PID state was retained at $PidFile."
            }
            if ($apiStatus -eq "Match" -or $frontendStatus -eq "Match") {
                $safePort = Find-SafeApiPort
                if ($null -ne $safePort) { Write-Info "After stopping it, a checked alternative is: .\scripts\dev-up.ps1 -ApiPort $safePort" }
                throw "A launcher-owned stack is already running. Run '.\scripts\dev-up.ps1 -Stop' first."
            }
            if (-not (Test-PortBindable -Port $script:State.ApiPort) -or
                ($null -ne $script:State.Frontend -and -not (Test-PortBindable -Port $script:State.Frontend.Port))) {
                throw "Recorded processes are gone but a saved port is occupied. No listener was killed; PID state was retained."
            }
            Remove-Item -LiteralPath $PidFile
            $script:State = $null
        }
    }

    if (-not (Test-PortBindable -Port $ApiPort)) {
        $owner = Get-PortOwnerDescription -Port $ApiPort
        $safePort = Find-SafeApiPort
        Write-DevWarning "API port $ApiPort is already owned by $owner. No process was stopped."
        if ($null -ne $safePort) { Write-Info "Checked custom-port command: .\scripts\dev-up.ps1 -ApiPort $safePort" }
        throw "Choose the checked custom port above, or stop the owning application and retry."
    }

    $lockFile = Join-Path $FrontendDir "package-lock.json"
    if (-not (Test-Path -LiteralPath $lockFile -PathType Leaf)) { throw "Frontend lockfile not found: $lockFile. No server was started." }

    $runId = [guid]::NewGuid().ToString("D")
    $logs = Get-ExpectedLogPaths -RunId $runId
    $script:State = [pscustomobject]@{
        RunId = $runId
        ApiPort = $ApiPort
        Frontend = $null
        Logs = $logs
        ApiRecord = $null
        FrontendRecord = $null
    }

    Write-Step "Reconciling frontend dependencies from package-lock.json (npm ci)..."
    $npmExit = Invoke-NpmStage -Arguments @("ci", "--no-audit", "--no-fund") -Stage "preflight"
    if ($npmExit -ne 0) {
        throw "Frontend dependency reconciliation failed (npm ci exit $npmExit). No server was started. Run: Set-Location `"$FrontendDir`"; & `"$script:NpmCmd`" ci --no-audit --no-fund"
    }

    $apiBaseUrl = "http://localhost:$ApiPort/api"
    $readyUrl = "http://localhost:$ApiPort/health/ready"
    New-EmptyLogFiles -Paths @($logs.ApiStdout, $logs.ApiStderr)
    Write-Step "Database: $DevDbPath (pinned via ConnectionStrings__DefaultConnection)"
    Write-Step "Starting API (dotnet run) on port $ApiPort..."
    $apiProcess = Start-LoggedCommand -Executable $script:DotnetExe `
        -Arguments @("run", "--no-launch-profile", "--project", $ApiProject, "--urls", "http://localhost:$ApiPort") `
        -WorkingDirectory $RepoRoot -StdoutLog $logs.ApiStdout -StderrLog $logs.ApiStderr `
        -EnvironmentOverrides @{
            TASKDECK_DEV_RUN_ID = $runId
            ConnectionStrings__DefaultConnection = "Data Source=$DevDbPath"
            ASPNETCORE_ENVIRONMENT = "Development"
        }
    $script:State.ApiRecord = New-ProcessRecord -Role "api" -Process $apiProcess
    $script:TransactionActive = $true
    Write-StateFile

    Write-Step "Waiting for $readyUrl (up to ${ApiReadyTimeoutSeconds}s)..."
    $readyTimer = [System.Diagnostics.Stopwatch]::StartNew()
    $apiReady = $false
    while ($readyTimer.Elapsed.TotalSeconds -lt $ApiReadyTimeoutSeconds) {
        $identity = Get-ProcessIdentityStatus -Record $script:State.ApiRecord
        if ($identity -ne "Match") {
            $tail = Get-LogTail -Path $logs.ApiStderr
            if (-not [string]::IsNullOrWhiteSpace($tail)) { [Console]::Error.WriteLine($tail) }
            throw "API process identity became $($identity.ToLowerInvariant()) before readiness."
        }
        if (Test-HttpReady -Url $readyUrl -ExpectedRunId $runId) { $apiReady = $true; break }
        Start-Sleep -Milliseconds 200
    }
    if (-not $apiReady) {
        $tail = Get-LogTail -Path $logs.ApiStderr
        if (-not [string]::IsNullOrWhiteSpace($tail)) { [Console]::Error.WriteLine($tail) }
        throw "API did not report ready within ${ApiReadyTimeoutSeconds}s."
    }
    Write-Step "API is ready."

    if ($Seed) {
        Write-Step "Seeding demo account (demo / demo123) against $apiBaseUrl..."
        if (-not (Test-HttpReady -Url $readyUrl -ExpectedRunId $runId)) {
            throw "API run identity changed before demo seeding."
        }
        $seedArguments = @("run", "demo:seed")
        if ($ResetSeed) { $seedArguments += @("--", "--reset") }
        $seedExit = Invoke-NpmStage -Arguments $seedArguments -Stage "seed" -EnvironmentOverrides @{
            TASKDECK_DEV_RUN_ID = $runId
            TASKDECK_API_BASE_URL = $apiBaseUrl
        }
        if ($seedExit -ne 0) { throw "demo:seed failed (exit $seedExit); the partially started stack will be stopped." }
        if (-not (Test-HttpReady -Url $readyUrl -ExpectedRunId $runId)) {
            throw "API run identity changed after demo seeding."
        }
        Assert-ProcessIdentityMatch -Record $script:State.ApiRecord -Context "during demo seeding"
    }

    New-EmptyLogFiles -Paths @($logs.FrontendStdout, $logs.FrontendStderr)
    Write-Step "Starting Vite dev server (npm run dev) against $apiBaseUrl..."
    $frontendProcess = Start-LoggedCommand -Executable $script:NpmCmd -Arguments @("run", "dev") `
        -WorkingDirectory $FrontendDir -StdoutLog $logs.FrontendStdout -StderrLog $logs.FrontendStderr `
        -EnvironmentOverrides @{
            TASKDECK_DEV_RUN_ID = $runId
            VITE_API_BASE_URL = $apiBaseUrl
        }
    $script:State.FrontendRecord = New-ProcessRecord -Role "frontend" -Process $frontendProcess
    Write-StateFile

    $scan = [pscustomobject]@{ StdoutCount = 0; StderrCount = 0; MarkerCount = 0; Marker = $null; FirstMarkerSeconds = 0.0 }
    $frontendTimer = [System.Diagnostics.Stopwatch]::StartNew()
    while ($true) {
        Update-MarkerScan -Scan $scan -Timer $frontendTimer
        $identity = Get-ProcessIdentityStatus -Record $script:State.FrontendRecord
        if ($identity -ne "Match") {
            $tail = Get-LogTail -Path $logs.FrontendStderr
            if (-not [string]::IsNullOrWhiteSpace($tail)) { [Console]::Error.WriteLine($tail) }
            throw "Vite process identity became $($identity.ToLowerInvariant()) before stable readiness."
        }
        Assert-ProcessIdentityMatch -Record $script:State.ApiRecord -Context "while Vite readiness was being accepted"
        if ($scan.MarkerCount -eq 1 -and ($frontendTimer.Elapsed.TotalSeconds - $scan.FirstMarkerSeconds) -ge $MarkerSettleSeconds) {
            Update-MarkerScan -Scan $scan -Timer $frontendTimer
            if ($scan.MarkerCount -ne 1) { throw "Vite readiness marker was not unique." }
            Assert-ProcessIdentityMatch -Record $script:State.FrontendRecord -Context "after marker parsing"
            Assert-ProcessIdentityMatch -Record $script:State.ApiRecord -Context "after marker parsing"
            if (-not (Test-FrontendEndpoint -Url $scan.Marker.Url)) { throw "Vite readiness marker did not resolve to the Taskdeck entry page; it was rejected as spoofed." }
            break
        }
        if ($frontendTimer.Elapsed.TotalSeconds -ge $FrontendReadyTimeoutSeconds) {
            $tail = Get-LogTail -Path $logs.FrontendStderr
            if (-not [string]::IsNullOrWhiteSpace($tail)) { [Console]::Error.WriteLine($tail) }
            throw "Vite did not emit one exact readiness marker within ${FrontendReadyTimeoutSeconds}s; missing and late markers are rejected."
        }
        Start-Sleep -Milliseconds 100
    }

    Update-MarkerScan -Scan $scan -Timer $frontendTimer
    if ($scan.MarkerCount -ne 1) { throw "Vite readiness marker was not unique at transactional commit." }
    if (-not (Test-HttpReady -Url $readyUrl -ExpectedRunId $runId)) { throw "API lost readiness or changed run identity before transactional commit." }
    if (-not (Test-FrontendEndpoint -Url $scan.Marker.Url)) { throw "Frontend entry page became unavailable before transactional commit." }
    Update-MarkerScan -Scan $scan -Timer $frontendTimer
    if ($scan.MarkerCount -ne 1) { throw "Vite readiness marker was not unique after final endpoint probes." }
    Assert-ProcessIdentityMatch -Record $script:State.ApiRecord -Context "after final endpoint probes"
    Assert-ProcessIdentityMatch -Record $script:State.FrontendRecord -Context "after final endpoint probes"

    $script:State.Frontend = [pscustomobject]@{ Url = [string]$scan.Marker.Url; Port = [int]$scan.Marker.Port }
    Write-StateFile
    if (-not (Test-HttpReady -Url $readyUrl -ExpectedRunId $runId)) { throw "API lost readiness or changed run identity after final state commit." }
    Assert-ProcessIdentityMatch -Record $script:State.ApiRecord -Context "after final state commit"
    Assert-ProcessIdentityMatch -Record $script:State.FrontendRecord -Context "after final state commit"
    Disconnect-LoggedBroker -Process $apiProcess
    Disconnect-LoggedBroker -Process $frontendProcess
    $script:TransactionActive = $false

    Write-Host ""
    Write-Step "Stack is up."
    Write-Info "API     : http://localhost:$ApiPort  (Swagger: http://localhost:$ApiPort/swagger)"
    Write-Info "Frontend: $($script:State.Frontend.Url)"
    if ($Seed) { Write-Info "Sign in : demo / demo123" }
    Write-Info "PIDs    : API=$($script:State.ApiRecord.Pid)  Frontend=$($script:State.FrontendRecord.Pid)  (versioned state: $PidFile)"
    Write-Info "Logs    : $($logs.ApiStdout) ; $($logs.ApiStderr) ; $($logs.FrontendStdout) ; $($logs.FrontendStderr)"
    Write-Info "Stop    : .\scripts\dev-up.ps1 -Stop"
}

$exitCode = 0
try {
    Invoke-Main
} catch {
    $message = $_.Exception.Message
    [Console]::Error.WriteLine("[dev-up] FATAL: $message")
    $exitCode = 1
} finally {
    try {
        if ($script:TransactionActive -and -not (Invoke-TransactionCleanup)) {
            [Console]::Error.WriteLine("[dev-up] FATAL: Cleanup was incomplete; PID state was retained.")
            $exitCode = 1
        }
    } catch {
        [Console]::Error.WriteLine("[dev-up] FATAL: Transaction cleanup threw: $($_.Exception.Message). PID state was retained.")
        $exitCode = 1
    } finally {
        Exit-OperationLock
    }
}
exit $exitCode
