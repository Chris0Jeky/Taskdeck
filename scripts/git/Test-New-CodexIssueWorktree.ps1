[CmdletBinding()]
param(
    [ValidateSet(
        "success-detached",
        "handoff-order",
        "guard-then-branch",
        "handoff-fail-fast",
        "handoff-missing-executable",
        "initializer-validation",
        "headless-permission-contract",
        "existing-branch",
        "existing-path",
        "worktree-root-traversal",
        "worktree-root-rooted",
        "worktree-root-unapproved",
        "worktree-root-case-variant",
        "worktree-root-reparse",
        "invalid-slug",
        "invalid-branch",
        "batch-shim-bypass",
        "metachar-base",
        "revision-range-base",
        "annotated-tag-base",
        "base-missing-handoff-artifacts",
        "fully-qualified-ref",
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
        "initializer-validation",
        "headless-permission-contract",
        "existing-branch",
        "existing-path",
        "worktree-root-traversal",
        "worktree-root-rooted",
        "worktree-root-unapproved",
        "worktree-root-case-variant",
        "worktree-root-reparse",
        "invalid-slug",
        "invalid-branch",
        "batch-shim-bypass",
        "metachar-base",
        "revision-range-base",
        "annotated-tag-base",
        "base-missing-handoff-artifacts",
        "fully-qualified-ref",
        "refresh-remote-base",
        "missing-base",
        "what-if",
        "git-add-failure"
    )
)

$ErrorActionPreference = "Stop"

$helperPath = Join-Path $PSScriptRoot "New-CodexIssueWorktree.ps1"
$initializerPath = Join-Path $PSScriptRoot "Initialize-CodexIssueWorktree.ps1"
$claudeSettingsPath = Join-Path $PSScriptRoot "../../.claude/settings.json"
$worktreeProtocolPath = Join-Path $PSScriptRoot "../../docs/WORKTREE_AGENT_PROTOCOL.md"
$worktreeGuidancePaths = @(
    $worktreeProtocolPath,
    (Join-Path $PSScriptRoot "../../docs/tooling/CODEX_AUTONOMY_RUNBOOK.md"),
    (Join-Path $PSScriptRoot "../../AGENTS.md"),
    (Join-Path $PSScriptRoot "../../CLAUDE.md"),
    (Join-Path $PSScriptRoot "../../.claude/README.md"),
    (Join-Path $PSScriptRoot "../../.claude/skills/issue-to-pr/SKILL.md"),
    (Join-Path $PSScriptRoot "../../.claude/skills/taskdeck-issue-batch-orchestrator/SKILL.md"),
    (Join-Path $PSScriptRoot "../../.claude/skills/taskdeck-worktree-issue-worker/SKILL.md"),
    (Join-Path $PSScriptRoot "../../.codex/skills/taskdeck-issue-batch-orchestrator/SKILL.md"),
    (Join-Path $PSScriptRoot "../../.codex/skills/taskdeck-worktree-issue-worker/SKILL.md")
)
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

    $fixtureHelperPath = Join-Path $WorkingDirectory "scripts/git/New-CodexIssueWorktree.ps1"
    $hostArguments = @("-NoLogo", "-NoProfile", "-NonInteractive", "-File", $fixtureHelperPath) + $Arguments
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

function Assert-NormalizedContains {
    param(
        [string]$Text,
        [string]$ExpectedSubstring,
        [string]$Message
    )

    Assert-Contains ($Text -replace '\s+', ' ') ($ExpectedSubstring -replace '\s+', ' ') $Message
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

function Get-ModeledEffectivePermissionConfiguration {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$SettingSources,

        [Parameter(Mandatory = $true)]
        [string]$ProjectSettingsPath,

        [Parameter(Mandatory = $true)]
        [string]$MainCheckoutLocalSettingsPath,

        [string[]]$CommandLineAllowRules = @(),

        [string]$CommandLinePermissionMode,

        [bool]$WorkspaceTrusted = $true
    )

    $effectiveRules = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $effectiveAdditionalDirectories = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $effectiveEnvironment = @{}
    $effectivePermissionMode = $null
    foreach ($source in $SettingSources) {
        $settingsPath = switch ($source) {
            "project" { $ProjectSettingsPath }
            "local" { $MainCheckoutLocalSettingsPath }
            default { throw "Unsupported modeled setting source: $source" }
        }

        if (-not (Test-Path -LiteralPath $settingsPath -PathType Leaf)) {
            continue
        }

        if ($source -ceq "project" -and -not $WorkspaceTrusted) {
            continue
        }

        $settings = Get-Content -Raw -LiteralPath $settingsPath | ConvertFrom-Json
        if (-not [string]::IsNullOrWhiteSpace($settings.permissions.defaultMode)) {
            $effectivePermissionMode = [string]$settings.permissions.defaultMode
        }
        foreach ($rule in @($settings.permissions.allow)) {
            if (-not [string]::IsNullOrWhiteSpace($rule)) {
                $null = $effectiveRules.Add([string]$rule)
            }
        }
        if ($null -ne $settings.env) {
            foreach ($environmentEntry in @($settings.env.PSObject.Properties)) {
                $effectiveEnvironment[$environmentEntry.Name] = [string]$environmentEntry.Value
            }
        }
        foreach ($additionalDirectory in @($settings.additionalDirectories)) {
            if (-not [string]::IsNullOrWhiteSpace($additionalDirectory)) {
                $null = $effectiveAdditionalDirectories.Add([string]$additionalDirectory)
            }
        }
    }

    foreach ($rule in $CommandLineAllowRules) {
        if (-not [string]::IsNullOrWhiteSpace($rule)) {
            $null = $effectiveRules.Add($rule)
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($CommandLinePermissionMode)) {
        $effectivePermissionMode = $CommandLinePermissionMode
    }

    return [pscustomobject]@{
        PermissionMode = $effectivePermissionMode
        Allow = @($effectiveRules)
        Environment = $effectiveEnvironment
        AdditionalDirectories = @($effectiveAdditionalDirectories)
        WorkspaceTrusted = $WorkspaceTrusted
    }
}

function Get-PrintedHandoffLines {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Output
    )

    $outputLines = @($Output -split '\r?\n')
    $marker = "PowerShell worker handoff (run this entire block unchanged):"
    $markerIndex = [Array]::IndexOf($outputLines, $marker)
    Assert-True ($markerIndex -ge 0) "Helper output omitted the PowerShell handoff marker."
    Assert-True (($markerIndex + 3) -lt $outputLines.Count) "Helper output omitted one or more fail-fast handoff commands."

    return @(
        $outputLines[$markerIndex + 1].TrimStart(),
        $outputLines[$markerIndex + 2].TrimStart(),
        $outputLines[$markerIndex + 3].TrimStart()
    )
}

function Get-PrintedInitializerLaunchRule {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Output
    )

    $outputLines = @($Output -split '\r?\n')
    $marker = "Claude Code task-scoped initializer allow rule (additive PowerShell transport):"
    $markerIndex = [Array]::IndexOf($outputLines, $marker)
    Assert-True ($markerIndex -ge 0) "Helper output omitted the task-scoped initializer allow-rule marker."
    Assert-True (($markerIndex + 4) -lt $outputLines.Count) "Helper output omitted the task-scoped initializer allow-rule transport."
    Assert-Equal "`$initializerAllowRule = @'" $outputLines[$markerIndex + 1].TrimStart() "Helper output omitted the single-quoted here-string opener."
    Assert-Equal "'@" $outputLines[$markerIndex + 3].TrimStart() "Helper output omitted the single-quoted here-string terminator."
    Assert-Equal '# Pass as one argv value: claude ... --allowedTools $initializerAllowRule' $outputLines[$markerIndex + 4].TrimStart() "Helper output did not pass the rule variable as one CLI argv value."

    return $outputLines[$markerIndex + 2].TrimStart()
}

