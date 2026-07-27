[CmdletBinding()]
param(
    [string]$ConfigPath,
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $ConfigPath = Join-Path (Join-Path $repositoryRoot 'backend') 'stryker-config.json'
}

function Test-StrykerConfig {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $resolvedPath = Resolve-Path -LiteralPath $Path -ErrorAction Stop

    try {
        $config = Get-Content -LiteralPath $resolvedPath -Raw | ConvertFrom-Json -ErrorAction Stop
    } catch {
        throw "Stryker configuration '$resolvedPath' is not valid JSON: $($_.Exception.Message)"
    }

    $strykerConfig = $config.'stryker-config'
    if ($null -eq $strykerConfig) {
        throw "Stryker configuration '$resolvedPath' must contain a stryker-config object."
    }

    foreach ($obsoleteKey in @('ignored-methods', 'excluded-mutations')) {
        if ($null -ne $strykerConfig.PSObject.Properties[$obsoleteKey]) {
            $replacementKey = $obsoleteKey -replace '^excluded-', 'ignore-' -replace '^ignored-', 'ignore-'
            throw "Stryker configuration '$resolvedPath' uses obsolete '$obsoleteKey'. Use '$replacementKey' instead."
        }
    }

    foreach ($requiredKey in @('ignore-methods', 'ignore-mutations')) {
        $property = $strykerConfig.PSObject.Properties[$requiredKey]
        if ($null -eq $property) {
            throw "Stryker configuration '$resolvedPath' must contain '$requiredKey' as an array."
        }

        if ($property.Value -isnot [System.Array]) {
            throw "Stryker configuration '$resolvedPath' must define '$requiredKey' as an array."
        }
    }
}

function Invoke-StrykerConfigSelfTest {
    $temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("taskdeck-stryker-config-$([guid]::NewGuid().ToString('N'))")
    $validPath = Join-Path $temporaryDirectory 'valid.json'

    try {
        New-Item -ItemType Directory -Path $temporaryDirectory -ErrorAction Stop | Out-Null
        Copy-Item -LiteralPath $ConfigPath -Destination $validPath -ErrorAction Stop
        Test-StrykerConfig -Path $validPath

        foreach ($obsoleteKey in @('ignored-methods', 'excluded-mutations')) {
            $obsoletePath = Join-Path $temporaryDirectory "$obsoleteKey.json"
            @"
{
  "stryker-config": {
    "ignore-methods": [],
    "ignore-mutations": [],
    "$obsoleteKey": []
  }
}
"@ | Set-Content -LiteralPath $obsoletePath -Encoding UTF8

            $obsoleteKeyWasRejected = $false
            try {
                Test-StrykerConfig -Path $obsoletePath
            } catch {
                if ($_.Exception.Message -notmatch "obsolete '$obsoleteKey'") {
                    throw
                }

                $obsoleteKeyWasRejected = $true
            }

            if (-not $obsoleteKeyWasRejected) {
                throw "The Stryker configuration preflight accepted obsolete key '$obsoleteKey'."
            }
        }
    } finally {
        if (Test-Path -LiteralPath $temporaryDirectory) {
            Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
        }
    }
}

Test-StrykerConfig -Path $ConfigPath

if ($SelfTest) {
    Invoke-StrykerConfigSelfTest
}

Write-Host "Stryker configuration preflight passed: $ConfigPath"
