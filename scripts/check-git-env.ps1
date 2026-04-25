[CmdletBinding()]
param(
    [string]$RepoDir
)

$ErrorActionPreference = "Stop"
$exitCode = 0

if ([string]::IsNullOrWhiteSpace($RepoDir)) {
    $detectedRoot = (& git rev-parse --show-toplevel 2>$null)
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($detectedRoot)) {
        $RepoDir = $detectedRoot.Trim()
    } else {
        $RepoDir = (Get-Location).Path
    }
}

$resolvedRepo = (Resolve-Path -LiteralPath $RepoDir).Path
Write-Host "[INFO]  repo path:   $resolvedRepo"

$gitCommand = Get-Command git -ErrorAction SilentlyContinue
if ($null -eq $gitCommand) {
    Write-Host "[ERROR] git is not on PATH. Install Git for Windows from https://git-scm.com"
    exit 1
}

$gitPath = $gitCommand.Source
$gitVersion = (& git --version 2>$null)
Write-Host "[INFO]  git path:    $gitPath"
Write-Host "[INFO]  git version: $gitVersion"

if ($gitPath -match "\\cygwin\\" -or $gitPath -match "\\msys64\\usr\\bin\\git\.exe$" -or $gitVersion -match "cygwin") {
    Write-Host "[WARN]  git appears to be Cygwin/MSYS Git rather than Git for Windows."
    Write-Host "        Put C:\Program Files\Git\cmd at the front of PATH for Taskdeck agent work."
    $exitCode = 1
} elseif ($gitPath -notmatch "\\Git\\cmd\\git\.exe$" -and $gitPath -notmatch "\\Git\\mingw64\\bin\\git\.exe$") {
    Write-Host "[WARN]  git does not resolve to the usual Git for Windows path. Verify it is not Cygwin/MSYS Git."
    $exitCode = 1
} else {
    Write-Host "[OK]    git resolves to Git for Windows."
}

$gitDir = (& git -C $resolvedRepo rev-parse --absolute-git-dir 2>$null)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($gitDir)) {
    Write-Host "[ERROR] could not resolve git directory for $resolvedRepo"
    exit 1
}

$lockFile = Join-Path $gitDir.Trim() "index.lock"
if (Test-Path -LiteralPath $lockFile) {
    Write-Host ""
    Write-Host "[WARN]  Git index lock found: $lockFile"
    $activeGit = Get-Process -Name git -ErrorAction SilentlyContinue
    if ($activeGit) {
        Write-Host "[WARN]  Active git process(es) detected. Do not remove the lock until they finish."
        $activeGit | Select-Object Id, ProcessName, StartTime | Format-Table | Out-String | Write-Host
    } else {
        Write-Host "[WARN]  No active git.exe process found. The lock may be stale."
        Write-Host "        Safe removal command after human/agent review:"
        Write-Host "        Remove-Item -LiteralPath `"$lockFile`""
    }
    $exitCode = 1
} else {
    Write-Host "[OK]    No .git/index.lock present."
}

$badShellFiles = (& git -C $resolvedRepo ls-files --eol -- "*.sh" 2>$null | Select-String "w/crlf")
if ($badShellFiles) {
    Write-Host ""
    Write-Host "[WARN]  Some .sh files are checked out with CRLF and may fail under Bash:"
    foreach ($match in $badShellFiles) {
        Write-Host "        $($match.Line)"
    }
    Write-Host "        Run: git add --renormalize -- '*.sh'"
    $exitCode = 1
} else {
    Write-Host "[OK]    Shell scripts are not checked out as CRLF."
}

Write-Host ""
if ($exitCode -eq 0) {
    Write-Host "All checks passed."
} else {
    Write-Host "One or more issues detected. See warnings above."
}

exit $exitCode
