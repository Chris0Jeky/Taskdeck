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
        "remote-default-head",
        "missing-base",
        "what-if",
        "git-add-failure",
        "target-artifact-smudge"
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
        "remote-default-head",
        "missing-base",
        "what-if",
        "git-add-failure",
        "target-artifact-smudge"
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

function Get-PrintedHandoffLaunchRules {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Output
    )

    $outputLines = @($Output -split '\r?\n')
    $marker = "Claude Code task-scoped handoff allow rules (additive PowerShell transport):"
    $markerIndex = [Array]::IndexOf($outputLines, $marker)
    Assert-True ($markerIndex -ge 0) "Helper output omitted the task-scoped handoff allow-rule marker."
    Assert-True (($markerIndex + 8) -lt $outputLines.Count) "Helper output omitted one or more task-scoped handoff allow rules."
    Assert-Equal "`$guardAllowRule = @'" $outputLines[$markerIndex + 1] "Helper output omitted the guard-rule single-quoted here-string opener."
    Assert-Equal "'@" $outputLines[$markerIndex + 3] "Helper output omitted the guard-rule here-string terminator."
    Assert-Equal "`$initializerAllowRule = @'" $outputLines[$markerIndex + 4] "Helper output omitted the initializer-rule single-quoted here-string opener."
    Assert-Equal "'@" $outputLines[$markerIndex + 6] "Helper output omitted the initializer-rule here-string terminator."
    Assert-Equal '$handoffAllowRules = @($guardAllowRule, $initializerAllowRule)' $outputLines[$markerIndex + 7] "Helper output omitted the two-rule argv array."
    Assert-Equal '# Pass as two argv values: claude ... --allowedTools $handoffAllowRules --permission-mode dontAsk <reviewed task prompt>' $outputLines[$markerIndex + 8] "Helper output did not pass both rules as bounded CLI argv values."

    return [pscustomobject]@{
        Guard = $outputLines[$markerIndex + 2]
        Initializer = $outputLines[$markerIndex + 5]
    }
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

        Set-Content -LiteralPath (Join-Path $seedPath "tracked.txt") -Value "case-variant remote refresh" -Encoding Ascii
        $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("add", "tracked.txt")
        $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("commit", "-m", "Advance case-variant remote fixture")
        $caseVariantRemoteBase = Invoke-Git -WorkingDirectory $seedPath -Arguments @("rev-parse", "HEAD")
        $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("push", "origin", "main")
        $caseVariantTrackingBefore = Invoke-Git -WorkingDirectory $callerPath -Arguments @("rev-parse", "refs/remotes/origin/main")
        Assert-True ($caseVariantTrackingBefore -cne $caseVariantRemoteBase) "Fixture origin/main should be stale before the case-variant remote probe."

        $caseVariantRemote = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "494", "-Slug", "case-variant-remote", "-BaseBranch", "Origin/main")
        Assert-Equal 0 $caseVariantRemote.ExitCode "Windows should canonicalize a configured remote's case before refresh instead of resolving a stale loose ref.`n$($caseVariantRemote.Output)"
        $caseVariantWorktree = Join-Path $callerPath ".worktrees/codex-494-case-variant-remote"
        $caseVariantHead = Invoke-Git -WorkingDirectory $caseVariantWorktree -Arguments @("rev-parse", "HEAD")
        $caseVariantTrackingAfter = Invoke-Git -WorkingDirectory $callerPath -Arguments @("rev-parse", "refs/remotes/origin/main")
        Assert-Equal $caseVariantRemoteBase $caseVariantHead "Case-variant remote shorthand selected a stale fallback instead of the refreshed remote commit."
        Assert-Equal $caseVariantRemoteBase $caseVariantTrackingAfter "Case-variant remote shorthand did not refresh the canonical tracking ref."

        $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("remote", "add", "ORIGIN", $remotePath)
        try {
            $ambiguousRemoteTarget = Join-Path $callerPath ".worktrees/codex-497-ambiguous-remote-case"
            $ambiguousRegistrationsBefore = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
            $ambiguousRefsBefore = Invoke-Git -WorkingDirectory $callerPath -Arguments @("for-each-ref", "--format=%(refname):%(objectname)")
            $ambiguousRemote = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "497", "-Slug", "ambiguous-remote-case", "-BaseBranch", "OrIgIn/main")
            Assert-True ($ambiguousRemote.ExitCode -ne 0) "A mixed-case prefix matching two configured remotes must fail closed."
            Assert-NormalizedContains $ambiguousRemote.Output "Base branch remote prefix is ambiguous by case: OrIgIn/main" "Ambiguous remote-case rejection did not identify the unsafe prefix."
            Assert-True (-not (Test-Path -LiteralPath $ambiguousRemoteTarget)) "Ambiguous remote-case rejection created a worktree target."
            $ambiguousRegistrationsAfter = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
            $ambiguousRefsAfter = Invoke-Git -WorkingDirectory $callerPath -Arguments @("for-each-ref", "--format=%(refname):%(objectname)")
            Assert-Equal $ambiguousRegistrationsBefore $ambiguousRegistrationsAfter "Ambiguous remote-case rejection changed worktree registrations."
            Assert-Equal $ambiguousRefsBefore $ambiguousRefsAfter "Ambiguous remote-case rejection changed Git refs."
        }
        finally {
            $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("remote", "remove", "ORIGIN")
        }

        $timeoutProbeDirectory = Join-Path $testRoot "timeout-probe"
        New-Item -ItemType Directory -Path $timeoutProbeDirectory | Out-Null
        $timeoutRemoteScript = Join-Path $timeoutProbeDirectory "remote-helper.ps1"
        $timeoutRootPidPath = Join-Path $timeoutProbeDirectory "root.pid"
        $timeoutRootStartPath = Join-Path $timeoutProbeDirectory "root.start"
        $timeoutChildPidPath = Join-Path $timeoutProbeDirectory "child.pid"
        $timeoutChildStartPath = Join-Path $timeoutProbeDirectory "child.start"
        Set-Content -LiteralPath $timeoutRemoteScript -Encoding Ascii -Value @'
