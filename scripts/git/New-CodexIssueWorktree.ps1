[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [int]$IssueNumber,

    [Parameter(Mandatory = $true)]
    [string]$Slug,

    [string]$BaseBranch = "origin/main",
    [string]$WorktreeRoot = ".worktrees",
    [string]$BranchName
)

$ErrorActionPreference = "Stop"

$gitCommand = Get-Command git -CommandType Application -All -ErrorAction SilentlyContinue |
    Where-Object { [System.IO.Path]::GetExtension($_.Source) -notin @('.cmd', '.bat') } |
    Select-Object -First 1
if ($null -eq $gitCommand) {
    throw "No argv-safe Git executable was found on PATH; .cmd and .bat shims are not supported."
}

$gitExecutable = $gitCommand.Source

function ConvertTo-NativeArgument {
    param(
        [AllowEmptyString()]
        [string]$Argument
    )

    if ([string]::IsNullOrEmpty($Argument)) {
        return '""'
    }
    if ($Argument -notmatch '[\s"]') {
        return $Argument
    }

    $builder = [System.Text.StringBuilder]::new()
    [void]$builder.Append('"')
    $backslashCount = 0
    foreach ($character in $Argument.ToCharArray()) {
        if ($character -eq [char]'\') {
            $backslashCount++
            continue
        }

        $escapedBackslashCount = if ($character -eq [char]'"') {
            ($backslashCount * 2) + 1
        }
        else {
            $backslashCount
        }
        for ($index = 0; $index -lt $escapedBackslashCount; $index++) {
            [void]$builder.Append([char]'\')
        }
        [void]$builder.Append($character)
        $backslashCount = 0
    }

    for ($index = 0; $index -lt ($backslashCount * 2); $index++) {
        [void]$builder.Append([char]'\')
    }
    [void]$builder.Append('"')
    return $builder.ToString()
}

function Invoke-GitCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $process = $null
    try {
        $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = $gitExecutable
        $startInfo.WorkingDirectory = (Get-Location).Path
        $startInfo.UseShellExecute = $false
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true

        if ($null -ne $startInfo.PSObject.Properties['ArgumentList']) {
            foreach ($argument in $Arguments) {
                $startInfo.ArgumentList.Add($argument)
            }
        }
        else {
            $startInfo.Arguments = (($Arguments | ForEach-Object { ConvertTo-NativeArgument $_ }) -join ' ')
        }

        $process = [System.Diagnostics.Process]::new()
        $process.StartInfo = $startInfo
        if (-not $process.Start()) {
            throw "Git process did not start."
        }

        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $stdout = $stdoutTask.GetAwaiter().GetResult().Trim()
        $stderr = $stderrTask.GetAwaiter().GetResult().Trim()
        $output = (@($stdout, $stderr) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join "`n"

        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            Stdout = $stdout
            Stderr = $stderr
            Output = $output
        }
    }
    catch {
        return [pscustomobject]@{
            ExitCode = -1
            Stdout = ""
            Stderr = $_.Exception.Message
            Output = $_.Exception.Message
        }
    }
    finally {
        if ($null -ne $process) {
            $process.Dispose()
        }
    }
}

function Format-GitContext {
    param([string]$Output)

    if ([string]::IsNullOrWhiteSpace($Output)) {
        return ""
    }

    return "`nGit: $Output"
}

function Resolve-BaseCommit {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Repository,

        [Parameter(Mandatory = $true)]
        [string]$Reference,

        [Parameter(Mandatory = $true)]
        [string]$DisplayName
    )

    $baseCommitExpression = "${Reference}^{commit}"
    $baseLookupResult = Invoke-GitCommand -Arguments @("-C", $Repository, "rev-parse", "--verify", "--end-of-options", $baseCommitExpression)
    if ($baseLookupResult.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($baseLookupResult.Stdout)) {
        throw "Base commit not found: $DisplayName$(Format-GitContext $baseLookupResult.Output)"
    }

    $resolvedCommit = $baseLookupResult.Stdout.Trim()
    $baseTypeResult = Invoke-GitCommand -Arguments @("-C", $Repository, "cat-file", "-t", $resolvedCommit)
    if ($baseTypeResult.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($baseTypeResult.Stdout) -or $baseTypeResult.Stdout.Trim() -cne "commit") {
        throw "Base does not resolve to a commit: $DisplayName$(Format-GitContext $baseTypeResult.Output)"
    }

    return $resolvedCommit
}

