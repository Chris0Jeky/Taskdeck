param(
    [switch]$NoCache
)

$ErrorActionPreference = 'Stop'

$apiArgs = @('build', '-f', 'deploy/docker/backend.Dockerfile', '-t', 'taskdeck-api:local')
$webArgs = @('build', '--build-arg', 'VITE_API_BASE_URL=/api', '-f', 'deploy/docker/frontend.Dockerfile', '-t', 'taskdeck-web:local')
if ($NoCache) {
    $apiArgs += '--no-cache'
    $webArgs += '--no-cache'
}
$apiArgs += '.'
$webArgs += '.'

Write-Host 'Building taskdeck-api:local...'
docker @apiArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host 'Building taskdeck-web:local...'
docker @webArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host 'Container image build completed.'
