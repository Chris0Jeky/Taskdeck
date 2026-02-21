param(
    [string]$DefaultServers = 'docker,docker-docs,openapi,time,jetbrains,filesystem,SQLite,terraform',
    [string]$OptionalServers = 'postman,dockerhub',
    [switch]$IncludeOptional,
    [switch]$FailOnOptionalErrors,
    [switch]$SkipOptionalWhenMissingPrereqs,
    [switch]$CiMode
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

function Get-DockerMcpSecretNames {
    $secretOutput = docker mcp secret ls 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw 'Failed to list Docker MCP secrets.'
    }

    $secretNames = @()
    foreach ($line in ($secretOutput -split "`r?`n")) {
        $trimmed = $line.Trim()
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

    $lines = Get-Content $configPath
    if ($null -eq $lines -or $lines.Count -eq 0) {
        return ''
    }

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
                $warnings += "[$server] No prerequisite checks are defined. Dry-run output will be used as the source of truth."
            }
        }
    }

    return $warnings
}

function Write-CiResult {
    param(
        [string]$Result,
        [string]$Message = ''
    )

    if (-not $CiMode) {
        return
    }

    Write-Host "MCP_PROFILE_RESULT=$Result"
    if ($Message -ne '') {
        Write-Host "MCP_PROFILE_MESSAGE=$Message"
    }
}

$hadWarnings = $false

try {
    Write-Host '=== Docker MCP Enabled Servers ==='
    docker mcp server ls
    if ($LASTEXITCODE -ne 0) {
        throw 'Failed to list Docker MCP servers.'
    }

    Write-Host ''
    Write-Host "=== Docker MCP Gateway Dry-Run (Default): $DefaultServers ==="
    docker mcp gateway run --dry-run --servers $DefaultServers
    if ($LASTEXITCODE -ne 0) {
        throw 'Default Docker MCP server dry-run failed.'
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
                Write-Warning 'Skipping optional Docker MCP dry-run due to missing prerequisites.'
            }
        }

        if (-not ($SkipOptionalWhenMissingPrereqs -and $prereqWarnings.Count -gt 0)) {
            Write-Host ''
            Write-Host "=== Docker MCP Gateway Dry-Run (Optional): $OptionalServers ==="
            docker mcp gateway run --dry-run --servers $OptionalServers
            $optionalExit = $LASTEXITCODE
            if ($optionalExit -ne 0) {
                if ($FailOnOptionalErrors) {
                    throw 'Optional Docker MCP server dry-run failed.'
                }

                $hadWarnings = $true
                Write-Warning 'Optional Docker MCP dry-run failed. Review output above for runtime/provider errors.'
            }
        }
    }

    Write-Host ''
    if ($hadWarnings) {
        Write-Host 'Docker MCP profile checks passed with warnings.'
        Write-CiResult -Result 'PASS_WITH_WARNINGS' -Message 'Review warning output.'
    }
    else {
        Write-Host 'Docker MCP profile checks passed.'
        Write-CiResult -Result 'PASS'
    }

    exit 0
}
catch {
    $message = $_.Exception.Message
    Write-CiResult -Result 'FAIL' -Message $message
    Write-Error $message
    exit 1
}

