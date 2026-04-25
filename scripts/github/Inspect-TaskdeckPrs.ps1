[CmdletBinding()]
param(
    [string]$Repository = "Chris0Jeky/Taskdeck",
    [int]$Limit = 25,
    [switch]$Json
)

$ErrorActionPreference = "Stop"

$gh = Get-Command gh -ErrorAction SilentlyContinue
if ($null -eq $gh) {
    throw "GitHub CLI 'gh' is required. Install it or use GitHub MCP as the fallback."
}

$fields = "number,title,url,headRefName,baseRefName,isDraft,mergeable,reviewDecision,statusCheckRollup,closingIssuesReferences,comments,updatedAt"
$raw = & $gh.Source pr list --repo $Repository --state open --limit $Limit --json $fields
if ($LASTEXITCODE -ne 0) {
    throw "gh pr list failed."
}

$prs = $raw | ConvertFrom-Json
$summary = foreach ($pr in $prs) {
    $checks = @($pr.statusCheckRollup)
    $failed = @($checks | Where-Object { $_.conclusion -in @("FAILURE", "TIMED_OUT", "CANCELLED", "ACTION_REQUIRED") -or $_.status -eq "FAILURE" })
    $pending = @($checks | Where-Object { $_.status -in @("QUEUED", "IN_PROGRESS", "PENDING") -or $null -eq $_.conclusion })
    $issues = @($pr.closingIssuesReferences | ForEach-Object { "#$($_.number)" })

    [pscustomobject]@{
        number = $pr.number
        title = $pr.title
        url = $pr.url
        branch = $pr.headRefName
        base = $pr.baseRefName
        draft = $pr.isDraft
        mergeable = $pr.mergeable
        reviewDecision = $pr.reviewDecision
        failedChecks = $failed.Count
        pendingChecks = $pending.Count
        linkedIssues = ($issues -join ", ")
        commentCount = @($pr.comments).Count
        updatedAt = $pr.updatedAt
    }
}

if ($Json) {
    if ($null -eq $summary) {
        Write-Output "[]"
        return
    }
    @($summary) | ConvertTo-Json -Depth 6
    return
}

Write-Host "# Open Taskdeck PR Snapshot"
Write-Host ""
Write-Host "Repository: $Repository"
Write-Host ""

if ($null -eq $summary -or @($summary).Count -eq 0) {
    Write-Host "No open PRs found."
    return
}

foreach ($pr in $summary) {
    Write-Host "- PR #$($pr.number): $($pr.title)"
    Write-Host "  URL: $($pr.url)"
    Write-Host "  Branch: $($pr.branch) -> $($pr.base); mergeable=$($pr.mergeable); draft=$($pr.draft); review=$($pr.reviewDecision)"
    Write-Host "  Checks: failed=$($pr.failedChecks), pending=$($pr.pendingChecks); comments=$($pr.commentCount); linked=$($pr.linkedIssues)"
}
