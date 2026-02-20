param(
    [switch]$RemoveVolumes,
    [string]$ComposeFile = 'deploy/docker-compose.yml',
    [string]$Profile = 'baseline'
)

$ErrorActionPreference = 'Stop'

$args = @('compose', '-f', $ComposeFile, '--profile', $Profile, 'down')
if ($RemoveVolumes) {
    $args += '--volumes'
}

docker @args
exit $LASTEXITCODE
