param(
    [string]$PostmanApiKey = '',
    [string]$DockerHubUsername = '',
    [string]$DockerHubPatToken = '',
    [switch]$UseEnvironment,
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'

function Resolve-Value {
    param(
        [string]$ExplicitValue,
        [string]$EnvVarName,
        [switch]$UseEnvironmentSwitch
    )

    if ($ExplicitValue -ne '') {
        return $ExplicitValue
    }

    $envValue = [Environment]::GetEnvironmentVariable($EnvVarName)
    if ($UseEnvironmentSwitch -and -not [string]::IsNullOrWhiteSpace($envValue)) {
        return $envValue
    }

    return ''
}

function Set-DockerMcpSecret {
    param(
        [Parameter(Mandatory = $true)] [string]$SecretName,
        [Parameter(Mandatory = $true)] [string]$SecretValue
    )

    $SecretValue | docker mcp secret set $SecretName | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to set Docker MCP secret '$SecretName'."
    }
}

function Set-DockerHubUsernameConfig {
    param(
        [Parameter(Mandatory = $true)] [string]$Username
    )

    $configPath = Join-Path $env:USERPROFILE '.docker\mcp\config.yaml'
    $configDir  = Split-Path $configPath -Parent
    if (-not (Test-Path $configDir)) {
        New-Item -ItemType Directory -Path $configDir -Force | Out-Null
    }
    if (-not (Test-Path $configPath)) {
        New-Item -ItemType File -Path $configPath -Force | Out-Null
    }

    $raw = Get-Content -Raw -ErrorAction SilentlyContinue $configPath
    if ($null -eq $raw) {
        $raw = ''
    }

    $lines = @()
    if ($raw -ne '') {
        $lines = $raw -split "`r?`n"
    }

    $keyPattern = '^[A-Za-z0-9_-]+:\s*$'
    $dockerHubStart = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^\s*dockerhub:\s*$') {
            $dockerHubStart = $i
            break
        }
    }

    $usernameLine = "  username: '$($Username -replace "'", "''")'"

    if ($dockerHubStart -eq -1) {
        if ($lines.Count -gt 0 -and $lines[$lines.Count - 1] -ne '') {
            $lines += ''
        }
        $lines += 'dockerhub:'
        $lines += $usernameLine
    }
    else {
        $dockerHubEnd = $lines.Count
        for ($j = $dockerHubStart + 1; $j -lt $lines.Count; $j++) {
            if ($lines[$j] -match $keyPattern) {
                $dockerHubEnd = $j
                break
            }
        }

        $usernameIndex = -1
        for ($k = $dockerHubStart + 1; $k -lt $dockerHubEnd; $k++) {
            if ($lines[$k] -match '^\s*username:\s*') {
                $usernameIndex = $k
                break
            }
        }

        if ($usernameIndex -ge 0) {
            $lines[$usernameIndex] = $usernameLine
        }
        else {
            $before = $lines[0..$dockerHubStart]
            $after = @()
            if ($dockerHubStart + 1 -lt $lines.Count) {
                $after = $lines[($dockerHubStart + 1)..($lines.Count - 1)]
            }
            $lines = @($before + @($usernameLine) + $after)
        }
    }

    $content = ($lines -join [Environment]::NewLine).TrimEnd()
    Set-Content -Path $configPath -Value $content -Encoding UTF8
}

$resolvedPostmanKey = Resolve-Value -ExplicitValue $PostmanApiKey -EnvVarName 'POSTMAN_API_KEY' -UseEnvironmentSwitch:$UseEnvironment
$resolvedDockerHubUsername = Resolve-Value -ExplicitValue $DockerHubUsername -EnvVarName 'DOCKERHUB_USERNAME' -UseEnvironmentSwitch:$UseEnvironment
$resolvedDockerHubPat = Resolve-Value -ExplicitValue $DockerHubPatToken -EnvVarName 'HUB_PAT_TOKEN' -UseEnvironmentSwitch:$UseEnvironment

$didChange = $false

if ($resolvedPostmanKey -ne '') {
    Set-DockerMcpSecret -SecretName 'postman.postman-api-key' -SecretValue $resolvedPostmanKey
    Write-Host "Configured secret: postman.postman-api-key"
    $didChange = $true
}

if ($resolvedDockerHubPat -ne '') {
    Set-DockerMcpSecret -SecretName 'dockerhub.pat_token' -SecretValue $resolvedDockerHubPat
    Write-Host "Configured secret: dockerhub.pat_token"
    $didChange = $true
}

if ($resolvedDockerHubUsername -ne '') {
    Set-DockerHubUsernameConfig -Username $resolvedDockerHubUsername
    Write-Host "Configured Docker MCP config: dockerhub.username"
    $didChange = $true
}

if (-not $didChange) {
    Write-Host 'No credentials were configured.'
    Write-Host 'Provide values via params or pass -UseEnvironment with one or more of:'
    Write-Host '  POSTMAN_API_KEY, DOCKERHUB_USERNAME, HUB_PAT_TOKEN'
    exit 1
}

Write-Host 'Docker MCP secret names currently stored:'
docker mcp secret ls
if ($LASTEXITCODE -ne 0) {
    throw 'Failed to list Docker MCP secrets.'
}

if ($Verify) {
    $servers = @()
    if ($resolvedPostmanKey -ne '') { $servers += 'postman' }
    if ($resolvedDockerHubPat -ne '' -and $resolvedDockerHubUsername -ne '') { $servers += 'dockerhub' }

    if ($servers.Count -eq 0) {
        Write-Host 'No optional server had both required credentials for verification.'
        exit 0
    }

    $serverCsv = ($servers -join ',')
    docker mcp gateway run --dry-run --servers $serverCsv
    exit $LASTEXITCODE
}
