param(
    [string[]]$Environments = @('dev', 'staging', 'prod')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$repoRoot = Split-Path -Parent $root

foreach ($environment in $Environments) {
    $tfDir = Join-Path $repoRoot "deploy/terraform/aws/environments/$environment"
    if (-not (Test-Path $tfDir)) {
        throw "Terraform environment not found: $tfDir"
    }

    Write-Host "Validating Terraform baseline for $environment at $tfDir" -ForegroundColor Cyan
    terraform -chdir="$tfDir" init -backend=false -input=false | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "terraform init failed for $environment"
    }

    terraform -chdir="$tfDir" validate | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "terraform validate failed for $environment"
    }
}

Write-Host 'Terraform baseline validation passed for all requested environments.' -ForegroundColor Green
