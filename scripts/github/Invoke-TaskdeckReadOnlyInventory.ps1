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

function New-GitOptionPolicy {
    param(
        [string[]]$LongFlags = @(),
        [string[]]$LongValueFlags = @(),
        [string]$ShortFlags = "",
        [string]$ShortValueFlags = "",
        [switch]$AllowNumericShort
    )

    return @{
        LongFlags = $LongFlags
        LongValueFlags = $LongValueFlags
        ShortFlags = $ShortFlags
        ShortValueFlags = $ShortValueFlags
        AllowNumericShort = [bool]$AllowNumericShort
    }
}

# Git accepts any unambiguous abbreviation of a long option before it executes anything, so a
# denylist of full option names (--upload-pack, --filters, --output, ...) is bypassable with
# --upl=, --filt=, --outp=. The contract is therefore an exact, per-subcommand allowlist:
# an option token is accepted only when it matches a listed name character-for-character.
# Long options that take a value must use the attached --name=value form; short options that
# take a value must be a lone -x token followed by its value.
$script:GitLogLikePolicy = New-GitOptionPolicy `
    -LongFlags @(
        "--abbrev-commit", "--all", "--author-date-order", "--boundary", "--branches", "--cherry-pick",
        "--children", "--color", "--date-order", "--decorate", "--first-parent", "--follow",
        "--full-history", "--graph", "--merges", "--name-only", "--name-status", "--no-abbrev-commit",
        "--no-color", "--no-decorate", "--no-ext-diff", "--no-merges", "--no-patch", "--no-renames",
        "--numstat", "--oneline", "--parents", "--patch", "--raw", "--remotes", "--reverse",
        "--shortstat", "--simplify-merges", "--stat", "--summary", "--tags", "--topo-order",
        "--no-textconv"
    ) `
    -LongValueFlags @(
        "--abbrev", "--after", "--author", "--before", "--branches", "--color", "--committer",
        "--date", "--decorate", "--diff-filter", "--format", "--grep", "--max-count", "--pretty",
        "--remotes", "--since", "--skip", "--stat", "--tags", "--until"
    ) `
    -ShortFlags "psz" `
    -ShortValueFlags "nSG" `
    -AllowNumericShort

$script:GitReadOptionPolicy = @{
    "cat-file" = (New-GitOptionPolicy -LongFlags @("--batch-all-objects", "--buffer") -ShortFlags "tsep")
    "describe" = (New-GitOptionPolicy `
        -LongFlags @("--all", "--always", "--contains", "--dirty", "--first-parent", "--long", "--tags") `
        -LongValueFlags @("--abbrev", "--candidates", "--dirty", "--exclude", "--match"))
    "diff" = (New-GitOptionPolicy `
        -LongFlags @(
            "--binary", "--cached", "--check", "--color", "--exit-code", "--find-copies",
            "--find-copies-harder", "--find-renames", "--full-index", "--histogram",
            "--ignore-all-space", "--ignore-blank-lines", "--ignore-space-change", "--merge-base",
            "--minimal", "--name-only", "--name-status", "--no-color", "--no-ext-diff", "--no-patch",
            "--no-renames", "--no-textconv", "--numstat", "--patch", "--patience", "--quiet", "--raw",
            "--shortstat", "--staged", "--stat", "--summary", "--text", "--word-diff"
        ) `
        -LongValueFlags @(
            "--abbrev", "--color", "--diff-filter", "--dst-prefix", "--find-copies", "--find-renames",
            "--ignore-submodules", "--inter-hunk-context", "--relative", "--src-prefix", "--stat",
            "--unified", "--word-diff"
        ) `
        -ShortFlags "bpstwzCMR" `
        -ShortValueFlags "U")
    "for-each-ref" = (New-GitOptionPolicy `
        -LongFlags @("--omit-empty") `
        -LongValueFlags @(
            "--contains", "--count", "--format", "--merged", "--no-contains", "--no-merged",
            "--points-at", "--sort"
        ))
    "grep" = (New-GitOptionPolicy `
        -LongFlags @(
            "--all-match", "--and", "--basic-regexp", "--break", "--cached", "--color", "--column",
            "--count", "--extended-regexp", "--files-with-matches", "--files-without-match",
            "--fixed-strings", "--full-name", "--function-context", "--heading", "--ignore-case",
            "--invert-match", "--line-number", "--name-only", "--no-color", "--no-index", "--not",
            "--null", "--or", "--perl-regexp", "--quiet", "--show-function", "--text", "--untracked",
            "--word-regexp"
        ) `
        -LongValueFlags @(
            "--after-context", "--before-context", "--color", "--context", "--max-count",
            "--max-depth", "--threads"
        ) `
        -ShortFlags "cehilnpqvwzEFHILP" `
        -ShortValueFlags "ABCe")
    "log" = $script:GitLogLikePolicy
    "ls-files" = (New-GitOptionPolicy `
        -LongFlags @(
            "--cached", "--deleted", "--directory", "--eol", "--error-unmatch", "--exclude-standard",
            "--full-name", "--ignored", "--killed", "--modified", "--no-empty-directory", "--others",
            "--stage", "--unmerged"
        ) `
        -LongValueFlags @("--abbrev", "--exclude", "--format") `
        -ShortFlags "cdimostuz" `
        -ShortValueFlags "x")
    "ls-remote" = (New-GitOptionPolicy `
        -LongFlags @("--exit-code", "--get-url", "--heads", "--quiet", "--refs", "--symref", "--tags") `
        -LongValueFlags @("--sort") `
        -ShortFlags "htq")
    "ls-tree" = (New-GitOptionPolicy `
        -LongFlags @("--full-name", "--full-tree", "--long", "--name-only", "--name-status", "--object-only") `
        -LongValueFlags @("--abbrev", "--format") `
        -ShortFlags "dlrtz")
    "merge-base" = (New-GitOptionPolicy `
        -LongFlags @("--all", "--fork-point", "--independent", "--is-ancestor", "--octopus") `
        -ShortFlags "a")
    "name-rev" = (New-GitOptionPolicy `
        -LongFlags @("--all", "--always", "--name-only", "--tags") `
        -LongValueFlags @("--exclude", "--refs"))
    "rev-parse" = (New-GitOptionPolicy `
        -LongFlags @(
            "--abbrev-ref", "--absolute-git-dir", "--all", "--branches", "--flags", "--git-common-dir",
            "--git-dir", "--is-bare-repository", "--is-inside-git-dir", "--is-inside-work-tree",
            "--no-flags", "--no-revs", "--not", "--quiet", "--remotes", "--revs-only", "--short",
            "--show-cdup", "--show-prefix", "--show-toplevel", "--symbolic", "--symbolic-full-name",
            "--tags", "--verify"
        ) `
        -LongValueFlags @("--abbrev-ref", "--default", "--disambiguate", "--git-path", "--short") `
        -ShortFlags "q")
    "show" = $script:GitLogLikePolicy
    "status" = (New-GitOptionPolicy `
        -LongFlags @(
            "--ahead-behind", "--branch", "--column", "--ignored", "--long", "--no-ahead-behind",
            "--no-column", "--no-renames", "--porcelain", "--renames", "--short", "--show-stash",
            "--untracked-files", "--verbose"
        ) `
        -LongValueFlags @("--column", "--ignored", "--ignore-submodules", "--porcelain", "--untracked-files") `
        -ShortFlags "bsuvz")
    "worktree" = (New-GitOptionPolicy -LongFlags @("--porcelain", "--verbose") -ShortFlags "vz")
}

# Environment variables that can make Git launch an external transport, helper, or prompt program.
# They are cleared for every launched Git process; command-line -c settings close the config half.
$script:GitNeutralizedEnvironmentVariables = @(
    "GIT_ALLOW_PROTOCOL",
    "GIT_ASKPASS",
    "GIT_CONFIG_COUNT",
    "GIT_CONFIG_PARAMETERS",
    "GIT_EXTERNAL_DIFF",
    "GIT_PROTOCOL_FROM_USER",
    "GIT_PROXY_COMMAND",
    "GIT_SSH",
    "GIT_SSH_COMMAND",
    "GIT_SSH_VARIANT",
    "SSH_ASKPASS"
)

# Command-line configuration applied to every launched Git process. Command-line -c wins over
# repository, user, and GIT_CONFIG_PARAMETERS configuration, so a resolved insteadOf rewrite or a
# configured core.sshCommand cannot reintroduce a transport helper.
$script:GitHardeningConfiguration = @(
    "-c", "core.fsmonitor=false",
    "-c", "diff.external=",
    "-c", "core.sshCommand=",
    "-c", "protocol.allow=never",
    "-c", "protocol.https.allow=always"
)

function Split-GitInventoryArguments {
    param(
        [Parameter(Mandatory = $true)][string]$Subcommand,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Arguments,
        [Parameter(Mandatory = $true)][hashtable]$Policy
    )

    $options = New-Object System.Collections.Generic.List[string]
    $operands = New-Object System.Collections.Generic.List[string]
    $endOfOptions = $false
    $index = 0

    while ($index -lt $Arguments.Count) {
        $token = $Arguments[$index]

        if ($endOfOptions) {
            [void]$operands.Add($token)
            $index++
            continue
        }

        if ($token -ceq "--") {
            $endOfOptions = $true
            $index++
            continue
        }

        if ($token -ceq "-") {
            Deny-InventoryCommand "git operand '-' reads from standard input and is not allowed"
        }

        if ($token.StartsWith("--", [System.StringComparison]::Ordinal)) {
            $name = $token
            $separator = $token.IndexOf("=")
            if ($separator -ge 0) {
                $name = $token.Substring(0, $separator)
                if ($Policy["LongValueFlags"] -cnotcontains $name) {
                    Deny-InventoryCommand "git option '$name' is not allowlisted for '$Subcommand'; only exact option names are accepted and abbreviations are never expanded"
                }
            }
            elseif ($Policy["LongFlags"] -cnotcontains $name) {
                Deny-InventoryCommand "git option '$name' is not allowlisted for '$Subcommand'; only exact option names are accepted and abbreviations are never expanded"
            }
            [void]$options.Add($token)
            $index++
            continue
        }

        if ($token.StartsWith("-", [System.StringComparison]::Ordinal)) {
            $characters = $token.Substring(1)
            if ($Policy["AllowNumericShort"] -and $characters -match "^[0-9]+$") {
                [void]$options.Add($token)
                $index++
                continue
            }
            if ($characters.Length -eq 1 -and $Policy["ShortValueFlags"].Contains($characters)) {
                if ($index + 1 -ge $Arguments.Count) {
                    Deny-InventoryCommand "git short option '$token' requires a separate value argument"
                }
                [void]$options.Add($token)
                [void]$options.Add($Arguments[$index + 1])
                $index += 2
                continue
            }
            foreach ($character in $characters.ToCharArray()) {
                if (-not $Policy["ShortFlags"].Contains([string]$character)) {
                    Deny-InventoryCommand "git short option '-$character' in '$token' is not allowlisted for '$Subcommand'"
                }
            }
            [void]$options.Add($token)
            $index++
            continue
        }

        [void]$operands.Add($token)
        $index++
    }

    return @{
        Options = $options.ToArray()
        Operands = $operands.ToArray()
    }
}

function Assert-GitHttpsRemoteOperand {
    param([Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Operands)

    if ($Operands.Count -lt 1) {
        Deny-InventoryCommand "git ls-remote requires an explicit https:// repository URL; a configured remote name can resolve to ssh, git, file, or an ext:: helper"
    }

    $url = $Operands[0]
    if (-not $url.StartsWith("https://", [System.StringComparison]::Ordinal)) {
        Deny-InventoryCommand "git ls-remote accepts only an explicit lowercase https:// URL, not '$url'"
    }

    $remainder = $url.Substring("https://".Length)
    $authority = $remainder.Split("/")[0]
    if ([string]::IsNullOrEmpty($authority)) {
        Deny-InventoryCommand "git ls-remote https URL '$url' has no host"
    }
    if ($authority -notmatch "^[A-Za-z0-9._~%!$&'()*+,;=:@-]+$") {
        Deny-InventoryCommand "git ls-remote https URL host '$authority' contains characters outside the allowed set"
    }
    if ($url -match "\s") {
        Deny-InventoryCommand "git ls-remote https URL cannot contain whitespace"
    }
}

function Assert-GitReadCommand {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    if ($Arguments.Count -eq 0) {
        Deny-InventoryCommand "git requires an allowlisted read subcommand"
    }

    $subcommand = $Arguments[0].ToLowerInvariant()
    if (-not $script:GitReadOptionPolicy.ContainsKey($subcommand)) {
        Deny-InventoryCommand "git subcommand '$subcommand' is not allowlisted"
    }

    $optionStart = 1
    if ($subcommand -eq "worktree") {
        if ($Arguments.Count -lt 2 -or $Arguments[1].ToLowerInvariant() -ne "list") {
            Deny-InventoryCommand "only 'git worktree list' is allowed"
        }
        $optionStart = 2
    }

    foreach ($argument in $Arguments) {
        if ($argument -match "^[A-Za-z][A-Za-z0-9+.-]*::") {
            Deny-InventoryCommand "git argument '$argument' names a transport helper and can launch a program outside the read boundary"
        }
    }

    $policy = $script:GitReadOptionPolicy[$subcommand]
    $tail = @()
    if ($Arguments.Count -gt $optionStart) {
        $tail = @($Arguments | Select-Object -Skip $optionStart)
    }
    $parsed = Split-GitInventoryArguments -Subcommand $subcommand -Arguments $tail -Policy $policy

    if ($subcommand -eq "ls-remote") {
        Assert-GitHttpsRemoteOperand -Operands $parsed["Operands"]
    }

    return $parsed
}

function Get-GitLaunchArguments {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    $parsed = Assert-GitReadCommand -Arguments $Arguments
    $subcommand = $Arguments[0].ToLowerInvariant()

    $launchArguments = @($script:GitHardeningConfiguration) + @($subcommand)
    if ($subcommand -in @("diff", "log", "show")) {
        $launchArguments += @("--no-ext-diff", "--no-textconv")
    }

    if ($subcommand -eq "ls-remote") {
        # ls-remote is the only remote-touching lane, and its operand has already been proven to be
        # an explicit https URL. --end-of-options keeps Git's abbreviation-tolerant parser from
        # re-reading that operand as an option.
        $launchArguments += @($parsed["Options"])
        $launchArguments += "--end-of-options"
        $launchArguments += @($parsed["Operands"])
        return $launchArguments
    }

    # Every other subcommand keeps its validated argv verbatim, including any -- pathspec separator.
    if ($Arguments.Count -gt 1) {
        $launchArguments += @($Arguments | Select-Object -Skip 1)
    }

    return $launchArguments
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
        "git" { [void](Assert-GitReadCommand -Arguments $arguments) }
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

function Set-InventoryEnvironmentVariable {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Saved,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $false)][AllowNull()][AllowEmptyString()][string]$Value
    )

    if (-not $Saved.ContainsKey($Name)) {
        $Saved[$Name] = [System.Environment]::GetEnvironmentVariable($Name, "Process")
    }
    [System.Environment]::SetEnvironmentVariable($Name, $Value, "Process")
}

function Restore-InventoryEnvironment {
    param([Parameter(Mandatory = $true)][hashtable]$Saved)

    foreach ($name in @($Saved.Keys)) {
        [System.Environment]::SetEnvironmentVariable($name, $Saved[$name], "Process")
    }
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
    Assert-Allowed @("git", "grep", "-nIi", "needle", "--", "src")
    Assert-Allowed @("git", "log", "-n", "5", "--format=%H %s")
    Assert-Allowed @("git", "for-each-ref", "--format=%(refname)", "refs/heads")
    Assert-Allowed @("git", "ls-remote", "--heads", "https://github.com/Chris0Jeky/Taskdeck.git")
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

    # Full option names stay denied because they are absent from every subcommand allowlist.
    Assert-Denied @("git", "diff", "--output=result.patch") "not allowlisted for 'diff'"
    Assert-Denied @("git", "cat-file", "--filters", "HEAD:path") "not allowlisted for 'cat-file'"
    Assert-Denied @("git", "cat-file", "--batch-command") "not allowlisted for 'cat-file'"
    Assert-Denied @("git", "log", "--show-signature") "not allowlisted for 'log'"

    # Git expands unambiguous long-option abbreviations before executing, so the allowlist must
    # reject every abbreviation of a process-launching option as well as the full spelling.
    Assert-Denied @("git", "ls-remote", "--upload-pack=/path/to/program", "https://github.com/o/r") "abbreviations are never expanded"
    Assert-Denied @("git", "ls-remote", "--upl=/path/to/program", "https://github.com/o/r") "abbreviations are never expanded"
    Assert-Denied @("git", "ls-remote", "--u=/path/to/program", "https://github.com/o/r") "abbreviations are never expanded"
    Assert-Denied @("git", "cat-file", "--filt", "HEAD:path") "abbreviations are never expanded"
    Assert-Denied @("git", "diff", "--outp=result.patch") "abbreviations are never expanded"
    Assert-Denied @("git", "log", "--ext-diff") "not allowlisted for 'log'"
    Assert-Denied @("git", "log", "--textc") "not allowlisted for 'log'"
    # Case variants are separate tokens and are never folded into an allowlisted spelling.
    Assert-Denied @("git", "status", "--SHORT") "not allowlisted for 'status'"

    Assert-Denied @("git", "grep", "-O", "powershell", "needle") "short option '-O'"
    Assert-Denied @("git", "grep", "-nOpowershell", "needle") "short option '-O'"
    Assert-Denied @("git", "grep", "-inO", "needle") "short option '-O'"
    Assert-Denied @("git", "grep", "-f", "patterns.txt", "--", "src") "short option '-f'"
    Assert-Denied @("git", "status", "-") "reads from standard input"

    # ls-remote resolves its operand through Git's URL and configuration layers, so only an
    # explicit lowercase https URL may reach the transport.
    Assert-Denied @("git", "ls-remote", "ssh://example.invalid/repo") "only an explicit lowercase https"
    Assert-Denied @("git", "ls-remote", "git://example.invalid/repo") "only an explicit lowercase https"
    Assert-Denied @("git", "ls-remote", "file:///c/repo") "only an explicit lowercase https"
    Assert-Denied @("git", "ls-remote", "HTTPS://example.invalid/repo") "only an explicit lowercase https"
    Assert-Denied @("git", "ls-remote", "git@example.invalid:owner/repo.git") "only an explicit lowercase https"
    Assert-Denied @("git", "ls-remote", "../other-repo") "only an explicit lowercase https"
    Assert-Denied @("git", "ls-remote", "origin") "only an explicit lowercase https"
    Assert-Denied @("git", "ls-remote") "explicit https:// repository URL"
    Assert-Denied @("git", "ls-remote", "--heads") "explicit https:// repository URL"
    Assert-Denied @("git", "ls-remote", "https:///repo") "has no host"
    Assert-Denied @("git", "ls-remote", "ext::powershell -Command touch-owned") "transport helper"
    Assert-Denied @("git", "ls-remote", "ext::sh -c whoami", "https://github.com/o/r") "transport helper"

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

    # A non-https remote must be refused before any transport process can be spawned.
    foreach ($rejectedRemote in @("ssh://example.invalid/repo", "git@example.invalid:o/r.git", "ext::sh -c whoami", "origin")) {
        try {
            Invoke-ValidatedInventoryCommand -CommandTokens @("git", "ls-remote", $rejectedRemote) -Launcher $fakeLauncher
        }
        catch {
            if ($state.LaunchCount -ne 1) {
                throw "Non-https ls-remote reached the launcher: $rejectedRemote"
            }
            $state.Checks++
            continue
        }
        throw "Expected ls-remote to be denied: $rejectedRemote"
    }

    $remoteLaunch = Get-GitLaunchArguments -Arguments @("ls-remote", "--heads", "https://github.com/Chris0Jeky/Taskdeck.git")
    foreach ($requiredPair in @("core.sshCommand=", "protocol.allow=never", "protocol.https.allow=always", "diff.external=")) {
        if ($remoteLaunch -cnotcontains $requiredPair) {
            throw "ls-remote launch arguments must pin '$requiredPair'."
        }
    }
    $endOfOptionsIndex = [array]::IndexOf($remoteLaunch, "--end-of-options")
    $urlIndex = [array]::IndexOf($remoteLaunch, "https://github.com/Chris0Jeky/Taskdeck.git")
    if ($endOfOptionsIndex -lt 0 -or $urlIndex -ne $endOfOptionsIndex + 1) {
        throw "The validated remote URL must follow --end-of-options."
    }
    $state.Checks++

    foreach ($requiredVariable in @("GIT_SSH", "GIT_SSH_COMMAND", "GIT_SSH_VARIANT", "GIT_ALLOW_PROTOCOL", "GIT_PROXY_COMMAND", "GIT_CONFIG_PARAMETERS")) {
        if ($script:GitNeutralizedEnvironmentVariables -cnotcontains $requiredVariable) {
            throw "Git launches must neutralize $requiredVariable."
        }
    }
    $state.Checks++

    $environmentProbe = @{}
    [System.Environment]::SetEnvironmentVariable("TASKDECK_INVENTORY_PROBE", "external-helper", "Process")
    Set-InventoryEnvironmentVariable -Saved $environmentProbe -Name "TASKDECK_INVENTORY_PROBE" -Value $null
    if ($null -ne [System.Environment]::GetEnvironmentVariable("TASKDECK_INVENTORY_PROBE", "Process")) {
        throw "Neutralized environment variables must be cleared before the child process starts."
    }
    Restore-InventoryEnvironment -Saved $environmentProbe
    if ([System.Environment]::GetEnvironmentVariable("TASKDECK_INVENTORY_PROBE", "Process") -cne "external-helper") {
        throw "Neutralized environment variables must be restored afterwards."
    }
    [System.Environment]::SetEnvironmentVariable("TASKDECK_INVENTORY_PROBE", $null, "Process")
    $state.Checks++

    $pathspecLaunch = Get-GitLaunchArguments -Arguments @("diff", "--name-only", "HEAD", "--", "scripts")
    if ($pathspecLaunch -cnotcontains "--") {
        throw "Local subcommands must keep their -- pathspec separator."
    }
    $state.Checks++

    $worktreeLaunch = Get-GitLaunchArguments -Arguments @("worktree", "list", "--porcelain")
    if ($worktreeLaunch -cnotcontains "list" -or $worktreeLaunch -cnotcontains "--porcelain") {
        throw "worktree list launch arguments lost their action or option."
    }
    $state.Checks++

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

$savedEnvironment = @{}
try {
    $launchArguments = $toolArguments
    if ($toolName -eq "git") {
        Set-InventoryEnvironmentVariable -Saved $savedEnvironment -Name "GIT_OPTIONAL_LOCKS" -Value "0"
        Set-InventoryEnvironmentVariable -Saved $savedEnvironment -Name "GIT_PAGER" -Value "cat"
        Set-InventoryEnvironmentVariable -Saved $savedEnvironment -Name "PAGER" -Value "cat"
        Set-InventoryEnvironmentVariable -Saved $savedEnvironment -Name "GIT_TERMINAL_PROMPT" -Value "0"
        foreach ($neutralized in $script:GitNeutralizedEnvironmentVariables) {
            Set-InventoryEnvironmentVariable -Saved $savedEnvironment -Name $neutralized -Value $null
        }

        $launchArguments = Get-GitLaunchArguments -Arguments $toolArguments
    }
    else {
        Set-InventoryEnvironmentVariable -Saved $savedEnvironment -Name "GH_PROMPT_DISABLED" -Value "true"
        Set-InventoryEnvironmentVariable -Saved $savedEnvironment -Name "GH_PAGER" -Value "cat"
        Set-InventoryEnvironmentVariable -Saved $savedEnvironment -Name "GH_NO_UPDATE_NOTIFIER" -Value "1"
    }
    & $executable @launchArguments
    $childExitCode = $LASTEXITCODE
}
finally {
    Restore-InventoryEnvironment -Saved $savedEnvironment
}

if ($childExitCode -ne 0) {
    exit $childExitCode
}
