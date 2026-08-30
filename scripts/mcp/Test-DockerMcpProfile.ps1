param(
    [string]$DefaultServers = 'docker,docker-docs,time,jetbrains,filesystem,SQLite',
    [string]$OptionalServers = 'postman,dockerhub',
    [switch]$IncludeOptional,
    [switch]$FailOnOptionalErrors,
    [switch]$SkipOptionalWhenMissingPrereqs,
    [switch]$CiMode,
    [string]$DockerExecutable = 'docker'
)

$ErrorActionPreference = 'Stop'

function Convert-ServerCsvToList {
    param([string]$ServerCsv)

    $result = @()
    foreach ($item in ($ServerCsv -split ',')) {
        $normalized = $item.Trim().ToLowerInvariant()
        if ($normalized -ne '' -and -not ($result -contains $normalized)) {
            $result += $normalized
        }
    }
    return $result
}

function Invoke-DockerCommand {
    param(
        [Parameter(Mandatory = $true)] [string[]]$Arguments,
        [switch]$SuppressStdErr
    )

    $global:LASTEXITCODE = $null
    if ($SuppressStdErr) {
        $output = @(& $DockerExecutable @Arguments 2>$null)
    }
    else {
        $output = @(& $DockerExecutable @Arguments)
    }

    $invocationSucceeded = $?
    $exitCode = $global:LASTEXITCODE
    if ($null -eq $exitCode) {
        $exitCode = if ($invocationSucceeded) { 0 } else { 1 }
    }

    return [pscustomobject]@{
        Output = $output
        ExitCode = $exitCode
    }
}

function Get-Sha256Hex {
    param([string]$Value)

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
        $hash = $sha256.ComputeHash($bytes)
        return (([System.BitConverter]::ToString($hash) -replace '-', '').ToLowerInvariant())
    }
    finally {
        $sha256.Dispose()
    }
}

function Get-DockerMcpContainerSnapshot {
    $result = Invoke-DockerCommand -Arguments @(
        'ps',
        '--all',
        '--quiet',
        '--no-trunc',
        '--filter',
        'label=docker-mcp=true'
    )
    if ($result.ExitCode -ne 0) {
        throw 'Failed to inventory Docker MCP containers.'
    }

    $containerIds = @()
    foreach ($line in $result.Output) {
        $containerId = ([string]$line).Trim().ToLowerInvariant()
        if ($containerId -eq '') {
            continue
        }
        if ($containerId -notmatch '^[0-9a-f]{64}$') {
            throw 'Docker MCP container inventory returned an unexpected container identifier.'
        }
        if (-not ($containerIds -contains $containerId)) {
            $containerIds += $containerId
        }
    }

    $containerIds = @($containerIds | Sort-Object)
    $identityPayload = $containerIds -join "`n"
    return [pscustomobject]@{
        Ids = $containerIds
        Count = $containerIds.Count
        Sha256 = Get-Sha256Hex -Value $identityPayload
    }
}

function Compare-DockerMcpContainerSnapshots {
    param(
        [Parameter(Mandatory = $true)] $Before,
        [Parameter(Mandatory = $true)] $After
    )

    $added = @($After.Ids | Where-Object { -not ($Before.Ids -contains $_) })
    $removed = @($Before.Ids | Where-Object { -not ($After.Ids -contains $_) })
    return [pscustomobject]@{
        Added = $added
        Removed = $removed
        IsEqual = ($added.Count -eq 0 -and $removed.Count -eq 0)
    }
}

