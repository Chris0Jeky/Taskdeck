[CmdletBinding()]
param(
    [ValidateSet(
        "success-detached",
        "handoff-order",
        "guard-then-branch",
        "handoff-fail-fast",
        "handoff-missing-executable",
        "existing-branch",
        "existing-path",
        "worktree-root-traversal",
        "worktree-root-rooted",
        "worktree-root-unapproved",
        "worktree-root-reparse",
        "invalid-slug",
        "invalid-branch",
        "batch-shim-bypass",
        "metachar-base",
        "revision-range-base",
        "annotated-tag-base",
        "refresh-remote-base",
        "missing-base",
        "what-if",
        "git-add-failure"
    )]
    [string[]]$Case = @(
        "success-detached",
        "handoff-order",
        "guard-then-branch",
        "handoff-fail-fast",
        "handoff-missing-executable",
        "existing-branch",
        "existing-path",
        "worktree-root-traversal",
        "worktree-root-rooted",
        "worktree-root-unapproved",
        "worktree-root-reparse",
        "invalid-slug",
        "invalid-branch",
        "batch-shim-bypass",
        "metachar-base",
        "revision-range-base",
        "annotated-tag-base",
        "refresh-remote-base",
        "missing-base",
        "what-if",
        "git-add-failure"
    )
)

$ErrorActionPreference = "Stop"

$helperPath = Join-Path $PSScriptRoot "New-CodexIssueWorktree.ps1"
$gitCommand = Get-Command git -CommandType Application -All -ErrorAction SilentlyContinue |
    Where-Object { [System.IO.Path]::GetExtension($_.Source) -notin @('.cmd', '.bat') } |
    Select-Object -First 1
if ($null -eq $gitCommand) {
    throw "No argv-safe Git executable was found on PATH; .cmd and .bat shims are not supported."
}
$gitExecutable = $gitCommand.Source
$powerShellExecutable = (Get-Process -Id $PID).Path
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) "taskdeck-worktree-helper-$([Guid]::NewGuid().ToString('N'))"
$passed = 0
$testFailure = $null
$cleanupFailure = $null
$reparseRootToRemove = $null
$selectedCases = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($caseName in $Case) {
    $null = $selectedCases.Add($caseName)
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

function Invoke-ProcessCapture {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory
    )

    $process = $null
    try {
        $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = $FilePath
        $startInfo.WorkingDirectory = $WorkingDirectory
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
            throw "Process did not start: $FilePath"
        }

        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()

        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            Output = "$stdout$stderr"
        }
    }
    catch {
        return [pscustomobject]@{
            ExitCode = -1
            Output = $_.Exception.Message
        }
    }
    finally {
        if ($null -ne $process) {
            $process.Dispose()
        }
    }
}

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $result = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments $Arguments -WorkingDirectory $WorkingDirectory
    if ($result.ExitCode -ne 0) {
        throw "Fixture git command failed (exit $($result.ExitCode)): git $($Arguments -join ' ')`n$($result.Output)"
    }

    return $result.Output.TrimEnd()
}

function Invoke-Helper {
    param(
        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $hostArguments = @("-NoLogo", "-NoProfile", "-NonInteractive", "-File", $helperPath) + $Arguments
    return Invoke-ProcessCapture -FilePath $powerShellExecutable -Arguments $hostArguments -WorkingDirectory $WorkingDirectory
}

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-Equal {
    param(
        $Expected,
        $Actual,
        [string]$Message
    )

    if ($Expected -cne $Actual) {
        throw "$Message`nExpected: <$Expected>`nActual:   <$Actual>"
    }
}

function Assert-Contains {
    param(
        [string]$Text,
        [string]$ExpectedSubstring,
        [string]$Message
    )

    if (-not $Text.Contains($ExpectedSubstring)) {
        throw "$Message`nExpected substring: <$ExpectedSubstring>`nActual output:`n$Text"
    }
}

function Complete-Test {
    param([string]$Name)

    $script:passed++
    Write-Host "PASS [$script:passed]: $Name"
}

function Test-CaseSelected {
    param([string]$Name)

    return $selectedCases.Contains($Name)
}

function Get-PrintedHandoffLines {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Output
    )

    $outputLines = @($Output -split '\r?\n')
    $marker = "PowerShell-only worker handoff (run this entire block in order):"
    $markerIndex = [Array]::IndexOf($outputLines, $marker)
    Assert-True ($markerIndex -ge 0) "Helper output omitted the PowerShell-only handoff marker."
    Assert-True (($markerIndex + 6) -lt $outputLines.Count) "Helper output omitted one or more fail-fast handoff commands."

    return @(
        $outputLines[$markerIndex + 1].TrimStart(),
        $outputLines[$markerIndex + 2].TrimStart(),
        $outputLines[$markerIndex + 3].TrimStart(),
        $outputLines[$markerIndex + 4].TrimStart(),
        $outputLines[$markerIndex + 5].TrimStart(),
        $outputLines[$markerIndex + 6].TrimStart()
    )
}