$ErrorActionPreference = "Stop"
$self = Get-Process -Id $PID
[System.IO.File]::WriteAllText($env:TASKDECK_TIMEOUT_ROOT_PID, [string]$PID)
[System.IO.File]::WriteAllText($env:TASKDECK_TIMEOUT_ROOT_START, [string]$self.StartTime.ToUniversalTime().Ticks)
$childStartInfo = [System.Diagnostics.ProcessStartInfo]::new()
$childStartInfo.FileName = $self.Path
$childStartInfo.UseShellExecute = $false
$childStartInfo.CreateNoWindow = $true
$childArguments = @("-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30")
if ($null -ne $childStartInfo.PSObject.Properties['ArgumentList']) {
    foreach ($argument in $childArguments) {
        $childStartInfo.ArgumentList.Add($argument)
    }
}
else {
    $childStartInfo.Arguments = '-NoLogo -NoProfile -NonInteractive -Command "Start-Sleep -Seconds 30"'
}
$child = [System.Diagnostics.Process]::new()
try {
    $child.StartInfo = $childStartInfo
    if (-not $child.Start()) {
        throw "Timeout-probe child process did not start."
    }
    [System.IO.File]::WriteAllText($env:TASKDECK_TIMEOUT_CHILD_PID, [string]$child.Id)
    [System.IO.File]::WriteAllText($env:TASKDECK_TIMEOUT_CHILD_START, [string]$child.StartTime.ToUniversalTime().Ticks)
    $child.WaitForExit()
}
finally {
    $child.Dispose()
}
'@

        $timeoutRootPid = $null
        $timeoutRootStart = $null
        $timeoutChildPid = $null
        $timeoutChildStart = $null
        $timeoutEnvironment = @{
            TASKDECK_TIMEOUT_ROOT_PID = $timeoutRootPidPath
            TASKDECK_TIMEOUT_ROOT_START = $timeoutRootStartPath
            TASKDECK_TIMEOUT_CHILD_PID = $timeoutChildPidPath
            TASKDECK_TIMEOUT_CHILD_START = $timeoutChildStartPath
        }
        $previousTimeoutEnvironment = @{}
        foreach ($environmentName in $timeoutEnvironment.Keys) {
            $previousTimeoutEnvironment[$environmentName] = [System.Environment]::GetEnvironmentVariable($environmentName, "Process")
            [System.Environment]::SetEnvironmentVariable($environmentName, $timeoutEnvironment[$environmentName], "Process")
        }
        $timeoutPowerShellArgument = $powerShellExecutable.Replace('\', '/').Replace('%', '%%').Replace(' ', '% ')
        $timeoutScriptArgument = $timeoutRemoteScript.Replace('\', '/').Replace('%', '%%').Replace(' ', '% ')
        $timeoutRemoteUrl = "ext::$timeoutPowerShellArgument -NoLogo -NoProfile -NonInteractive -File $timeoutScriptArgument"
        $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("config", "protocol.ext.allow", "always")
        $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("remote", "add", "timeout-probe", $timeoutRemoteUrl)
        try {
            $timeoutTarget = Join-Path $callerPath ".worktrees/codex-498-git-timeout"
            $timeoutRegistrationsBefore = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
            $timeoutRefsBefore = Invoke-Git -WorkingDirectory $callerPath -Arguments @("for-each-ref", "--format=%(refname):%(objectname)")
            $timeoutStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
            $timeoutResult = Invoke-Helper -WorkingDirectory $callerPath -Arguments @(
                "-IssueNumber", "498",
                "-Slug", "git-timeout",
                "-BaseBranch", "timeout-probe/main",
                "-GitCommandTimeoutSeconds", "5"
            )
            $timeoutStopwatch.Stop()

            Assert-True ($timeoutResult.ExitCode -ne 0) "A non-responsive remote helper must fail closed."
            Assert-NormalizedContains $timeoutResult.Output "Git command timed out after 5 seconds; its helper-owned process tree was terminated and reaped." "Timeout diagnostic did not confirm bounded tree cleanup."
            Assert-True ($timeoutStopwatch.Elapsed.TotalSeconds -lt 20) "Timed-out Git command returned too slowly: $($timeoutStopwatch.Elapsed.TotalSeconds) seconds."
            Assert-True (Test-Path -LiteralPath $timeoutRootPidPath -PathType Leaf) "Timeout fixture did not record its root process."
            Assert-True (Test-Path -LiteralPath $timeoutChildPidPath -PathType Leaf) "Timeout fixture did not record its child process."
            $timeoutRootPid = [int](Get-Content -Raw -LiteralPath $timeoutRootPidPath)
            $timeoutRootStart = [long](Get-Content -Raw -LiteralPath $timeoutRootStartPath)
            $timeoutChildPid = [int](Get-Content -Raw -LiteralPath $timeoutChildPidPath)
            $timeoutChildStart = [long](Get-Content -Raw -LiteralPath $timeoutChildStartPath)
            $liveTimeoutRoot = Get-Process -Id $timeoutRootPid -ErrorAction SilentlyContinue
            $liveTimeoutChild = Get-Process -Id $timeoutChildPid -ErrorAction SilentlyContinue
            $sameTimeoutRoot = $null -ne $liveTimeoutRoot -and $liveTimeoutRoot.StartTime.ToUniversalTime().Ticks -eq $timeoutRootStart
            $sameTimeoutChild = $null -ne $liveTimeoutChild -and $liveTimeoutChild.StartTime.ToUniversalTime().Ticks -eq $timeoutChildStart
            Assert-True (-not $sameTimeoutRoot) "Timed-out Git command returned while its exact remote-helper root was still alive."
            Assert-True (-not $sameTimeoutChild) "Timed-out Git command returned while its exact remote-helper child was still alive."
            Assert-True (-not (Test-Path -LiteralPath $timeoutTarget)) "Timed-out remote lookup created a worktree target."
            $timeoutBranch = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/issue-498/git-timeout") -WorkingDirectory $callerPath
            Assert-Equal 1 $timeoutBranch.ExitCode "Timed-out remote lookup created its planned branch."
            $timeoutRegistrationsAfter = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
            $timeoutRefsAfter = Invoke-Git -WorkingDirectory $callerPath -Arguments @("for-each-ref", "--format=%(refname):%(objectname)")
            Assert-Equal $timeoutRegistrationsBefore $timeoutRegistrationsAfter "Timed-out remote lookup changed worktree registrations."
            Assert-Equal $timeoutRefsBefore $timeoutRefsAfter "Timed-out remote lookup changed Git refs."
        }
        finally {
            foreach ($environmentName in $timeoutEnvironment.Keys) {
                [System.Environment]::SetEnvironmentVariable($environmentName, $previousTimeoutEnvironment[$environmentName], "Process")
            }
            if ($null -eq $timeoutRootPid -and
                (Test-Path -LiteralPath $timeoutRootPidPath -PathType Leaf) -and
                (Test-Path -LiteralPath $timeoutRootStartPath -PathType Leaf)) {
                $timeoutRootPid = [int](Get-Content -Raw -LiteralPath $timeoutRootPidPath)
                $timeoutRootStart = [long](Get-Content -Raw -LiteralPath $timeoutRootStartPath)
            }
            if ($null -eq $timeoutChildPid -and
                (Test-Path -LiteralPath $timeoutChildPidPath -PathType Leaf) -and
                (Test-Path -LiteralPath $timeoutChildStartPath -PathType Leaf)) {
                $timeoutChildPid = [int](Get-Content -Raw -LiteralPath $timeoutChildPidPath)
                $timeoutChildStart = [long](Get-Content -Raw -LiteralPath $timeoutChildStartPath)
            }
            foreach ($processIdentity in @(
                [pscustomobject]@{ Id = $timeoutChildPid; Start = $timeoutChildStart },
                [pscustomobject]@{ Id = $timeoutRootPid; Start = $timeoutRootStart }
            )) {
                if ($null -eq $processIdentity.Id -or $null -eq $processIdentity.Start) {
                    continue
                }
                $recordedProcess = Get-Process -Id $processIdentity.Id -ErrorAction SilentlyContinue
                if ($null -ne $recordedProcess -and
                    $recordedProcess.StartTime.ToUniversalTime().Ticks -eq $processIdentity.Start) {
                    try {
                        if (-not $recordedProcess.HasExited) {
                            $recordedProcess.Kill()
                            if (-not $recordedProcess.WaitForExit(5000)) {
                                throw "Timeout fixture process $($recordedProcess.Id) did not exit during test cleanup."
                            }
                            $recordedProcess.WaitForExit()
                        }
                    }
                    finally {
                        $recordedProcess.Dispose()
                    }
                }
            }
            $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("remote", "remove", "timeout-probe")
            $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("config", "--unset", "protocol.ext.allow")
        }
        Complete-Test "complete remote names are safely refreshed and non-responsive Git process trees are bounded and reaped"
    }

    if (Test-CaseSelected "remote-default-head") {
        $staleDefaultHead = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("rev-parse", "--verify", "refs/remotes/origin/HEAD") -WorkingDirectory $callerPath
        $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("switch", "-c", "default-next")
        Set-Content -LiteralPath (Join-Path $seedPath "tracked.txt") -Value "remote default advanced" -Encoding Ascii
        $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("add", "tracked.txt")
        $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("commit", "-m", "Advance remote default branch")
        $remoteDefaultCommit = Invoke-Git -WorkingDirectory $seedPath -Arguments @("rev-parse", "HEAD")
        $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("push", "origin", "default-next")
        $null = Invoke-Git -WorkingDirectory $remotePath -Arguments @("symbolic-ref", "HEAD", "refs/heads/default-next")
        $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("switch", "main")

        $remoteDefault = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "472", "-Slug", "remote-default-head", "-BaseBranch", "origin/HEAD")
        Assert-Equal 0 $remoteDefault.ExitCode "origin/HEAD should resolve and refresh the remote's current default branch.`n$($remoteDefault.Output)"
        $remoteDefaultWorktree = Join-Path $callerPath ".worktrees/codex-472-remote-default-head"
        $remoteDefaultHead = Invoke-Git -WorkingDirectory $remoteDefaultWorktree -Arguments @("rev-parse", "HEAD")
        Assert-Equal $remoteDefaultCommit $remoteDefaultHead "origin/HEAD should not use a stale local symbolic remote ref."
        if ($staleDefaultHead.ExitCode -eq 0) {
            Assert-True ($staleDefaultHead.Output.Trim() -cne $remoteDefaultHead) "The disposable clone must retain a stale origin/HEAD probe for this regression."
        }
        Complete-Test "remote symbolic default bases resolve from the remote instead of stale origin/HEAD"
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
        Assert-NormalizedContains $reparse.Output "is a reparse point or symbolic link" "Reparse-root diagnostic was not clear."
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
            $escapedGuard = (Join-Path $createdWorktree "scripts/worktree_guard.ps1").Replace("'", "''")
            $escapedInitializer = (Join-Path $createdWorktree "scripts/git/Initialize-CodexIssueWorktree.ps1").Replace("'", "''")
            $expectedHandoffHead = Invoke-Git -WorkingDirectory $createdWorktree -Arguments @("rev-parse", "HEAD")
            $guardLine = "& '$escapedGuard' -GitExecutable '$escapedGitExecutable'"
            $initializerLine = "& '$escapedInitializer' -GitExecutable '$escapedGitExecutable' -BranchName 'issue-424/dirty-source' -ExpectedWorktree '$escapedWorktree' -ExpectedHead '$expectedHandoffHead'"
            $guardAllowRule = "PowerShell($guardLine)"
            $initializerAllowRule = "PowerShell($initializerLine)"
            $guardCaptureLine = '$guardSucceeded = $?; $guardExitCode = $LASTEXITCODE'
            $guardExitLine = 'if (-not $guardSucceeded -or $guardExitCode -ne 0) { if ($null -ne $guardExitCode -and $guardExitCode -ne 0) { exit $guardExitCode }; exit 1 }'
            $initializerCaptureLine = '$handoffSucceeded = $?; $handoffExitCode = $LASTEXITCODE'
            $initializerExitLine = 'if (-not $handoffSucceeded -or $handoffExitCode -ne 0) { if ($null -ne $handoffExitCode -and $handoffExitCode -ne 0) { exit $handoffExitCode }; exit 1 }'
            $handoffLines = @(Get-PrintedHandoffLines -Output $success.Output)
            Assert-Equal $guardLine $handoffLines[0] "Handoff output must make the worktree guard its first command."
            Assert-Equal $guardCaptureLine $handoffLines[1] "Handoff output omitted the guard status capture."
            Assert-Equal $guardExitLine $handoffLines[2] "Handoff output omitted the guard fail-fast gate."
            Assert-Equal $initializerLine $handoffLines[3] "Handoff output omitted the bounded initializer command and exact worktree binding."
            Assert-Equal $initializerCaptureLine $handoffLines[4] "Handoff output omitted the initializer status capture."
            Assert-Equal $initializerExitLine $handoffLines[5] "Handoff output omitted the initializer fail-fast gate."
            $handoffLaunchRules = Get-PrintedHandoffLaunchRules -Output $success.Output
            Assert-Equal $guardAllowRule $handoffLaunchRules.Guard "Helper output omitted the exact target-scoped guard allow rule."
            Assert-Equal $initializerAllowRule $handoffLaunchRules.Initializer "Helper output omitted the exact target-scoped initializer allow rule."
            Assert-True ($guardAllowRule.Contains("''")) "Exact guard rule should PowerShell-escape the apostrophe-containing target path."
            Assert-True ($initializerAllowRule.Contains("''")) "Exact full-command rule should PowerShell-escape the apostrophe-containing target path."
            Assert-True (-not $guardAllowRule.Contains(":*)")) "Exact full-command guard rule must not retain a wildcard suffix."
            Assert-True (-not $initializerAllowRule.Contains(":*)")) "Exact full-command initializer rule must not retain a wildcard suffix."

            $claudeSettings = Get-Content -Raw -LiteralPath $claudeSettingsPath | ConvertFrom-Json
            Assert-True ($null -eq $claudeSettings.PSObject.Properties['hooks']) "Taskdeck project settings must not install runtime hooks."
            Assert-True ($null -eq $claudeSettings.permissions.PSObject.Properties['deny']) "Taskdeck project settings must not install a local command-deny list."
            Assert-True ($claudeSettings.permissions.allow -notcontains "PowerShell(& 'scripts/git/Initialize-CodexIssueWorktree.ps1':*)") "Claude permissions retained the cross-worktree relative initializer rule."
            Assert-True ($claudeSettings.permissions.allow -notcontains $guardAllowRule) "Claude project settings should not commit a task-specific absolute guard rule."
            Assert-True ($claudeSettings.permissions.allow -notcontains $initializerAllowRule) "Claude project settings should not commit a task-specific absolute initializer rule."
            Assert-True ($claudeSettings.permissions.allow -notcontains "Bash(powershell -NoLogo -NoProfile -NonInteractive -File scripts/git/Initialize-CodexIssueWorktree.ps1:*)") "Claude permissions retained the PATH-resolved PowerShell initializer rule."
            Complete-Test "handoff emits additive exact-target guard and initializer rules with fail-fast gates"
        }

        if (Test-CaseSelected "headless-permission-contract") {
            $escapedInitializer = (Join-Path $createdWorktree "scripts/git/Initialize-CodexIssueWorktree.ps1").Replace("'", "''")
            $initializerInvocationPrefix = "& '$escapedInitializer'"
            $headlessHandoffLines = @(Get-PrintedHandoffLines -Output $success.Output)
            Assert-True ($headlessHandoffLines[3].StartsWith($initializerInvocationPrefix, [System.StringComparison]::Ordinal)) "Printed handoff must run the exact helper-created target initializer after the direct guard."

            $claudeSettings = Get-Content -Raw -LiteralPath $claudeSettingsPath | ConvertFrom-Json
            $powerShellGuardRule = "PowerShell($($headlessHandoffLines[0]))"
            $powerShellInitializerRule = "PowerShell($($headlessHandoffLines[3]))"
            $printedHandoffRules = Get-PrintedHandoffLaunchRules -Output $success.Output
            Assert-Equal $powerShellGuardRule $printedHandoffRules.Guard "Helper did not print the task-scoped guard rule used by the handoff."
            Assert-Equal $powerShellInitializerRule $printedHandoffRules.Initializer "Helper did not print the task-scoped initializer rule used by the handoff."
            Assert-True ($claudeSettings.permissions.allow -notcontains $powerShellGuardRule) "Claude project settings should not commit a task-specific guard rule."
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

            $taskLaunchRule = "Bash(dotnet test backend/Taskdeck.sln -c Release -m:1)"
            $reviewedConfiguration = Get-ModeledEffectivePermissionConfiguration -SettingSources @("project") -ProjectSettingsPath $claudeSettingsPath -MainCheckoutLocalSettingsPath $mainCheckoutLocalSettingsPath -CommandLineAllowRules @($powerShellGuardRule, $powerShellInitializerRule, $taskLaunchRule) -CommandLinePermissionMode "dontAsk"
            Assert-Equal "dontAsk" $reviewedConfiguration.PermissionMode "The supported project-only posture should use the explicit dontAsk mode."
            Assert-True ($reviewedConfiguration.Allow -notcontains $broadLocalRule) "The supported project-only source posture should exclude the main-checkout local allow."
            Assert-True ($reviewedConfiguration.Allow -contains $powerShellGuardRule) "The reviewed effective permissions should include the explicit additive task guard rule."
            Assert-True ($reviewedConfiguration.Allow -contains $powerShellInitializerRule) "The reviewed effective permissions should include the explicit additive task initializer rule."
            $alteredGuardRule = $powerShellGuardRule.Replace("worktree_guard.ps1", "other_guard.ps1")
            Assert-True ($reviewedConfiguration.Allow -notcontains $alteredGuardRule) "The exact guard rule must not authorize a substituted guard path."
            $alteredInitializerRule = $powerShellInitializerRule.Replace("issue-424/dirty-source", "issue-424/other-branch")
            Assert-True ($reviewedConfiguration.Allow -notcontains $alteredInitializerRule) "The exact initializer rule must not authorize substituted branch arguments."
            Assert-True ($reviewedConfiguration.Allow -contains $taskLaunchRule) "The reviewed effective permissions should include explicit task launch rules."
            Assert-True (-not $reviewedConfiguration.Environment.ContainsKey("CLAUDE_CODE_USE_POWERSHELL_TOOL")) "Project settings must not enable the unsandboxed Windows PowerShell tool repo-wide."
            $committedPowerShellRules = @($claudeSettings.permissions.allow | Where-Object { $_.StartsWith('PowerShell(', [System.StringComparison]::Ordinal) })
            $expectedProjectUtilityPowerShellRules = @(
                "PowerShell(py -3 -B scripts/agent_hooks/render_failure_ledger.py:*)",
                'PowerShell(py -3 -B -m unittest discover -s scripts/agent_hooks -p "test_render_failure_ledger.py":*)'
            )
            Assert-Equal $expectedProjectUtilityPowerShellRules.Count $committedPowerShellRules.Count "Project settings must permit only the reviewed manual agent-utility PowerShell commands."
            foreach ($expectedProjectUtilityRule in $expectedProjectUtilityPowerShellRules) {
                Assert-True ($committedPowerShellRules -contains $expectedProjectUtilityRule) "Project settings omitted or replaced a reviewed manual agent-utility PowerShell command: $expectedProjectUtilityRule"
            }
            $effectivePowerShellRules = @($reviewedConfiguration.Allow | Where-Object { $_.StartsWith('PowerShell(', [System.StringComparison]::Ordinal) })
            Assert-Equal ($expectedProjectUtilityPowerShellRules.Count + 2) $effectivePowerShellRules.Count "The supported headless posture should add only the exact guard and initializer PowerShell rules to reviewed manual utilities."
            Assert-True ($effectivePowerShellRules -contains $powerShellGuardRule) "The supported headless posture should permit the exact guard PowerShell rule."
            Assert-True ($effectivePowerShellRules -contains $powerShellInitializerRule) "The supported headless posture should permit the exact initializer PowerShell rule."

            $untrustedConfiguration = Get-ModeledEffectivePermissionConfiguration -SettingSources @("project") -ProjectSettingsPath $claudeSettingsPath -MainCheckoutLocalSettingsPath $mainCheckoutLocalSettingsPath -CommandLineAllowRules @($powerShellGuardRule, $powerShellInitializerRule, $taskLaunchRule) -CommandLinePermissionMode "dontAsk" -WorkspaceTrusted $false
            Assert-True ($untrustedConfiguration.Allow -contains $powerShellGuardRule) "An untrusted workspace should retain the exact guard rule supplied through CLI argv."
            Assert-True ($untrustedConfiguration.Allow -contains $powerShellInitializerRule) "An untrusted workspace should retain the exact initializer rule supplied through CLI argv."
            Assert-True ($untrustedConfiguration.Allow -contains $taskLaunchRule) "An untrusted workspace should retain other explicitly supplied CLI rules."
            $untrustedPowerShellRules = @($untrustedConfiguration.Allow | Where-Object { $_.StartsWith('PowerShell(', [System.StringComparison]::Ordinal) })
            Assert-Equal 2 $untrustedPowerShellRules.Count "An untrusted workspace should retain only the two exact CLI-supplied PowerShell rules."
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
            Assert-Contains $protocol 'claude -p --setting-sources project --allowedTools $handoffAllowRules --permission-mode dontAsk' "Headless guidance omitted the exact-target project-only launch and both task handoff rules."
            Assert-True (-not $protocol.Contains('claude -p --worktree')) "Headless guidance would create a second Claude worktree instead of staying in the helper-created target."
            Assert-Contains $protocol "Set-Location -LiteralPath '<exact helper-created worktree>'" "Headless guidance did not bind the Claude process cwd to the helper-created target."
            Assert-NormalizedContains $protocol "acceptEdits does not approve arbitrary Git or PowerShell commands" "Headless guidance must not present acceptEdits as sufficient command authorization."
            Assert-NormalizedContains $protocol "Do not present the launch allowlist as the sole authorization boundary" "Headless guidance must describe the complete effective permission boundary."
            Assert-NormalizedContains $protocol "Organization-managed settings remain effective" "Headless guidance must bound residual administrator-owned trust."
            Assert-NormalizedContains $protocol "built-in read-only Bash commands, or applicable externally managed hook decisions" "Headless guidance must retain documented non-allowlist authorization paths."
            Assert-NormalizedContains $protocol 'overrides a file-backed `defaultMode` for that session' "Headless guidance must distinguish the command-line mode override from merged allow rules."
            Assert-Contains $protocol "CLAUDE_CODE_USE_POWERSHELL_TOOL" "Headless guidance omitted the task-scoped host PowerShell-tool enablement."
            Assert-Contains $protocol "Remove-Item Env:CLAUDE_CODE_USE_POWERSHELL_TOOL" "Headless guidance did not restore an absent host PowerShell-tool value after launch."
            Assert-NormalizedContains $protocol 'restore its prior process value after `claude -p` returns' "Headless guidance did not bound the PowerShell-tool opt-in to one launch."
            Assert-NormalizedContains $protocol "repository deliberately does not enable that tool or grant generic PowerShell access project-wide" "Headless guidance omitted the no-generic-project-wide-PowerShell boundary."
            Assert-NormalizedContains $protocol "Two narrow manual failure-ledger utility rules remain effective" "Headless guidance omitted the two committed manual utility rules from the effective permission set."
            Assert-NormalizedContains $protocol "on Windows it is not sandboxed" "Headless guidance omitted the PowerShell sandbox limitation."
            Assert-NormalizedContains $protocol "repository installs no project command-deny, failure-capture, or pre-commit hooks" "Headless guidance omitted the no-project-hook boundary."
            Assert-NormalizedContains $protocol "does not make an untrusted workspace trusted" "Headless guidance must not treat -p as accepted project trust."
            Assert-NormalizedContains $protocol "Pass every required allow rule through CLI argv" "Headless guidance omitted the untrusted-workspace CLI-only posture."
            Assert-Contains $protocol '$guardAllowRule = @''' "Headless guidance omitted quote-safe guard-rule transport."
            Assert-Contains $protocol '$initializerAllowRule = @''' "Headless guidance omitted quote-safe initializer-rule transport."
            Assert-Contains $protocol '$handoffAllowRules = @($guardAllowRule, $initializerAllowRule)' "Headless guidance omitted the two-rule argv array."
            Assert-Contains $protocol "--permission-mode dontAsk" "Headless guidance omitted the non-prompting permission mode for reviewed effective permissions."
            Assert-Contains $protocol '$coordinatorBranchBaseline' "Post-run guidance omitted the pre-creation coordinator branch baseline."
            Assert-Contains $protocol '$coordinatorStatusBaseline' "Post-run guidance omitted the pre-creation coordinator status baseline."

            $headlessExampleStart = $protocol.IndexOf("Set-Location -LiteralPath '<exact helper-created worktree>'", [System.StringComparison]::Ordinal)
            $headlessExampleEnd = $protocol.IndexOf('```', $headlessExampleStart, [System.StringComparison]::Ordinal)
            Assert-True ($headlessExampleStart -ge 0 -and $headlessExampleEnd -gt $headlessExampleStart) "Headless guidance omitted its executable PowerShell example."
            $headlessExampleScript = $protocol.Substring($headlessExampleStart, $headlessExampleEnd - $headlessExampleStart)
            $documentedLaunchLine = 'claude -p --setting-sources project --allowedTools $handoffAllowRules --permission-mode dontAsk <reviewed task prompt>'
            $launchProbeLine = '$script:observedHeadlessPowerShellToolValue = $env:CLAUDE_CODE_USE_POWERSHELL_TOOL; throw ''Taskdeck headless launch canary'''
            $escapedHeadlessWorktree = $createdWorktree.Replace("'", "''")
            $headlessExampleScript = $headlessExampleScript.Replace("'<exact helper-created worktree>'", "'$escapedHeadlessWorktree'").Replace($documentedLaunchLine, $launchProbeLine)
            $headlessExampleBlock = [scriptblock]::Create($headlessExampleScript)
            $originalHeadlessLocation = (Get-Location).Path
            $originalPowerShellToolValue = [Environment]::GetEnvironmentVariable('CLAUDE_CODE_USE_POWERSHELL_TOOL', [EnvironmentVariableTarget]::Process)
            $powerShellToolRestoreCases = @(
                [pscustomobject]@{ Exists = $false; Value = $null },
                [pscustomobject]@{ Exists = $true; Value = '0' }
            )
            try {
                foreach ($restoreCase in $powerShellToolRestoreCases) {
                    if ($restoreCase.Exists) {
                        $env:CLAUDE_CODE_USE_POWERSHELL_TOOL = $restoreCase.Value
                    } else {
                        Remove-Item Env:CLAUDE_CODE_USE_POWERSHELL_TOOL -ErrorAction SilentlyContinue
                    }
                    $script:observedHeadlessPowerShellToolValue = $null
                    $launchProbeFailed = $false
                    try {
                        & $headlessExampleBlock
                    } catch {
                        $launchProbeFailed = $true
                        Assert-Contains $_.Exception.Message 'Taskdeck headless launch canary' "Headless launch probe failed for an unexpected reason."
                    }
                    Assert-True $launchProbeFailed "Headless launch probe should exercise the documented finally block."
                    Assert-Equal '1' $script:observedHeadlessPowerShellToolValue "Headless launch did not enable the PowerShell tool for its child process."
                    $restoredPowerShellToolValue = [Environment]::GetEnvironmentVariable('CLAUDE_CODE_USE_POWERSHELL_TOOL', [EnvironmentVariableTarget]::Process)
                    Assert-Equal $restoreCase.Value $restoredPowerShellToolValue "Headless launch did not restore the prior PowerShell-tool process value."
                }
            } finally {
                if ($null -eq $originalPowerShellToolValue) {
                    Remove-Item Env:CLAUDE_CODE_USE_POWERSHELL_TOOL -ErrorAction SilentlyContinue
                } else {
                    $env:CLAUDE_CODE_USE_POWERSHELL_TOOL = $originalPowerShellToolValue
                }
                Set-Location -LiteralPath $originalHeadlessLocation
                Remove-Variable -Name observedHeadlessPowerShellToolValue -Scope Script -ErrorAction SilentlyContinue
            }
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

            $metacharHandoffRules = Get-PrintedHandoffLaunchRules -Output $powerShellHostResult.Output
            $metacharOutputLines = @($powerShellHostResult.Output -split '\r?\n')
            $metacharRuleMarkerIndex = [Array]::IndexOf($metacharOutputLines, "Claude Code task-scoped handoff allow rules (additive PowerShell transport):")
            $ruleCapturePath = Join-Path $fixtureRoot "handoff-rule-argv.txt"
            $escapedRuleCapturePath = $ruleCapturePath.Replace("'", "''")
            $ruleCaptureScript = Join-Path $fixtureRoot "handoff-rule-capture.ps1"
            Set-Content -LiteralPath $ruleCaptureScript -Encoding Ascii -Value @(
                'if ($args.Count -ne 3 -or $args[0] -cne ''--allowedTools'') { exit 91 }',
                "[System.IO.File]::WriteAllText('$escapedRuleCapturePath', `$args[1] + [Environment]::NewLine + `$args[2])"
            )
            $escapedPowerShellExecutable = $powerShellExecutable.Replace("'", "''")
            $escapedRuleCaptureScript = $ruleCaptureScript.Replace("'", "''")
            $ruleTransportScript = Join-Path $fixtureRoot "initializer-rule-transport.ps1"
            Set-Content -LiteralPath $ruleTransportScript -Encoding Ascii -Value @(
                $metacharOutputLines[$metacharRuleMarkerIndex + 1],
                $metacharOutputLines[$metacharRuleMarkerIndex + 2],
                $metacharOutputLines[$metacharRuleMarkerIndex + 3],
                $metacharOutputLines[$metacharRuleMarkerIndex + 4],
                $metacharOutputLines[$metacharRuleMarkerIndex + 5],
                $metacharOutputLines[$metacharRuleMarkerIndex + 6],
                $metacharOutputLines[$metacharRuleMarkerIndex + 7],
                "& '$escapedPowerShellExecutable' -NoLogo -NoProfile -NonInteractive -File '$escapedRuleCaptureScript' --allowedTools `$handoffAllowRules",
                'exit $LASTEXITCODE'
            )
            $ruleTransport = Invoke-ProcessCapture -FilePath $powerShellExecutable -Arguments @("-NoLogo", "-NoProfile", "-NonInteractive", "-File", $ruleTransportScript) -WorkingDirectory $fixtureRoot
            Assert-Equal 0 $ruleTransport.ExitCode "Exact emitted here-strings should parse and cross a real PowerShell 5.1 native boundary as two argv values.`n$($ruleTransport.Output)"
            $expectedCapturedRules = $metacharHandoffRules.Guard + [Environment]::NewLine + $metacharHandoffRules.Initializer
            Assert-Equal $expectedCapturedRules ([System.IO.File]::ReadAllText($ruleCapturePath)) "Native argv transport changed one or both exact handoff rules."

            $quotedBranch = 'issue-469/quoted"rule'
            $quoteRegistrationsBefore = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
            $quotedRuleResult = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "469", "-Slug", "quoted-rule", "-BranchName", $quotedBranch)
            $quotedRuleWorktree = Join-Path $callerPath ".worktrees/codex-469-quoted-rule"
            Assert-True ($quotedRuleResult.ExitCode -ne 0) "Windows-incompatible quote-bearing branch should fail before detached worktree creation."
            Assert-NormalizedContains $quotedRuleResult.Output "Invalid branch name for Windows-compatible worktrees: $quotedBranch" "Quote-bearing branch rejection omitted its portability diagnostic."
            Assert-True (-not (Test-Path -LiteralPath $quotedRuleWorktree)) "Rejected quote-bearing branch left a target path."
            $quotedBranchRef = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/$quotedBranch") -WorkingDirectory $callerPath
            Assert-Equal 1 $quotedBranchRef.ExitCode "Rejected quote-bearing branch created a shared ref."
            $quoteRegistrationsAfter = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
            Assert-Equal $quoteRegistrationsBefore $quoteRegistrationsAfter "Rejected quote-bearing branch changed worktree registrations."
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
        Assert-NormalizedContains $missingExecutableHandoff.Output "no argv-safe Git executable was found" "Missing Git should retain its setup diagnostic."
        Assert-True (-not (($missingExecutableHandoff.Output -replace '\s+', ' ').Contains("not inside a git repository"))) "Missing Git must not be mislabeled as an ordinary non-repository result."
        $missingExecutableBranch = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/issue-442/missing-executable") -WorkingDirectory $callerPath
        Assert-Equal 1 $missingExecutableBranch.ExitCode "A disappeared Git executable must not create the planned branch."

        $missingInitializerLines = @(Get-PrintedHandoffLines -Output $missingExecutableResult.Output)
        $escapedInitializerPath = (Join-Path $missingExecutableWorktree "scripts/git/Initialize-CodexIssueWorktree.ps1").Replace("'", "''")
        $escapedMissingInitializerPath = (Join-Path $missingExecutableWorktree "scripts/git/missing-initializer.ps1").Replace("'", "''")
        $missingInitializerLines[3] = $missingInitializerLines[3].Replace($escapedInitializerPath, $escapedMissingInitializerPath)
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
        Assert-NormalizedContains $collision.Output "removal of the unused helper-created worktree was scheduled" "Late branch collision should report cleanup scheduling."
        for ($attempt = 0; $attempt -lt 50 -and (Test-Path -LiteralPath $initializerWorktree); $attempt++) {
            Start-Sleep -Milliseconds 100
        }
        Assert-True (-not (Test-Path -LiteralPath $initializerWorktree)) "Late branch collision must not leave an orphan helper-created worktree."
        $registrationsAfterCollision = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
        Assert-True (-not $registrationsAfterCollision.Contains($initializerWorktree)) "Late branch collision must remove the worktree registration."

        $ignoredCollisionResult = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "498", "-Slug", "ignored-collision")
        Assert-Equal 0 $ignoredCollisionResult.ExitCode "Ignored-collision fixture worktree creation should succeed.`n$($ignoredCollisionResult.Output)"
        $ignoredCollisionWorktree = Join-Path $callerPath ".worktrees/codex-498-ignored-collision"
        $ignoredCollisionHead = Invoke-Git -WorkingDirectory $ignoredCollisionWorktree -Arguments @("rev-parse", "HEAD")
        $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("branch", "issue-498/ignored-collision", $ignoredCollisionHead)
        $ignoredCanaryDirectory = Join-Path $ignoredCollisionWorktree ".runtime-codex"
        $ignoredCanaryPath = Join-Path $ignoredCanaryDirectory "preserve.txt"
        $callerCommonGitDirectory = Invoke-Git -WorkingDirectory $callerPath -Arguments @(
            "rev-parse", "--path-format=absolute", "--git-common-dir"
        )
        Add-Content -LiteralPath (Join-Path $callerCommonGitDirectory "info/exclude") -Value "/.runtime-codex/" -Encoding Ascii
        $null = New-Item -ItemType Directory -Path $ignoredCanaryDirectory
        Set-Content -LiteralPath $ignoredCanaryPath -Value "must survive refused cleanup" -Encoding Ascii
        $ignoredStatusBefore = Invoke-Git -WorkingDirectory $ignoredCollisionWorktree -Arguments @(
            "status", "--porcelain=v1", "--untracked-files=all", "--ignored=matching", "--"
        )
        Assert-NormalizedContains $ignoredStatusBefore "!! .runtime-codex/" "Ignored-collision fixture did not expose the ignored canary to the cleanup inventory."

        $ignoredCollisionScript = Join-Path $fixtureRoot "initializer-ignored-branch-collision.ps1"
        Set-Content -LiteralPath $ignoredCollisionScript -Value @(Get-PrintedHandoffLines -Output $ignoredCollisionResult.Output) -Encoding Ascii
        $ignoredCollision = Invoke-ProcessCapture -FilePath $powerShellExecutable -Arguments @("-NoLogo", "-NoProfile", "-NonInteractive", "-File", $ignoredCollisionScript) -WorkingDirectory $ignoredCollisionWorktree
        Assert-True ($ignoredCollision.ExitCode -ne 0) "A late branch collision with ignored content should fail the initializer."
        Assert-NormalizedContains $ignoredCollision.Output "cleanup was refused because the helper-created worktree contains tracked, untracked, or ignored content" "Ignored-content collision should explain why cleanup was refused."
        Assert-NormalizedContains $ignoredCollision.Output "the worktree was preserved at" "Ignored-content collision should report preservation instead of scheduled removal."
        Start-Sleep -Milliseconds 500
        Assert-True (Test-Path -LiteralPath $ignoredCanaryPath -PathType Leaf) "Refused collision cleanup deleted ignored worktree content."
        Assert-True (Test-Path -LiteralPath $ignoredCollisionWorktree -PathType Container) "Refused collision cleanup deleted the helper-created worktree."
        $ignoredRegistrationsAfter = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
        $normalizedIgnoredCollisionWorktree = $ignoredCollisionWorktree.Replace('\', '/')
        Assert-True $ignoredRegistrationsAfter.Replace('\', '/').Contains($normalizedIgnoredCollisionWorktree) "Refused collision cleanup removed the worktree registration."

        Remove-Item -LiteralPath $ignoredCanaryDirectory -Recurse -Force
        $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "remove", $ignoredCollisionWorktree)

        foreach ($hiddenFlag in @("assume-unchanged", "skip-worktree")) {
            $hiddenIssue = if ($hiddenFlag -eq "assume-unchanged") { "503" } else { "504" }
            $hiddenResult = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", $hiddenIssue, "-Slug", "hidden-initializer-collision")
            Assert-Equal 0 $hiddenResult.ExitCode "Hidden-index initializer fixture creation should succeed for $hiddenFlag.`n$($hiddenResult.Output)"
            $hiddenWorktree = Join-Path $callerPath ".worktrees/codex-$hiddenIssue-hidden-initializer-collision"
            $hiddenHead = Invoke-Git -WorkingDirectory $hiddenWorktree -Arguments @("rev-parse", "HEAD")
            $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("branch", "issue-$hiddenIssue/hidden-initializer-collision", $hiddenHead)
            $hiddenTarget = Join-Path $hiddenWorktree "scripts/worktree_guard.ps1"
            Add-Content -LiteralPath $hiddenTarget -Value "`n# hidden initializer collision $hiddenFlag" -Encoding Ascii
            $null = Invoke-Git -WorkingDirectory $hiddenWorktree -Arguments @("update-index", "--$hiddenFlag", "--", "scripts/worktree_guard.ps1")
            $hiddenCollisionScript = Join-Path $fixtureRoot "initializer-$hiddenFlag-collision.ps1"
            Set-Content -LiteralPath $hiddenCollisionScript -Value @(Get-PrintedHandoffLines -Output $hiddenResult.Output) -Encoding Ascii
            $hiddenCollision = Invoke-ProcessCapture -FilePath $powerShellExecutable -Arguments @("-NoLogo", "-NoProfile", "-NonInteractive", "-File", $hiddenCollisionScript) -WorkingDirectory $hiddenWorktree
            Assert-True ($hiddenCollision.ExitCode -ne 0) "Hidden-index collision must fail for $hiddenFlag."
            Assert-NormalizedContains $hiddenCollision.Output "index-hidden entries" "Hidden-index collision must explain preservation for $hiddenFlag."
            Assert-Contains (Get-Content -Raw -LiteralPath $hiddenTarget) "hidden initializer collision $hiddenFlag" "Hidden bytes must survive $hiddenFlag cleanup refusal."
            Assert-True (Test-Path -LiteralPath $hiddenWorktree -PathType Container) "Hidden worktree must survive $hiddenFlag cleanup refusal."
            $hiddenRegistration = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
            Assert-True $hiddenRegistration.Replace('\', '/').Contains($hiddenWorktree.Replace('\', '/')) "Hidden registration must survive $hiddenFlag cleanup refusal."
            $null = Invoke-Git -WorkingDirectory $hiddenWorktree -Arguments @("update-index", "--no-$hiddenFlag", "--", "scripts/worktree_guard.ps1")
            $null = Invoke-Git -WorkingDirectory $hiddenWorktree -Arguments @("restore", "--worktree", "--", "scripts/worktree_guard.ps1")
            $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "remove", $hiddenWorktree)
        }

        $separateGitDirectory = Join-Path $fixtureRoot "separate common git directory"
        $separateGitCaller = Join-Path $fixtureRoot "separate git caller"
        $null = Invoke-Git -WorkingDirectory $fixtureRoot -Arguments @(
            "clone", "--separate-git-dir", $separateGitDirectory, "-b", "main", $remotePath, $separateGitCaller
        )
        $separateHelperResult = Invoke-Helper -WorkingDirectory $separateGitCaller -Arguments @("-IssueNumber", "495", "-Slug", "separate-git-cleanup")
        Assert-Equal 0 $separateHelperResult.ExitCode "Separate-Git-dir cleanup fixture creation should succeed.`n$($separateHelperResult.Output)"
        $separateWorktree = Join-Path $separateGitCaller ".worktrees/codex-495-separate-git-cleanup"
        $separateHead = Invoke-Git -WorkingDirectory $separateWorktree -Arguments @("rev-parse", "HEAD")
        $null = Invoke-Git -WorkingDirectory $separateGitCaller -Arguments @("branch", "issue-495/separate-git-cleanup", $separateHead)
        $separateRegistrationsBefore = Invoke-Git -WorkingDirectory $separateGitCaller -Arguments @("worktree", "list", "--porcelain")
        $normalizedSeparateWorktree = $separateWorktree.Replace('\', '/')
        Assert-True $separateRegistrationsBefore.Replace('\', '/').Contains($normalizedSeparateWorktree) "Separate-Git-dir fixture did not register the helper-created worktree."
        $separateCollisionScript = Join-Path $fixtureRoot "separate-git-branch-collision.ps1"
        Set-Content -LiteralPath $separateCollisionScript -Value @(Get-PrintedHandoffLines -Output $separateHelperResult.Output) -Encoding Ascii
        $separateCollision = Invoke-ProcessCapture -FilePath $powerShellExecutable -Arguments @("-NoLogo", "-NoProfile", "-NonInteractive", "-File", $separateCollisionScript) -WorkingDirectory $separateWorktree
        Assert-True ($separateCollision.ExitCode -ne 0) "A separate-Git-dir post-helper branch collision should fail the initializer."
        Assert-NormalizedContains $separateCollision.Output "removal of the unused helper-created worktree was scheduled" "Separate-Git-dir collision should report cleanup scheduling."
        for ($attempt = 0; $attempt -lt 100 -and (Test-Path -LiteralPath $separateWorktree); $attempt++) {
            Start-Sleep -Milliseconds 100
        }
        Assert-True (-not (Test-Path -LiteralPath $separateWorktree)) "Separate-Git-dir late collision must remove the helper-created worktree path."
        $separateRegistrationsAfter = Invoke-Git -WorkingDirectory $separateGitCaller -Arguments @("worktree", "list", "--porcelain")
        Assert-True (-not $separateRegistrationsAfter.Replace('\', '/').Contains($normalizedSeparateWorktree)) "Separate-Git-dir late collision must remove the worktree registration."
        Assert-True (Test-Path -LiteralPath $separateGitDirectory -PathType Container) "Separate-Git-dir cleanup removed the repository's common Git directory."

        $initializerResult = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "490", "-Slug", "initializer-validation-continued")
        Assert-Equal 0 $initializerResult.ExitCode "Post-collision initializer fixture worktree creation should succeed.`n$($initializerResult.Output)"
        $initializerSource = Get-Content -Raw -LiteralPath (Join-Path $callerPath "scripts/git/Initialize-CodexIssueWorktree.ps1")
        Assert-Contains $initializerSource 'ls-files", "-v", "-z' "Initializer must inspect hidden index flags before immediate late-collision cleanup."
        Assert-Contains $initializerSource 'cleanupHidden' "Delayed late-collision cleanup must inspect hidden index flags before plain removal."
        $initializerWorktree = Join-Path $callerPath ".worktrees/codex-490-initializer-validation-continued"
        $initializerHead = Invoke-Git -WorkingDirectory $initializerWorktree -Arguments @("rev-parse", "HEAD")

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
        Complete-Test "initializer removes only clean collision worktrees, preserves ignored content, and fails closed on invalid detached state or input"
    }

    if (Test-CaseSelected "existing-branch") {
        $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("branch", "issue-425/existing", "refs/remotes/origin/main")
        $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("branch", "namespace-ancestor", "refs/remotes/origin/main")
        $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("branch", "namespace-descendant/child", "refs/remotes/origin/main")
        $registrationsBeforeBranchCollisions = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
        $branchesBeforeBranchCollisions = Invoke-Git -WorkingDirectory $callerPath -Arguments @("for-each-ref", "--format=%(refname)", "--", "refs/heads/")
        $branchCollision = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "425", "-Slug", "existing")
        Assert-True ($branchCollision.ExitCode -ne 0) "Existing requested branch should fail closed."
        Assert-NormalizedContains $branchCollision.Output "Branch already exists: issue-425/existing" "Branch collision diagnostic was not clear."
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $callerPath ".worktrees/codex-425-existing"))) "Branch collision should not create a worktree."
        $ancestorCollision = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "477", "-Slug", "ancestor-collision", "-BranchName", "namespace-ancestor/child")
        Assert-True ($ancestorCollision.ExitCode -ne 0) "An existing ancestor branch should fail closed."
        Assert-NormalizedContains $ancestorCollision.Output "Branch namespace conflicts with existing branch 'namespace-ancestor': namespace-ancestor/child" "Ancestor branch collision diagnostic was not clear."
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $callerPath ".worktrees/codex-477-ancestor-collision"))) "Ancestor branch collision should not create a worktree."
        $descendantCollision = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "478", "-Slug", "descendant-collision", "-BranchName", "namespace-descendant")
        Assert-True ($descendantCollision.ExitCode -ne 0) "An existing descendant branch should fail closed."
        Assert-NormalizedContains $descendantCollision.Output "Branch namespace conflicts with existing branch 'namespace-descendant/child': namespace-descendant" "Descendant branch collision diagnostic was not clear."
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $callerPath ".worktrees/codex-478-descendant-collision"))) "Descendant branch collision should not create a worktree."
        $registrationsAfterBranchCollisions = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
        $branchesAfterBranchCollisions = Invoke-Git -WorkingDirectory $callerPath -Arguments @("for-each-ref", "--format=%(refname)", "--", "refs/heads/")
        Assert-Equal $registrationsBeforeBranchCollisions $registrationsAfterBranchCollisions "Branch namespace collision changed worktree registrations."
        Assert-Equal $branchesBeforeBranchCollisions $branchesAfterBranchCollisions "Branch namespace collision changed local refs."
        Complete-Test "existing branch and ref namespace collisions fail closed"
    }

    if (Test-CaseSelected "existing-path") {
        $pathCollisionTarget = Join-Path $callerPath ".worktrees/codex-426-path-collision"
        New-Item -ItemType Directory -Force -Path $pathCollisionTarget | Out-Null
        $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("switch", "-c", "occupied-preflight-base")
        Set-Content -LiteralPath (Join-Path $seedPath "tracked.txt") -Value "occupied preflight remote" -Encoding Ascii
        $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("add", "tracked.txt")
        $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("commit", "-m", "Advance occupied-target fixture")
        $occupiedRemoteBase = Invoke-Git -WorkingDirectory $seedPath -Arguments @("rev-parse", "HEAD")
        $occupiedStaleBase = Invoke-Git -WorkingDirectory $seedPath -Arguments @("rev-parse", "HEAD^")
        $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("push", "origin", "HEAD:refs/heads/occupied-preflight")
        $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("switch", "main")
        $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("update-ref", "refs/remotes/origin/occupied-preflight", $occupiedStaleBase)
        Assert-True ($occupiedRemoteBase -cne $occupiedStaleBase) "Occupied-target fixture should start with a stale tracking ref."

        $registrationsBeforePathCollision = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
        $refsBeforePathCollision = Invoke-Git -WorkingDirectory $callerPath -Arguments @("for-each-ref", "--format=%(refname):%(objectname)")
        $pathCollision = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "426", "-Slug", "path-collision", "-BaseBranch", "origin/occupied-preflight")
        Assert-True ($pathCollision.ExitCode -ne 0) "Existing target path should fail closed."
        Assert-NormalizedContains $pathCollision.Output "Worktree path already exists:" "Path collision diagnostic was not clear."
        $pathCollisionWhatIf = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "426", "-Slug", "path-collision", "-BaseBranch", "origin/occupied-preflight", "-WhatIf")
        Assert-True ($pathCollisionWhatIf.ExitCode -ne 0) "WhatIf must reject an occupied target instead of reporting success."
        Assert-NormalizedContains $pathCollisionWhatIf.Output "Worktree path already exists:" "WhatIf path-collision diagnostic was not clear."
        $registrationsAfterPathCollision = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
        $refsAfterPathCollision = Invoke-Git -WorkingDirectory $callerPath -Arguments @("for-each-ref", "--format=%(refname):%(objectname)")
        Assert-Equal $registrationsBeforePathCollision $registrationsAfterPathCollision "Normal or WhatIf occupied-target rejection changed worktree registrations."
        Assert-Equal $refsBeforePathCollision $refsAfterPathCollision "Normal or WhatIf occupied-target rejection changed Git refs."
        $occupiedTrackingAfter = Invoke-Git -WorkingDirectory $callerPath -Arguments @("rev-parse", "refs/remotes/origin/occupied-preflight")
        Assert-Equal $occupiedStaleBase $occupiedTrackingAfter "Occupied-target rejection refreshed the remote-tracking ref before failing."
        Complete-Test "existing target path fails before normal or WhatIf ref and registration mutation"
    }

    if (Test-CaseSelected "worktree-root-traversal") {
        $registrationsBefore = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
        $traversalTarget = Join-Path (Split-Path -Parent $callerPath) "escaped-worktrees/codex-435-root-traversal"
        $traversal = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "435", "-Slug", "root-traversal", "-WorktreeRoot", "../escaped-worktrees")
        Assert-True ($traversal.ExitCode -ne 0) "Traversal worktree root should fail closed."
        Assert-NormalizedContains $traversal.Output "Invalid worktree root: '../escaped-worktrees'." "Traversal-root diagnostic was not clear."
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
        Assert-NormalizedContains $rooted.Output "Invalid worktree root:" "Rooted-root diagnostic was not clear."
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
        Assert-NormalizedContains $unapproved.Output "Invalid worktree root: 'custom-worktrees'." "Unapproved-root diagnostic was not clear."
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
        Assert-NormalizedContains $caseVariant.Output "Invalid worktree root: '.WORKTREES'." "Case-variant root diagnostic was not clear."
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
        Assert-NormalizedContains $invalidSlug.Output "Invalid slug: 'Invalid-Slug'." "Invalid slug diagnostic was not clear."
        Assert-True (-not (Test-Path -LiteralPath $invalidSlugTarget)) "Invalid slug should not create a target path."

        $newlineSlug = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "447", "-Slug", "valid-slug`n")
        Assert-True ($newlineSlug.ExitCode -ne 0) "A slug ending in a line feed should fail closed."
        Assert-NormalizedContains $newlineSlug.Output "Invalid slug:" "Final-line-feed slug diagnostic was not clear."
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
        Assert-NormalizedContains $invalidBranch.Output "Invalid branch name: invalid branch" "Invalid branch diagnostic was not clear."
        Assert-True (-not (Test-Path -LiteralPath $invalidBranchTarget)) "Invalid branch should not create a target path."

        $windowsIncompatibleBranchCases = @(
            @{ IssueNumber = '470'; Slug = 'reserved-branch'; BranchName = 'issue-470/CON' },
            @{ IssueNumber = '471'; Slug = 'superscript-device'; BranchName = "issue-471/COM$([char]0x00B9)" },
            @{ IssueNumber = '472'; Slug = 'trailing-period'; BranchName = 'issue-472/trailing./leaf' },
            @{ IssueNumber = '473'; Slug = 'console-device'; BranchName = 'issue-473/CONIN$' },
            @{ IssueNumber = '474'; Slug = 'long-directory'; BranchName = "issue-474/$('a' * 256)/leaf" },
            @{ IssueNumber = '475'; Slug = 'long-lock'; BranchName = "issue-475/$('b' * 251)" }
        )
        foreach ($branchCase in $windowsIncompatibleBranchCases) {
            $branchTarget = Join-Path $callerPath ".worktrees/codex-$($branchCase.IssueNumber)-$($branchCase.Slug)"
            $branchResult = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", $branchCase.IssueNumber, "-Slug", $branchCase.Slug, "-BranchName", $branchCase.BranchName)
            Assert-True ($branchResult.ExitCode -ne 0) "Windows-incompatible branch '$($branchCase.BranchName)' should fail closed."
            Assert-NormalizedContains $branchResult.Output "Invalid branch name for Windows-compatible worktrees:" "Windows-incompatible branch diagnostic was not clear."
            Assert-True (-not (Test-Path -LiteralPath $branchTarget)) "Windows-incompatible branch '$($branchCase.BranchName)' should not create a target path."
            $branchRef = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/$($branchCase.BranchName)") -WorkingDirectory $callerPath
            Assert-Equal 1 $branchRef.ExitCode "Windows-incompatible branch '$($branchCase.BranchName)' created a shared ref."
        }
        $registrationsAfter = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
        Assert-Equal $registrationsBefore $registrationsAfter "Invalid branch changed Git worktree registrations."
        Complete-Test "invalid and Windows-incompatible branches fail closed without target or registration mutation"
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
        Assert-NormalizedContains $shimBypass.Output "Base commit not found: origin/not-there" "Helper did not bypass the unsafe Git batch shim."
        Assert-True (-not (Test-Path -LiteralPath $shimSentinel)) "Helper executed the PATH-first Git batch shim."
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $callerPath ".worktrees/codex-434-shim-bypass"))) "Shim-bypass probe should not create a worktree."
        Complete-Test "PATH-first Git batch shim is bypassed"
    }

    if (Test-CaseSelected "metachar-base") {
        $canaryPath = Join-Path $callerPath "git-shim-canary.txt"
        $metacharBase = "origin/main&echo TASKDECK_CANARY>git-shim-canary.txt"
        $metacharResult = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "433", "-Slug", "metachar-base", "-BaseBranch", $metacharBase)
        Assert-True ($metacharResult.ExitCode -ne 0) "Metacharacter base should fail closed."
        Assert-NormalizedContains $metacharResult.Output "Invalid remote branch in base:" "Metacharacter base diagnostic was not clear."
        Assert-True (-not (Test-Path -LiteralPath $canaryPath)) "Git shim metacharacters escaped the native argument boundary."
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $callerPath ".worktrees/codex-433-metachar-base"))) "Metacharacter base should not create a worktree."
        Complete-Test "metacharacter base cannot escape the native Git argument boundary"
    }

    if (Test-CaseSelected "revision-range-base") {
        $revisionRange = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "432", "-Slug", "revision-range", "-BaseBranch", "HEAD~1..HEAD")
        Assert-True ($revisionRange.ExitCode -ne 0) "Revision-set base should fail closed instead of selecting one commit."
        Assert-NormalizedContains $revisionRange.Output "Base commit not found: HEAD~1..HEAD" "Revision-set base diagnostic was not clear."
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
            Assert-NormalizedContains $linkedSource.Output "Run this helper from the repository's main checkout; linked source worktrees are not allowed" "Linked-source rejection should state the main-checkout-only contract."
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
        Assert-NormalizedContains $wrongHelperPath.Output "does not match the current repository's reviewed helper" "Wrong-checkout helper rejection should identify the exact path binding."
        Assert-True (-not (Test-Path -LiteralPath $wrongHelperTarget)) "Wrong-checkout helper rejection should not leave a target path."
        $wrongHelperBranch = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/issue-461/wrong-helper-checkout") -WorkingDirectory $callerPath
        Assert-Equal 1 $wrongHelperBranch.ExitCode "Wrong-checkout helper rejection should not create the planned branch."

        $oldCommitTarget = Join-Path $callerPath ".worktrees/codex-453-old-commit-base"
        $oldCommitBase = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "453", "-Slug", "old-commit-base", "-BaseBranch", $preHelperCommit)
        Assert-True ($oldCommitBase.ExitCode -ne 0) "A commit predating the handoff artifacts should fail closed."
        Assert-NormalizedContains $oldCommitBase.Output "does not contain required handoff artifact 'scripts/worktree_guard.ps1'" "Old-commit rejection should name the missing handoff artifact."
        Assert-True (-not (Test-Path -LiteralPath $oldCommitTarget)) "Rejected old commit should not leave a target path."
        $oldCommitBranch = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/issue-453/old-commit-base") -WorkingDirectory $callerPath
        Assert-Equal 1 $oldCommitBranch.ExitCode "Rejected old commit should not create the planned branch."

        $oldTagTarget = Join-Path $callerPath ".worktrees/codex-454-old-tag-base"
        $oldTagBase = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "454", "-Slug", "old-tag-base", "-BaseBranch", "pre-helper-base")
        Assert-True ($oldTagBase.ExitCode -ne 0) "A tag predating the handoff artifacts should fail closed."
        Assert-NormalizedContains $oldTagBase.Output "does not contain required handoff artifact 'scripts/worktree_guard.ps1'" "Old-tag rejection should name the missing handoff artifact."
        Assert-True (-not (Test-Path -LiteralPath $oldTagTarget)) "Rejected old tag should not leave a target path."
        $oldTagBranch = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/issue-454/old-tag-base") -WorkingDirectory $callerPath
        Assert-Equal 1 $oldTagBranch.ExitCode "Rejected old tag should not create the planned branch."

        $missingInitializerTarget = Join-Path $callerPath ".worktrees/codex-456-missing-initializer-base"
        $missingInitializerBase = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "456", "-Slug", "missing-initializer-base", "-BaseBranch", $guardOnlyCommit)
        Assert-True ($missingInitializerBase.ExitCode -ne 0) "A base containing the guard but predating the initializer should fail closed."
        Assert-NormalizedContains $missingInitializerBase.Output "does not contain required handoff artifact 'scripts/git/Initialize-CodexIssueWorktree.ps1'" "Guard-only base rejection should name the missing initializer artifact."
        Assert-True (-not (Test-Path -LiteralPath $missingInitializerTarget)) "Rejected guard-only base should not leave a target path."
        $missingInitializerBranch = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/issue-456/missing-initializer-base") -WorkingDirectory $callerPath
        Assert-Equal 1 $missingInitializerBranch.ExitCode "Rejected guard-only base should not create the planned branch."

        $modifiedArtifactTarget = Join-Path $callerPath ".worktrees/codex-457-modified-artifact-base"
        $modifiedArtifactBase = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "457", "-Slug", "modified-artifact-base", "-BaseBranch", $modifiedHandoffCommit)
        Assert-True ($modifiedArtifactBase.ExitCode -ne 0) "A base containing a different initializer blob should fail closed."
        Assert-NormalizedContains $modifiedArtifactBase.Output "handoff artifact 'scripts/git/Initialize-CodexIssueWorktree.ps1' does not match the reviewed artifact in the invoking checkout HEAD" "Modified-artifact rejection should name the initializer trust mismatch."
        Assert-True (-not (Test-Path -LiteralPath $modifiedArtifactTarget)) "Rejected modified-artifact base should not leave a target path."
        $modifiedArtifactBranch = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/issue-457/modified-artifact-base") -WorkingDirectory $callerPath
        Assert-Equal 1 $modifiedArtifactBranch.ExitCode "Rejected modified-artifact base should not create the planned branch."

        $modifiedGuardTarget = Join-Path $callerPath ".worktrees/codex-464-modified-guard-base"
        $modifiedGuardBase = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "464", "-Slug", "modified-guard-base", "-BaseBranch", $modifiedGuardCommit)
        Assert-True ($modifiedGuardBase.ExitCode -ne 0) "A base containing a different guard blob should fail closed."
        Assert-NormalizedContains $modifiedGuardBase.Output "handoff artifact 'scripts/worktree_guard.ps1' does not match the reviewed artifact in the invoking checkout HEAD" "Modified-guard rejection should name the guard trust mismatch."
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
            Assert-NormalizedContains $dirtySource.Output "Reviewed handoff artifact 'scripts/git/Initialize-CodexIssueWorktree.ps1' working content does not match the invoking checkout HEAD blob" "Dirty source-artifact rejection should identify the uncommitted initializer."
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
            Assert-NormalizedContains $stagedSource.Output "Reviewed handoff artifact 'scripts/git/Initialize-CodexIssueWorktree.ps1' has staged changes" "Staged source-artifact rejection should identify the indexed initializer."
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
            Assert-NormalizedContains $hiddenSource.Output "Reviewed handoff artifact 'scripts/git/Initialize-CodexIssueWorktree.ps1' working content does not match the invoking checkout HEAD blob" "Direct source hashing should identify the hidden initializer mismatch."
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
            Assert-NormalizedContains $filterSource.Output "Reviewed handoff artifact 'scripts/git/Initialize-CodexIssueWorktree.ps1' working content does not match the invoking checkout HEAD blob" "Filter-free byte comparison should identify the altered initializer."
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
            Assert-NormalizedContains $dirtyGuard.Output "Reviewed handoff artifact 'scripts/worktree_guard.ps1' working content does not match the invoking checkout HEAD blob" "Dirty source-guard rejection should identify the altered guard."
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
            Assert-NormalizedContains $missingGuard.Output "Reviewed handoff artifact 'scripts/worktree_guard.ps1' is missing from the invoking checkout" "Missing source-guard rejection should identify the absent guard."
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
            Assert-NormalizedContains $dirtyHelper.Output "Reviewed handoff artifact 'scripts/git/New-CodexIssueWorktree.ps1' working content does not match the invoking checkout HEAD blob" "Dirty helper rejection should identify the uncommitted helper itself."
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
            Assert-NormalizedContains $missingSource.Output "Reviewed handoff artifact 'scripts/git/Initialize-CodexIssueWorktree.ps1' is missing from the invoking checkout" "Missing source-artifact rejection should identify the absent initializer."
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

    if (Test-CaseSelected "target-artifact-smudge") {
        Set-Content -LiteralPath (Join-Path $seedPath ".gitattributes") -Encoding Ascii -Value "scripts/worktree_guard.ps1 filter=taskdeck-target-smudge"
        $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("add", ".gitattributes")
        $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("commit", "-m", "Smudge target guard fixture")
        $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("push", "origin", "HEAD:refs/heads/target-smudge")
        $registrationsBeforeTargetSmudge = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
        $targetSmudgeTarget = Join-Path $callerPath ".worktrees/codex-473-target-smudge"
        $cleanupTracePath = Join-Path $fixtureRoot "target-smudge-cleanup-trace.json"
        $previousTrace2Event = [Environment]::GetEnvironmentVariable("GIT_TRACE2_EVENT", [EnvironmentVariableTarget]::Process)
        $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("config", "filter.taskdeck-target-smudge.clean", "git hash-object --stdin")
        $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("config", "filter.taskdeck-target-smudge.smudge", "git hash-object --stdin")
        try {
            [Environment]::SetEnvironmentVariable("GIT_TRACE2_EVENT", $cleanupTracePath, [EnvironmentVariableTarget]::Process)
            $targetSmudge = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "473", "-Slug", "target-smudge", "-BaseBranch", "origin/target-smudge")
            Assert-True ($targetSmudge.ExitCode -ne 0) "A smudged target guard should fail before the helper emits worker commands."
            Assert-NormalizedContains $targetSmudge.Output "Helper-created worktree handoff artifact 'scripts/worktree_guard.ps1' does not match the reviewed raw blob" "Target smudge rejection should name the reviewed raw-blob mismatch."
            Assert-True (-not (Test-Path -LiteralPath $targetSmudgeTarget)) "Target smudge rejection must remove the helper-created worktree path.`n$($targetSmudge.Output)"
            $registrationsAfterTargetSmudge = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
            Assert-Equal $registrationsBeforeTargetSmudge $registrationsAfterTargetSmudge "Target smudge rejection must remove the helper-created worktree registration."

            $cleanupTraceEvents = @(
                Get-Content -LiteralPath $cleanupTracePath |
                    ForEach-Object { $_ | ConvertFrom-Json }
            )
            $cleanupRemoveEvents = @(
                $cleanupTraceEvents | Where-Object {
                    $traceArguments = @($_.argv)
                    $worktreeArgumentIndex = [Array]::IndexOf($traceArguments, "worktree")
                    $worktreeArgumentIndex -ge 0 -and
                        $worktreeArgumentIndex -eq ($traceArguments.Count - 3) -and
                        $traceArguments[$worktreeArgumentIndex + 1] -ceq "remove" -and
                        $traceArguments[$worktreeArgumentIndex + 2] -ceq $targetSmudgeTarget
                }
            )
            Assert-Equal 1 $cleanupRemoveEvents.Count "Target-smudge cleanup did not issue exactly one plain worktree removal with the expected target."
            Assert-True (@($cleanupRemoveEvents[0].argv) -notcontains "-f" -and @($cleanupRemoveEvents[0].argv) -notcontains "--force") "Target-smudge cleanup must never pass -f or --force to git worktree remove."
        }
        finally {
            [Environment]::SetEnvironmentVariable("GIT_TRACE2_EVENT", $previousTrace2Event, [EnvironmentVariableTarget]::Process)
            $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("config", "--unset", "filter.taskdeck-target-smudge.clean")
            $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("config", "--unset", "filter.taskdeck-target-smudge.smudge")
        }

        Set-Content -LiteralPath (Join-Path $seedPath ".gitattributes") -Encoding Ascii -Value "scripts/worktree_guard.ps1 filter=taskdeck-target-smudge-unexpected"
        $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("add", ".gitattributes")
        $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("commit", "-m", "Add unexpected smudge dirt fixture")
        $null = Invoke-Git -WorkingDirectory $seedPath -Arguments @("push", "origin", "HEAD:refs/heads/target-smudge-unexpected")
        $unexpectedFilterScript = Join-Path $testRoot "unexpected-smudge-filter.ps1"
        Set-Content -LiteralPath $unexpectedFilterScript -Encoding Ascii -Value @(
            '$inputStream = [Console]::OpenStandardInput()',
            '$memory = [System.IO.MemoryStream]::new()',
            '$inputStream.CopyTo($memory)',
            '[System.IO.File]::WriteAllText((Join-Path (Get-Location) ''unexpected-cleanup.txt''), ''unexpected'')',
            '$output = [Text.Encoding]::ASCII.GetBytes(''SMUDGED'')',
            '$outputStream = [Console]::OpenStandardOutput()',
            '$outputStream.Write($output, 0, $output.Length)'
        )
        $filterPowerShell = $powerShellExecutable.Replace('\', '/')
        $filterScriptArgument = $unexpectedFilterScript.Replace('\', '/')
        $unexpectedFilterCommand = "`"$filterPowerShell`" -NoLogo -NoProfile -NonInteractive -File `"$filterScriptArgument`""
        $unexpectedTarget = Join-Path $callerPath ".worktrees/codex-496-target-smudge-unexpected"
        $unexpectedRegistrationsBefore = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
        $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("config", "filter.taskdeck-target-smudge-unexpected.clean", $unexpectedFilterCommand)
        $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("config", "filter.taskdeck-target-smudge-unexpected.smudge", $unexpectedFilterCommand)
        try {
            $unexpectedSmudge = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "496", "-Slug", "target-smudge-unexpected", "-BaseBranch", "origin/target-smudge-unexpected")
            Assert-True ($unexpectedSmudge.ExitCode -ne 0) "Unexpected target dirt should fail handoff verification and cleanup."
            Assert-NormalizedContains $unexpectedSmudge.Output "with unexpected dirt: ?? unexpected-cleanup.txt" "Unexpected target dirt should be named by the fail-closed cleanup diagnostic."
            Assert-True (Test-Path -LiteralPath $unexpectedTarget -PathType Container) "Fail-closed cleanup must preserve a helper-created worktree containing unexpected dirt."
            Assert-True (Test-Path -LiteralPath (Join-Path $unexpectedTarget "unexpected-cleanup.txt") -PathType Leaf) "Unexpected-dirt fixture did not create its untracked cleanup canary."
            $unexpectedRegistrationsAfter = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
            Assert-True ($unexpectedRegistrationsAfter -cne $unexpectedRegistrationsBefore) "Fail-closed unexpected-dirt cleanup should preserve the worktree registration."

            Remove-Item -LiteralPath (Join-Path $unexpectedTarget "unexpected-cleanup.txt") -Force
            $null = Invoke-Git -WorkingDirectory $unexpectedTarget -Arguments @("update-index", "--skip-worktree", "--", "scripts/worktree_guard.ps1")
            $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "remove", $unexpectedTarget)
        }
        finally {
            $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("config", "--unset", "filter.taskdeck-target-smudge-unexpected.clean")
            $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("config", "--unset", "filter.taskdeck-target-smudge-unexpected.smudge")
        }
        Complete-Test "dirty handoff artifacts are narrowly neutralized without force while unexpected dirt fails closed"
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
        Assert-NormalizedContains $missingBase.Output "Base commit not found: origin/not-there" "Missing-base diagnostic was not clear."
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
        Assert-NormalizedContains $missingLocalWhatIf.Output "Base commit not found: refs/heads/definitely-missing-what-if" "Missing local WhatIf base diagnostic was not clear."
        Assert-True (-not (Test-Path -LiteralPath $missingLocalWhatIfTarget)) "Missing local WhatIf base created a target."

        $missingRemoteWhatIfTarget = Join-Path $callerPath ".worktrees/codex-452-what-if-remote-missing"
        $missingRemoteWhatIf = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "452", "-Slug", "what-if-remote-missing", "-BaseBranch", "origin/definitely-missing-what-if", "-WhatIf")
        Assert-True ($missingRemoteWhatIf.ExitCode -ne 0) "WhatIf should fail when an explicit remote base does not exist."
        Assert-NormalizedContains $missingRemoteWhatIf.Output "Base commit not found: origin/definitely-missing-what-if" "Missing remote WhatIf base diagnostic was not clear."
        Assert-True (-not (Test-Path -LiteralPath $missingRemoteWhatIfTarget)) "Missing remote WhatIf base created a target."
        $whatIfRegistrationsAfter = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
        $whatIfRefsAfter = Invoke-Git -WorkingDirectory $callerPath -Arguments @("for-each-ref", "--format=%(refname):%(objectname)")
        Assert-Equal $whatIfRegistrationsBefore $whatIfRegistrationsAfter "Missing-base WhatIf probes changed Git worktree registrations."
        Assert-Equal $whatIfRefsBefore $whatIfRefsAfter "Missing-base WhatIf probes changed Git refs."
        Complete-Test "WhatIf validates local and remote bases without worktree, branch, or ref mutation"
    }

    if (Test-CaseSelected "git-add-failure") {
        $timeoutHookDirectory = Join-Path $testRoot "worktree-add-timeout-hook"
        New-Item -ItemType Directory -Path $timeoutHookDirectory | Out-Null
        $timeoutHookPath = Join-Path $timeoutHookDirectory "post-checkout"
        $timeoutHookStartedPath = Join-Path $timeoutHookDirectory "started.txt"
        $timeoutHookStartedArgument = $timeoutHookStartedPath.Replace('\', '/')
        $timeoutGitArgument = $gitExecutable.Replace('\', '/')
        $timeoutHookContent = @'
#!/bin/sh
printf started > "__STARTED_PATH__"
if [ "$TASKDECK_DIRTY_TIMEOUT_HOOK" = "1" ]; then
    printf '\n# dirty timeout hook canary\n' >> scripts/worktree_guard.ps1
fi
if [ "$TASKDECK_HIDDEN_TIMEOUT_HOOK" = "assume" ]; then
    "__GIT_EXECUTABLE__" update-index --assume-unchanged -- scripts/worktree_guard.ps1
    printf '\n# hidden timeout hook canary assume\n' >> scripts/worktree_guard.ps1
fi
if [ "$TASKDECK_HIDDEN_TIMEOUT_HOOK" = "skip" ]; then
    "__GIT_EXECUTABLE__" update-index --skip-worktree -- scripts/worktree_guard.ps1
    printf '\n# hidden timeout hook canary skip\n' >> scripts/worktree_guard.ps1
fi
sleep 30
'@.Replace('__STARTED_PATH__', $timeoutHookStartedArgument).Replace('__GIT_EXECUTABLE__', $timeoutGitArgument)
        [System.IO.File]::WriteAllText(
            $timeoutHookPath,
            $timeoutHookContent,
            [System.Text.UTF8Encoding]::new($false))
        $timedOutWorktree = Join-Path $callerPath ".worktrees/codex-499-git-add-timeout"
        $timedOutRegistrationsBefore = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
        $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("config", "core.hooksPath", $timeoutHookDirectory)
        try {
            $timedOutAdd = Invoke-Helper -WorkingDirectory $callerPath -Arguments @(
                "-IssueNumber", "499",
                "-Slug", "git-add-timeout",
                "-GitCommandTimeoutSeconds", "5"
            )
            Assert-True ($timedOutAdd.ExitCode -ne 0) "A timed-out git worktree add must fail closed."
            Assert-NormalizedContains $timedOutAdd.Output "git worktree add failed for" "Timed-out worktree-add diagnostic omitted the failed operation."
            $normalizedTimedOutAdd = $timedOutAdd.Output -replace '\s+', ' '
            $reportedBoundedTermination =
                $normalizedTimedOutAdd.Contains("Git command timed out after 5 seconds; its helper-owned process tree was terminated and reaped.") -or
                $normalizedTimedOutAdd.Contains("Git stderr drain did not finish within 5000 ms after its process exited.")
            Assert-True $reportedBoundedTermination "Timed-out worktree-add diagnostic omitted its bounded termination result."
            Assert-True (Test-Path -LiteralPath $timeoutHookStartedPath -PathType Leaf) "Post-checkout timeout fixture did not start."
            Assert-True (-not (Test-Path -LiteralPath $timedOutWorktree)) "Timed-out worktree add left its populated target behind."
            $timedOutRegistrationsAfter = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
            Assert-Equal $timedOutRegistrationsBefore $timedOutRegistrationsAfter "Timed-out worktree add left stale registration metadata."
        }
        finally {
            $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("config", "--unset", "core.hooksPath")
        }

        $dirtyTimedOutWorktree = Join-Path $callerPath ".worktrees/codex-500-dirty-git-add-timeout"
        $dirtyTimedOutRegistrationsBefore = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
        $previousDirtyTimeoutHook = [System.Environment]::GetEnvironmentVariable("TASKDECK_DIRTY_TIMEOUT_HOOK", "Process")
        [System.Environment]::SetEnvironmentVariable("TASKDECK_DIRTY_TIMEOUT_HOOK", "1", "Process")
        $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("config", "core.hooksPath", $timeoutHookDirectory)
        try {
            $dirtyTimedOutAdd = Invoke-Helper -WorkingDirectory $callerPath -Arguments @(
                "-IssueNumber", "500",
                "-Slug", "dirty-git-add-timeout",
                "-GitCommandTimeoutSeconds", "5"
            )
            Assert-True ($dirtyTimedOutAdd.ExitCode -ne 0) "A dirty timed-out git worktree add must fail closed."
            Assert-NormalizedContains $dirtyTimedOutAdd.Output "Refusing to remove partially created worktree" "Dirty partial-registration cleanup did not explain its preservation decision."
            Assert-True (Test-Path -LiteralPath $dirtyTimedOutWorktree -PathType Container) "Dirty partial-registration cleanup deleted the populated target."
            $dirtyGuardPath = Join-Path $dirtyTimedOutWorktree "scripts/worktree_guard.ps1"
            Assert-NormalizedContains (Get-Content -Raw -LiteralPath $dirtyGuardPath) "dirty timeout hook canary" "Dirty timeout hook did not modify the preservation canary."
            $dirtyTimedOutRegistrationsAfter = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
            Assert-True ($dirtyTimedOutRegistrationsAfter -cne $dirtyTimedOutRegistrationsBefore) "Dirty partial-registration cleanup removed the worktree registration."
            Assert-NormalizedContains $dirtyTimedOutRegistrationsAfter ($dirtyTimedOutWorktree.Replace('\', '/')) "Dirty partial-registration cleanup did not preserve the exact registration."
        }
        finally {
            [System.Environment]::SetEnvironmentVariable("TASKDECK_DIRTY_TIMEOUT_HOOK", $previousDirtyTimeoutHook, "Process")
            $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("config", "--unset", "core.hooksPath")
            if (Test-Path -LiteralPath $dirtyTimedOutWorktree -PathType Container) {
                $null = Invoke-Git -WorkingDirectory $dirtyTimedOutWorktree -Arguments @("restore", "--worktree", "--", "scripts/worktree_guard.ps1")
                $null = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("worktree", "unlock", $dirtyTimedOutWorktree) -WorkingDirectory $callerPath
                $dirtyCleanup = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("worktree", "remove", $dirtyTimedOutWorktree) -WorkingDirectory $callerPath
                Assert-Equal 0 $dirtyCleanup.ExitCode "Dirty timeout fixture cleanup failed after its preservation proof.`n$($dirtyCleanup.Output)"
            }
        }

        $previousHiddenTimeoutHook = [System.Environment]::GetEnvironmentVariable("TASKDECK_HIDDEN_TIMEOUT_HOOK", "Process")
        $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("config", "core.hooksPath", $timeoutHookDirectory)
        try {
            $hiddenTimeoutScenarios = @(
                [pscustomobject]@{ Flag = "assume"; Issue = "501"; Slug = "assume-hidden-git-add-timeout"; ClearArgument = "--no-assume-unchanged" },
                [pscustomobject]@{ Flag = "skip"; Issue = "502"; Slug = "skip-hidden-git-add-timeout"; ClearArgument = "--no-skip-worktree" }
            )
            foreach ($scenario in $hiddenTimeoutScenarios) {
                $hiddenTimedOutWorktree = Join-Path $callerPath ".worktrees/codex-$($scenario.Issue)-$($scenario.Slug)"
                $hiddenTimedOutRegistrationsBefore = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
                [System.Environment]::SetEnvironmentVariable("TASKDECK_HIDDEN_TIMEOUT_HOOK", $scenario.Flag, "Process")
                if (Test-Path -LiteralPath $timeoutHookStartedPath -PathType Leaf) {
                    Remove-Item -LiteralPath $timeoutHookStartedPath -Force
                }
                try {
                    $hiddenTimedOutAdd = Invoke-Helper -WorkingDirectory $callerPath -Arguments @(
                        "-IssueNumber", $scenario.Issue,
                        "-Slug", $scenario.Slug,
                        "-GitCommandTimeoutSeconds", "5"
                    )
                    Assert-True ($hiddenTimedOutAdd.ExitCode -ne 0) "An index-hidden dirty timed-out git worktree add must fail closed."
                    Assert-NormalizedContains $hiddenTimedOutAdd.Output "index contains assume-unchanged or skip-worktree entries that can hide modified data" "Index-hidden partial-registration cleanup did not explain its preservation decision."
                    Assert-True (Test-Path -LiteralPath $timeoutHookStartedPath -PathType Leaf) "Index-hidden timeout fixture did not start."
                    Assert-True (Test-Path -LiteralPath $hiddenTimedOutWorktree -PathType Container) "Index-hidden partial-registration cleanup deleted the populated target."
                    $hiddenGuardPath = Join-Path $hiddenTimedOutWorktree "scripts/worktree_guard.ps1"
                    Assert-NormalizedContains (Get-Content -Raw -LiteralPath $hiddenGuardPath) "hidden timeout hook canary $($scenario.Flag)" "Index-hidden timeout hook did not preserve its modified bytes."
                    $hiddenTimedOutRegistrationsAfter = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "list", "--porcelain")
                    Assert-True ($hiddenTimedOutRegistrationsAfter -cne $hiddenTimedOutRegistrationsBefore) "Index-hidden partial-registration cleanup removed the worktree registration."
                    Assert-NormalizedContains $hiddenTimedOutRegistrationsAfter ($hiddenTimedOutWorktree.Replace('\', '/')) "Index-hidden partial-registration cleanup did not preserve the exact registration."
                }
                finally {
                    if (Test-Path -LiteralPath $hiddenTimedOutWorktree -PathType Container) {
                        $null = Invoke-Git -WorkingDirectory $hiddenTimedOutWorktree -Arguments @("update-index", $scenario.ClearArgument, "--", "scripts/worktree_guard.ps1")
                        $null = Invoke-Git -WorkingDirectory $hiddenTimedOutWorktree -Arguments @("restore", "--worktree", "--", "scripts/worktree_guard.ps1")
                        $null = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("worktree", "unlock", $hiddenTimedOutWorktree) -WorkingDirectory $callerPath
                        $hiddenCleanup = Invoke-ProcessCapture -FilePath $gitExecutable -Arguments @("worktree", "remove", $hiddenTimedOutWorktree) -WorkingDirectory $callerPath
                        Assert-Equal 0 $hiddenCleanup.ExitCode "Index-hidden timeout fixture cleanup failed after its preservation proof.`n$($hiddenCleanup.Output)"
                    }
                }
            }
        }
        finally {
            [System.Environment]::SetEnvironmentVariable("TASKDECK_HIDDEN_TIMEOUT_HOOK", $previousHiddenTimeoutHook, "Process")
            $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("config", "--unset", "core.hooksPath")
        }

        $gitFailureTarget = Join-Path $callerPath ".worktrees/codex-431-git-failure"
        $null = Invoke-Git -WorkingDirectory $callerPath -Arguments @("worktree", "add", "--detach", $gitFailureTarget, "refs/remotes/origin/main")
        $parkedGitFailureTarget = Join-Path $callerPath ".worktrees/parked-git-failure"
        Move-Item -LiteralPath $gitFailureTarget -Destination $parkedGitFailureTarget
        $gitFailure = Invoke-Helper -WorkingDirectory $callerPath -Arguments @("-IssueNumber", "431", "-Slug", "git-failure")
        Assert-True ($gitFailure.ExitCode -ne 0) "Native git worktree failure should fail closed."
        Assert-NormalizedContains $gitFailure.Output "git worktree add failed for" "Git failure diagnostic omitted the failed operation."
        Assert-NormalizedContains $gitFailure.Output "is a missing but already registered worktree" "Git stderr context should be preserved."
        Assert-NormalizedContains $gitFailure.Output "(exit code 128)" "Git failure diagnostic omitted the native exit code."
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $callerPath ".worktrees/codex-431-git-failure"))) "Failed git worktree add should not leave a target worktree."
        Complete-Test "native git failures and post-registration timeouts clean up safely with clear diagnostics"
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
