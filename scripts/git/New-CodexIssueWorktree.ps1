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
if (-not $logicalWorktreeRoot.Equals(".worktrees", $pathComparison)) {
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

if ($Slug -cnotmatch "^[a-z0-9][a-z0-9-]{1,60}$") {
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
$remoteSeparatorIndex = $BaseBranch.IndexOf('/')
if ($remoteSeparatorIndex -gt 0 -and $remoteSeparatorIndex -lt ($BaseBranch.Length - 1)) {
    $remoteNameCandidate = $BaseBranch.Substring(0, $remoteSeparatorIndex)
    $remoteBranchCandidate = $BaseBranch.Substring($remoteSeparatorIndex + 1)
    $remoteLookupResult = Invoke-GitCommand -Arguments @("remote", "get-url", "--", $remoteNameCandidate)
    $remoteBranchValidation = Invoke-GitCommand -Arguments @("check-ref-format", "--branch", $remoteBranchCandidate)
    if ($remoteLookupResult.ExitCode -eq 0 -and
        $remoteBranchValidation.ExitCode -eq 0 -and
        $remoteBranchValidation.Stdout.Trim() -ceq $remoteBranchCandidate) {
        $candidateRemote = $remoteNameCandidate
        $candidateBranch = $remoteBranchCandidate
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

    $baseCommitReference = if ($null -ne $candidateRemote) {
        "refs/remotes/$candidateRemote/$candidateBranch"
    }
    else {
        $BaseBranch
    }
    $baseCommitExpression = "${baseCommitReference}^{commit}"
    $baseLookupResult = Invoke-GitCommand -Arguments @("rev-parse", "--verify", "--end-of-options", $baseCommitExpression)
    if ($baseLookupResult.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($baseLookupResult.Stdout)) {
        throw "Base commit not found: $BaseBranch$(Format-GitContext $baseLookupResult.Output)"
    }
    $baseCommit = $baseLookupResult.Stdout.Trim()
    $baseTypeResult = Invoke-GitCommand -Arguments @("cat-file", "-t", $baseCommit)
    if ($baseTypeResult.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($baseTypeResult.Stdout) -or $baseTypeResult.Stdout.Trim() -cne "commit") {
        throw "Base does not resolve to a commit: $BaseBranch$(Format-GitContext $baseTypeResult.Output)"
    }

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
    Write-Host "  powershell -NoLogo -NoProfile -NonInteractive -File scripts/git/Initialize-CodexIssueWorktree.ps1 -GitExecutable '$escapedGitExecutable' -BranchName '$escapedBranchName' -ExpectedWorktree '$escapedWorktreeDir' -ExpectedHead '$baseCommit'"
    Write-Host '  $handoffSucceeded = $?; $handoffExitCode = $LASTEXITCODE'
    Write-Host '  if (-not $handoffSucceeded -or $handoffExitCode -ne 0) { if ($null -ne $handoffExitCode -and $handoffExitCode -ne 0) { exit $handoffExitCode }; exit 1 }'
}