function Get-ReviewedHandoffArtifacts {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Repository
    )

    $requiredArtifacts = @(
        "scripts/worktree_guard.ps1",
        "scripts/git/Initialize-CodexIssueWorktree.ps1"
    )
    $sourceHead = Resolve-BaseCommit -Repository $Repository -Reference "HEAD" -DisplayName "invoking checkout HEAD"
    $reviewedArtifacts = [ordered]@{}
    foreach ($artifact in $requiredArtifacts) {
        $objectExpression = "${sourceHead}:$artifact"
        $artifactLookupResult = Invoke-GitCommand -Arguments @("-C", $Repository, "rev-parse", "--verify", "--end-of-options", $objectExpression)
        if ($artifactLookupResult.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($artifactLookupResult.Stdout)) {
            throw "Invoking checkout HEAD does not contain required handoff artifact '$artifact'.$(Format-GitContext $artifactLookupResult.Output)"
        }

        $artifactBlob = $artifactLookupResult.Stdout.Trim()
        $artifactTypeResult = Invoke-GitCommand -Arguments @("-C", $Repository, "cat-file", "-t", $artifactBlob)
        if ($artifactTypeResult.ExitCode -ne 0 -or
            [string]::IsNullOrWhiteSpace($artifactTypeResult.Stdout) -or
            $artifactTypeResult.Stdout.Trim() -cne "blob") {
            throw "Invoking checkout HEAD handoff artifact '$artifact' is not a blob.$(Format-GitContext $artifactTypeResult.Output)"
        }

        $artifactPath = Join-Path $Repository $artifact
        if (-not (Test-Path -LiteralPath $artifactPath -PathType Leaf)) {
            throw "Reviewed handoff artifact '$artifact' is missing from the invoking checkout."
        }

        $worktreeDiff = Invoke-GitCommand -Arguments @("-C", $Repository, "diff", "--no-ext-diff", "--quiet", "--", $artifact)
        if ($worktreeDiff.ExitCode -eq 1) {
            throw "Reviewed handoff artifact '$artifact' has unstaged changes in the invoking checkout."
        }
        if ($worktreeDiff.ExitCode -ne 0) {
            throw "Git could not inspect unstaged changes for reviewed handoff artifact '$artifact'.$(Format-GitContext $worktreeDiff.Output)"
        }

        $indexDiff = Invoke-GitCommand -Arguments @("-C", $Repository, "diff", "--cached", "--no-ext-diff", "--quiet", $sourceHead, "--", $artifact)
        if ($indexDiff.ExitCode -eq 1) {
            throw "Reviewed handoff artifact '$artifact' has staged changes in the invoking checkout."
        }
        if ($indexDiff.ExitCode -ne 0) {
            throw "Git could not inspect staged changes for reviewed handoff artifact '$artifact'.$(Format-GitContext $indexDiff.Output)"
        }

        $reviewedArtifacts[$artifact] = $artifactBlob
    }

    return $reviewedArtifacts
}

function Assert-BaseMatchesReviewedHandoffArtifacts {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Repository,

        [Parameter(Mandatory = $true)]
        [string]$Commit,

        [Parameter(Mandatory = $true)]
        [string]$DisplayName,

        [Parameter(Mandatory = $true)]
        [System.Collections.IDictionary]$ReviewedArtifacts
    )

    foreach ($artifact in $ReviewedArtifacts.Keys) {
        $objectExpression = "${Commit}:$artifact"
        $artifactLookupResult = Invoke-GitCommand -Arguments @("-C", $Repository, "rev-parse", "--verify", "--end-of-options", $objectExpression)
        if ($artifactLookupResult.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($artifactLookupResult.Stdout)) {
            throw "Base commit '$DisplayName' does not contain required handoff artifact '$artifact'.$(Format-GitContext $artifactLookupResult.Output)"
        }

        $artifactBlob = $artifactLookupResult.Stdout.Trim()
        $artifactTypeResult = Invoke-GitCommand -Arguments @("-C", $Repository, "cat-file", "-t", $artifactBlob)
        if ($artifactTypeResult.ExitCode -ne 0 -or
            [string]::IsNullOrWhiteSpace($artifactTypeResult.Stdout) -or
            $artifactTypeResult.Stdout.Trim() -cne "blob") {
            throw "Base commit '$DisplayName' does not contain required handoff artifact '$artifact'.$(Format-GitContext $artifactTypeResult.Output)"
        }
        if ($artifactBlob -cne $ReviewedArtifacts[$artifact]) {
            throw "Base commit '$DisplayName' handoff artifact '$artifact' does not match the reviewed artifact in the invoking checkout HEAD."
        }
    }
}