try {
    New-Item -ItemType Directory -Path $testRoot | Out-Null

    $fixtureRoot = Join-Path $testRoot "fixture's path with spaces"
    New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
    $remotePath = Join-Path $fixtureRoot "remote.git"
    $seedPath = Join-Path $fixtureRoot "seed"
    $callerPath = Join-Path $fixtureRoot "caller"

    $null = Invoke-Git -WorkingDirectory $fixtureRoot -Arguments @("init", "--bare", $remotePath)
    $null = Invoke-Git -WorkingDirectory $fixtureRoot -Arguments @("init", "-b", "main", $seedPath)
    $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("config", "user.name", "Taskdeck Test")
    $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("config", "user.email", "taskdeck-test@example.invalid")
    Set-Content -LiteralPath (Join-Path $seedPath ".gitignore") -Value @(".worktrees/", ".claude/settings.local.json") -Encoding Ascii
    Set-Content -LiteralPath (Join-Path $seedPath "tracked.txt") -Value "committed" -Encoding Ascii
    $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("add", ".gitignore", "tracked.txt")
    $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("commit", "-m", "Seed pre-helper fixture")
    $preHelperCommit = Invoke-Git -WorkingDirectory $seedPath -Arguments @("rev-parse", "HEAD")
    $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("tag", "pre-helper-base", $preHelperCommit)
    $seedScriptsPath = Join-Path $seedPath "scripts"
    New-Item -ItemType Directory -Path $seedScriptsPath | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "../worktree_guard.ps1") -Destination (Join-Path $seedScriptsPath "worktree_guard.ps1")
    $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("add", "scripts/worktree_guard.ps1")
    $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("commit", "-m", "Add worktree guard")
    $guardOnlyCommit = Invoke-Git -WorkingDirectory $seedPath -Arguments @("rev-parse", "HEAD")
    $seedGitScriptsPath = Join-Path $seedScriptsPath "git"
    New-Item -ItemType Directory -Path $seedGitScriptsPath | Out-Null
    Copy-Item -LiteralPath $initializerPath -Destination (Join-Path $seedGitScriptsPath "Initialize-CodexIssueWorktree.ps1")
    Copy-Item -LiteralPath $helperPath -Destination (Join-Path $seedGitScriptsPath "New-CodexIssueWorktree.ps1")
    $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("add", "scripts/git/Initialize-CodexIssueWorktree.ps1", "scripts/git/New-CodexIssueWorktree.ps1")
    $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("commit", "-m", "Add worktree helper and initializer")
    $helperArtifactCommit = Invoke-Git -WorkingDirectory $seedPath -Arguments @("rev-parse", "HEAD")
    $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("switch", "-c", "modified-handoff-base")
    Add-Content -LiteralPath (Join-Path $seedGitScriptsPath "Initialize-CodexIssueWorktree.ps1") -Value "# Modified fixture initializer." -Encoding Ascii
    $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("add", "scripts/git/Initialize-CodexIssueWorktree.ps1")
    $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("commit", "-m", "Modify fixture initializer")
    $modifiedHandoffCommit = Invoke-Git -WorkingDirectory $seedPath -Arguments @("rev-parse", "HEAD")
    $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("switch", "main")
    $modifiedGuardCanary = Join-Path $fixtureRoot "modified-base-guard-executed.txt"
    $escapedModifiedGuardCanary = $modifiedGuardCanary.Replace("'", "''")
    $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("switch", "-c", "modified-guard-base")
    Add-Content -LiteralPath (Join-Path $seedScriptsPath "worktree_guard.ps1") -Encoding Ascii -Value "[System.IO.File]::WriteAllText('$escapedModifiedGuardCanary', 'EXECUTED')"
    $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("add", "scripts/worktree_guard.ps1")
    $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("commit", "-m", "Modify fixture guard")
    $modifiedGuardCommit = Invoke-Git -WorkingDirectory $seedPath -Arguments @("rev-parse", "HEAD")
    $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("switch", "main")
    Set-Content -LiteralPath (Join-Path $seedPath "tracked.txt") -Value "advanced" -Encoding Ascii
    $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("add", "tracked.txt")
    $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("commit", "-m", "Advance fixture")
    $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("remote", "add", "origin", $remotePath)
    $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("push", "-u", "origin", "main")
    $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("push", "origin", "pre-helper-base")
    $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("push", "origin", "modified-handoff-base")
    $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("push", "origin", "modified-guard-base")
    $null = Invoke-Git -WorkingDirectory $fixtureRoot -Arguments @("clone", "-b", "main", $remotePath, $callerPath)

    $fixtureBase = Invoke-Git -WorkingDirectory $callerPath -Arguments @("rev-list", "-n", "1", "--end-of-options", "origin/main")
    Assert-True (-not [string]::IsNullOrWhiteSpace($fixtureBase)) "Fixture origin/main did not resolve through the normal Git command."
    $fixtureBaseType = Invoke-Git -WorkingDirectory $callerPath -Arguments @("cat-file", "-t", $fixtureBase)
    Assert-Equal "commit" $fixtureBaseType "Fixture origin/main did not peel to a commit."

    if (Test-CaseSelected "refresh-remote-base") {
        $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("branch", "origin/main", $fixtureBase)
        $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("tag", "origin/main", $fixtureBase)
        Set-Content -LiteralPath (Join-Path $seedPath "tracked.txt") -Value "fresh remote base" -Encoding Ascii
        $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("add", "tracked.txt")
        $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("commit", "-m", "Refresh fixture remote")
        $freshRemoteBase = Invoke-Git -WorkingDirectory $seedPath -Arguments @("rev-parse", "HEAD")
        $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("push", "origin", "main")
        $staleRemoteBase = Invoke-Git -WorkingDirectory $callerPath -Arguments @("rev-parse", "refs/remotes/origin/main")
        Assert-True ($staleRemoteBase -cne $freshRemoteBase) "Fixture origin/main should be stale before the helper runs."

        $refresh = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "439", "-Slug", "fresh-remote")
        Assert-Equal 0 $refresh.ExitCode "Helper should refresh an explicit remote base before creating the worktree.`n$($refresh.Output)"
        $refreshedWorktree = Join-Path $callerPath ".worktrees/codex-439-fresh-remote"
        $refreshedHead = Invoke-Git -WorkingDirectory $refreshedWorktree -Arguments @("rev-parse", "HEAD")
        $refreshedTrackingRef = Invoke-Git -WorkingDirectory $callerPath -Arguments @("rev-parse", "refs/remotes/origin/main")
        $shadowBranch = Invoke-Git -WorkingDirectory $callerPath -Arguments @("rev-parse", "refs/heads/origin/main")
        $shadowTag = Invoke-Git -WorkingDirectory $callerPath -Arguments @("rev-parse", "refs/tags/origin/main")
        Assert-Equal $freshRemoteBase $refreshedHead "Detached worktree should use the newly fetched remote base."
        Assert-Equal $freshRemoteBase $refreshedTrackingRef "Helper should refresh the explicit remote-tracking ref."
        Assert-Equal $fixtureBase $shadowBranch "Refreshing a remote base should not rewrite a same-named local branch."
        Assert-Equal $fixtureBase $shadowTag "Refreshing a remote base should not rewrite a same-named local tag."

        $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("config", "remote.-u.url", $remotePath)
        $optionRemote = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "445", "-Slug", "option-remote", "-BaseBranch", "-u/main")
        Assert-Equal 0 $optionRemote.ExitCode "A configured option-looking remote should remain a repository argv after --.`n$($optionRemote.Output)"
        $optionRemoteWorktree = Join-Path $callerPath ".worktrees/codex-445-option-remote"
        $optionRemoteHead = Invoke-Git -WorkingDirectory $optionRemoteWorktree -Arguments @("rev-parse", "HEAD")
        Assert-Equal $freshRemoteBase $optionRemoteHead "Option-looking remote worktree should detach at its fully qualified tracking ref."

        $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("remote", "add", "team/origin", $remotePath)
        $slashRemote = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "448", "-Slug", "slash-remote", "-BaseBranch", "team/origin/main")
        Assert-Equal 0 $slashRemote.ExitCode "A configured remote containing a slash should be matched as one complete name.`n$($slashRemote.Output)"
        $slashRemoteWorktree = Join-Path $callerPath ".worktrees/codex-448-slash-remote"
        $slashRemoteHead = Invoke-Git -WorkingDirectory $slashRemoteWorktree -Arguments @("rev-parse", "HEAD")
        $slashRemoteTrackingRef = Invoke-Git -WorkingDirectory $callerPath -Arguments @("rev-parse", "refs/remotes/team/origin/main")
        Assert-Equal $freshRemoteBase $slashRemoteHead "Slash-containing remote worktree should detach at the freshly fetched commit."
        Assert-Equal $freshRemoteBase $slashRemoteTrackingRef "Slash-containing remote should refresh its fully qualified tracking ref."
        Complete-Test "complete remote names are option-delimited, refreshed, and resolved without local-ref shadowing"
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
        (Test-CaseSelected "headless-permission-contract") -or
        (Test-CaseSelected "guard-then-branch")
    if ($requiresCreatedWorktree) {
        $success = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "424", "-Slug", "dirty-source")
        Assert-Equal 0 $success.ExitCode "Dirty source checkout should not block detached worktree creation.`n$($success.Output)"
        $createdWorktree = Join-Path $callerPath ".worktrees/codex-424-dirty-source"
        Assert-True (Test-Path -LiteralPath $createdWorktree -PathType Container) "Expected worktree was not created."

        if (Test-CaseSelected "success-detached") {
            $symbolicHead = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("symbolic-ref", "-q", "HEAD") -WorkingDirectory $createdWorktree
            Assert-Equal 1 $symbolicHead.ExitCode "New worktree HEAD should be detached."
            $expectedHead = Invoke-Git -WorkingDirectory $callerPath -Arguments @("rev-parse", "refs/remotes/origin/main")
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
            Assert-True ($createdWorktree.Contains("'")) "Exact-command quoting fixture should place the target beneath an apostrophe-containing path."
            $escapedGitExecutable = $gitExecutable.Replace("'", "''")
            $escapedWorktree = $createdWorktree.Replace("'", "''")
            $escapedInitializer = (Join-Path $createdWorktree "scripts/git/Initialize-CodexIssueWorktree.ps1").Replace("'", "''")
            $expectedHandoffHead = Invoke-Git -WorkingDirectory $createdWorktree -Arguments @("rev-parse", "HEAD")
            $initializerLine = "& '$escapedInitializer' -GitExecutable '$escapedGitExecutable' -BranchName 'issue-424/dirty-source' -ExpectedWorktree '$escapedWorktree' -ExpectedHead '$expectedHandoffHead'"
            $initializerAllowRule = "PowerShell($initializerLine)"
            $handoffCaptureLine = '$handoffSucceeded = $?; $handoffExitCode = $LASTEXITCODE'
            $handoffExitLine = 'if (-not $handoffSucceeded -or $handoffExitCode -ne 0) { if ($null -ne $handoffExitCode -and $handoffExitCode -ne 0) { exit $handoffExitCode }; exit 1 }'
            $handoffLines = @(Get-PrintedHandoffLines -Output $success.Output)
            Assert-Equal $initializerLine $handoffLines[0] "Handoff output omitted the stable initializer command and exact worktree binding."
            Assert-Equal $handoffCaptureLine $handoffLines[1] "Handoff output omitted the initializer status capture."
            Assert-Equal $handoffExitLine $handoffLines[2] "Handoff output omitted the initializer fail-fast gate."
            Assert-Equal $initializerAllowRule (Get-PrintedInitializerLaunchRule -Output $success.Output) "Helper output omitted the exact target-scoped initializer allow rule."
            Assert-True ($initializerAllowRule.Contains("''")) "Exact full-command rule should PowerShell-escape the apostrophe-containing target path."
            Assert-True (-not $initializerAllowRule.Contains(":*)")) "Exact full-command initializer rule must not retain a wildcard suffix."

            $claudeSettings = Get-Content -Raw -LiteralPath $claudeSettingsPath | ConvertFrom-Json
            Assert-True ($claudeSettings.permissions.allow -notcontains "PowerShell(& 'scripts/git/Initialize-CodexIssueWorktree.ps1':*)") "Claude permissions retained the cross-worktree relative initializer rule."
            Assert-True ($claudeSettings.permissions.allow -notcontains $initializerAllowRule) "Claude project settings should not commit a task-specific absolute initializer rule."
            Assert-True ($claudeSettings.permissions.allow -notcontains "Bash(powershell -NoLogo -NoProfile -NonInteractive -File scripts/git/Initialize-CodexIssueWorktree.ps1:*)") "Claude permissions retained the PATH-resolved PowerShell initializer rule."
            Complete-Test "handoff emits one additive exact-target initializer rule and a fail-fast gate"
        }

        if (Test-CaseSelected "headless-permission-contract") {
            $escapedInitializer = (Join-Path $createdWorktree "scripts/git/Initialize-CodexIssueWorktree.ps1").Replace("'", "''")
            $initializerInvocationPrefix = "& '$escapedInitializer'"
            $headlessHandoffLines = @(Get-PrintedHandoffLines -Output $success.Output)
            Assert-True ($headlessHandoffLines[0].StartsWith($initializerInvocationPrefix, [System.StringComparison]::Ordinal)) "Printed handoff must use the exact helper-created target initializer."

            $claudeSettings = Get-Content -Raw -LiteralPath $claudeSettingsPath | ConvertFrom-Json
            $powerShellInitializerRule = "PowerShell($($headlessHandoffLines[0]))"
            Assert-Equal $powerShellInitializerRule (Get-PrintedInitializerLaunchRule -Output $success.Output) "Helper did not print the task-scoped initializer rule used by the handoff."
            Assert-True ($claudeSettings.permissions.allow -notcontains $powerShellInitializerRule) "Claude project settings should not commit a task-specific initializer rule."
            Assert-True ($claudeSettings.permissions.allow -notcontains "PowerShell(& 'scripts/git/Initialize-CodexIssueWorktree.ps1':*)") "Claude project settings retained the generic relative initializer rule."

            $mainCheckoutClaudeDirectory = Join-Path $callerPath ".claude"
            $mainCheckoutLocalSettingsPath = Join-Path $mainCheckoutClaudeDirectory "settings.local.json"
            $linkedWorktreeLocalSettingsPath = Join-Path $createdWorktree ".claude/settings.local.json"
            $broadLocalRule = "PowerShell(*)"
            New-Item -ItemType Directory -Path $mainCheckoutClaudeDirectory | Out-Null
            [ordered]@{
                permissions = [ordered]@{
                    defaultMode = "bypassPermissions"
                    allow = @($broadLocalRule)
                }
            } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $mainCheckoutLocalSettingsPath -Encoding Ascii
            Assert-True (Test-Path -LiteralPath $mainCheckoutLocalSettingsPath -PathType Leaf) "Permission fixture omitted the main-checkout local settings file."
            $localSettingsIgnore = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("check-ignore", "--quiet", ".claude/settings.local.json") -WorkingDirectory $callerPath
            Assert-Equal 0 $localSettingsIgnore.ExitCode "Permission fixture should model a gitignored main-checkout local settings file."
            Assert-True (-not (Test-Path -LiteralPath $linkedWorktreeLocalSettingsPath)) "Permission fixture should not copy the local settings file into the linked worktree."

            $inheritedConfiguration = Get-ModeledEffectivePermissionConfiguration -SettingSources @("project", "local") -ProjectSettingsPath $claudeSettingsPath -MainCheckoutLocalSettingsPath $mainCheckoutLocalSettingsPath
            Assert-Equal "bypassPermissions" $inheritedConfiguration.PermissionMode "The main-checkout local default mode should override the committed project default when local settings are enabled."
            Assert-True ($inheritedConfiguration.Allow -contains $broadLocalRule) "A main-checkout local allow should remain effective for a linked worktree when the local source is enabled."

            $dontAskWithLocalConfiguration = Get-ModeledEffectivePermissionConfiguration -SettingSources @("project", "local") -ProjectSettingsPath $claudeSettingsPath -MainCheckoutLocalSettingsPath $mainCheckoutLocalSettingsPath -CommandLinePermissionMode "dontAsk"
            Assert-Equal "dontAsk" $dontAskWithLocalConfiguration.PermissionMode "The command-line dontAsk mode should override a local bypassPermissions default."
            Assert-True ($dontAskWithLocalConfiguration.Allow -contains $broadLocalRule) "Command-line dontAsk should not erase a broad local allow while the local source remains enabled."

            $taskLaunchRule = "PowerShell(dotnet test backend/Taskdeck.sln -c Release -m:1)"
            $reviewedConfiguration = Get-ModeledEffectivePermissionConfiguration -SettingSources @("project") -ProjectSettingsPath $claudeSettingsPath -MainCheckoutLocalSettingsPath $mainCheckoutLocalSettingsPath -CommandLineAllowRules @($powerShellInitializerRule, $taskLaunchRule) -CommandLinePermissionMode "dontAsk"
            Assert-Equal "dontAsk" $reviewedConfiguration.PermissionMode "The supported project-only posture should use the explicit dontAsk mode."
            Assert-True ($reviewedConfiguration.Allow -notcontains $broadLocalRule) "The supported project-only source posture should exclude the main-checkout local allow."
            Assert-True ($reviewedConfiguration.Allow -contains $powerShellInitializerRule) "The reviewed effective permissions should include the explicit additive task initializer rule."
            $alteredInitializerRule = $powerShellInitializerRule.Replace("issue-424/dirty-source", "issue-424/other-branch")
            Assert-True ($reviewedConfiguration.Allow -notcontains $alteredInitializerRule) "The exact initializer rule must not authorize substituted branch arguments."
            Assert-True ($reviewedConfiguration.Allow -contains $taskLaunchRule) "The reviewed effective permissions should include explicit task launch rules."
            Assert-Equal "1" $reviewedConfiguration.Environment["CLAUDE_CODE_USE_POWERSHELL_TOOL"] "Project settings should enable Claude Code's progressive Windows PowerShell tool surface."
            $committedProjectGuardRule = "PowerShell(powershell -File scripts/worktree_guard.ps1:*)"
            Assert-True ($reviewedConfiguration.Allow -contains $committedProjectGuardRule) "A trusted project source should include committed project allow rules."

            $untrustedConfiguration = Get-ModeledEffectivePermissionConfiguration -SettingSources @("project") -ProjectSettingsPath $claudeSettingsPath -MainCheckoutLocalSettingsPath $mainCheckoutLocalSettingsPath -CommandLineAllowRules @($powerShellInitializerRule, $taskLaunchRule) -CommandLinePermissionMode "dontAsk" -WorkspaceTrusted $false
            Assert-True ($untrustedConfiguration.Allow -notcontains $committedProjectGuardRule) "An untrusted workspace should ignore committed project allow rules."
            Assert-True ($untrustedConfiguration.Allow -contains $powerShellInitializerRule) "An untrusted workspace should retain the exact initializer rule supplied through CLI argv."
            Assert-True ($untrustedConfiguration.Allow -contains $taskLaunchRule) "An untrusted workspace should retain other explicitly supplied CLI rules."
            Assert-True (-not $untrustedConfiguration.Environment.ContainsKey("CLAUDE_CODE_USE_POWERSHELL_TOOL")) "An untrusted workspace should not rely on the project environment for PowerShell tool enablement."

            $projectTrustFixturePath = Join-Path $fixtureRoot "project-trust-settings.json"
            $projectTrustFixture = Get-Content -Raw -LiteralPath $claudeSettingsPath | ConvertFrom-Json
            $projectTrustFixture | Add-Member -NotePropertyName additionalDirectories -NotePropertyValue @("../reviewed-shared") -Force
            $projectTrustFixture | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $projectTrustFixturePath -Encoding Ascii
            $trustedAdditionalDirectoryConfiguration = Get-ModeledEffectivePermissionConfiguration -SettingSources @("project") -ProjectSettingsPath $projectTrustFixturePath -MainCheckoutLocalSettingsPath $mainCheckoutLocalSettingsPath
            $untrustedAdditionalDirectoryConfiguration = Get-ModeledEffectivePermissionConfiguration -SettingSources @("project") -ProjectSettingsPath $projectTrustFixturePath -MainCheckoutLocalSettingsPath $mainCheckoutLocalSettingsPath -WorkspaceTrusted $false
            Assert-True ($trustedAdditionalDirectoryConfiguration.AdditionalDirectories -contains "../reviewed-shared") "A trusted project source should include reviewed additional directories."
            Assert-True ($untrustedAdditionalDirectoryConfiguration.AdditionalDirectories -notcontains "../reviewed-shared") "An untrusted workspace should ignore project additional directories."

            foreach ($guidancePath in $worktreeGuidancePaths) {
                $guidance = Get-Content -Raw -LiteralPath $guidancePath
                Assert-Contains $guidance "Initialize-CodexIssueWorktree.ps1" "Detached-first guidance omitted the reviewed initializer wrapper: $guidancePath"
            }
            $protocol = Get-Content -Raw -LiteralPath $worktreeProtocolPath
            Assert-Contains $protocol 'claude -p --worktree --setting-sources project --allowedTools $initializerAllowRule' "Headless guidance omitted the project-only source posture and quote-safe task initializer launch rule."
            Assert-NormalizedContains $protocol "acceptEdits does not approve arbitrary Git or PowerShell commands" "Headless guidance must not present acceptEdits as sufficient command authorization."
            Assert-NormalizedContains $protocol "Do not present the launch allowlist as the sole authorization boundary" "Headless guidance must describe the complete effective permission boundary."
            Assert-NormalizedContains $protocol "Organization-managed settings remain effective" "Headless guidance must bound residual administrator-owned trust."
            Assert-NormalizedContains $protocol "built-in read-only Bash commands, or applicable hook approvals" "Headless guidance must retain documented non-allowlist authorization paths."
            Assert-NormalizedContains $protocol 'overrides a file-backed `defaultMode` for that session' "Headless guidance must distinguish the command-line mode override from merged allow rules."
            Assert-Contains $protocol "CLAUDE_CODE_USE_POWERSHELL_TOOL" "Headless guidance omitted the committed PowerShell-tool enablement."
            Assert-NormalizedContains $protocol "does not make an untrusted workspace trusted" "Headless guidance must not treat -p --worktree as accepted project trust."
            Assert-NormalizedContains $protocol "Pass every required allow rule through CLI argv" "Headless guidance omitted the untrusted-workspace CLI-only posture."
            Assert-Contains $protocol '$initializerAllowRule = @''' "Headless guidance omitted quote-safe allow-rule transport."
            Assert-Contains $protocol "--permission-mode dontAsk" "Headless guidance omitted the non-prompting permission mode for reviewed effective permissions."
            Assert-Contains $protocol '$coordinatorBranchBaseline' "Post-run guidance omitted the pre-creation coordinator branch baseline."
            Assert-Contains $protocol '$coordinatorStatusBaseline' "Post-run guidance omitted the pre-creation coordinator status baseline."
            Complete-Test "headless posture excludes inherited local allows and reviews the complete effective permission set"
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
            Assert-NormalizedContains $missingGuard.Output "no argv-safe Git executable was found" "Missing Git should retain the guard setup diagnostic."
            Assert-True (-not (($missingGuard.Output -replace '\s+', ' ').Contains("not inside a git repository"))) "Missing Git must not be mislabeled as an ordinary non-repository result."
            $outsideRepoGuard = Invoke-ProcessCapture -FilePath $powerShellExecutable -Arguments @("-NoLogo", "-NoProfile", "-NonInteractive", "-File", (Join-Path $createdWorktree "scripts/worktree_guard.ps1"), "-GitExecutable", $gitExecutable) -WorkingDirectory $fixtureRoot
            Assert-Equal 2 $outsideRepoGuard.ExitCode "Guard should fail with its advertised repository-check exit code outside Git."
            Assert-NormalizedContains $outsideRepoGuard.Output "not inside a git repository" "Guard should distinguish an ordinary non-repository result from executable launch failure."
            $createdBranch = Invoke-Git -WorkingDirectory $createdWorktree -Arguments @("branch", "--show-current")
            Assert-Equal "issue-424/dirty-source" $createdBranch "Post-guard branch command created the wrong branch."

            $metacharBranch = "issue-449/powershell&safe"
            $powerShellHostResult = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "449", "-Slug", "powershell-host", "-BranchName", $metacharBranch)
            Assert-Equal 0 $powerShellHostResult.ExitCode "PowerShell-host fixture worktree creation should succeed.`n$($powerShellHostResult.Output)"
            $powerShellHostWorktree = Join-Path $callerPath ".worktrees/codex-449-powershell-host"
            $powerShellHostScript = Join-Path $fixtureRoot "powershell-host-handoff.ps1"
            Set-Content -LiteralPath $powerShellHostScript -Value @(Get-PrintedHandoffLines -Output $powerShellHostResult.Output) -Encoding Ascii
            $powerShellShimSentinel = Join-Path $shimDirectory "powershell-shim-invoked.txt"
            $powerShellShimCanary = Join-Path $powerShellHostWorktree "powershell-host-canary.txt"
            Set-Content -LiteralPath (Join-Path $shimDirectory "powershell.cmd") -Encoding Ascii -Value @(
                '@echo off',
                'echo invoked>"%~dp0powershell-shim-invoked.txt"',
                'echo TASKDECK_CANARY>powershell-host-canary.txt',
                'exit /b 99'
            )
            $previousPath = $env:PATH
            try {
                $env:PATH = "$shimDirectory$([System.IO.Path]::PathSeparator)$previousPath"
                $powerShellHostHandoff = Invoke-ProcessCapture -FilePath $powerShellExecutable -Arguments @("-NoLogo", "-NoProfile", "-NonInteractive", "-File", $powerShellHostScript) -WorkingDirectory $powerShellHostWorktree
            }
            finally {
                $env:PATH = $previousPath
            }
            Assert-Equal 0 $powerShellHostHandoff.ExitCode "In-process handoff should create a metacharacter branch without resolving PowerShell through PATH.`n$($powerShellHostHandoff.Output)"
            Assert-True (-not (Test-Path -LiteralPath $powerShellShimSentinel)) "Printed handoff executed the PATH-first PowerShell batch shim."
            Assert-True (-not (Test-Path -LiteralPath $powerShellShimCanary)) "PowerShell shim or branch metacharacters executed the canary."
            $metacharCreatedBranch = Invoke-Git -WorkingDirectory $powerShellHostWorktree -Arguments @("branch", "--show-current")
            Assert-Equal $metacharBranch $metacharCreatedBranch "Initializer did not create the intended metacharacter branch through native argv."

            $quotedBranch = 'issue-469/quoted"rule'
            $quotedRuleResult = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "469", "-Slug", "quoted-rule", "-BranchName", $quotedBranch)
            Assert-Equal 0 $quotedRuleResult.ExitCode "Git-valid quote-bearing branch should survive helper validation and detached creation.`n$($quotedRuleResult.Output)"
            $quotedInitializerRule = Get-PrintedInitializerLaunchRule -Output $quotedRuleResult.Output
            Assert-True $quotedInitializerRule.Contains('"') "Quote-bearing custom branch was lost from the exact initializer allow rule."
            $quotedHandoffLines = @(Get-PrintedHandoffLines -Output $quotedRuleResult.Output)
            Assert-Equal "PowerShell($($quotedHandoffLines[0]))" $quotedInitializerRule "Here-string transport changed the exact quote-bearing initializer rule."
            $quotedOutputLines = @($quotedRuleResult.Output -split '\r?\n')
            $quotedRuleMarkerIndex = [Array]::IndexOf($quotedOutputLines, "Claude Code task-scoped initializer allow rule (additive PowerShell transport):")
            $quotedRuleCapturePath = Join-Path $fixtureRoot "quoted-rule-argv.txt"
            $escapedQuotedRuleCapturePath = $quotedRuleCapturePath.Replace("'", "''")
            $quotedRuleTransportScript = Join-Path $fixtureRoot "quoted-rule-transport.ps1"
            Set-Content -LiteralPath $quotedRuleTransportScript -Encoding Ascii -Value @(
                $quotedOutputLines[$quotedRuleMarkerIndex + 1].TrimStart(),
                $quotedOutputLines[$quotedRuleMarkerIndex + 2].TrimStart(),
                $quotedOutputLines[$quotedRuleMarkerIndex + 3].TrimStart(),
                '$capturedArguments = @("--allowedTools", $initializerAllowRule)',
                'if ($capturedArguments.Count -ne 2) { exit 91 }',
                "[System.IO.File]::WriteAllText('$escapedQuotedRuleCapturePath', `$capturedArguments[1])"
            )
            $quotedRuleTransport = Invoke-ProcessCapture -FilePath $powerShellExecutable -Arguments @("-NoLogo", "-NoProfile", "-NonInteractive", "-File", $quotedRuleTransportScript) -WorkingDirectory $fixtureRoot
            Assert-Equal 0 $quotedRuleTransport.ExitCode "Single-quoted here-string transport should preserve the rule as one argv value.`n$($quotedRuleTransport.Output)"
            Assert-Equal $quotedInitializerRule ([System.IO.File]::ReadAllText($quotedRuleCapturePath)) "Single-argv transport changed the quote-bearing initializer rule."
            Complete-Test "printed handoff stays in the trusted PowerShell host and uses pinned Git under a shimmed PATH"
        }
    }

    if (Test-CaseSelected "handoff-fail-fast") {
        $failFast = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "441", "-Slug", "fail-fast")
        Assert-Equal 0 $failFast.ExitCode "Fail-fast fixture worktree creation should succeed.`n$($failFast.Output)"
        $failFastWorktree = Join-Path $callerPath ".worktrees/codex-441-fail-fast"
        $failFastScript = Join-Path $fixtureRoot "failing-handoff.ps1"
        Set-Content -LiteralPath $failFastScript -Value @(Get-PrintedHandoffLines -Output $failFast.Output) -Encoding Ascii
        $handoffFailure = Invoke-ProcessCapture -FilePath $powerShellExecutable -Arguments @("-NoLogo", "-NoProfile", "-NonInteractive", "-File", $failFastScript) -WorkingDirectory $callerPath
        Assert-Equal 1 $handoffFailure.ExitCode "Guard failure in the coordinator checkout should preserve its exit code."
        $unexpectedBranch = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/issue-441/fail-fast") -WorkingDirectory $callerPath
        Assert-Equal 1 $unexpectedBranch.ExitCode "Guard failure must stop before branch creation in the coordinator checkout."

        $misleadingParent = Join-Path $fixtureRoot ".worktrees"
        New-Item -ItemType Directory -Path $misleadingParent -Force | Out-Null
        $misleadingCoordinator = Join-Path $misleadingParent "standalone-coordinator"
        $null = Invoke-Git -WorkingDirectory $fixtureRoot -Arguments @("clone", "-b", "main", $remotePath, $misleadingCoordinator)
        $standaloneGuard = Invoke-ProcessCapture -FilePath $powerShellExecutable -Arguments @(
            "-NoLogo", "-NoProfile", "-NonInteractive", "-File", "scripts/worktree_guard.ps1",
            "-GitExecutable", $gitExecutable
        ) -WorkingDirectory $misleadingCoordinator
        Assert-Equal 1 $standaloneGuard.ExitCode "The guard itself should reject a standalone checkout beneath a .worktrees ancestor."
        Assert-NormalizedContains $standaloneGuard.Output "main checkout or an unrecognized worktree" "Standalone-checkout rejection should come from the linked-layout guard diagnostic."
        $wrongTreeInitializerCanary = Join-Path $misleadingCoordinator "wrong-tree-initializer-executed.txt"
        $escapedWrongTreeCanary = $wrongTreeInitializerCanary.Replace("'", "''")
        Set-Content -LiteralPath (Join-Path $misleadingCoordinator "scripts/git/Initialize-CodexIssueWorktree.ps1") -Encoding Ascii -Value @(
            "[System.IO.File]::WriteAllText('$escapedWrongTreeCanary', 'EXECUTED')",
            "exit 99"
        )
        $misleadingFailure = Invoke-ProcessCapture -FilePath $powerShellExecutable -Arguments @("-NoLogo", "-NoProfile", "-NonInteractive", "-File", $failFastScript) -WorkingDirectory $misleadingCoordinator
        Assert-Equal 1 $misleadingFailure.ExitCode "A standalone checkout beneath a .worktrees ancestor must run the target initializer and fail its linked-worktree guard."
        Assert-True (-not (Test-Path -LiteralPath $wrongTreeInitializerCanary)) "Wrong-checkout initializer code ran instead of the exact helper-created target initializer."
        $misleadingBranch = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/issue-441/fail-fast") -WorkingDirectory $misleadingCoordinator
        Assert-Equal 1 $misleadingBranch.ExitCode "A misleading path marker must not permit branch creation in a standalone checkout."

        $otherWorktreeResult = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "443", "-Slug", "other-worktree")
        Assert-Equal 0 $otherWorktreeResult.ExitCode "Different-worktree fixture creation should succeed.`n$($otherWorktreeResult.Output)"
        $otherWorktree = Join-Path $callerPath ".worktrees/codex-443-other-worktree"
        $wrongWorktreeFailure = Invoke-ProcessCapture -FilePath $powerShellExecutable -Arguments @("-NoLogo", "-NoProfile", "-NonInteractive", "-File", $failFastScript) -WorkingDirectory $otherWorktree
        Assert-Equal 1 $wrongWorktreeFailure.ExitCode "A valid but different linked worktree must reject this helper handoff."
        Assert-NormalizedContains $wrongWorktreeFailure.Output "does not match the helper-created worktree" "Wrong-worktree failure should explain the exact binding mismatch."
        $unexpectedSharedBranch = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/issue-441/fail-fast") -WorkingDirectory $callerPath
        Assert-Equal 1 $unexpectedSharedBranch.ExitCode "Wrong-worktree handoff must stop before shared branch creation."
        $failFastSymbolicHead = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("symbolic-ref", "-q", "HEAD") -WorkingDirectory $failFastWorktree
        Assert-Equal 1 $failFastSymbolicHead.ExitCode "The intended worktree should remain detached until its own handoff runs."
        Complete-Test "exact target initializer and guard reject wrong contexts before canary or branch creation"
    }

    if (Test-CaseSelected "handoff-missing-executable") {
        $missingExecutableResult = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "442", "-Slug", "missing-executable")
        Assert-Equal 0 $missingExecutableResult.ExitCode "Missing-executable fixture worktree creation should succeed.`n$($missingExecutableResult.Output)"
        $missingExecutableWorktree = Join-Path $callerPath ".worktrees/codex-442-missing-executable"
        $missingExecutableLines = @(Get-PrintedHandoffLines -Output $missingExecutableResult.Output)
        $escapedGitExecutable = $gitExecutable.Replace("'", "''")
        $missingExecutablePath = (Join-Path $fixtureRoot "removed-git.exe").Replace("'", "''")
        $missingExecutableLines[0] = $missingExecutableLines[0].Replace($escapedGitExecutable, $missingExecutablePath)
        $missingExecutableScript = Join-Path $fixtureRoot "missing-executable-handoff.ps1"
        Set-Content -LiteralPath $missingExecutableScript -Value $missingExecutableLines -Encoding Ascii
        $missingExecutableHandoff = Invoke-ProcessCapture -FilePath $powerShellExecutable -Arguments @("-NoLogo", "-NoProfile", "-NonInteractive", "-File", $missingExecutableScript) -WorkingDirectory $missingExecutableWorktree
        Assert-Equal 2 $missingExecutableHandoff.ExitCode "A disappeared Git executable should preserve the initializer setup-failure exit code."
        Assert-NormalizedContains $missingExecutableHandoff.Output "no argv-safe native Git executable was found" "Missing Git should retain its setup diagnostic."
        Assert-True (-not (($missingExecutableHandoff.Output -replace '\s+', ' ').Contains("not inside a git repository"))) "Missing Git must not be mislabeled as an ordinary non-repository result."
        $missingExecutableBranch = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/issue-442/missing-executable") -WorkingDirectory $callerPath
        Assert-Equal 1 $missingExecutableBranch.ExitCode "A disappeared Git executable must not create the planned branch."

        $missingInitializerLines = @(Get-PrintedHandoffLines -Output $missingExecutableResult.Output)
        $escapedInitializerPath = (Join-Path $missingExecutableWorktree "scripts/git/Initialize-CodexIssueWorktree.ps1").Replace("'", "''")
        $escapedMissingInitializerPath = (Join-Path $missingExecutableWorktree "scripts/git/missing-initializer.ps1").Replace("'", "''")
        $missingInitializerLines[0] = $missingInitializerLines[0].Replace($escapedInitializerPath, $escapedMissingInitializerPath)
        $missingInitializerScript = Join-Path $fixtureRoot "missing-initializer-handoff.ps1"
        Set-Content -LiteralPath $missingInitializerScript -Value @('$global:LASTEXITCODE = $null') -Encoding Ascii
        Add-Content -LiteralPath $missingInitializerScript -Value $missingInitializerLines -Encoding Ascii
        $missingInitializerHandoff = Invoke-ProcessCapture -FilePath $powerShellExecutable -Arguments @("-NoLogo", "-NoProfile", "-NonInteractive", "-File", $missingInitializerScript) -WorkingDirectory $missingExecutableWorktree
        Assert-True ($missingInitializerHandoff.ExitCode -ne 0) "A disappeared initializer script should fail the handoff."
        $missingInitializerBranch = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/issue-442/missing-executable") -WorkingDirectory $callerPath
        Assert-Equal 1 $missingInitializerBranch.ExitCode "A disappeared initializer must not create the planned branch."
        Complete-Test "missing Git or initializer executables fail the printed handoff closed"
    }

    if (Test-CaseSelected "initializer-validation") {
        $initializerResult = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "446", "-Slug", "initializer-validation")
        Assert-Equal 0 $initializerResult.ExitCode "Initializer-validation fixture worktree creation should succeed.`n$($initializerResult.Output)"
        $initializerWorktree = Join-Path $callerPath ".worktrees/codex-446-initializer-validation"
        $initializerHead = Invoke-Git -WorkingDirectory $initializerWorktree -Arguments @("rev-parse", "HEAD")
        $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("branch", "issue-446/initializer-validation", $initializerHead)

        $collisionScript = Join-Path $fixtureRoot "initializer-branch-collision.ps1"
        Set-Content -LiteralPath $collisionScript -Value @(Get-PrintedHandoffLines -Output $initializerResult.Output) -Encoding Ascii
        $collision = Invoke-ProcessCapture -FilePath $powerShellExecutable -Arguments @("-NoLogo", "-NoProfile", "-NonInteractive", "-File", $collisionScript) -WorkingDirectory $initializerWorktree
        Assert-True ($collision.ExitCode -ne 0) "A post-helper branch collision should fail the initializer."
        Assert-NormalizedContains $collision.Output "git switch -c failed" "Branch collision should retain its switch failure diagnostic."
        $detachedAfterCollision = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("symbolic-ref", "-q", "HEAD") -WorkingDirectory $initializerWorktree
        Assert-Equal 1 $detachedAfterCollision.ExitCode "Branch collision should leave the helper-created worktree detached."

        $mismatchedExpectedHead = Invoke-Git -WorkingDirectory $callerPath -Arguments @("rev-parse", "$initializerHead^")
        $baseMismatchBranch = "issue-446/base-mismatch"
        $baseMismatch = Invoke-ProcessCapture -FilePath $powerShellExecutable -Arguments @(
            "-NoLogo", "-NoProfile", "-NonInteractive", "-File", "scripts/git/Initialize-CodexIssueWorktree.ps1",
            "-GitExecutable", $gitExecutable,
            "-BranchName", $baseMismatchBranch,
            "-ExpectedWorktree", $initializerWorktree,
            "-ExpectedHead", $mismatchedExpectedHead
        ) -WorkingDirectory $initializerWorktree
        Assert-Equal 1 $baseMismatch.ExitCode "Initializer should reject a detached HEAD that differs from the helper-selected base."
        Assert-NormalizedContains $baseMismatch.Output "detached HEAD '$initializerHead' does not match the helper-created base '$mismatchedExpectedHead'" "Detached-base mismatch should retain the exact-base diagnostic."
        $baseMismatchRef = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/$baseMismatchBranch") -WorkingDirectory $callerPath
        Assert-Equal 1 $baseMismatchRef.ExitCode "Detached-base mismatch must stop before branch creation."
        $headAfterBaseMismatch = Invoke-Git -WorkingDirectory $initializerWorktree -Arguments @("rev-parse", "HEAD")
        Assert-Equal $initializerHead $headAfterBaseMismatch "Detached-base mismatch should preserve the original detached HEAD OID."
        $detachedAfterBaseMismatch = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("symbolic-ref", "-q", "HEAD") -WorkingDirectory $initializerWorktree
        Assert-Equal 1 $detachedAfterBaseMismatch.ExitCode "Detached-base mismatch should leave the helper-created worktree detached."

        $canaryPath = Join-Path $initializerWorktree "initializer-canary.txt"
        $invalidInitializer = Invoke-ProcessCapture -FilePath $powerShellExecutable -Arguments @(
            "-NoLogo", "-NoProfile", "-NonInteractive", "-File", "scripts/git/Initialize-CodexIssueWorktree.ps1",
            "-GitExecutable", $gitExecutable,
            "-BranchName", "bad&echo CANARY>initializer-canary.txt",
            "-ExpectedWorktree", $initializerWorktree,
            "-ExpectedHead", $initializerHead
        ) -WorkingDirectory $initializerWorktree
        Assert-Equal 2 $invalidInitializer.ExitCode "Initializer should reject an invalid branch before switch."
        Assert-True (-not (Test-Path -LiteralPath $canaryPath)) "Initializer branch metacharacters escaped the native argv boundary."

        $wrongExecutable = Invoke-ProcessCapture -FilePath $powerShellExecutable -Arguments @(
            "-NoLogo", "-NoProfile", "-NonInteractive", "-File", "scripts/git/Initialize-CodexIssueWorktree.ps1",
            "-GitExecutable", $powerShellExecutable,
            "-BranchName", "issue-446/wrong-executable",
            "-ExpectedWorktree", $initializerWorktree,
            "-ExpectedHead", $initializerHead
        ) -WorkingDirectory $initializerWorktree
        Assert-Equal 2 $wrongExecutable.ExitCode "Initializer should reject a non-Git executable before guard or switch."

        $null = Invoke-Git -WorkingDirectory $initializerWorktree -Arguments @("switch", "-c", "manual-attached")
        $attachedInitializer = Invoke-ProcessCapture -FilePath $powerShellExecutable -Arguments @(
            "-NoLogo", "-NoProfile", "-NonInteractive", "-File", "scripts/git/Initialize-CodexIssueWorktree.ps1",
            "-GitExecutable", $gitExecutable,
            "-BranchName", "issue-446/after-attached",
            "-ExpectedWorktree", $initializerWorktree,
            "-ExpectedHead", $initializerHead
        ) -WorkingDirectory $initializerWorktree
        Assert-Equal 1 $attachedInitializer.ExitCode "Initializer should reject an already-attached helper worktree."
        Assert-Equal "manual-attached" (Invoke-Git -WorkingDirectory $initializerWorktree -Arguments @("branch", "--show-current")) "Attached-worktree rejection should preserve the current branch."
        Complete-Test "initializer fails closed on collisions, detached-base mismatch, invalid input, wrong executables, and attached HEAD"
    }

    if (Test-CaseSelected "existing-branch") {
        $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("branch", "issue-425/existing", "refs/remotes/origin/main")
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

    if (Test-CaseSelected "worktree-root-case-variant") {
        $registrationsBefore = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
        $caseVariantTarget = Join-Path $callerPath ".WORKTREES/codex-450-case-variant-root"
        $caseVariant = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "450", "-Slug", "case-variant-root", "-WorktreeRoot", ".WORKTREES")
        Assert-True ($caseVariant.ExitCode -ne 0) "Case-variant worktree root should fail closed."
        Assert-Contains $caseVariant.Output "Invalid worktree root: '.WORKTREES'." "Case-variant root diagnostic was not clear."
        Assert-True (-not (Test-Path -LiteralPath $caseVariantTarget)) "Case-variant root created a target."
        $registrationsAfter = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
        Assert-Equal $registrationsBefore $registrationsAfter "Case-variant root changed Git worktree registrations."
        Complete-Test "case-variant worktree root fails closed before handoff"
    }

    if (Test-CaseSelected "invalid-slug") {
        $registrationsBefore = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
        $invalidSlugTarget = Join-Path $callerPath ".worktrees/codex-427-Invalid-Slug"
        $invalidSlug = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "427", "-Slug", "Invalid-Slug")
        Assert-True ($invalidSlug.ExitCode -ne 0) "Invalid slug should fail closed."
        Assert-Contains $invalidSlug.Output "Invalid slug: 'Invalid-Slug'." "Invalid slug diagnostic was not clear."
        Assert-True (-not (Test-Path -LiteralPath $invalidSlugTarget)) "Invalid slug should not create a target path."

        $newlineSlug = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "447", "-Slug", "valid-slug`n")
        Assert-True ($newlineSlug.ExitCode -ne 0) "A slug ending in a line feed should fail closed."
        Assert-Contains $newlineSlug.Output "Invalid slug:" "Final-line-feed slug diagnostic was not clear."
        $newlineSlugTargets = @(
            Get-ChildItem -LiteralPath (Join-Path $callerPath ".worktrees") -Force |
                Where-Object { $_.Name.StartsWith("codex-447-", [System.StringComparison]::Ordinal) }
        )
        Assert-Equal 0 $newlineSlugTargets.Count "Final-line-feed slug should not create a target path."
        $registrationsAfter = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
        Assert-Equal $registrationsBefore $registrationsAfter "Invalid slug changed Git worktree registrations."
        Complete-Test "uppercase and final-line-feed slugs fail closed without target or registration mutation"
    }

    if (Test-CaseSelected "invalid-branch") {
        $registrationsBefore = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
        $invalidBranchTarget = Join-Path $callerPath ".worktrees/codex-428-invalid-branch"
        $invalidBranch = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "428", "-Slug", "invalid-branch", "-BranchName", "invalid branch")
        Assert-True ($invalidBranch.ExitCode -ne 0) "Invalid branch should fail closed."
        Assert-Contains $invalidBranch.Output "Invalid branch name: invalid branch" "Invalid branch diagnostic was not clear."
        Assert-True (-not (Test-Path -LiteralPath $invalidBranchTarget)) "Invalid branch should not create a target path."
        $registrationsAfter = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
        Assert-Equal $registrationsBefore $registrationsAfter "Invalid branch changed Git worktree registrations."
        Complete-Test "invalid branch fails closed without target or registration mutation"
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

    if (Test-CaseSelected "base-missing-handoff-artifacts") {
        $registrationsBeforeMissingArtifacts = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
        $linkedSourcePath = Join-Path $fixtureRoot "linked source checkout"
        $linkedSourceTrackingBefore = Invoke-Git -WorkingDirectory $callerPath -Arguments @("rev-parse", "refs/remotes/origin/main")
        $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "add", "--detach", $linkedSourcePath, $fixtureBase)
        try {
            $linkedRegistrationsBefore = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
            $linkedSourceTarget = Join-Path $linkedSourcePath ".worktrees/codex-468-linked-source"
            $linkedSource = Invoke-Helper -WorkingDirectory $linkedSourcePath -Arguments @("-IssueNumber", "468", "-Slug", "linked-source")
            Assert-True ($linkedSource.ExitCode -ne 0) "A helper invoked from a linked source worktree should fail closed."
            Assert-Contains $linkedSource.Output "Run this helper from the repository's main checkout; linked source worktrees are not allowed" "Linked-source rejection should state the main-checkout-only contract."
            Assert-True (-not (Test-Path -LiteralPath $linkedSourceTarget)) "Linked-source rejection should not leave a nested target path."
            $linkedSourceBranch = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/issue-468/linked-source") -WorkingDirectory $callerPath
            Assert-Equal 1 $linkedSourceBranch.ExitCode "Linked-source rejection should not create the planned branch."
            $linkedRegistrationsAfter = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
            Assert-Equal $linkedRegistrationsBefore $linkedRegistrationsAfter "Linked-source rejection changed Git worktree registrations."
            $linkedSourceTrackingAfter = Invoke-Git -WorkingDirectory $callerPath -Arguments @("rev-parse", "refs/remotes/origin/main")
            Assert-Equal $linkedSourceTrackingBefore $linkedSourceTrackingAfter "Linked-source rejection changed the remote-tracking ref."
        }
        finally {
            $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "remove", "--force", $linkedSourcePath)
        }
        $wrongHelperTarget = Join-Path $callerPath ".worktrees/codex-461-wrong-helper-checkout"
        $wrongHelperPath = Invoke-ProcessCapture -FilePath $powerShellExecutable -Arguments @(
            "-NoLogo", "-NoProfile", "-NonInteractive", "-File", $helperPath,
            "-IssueNumber", "461", "-Slug", "wrong-helper-checkout"
        ) -WorkingDirectory $callerPath
        Assert-True ($wrongHelperPath.ExitCode -ne 0) "A helper invoked from outside the caller repository should fail closed."
        Assert-Contains $wrongHelperPath.Output "does not match the current repository's reviewed helper" "Wrong-checkout helper rejection should identify the exact path binding."
        Assert-True (-not (Test-Path -LiteralPath $wrongHelperTarget)) "Wrong-checkout helper rejection should not leave a target path."
        $wrongHelperBranch = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/issue-461/wrong-helper-checkout") -WorkingDirectory $callerPath
        Assert-Equal 1 $wrongHelperBranch.ExitCode "Wrong-checkout helper rejection should not create the planned branch."

        $oldCommitTarget = Join-Path $callerPath ".worktrees/codex-453-old-commit-base"
        $oldCommitBase = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "453", "-Slug", "old-commit-base", "-BaseBranch", $preHelperCommit)
        Assert-True ($oldCommitBase.ExitCode -ne 0) "A commit predating the handoff artifacts should fail closed."
        Assert-Contains $oldCommitBase.Output "does not contain required handoff artifact 'scripts/worktree_guard.ps1'" "Old-commit rejection should name the missing handoff artifact."
        Assert-True (-not (Test-Path -LiteralPath $oldCommitTarget)) "Rejected old commit should not leave a target path."
        $oldCommitBranch = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/issue-453/old-commit-base") -WorkingDirectory $callerPath
        Assert-Equal 1 $oldCommitBranch.ExitCode "Rejected old commit should not create the planned branch."

        $oldTagTarget = Join-Path $callerPath ".worktrees/codex-454-old-tag-base"
        $oldTagBase = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "454", "-Slug", "old-tag-base", "-BaseBranch", "pre-helper-base")
        Assert-True ($oldTagBase.ExitCode -ne 0) "A tag predating the handoff artifacts should fail closed."
        Assert-Contains $oldTagBase.Output "does not contain required handoff artifact 'scripts/worktree_guard.ps1'" "Old-tag rejection should name the missing handoff artifact."
        Assert-True (-not (Test-Path -LiteralPath $oldTagTarget)) "Rejected old tag should not leave a target path."
        $oldTagBranch = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/issue-454/old-tag-base") -WorkingDirectory $callerPath
        Assert-Equal 1 $oldTagBranch.ExitCode "Rejected old tag should not create the planned branch."

        $missingInitializerTarget = Join-Path $callerPath ".worktrees/codex-456-missing-initializer-base"
        $missingInitializerBase = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "456", "-Slug", "missing-initializer-base", "-BaseBranch", $guardOnlyCommit)
        Assert-True ($missingInitializerBase.ExitCode -ne 0) "A base containing the guard but predating the initializer should fail closed."
        Assert-Contains $missingInitializerBase.Output "does not contain required handoff artifact 'scripts/git/Initialize-CodexIssueWorktree.ps1'" "Guard-only base rejection should name the missing initializer artifact."
        Assert-True (-not (Test-Path -LiteralPath $missingInitializerTarget)) "Rejected guard-only base should not leave a target path."
        $missingInitializerBranch = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/issue-456/missing-initializer-base") -WorkingDirectory $callerPath
        Assert-Equal 1 $missingInitializerBranch.ExitCode "Rejected guard-only base should not create the planned branch."

        $modifiedArtifactTarget = Join-Path $callerPath ".worktrees/codex-457-modified-artifact-base"
        $modifiedArtifactBase = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "457", "-Slug", "modified-artifact-base", "-BaseBranch", $modifiedHandoffCommit)
        Assert-True ($modifiedArtifactBase.ExitCode -ne 0) "A base containing a different initializer blob should fail closed."
        Assert-Contains $modifiedArtifactBase.Output "handoff artifact 'scripts/git/Initialize-CodexIssueWorktree.ps1' does not match the reviewed artifact in the invoking checkout HEAD" "Modified-artifact rejection should name the initializer trust mismatch."
        Assert-True (-not (Test-Path -LiteralPath $modifiedArtifactTarget)) "Rejected modified-artifact base should not leave a target path."
        $modifiedArtifactBranch = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/issue-457/modified-artifact-base") -WorkingDirectory $callerPath
        Assert-Equal 1 $modifiedArtifactBranch.ExitCode "Rejected modified-artifact base should not create the planned branch."

        $modifiedGuardTarget = Join-Path $callerPath ".worktrees/codex-464-modified-guard-base"
        $modifiedGuardBase = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "464", "-Slug", "modified-guard-base", "-BaseBranch", $modifiedGuardCommit)
        Assert-True ($modifiedGuardBase.ExitCode -ne 0) "A base containing a different guard blob should fail closed."
        Assert-Contains $modifiedGuardBase.Output "handoff artifact 'scripts/worktree_guard.ps1' does not match the reviewed artifact in the invoking checkout HEAD" "Modified-guard rejection should name the guard trust mismatch."
        Assert-True (-not (Test-Path -LiteralPath $modifiedGuardCanary)) "Rejected selected-base guard code executed despite its reviewed-blob mismatch."
        Assert-True (-not (Test-Path -LiteralPath $modifiedGuardTarget)) "Rejected modified-guard base should not leave a target path."
        $modifiedGuardBranch = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/issue-464/modified-guard-base") -WorkingDirectory $callerPath
        Assert-Equal 1 $modifiedGuardBranch.ExitCode "Rejected modified-guard base should not create the planned branch."

        $callerInitializerPath = Join-Path $callerPath "scripts/git/Initialize-CodexIssueWorktree.ps1"
        $originalInitializerBytes = [System.IO.File]::ReadAllBytes($callerInitializerPath)
        $dirtySourceMarker = "# Uncommitted source-artifact canary."
        try {
            Add-Content -LiteralPath $callerInitializerPath -Value $dirtySourceMarker -Encoding Ascii
            $dirtySourceTarget = Join-Path $callerPath ".worktrees/codex-458-dirty-source-artifact"
            $dirtySource = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "458", "-Slug", "dirty-source-artifact")
            Assert-True ($dirtySource.ExitCode -ne 0) "A dirty invoking-checkout initializer should fail closed."
            Assert-Contains $dirtySource.Output "Reviewed handoff artifact 'scripts/git/Initialize-CodexIssueWorktree.ps1' working content does not match the invoking checkout HEAD blob" "Dirty source-artifact rejection should identify the uncommitted initializer."
            Assert-Contains (Get-Content -Raw -LiteralPath $callerInitializerPath) $dirtySourceMarker "Source-artifact rejection should preserve the maintainer-owned dirty content."
            Assert-True (-not (Test-Path -LiteralPath $dirtySourceTarget)) "Dirty source-artifact rejection should not leave a target path."
            $dirtySourceBranch = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/issue-458/dirty-source-artifact") -WorkingDirectory $callerPath
            Assert-Equal 1 $dirtySourceBranch.ExitCode "Dirty source-artifact rejection should not create the planned branch."
        }
        finally {
            [System.IO.File]::WriteAllBytes($callerInitializerPath, $originalInitializerBytes)
        }

        $stagedSourceMarker = "# Staged source-artifact canary."
        try {
            Add-Content -LiteralPath $callerInitializerPath -Value $stagedSourceMarker -Encoding Ascii
            $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("add", "scripts/git/Initialize-CodexIssueWorktree.ps1")
            $stagedSourceTarget = Join-Path $callerPath ".worktrees/codex-460-staged-source-artifact"
            $stagedSource = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "460", "-Slug", "staged-source-artifact")
            Assert-True ($stagedSource.ExitCode -ne 0) "A staged invoking-checkout initializer should fail closed."
            Assert-Contains $stagedSource.Output "Reviewed handoff artifact 'scripts/git/Initialize-CodexIssueWorktree.ps1' has staged changes" "Staged source-artifact rejection should identify the indexed initializer."
            Assert-Contains (Get-Content -Raw -LiteralPath $callerInitializerPath) $stagedSourceMarker "Source-artifact rejection should preserve the maintainer-owned staged content."
            Assert-True (-not (Test-Path -LiteralPath $stagedSourceTarget)) "Staged source-artifact rejection should not leave a target path."
            $stagedSourceBranch = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/issue-460/staged-source-artifact") -WorkingDirectory $callerPath
            Assert-Equal 1 $stagedSourceBranch.ExitCode "Staged source-artifact rejection should not create the planned branch."
        }
        finally {
            [System.IO.File]::WriteAllBytes($callerInitializerPath, $originalInitializerBytes)
            $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("add", "scripts/git/Initialize-CodexIssueWorktree.ps1")
        }

        $hiddenSourceCanary = Join-Path $callerPath "skip-worktree-initializer-executed.txt"
        $escapedHiddenSourceCanary = $hiddenSourceCanary.Replace("'", "''")
        $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("update-index", "--skip-worktree", "--", "scripts/git/Initialize-CodexIssueWorktree.ps1")
        try {
            Add-Content -LiteralPath $callerInitializerPath -Encoding Ascii -Value "[System.IO.File]::WriteAllText('$escapedHiddenSourceCanary', 'EXECUTED')"
            $hiddenIndexEntry = Invoke-Git -WorkingDirectory $callerPath -Arguments @("ls-files", "-v", "--", "scripts/git/Initialize-CodexIssueWorktree.ps1")
            Assert-True $hiddenIndexEntry.StartsWith("S ", [System.StringComparison]::Ordinal) "skip-worktree fixture should mark the initializer index entry."
            $hiddenSourceTarget = Join-Path $callerPath ".worktrees/codex-463-hidden-source-artifact"
            $hiddenSource = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "463", "-Slug", "hidden-source-artifact")
            Assert-True ($hiddenSource.ExitCode -ne 0) "A skip-worktree-hidden initializer modification should fail closed."
            Assert-Contains $hiddenSource.Output "Reviewed handoff artifact 'scripts/git/Initialize-CodexIssueWorktree.ps1' working content does not match the invoking checkout HEAD blob" "Direct source hashing should identify the hidden initializer mismatch."
            Assert-True (-not (Test-Path -LiteralPath $hiddenSourceCanary)) "Hidden source initializer code executed despite the reviewed-blob mismatch."
            Assert-True (-not (Test-Path -LiteralPath $hiddenSourceTarget)) "Hidden source-artifact rejection should not leave a target path."
            $hiddenSourceBranch = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/issue-463/hidden-source-artifact") -WorkingDirectory $callerPath
            Assert-Equal 1 $hiddenSourceBranch.ExitCode "Hidden source-artifact rejection should not create the planned branch."
        }
        finally {
            [System.IO.File]::WriteAllBytes($callerInitializerPath, $originalInitializerBytes)
            $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("update-index", "--no-skip-worktree", "--", "scripts/git/Initialize-CodexIssueWorktree.ps1")
        }

        $filterAttributesPath = Join-Path $callerPath ".git/info/attributes"
        $filterAttributesExisted = Test-Path -LiteralPath $filterAttributesPath
        $originalFilterAttributesBytes = if ($filterAttributesExisted) { [System.IO.File]::ReadAllBytes($filterAttributesPath) } else { $null }
        $filterCanary = Join-Path $callerPath "filter-clean-executed.txt"
        try {
            Set-Content -LiteralPath $filterAttributesPath -Encoding Ascii -Value "scripts/git/Initialize-CodexIssueWorktree.ps1 filter=taskdeck-canary"
            $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("config", "filter.taskdeck-canary.clean", "echo EXECUTED > filter-clean-executed.txt && cat")
            Add-Content -LiteralPath $callerInitializerPath -Encoding Ascii -Value "# Local clean-filter bypass canary."
            $filterSourceTarget = Join-Path $callerPath ".worktrees/codex-465-filter-source-artifact"
            $filterSource = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "465", "-Slug", "filter-source-artifact")
            Assert-True ($filterSource.ExitCode -ne 0) "An altered initializer covered by a local clean filter should fail closed."
            Assert-Contains $filterSource.Output "Reviewed handoff artifact 'scripts/git/Initialize-CodexIssueWorktree.ps1' working content does not match the invoking checkout HEAD blob" "Filter-free byte comparison should identify the altered initializer."
            Assert-True (-not (Test-Path -LiteralPath $filterCanary)) "Artifact verification executed the repository-local clean filter."
            Assert-True (-not (Test-Path -LiteralPath $filterSourceTarget)) "Filter-covered source-artifact rejection should not leave a target path."
            $filterSourceBranch = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/issue-465/filter-source-artifact") -WorkingDirectory $callerPath
            Assert-Equal 1 $filterSourceBranch.ExitCode "Filter-covered source-artifact rejection should not create the planned branch."
        }
        finally {
            [System.IO.File]::WriteAllBytes($callerInitializerPath, $originalInitializerBytes)
            $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("config", "--unset", "filter.taskdeck-canary.clean")
            if ($filterAttributesExisted) {
                [System.IO.File]::WriteAllBytes($filterAttributesPath, $originalFilterAttributesBytes)
            }
            else {
                Remove-Item -LiteralPath $filterAttributesPath -Force -ErrorAction SilentlyContinue
            }
        }

        $callerGuardPath = Join-Path $callerPath "scripts/worktree_guard.ps1"
        $originalGuardBytes = [System.IO.File]::ReadAllBytes($callerGuardPath)
        $sourceGuardCanary = Join-Path $callerPath "source-guard-executed.txt"
        $escapedSourceGuardCanary = $sourceGuardCanary.Replace("'", "''")
        try {
            Add-Content -LiteralPath $callerGuardPath -Encoding Ascii -Value "[System.IO.File]::WriteAllText('$escapedSourceGuardCanary', 'EXECUTED')"
            $dirtyGuardTarget = Join-Path $callerPath ".worktrees/codex-466-dirty-source-guard"
            $dirtyGuard = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "466", "-Slug", "dirty-source-guard")
            Assert-True ($dirtyGuard.ExitCode -ne 0) "A dirty invoking-checkout guard should fail closed."
            Assert-Contains $dirtyGuard.Output "Reviewed handoff artifact 'scripts/worktree_guard.ps1' working content does not match the invoking checkout HEAD blob" "Dirty source-guard rejection should identify the altered guard."
            Assert-True (-not (Test-Path -LiteralPath $sourceGuardCanary)) "Altered source guard code executed despite its reviewed-blob mismatch."
            Assert-True (-not (Test-Path -LiteralPath $dirtyGuardTarget)) "Dirty source-guard rejection should not leave a target path."
            $dirtyGuardBranch = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/issue-466/dirty-source-guard") -WorkingDirectory $callerPath
            Assert-Equal 1 $dirtyGuardBranch.ExitCode "Dirty source-guard rejection should not create the planned branch."
        }
        finally {
            [System.IO.File]::WriteAllBytes($callerGuardPath, $originalGuardBytes)
        }

        $missingGuardBackup = Join-Path $fixtureRoot "missing-source-guard.ps1"
        Move-Item -LiteralPath $callerGuardPath -Destination $missingGuardBackup
        try {
            $missingGuardTarget = Join-Path $callerPath ".worktrees/codex-467-missing-source-guard"
            $missingGuard = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "467", "-Slug", "missing-source-guard")
            Assert-True ($missingGuard.ExitCode -ne 0) "A missing invoking-checkout guard should fail closed."
            Assert-Contains $missingGuard.Output "Reviewed handoff artifact 'scripts/worktree_guard.ps1' is missing from the invoking checkout" "Missing source-guard rejection should identify the absent guard."
            Assert-True (-not (Test-Path -LiteralPath $missingGuardTarget)) "Missing source-guard rejection should not leave a target path."
            $missingGuardBranch = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/issue-467/missing-source-guard") -WorkingDirectory $callerPath
            Assert-Equal 1 $missingGuardBranch.ExitCode "Missing source-guard rejection should not create the planned branch."
        }
        finally {
            Move-Item -LiteralPath $missingGuardBackup -Destination $callerGuardPath
        }

        $callerHelperPath = Join-Path $callerPath "scripts/git/New-CodexIssueWorktree.ps1"
        $originalHelperBytes = [System.IO.File]::ReadAllBytes($callerHelperPath)
        $dirtyHelperMarker = "# Uncommitted helper self-check canary."
        try {
            Add-Content -LiteralPath $callerHelperPath -Value $dirtyHelperMarker -Encoding Ascii
            $dirtyHelperTarget = Join-Path $callerPath ".worktrees/codex-462-dirty-helper-artifact"
            $dirtyHelper = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "462", "-Slug", "dirty-helper-artifact")
            Assert-True ($dirtyHelper.ExitCode -ne 0) "A dirty invoking helper should fail its own trust check."
            Assert-Contains $dirtyHelper.Output "Reviewed handoff artifact 'scripts/git/New-CodexIssueWorktree.ps1' working content does not match the invoking checkout HEAD blob" "Dirty helper rejection should identify the uncommitted helper itself."
            Assert-Contains (Get-Content -Raw -LiteralPath $callerHelperPath) $dirtyHelperMarker "Helper self-rejection should preserve the maintainer-owned dirty content."
            Assert-True (-not (Test-Path -LiteralPath $dirtyHelperTarget)) "Dirty helper rejection should not leave a target path."
            $dirtyHelperBranch = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/issue-462/dirty-helper-artifact") -WorkingDirectory $callerPath
            Assert-Equal 1 $dirtyHelperBranch.ExitCode "Dirty helper rejection should not create the planned branch."
        }
        finally {
            [System.IO.File]::WriteAllBytes($callerHelperPath, $originalHelperBytes)
        }

        $missingSourceBackup = Join-Path $fixtureRoot "missing-source-initializer.ps1"
        Move-Item -LiteralPath $callerInitializerPath -Destination $missingSourceBackup
        try {
            $missingSourceTarget = Join-Path $callerPath ".worktrees/codex-459-missing-source-artifact"
            $missingSource = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "459", "-Slug", "missing-source-artifact")
            Assert-True ($missingSource.ExitCode -ne 0) "A missing invoking-checkout initializer should fail closed."
            Assert-Contains $missingSource.Output "Reviewed handoff artifact 'scripts/git/Initialize-CodexIssueWorktree.ps1' is missing from the invoking checkout" "Missing source-artifact rejection should identify the absent initializer."
            Assert-True (-not (Test-Path -LiteralPath $missingSourceTarget)) "Missing source-artifact rejection should not leave a target path."
            $missingSourceBranch = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/issue-459/missing-source-artifact") -WorkingDirectory $callerPath
            Assert-Equal 1 $missingSourceBranch.ExitCode "Missing source-artifact rejection should not create the planned branch."
        }
        finally {
            Move-Item -LiteralPath $missingSourceBackup -Destination $callerInitializerPath
        }
        $registrationsAfterMissingArtifacts = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
        Assert-Equal $registrationsBeforeMissingArtifacts $registrationsAfterMissingArtifacts "Rejected historical, modified-base, or invalid source artifact changed Git worktree registrations."
        Complete-Test "handoff artifacts are clean at source and exact-blob pinned in every selected local base"
    }

    if (Test-CaseSelected "fully-qualified-ref") {
        $explicitReference = "refs/heads/explicit-base"
        $competingRemoteReference = "refs/heads/heads/explicit-base"
        Assert-True ($fixtureBase -cne $helperArtifactCommit) "Fully-qualified-ref fixture commits must differ."
        $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("update-ref", $explicitReference, $fixtureBase)
        $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("remote", "add", "refs", $remotePath)
        $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("push", "origin", "${helperArtifactCommit}:$competingRemoteReference")
        $unexpectedTrackingRef = "refs/remotes/refs/heads/explicit-base"
        $trackingBeforeExplicitRef = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", $unexpectedTrackingRef) -WorkingDirectory $callerPath
        Assert-Equal 1 $trackingBeforeExplicitRef.ExitCode "Competing remote-tracking ref should be absent before the explicit-ref probe."

        $explicitRefResult = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "455", "-Slug", "fully-qualified-ref", "-BaseBranch", $explicitReference)
        Assert-Equal 0 $explicitRefResult.ExitCode "A fully-qualified local ref should win over a same-prefix remote name.`n$($explicitRefResult.Output)"
        $explicitRefWorktree = Join-Path $callerPath ".worktrees/codex-455-fully-qualified-ref"
        $explicitRefHead = Invoke-Git -WorkingDirectory $explicitRefWorktree -Arguments @("rev-parse", "HEAD")
        Assert-Equal $fixtureBase $explicitRefHead "Fully-qualified ref should resolve directly instead of as remote shorthand."
        Assert-True ($explicitRefHead -cne $helperArtifactCommit) "Fully-qualified ref unexpectedly selected the competing remote branch."
        $trackingAfterExplicitRef = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", $unexpectedTrackingRef) -WorkingDirectory $callerPath
        Assert-Equal 1 $trackingAfterExplicitRef.ExitCode "Explicit refs/... input should not fetch or create a same-prefix remote-tracking ref."
        Complete-Test "fully-qualified refs bypass competing remote shorthand"
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
        $whatIfTrackingBefore = Invoke-Git -WorkingDirectory $callerPath -Arguments @("rev-parse", "refs/remotes/origin/main")
        Assert-True ($whatIfTrackingBefore -cne $whatIfRemoteBase) "Fixture origin/main should be stale before the WhatIf probe."

        $whatIf = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "430", "-Slug", "what-if", "-WhatIf")
        Assert-Equal 0 $whatIf.ExitCode "WhatIf should validate inputs without failing."
        Assert-True (-not (Test-Path -LiteralPath $whatIfTarget)) "WhatIf must not create the target path."
        $whatIfBranch = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/issue-430/what-if") -WorkingDirectory $callerPath
        Assert-Equal 1 $whatIfBranch.ExitCode "WhatIf must not create the planned branch."
        $whatIfTrackingAfter = Invoke-Git -WorkingDirectory $callerPath -Arguments @("rev-parse", "refs/remotes/origin/main")
        Assert-Equal $whatIfTrackingBefore $whatIfTrackingAfter "WhatIf must not refresh the remote-tracking ref."

        $whatIfRegistrationsBefore = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
        $whatIfRefsBefore = Invoke-Git -WorkingDirectory $callerPath -Arguments @("for-each-ref", "--format=%(refname):%(objectname)")
        $missingLocalWhatIfTarget = Join-Path $callerPath ".worktrees/codex-451-what-if-local-missing"
        $missingLocalWhatIf = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "451", "-Slug", "what-if-local-missing", "-BaseBranch", "refs/heads/definitely-missing-what-if", "-WhatIf")
        Assert-True ($missingLocalWhatIf.ExitCode -ne 0) "WhatIf should fail when a local base cannot resolve."
        Assert-Contains $missingLocalWhatIf.Output "Base commit not found: refs/heads/definitely-missing-what-if" "Missing local WhatIf base diagnostic was not clear."
        Assert-True (-not (Test-Path -LiteralPath $missingLocalWhatIfTarget)) "Missing local WhatIf base created a target."

        $missingRemoteWhatIfTarget = Join-Path $callerPath ".worktrees/codex-452-what-if-remote-missing"
        $missingRemoteWhatIf = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "452", "-Slug", "what-if-remote-missing", "-BaseBranch", "origin/definitely-missing-what-if", "-WhatIf")
        Assert-True ($missingRemoteWhatIf.ExitCode -ne 0) "WhatIf should fail when an explicit remote base does not exist."
        Assert-Contains $missingRemoteWhatIf.Output "Base commit not found: origin/definitely-missing-what-if" "Missing remote WhatIf base diagnostic was not clear."
        Assert-True (-not (Test-Path -LiteralPath $missingRemoteWhatIfTarget)) "Missing remote WhatIf base created a target."
        $whatIfRegistrationsAfter = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
        $whatIfRefsAfter = Invoke-Git -WorkingDirectory $callerPath -Arguments @("for-each-ref", "--format=%(refname):%(objectname)")
        Assert-Equal $whatIfRegistrationsBefore $whatIfRegistrationsAfter "Missing-base WhatIf probes changed Git worktree registrations."
        Assert-Equal $whatIfRefsBefore $whatIfRefsAfter "Missing-base WhatIf probes changed Git refs."
        Complete-Test "WhatIf validates local and remote bases without worktree, branch, or ref mutation"
    }

    if (Test-CaseSelected "git-add-failure") {
        $gitFailureTarget = Join-Path $callerPath ".worktrees/codex-431-git-failure"
        $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "add", "--detach", $gitFailureTarget, "refs/remotes/origin/main")
        $parkedGitFailureTarget = Join-Path $callerPath ".worktrees/parked-git-failure"
        Move-Item -LiteralPath $gitFailureTarget -Destination $parkedGitFailureTarget
        $gitFailure = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "431", "-Slug", "git-failure")
        Assert-True ($gitFailure.ExitCode -ne 0) "Native git worktree failure should fail closed."
        Assert-Contains $gitFailure.Output "git worktree add failed for" "Git failure diagnostic omitted the failed operation."
        Assert-NormalizedContains $gitFailure.Output "is a missing but already registered worktree" "Git stderr context should be preserved."
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
