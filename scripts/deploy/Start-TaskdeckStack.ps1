param(
    [switch]$Build,
    [string]$ComposeFile = 'deploy/docker-compose.yml',
    [string]$Profile = 'baseline',
    [string]$EnvFile = ''
)

$ErrorActionPreference = 'Stop'

$args = @('compose', '-f', $ComposeFile)
if ($EnvFile -ne '') {
    $args += @('--env-file', $EnvFile)
}
$args += @('--profile', $Profile, 'up', '-d')
if ($Build) {
    $args += '--build'
}

docker @args
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

docker compose -f $ComposeFile --profile $Profile ps
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
