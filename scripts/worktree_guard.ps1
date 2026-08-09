[CmdletBinding()]
param(
    [string[]]$AllowedMarkers = @("\.worktrees\", "/.worktrees/", "\.codex\worktrees\", "/.codex/worktrees/", "\.claude\worktrees\", "/.claude/worktrees/"),
    [string]$GitExecutable = "git"
)

$ErrorActionPreference = "Stop"

$gitCommand = Get-Command $GitExecutable -CommandType Application -All -ErrorAction SilentlyContinue |
    Where-Object { [System.IO.Path]::GetExtension($_.Source) -notin @('.cmd', '.bat') } |
    Select-Object -First 1
if ($null -eq $gitCommand) {
    Write-Error "ERROR [worktree_guard]: no argv-safe Git executable was found; .cmd and .bat shims are not supported." -ErrorAction Continue
    exit 2
}

$topLevel = $null
$gitInvocationSucceeded = $false
$gitExitCode = $null
$previousErrorActionPreference = $ErrorActionPreference
$ErrorActionPreference = "Continue"
$global:LASTEXITCODE = $null
try {
    $topLevel = (& $gitCommand.Source rev-parse --show-toplevel 2>$null)
    $gitInvocationSucceeded = $?
    $gitExitCode = $LASTEXITCODE
}
catch {
    Write-Error "ERROR [worktree_guard]: the selected Git executable could not be run." -ErrorAction Continue
    exit 2
}
finally {
    $ErrorActionPreference = $previousErrorActionPreference
}
if (-not $gitInvocationSucceeded -and $null -eq $gitExitCode) {
    Write-Error "ERROR [worktree_guard]: the selected Git executable could not be run." -ErrorAction Continue
    exit 2
}
if ($gitExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($topLevel)) {
    Write-Error "ERROR [worktree_guard]: not inside a git repository." -ErrorAction Continue
    exit 2
}

$topLevel = $topLevel.Trim()
$layoutOutput = $null
$layoutInvocationSucceeded = $false
$layoutExitCode = $null
$previousErrorActionPreference = $ErrorActionPreference
$ErrorActionPreference = "Continue"
$global:LASTEXITCODE = $null
try {
    $layoutOutput = @(& $gitCommand.Source rev-parse --git-dir --git-common-dir 2>$null)
    $layoutInvocationSucceeded = $?
    $layoutExitCode = $LASTEXITCODE
}
catch {
    Write-Error "ERROR [worktree_guard]: the selected Git executable could not inspect repository layout." -ErrorAction Continue
    exit 2
}
finally {
    $ErrorActionPreference = $previousErrorActionPreference
}
if (-not $layoutInvocationSucceeded -or $layoutExitCode -ne 0 -or $layoutOutput.Count -ne 2) {
    Write-Error "ERROR [worktree_guard]: repository layout could not be verified." -ErrorAction Continue
    exit 2
}

$invocationDirectory = (Get-Location).Path
function Resolve-GuardGitPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path).TrimEnd(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $invocationDirectory $Path)).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
}

$gitDirectory = Resolve-GuardGitPath $layoutOutput[0].Trim()
$gitCommonDirectory = Resolve-GuardGitPath $layoutOutput[1].Trim()
$pathComparison = if ([System.IO.Path]::DirectorySeparatorChar -eq [char]'\') {
    [System.StringComparison]::OrdinalIgnoreCase
}
else {
    [System.StringComparison]::Ordinal
}
$linkedWorktreeDirectory = (Join-Path $gitCommonDirectory "worktrees").TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
$isLinkedWorktree = -not $gitDirectory.Equals($gitCommonDirectory, $pathComparison) -and
    $gitDirectory.StartsWith($linkedWorktreeDirectory, $pathComparison)

$normalized = $topLevel -replace "/", "\"
$isAllowed = $false

foreach ($marker in $AllowedMarkers) {
    $markerNormalized = $marker -replace "/", "\"
    if ($normalized.Contains($markerNormalized)) {
        $isAllowed = $true
        break
    }
}

if (-not $isAllowed -or -not $isLinkedWorktree) {
    Write-Error -ErrorAction Continue @"
FATAL [worktree_guard]: You are in the main checkout or an unrecognized worktree.
  toplevel: $topLevel

Codex/agent workers must operate from an isolated worktree such as:
  .worktrees\codex-<issue>-<slug>

Do not run git checkout, commit, push, or file edits for a parallel issue from the main checkout.
"@
    exit 1
}

$env:WT_REPO_ROOT = $topLevel
$env:WT_PROJECT_DIR = $topLevel
$global:WT_REPO_ROOT = $topLevel
$global:WT_PROJECT_DIR = $topLevel

Set-Location -LiteralPath $topLevel

Write-Host "OK [worktree_guard]: Running in an isolated worktree."
Write-Host "  WT_REPO_ROOT=$env:WT_REPO_ROOT"
Write-Host "  WT_PROJECT_DIR=$env:WT_PROJECT_DIR"
