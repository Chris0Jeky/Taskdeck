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

    [switch]$LiveOpenAI,

    [switch]$LiveOpenAIIfConfigured
)

$ErrorActionPreference = "Stop"
if ($LiveOpenAI -and $LiveOpenAIIfConfigured) {
    [Console]::Error.WriteLine("Hosted acceptance switches cannot be combined.")
    exit 1
}

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
if ($LiveOpenAIIfConfigured) {
    $arguments += "--live-openai-if-configured"
}

$previousErrorActionPreference = $ErrorActionPreference
try {
    $ErrorActionPreference = "Continue"
    $nativeOutput = & $python.Source @arguments 2>&1
    $nativeExitCode = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = $previousErrorActionPreference
}
foreach ($line in $nativeOutput) {
    Write-Output ($line.ToString())
}
if ($nativeExitCode -ne 0) {
    exit $nativeExitCode
}