function Get-DockerMcpProfileServerNames {
    $result = Invoke-DockerCommand -Arguments @('mcp', 'profile', 'server', 'ls', '--format', 'json') -SuppressStdErr
    if ($result.ExitCode -ne 0) {
        throw 'Failed to list Docker MCP profile servers with the read-only profile inventory command.'
    }

    $jsonText = $result.Output -join "`n"
    if ([string]::IsNullOrWhiteSpace($jsonText)) {
        throw 'Docker MCP profile server inventory returned no JSON.'
    }

    try {
        $inventory = $jsonText | ConvertFrom-Json
    }
    catch {
        throw 'Docker MCP profile server inventory returned invalid JSON.'
    }

    $serverNames = @()
    foreach ($item in @($inventory)) {
        $hasServers = $null -ne $item -and $item.PSObject.Properties.Name -contains 'servers'
        if ($hasServers) {
            foreach ($serverEntry in @($item.servers)) {
                $name = $null
                if ($null -ne $serverEntry -and
                    $serverEntry.PSObject.Properties.Name -contains 'snapshot' -and
                    $null -ne $serverEntry.snapshot -and
                    $serverEntry.snapshot.PSObject.Properties.Name -contains 'server' -and
                    $null -ne $serverEntry.snapshot.server -and
                    $serverEntry.snapshot.server.PSObject.Properties.Name -contains 'name') {
                    $name = [string]$serverEntry.snapshot.server.name
                }
                elseif ($null -ne $serverEntry -and $serverEntry.PSObject.Properties.Name -contains 'name') {
                    $name = [string]$serverEntry.name
                }

                if (-not [string]::IsNullOrWhiteSpace($name)) {
                    $normalized = $name.Trim().ToLowerInvariant()
                    if (-not ($serverNames -contains $normalized)) {
                        $serverNames += $normalized
                    }
                }
            }
        }
        elseif ($null -ne $item -and $item.PSObject.Properties.Name -contains 'name') {
            $name = [string]$item.name
            if (-not [string]::IsNullOrWhiteSpace($name)) {
                $normalized = $name.Trim().ToLowerInvariant()
                if (-not ($serverNames -contains $normalized)) {
                    $serverNames += $normalized
                }
            }
        }
    }

    if ($serverNames.Count -eq 0) {
        throw 'Docker MCP profile server inventory contained no recognizable server names.'
    }

    return @($serverNames | Sort-Object)
}

function Get-DockerMcpSecretNames {
    $result = Invoke-DockerCommand -Arguments @('mcp', 'secret', 'ls') -SuppressStdErr
    if ($result.ExitCode -ne 0) {
        throw 'Failed to list Docker MCP secrets.'
    }

    $secretNames = @()
    foreach ($line in $result.Output) {
        $trimmed = ([string]$line).Trim()
        if ($trimmed -eq '') {
            continue
        }

        $name = $trimmed.Split('|')[0].Trim()
        if ($name -ne '' -and -not ($secretNames -contains $name)) {
            $secretNames += $name
        }
    }

    return $secretNames
}

function Get-DockerHubUsernameFromConfig {
    $configPath = Join-Path $env:USERPROFILE '.docker\mcp\config.yaml'
    if (-not (Test-Path $configPath)) {
        return ''
    }

    $rawContent = Get-Content -LiteralPath $configPath -Raw
    if ([string]::IsNullOrWhiteSpace($rawContent)) {
        return ''
    }
    $lines = $rawContent -split "`r?`n"

    $keyPattern = '^[A-Za-z0-9_-]+:\s*$'
    $dockerHubStart = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^\s*dockerhub:\s*$') {
            $dockerHubStart = $i
            break
        }
    }

    if ($dockerHubStart -lt 0) {
        return ''
    }

    $dockerHubEnd = $lines.Count
    for ($j = $dockerHubStart + 1; $j -lt $lines.Count; $j++) {
        if ($lines[$j] -match $keyPattern) {
            $dockerHubEnd = $j
            break
        }
    }

    for ($k = $dockerHubStart + 1; $k -lt $dockerHubEnd; $k++) {
        if ($lines[$k] -match '^\s*username:\s*(.+?)\s*$') {
            $value = $matches[1].Trim()
            if ($value.StartsWith("'") -and $value.EndsWith("'") -and $value.Length -ge 2) {
                $value = $value.Substring(1, $value.Length - 2)
            }
            if ($value.StartsWith('"') -and $value.EndsWith('"') -and $value.Length -ge 2) {
                $value = $value.Substring(1, $value.Length - 2)
            }
            return $value
        }
    }

    return ''
}

