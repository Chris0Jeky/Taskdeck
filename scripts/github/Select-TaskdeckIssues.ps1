[CmdletBinding()]
param(
    [string]$Repository = "Chris0Jeky/Taskdeck",
    [int]$Limit = 10,
    [switch]$IncludeTrackers,
    [switch]$Json
)

$ErrorActionPreference = "Stop"

$gh = Get-Command gh -ErrorAction SilentlyContinue
if ($null -eq $gh) {
    throw "GitHub CLI 'gh' is required. Install it or use GitHub MCP as the fallback."
}

$raw = & $gh.Source issue list --repo $Repository --state open --limit 200 --json number,title,labels,url,body,assignees,milestone,updatedAt
if ($LASTEXITCODE -ne 0) {
    throw "gh issue list failed."
}

$priorityRank = @{
    "Priority I" = 1
    "Priority II" = 2
    "Priority III" = 3
    "Priority IV" = 4
    "Priority V" = 5
}

$issues = $raw | ConvertFrom-Json
$candidates = foreach ($issue in $issues) {
    $labelNames = @($issue.labels | ForEach-Object { $_.name })
    $priorityLabels = @($labelNames | Where-Object { $priorityRank.ContainsKey($_) })
    $blocked = $labelNames -contains "blocked" -or $labelNames -contains "Blocked" -or ($issue.body -match "(?im)^\s*(depends on|blocked by)\s+#\d+")
    $trackerLike = $issue.title -match "(?i)\b(tracker|umbrella|wave index)\b"

    [pscustomobject]@{
        number = $issue.number
        title = $issue.title
        url = $issue.url
        priority = if ($priorityLabels.Count -eq 1) { $priorityLabels[0] } else { "MISSING_OR_MULTIPLE" }
        priorityRank = if ($priorityLabels.Count -eq 1) { $priorityRank[$priorityLabels[0]] } else { 99 }
        blocked = [bool]$blocked
        trackerLike = [bool]$trackerLike
        labels = ($labelNames -join ", ")
        updatedAt = $issue.updatedAt
    }
}

$plan = $candidates |
    Where-Object { -not $_.blocked } |
    Where-Object { $IncludeTrackers -or -not $_.trackerLike } |
    Sort-Object priorityRank, number |
    Select-Object -First $Limit

if ($Json) {
    $plan | ConvertTo-Json -Depth 5
    return
}

Write-Host "# Candidate Taskdeck Issue Batch"
Write-Host ""
Write-Host "Repository: $Repository"
Write-Host "Limit: $Limit"
if (-not $IncludeTrackers) {
    Write-Host "Tracker/umbrella issues are excluded by default. Use -IncludeTrackers to include them."
}
Write-Host ""

foreach ($issue in $plan) {
    Write-Host "- #$($issue.number) [$($issue.priority)] $($issue.title)"
    Write-Host "  $($issue.url)"
    if ($issue.priority -eq "MISSING_OR_MULTIPLE") {
        Write-Host "  WARNING: issue priority labels need correction before execution."
    }
}

Write-Host ""
Write-Host "Review dependency details and project status before starting. This script is a shortlist, not approval to violate WIP limits."
