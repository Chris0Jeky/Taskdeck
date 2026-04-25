[CmdletBinding()]
param(
    [string[]]$AllowedMarkers = @("\.worktrees\", "/.worktrees/", "\.codex\worktrees\", "/.codex/worktrees/", "\.claude\worktrees\", "/.claude/worktrees/")
)

$ErrorActionPreference = "Stop"

$topLevel = (& git rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($topLevel)) {
    Write-Error "ERROR [worktree_guard]: not inside a git repository."
    exit 2
}

$topLevel = $topLevel.Trim()
$normalized = $topLevel -replace "/", "\"
$isAllowed = $false

foreach ($marker in $AllowedMarkers) {
    $markerNormalized = $marker -replace "/", "\"
    if ($normalized.Contains($markerNormalized)) {
        $isAllowed = $true
        break
    }
}

if (-not $isAllowed) {
    Write-Error @"
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
