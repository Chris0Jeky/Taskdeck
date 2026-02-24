param(
    [Parameter(Mandatory = $true)]
    [string]$SpecPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not (Test-Path -LiteralPath $SpecPath -PathType Leaf))
{
    throw "OpenAPI spec file not found: $SpecPath"
}

$raw = Get-Content -LiteralPath $SpecPath -Raw
if ([string]::IsNullOrWhiteSpace($raw))
{
    throw "OpenAPI spec file is empty: $SpecPath"
}

try
{
    $document = $raw | ConvertFrom-Json
}
catch
{
    throw "OpenAPI spec is not valid JSON: $SpecPath`n$($_.Exception.Message)"
}

if ([string]::IsNullOrWhiteSpace([string]$document.openapi))
{
    throw "OpenAPI spec is missing non-empty 'openapi' version."
}

if ($null -eq $document.info)
{
    throw "OpenAPI spec is missing 'info' object."
}

if ([string]::IsNullOrWhiteSpace([string]$document.info.title))
{
    throw "OpenAPI spec is missing non-empty 'info.title'."
}

if ([string]::IsNullOrWhiteSpace([string]$document.info.version))
{
    throw "OpenAPI spec is missing non-empty 'info.version'."
}

$pathsProperty = $document.PSObject.Properties['paths']
if ($null -eq $pathsProperty)
{
    throw "OpenAPI spec is missing 'paths' object."
}

$pathCount = ($pathsProperty.Value.PSObject.Properties | Measure-Object).Count
if ($pathCount -lt 1)
{
    throw "OpenAPI spec contains zero paths."
}

Write-Host "OpenAPI spec validated: version=$($document.openapi) title='$($document.info.title)' paths=$pathCount"
