param()

$ErrorActionPreference = 'Stop'

Write-Host '=== CLI Availability ==='
$dockerPath = (Get-Command docker -ErrorAction SilentlyContinue).Source
$rgPath = (Get-Command rg -ErrorAction SilentlyContinue).Source
$wingetPath = (Get-Command winget -ErrorAction SilentlyContinue).Source

[PSCustomObject]@{
    docker = if ($dockerPath) { $dockerPath } else { 'NOT_FOUND' }
    rg = if ($rgPath) { $rgPath } else { 'NOT_FOUND' }
    winget = if ($wingetPath) { $wingetPath } else { 'NOT_FOUND' }
} | Format-List

Write-Host '=== Docker Version ==='
docker version
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host '=== Docker Contexts ==='
docker context ls
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host '=== Docker Info (server) ==='
docker info
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host '=== WSL Status ==='
wsl --status
wsl -l -v
