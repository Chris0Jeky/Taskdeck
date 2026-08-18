[CmdletBinding(DefaultParameterSetName = "Run")]
param(
    [Parameter(Mandatory = $true, ParameterSetName = "Run")]
    [ValidateNotNullOrEmpty()]
    [string[]]$Command,

    [Parameter(ParameterSetName = "Run")]
    [switch]$ValidateOnly,

    [Parameter(Mandatory = $true, ParameterSetName = "SelfTest")]
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Deny-InventoryCommand {
    param([Parameter(Mandatory = $true)][string]$Reason)

    throw "Read-only inventory command denied: $Reason"
}

function Assert-NoShellControlTokens {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    $controlTokens = @("&", "&&", "|", "||", ";", ">", ">>", "<", "<<", "2>", "2>>")
    foreach ($argument in $Arguments) {
        if ($argument.IndexOf([char]0) -ge 0 -or $argument.Contains("`r") -or $argument.Contains("`n")) {
            Deny-InventoryCommand "arguments cannot contain NUL or newline characters"
        }
        if ($controlTokens -contains $argument) {
            Deny-InventoryCommand "shell control token '$argument' is not an argv value"
        }
    }
}

function Assert-GitReadCommand {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    if ($Arguments.Count -eq 0) {
        Deny-InventoryCommand "git requires an allowlisted read subcommand"
    }

    $subcommand = $Arguments[0].ToLowerInvariant()
    $simpleReadCommands = @(
        "cat-file",
        "describe",
        "diff",
        "for-each-ref",
        "grep",
        "log",
        "ls-files",
        "ls-remote",
        "ls-tree",
        "merge-base",
        "name-rev",
        "rev-parse",
        "show",
        "status"
    )

    if ($subcommand -eq "worktree") {
        if ($Arguments.Count -lt 2 -or $Arguments[1].ToLowerInvariant() -ne "list") {
            Deny-InventoryCommand "only 'git worktree list' is allowed"
        }
    }
    elseif ($simpleReadCommands -notcontains $subcommand) {
        Deny-InventoryCommand "git subcommand '$subcommand' is not allowlisted"
    }

    foreach ($argument in $Arguments) {
        $normalized = $argument.ToLowerInvariant()
        if (
            $normalized -eq "--output" -or
            $normalized.StartsWith("--output=") -or
            $normalized -eq "--ext-diff" -or
            $normalized -eq "--textconv" -or
            $normalized -eq "--exec" -or
            $normalized.StartsWith("--exec=") -or
            $normalized -eq "--upload-pack" -or
            $normalized.StartsWith("--upload-pack=") -or
            $normalized -eq "--filters" -or
            $normalized.StartsWith("--filters=") -or
            $normalized -eq "--batch-command" -or
            $normalized -eq "--open-files-in-pager" -or
            $normalized.StartsWith("--open-files-in-pager=") -or
            $argument -ceq "-O" -or
            $argument -cmatch "^-O.+" -or
            $argument -match "^[A-Za-z][A-Za-z0-9+.-]*::"
        ) {
            Deny-InventoryCommand "git option '$argument' can write or execute outside the read boundary"
        }
    }
}

function Get-GhApiMethod {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    $method = $null
    for ($index = 0; $index -lt $Arguments.Count; $index++) {
        $argument = $Arguments[$index]
        if ($argument -in @("--method", "-X")) {
            if ($index + 1 -ge $Arguments.Count) {
                Deny-InventoryCommand "gh api method flag requires a value"
            }
            $method = $Arguments[$index + 1]
            $index++
            continue
        }
        if ($argument -match "^(?:--method|-X)=(.+)$") {
            $method = $Matches[1]
        }
    }

    return $method
}

function Get-GraphQlQueryText {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    $queryText = $null
    for ($index = 1; $index -lt $Arguments.Count; $index++) {
        $argument = $Arguments[$index]
        if ($argument -in @("-f", "--raw-field", "-F", "--field")) {
            if ($index + 1 -ge $Arguments.Count) {
                Deny-InventoryCommand "gh api graphql field flag requires a value"
            }
            $field = $Arguments[$index + 1]
            if ($field.StartsWith("query=")) {
                if ($null -ne $queryText) {
                    Deny-InventoryCommand "gh api graphql requires exactly one inline query field"
                }
                $queryText = $field.Substring("query=".Length)
            }
            $index++
            continue
        }
        if ($argument -match "^(?:-f|--raw-field|-F|--field)=query=(.*)$") {
            if ($null -ne $queryText) {
                Deny-InventoryCommand "gh api graphql requires exactly one inline query field"
            }
            $queryText = $Matches[1]
        }
    }

    if ([string]::IsNullOrWhiteSpace($queryText)) {
        Deny-InventoryCommand "gh api graphql requires an inspectable inline query= field"
    }
    if ($queryText.StartsWith("@")) {
        Deny-InventoryCommand "gh api graphql cannot load an uninspected query from a file"
    }
    if ($queryText -match "(?i)(?<![A-Za-z0-9_])(mutation|subscription)(?![A-Za-z0-9_])") {
        Deny-InventoryCommand "GraphQL mutations and subscriptions are not read-only inventory"
    }

    return $queryText
}

function Assert-GhApiReadCommand {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    if ($Arguments.Count -eq 0) {
        Deny-InventoryCommand "gh api requires an endpoint"
    }

    for ($index = 0; $index -lt $Arguments.Count; $index++) {
        $argument = $Arguments[$index]
        if ($argument -cmatch "^-(?:X|f|F).+") {
            Deny-InventoryCommand "gh api attached short options are ambiguous; pass -X, -f, or -F and its value as separate argv"
        }
        if ($argument -eq "--input" -or $argument.StartsWith("--input=")) {
            Deny-InventoryCommand "gh api --input can disclose a local file and is not allowed"
        }
        if ($argument -eq "--cache" -or $argument.StartsWith("--cache=")) {
            Deny-InventoryCommand "gh api --cache writes local state and is not allowed"
        }
        if ($argument -in @("-F", "--field")) {
            if ($index + 1 -ge $Arguments.Count) {
                Deny-InventoryCommand "gh api typed field flag requires a value"
            }
            if ($Arguments[$index + 1] -match "^[^=]+=@") {
                Deny-InventoryCommand "gh api typed fields cannot read values from local files"
            }
            $index++
            continue
        }
        if ($argument -match "^(?:-F|--field)=(?:[^=]+)=@") {
            Deny-InventoryCommand "gh api typed fields cannot read values from local files"
        }
    }

    $isGraphQl = $Arguments[0].ToLowerInvariant() -eq "graphql"
    $method = Get-GhApiMethod -Arguments $Arguments
    if ($isGraphQl) {
        if ($null -ne $method -and $method.ToUpperInvariant() -ne "POST") {
            Deny-InventoryCommand "GraphQL inventory uses query-only POST transport"
        }
        [void](Get-GraphQlQueryText -Arguments $Arguments)
        return
    }

    $hasFields = $false
    foreach ($argument in $Arguments) {
        if ($argument -in @("-f", "--raw-field", "-F", "--field")) {
            $hasFields = $true
        }
        if ($argument -match "^(?:-f|--raw-field|-F|--field)=") {
            $hasFields = $true
        }
    }

    if ($null -eq $method) {
        if ($hasFields) {
            Deny-InventoryCommand "REST fields change gh api's default to POST; pass --method GET explicitly"
        }
        $method = "GET"
    }
    if ($method.ToUpperInvariant() -ne "GET") {
        Deny-InventoryCommand "REST method '$method' is not read-only"
    }
}

function Assert-GhReadCommand {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    if ($Arguments.Count -eq 0) {
        Deny-InventoryCommand "gh requires an allowlisted read subcommand"
    }
    foreach ($argument in $Arguments) {
        if ($argument -eq "--web" -or $argument.StartsWith("--web=") -or $argument -eq "-w" -or $argument.StartsWith("-w=")) {
            Deny-InventoryCommand "--web/-w launches an external interactive surface"
        }
    }

    $group = $Arguments[0].ToLowerInvariant()
    if ($group -eq "api") {
        Assert-GhApiReadCommand -Arguments @($Arguments | Select-Object -Skip 1)
        return
    }
    if ($group -eq "status") {
        return
    }

    if ($Arguments.Count -lt 2) {
        Deny-InventoryCommand "gh group '$group' requires an allowlisted read action"
    }
    $action = $Arguments[1].ToLowerInvariant()
    $allowedActions = @{
        "cache" = @("list")
        "issue" = @("list", "status", "view")
        "label" = @("list")
        "pr" = @("checks", "diff", "list", "status", "view")
        "project" = @("field-list", "item-list", "list", "view")
        "release" = @("list", "view")
        "repo" = @("list", "view")
        "run" = @("list", "view", "watch")
        "search" = @("code", "commits", "issues", "prs", "repos")
        "workflow" = @("list", "view")
    }

    if (-not $allowedActions.ContainsKey($group) -or $allowedActions[$group] -notcontains $action) {
        Deny-InventoryCommand "gh action '$group $action' is not allowlisted"
    }
}

function Assert-ReadOnlyInventoryCommand {
    param([Parameter(Mandatory = $true)][string[]]$CommandTokens)

    if ($CommandTokens.Count -lt 2) {
        Deny-InventoryCommand "pass a tool and an allowlisted read subcommand"
    }
    Assert-NoShellControlTokens -Arguments $CommandTokens

    $tool = $CommandTokens[0].ToLowerInvariant()
    $arguments = @($CommandTokens | Select-Object -Skip 1)
    switch ($tool) {
        "git" { Assert-GitReadCommand -Arguments $arguments }
        "gh" { Assert-GhReadCommand -Arguments $arguments }
        default { Deny-InventoryCommand "tool '$tool' is not allowed; use only git or gh" }
    }
}

function Resolve-InventoryExecutable {
    param([Parameter(Mandatory = $true)][string]$Tool)

    $isWindows = [System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT
    $command = Get-Command $Tool -CommandType Application -All -ErrorAction SilentlyContinue |
        Where-Object {
            $extension = [System.IO.Path]::GetExtension($_.Source)
            $extension -notin @(".cmd", ".bat") -and (-not $isWindows -or $extension -eq ".exe")
        } |
        Select-Object -First 1
    if ($null -eq $command) {
        throw "No argv-safe $Tool executable was found on PATH."
    }
    return $command.Source
}

function Invoke-ValidatedInventoryCommand {
    param(
        [Parameter(Mandatory = $true)][string[]]$CommandTokens,
        [Parameter(Mandatory = $true)][scriptblock]$Launcher
    )

    Assert-ReadOnlyInventoryCommand -CommandTokens $CommandTokens
    $tool = $CommandTokens[0].ToLowerInvariant()
    $arguments = @($CommandTokens | Select-Object -Skip 1)
    & $Launcher $tool $arguments
}

function Invoke-ReadOnlyInventorySelfTest {
    $state = [pscustomobject]@{
        Checks = 0
        LaunchCount = 0
    }

    function Assert-Allowed {
        param([string[]]$Tokens)
        Assert-ReadOnlyInventoryCommand -CommandTokens $Tokens
        $state.Checks++
    }

    function Assert-Denied {
        param([string[]]$Tokens, [string]$Pattern)
        try {
            Assert-ReadOnlyInventoryCommand -CommandTokens $Tokens
        }
        catch {
            if ($_.Exception.Message -notmatch $Pattern) {
                throw "Expected denial matching '$Pattern', got '$($_.Exception.Message)'."
            }
            $state.Checks++
            return
        }
        throw "Expected command to be denied: $($Tokens -join ' ')"
    }

    Assert-Allowed @("git", "status", "--short", "--branch")
    Assert-Allowed @("git", "worktree", "list", "--porcelain")
    Assert-Allowed @("git", "diff", "origin/main..HEAD", "--", "src")
    Assert-Allowed @("gh", "pr", "list", "--state", "open")
    Assert-Allowed @("gh", "issue", "view", "1753", "--json", "title,state")
    Assert-Allowed @("gh", "project", "item-list", "1", "--owner", "example")
    Assert-Allowed @("gh", "run", "view", "123", "--json", "jobs")
    Assert-Allowed @("gh", "api", "repos/example/repo/pulls/1/comments")
    Assert-Allowed @("gh", "api", "--method", "GET", "repos/example/repo/issues", "-f", "state=open")
    Assert-Allowed @("gh", "api", "graphql", "-f", 'query=query($owner:String!){repositoryOwner(login:$owner){login}}', "-F", "owner=example")

    Assert-Denied @("gh", "api", "--method", "POST", "repos/example/repo/issues") "method.*not read-only"
    Assert-Denied @("gh", "api", "-X", "DELETE", "repos/example/repo/issues/1") "method.*not read-only"
    Assert-Denied @("gh", "api", "repos/example/repo/issues", "-f", "title=x") "default to POST"
    Assert-Denied @("gh", "api", "graphql", "-f", "query=mutation { deleteProjectV2(input:{}) { clientMutationId } }") "mutations"
    Assert-Denied @("gh", "pr", "comment", "1", "--body", "x") "not allowlisted"
    Assert-Denied @("gh", "pr", "merge", "1") "not allowlisted"
    Assert-Denied @("gh", "issue", "edit", "1", "--add-label", "bug") "not allowlisted"
    Assert-Denied @("gh", "project", "item-edit", "--id", "x") "not allowlisted"
    Assert-Denied @("gh", "run", "cancel", "123") "not allowlisted"
    Assert-Denied @("gh", "run", "rerun", "123") "not allowlisted"
    Assert-Denied @("gh", "workflow", "run", "ci.yml") "not allowlisted"
    Assert-Denied @("git", "fetch", "origin", "main") "not allowlisted"
    Assert-Denied @("git", "reset", "--hard") "not allowlisted"
    Assert-Denied @("git", "diff", "--output=result.patch") "can write"
    Assert-Denied @("git", "grep", "-O", "powershell", "needle") "can write or execute"
    Assert-Denied @("git", "cat-file", "--filters", "HEAD:path") "can write or execute"
    Assert-Denied @("git", "cat-file", "--batch-command") "can write or execute"
    Assert-Denied @("git", "ls-remote", "ext::powershell -Command touch-owned") "can write or execute"
    Assert-Denied @("gh", "release", "download", "v1") "not allowlisted"
    Assert-Denied @("gh", "api", "repos/example/repo/issues", "--cache", "1h") "writes local state"
    Assert-Denied @("gh", "api", "--method", "GET", "repos/example/repo/issues", "-F", "value=@secret.txt") "local files"
    Assert-Denied @("gh", "api", "repos/example/repo/issues/1", "-XDELETE") "attached short options"
    Assert-Denied @("gh", "api", "repos/example/repo/issues", "-XPOST", "-Ftitle=x") "attached short options"
    Assert-Denied @("gh", "api", "repos/example/repo/issues", "-fbody=x") "attached short options"
    Assert-Denied @("gh", "api", "--method", "GET", "repos/example/repo/issues", "-Fq=@secret.txt") "attached short options"
    Assert-Denied @("gh", "pr", "view", "1", "--web=true") "interactive surface"
    Assert-Denied @("gh", "pr", "view", "1", "-w") "interactive surface"
    Assert-Denied @("powershell", "-Command", "gh pr list") "tool.*not allowed"
    Assert-Denied @("gh", "pr", "list", "&&", "gh", "pr", "comment", "1") "shell control"

    $fakeLauncher = {
        param([string]$Tool, [string[]]$Arguments)
        $state.LaunchCount++
    }
    Invoke-ValidatedInventoryCommand -CommandTokens @("gh", "pr", "list") -Launcher $fakeLauncher
    if ($state.LaunchCount -ne 1) {
        throw "Allowed command should launch exactly once."
    }
    try {
        Invoke-ValidatedInventoryCommand -CommandTokens @("gh", "pr", "comment", "1") -Launcher $fakeLauncher
    }
    catch {
        if ($state.LaunchCount -ne 1) {
            throw "Denied command reached the launcher."
        }
        $state.Checks++
    }

    Write-Output "Read-only inventory self-test passed: $($state.Checks) checks."
}

if ($SelfTest) {
    Invoke-ReadOnlyInventorySelfTest
    return
}

Assert-ReadOnlyInventoryCommand -CommandTokens $Command
if ($ValidateOnly) {
    Write-Output "Read-only inventory command accepted."
    return
}

$toolName = $Command[0].ToLowerInvariant()
$toolArguments = @($Command | Select-Object -Skip 1)
$executable = Resolve-InventoryExecutable -Tool $toolName

$oldOptionalLocks = $env:GIT_OPTIONAL_LOCKS
$oldGitPager = $env:GIT_PAGER
$oldPager = $env:PAGER
$oldExternalDiff = $env:GIT_EXTERNAL_DIFF
$oldGhPromptDisabled = $env:GH_PROMPT_DISABLED
$oldGhPager = $env:GH_PAGER
$oldGhNoUpdateNotifier = $env:GH_NO_UPDATE_NOTIFIER
try {
    $launchArguments = $toolArguments
    if ($toolName -eq "git") {
        $env:GIT_OPTIONAL_LOCKS = "0"
        $env:GIT_PAGER = "cat"
        $env:PAGER = "cat"
        $env:GIT_EXTERNAL_DIFF = ""

        $subcommand = $toolArguments[0].ToLowerInvariant()
        $remainingArguments = @($toolArguments | Select-Object -Skip 1)
        $launchArguments = @("-c", "core.fsmonitor=false", "-c", "diff.external=", $subcommand)
        if ($subcommand -in @("diff", "log", "show")) {
            $launchArguments += @("--no-ext-diff", "--no-textconv")
        }
        $launchArguments += $remainingArguments
    }
    else {
        $env:GH_PROMPT_DISABLED = "true"
        $env:GH_PAGER = "cat"
        $env:GH_NO_UPDATE_NOTIFIER = "1"
    }
    & $executable @launchArguments
    $childExitCode = $LASTEXITCODE
}
finally {
    $env:GIT_OPTIONAL_LOCKS = $oldOptionalLocks
    $env:GIT_PAGER = $oldGitPager
    $env:PAGER = $oldPager
    $env:GIT_EXTERNAL_DIFF = $oldExternalDiff
    $env:GH_PROMPT_DISABLED = $oldGhPromptDisabled
    $env:GH_PAGER = $oldGhPager
    $env:GH_NO_UPDATE_NOTIFIER = $oldGhNoUpdateNotifier
}

if ($childExitCode -ne 0) {
    exit $childExitCode
}
