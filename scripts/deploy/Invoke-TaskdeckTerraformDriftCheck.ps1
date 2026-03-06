param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('dev', 'staging', 'prod')]
    [string]$Environment,

    [Parameter(Mandatory = $true)]
    [string]$VarFile,

    [string]$BackendConfigFile,

    [switch]$RefreshOnly
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$repoRoot = Split-Path -Parent $root
$tfDir = Join-Path $repoRoot "deploy/terraform/aws/environments/$Environment"

if (-not (Test-Path $tfDir)) {
    throw "Terraform environment not found: $tfDir"
}

if (-not (Test-Path $VarFile)) {
    throw "Var file not found: $VarFile"
}
$resolvedVarFile = (Resolve-Path $VarFile).ProviderPath

$initArgs = @("-chdir=$tfDir", 'init', '-input=false')
if ($BackendConfigFile) {
    if (-not (Test-Path $BackendConfigFile)) {
        throw "Backend config file not found: $BackendConfigFile"
    }

    $resolvedBackendConfigFile = (Resolve-Path $BackendConfigFile).ProviderPath
    $initArgs += "-backend-config=$resolvedBackendConfigFile"
}

terraform @initArgs | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "terraform init failed for $Environment"
}

$planArgs = @("-chdir=$tfDir", 'plan', '-detailed-exitcode', "-var-file=$resolvedVarFile")
if ($RefreshOnly) {
    $planArgs += '-refresh-only'
}

terraform @planArgs | Out-Host
switch ($LASTEXITCODE) {
    0 {
        Write-Host "No drift detected for $Environment." -ForegroundColor Green
        exit 0
    }
    2 {
        if ($RefreshOnly) {
            Write-Warning "Drift detected for $Environment."
        }
        else {
            Write-Warning "Terraform plan indicates changes for $Environment (non-refresh-only run; these may be intentional configuration changes, not only drift)."
        }
        exit 2
    }
    default {
        throw "terraform plan failed for $Environment with exit code $LASTEXITCODE"
    }
}