try {
    New-Item -ItemType Directory -Path $testRoot | Out-Null

    $fixtureRoot = Join-Path $testRoot "fixture with spaces"
    New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
    $remotePath = Join-Path $fixtureRoot "remote.git"
    $seedPath = Join-Path $fixtureRoot "seed"
    $callerPath = Join-Path $fixtureRoot "caller"

    $null = Invoke-Git -WorkingDirectory $fixtureRoot -Arguments @("init", "--bare", $remotePath)
    $null = Invoke-Git -WorkingDirectory $fixtureRoot -Arguments @("init", "-b", "main", $seedPath)
    $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("config", "user.name", "Taskdeck Test")
    $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("config", "user.email", "taskdeck-test@example.invalid")
    Set-Content -LiteralPath (Join-Path $seedPath ".gitignore") -Value ".worktrees/" -Encoding Ascii
    Set-Content -LiteralPath (Join-Path $seedPath "tracked.txt") -Value "committed" -Encoding Ascii
    $seedScriptsPath = Join-Path $seedPath "scripts"
    New-Item -ItemType Directory -Path $seedScriptsPath | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "../worktree_guard.ps1") -Destination (Join-Path $seedScriptsPath "worktree_guard.ps1")
    $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("add", ".gitignore", "tracked.txt", "scripts/worktree_guard.ps1")
    $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("commit", "-m", "Seed fixture")
    Set-Content -LiteralPath (Join-Path $seedPath "tracked.txt") -Value "advanced" -Encoding Ascii
    $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("add", "tracked.txt")
    $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("commit", "-m", "Advance fixture")
    $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("remote", "add", "origin", $remotePath)
    $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("push", "-u", "origin", "main")
    $null = Invoke-Git -WorkingDirectory $fixtureRoot -Arguments @("clone", "-b", "main", $remotePath, $callerPath)

    $fixtureBase = Invoke-Git -WorkingDirectory $callerPath -Arguments @("rev-list", "-n", "1", "--end-of-options", "origin/main")
    Assert-True (-not [string]::IsNullOrWhiteSpace($fixtureBase)) "Fixture origin/main did not resolve through the normal Git command."
    $fixtureBaseType = Invoke-Git -WorkingDirectory $callerPath -Arguments @("cat-file", "-t", $fixtureBase)
    Assert-Equal "commit" $fixtureBaseType "Fixture origin/main did not peel to a commit."

    if (Test-CaseSelected "refresh-remote-base") {
        Set-Content -LiteralPath (Join-Path $seedPath "tracked.txt") -Value "fresh remote base" -Encoding Ascii
        $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("add", "tracked.txt")
        $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("commit", "-m", "Refresh fixture remote")
        $freshRemoteBase = Invoke-Git -WorkingDirectory $seedPath -Arguments @("rev-parse", "HEAD")
        $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("push", "origin", "main")
        $staleRemoteBase = Invoke-Git -WorkingDirectory $callerPath -Arguments @("rev-parse", "origin/main")
        Assert-True ($staleRemoteBase -cne $freshRemoteBase) "Fixture origin/main should be stale before the helper runs."

        $refresh = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "439", "-Slug", "fresh-remote")
        Assert-Equal 0 $refresh.ExitCode "Helper should refresh an explicit remote base before creating the worktree.`n$($refresh.Output)"
        $refreshedWorktree = Join-Path $callerPath ".worktrees/codex-439-fresh-remote"
        $refreshedHead = Invoke-Git -WorkingDirectory $refreshedWorktree -Arguments @("rev-parse", "HEAD")
        $refreshedTrackingRef = Invoke-Git -WorkingDirectory $callerPath -Arguments @("rev-parse", "origin/main")
        Assert-Equal $freshRemoteBase $refreshedHead "Detached worktree should use the newly fetched remote base."
        Assert-Equal $freshRemoteBase $refreshedTrackingRef "Helper should refresh the explicit remote-tracking ref."
        Complete-Test "explicit remote base is refreshed before worktree creation"
    }

    Set-Content -LiteralPath (Join-Path $callerPath "tracked.txt") -Value "maintainer-owned change" -Encoding Ascii
    Set-Content -LiteralPath (Join-Path $callerPath "untracked.txt") -Value "maintainer-owned untracked file" -Encoding Ascii
    $statusBefore = Invoke-Git -WorkingDirectory $callerPath -Arguments @("status", "--short", "--untracked-files=all")

    if (Test-CaseSelected "worktree-root-reparse") {
        $reparseCallerPath = Join-Path $fixtureRoot "reparse caller"
        $outsideWorktreeRoot = Join-Path $fixtureRoot "outside worktrees"
        $reparseWorktreeRoot = Join-Path $reparseCallerPath ".worktrees"
        $outsideTarget = Join-Path $outsideWorktreeRoot "codex-438-reparse-root"
        $null = Invoke-Git -WorkingDirectory $fixtureRoot -Arguments @("clone", "-b", "main", $remotePath, $reparseCallerPath)
        New-Item -ItemType Directory -Path $outsideWorktreeRoot | Out-Null
        $linkType = if ([System.IO.Path]::DirectorySeparatorChar -eq [char]'\') { "Junction" } else { "SymbolicLink" }
        $null = New-Item -ItemType $linkType -Path $reparseWorktreeRoot -Value $outsideWorktreeRoot
        $reparseRootToRemove = $reparseWorktreeRoot
        $registrationsBefore = Invoke-Git -WorkingDirectory $reparseCallerPath -Arguments @("worktree", "list", "--porcelain")

        $reparse = Invoke-Helper -WorkingDirectory $reparseCallerPath -Arguments @("-IssueNumber", "438", "-Slug", "reparse-root")
        Assert-True ($reparse.ExitCode -ne 0) "Reparse-point worktree root should fail closed."
        Assert-Contains $reparse.Output "is a reparse point or symbolic link" "Reparse-root diagnostic was not clear."
        Assert-True (-not (Test-Path -LiteralPath $outsideTarget)) "Reparse root created an out-of-bound target."
        $registrationsAfter = Invoke-Git -WorkingDirectory $reparseCallerPath -Arguments @("worktree", "list", "--porcelain")
        Assert-Equal $registrationsBefore $registrationsAfter "Reparse root changed Git worktree registrations."
        Remove-Item -LiteralPath $reparseWorktreeRoot -Force
        $reparseRootToRemove = $null
        Complete-Test "reparse-point worktree root fails closed"
    }

    $requiresCreatedWorktree =
        (Test-CaseSelected "success-detached") -or
        (Test-CaseSelected "handoff-order") -or
        (Test-CaseSelected "guard-then-branch")
    if ($requiresCreatedWorktree) {
        $success = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "424", "-Slug", "dirty-source")
        Assert-Equal 0 $success.ExitCode "Dirty source checkout should not block detached worktree creation.`n$($success.Output)"
        $createdWorktree = Join-Path $callerPath ".worktrees/codex-424-dirty-source"
        Assert-True (Test-Path -LiteralPath $createdWorktree -PathType Container) "Expected worktree was not created."

        if (Test-CaseSelected "success-detached") {
            $symbolicHead = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("symbolic-ref", "-q", "HEAD") -WorkingDirectory $createdWorktree
            Assert-Equal 1 $symbolicHead.ExitCode "New worktree HEAD should be detached."
            $expectedHead = Invoke-Git -WorkingDirectory $callerPath -Arguments @("rev-parse", "origin/main")
            $actualHead = Invoke-Git -WorkingDirectory $createdWorktree -Arguments @("rev-parse", "HEAD")
            Assert-Equal $expectedHead $actualHead "Detached worktree should start at origin/main."
            $uncreatedBranch = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/issue-424/dirty-source") -WorkingDirectory $callerPath
            Assert-Equal 1 $uncreatedBranch.ExitCode "Helper must not create the planned issue branch."
            $statusAfter = Invoke-Git -WorkingDirectory $callerPath -Arguments @("status", "--short", "--untracked-files=all")
            Assert-Equal $statusBefore $statusAfter "Creating a detached worktree must preserve source-checkout status."
            Assert-Equal "maintainer-owned change" ((Get-Content -Raw -LiteralPath (Join-Path $callerPath "tracked.txt")).Trim()) "Tracked source content changed."
            Assert-Equal "maintainer-owned untracked file" ((Get-Content -Raw -LiteralPath (Join-Path $callerPath "untracked.txt")).Trim()) "Untracked source content changed."
            Complete-Test "dirty source state is preserved and origin/main is detached"
        }

        if (Test-CaseSelected "handoff-order") {
            $escapedGitExecutable = $gitExecutable.Replace("'", "''")
            $escapedPowerShellExecutable = $powerShellExecutable.Replace("'", "''")
            $guardLine = "& '$escapedPowerShellExecutable' -NoLogo -NoProfile -NonInteractive -File scripts/worktree_guard.ps1 -GitExecutable '$escapedGitExecutable'"
            $guardCaptureLine = '$guardSucceeded = $?; $guardExitCode = $LASTEXITCODE'
            $guardExitLine = 'if (-not $guardSucceeded -or $guardExitCode -ne 0) { if ($null -ne $guardExitCode -and $guardExitCode -ne 0) { exit $guardExitCode }; exit 1 }'
            $switchLine = "& '$escapedGitExecutable' switch -c 'issue-424/dirty-source'"
            $switchCaptureLine = '$switchSucceeded = $?; $switchExitCode = $LASTEXITCODE'
            $switchExitLine = 'if (-not $switchSucceeded -or $switchExitCode -ne 0) { if ($null -ne $switchExitCode -and $switchExitCode -ne 0) { exit $switchExitCode }; exit 1 }'
            $handoffLines = @(Get-PrintedHandoffLines -Output $success.Output)
            Assert-Equal $guardLine $handoffLines[0] "Handoff output omitted the pinned-Git worktree guard command."
            Assert-Equal $guardCaptureLine $handoffLines[1] "Handoff output omitted the guard status capture."
            Assert-Equal $guardExitLine $handoffLines[2] "Handoff output omitted the guard fail-fast gate."
            Assert-Equal $switchLine $handoffLines[3] "Branch creation must be printed after the guard fail-fast gate."
            Assert-Equal $switchCaptureLine $handoffLines[4] "Handoff output omitted the branch-command status capture."
            Assert-Equal $switchExitLine $handoffLines[5] "Handoff output omitted the branch-command fail-fast gate."
            Complete-Test "handoff pins Git and gates both commands fail-fast"
        }

        if (Test-CaseSelected "guard-then-branch") {
            $handoffScript = Join-Path $fixtureRoot "successful-handoff.ps1"
            Set-Content -LiteralPath $handoffScript -Value @(Get-PrintedHandoffLines -Output $success.Output) -Encoding Ascii
            $shimDirectory = Join-Path $fixtureRoot "guard git shim"
            New-Item -ItemType Directory -Path $shimDirectory | Out-Null
            $shimSentinel = Join-Path $shimDirectory "shim-invoked.txt"
            Set-Content -LiteralPath (Join-Path $shimDirectory "git.cmd") -Encoding Ascii -Value @(
                '@echo off',
                'echo invoked>"%~dp0shim-invoked.txt"',
                'exit /b 99'
            )
            $previousPath = $env:PATH
            try {
                $env:PATH = "$shimDirectory$([System.IO.Path]::PathSeparator)$previousPath"
                $handoff = Invoke-ProcessCapture -FilePath $powerShellExecutable -Arguments @("-NoLogo", "-NoProfile", "-NonInteractive", "-File", $handoffScript) -WorkingDirectory $createdWorktree
            }
            finally {
                $env:PATH = $previousPath
            }
            Assert-Equal 0 $handoff.ExitCode "Printed handoff block should guard and create the branch.`n$($handoff.Output)"
            Assert-True (-not (Test-Path -LiteralPath $shimSentinel)) "Printed handoff executed the PATH-first Git batch shim."
            $missingGuardExecutable = Join-Path $fixtureRoot "missing-git.exe"
            $missingGuard = Invoke-ProcessCapture -FilePath $powerShellExecutable -Arguments @("-NoLogo", "-NoProfile", "-NonInteractive", "-File", "scripts/worktree_guard.ps1", "-GitExecutable", $missingGuardExecutable) -WorkingDirectory $createdWorktree
            Assert-Equal 2 $missingGuard.ExitCode "Guard should preserve its advertised setup-failure exit code."
            $outsideRepoGuard = Invoke-ProcessCapture -FilePath $powerShellExecutable -Arguments @("-NoLogo", "-NoProfile", "-NonInteractive", "-File", (Join-Path $createdWorktree "scripts/worktree_guard.ps1"), "-GitExecutable", $gitExecutable) -WorkingDirectory $fixtureRoot
            Assert-Equal 2 $outsideRepoGuard.ExitCode "Guard should fail with its advertised repository-check exit code outside Git."
            Assert-Contains $outsideRepoGuard.Output "not inside a git repository" "Guard should distinguish an ordinary non-repository result from executable launch failure."
            $createdBranch = Invoke-Git -WorkingDirectory $createdWorktree -Arguments @("branch", "--show-current")
            Assert-Equal "issue-424/dirty-source" $createdBranch "Post-guard branch command created the wrong branch."
            Complete-Test "printed handoff executes with pinned Git under a shimmed PATH"
        }
    }

    if (Test-CaseSelected "handoff-fail-fast") {
        $failFast = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "441", "-Slug", "fail-fast")
        Assert-Equal 0 $failFast.ExitCode "Fail-fast fixture worktree creation should succeed.`n$($failFast.Output)"
        $failFastScript = Join-Path $fixtureRoot "failing-handoff.ps1"
        Set-Content -LiteralPath $failFastScript -Value @(Get-PrintedHandoffLines -Output $failFast.Output) -Encoding Ascii
        $handoffFailure = Invoke-ProcessCapture -FilePath $powerShellExecutable -Arguments @("-NoLogo", "-NoProfile", "-NonInteractive", "-File", $failFastScript) -WorkingDirectory $callerPath
        Assert-Equal 1 $handoffFailure.ExitCode "Guard failure in the coordinator checkout should preserve its exit code."
        $unexpectedBranch = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/issue-441/fail-fast") -WorkingDirectory $callerPath
        Assert-Equal 1 $unexpectedBranch.ExitCode "Guard failure must stop before branch creation in the coordinator checkout."
        Complete-Test "guard failure stops the printed handoff before branch creation"
    }

    if (Test-CaseSelected "handoff-missing-executable") {
        $missingExecutableResult = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "442", "-Slug", "missing-executable")
        Assert-Equal 0 $missingExecutableResult.ExitCode "Missing-executable fixture worktree creation should succeed.`n$($missingExecutableResult.Output)"
        $missingExecutableWorktree = Join-Path $callerPath ".worktrees/codex-442-missing-executable"
        $missingExecutableLines = @(Get-PrintedHandoffLines -Output $missingExecutableResult.Output)
        $missingExecutablePath = (Join-Path $fixtureRoot "removed-git.exe").Replace("'", "''")
        $missingExecutableLines[3] = "& '$missingExecutablePath' switch -c 'issue-442/missing-executable'"
        $missingExecutableScript = Join-Path $fixtureRoot "missing-executable-handoff.ps1"
        Set-Content -LiteralPath $missingExecutableScript -Value $missingExecutableLines -Encoding Ascii
        $missingExecutableHandoff = Invoke-ProcessCapture -FilePath $powerShellExecutable -Arguments @("-NoLogo", "-NoProfile", "-NonInteractive", "-File", $missingExecutableScript) -WorkingDirectory $missingExecutableWorktree
        Assert-Equal 1 $missingExecutableHandoff.ExitCode "A disappeared Git executable should fail the handoff even when LASTEXITCODE remains zero."
        $missingExecutableBranch = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/issue-442/missing-executable") -WorkingDirectory $callerPath
        Assert-Equal 1 $missingExecutableBranch.ExitCode "A disappeared Git executable must not create the planned branch."

        $missingHostLines = @(Get-PrintedHandoffLines -Output $missingExecutableResult.Output)
        $missingHostPath = (Join-Path $fixtureRoot "removed-powershell.exe").Replace("'", "''")
        $missingHostLines[0] = "& '$missingHostPath' -NoLogo -NoProfile -NonInteractive -File scripts/worktree_guard.ps1 -GitExecutable '$($gitExecutable.Replace("'", "''"))'"
        $missingHostScript = Join-Path $fixtureRoot "missing-host-handoff.ps1"
        Set-Content -LiteralPath $missingHostScript -Value @('$global:LASTEXITCODE = $null') -Encoding Ascii
        Add-Content -LiteralPath $missingHostScript -Value $missingHostLines -Encoding Ascii
        $missingHostHandoff = Invoke-ProcessCapture -FilePath $powerShellExecutable -Arguments @("-NoLogo", "-NoProfile", "-NonInteractive", "-File", $missingHostScript) -WorkingDirectory $missingExecutableWorktree
        Assert-Equal 1 $missingHostHandoff.ExitCode "A disappeared PowerShell host should fail closed when LASTEXITCODE is null."
        $missingHostBranch = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/issue-442/missing-executable") -WorkingDirectory $callerPath
        Assert-Equal 1 $missingHostBranch.ExitCode "A disappeared PowerShell host must not create the planned branch."
        Complete-Test "missing Git or PowerShell executables fail the printed handoff closed"
    }

    if (Test-CaseSelected "existing-branch") {
        $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("branch", "issue-425/existing", "origin/main")
        $branchCollision = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "425", "-Slug", "existing")
        Assert-True ($branchCollision.ExitCode -ne 0) "Existing requested branch should fail closed."
        Assert-Contains $branchCollision.Output "Branch already exists: issue-425/existing" "Branch collision diagnostic was not clear."
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $callerPath ".worktrees/codex-425-existing"))) "Branch collision should not create a worktree."
        Complete-Test "existing branch fails closed"
    }

    if (Test-CaseSelected "existing-path") {
        $pathCollisionTarget = Join-Path $callerPath ".worktrees/codex-426-path-collision"
        New-Item -ItemType Directory -Force -Path $pathCollisionTarget | Out-Null
        $pathCollision = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "426", "-Slug", "path-collision")
        Assert-True ($pathCollision.ExitCode -ne 0) "Existing target path should fail closed."
        Assert-Contains $pathCollision.Output "Worktree path already exists:" "Path collision diagnostic was not clear."
        Complete-Test "existing target path fails closed"
    }

    if (Test-CaseSelected "worktree-root-traversal") {
        $registrationsBefore = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
        $traversalTarget = Join-Path (Split-Path -Parent $callerPath) "escaped-worktrees/codex-435-root-traversal"
        $traversal = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "435", "-Slug", "root-traversal", "-WorktreeRoot", "../escaped-worktrees")
        Assert-True ($traversal.ExitCode -ne 0) "Traversal worktree root should fail closed."
        Assert-Contains $traversal.Output "Invalid worktree root: '../escaped-worktrees'." "Traversal-root diagnostic was not clear."
        Assert-True (-not (Test-Path -LiteralPath $traversalTarget)) "Traversal root created an out-of-bound target."
        $registrationsAfter = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
        Assert-Equal $registrationsBefore $registrationsAfter "Traversal root changed Git worktree registrations."
        Complete-Test "traversal worktree root fails closed"
    }

    if (Test-CaseSelected "worktree-root-rooted") {
        $registrationsBefore = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
        $rootedWorktreeRoot = Join-Path $fixtureRoot "rooted worktrees"
        $rootedTarget = Join-Path $rootedWorktreeRoot "codex-436-rooted-root"
        $rooted = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "436", "-Slug", "rooted-root", "-WorktreeRoot", $rootedWorktreeRoot)
        Assert-True ($rooted.ExitCode -ne 0) "Rooted worktree root should fail closed."
        Assert-Contains $rooted.Output "Invalid worktree root:" "Rooted-root diagnostic was not clear."
        Assert-True (-not (Test-Path -LiteralPath $rootedTarget)) "Rooted input created an out-of-bound target."
        $registrationsAfter = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
        Assert-Equal $registrationsBefore $registrationsAfter "Rooted input changed Git worktree registrations."
        Complete-Test "rooted worktree root fails closed"
    }

    if (Test-CaseSelected "worktree-root-unapproved") {
        $registrationsBefore = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
        $unapprovedTarget = Join-Path $callerPath "custom-worktrees/codex-437-unapproved-root"
        $unapproved = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "437", "-Slug", "unapproved-root", "-WorktreeRoot", "custom-worktrees")
        Assert-True ($unapproved.ExitCode -ne 0) "Unapproved in-repository worktree root should fail closed."
        Assert-Contains $unapproved.Output "Invalid worktree root: 'custom-worktrees'." "Unapproved-root diagnostic was not clear."
        Assert-True (-not (Test-Path -LiteralPath $unapprovedTarget)) "Unapproved root created a target."
        $registrationsAfter = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
        Assert-Equal $registrationsBefore $registrationsAfter "Unapproved root changed Git worktree registrations."
        Complete-Test "unapproved in-repository worktree root fails closed"
    }

    if (Test-CaseSelected "invalid-slug") {
        $invalidSlug = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "427", "-Slug", "Invalid_Slug")
        Assert-True ($invalidSlug.ExitCode -ne 0) "Invalid slug should fail closed."
        Assert-Contains $invalidSlug.Output "Invalid slug: 'Invalid_Slug'." "Invalid slug diagnostic was not clear."
        Complete-Test "invalid slug fails closed"
    }

    if (Test-CaseSelected "invalid-branch") {
        $invalidBranch = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "428", "-Slug", "invalid-branch", "-BranchName", "invalid branch")
        Assert-True ($invalidBranch.ExitCode -ne 0) "Invalid branch should fail closed."
        Assert-Contains $invalidBranch.Output "Invalid branch name: invalid branch" "Invalid branch diagnostic was not clear."
        Complete-Test "invalid branch fails closed"
    }

    if (Test-CaseSelected "batch-shim-bypass") {
        $shimDirectory = Join-Path $fixtureRoot "unsafe git shim"
        New-Item -ItemType Directory -Path $shimDirectory | Out-Null
        $shimPath = Join-Path $shimDirectory "git.cmd"
        $shimSentinel = Join-Path $shimDirectory "shim-invoked.txt"
        Set-Content -LiteralPath $shimPath -Encoding Ascii -Value @(
            '@echo off',
            'echo invoked>"%~dp0shim-invoked.txt"',
            'exit /b 99'
        )
        $previousPath = $env:PATH
        try {
            $env:PATH = "$shimDirectory$([System.IO.Path]::PathSeparator)$previousPath"
            $shimBypass = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "434", "-Slug", "shim-bypass", "-BaseBranch", "origin/not-there")
        }
        finally {
            $env:PATH = $previousPath
        }
        Assert-True ($shimBypass.ExitCode -ne 0) "Missing base should still fail when an unsafe Git batch shim is first on PATH."
        Assert-Contains $shimBypass.Output "Base commit not found: origin/not-there" "Helper did not bypass the unsafe Git batch shim."
        Assert-True (-not (Test-Path -LiteralPath $shimSentinel)) "Helper executed the PATH-first Git batch shim."
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $callerPath ".worktrees/codex-434-shim-bypass"))) "Shim-bypass probe should not create a worktree."
        Complete-Test "PATH-first Git batch shim is bypassed"
    }

    if (Test-CaseSelected "metachar-base") {
        $canaryPath = Join-Path $callerPath "git-shim-canary.txt"
        $metacharBase = "origin/main&echo TASKDECK_CANARY>git-shim-canary.txt"
        $metacharResult = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "433", "-Slug", "metachar-base", "-BaseBranch", $metacharBase)
        Assert-True ($metacharResult.ExitCode -ne 0) "Metacharacter base should fail closed."
        Assert-Contains $metacharResult.Output "Base commit not found:" "Metacharacter base diagnostic was not clear."
        Assert-True (-not (Test-Path -LiteralPath $canaryPath)) "Git shim metacharacters escaped the native argument boundary."
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $callerPath ".worktrees/codex-433-metachar-base"))) "Metacharacter base should not create a worktree."
        Complete-Test "metacharacter base cannot escape the native Git argument boundary"
    }

    if (Test-CaseSelected "revision-range-base") {
        $revisionRange = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "432", "-Slug", "revision-range", "-BaseBranch", "HEAD~1..HEAD")
        Assert-True ($revisionRange.ExitCode -ne 0) "Revision-set base should fail closed instead of selecting one commit."
        Assert-Contains $revisionRange.Output "Base commit not found: HEAD~1..HEAD" "Revision-set base diagnostic was not clear."
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $callerPath ".worktrees/codex-432-revision-range"))) "Revision-set base should not create a worktree."
        Complete-Test "revision-set base fails closed"
    }

    if (Test-CaseSelected "annotated-tag-base") {
        $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("config", "user.name", "Taskdeck Test")
        $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("config", "user.email", "taskdeck-test@example.invalid")
        $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("tag", "-a", "fixture-annotated", $fixtureBase, "-m", "Fixture annotated tag")
        $annotatedTag = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "440", "-Slug", "annotated-tag", "-BaseBranch", "fixture-annotated")
        Assert-Equal 0 $annotatedTag.ExitCode "Annotated tag should peel to its commit base.`n$($annotatedTag.Output)"
        $annotatedWorktree = Join-Path $callerPath ".worktrees/codex-440-annotated-tag"
        $annotatedHead = Invoke-Git -WorkingDirectory $annotatedWorktree -Arguments @("rev-parse", "HEAD")
        Assert-Equal $fixtureBase $annotatedHead "Annotated tag worktree should detach at the tagged commit."
        Complete-Test "annotated tag peels to one commit"
    }

    if (Test-CaseSelected "missing-base") {
        $missingBase = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "429", "-Slug", "missing-base", "-BaseBranch", "origin/not-there")
        Assert-True ($missingBase.ExitCode -ne 0) "Missing base should fail closed."
        Assert-Contains $missingBase.Output "Base commit not found: origin/not-there" "Missing-base diagnostic was not clear."
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $callerPath ".worktrees/codex-429-missing-base"))) "Missing base should not create a worktree."
        Complete-Test "missing base fails closed"
    }

    if (Test-CaseSelected "what-if") {
        $whatIfTarget = Join-Path $callerPath ".worktrees/codex-430-what-if"
        Set-Content -LiteralPath (Join-Path $seedPath "tracked.txt") -Value "what-if remote advance" -Encoding Ascii
        $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("add", "tracked.txt")
        $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("commit", "-m", "Advance remote before WhatIf")
        $whatIfRemoteBase = Invoke-Git -WorkingDirectory $seedPath -Arguments @("rev-parse", "HEAD")
        $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("push", "origin", "main")
        $whatIfTrackingBefore = Invoke-Git -WorkingDirectory $callerPath -Arguments @("rev-parse", "origin/main")
        Assert-True ($whatIfTrackingBefore -cne $whatIfRemoteBase) "Fixture origin/main should be stale before the WhatIf probe."

        $whatIf = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "430", "-Slug", "what-if", "-WhatIf")
        Assert-Equal 0 $whatIf.ExitCode "WhatIf should validate inputs without failing."
        Assert-True (-not (Test-Path -LiteralPath $whatIfTarget)) "WhatIf must not create the target path."
        $whatIfBranch = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/issue-430/what-if") -WorkingDirectory $callerPath
        Assert-Equal 1 $whatIfBranch.ExitCode "WhatIf must not create the planned branch."
        $whatIfTrackingAfter = Invoke-Git -WorkingDirectory $callerPath -Arguments @("rev-parse", "origin/main")
        Assert-Equal $whatIfTrackingBefore $whatIfTrackingAfter "WhatIf must not refresh the remote-tracking ref."
        Complete-Test "WhatIf performs no worktree, branch, or remote-ref mutation"
    }

    if (Test-CaseSelected "git-add-failure") {
        $gitFailureTarget = Join-Path $callerPath ".worktrees/codex-431-git-failure"
        $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "add", "--detach", $gitFailureTarget, "origin/main")
        $parkedGitFailureTarget = Join-Path $callerPath ".worktrees/parked-git-failure"
        Move-Item -LiteralPath $gitFailureTarget -Destination $parkedGitFailureTarget
        $gitFailure = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "431", "-Slug", "git-failure")
        Assert-True ($gitFailure.ExitCode -ne 0) "Native git worktree failure should fail closed."
        Assert-Contains $gitFailure.Output "git worktree add failed for" "Git failure diagnostic omitted the failed operation."
        Assert-Contains ($gitFailure.Output -replace '\s+', ' ') "is a missing but already registered worktree" "Git stderr context should be preserved."
        Assert-Contains $gitFailure.Output "(exit code 128)" "Git failure diagnostic omitted the native exit code."
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $callerPath ".worktrees/codex-431-git-failure"))) "Failed git worktree add should not leave a target worktree."
        Complete-Test "native git failure propagates with a clear diagnostic"
    }

    Write-Host "PASS: $passed/$($selectedCases.Count) selected worktree helper regression checks"
}
catch {
    $testFailure = $_
}
finally {
    try {
        $resolvedTestRoot = [System.IO.Path]::GetFullPath($testRoot)
        $resolvedTempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd([System.IO.Path]::DirectorySeparatorChar)
        $testRootName = Split-Path -Leaf $resolvedTestRoot
        $testRootPrefix = "taskdeck-worktree-helper-"
        $testRootId = if ($testRootName.StartsWith($testRootPrefix, [StringComparison]::Ordinal)) {
            $testRootName.Substring($testRootPrefix.Length)
        }
        else {
            ""
        }
        $parsedTestRootId = [Guid]::Empty
        $hasValidTestRootId = [Guid]::TryParseExact($testRootId, "N", [ref]$parsedTestRootId)
        $hasExpectedParent = (Split-Path -Parent $resolvedTestRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar) -ceq $resolvedTempRoot
        if (-not $hasValidTestRootId -or -not $hasExpectedParent) {
            throw "Refusing test cleanup for unexpected temp root: $resolvedTestRoot"
        }

        if ($null -ne $reparseRootToRemove) {
            $resolvedReparseRoot = [System.IO.Path]::GetFullPath($reparseRootToRemove)
            $expectedReparsePrefix = $resolvedTestRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
            if (-not $resolvedReparseRoot.StartsWith($expectedReparsePrefix, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Refusing reparse-point cleanup outside the test root: $resolvedReparseRoot"
            }
            try {
                $reparseItem = Get-Item -LiteralPath $resolvedReparseRoot -Force -ErrorAction Stop
            }
            catch [System.Management.Automation.ItemNotFoundException] {
                $reparseItem = $null
            }
            if ($null -ne $reparseItem -and ($reparseItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                Remove-Item -LiteralPath $resolvedReparseRoot -Force
            }
            elseif ($null -ne $reparseItem) {
                throw "Refusing cleanup because expected reparse point became a normal path: $resolvedReparseRoot"
            }
        }

        if (Test-Path -LiteralPath $resolvedTestRoot) {
            Get-ChildItem -LiteralPath $resolvedTestRoot -Recurse -Force -File | ForEach-Object { $_.IsReadOnly = $false }
            Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
        }
    }
    catch {
        $cleanupFailure = $_
        Write-Warning "Test cleanup failed; preserved exact temp root '$testRoot'. $($_.Exception.Message)"
    }
}

if ($null -ne $testFailure) {
    throw $testFailure
}
if ($null -ne $cleanupFailure) {
    throw $cleanupFailure
}