function Assert-RemoteBaseExistsWithoutFetch {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Remote,

        [Parameter(Mandatory = $true)]
        [string]$Branch,

        [Parameter(Mandatory = $true)]
        [string]$DisplayName
    )

    $remoteReference = "refs/heads/$Branch"
    $remoteLookupResult = Invoke-GitCommand -Arguments @("ls-remote", "--exit-code", "--heads", "--", $Remote, $remoteReference)
    $matchingRemoteRefs = @(
        $remoteLookupResult.Stdout -split '\r?\n' |
            Where-Object {
                $fields = $_ -split "`t", 2
                $fields.Count -eq 2 -and $fields[1] -ceq $remoteReference -and
                    $fields[0] -match '^[0-9a-fA-F]+$'
            }
    )
    if ($remoteLookupResult.ExitCode -ne 0 -or $matchingRemoteRefs.Count -ne 1) {
        throw "Base commit not found: $DisplayName. The explicit remote base could not be verified without updating refs.$(Format-GitContext $remoteLookupResult.Output)"
    }
}

function Assert-SafeWorktreeRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    try {
        $rootItem = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    }
    catch [System.Management.Automation.ItemNotFoundException] {
        return
    }
    catch {
        throw "Unsafe worktree root: '$Path' could not be inspected. $($_.Exception.Message)"
    }
    if (-not $rootItem.PSIsContainer) {
        throw "Unsafe worktree root: '$Path' exists but is not a directory."
    }
    if (($rootItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Unsafe worktree root: '$Path' is a reparse point or symbolic link."
    }
}

$repoRootResult = Invoke-GitCommand -Arguments @("rev-parse", "--show-toplevel")
if ($repoRootResult.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($repoRootResult.Stdout)) {
    throw "Run this script from inside a git repository.$(Format-GitContext $repoRootResult.Output)"
}

$repoRoot = [System.IO.Path]::GetFullPath($repoRootResult.Stdout.Trim()).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
Set-Location -LiteralPath $repoRoot
$reviewedHandoffArtifacts = Get-ReviewedHandoffArtifacts -Repository $repoRoot

if ([System.IO.Path]::IsPathRooted($WorktreeRoot)) {
    throw "Invalid worktree root: '$WorktreeRoot'. Codex issue worktrees must use the repository's .worktrees directory."
}

$pathComparison = if ([System.IO.Path]::DirectorySeparatorChar -eq [char]'\') {
    [System.StringComparison]::OrdinalIgnoreCase
}
else {
    [System.StringComparison]::Ordinal
}
$logicalWorktreeRoot = $WorktreeRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
if (-not $logicalWorktreeRoot.Equals(".worktrees", [System.StringComparison]::Ordinal)) {
    throw "Invalid worktree root: '$WorktreeRoot'. Codex issue worktrees must use the repository's .worktrees directory."
}

$expectedWorktreeRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot ".worktrees")).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
$requestedWorktreeRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $WorktreeRoot)).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
if (-not $requestedWorktreeRoot.Equals($expectedWorktreeRoot, $pathComparison)) {
    throw "Invalid worktree root: '$WorktreeRoot'. Codex issue worktrees must use the repository's .worktrees directory."
}
Assert-SafeWorktreeRoot -Path $requestedWorktreeRoot

if ($Slug -cnotmatch "^[a-z0-9][a-z0-9-]{1,60}\z") {
    throw "Invalid slug: '$Slug'. Use 2-61 lowercase letters, digits, or hyphens, starting with a letter or digit."
}

if ([string]::IsNullOrWhiteSpace($BranchName)) {
    $BranchName = "issue-$IssueNumber/$Slug"
}

$branchValidationResult = Invoke-GitCommand -Arguments @("check-ref-format", "--branch", $BranchName)
if ($branchValidationResult.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($branchValidationResult.Stdout) -or $branchValidationResult.Stdout.Trim() -cne $BranchName) {
    throw "Invalid branch name: $BranchName$(Format-GitContext $branchValidationResult.Output)"
}

$worktreeDir = Join-Path $requestedWorktreeRoot "codex-$IssueNumber-$Slug"
$branchLookupResult = Invoke-GitCommand -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/$BranchName")
if ($branchLookupResult.ExitCode -eq 0) {
    throw "Branch already exists: $BranchName"
}
if ($branchLookupResult.ExitCode -ne 1) {
    throw "Git could not check branch '$BranchName' (exit code $($branchLookupResult.ExitCode)).$(Format-GitContext $branchLookupResult.Output)"
}

if (Test-Path -LiteralPath $worktreeDir) {
    throw "Worktree path already exists: $worktreeDir"
}

