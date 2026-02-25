param(
    [int]$Port = 8080,
    [string]$ComposeFile = 'deploy/docker-compose.yml',
    [string]$Profile = 'baseline',
    [string]$EnvFile = 'deploy/.env',
    [int]$ReadyTimeoutSeconds = 90,
    [switch]$Build,
    [switch]$SkipSecretEnforcementCheck,
    [switch]$SkipRestartCheck
)

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

function Invoke-ComposeCommand {
    param(
        [Parameter(Mandatory = $true)] [string[]]$Arguments,
        [switch]$AllowFailure
    )

    $composeArgs = @('compose', '-f', $ComposeFile)
    if ($EnvFile -ne '') {
        $composeArgs += @('--env-file', $EnvFile)
    }
    if ($Profile -ne '') {
        $composeArgs += @('--profile', $Profile)
    }
    $composeArgs += $Arguments

    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & docker @composeArgs 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }
    $outputText = ($output | Out-String).Trim()

    if (-not $AllowFailure -and $exitCode -ne 0) {
        throw "docker compose command failed (exit $exitCode): docker $($composeArgs -join ' ')`n$outputText"
    }

    return [PSCustomObject]@{
        ExitCode = $exitCode
        Output = $outputText
    }
}

function Assert-HeaderValue {
    param(
        [Parameter(Mandatory = $true)] [Microsoft.PowerShell.Commands.WebResponseObject]$Response,
        [Parameter(Mandatory = $true)] [string]$HeaderName,
        [Parameter(Mandatory = $true)] [string]$ExpectedValue,
        [switch]$Exact
    )

    $actualValue = $Response.Headers[$HeaderName]
    if ([string]::IsNullOrWhiteSpace($actualValue)) {
        throw "Missing expected response header '$HeaderName'."
    }

    if ($Exact) {
        if ($actualValue -ne $ExpectedValue) {
            throw "Header '$HeaderName' expected '$ExpectedValue' but got '$actualValue'."
        }
        return
    }

    if ($actualValue -notlike "*$ExpectedValue*") {
        throw "Header '$HeaderName' does not contain expected value '$ExpectedValue'. Actual: '$actualValue'."
    }
}

function Wait-ForReady {
    param(
        [Parameter(Mandatory = $true)] [string]$ReadyUrl,
        [Parameter(Mandatory = $true)] [int]$TimeoutSeconds
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $lastError = ''

    do {
        try {
            $response = Invoke-WebRequest -Uri $ReadyUrl -Method GET -UseBasicParsing -TimeoutSec 5
            if ([int]$response.StatusCode -eq 200) {
                return
            }

            $lastError = "Status code $([int]$response.StatusCode)."
        }
        catch {
            if ($_.Exception -and $_.Exception.Response) {
                $lastError = "Status code $([int]$_.Exception.Response.StatusCode)."
            }
            else {
                $lastError = $_.Exception.Message
            }
        }

        if ((Get-Date) -ge $deadline) {
            throw "Readiness check failed for '$ReadyUrl' after $TimeoutSeconds seconds. Last error: $lastError"
        }

        Start-Sleep -Seconds 2
    } while ($true)
}

function Test-SecretEnforcement {
    $secretEnvFile = 'deploy/.env.example'
    if (-not (Test-Path $secretEnvFile)) {
        throw "Secret enforcement check requires '$secretEnvFile'."
    }

    $previousSecret = [Environment]::GetEnvironmentVariable('TASKDECK_JWT_SECRET', 'Process')
    [Environment]::SetEnvironmentVariable('TASKDECK_JWT_SECRET', $null, 'Process')

    try {
        $args = @('compose', '-f', $ComposeFile, '--env-file', $secretEnvFile)
        if ($Profile -ne '') {
            $args += @('--profile', $Profile)
        }
        $args += 'config'

        $previousPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            $output = & docker @args 2>&1
            $exitCode = $LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $previousPreference
        }
        $outputText = ($output | Out-String).Trim()

        if ($exitCode -eq 0) {
            throw "Secret enforcement check failed: docker compose config unexpectedly succeeded without TASKDECK_JWT_SECRET."
        }

        if ($outputText -notmatch 'TASKDECK_JWT_SECRET must be set') {
            throw "Secret enforcement check failed: expected missing-secret error was not found. Output:`n$outputText"
        }
    }
    finally {
        [Environment]::SetEnvironmentVariable('TASKDECK_JWT_SECRET', $previousSecret, 'Process')
    }
}

