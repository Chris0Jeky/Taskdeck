param(
    [switch]$RemoveVolumes,
    [string]$ComposeFile = 'deploy/docker-compose.yml',
    [string]$Profile = 'baseline',
    [string]$EnvFile = 'deploy/.env'
)

$ErrorActionPreference = 'Stop'

$args = @('compose', '-f', $ComposeFile)
if ($EnvFile -ne '') {
    $args += @('--env-file', $EnvFile)
}
$args += @('--profile', $Profile, 'down')
if ($RemoveVolumes) {
    $args += '--volumes'
}

docker @args
exit $LASTEXITCODE
