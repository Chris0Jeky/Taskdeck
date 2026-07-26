[CmdletBinding()]
param(
    [ValidateSet(
        "success-detached",
        "handoff-order",
        "guard-then-branch",
        "existing-branch",
        "existing-path",
        "invalid-slug",
        "invalid-branch",
        "batch-shim-bypass",
        "metachar-base",
        "revision-range-base",
        "missing-base",
        "what-if",
        "git-add-failure"
    )]
    [string[]]$Case = @(
        "success-detached",
        "handoff-order",
        "guard-then-branch",
        "existing-branch",
        "existing-path",
        "invalid-slug",
        "invalid-branch",
        "batch-shim-bypass",
        "metachar-base",
        "revision-range-base",
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

    Set-Content -LiteralPath (Join-Path $callerPath "tracked.txt") -Value "maintainer-owned change" -Encoding Ascii
    Set-Content -LiteralPath (Join-Path $callerPath "untracked.txt") -Value "maintainer-owned untracked file" -Encoding Ascii
    $statusBefore = Invoke-Git -WorkingDirectory $callerPath -Arguments @("status", "--short", "--untracked-files=all")

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
            $guardLine = "powershell -File scripts/worktree_guard.ps1"
            $escapedGitExecutable = $gitExecutable.Replace("'", "''")
            $switchLine = "& '$escapedGitExecutable' switch -c 'issue-424/dirty-source'"
            $guardIndex = $success.Output.IndexOf($guardLine, [StringComparison]::Ordinal)
            $switchIndex = $success.Output.IndexOf($switchLine, [StringComparison]::Ordinal)
            Assert-True ($guardIndex -ge 0) "Handoff output omitted the worktree guard command."
            Assert-True ($switchIndex -gt $guardIndex) "Branch creation must be printed after the guard command."
            Complete-Test "handoff prints guard before explicit branch creation"
        }

        if (Test-CaseSelected "guard-then-branch") {
            $guard = Invoke-ProcessCapture -FilePath $powerShellExecutable -Arguments @("-NoLogo", "-NoProfile", "-NonInteractive", "-File", "scripts/worktree_guard.ps1") -WorkingDirectory $createdWorktree
            Assert-Equal 0 $guard.ExitCode "Printed guard command should pass in the created worktree."
            $branchCreation = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("switch", "-c", "issue-424/dirty-source") -WorkingDirectory $createdWorktree
            Assert-Equal 0 $branchCreation.ExitCode "Printed post-guard branch command should create the issue branch."
            $createdBranch = Invoke-Git -WorkingDirectory $createdWorktree -Arguments @("branch", "--show-current")
            Assert-Equal "issue-424/dirty-source" $createdBranch "Post-guard branch command created the wrong branch."
            Complete-Test "printed guard and branch commands execute in order"
        }
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
        $revisionRange = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "432", "-Slug", "revision-range", "-BaseBranch", "origin/main~1..origin/main")
        Assert-True ($revisionRange.ExitCode -ne 0) "Revision-set base should fail closed instead of selecting one commit."
        Assert-Contains $revisionRange.Output "Base commit not found: origin/main~1..origin/main" "Revision-set base diagnostic was not clear."
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $callerPath ".worktrees/codex-432-revision-range"))) "Revision-set base should not create a worktree."
        Complete-Test "revision-set base fails closed"
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
        $whatIf = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "430", "-Slug", "what-if", "-WhatIf")
        Assert-Equal 0 $whatIf.ExitCode "WhatIf should validate inputs without failing."
        Assert-True (-not (Test-Path -LiteralPath $whatIfTarget)) "WhatIf must not create the target path."
        $whatIfBranch = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/issue-430/what-if") -WorkingDirectory $callerPath
        Assert-Equal 1 $whatIfBranch.ExitCode "WhatIf must not create the planned branch."
        Complete-Test "WhatIf performs no mutation"
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