function Test-ReverseProxyHeaders {
    param(
        [Parameter(Mandatory = $true)] [string]$BaseUrl
    )

    $response = Invoke-WebRequest -Uri "$BaseUrl/" -UseBasicParsing
    if ([int]$response.StatusCode -ne 200) {
        throw "Unexpected status code for root endpoint. Expected 200 but got $([int]$response.StatusCode)."
    }

    Assert-HeaderValue -Response $response -HeaderName 'X-Content-Type-Options' -ExpectedValue 'nosniff' -Exact
    Assert-HeaderValue -Response $response -HeaderName 'X-Frame-Options' -ExpectedValue 'SAMEORIGIN' -Exact
    Assert-HeaderValue -Response $response -HeaderName 'Referrer-Policy' -ExpectedValue 'strict-origin-when-cross-origin' -Exact
    Assert-HeaderValue -Response $response -HeaderName 'Permissions-Policy' -ExpectedValue 'geolocation=()'
    Assert-HeaderValue -Response $response -HeaderName 'Content-Security-Policy' -ExpectedValue "default-src 'self'"
}

function Test-RestartReliability {
    param(
        [Parameter(Mandatory = $true)] [string]$BaseUrl
    )

    Invoke-ComposeCommand -Arguments @('restart', 'proxy') | Out-Null
    Wait-ForReady -ReadyUrl "$BaseUrl/health/ready" -TimeoutSeconds $ReadyTimeoutSeconds
}

$repoRoot = Resolve-Path (Join-Path $scriptRoot '..\..')
Push-Location $repoRoot

try {
    $baseUrl = "http://localhost:$Port"
    $stackStarted = $false
    $startAttempted = $false

    try {
        if (-not $SkipSecretEnforcementCheck) {
            Test-SecretEnforcement
            Write-Host 'Secret enforcement check passed.'
        }

        $startScriptArgs = @(
            '-File', (Join-Path $scriptRoot 'Start-TaskdeckStack.ps1'),
            '-ComposeFile', $ComposeFile,
            '-Profile', $Profile,
            '-EnvFile', $EnvFile,
            '-ReadyTimeoutSeconds', $ReadyTimeoutSeconds.ToString()
        )
        if ($Build) {
            $startScriptArgs += '-Build'
        }

        $startAttempted = $true
        & powershell @startScriptArgs
        if ($LASTEXITCODE -ne 0) {
            throw "Stack startup script failed with exit code $LASTEXITCODE."
        }

        $stackStarted = $true

        & powershell -File (Join-Path $scriptRoot 'Smoke-TestTaskdeckStack.ps1') -Port $Port
        if ($LASTEXITCODE -ne 0) {
            throw "Smoke test script failed with exit code $LASTEXITCODE."
        }
        Write-Host 'Unauthorized behavior checks passed via smoke test script.'

        Test-ReverseProxyHeaders -BaseUrl $baseUrl
        Write-Host 'Reverse proxy header posture checks passed.'

        if (-not $SkipRestartCheck) {
            Test-RestartReliability -BaseUrl $baseUrl
            & powershell -File (Join-Path $scriptRoot 'Smoke-TestTaskdeckStack.ps1') -Port $Port
            if ($LASTEXITCODE -ne 0) {
                throw "Post-restart smoke test failed with exit code $LASTEXITCODE."
            }
            Write-Host 'Startup/restart reliability checks passed.'
        }
    }
    finally {
        $shouldTeardown = $stackStarted
        if (-not $shouldTeardown -and $startAttempted) {
            $runningServicesProbe = Invoke-ComposeCommand -Arguments @('ps', '--status', 'running', '--services') -AllowFailure
            if ($runningServicesProbe.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($runningServicesProbe.Output)) {
                $shouldTeardown = $true
                Write-Warning 'Detected running services after startup failure; attempting teardown cleanup.'
            }
        }

        if ($shouldTeardown) {
            & powershell -File (Join-Path $scriptRoot 'Stop-TaskdeckStack.ps1') -ComposeFile $ComposeFile -Profile $Profile -EnvFile $EnvFile
            if ($LASTEXITCODE -ne 0) {
                throw "Stack shutdown script failed with exit code $LASTEXITCODE."
            }

            $runningServices = Invoke-ComposeCommand -Arguments @('ps', '--status', 'running', '--services') -AllowFailure
            if ($runningServices.ExitCode -ne 0) {
                throw "Failed to inspect running services after shutdown: $($runningServices.Output)"
            }
            if (-not [string]::IsNullOrWhiteSpace($runningServices.Output)) {
                throw "Expected no running services after shutdown, but found: $($runningServices.Output)"
            }

            Write-Host 'Shutdown reliability check passed (no services left running).'
        }
    }

    Write-Host 'Deployment hardening verification checks passed.'
}
finally {
    Pop-Location
}
