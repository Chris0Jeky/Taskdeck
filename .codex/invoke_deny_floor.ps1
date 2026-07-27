[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$AdapterArguments
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Write-AdapterDeny {
    param([Parameter(Mandatory = $true)][string]$Reason)

    $document = @{
        hookSpecificOutput = @{
            hookEventName = "PreToolUse"
            permissionDecision = "deny"
            permissionDecisionReason = "[Taskdeck Codex deny-floor adapter] $Reason"
        }
    } | ConvertTo-Json -Compress
    [Console]::Out.WriteLine($document)
    exit 0
}

try {
    $core = Join-Path $PSScriptRoot "deny_floor_adapter.py"
    if (-not (Test-Path -LiteralPath $core -PathType Leaf)) {
        Write-AdapterDeny "Windows bridge is missing; fix the reviewed project hook before proceeding"
    }

    $systemRoot = [Environment]::GetEnvironmentVariable("SystemRoot", "Machine")
    if ([string]::IsNullOrWhiteSpace($systemRoot)) {
        $systemRoot = $env:SystemRoot
    }
    $launcher = Join-Path $systemRoot "py.exe"
    if (-not (Test-Path -LiteralPath $launcher -PathType Leaf)) {
        Write-AdapterDeny "verified Windows Python launcher is missing; fix the project hook before proceeding"
    }

    $stderrPath = [IO.Path]::GetTempFileName()
    try {
        $output = @(& $launcher -3 -B $core @AdapterArguments 2> $stderrPath)
        $code = $LASTEXITCODE
        $stderr = [IO.File]::ReadAllText($stderrPath)
    }
    finally {
        Remove-Item -LiteralPath $stderrPath -ErrorAction SilentlyContinue
    }
    if ($code -ne 0) {
        Write-AdapterDeny "Windows bridge failed; fix the reviewed project hook before proceeding"
    }
    if ($stderr.Length -gt 0) {
        Write-AdapterDeny "Windows bridge wrote unexpected diagnostic output; fix the reviewed project hook before proceeding"
    }
    if ($output.Count -gt 0) {
        [Console]::Out.WriteLine(($output -join [Environment]::NewLine))
    }
    exit 0
}
catch {
    Write-AdapterDeny "Windows launcher failed closed; fix the reviewed project hook before proceeding"
}
