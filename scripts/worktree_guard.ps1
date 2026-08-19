# Validate that this shell is inside a linked agent worktree (not the main
# checkout) and export canonical path variables for worker prompts.
#
# Validation is by SUBSTANCE, not by path shape: a worktree root outside the
# repository (chosen deliberately on Windows to stay under MAX_PATH) is just as
# valid as .worktrees\ or .claude\worktrees\. The guard checks that
#   1. the toplevel has a .git FILE holding a `gitdir:` pointer,
#   2. that pointer resolves to this repository's real git dir, which lives
#      under <main-repo>\.git\worktrees\<name> (so it is a linked worktree and
#      not the primary checkout, a plain clone, or a bare repository), and
#   3. HEAD resolves and matches the requested state (detached or a branch).
#
# Exit codes (unchanged contract):
#   0 - inside a valid linked worktree
#   1 - FATAL: main checkout / not a linked worktree / HEAD expectation unmet
#   2 - ERROR: setup failure, not inside a git repository, layout unreadable
#
# -AllowedMarkers is retained for invocation compatibility and is ADVISORY
# only: a root outside those markers is reported but not rejected.
[CmdletBinding()]
param(
    [string[]]$AllowedMarkers = @("\.worktrees\", "/.worktrees/", "\.codex\worktrees\", "/.codex/worktrees/", "\.claude\worktrees\", "/.claude/worktrees/"),
    [string]$GitExecutable = "git",
    [ValidateSet("Any", "Detached", "Branch")]
    [string]$ExpectHead = "Any",
    [string]$ExpectedBranch = ""
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

function Invoke-GuardGit {
    param([string[]]$Arguments)

    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $global:LASTEXITCODE = $null
    $output = @()
    $succeeded = $false
    $code = $null
    try {
        $output = @(& $gitCommand.Source @Arguments 2>$null)
        $succeeded = $?
        $code = $LASTEXITCODE
    }
    catch {
        $output = @()
        $succeeded = $false
        $code = $null
    }
    finally {
        $ErrorActionPreference = $previous
    }

    return [pscustomobject]@{
        Succeeded = $succeeded
        ExitCode  = $code
        Output    = $output
    }
}

# Substance check 1: this must be a LINKED worktree, i.e. its git dir lives
# under <common-git-dir>\worktrees\<name> and is not the common dir itself.
if (-not $isLinkedWorktree) {
    Write-Error -ErrorAction Continue @"
FATAL [worktree_guard]: You are in the main checkout or an unrecognized worktree.
  toplevel:   $topLevel
  git dir:    $gitDirectory
  common dir: $gitCommonDirectory

A linked worktree's git dir must live under <main-repo>\.git\worktrees\<name>.
Codex/agent workers must operate from an isolated worktree such as:
  .worktrees\codex-<issue>-<slug>

Do not run git checkout, commit, push, or file edits for a parallel issue from the main checkout.
"@
    exit 1
}

# Substance check 2: the worktree root's .git must be a FILE whose gitdir
# pointer resolves to exactly that linked git dir.
$dotGitPath = Join-Path $topLevel ".git"
if (-not (Test-Path -LiteralPath $dotGitPath -PathType Leaf)) {
    Write-Error -ErrorAction Continue @"
FATAL [worktree_guard]: Worktree root has no linked-worktree .git pointer file.
  toplevel: $topLevel

A linked worktree stores '.git' as a file containing 'gitdir: <path>'.
"@
    exit 1
}

$pointerLine = $null
try {
    $pointerLine = @(Get-Content -LiteralPath $dotGitPath -TotalCount 1 -ErrorAction Stop) | Select-Object -First 1
}
catch {
    $pointerLine = $null
}
if ($null -eq $pointerLine -or $pointerLine -notmatch '^\s*gitdir:\s*(.+?)\s*$') {
    Write-Error -ErrorAction Continue @"
FATAL [worktree_guard]: Worktree root .git file is not a gitdir pointer.
  toplevel:   $topLevel
  first line: $pointerLine
"@
    exit 1
}

$pointerTarget = $Matches[1]
$pointerResolved = $null
try {
    $pointerCandidate = if ([System.IO.Path]::IsPathRooted($pointerTarget)) {
        $pointerTarget
    }
    else {
        Join-Path $topLevel $pointerTarget
    }
    $pointerResolved = [System.IO.Path]::GetFullPath($pointerCandidate).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
}
catch {
    $pointerResolved = $null
}
if ([string]::IsNullOrWhiteSpace($pointerResolved) -or
    -not (Test-Path -LiteralPath $pointerResolved -PathType Container) -or
    -not $pointerResolved.Equals($gitDirectory, $pathComparison)) {
    Write-Error -ErrorAction Continue @"
FATAL [worktree_guard]: Worktree .git pointer does not resolve to this repository's linked git dir.
  toplevel: $topLevel
  pointer:  $(if ([string]::IsNullOrWhiteSpace($pointerResolved)) { $pointerTarget } else { $pointerResolved })
  git dir:  $gitDirectory
"@
    exit 1
}

# Substance check 3: HEAD must resolve, and match the requested state.
$headCommit = Invoke-GuardGit -Arguments @("rev-parse", "--verify", "--quiet", "HEAD")
if (-not $headCommit.Succeeded -or $headCommit.ExitCode -ne 0 -or $headCommit.Output.Count -eq 0) {
    Write-Error -ErrorAction Continue @"
FATAL [worktree_guard]: Worktree HEAD does not resolve to a commit.
  toplevel: $topLevel
"@
    exit 1
}

$symbolicHead = Invoke-GuardGit -Arguments @("symbolic-ref", "--quiet", "--short", "HEAD")
$headBranch = ""
if ($symbolicHead.Succeeded -and $symbolicHead.ExitCode -eq 0 -and $symbolicHead.Output.Count -gt 0) {
    $headBranch = ([string]$symbolicHead.Output[0]).Trim()
}
$headState = if ([string]::IsNullOrEmpty($headBranch)) { "detached" } else { "branch" }

$effectiveExpectHead = $ExpectHead
if (-not [string]::IsNullOrWhiteSpace($ExpectedBranch) -and $effectiveExpectHead -eq "Any") {
    $effectiveExpectHead = "Branch"
}

if ($effectiveExpectHead -eq "Detached" -and $headState -ne "detached") {
    Write-Error -ErrorAction Continue @"
FATAL [worktree_guard]: Worktree HEAD is not detached as required.
  toplevel: $topLevel
  HEAD:     branch $headBranch
"@
    exit 1
}
if ($effectiveExpectHead -eq "Branch") {
    if ($headState -ne "branch") {
        Write-Error -ErrorAction Continue @"
FATAL [worktree_guard]: Worktree HEAD is detached but a branch was required.
  toplevel: $topLevel
  expected: $(if ([string]::IsNullOrWhiteSpace($ExpectedBranch)) { "<any branch>" } else { $ExpectedBranch })
"@
        exit 1
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedBranch) -and $headBranch -ne $ExpectedBranch) {
        Write-Error -ErrorAction Continue @"
FATAL [worktree_guard]: Worktree HEAD is on the wrong branch.
  toplevel: $topLevel
  expected: $ExpectedBranch
  actual:   $headBranch
"@
        exit 1
    }
}

$env:WT_REPO_ROOT = $topLevel
$env:WT_PROJECT_DIR = $topLevel
$env:WT_GIT_DIR = $gitDirectory
$env:WT_HEAD_STATE = $headState
$env:WT_HEAD_BRANCH = $headBranch
$global:WT_REPO_ROOT = $topLevel
$global:WT_PROJECT_DIR = $topLevel
$global:WT_GIT_DIR = $gitDirectory
$global:WT_HEAD_STATE = $headState
$global:WT_HEAD_BRANCH = $headBranch

Set-Location -LiteralPath $topLevel

Write-Host "OK [worktree_guard]: Running in an isolated worktree."
Write-Host "  WT_REPO_ROOT=$env:WT_REPO_ROOT"
Write-Host "  WT_PROJECT_DIR=$env:WT_PROJECT_DIR"
$headStateSuffix = ""
if (-not [string]::IsNullOrEmpty($headBranch)) {
    $headStateSuffix = " ($headBranch)"
}
Write-Host "  WT_HEAD_STATE=$headState$headStateSuffix"

# Conventional roots are advisory only; an out-of-repo root is still valid.
$normalized = $topLevel -replace "/", "\"
$isConventionalRoot = $false
foreach ($marker in $AllowedMarkers) {
    $markerNormalized = $marker -replace "/", "\"
    if ($normalized.Contains($markerNormalized)) {
        $isConventionalRoot = $true
        break
    }
}
if (-not $isConventionalRoot) {
    Write-Host "NOTE [worktree_guard]: root is outside the conventional worktree directories; accepted on linked-worktree substance."
}