$candidateRemote = $null
$candidateBranch = $null
$remoteListResult = Invoke-GitCommand -Arguments @("remote")
if ($remoteListResult.ExitCode -ne 0) {
    throw "Git could not enumerate configured remotes.$(Format-GitContext $remoteListResult.Output)"
}
$configuredRemotes = @(
    $remoteListResult.Stdout -split '\r?\n' |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Sort-Object { $_.Length } -Descending
)
if (-not $BaseBranch.StartsWith("refs/", [System.StringComparison]::Ordinal)) {
    foreach ($remoteNameCandidate in $configuredRemotes) {
        $remotePrefix = "$remoteNameCandidate/"
        if (-not $BaseBranch.StartsWith($remotePrefix, [System.StringComparison]::Ordinal) -or
            $BaseBranch.Length -le $remotePrefix.Length) {
            continue
        }

        $remoteBranchCandidate = $BaseBranch.Substring($remotePrefix.Length)
        $remoteBranchValidation = Invoke-GitCommand -Arguments @("check-ref-format", "--branch", $remoteBranchCandidate)
        if ($remoteBranchValidation.ExitCode -eq 0 -and
            $remoteBranchValidation.Stdout.Trim() -ceq $remoteBranchCandidate) {
            $candidateRemote = $remoteNameCandidate
            $candidateBranch = $remoteBranchCandidate
            break
        }
    }
}

$baseCommitReference = if ($null -ne $candidateRemote) {
    "refs/remotes/$candidateRemote/$candidateBranch"
}
else {
    $BaseBranch
}

if ($WhatIfPreference) {
    if ($null -ne $candidateRemote) {
        Assert-RemoteBaseExistsWithoutFetch -Remote $candidateRemote -Branch $candidateBranch -DisplayName $BaseBranch
    }
    else {
        $whatIfBaseCommit = Resolve-BaseCommit -Repository $repoRoot -Reference $baseCommitReference -DisplayName $BaseBranch
        Assert-BaseMatchesReviewedHandoffArtifacts -Repository $repoRoot -Commit $whatIfBaseCommit -DisplayName $BaseBranch -ReviewedArtifacts $reviewedHandoffArtifacts
    }
}

if ($PSCmdlet.ShouldProcess($worktreeDir, "Refresh the base when remote and create a detached worktree from $BaseBranch")) {
    if ($null -ne $candidateRemote) {
        $remoteRefspec = "+refs/heads/$candidateBranch`:refs/remotes/$candidateRemote/$candidateBranch"
        $fetchResult = Invoke-GitCommand -Arguments @("fetch", "--no-tags", "--no-recurse-submodules", "--", $candidateRemote, $remoteRefspec)
        if ($fetchResult.ExitCode -ne 0) {
            throw "Base commit not found: $BaseBranch. Failed to refresh the explicit remote base.$(Format-GitContext $fetchResult.Output)"
        }
    }

    $baseCommit = Resolve-BaseCommit -Repository $repoRoot -Reference $baseCommitReference -DisplayName $BaseBranch
    Assert-BaseMatchesReviewedHandoffArtifacts -Repository $repoRoot -Commit $baseCommit -DisplayName $BaseBranch -ReviewedArtifacts $reviewedHandoffArtifacts

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $worktreeDir) | Out-Null
    Assert-SafeWorktreeRoot -Path $requestedWorktreeRoot

    $worktreeAddResult = Invoke-GitCommand -Arguments @("worktree", "add", "--detach", $worktreeDir, $baseCommit)
    if ($worktreeAddResult.ExitCode -ne 0) {
        throw "git worktree add failed for '$worktreeDir' from '$BaseBranch' (exit code $($worktreeAddResult.ExitCode)).$(Format-GitContext $worktreeAddResult.Output)"
    }
    if (-not [string]::IsNullOrWhiteSpace($worktreeAddResult.Output)) {
        Write-Host $worktreeAddResult.Output
    }

    $escapedBranchName = $BranchName.Replace("'", "''")
    $escapedGitExecutable = $gitExecutable.Replace("'", "''")
    $escapedWorktreeDir = $worktreeDir.Replace("'", "''")

    Write-Host "Created detached Codex issue worktree."
    Write-Host "  issue:          #$IssueNumber"
    Write-Host "  base:           $BaseBranch ($baseCommit)"
    Write-Host "  planned branch: $BranchName (not created yet)"
    Write-Host "  worktree:       $worktreeDir"
    Write-Host ""
    Write-Host "PowerShell worker handoff (run this entire block unchanged):"
    Write-Host "  & 'scripts/git/Initialize-CodexIssueWorktree.ps1' -GitExecutable '$escapedGitExecutable' -BranchName '$escapedBranchName' -ExpectedWorktree '$escapedWorktreeDir' -ExpectedHead '$baseCommit'"
    Write-Host '  $handoffSucceeded = $?; $handoffExitCode = $LASTEXITCODE'
    Write-Host '  if (-not $handoffSucceeded -or $handoffExitCode -ne 0) { if ($null -ne $handoffExitCode -and $handoffExitCode -ne 0) { exit $handoffExitCode }; exit 1 }'
}
