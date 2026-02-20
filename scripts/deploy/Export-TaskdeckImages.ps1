param(
    [string]$OutputDirectory = 'artifacts/container-images',
    [string]$ApiImage = 'taskdeck-api:local',
    [string]$WebImage = 'taskdeck-web:local'
)

$ErrorActionPreference = 'Stop'

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$apiTar = Join-Path $OutputDirectory 'taskdeck-api.tar'
$webTar = Join-Path $OutputDirectory 'taskdeck-web.tar'
$checksumsPath = Join-Path $OutputDirectory 'SHA256SUMS.txt'

Write-Host "Exporting $ApiImage to $apiTar"
docker save -o $apiTar $ApiImage
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Exporting $WebImage to $webTar"
docker save -o $webTar $WebImage
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$apiHash = (Get-FileHash -Algorithm SHA256 -Path $apiTar).Hash.ToLowerInvariant()
$webHash = (Get-FileHash -Algorithm SHA256 -Path $webTar).Hash.ToLowerInvariant()

@(
    "$apiHash *taskdeck-api.tar"
    "$webHash *taskdeck-web.tar"
) | Set-Content -Path $checksumsPath -Encoding ASCII

Write-Host "Wrote checksums to $checksumsPath"