function Get-OptionalPrerequisiteWarnings {
    param([string[]]$Servers)

    $warnings = @()
    if ($null -eq $Servers -or $Servers.Count -eq 0) {
        return $warnings
    }

    $requiresSecrets = $false
    foreach ($server in $Servers) {
        if ($server -eq 'postman' -or $server -eq 'dockerhub') {
            $requiresSecrets = $true
            break
        }
    }

    $secretNames = @()
    if ($requiresSecrets) {
        $secretNames = Get-DockerMcpSecretNames
    }

    $dockerHubUsername = ''
    if ($Servers -contains 'dockerhub') {
        $dockerHubUsername = Get-DockerHubUsernameFromConfig
    }

    foreach ($server in $Servers) {
        switch ($server) {
            'postman' {
                if (-not ($secretNames -contains 'postman.postman-api-key')) {
                    $warnings += "[postman] Missing secret 'postman.postman-api-key'. Set it with: echo '<key>' | docker mcp secret set postman.postman-api-key"
                }
            }
            'dockerhub' {
                if (-not ($secretNames -contains 'dockerhub.pat_token')) {
                    $warnings += "[dockerhub] Missing secret 'dockerhub.pat_token'. Set it with: echo '<pat>' | docker mcp secret set dockerhub.pat_token"
                }
                if ([string]::IsNullOrWhiteSpace($dockerHubUsername)) {
                    $warnings += "[dockerhub] Missing config 'dockerhub.username' in $env:USERPROFILE\.docker\mcp\config.yaml"
                }
            }
            default {
                $warnings += "[$server] No prerequisite checks are defined. Read-only profile membership is the only validation performed."
            }
        }
    }

    return $warnings
}

function Get-MissingServers {
    param(
        [string[]]$RequestedServers,
        [string[]]$ConfiguredServers
    )

    return @($RequestedServers | Where-Object { -not ($ConfiguredServers -contains $_) })
}

function Write-CiResult {
    param(
        [string]$Result,
        [string]$Message = ''
    )

    if (-not $CiMode) {
        return
    }

    Write-Host 'MCP_PROFILE_PROBE=READ_ONLY_PROFILE'
    Write-Host "MCP_PROFILE_RESULT=$Result"
    if ($Message -ne '') {
        Write-Host "MCP_PROFILE_MESSAGE=$Message"
    }
}

function Write-CiContainerEvidence {
    param(
        [Parameter(Mandatory = $true)] $Before,
        [Parameter(Mandatory = $true)] $After,
        [Parameter(Mandatory = $true)] $Comparison
    )

    if (-not $CiMode) {
        return
    }

    Write-Host "MCP_PROFILE_CONTAINERS_BEFORE_COUNT=$($Before.Count)"
    Write-Host "MCP_PROFILE_CONTAINERS_BEFORE_SHA256=$($Before.Sha256)"
    Write-Host "MCP_PROFILE_CONTAINERS_AFTER_COUNT=$($After.Count)"
    Write-Host "MCP_PROFILE_CONTAINERS_AFTER_SHA256=$($After.Sha256)"
    Write-Host "MCP_PROFILE_CONTAINERS_ADDED=$($Comparison.Added.Count)"
    Write-Host "MCP_PROFILE_CONTAINERS_REMOVED=$($Comparison.Removed.Count)"
}

$hadWarnings = $false
$validationError = $null
$beforeSnapshot = $null
$afterSnapshot = $null
$snapshotComparison = $null

