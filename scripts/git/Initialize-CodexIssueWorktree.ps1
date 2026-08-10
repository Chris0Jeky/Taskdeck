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

function Invoke-InitializerGit {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $process = $null
    try {
        $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = $script:ResolvedGitExecutable
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
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()

        return [pscustomobject]@{
            InvocationSucceeded = $true
            ExitCode = $process.ExitCode
            Output = "$stdout$stderr".Trim()
        }
    }
    catch {
        return [pscustomobject]@{
            InvocationSucceeded = $false
            ExitCode = $null
            Output = $_.Exception.Message
        }
    }
    finally {
        if ($null -ne $process) {
            $process.Dispose()
        }
    }
}

function Schedule-FailedInitializerWorktreeRemoval {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Worktree,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedHead
    )

    $hiddenIndexResult = Invoke-InitializerGit -Arguments @("ls-files", "-v", "-z")
    if (-not $hiddenIndexResult.InvocationSucceeded -or $hiddenIndexResult.ExitCode -ne 0) {
        Exit-WithInitializerError "git switch -c failed and cleanup was refused because Git could not inspect index flags; the helper-created worktree was preserved at '$Worktree'." 2
    }
    if (($hiddenIndexResult.Output -split "`0" | Where-Object { $_ -cmatch '^(?:[a-z]|S) ' }).Count -ne 0) {
        Exit-WithInitializerError "git switch -c failed and cleanup was refused because the helper-created worktree contains index-hidden entries that can hide modified data; the worktree was preserved at '$Worktree'." 1
    }
    $statusResult = Invoke-InitializerGit -Arguments @(
        "-c", "core.fsmonitor=false", "status", "--porcelain=v1", "--untracked-files=all", "--ignored=matching", "--"
    )
    if (-not $statusResult.InvocationSucceeded -or $statusResult.ExitCode -ne 0) {
        Exit-WithInitializerError "git switch -c failed and cleanup was refused because Git could not inventory tracked, untracked, and ignored worktree content; the helper-created worktree was preserved at '$Worktree'." 2
    }
    if (-not [string]::IsNullOrWhiteSpace($statusResult.Output)) {
        Exit-WithInitializerError "git switch -c failed and cleanup was refused because the helper-created worktree contains tracked, untracked, or ignored content; the worktree was preserved at '$Worktree'. Inspect it before any plain git worktree remove." 1
    }

    $commonGitDirectory = Invoke-InitializerGit -Arguments @("rev-parse", "--path-format=absolute", "--git-common-dir")
    if (-not $commonGitDirectory.InvocationSucceeded -or $commonGitDirectory.ExitCode -ne 0 -or
        [string]::IsNullOrWhiteSpace($commonGitDirectory.Output)) {
        Exit-WithInitializerError "git switch -c failed and the unused helper-created worktree could not be scheduled for removal because Git could not resolve the common directory." 2
    }
    $resolvedCommonGitDirectory = [System.IO.Path]::GetFullPath($commonGitDirectory.Output.Trim())
    if (-not (Test-Path -LiteralPath $resolvedCommonGitDirectory -PathType Container)) {
        Exit-WithInitializerError "git switch -c failed and the unused helper-created worktree could not be scheduled for removal because the common Git directory is not an inspectable directory." 2
    }

    $escapedGit = $script:ResolvedGitExecutable.Replace("'", "''")
    $escapedCommonGitDirectory = $resolvedCommonGitDirectory.Replace("'", "''")
    $escapedWorktree = $Worktree.Replace("'", "''")
    $escapedExpectedHead = $ExpectedHead.Replace("'", "''")
    $cleanupScript = @"
`$parentProcessId = $PID
while (`$null -ne (Get-Process -Id `$parentProcessId -ErrorAction SilentlyContinue)) {
    Start-Sleep -Milliseconds 100
}
`$cleanupTopLevel = @(& '$escapedGit' -C '$escapedWorktree' rev-parse --path-format=absolute --show-toplevel 2>`$null)
if (`$LASTEXITCODE -ne 0 -or `$cleanupTopLevel.Count -ne 1 -or
    -not [System.IO.Path]::GetFullPath(`$cleanupTopLevel[0]).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar).Equals('$escapedWorktree', [System.StringComparison]::OrdinalIgnoreCase)) {
    exit 3
}
`$cleanupCommonDirectory = @(& '$escapedGit' -C '$escapedWorktree' rev-parse --path-format=absolute --git-common-dir 2>`$null)
if (`$LASTEXITCODE -ne 0 -or `$cleanupCommonDirectory.Count -ne 1 -or
    -not [System.IO.Path]::GetFullPath(`$cleanupCommonDirectory[0]).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar).Equals('$escapedCommonGitDirectory', [System.StringComparison]::OrdinalIgnoreCase)) {
    exit 3
}
`$cleanupHead = @(& '$escapedGit' -C '$escapedWorktree' rev-parse --verify HEAD 2>`$null)
if (`$LASTEXITCODE -ne 0 -or `$cleanupHead.Count -ne 1 -or
    -not `$cleanupHead[0].Trim().Equals('$escapedExpectedHead', [System.StringComparison]::OrdinalIgnoreCase)) {
    exit 3
}
& '$escapedGit' -C '$escapedWorktree' symbolic-ref --quiet HEAD >`$null 2>`$null
if (`$LASTEXITCODE -ne 1) {
    exit 3
}
`$cleanupStatus = @(& '$escapedGit' -c core.fsmonitor=false -C '$escapedWorktree' status --porcelain=v1 --untracked-files=all --ignored=matching -- 2>`$null)
if (`$LASTEXITCODE -ne 0 -or `$cleanupStatus.Count -ne 0) {
    exit 4
}
`$cleanupHidden = @(& '$escapedGit' -C '$escapedWorktree' ls-files -v -z 2>`$null)
if (`$LASTEXITCODE -ne 0 -or (`$cleanupHidden -join "`0" -split "`0" | Where-Object { `$_ -cmatch '^(?:[a-z]|S) ' }).Count -ne 0) {
    exit 4
}
& '$escapedGit' '--git-dir=$escapedCommonGitDirectory' worktree remove '$escapedWorktree'
exit `$LASTEXITCODE
"@
    $encodedCleanupScript = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($cleanupScript))
    try {
        Start-Process -FilePath (Get-Process -Id $PID).Path -WorkingDirectory $resolvedCommonGitDirectory -WindowStyle Hidden -ArgumentList @(
            "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", $encodedCleanupScript
        ) | Out-Null
    }
    catch {
        Exit-WithInitializerError "git switch -c failed and the unused helper-created worktree could not be scheduled for removal. $($_.Exception.Message)" 2
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
    Schedule-FailedInitializerWorktreeRemoval -Worktree $expectedWorktreePath -ExpectedHead $ExpectedHead
    Exit-WithInitializerError "the selected Git executable could not create branch '$BranchName'; removal of the unused helper-created worktree was scheduled." 2
}
if ($switchResult.ExitCode -ne 0) {
    $switchContext = if ([string]::IsNullOrWhiteSpace($switchResult.Output)) { "" } else { " $($switchResult.Output)" }
    Schedule-FailedInitializerWorktreeRemoval -Worktree $expectedWorktreePath -ExpectedHead $ExpectedHead
    Exit-WithInitializerError "git switch -c failed for '$BranchName' (exit code $($switchResult.ExitCode)); removal of the unused helper-created worktree was scheduled.$switchContext" $switchResult.ExitCode
}
if (-not [string]::IsNullOrWhiteSpace($switchResult.Output)) {
    Write-Host $switchResult.Output
}
