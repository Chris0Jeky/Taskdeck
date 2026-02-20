param(
    [switch]$SkipInstall
)

$ErrorActionPreference = 'Stop'

if (-not $SkipInstall) {
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        throw 'winget is not available in PATH. Install App Installer / winget first.'
    }

    Write-Host 'Installing/upgrading Docker Desktop from winget...'
    winget install --id Docker.DockerDesktop --source winget --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if (-not (Test-Path 'C:\Program Files\Docker\Docker\Docker Desktop.exe')) {
    throw 'Docker Desktop executable was not found at C:\Program Files\Docker\Docker\Docker Desktop.exe'
}

Start-Process 'C:\Program Files\Docker\Docker\Docker Desktop.exe'

$deadline = (Get-Date).AddMinutes(3)
do {
    Start-Sleep -Seconds 2
    $pipeReady = Test-Path '\\.\pipe\dockerDesktopLinuxEngine'
} until ($pipeReady -or (Get-Date) -ge $deadline)

if (-not $pipeReady) {
    throw 'Docker daemon pipe did not become ready within 3 minutes.'
}

docker context use desktop-linux | Out-Null
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

docker version
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host 'Docker Desktop upgrade/startup flow completed.'