try {
    $defaultServerList = Convert-ServerCsvToList -ServerCsv $DefaultServers
    if ($defaultServerList.Count -eq 0) {
        throw 'At least one default Docker MCP server must be requested.'
    }

    $beforeSnapshot = Get-DockerMcpContainerSnapshot
    Write-Host "Docker MCP container state before validation: count=$($beforeSnapshot.Count) sha256=$($beforeSnapshot.Sha256)"

    Write-Host '=== Docker MCP Read-Only Profile Inventory ==='
    $configuredServers = Get-DockerMcpProfileServerNames
    Write-Host "Configured server names: $($configuredServers -join ',')"

    $missingDefaultServers = Get-MissingServers -RequestedServers $defaultServerList -ConfiguredServers $configuredServers
    if ($missingDefaultServers.Count -gt 0) {
        throw "Required Docker MCP server(s) are absent from the read-only profile inventory: $($missingDefaultServers -join ',')."
    }

    if ($IncludeOptional) {
        $optionalServerList = Convert-ServerCsvToList -ServerCsv $OptionalServers
        $prereqWarnings = Get-OptionalPrerequisiteWarnings -Servers $optionalServerList

        if ($prereqWarnings.Count -gt 0) {
            $hadWarnings = $true
            Write-Warning 'Optional MCP prerequisite diagnostics:'
            foreach ($warningText in $prereqWarnings) {
                Write-Warning " - $warningText"
            }

            if ($FailOnOptionalErrors) {
                throw 'Optional Docker MCP prerequisites are missing. Resolve missing prerequisite(s) and rerun.'
            }

            if ($SkipOptionalWhenMissingPrereqs) {
                Write-Warning 'Skipping optional profile membership validation due to missing prerequisites.'
            }
        }

        if (-not ($SkipOptionalWhenMissingPrereqs -and $prereqWarnings.Count -gt 0)) {
            $missingOptionalServers = Get-MissingServers -RequestedServers $optionalServerList -ConfiguredServers $configuredServers
            if ($missingOptionalServers.Count -gt 0) {
                if ($FailOnOptionalErrors) {
                    throw "Optional Docker MCP server(s) are absent from the read-only profile inventory: $($missingOptionalServers -join ',')."
                }

                $hadWarnings = $true
                Write-Warning "Optional Docker MCP server(s) are absent from the read-only profile inventory: $($missingOptionalServers -join ',')."
            }
        }
    }
}
catch {
    $validationError = $_.Exception.Message
}

if ($null -ne $beforeSnapshot) {
    try {
        $afterSnapshot = Get-DockerMcpContainerSnapshot
        $snapshotComparison = Compare-DockerMcpContainerSnapshots -Before $beforeSnapshot -After $afterSnapshot
        Write-Host "Docker MCP container state after validation: count=$($afterSnapshot.Count) sha256=$($afterSnapshot.Sha256)"
        Write-CiContainerEvidence -Before $beforeSnapshot -After $afterSnapshot -Comparison $snapshotComparison

        if (-not $snapshotComparison.IsEqual) {
            $stateError = "Docker MCP container state changed during read-only validation (added=$($snapshotComparison.Added.Count), removed=$($snapshotComparison.Removed.Count)). No cleanup was attempted because this invocation does not own any Docker MCP containers."
            if ([string]::IsNullOrWhiteSpace($validationError)) {
                $validationError = $stateError
            }
            else {
                $validationError = "$validationError $stateError"
            }
        }
    }
    catch {
        $stateError = "Unable to prove Docker MCP container state after validation: $($_.Exception.Message)"
        if ([string]::IsNullOrWhiteSpace($validationError)) {
            $validationError = $stateError
        }
        else {
            $validationError = "$validationError $stateError"
        }
    }
}

if (-not [string]::IsNullOrWhiteSpace($validationError)) {
    Write-CiResult -Result 'FAIL' -Message $validationError
    Write-Error $validationError
    exit 1
}

Write-Host ''
if ($hadWarnings) {
    Write-Host 'Docker MCP read-only profile checks passed with warnings and no container-state drift.'
    Write-CiResult -Result 'PASS_WITH_WARNINGS' -Message 'Review warning output.'
}
else {
    Write-Host 'Docker MCP read-only profile checks passed with no container-state drift.'
    Write-CiResult -Result 'PASS'
}

exit 0
