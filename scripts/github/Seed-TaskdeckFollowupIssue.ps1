[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [string]$Title,

    [Parameter(Mandatory = $true)]
    [string]$Body,

    [ValidateSet("Priority I", "Priority II", "Priority III", "Priority IV", "Priority V")]
    [string]$Priority = "Priority IV",

    [string[]]$Labels = @("docs"),
    [string]$Repository = "Chris0Jeky/Taskdeck",
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

$normalizedLabels = @()
foreach ($label in $Labels) {
    $normalizedLabels += ($label -split "," | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

$allLabels = @($normalizedLabels + $Priority | Select-Object -Unique)
$labelArgs = @()
foreach ($label in $allLabels) {
    $labelArgs += @("--label", $label)
}

$issueBody = @"
$Body

## Acceptance Criteria
- [ ] Scope is confirmed against current ``docs/STATUS.md``.
- [ ] Implementation includes the smallest appropriate tests or documented validation path.
- [ ] Any docs that become stale are updated in the same PR.

## Origin
Seeded as an explicit follow-up so batch execution does not silently defer discovered work.
"@

if ($DryRun) {
    [pscustomobject]@{
        repository = $Repository
        title = $Title
        labels = $allLabels
        body = $issueBody
    } | ConvertTo-Json -Depth 4
    return
}

$gh = Get-Command gh -ErrorAction SilentlyContinue
if ($null -eq $gh) {
    throw "GitHub CLI 'gh' is required. Install it or use GitHub MCP as the fallback."
}

if ($PSCmdlet.ShouldProcess($Repository, "Create follow-up issue '$Title'")) {
    & $gh.Source issue create --repo $Repository --title $Title --body $issueBody @labelArgs
    if ($LASTEXITCODE -ne 0) {
        throw "gh issue create failed."
    }
}
