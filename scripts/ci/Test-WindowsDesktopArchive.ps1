[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ArchivePath,

    [Parameter(Mandatory = $true)]
    [string]$ChecksumPath,

    [Parameter(Mandatory = $true)]
    [string]$EvidencePath,

    [Parameter(Mandatory = $true)]
    [string]$FrontendDirectory,

    [switch]$LiveOpenAI
)

$ErrorActionPreference = "Stop"
$harness = Join-Path $PSScriptRoot "windows_desktop_archive.py"
$python = Get-Command py -CommandType Application -ErrorAction Stop

$arguments = @(
    "-3",
    "-B",
    $harness,
    "--archive", $ArchivePath,
    "--checksum", $ChecksumPath,
    "--evidence", $EvidencePath,
    "--frontend-directory", $FrontendDirectory
)
if ($LiveOpenAI) {
    $arguments += "--live-openai"
}

& $python.Source @arguments
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
