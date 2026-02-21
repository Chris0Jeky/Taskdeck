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

function Resolve-ProxyPort {
    param(
        [string]$ComposeFilePath,
        [string]$EnvFilePath
    )

    $environmentPort = [Environment]::GetEnvironmentVariable('TASKDECK_PROXY_PORT')
    if (-not [string]::IsNullOrWhiteSpace($environmentPort)) {
        return $environmentPort.Trim()
    }

    $envFilePort = Get-EnvFileValue -Path $EnvFilePath -Key 'TASKDECK_PROXY_PORT' -DefaultValue ''
    if (-not [string]::IsNullOrWhiteSpace($envFilePort)) {
        return $envFilePort.Trim()
    }

    try {
        $portArgs = @('compose', '-f', $ComposeFilePath)
        if ($EnvFilePath -ne '') {
            $portArgs += @('--env-file', $EnvFilePath)
        }
        $portArgs += @('port', 'proxy', '8080')

        $composePortOutput = docker @portArgs 2>$null
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($composePortOutput)) {
            $portLines = $composePortOutput -split "`r?`n"
            foreach ($line in $portLines) {
                $trimmed = $line.Trim()
                if ([string]::IsNullOrWhiteSpace($trimmed)) {
                    continue
                }

                $portPart = $trimmed.Split(':')[-1]
                if ($portPart -match '^\d+$') {
                    return $portPart
                }
            }
        }
    }
    catch {
        # Ignore and fall back to default.
    }

    return '8080'
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
        $proxyPort = Resolve-ProxyPort -ComposeFilePath $ComposeFile -EnvFilePath $EnvFile
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
