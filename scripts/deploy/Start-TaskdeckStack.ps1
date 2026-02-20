param(
    [switch]$Build,
    [string]$ComposeFile = 'deploy/docker-compose.yml',
    [string]$Profile = 'baseline',
    [string]$EnvFile = 'deploy/.env',
    [switch]$SkipReadyWait,
    [int]$ReadyTimeoutSeconds = 90,
    [string]$ReadyUrl = ''
)

$ErrorActionPreference = 'Stop'

function Get-EnvFileValue {
    param(
        [string]$Path,
        [string]$Key,
        [string]$DefaultValue
    )

    if ($Path -eq '' -or -not (Test-Path $Path)) {
        return $DefaultValue
    }

    $raw = Get-Content -Raw -ErrorAction SilentlyContinue $Path
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return $DefaultValue
    }

    $lines = $raw -split "`r?`n"
    foreach ($line in $lines) {
        $trimmed = $line.Trim()
        if ($trimmed -eq '' -or $trimmed.StartsWith('#')) {
            continue
        }

        $parts = $trimmed -split '=', 2
        if ($parts.Count -ne 2) {
            continue
        }

        if ($parts[0].Trim() -ne $Key) {
            continue
        }

        $value = $parts[1].Trim()
        if ($value.StartsWith('"') -and $value.EndsWith('"') -and $value.Length -ge 2) {
            $value = $value.Substring(1, $value.Length - 2)
        }
        return $value
    }

    return $DefaultValue
}

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

if (-not $SkipReadyWait) {
    if ($ReadyTimeoutSeconds -le 0) {
        throw '-ReadyTimeoutSeconds must be greater than 0.'
    }

    $resolvedReadyUrl = $ReadyUrl
    if ($resolvedReadyUrl -eq '') {
        $proxyPort = Get-EnvFileValue -Path $EnvFile -Key 'TASKDECK_PROXY_PORT' -DefaultValue '8080'
        $resolvedReadyUrl = "http://localhost:$proxyPort/health/ready"
    }

    $deadline = (Get-Date).AddSeconds($ReadyTimeoutSeconds)
    $lastError = ''
    do {
        try {
            $response = Invoke-WebRequest -Uri $resolvedReadyUrl -Method GET -UseBasicParsing -TimeoutSec 5
            if ([int]$response.StatusCode -eq 200) {
                Write-Host "Stack readiness check passed: $resolvedReadyUrl"
                break
            }
            $lastError = "Readiness endpoint returned status $([int]$response.StatusCode)."
        }
        catch {
            if ($_.Exception -and $_.Exception.Response) {
                $statusCode = [int]$_.Exception.Response.StatusCode
                $lastError = "Readiness endpoint returned status $statusCode."
            }
            else {
                $lastError = $_.Exception.Message
            }
        }

        if ((Get-Date) -ge $deadline) {
            throw "Stack readiness check failed for '$resolvedReadyUrl' within $ReadyTimeoutSeconds seconds. Last error: $lastError"
        }

        Start-Sleep -Seconds 2
    } while ($true)
}
