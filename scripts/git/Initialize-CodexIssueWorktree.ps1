[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$GitExecutable,

    [Parameter(Mandatory = $true)]
    [string]$BranchName,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedWorktree,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedHead
)

$ErrorActionPreference = "Stop"

function Exit-WithInitializerError {
    param(
        [string]$Message,
        [int]$ExitCode
    )

    Write-Error "ERROR [worktree_initializer]: $Message" -ErrorAction Continue
    exit $ExitCode
}

function Invoke-InitializerGit {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $global:LASTEXITCODE = $null
    try {
        $output = @(& $script:ResolvedGitExecutable @Arguments 2>&1)
        $invocationSucceeded = $?
        $exitCode = $LASTEXITCODE
    }
    catch {
        return [pscustomobject]@{
            InvocationSucceeded = $false
            ExitCode = $null
            Output = $_.Exception.Message
        }
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    return [pscustomobject]@{
        InvocationSucceeded = $invocationSucceeded
        ExitCode = $exitCode
        Output = (($output | ForEach-Object { $_.ToString() }) -join "`n").Trim()
    }
}

$gitCommand = Get-Command $GitExecutable -CommandType Application -All -ErrorAction SilentlyContinue |
    Where-Object { [System.IO.Path]::GetExtension($_.Source) -notin @('.cmd', '.bat') } |
    Select-Object -First 1
if ($null -eq $gitCommand -or
    -not [System.IO.Path]::GetFileNameWithoutExtension($gitCommand.Source).Equals(
        "git",
        [System.StringComparison]::OrdinalIgnoreCase)) {
    Exit-WithInitializerError "no argv-safe native Git executable was found; only git or git.exe is supported." 2
}
$script:ResolvedGitExecutable = $gitCommand.Source

$guardPath = Join-Path (Split-Path -Parent $PSScriptRoot) "worktree_guard.ps1"
if (-not (Test-Path -LiteralPath $guardPath -PathType Leaf)) {
    Exit-WithInitializerError "required guard script was not found: $guardPath" 2
}

& $guardPath -GitExecutable $script:ResolvedGitExecutable
$guardSucceeded = $?
$guardExitCode = $LASTEXITCODE
if (-not $guardSucceeded -or $guardExitCode -ne 0) {
    if ($null -ne $guardExitCode -and $guardExitCode -ne 0) {
        exit $guardExitCode
    }
    exit 1
}

if ([string]::IsNullOrWhiteSpace($env:WT_PROJECT_DIR)) {
    Exit-WithInitializerError "the guard did not provide the current worktree root." 2
}

$pathComparison = if ([System.IO.Path]::DirectorySeparatorChar -eq [char]'\') {
    [System.StringComparison]::OrdinalIgnoreCase
}
else {
    [System.StringComparison]::Ordinal
}
$actualWorktree = [System.IO.Path]::GetFullPath($env:WT_PROJECT_DIR).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
$expectedWorktreePath = [System.IO.Path]::GetFullPath($ExpectedWorktree).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
if (-not $actualWorktree.Equals($expectedWorktreePath, $pathComparison)) {
    Exit-WithInitializerError "current worktree '$actualWorktree' does not match the helper-created worktree '$expectedWorktreePath'." 1
}

$symbolicHead = Invoke-InitializerGit -Arguments @("symbolic-ref", "--quiet", "HEAD")
if (-not $symbolicHead.InvocationSucceeded -and $null -eq $symbolicHead.ExitCode) {
    Exit-WithInitializerError "Git could not inspect whether HEAD is detached." 2
}
if ($symbolicHead.ExitCode -eq 0) {
    Exit-WithInitializerError "the helper-created worktree is already attached to '$($symbolicHead.Output)'." 1
}
if ($symbolicHead.ExitCode -ne 1) {
    Exit-WithInitializerError "Git could not inspect whether HEAD is detached (exit code $($symbolicHead.ExitCode))." 2
}

$headResult = Invoke-InitializerGit -Arguments @("rev-parse", "--verify", "HEAD")
if (-not $headResult.InvocationSucceeded -or $headResult.ExitCode -ne 0 -or
    [string]::IsNullOrWhiteSpace($headResult.Output)) {
    Exit-WithInitializerError "Git could not resolve the detached HEAD." 2
}
if (-not $headResult.Output.Trim().Equals($ExpectedHead, [System.StringComparison]::OrdinalIgnoreCase)) {
    Exit-WithInitializerError "detached HEAD '$($headResult.Output.Trim())' does not match the helper-created base '$ExpectedHead'." 1
}

$branchValidation = Invoke-InitializerGit -Arguments @("check-ref-format", "--branch", $BranchName)
if (-not $branchValidation.InvocationSucceeded -or $branchValidation.ExitCode -ne 0 -or
    [string]::IsNullOrWhiteSpace($branchValidation.Output) -or
    $branchValidation.Output.Trim() -cne $BranchName) {
    Exit-WithInitializerError "invalid branch name: $BranchName" 2
}

$switchResult = Invoke-InitializerGit -Arguments @("switch", "-c", $BranchName)
if (-not $switchResult.InvocationSucceeded -and $null -eq $switchResult.ExitCode) {
    Exit-WithInitializerError "the selected Git executable could not create branch '$BranchName'." 2
}
if ($switchResult.ExitCode -ne 0) {
    $switchContext = if ([string]::IsNullOrWhiteSpace($switchResult.Output)) { "" } else { " $($switchResult.Output)" }
    Exit-WithInitializerError "git switch -c failed for '$BranchName' (exit code $($switchResult.ExitCode)).$switchContext" $switchResult.ExitCode
}
if (-not [string]::IsNullOrWhiteSpace($switchResult.Output)) {
    Write-Host $switchResult.Output
}
