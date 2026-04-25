[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [int]$IssueNumber,

    [Parameter(Mandatory = $true)]
    [ValidatePattern("^[a-z0-9][a-z0-9-]{1,60}$")]
    [string]$Slug,

    [string]$BaseBranch = "main",
    [string]$WorktreeRoot = ".worktrees",
    [string]$BranchName
)

$ErrorActionPreference = "Stop"

$repoRoot = (& git rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($repoRoot)) {
    throw "Run this script from inside a git repository."
}

$repoRoot = $repoRoot.Trim()
Set-Location -LiteralPath $repoRoot

if ([string]::IsNullOrWhiteSpace($BranchName)) {
    $BranchName = "issue-$IssueNumber/$Slug"
}

$worktreeDir = Join-Path $repoRoot (Join-Path $WorktreeRoot "codex-$IssueNumber-$Slug")
$existingBranch = (& git branch --list $BranchName)
if (-not [string]::IsNullOrWhiteSpace($existingBranch)) {
    throw "Branch already exists: $BranchName"
}

if (Test-Path -LiteralPath $worktreeDir) {
    throw "Worktree path already exists: $worktreeDir"
}

$status = (& git status --short)
if (-not [string]::IsNullOrWhiteSpace($status)) {
    throw "Main checkout is not clean. Commit/stash unrelated work before creating a parallel issue worktree."
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $worktreeDir) | Out-Null

if ($PSCmdlet.ShouldProcess($worktreeDir, "Create worktree on branch $BranchName from $BaseBranch")) {
    & git worktree add -b $BranchName $worktreeDir $BaseBranch
    if ($LASTEXITCODE -ne 0) {
        throw "git worktree add failed."
    }

    Write-Host "Created Codex issue worktree."
    Write-Host "  issue:    #$IssueNumber"
    Write-Host "  branch:   $BranchName"
    Write-Host "  worktree: $worktreeDir"
    Write-Host ""
    Write-Host "First command in the worker session:"
    Write-Host "  powershell -File scripts/worktree_guard.ps1"
}
