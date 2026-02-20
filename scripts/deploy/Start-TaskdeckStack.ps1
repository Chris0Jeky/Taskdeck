param(
    [switch]$Build,
    [string]$ComposeFile = 'deploy/docker-compose.yml',
    [string]$Profile = 'baseline',
    [string]$EnvFile = 'deploy/.env'
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

$statusArgs = @('compose', '-f', $ComposeFile)
if ($EnvFile -ne '') {
    $statusArgs += @('--env-file', $EnvFile)
}
$statusArgs += @('--profile', $Profile, 'ps')

docker @statusArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
