[CmdletBinding()]
param(
    [string]$Owner = "Chris0Jeky",
    [int]$ProjectNumber = 1,
    [string]$Repository = "Chris0Jeky/Taskdeck",
    [int]$Limit = 1000,
    [switch]$Apply,
    [switch]$Json
)

$ErrorActionPreference = "Stop"

$gh = Get-Command gh -ErrorAction SilentlyContinue
if ($null -eq $gh) {
    throw "GitHub CLI 'gh' is required. Install it or use GitHub MCP as the fallback."
}

function Invoke-GhJson {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $output = & $gh.Source @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "gh $($Arguments -join ' ') failed: $output"
    }

    if ([string]::IsNullOrWhiteSpace($output)) {
        return $null
    }

    return ($output | ConvertFrom-Json)
}

function Get-PriorityLabels {
    param(
        [object[]]$Labels
    )

    @($Labels | Where-Object { $priorityRank.ContainsKey([string]$_) })
}

function Get-ReferencedIssueNumbers {
    param(
        [string]$Body
    )

    if ([string]::IsNullOrWhiteSpace($Body)) {
        return @()
    }

    $pattern = '(?im)\b(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?|refs?|references?)\s+(?:[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+)?#(?<number>\d+)'
    @([regex]::Matches($Body, $pattern) | ForEach-Object {
        [int]$_.Groups["number"].Value
    } | Sort-Object -Unique)
}

function Get-IssuePriority {
    param(
        [int]$Number
    )

    if ($issuePriorityByNumber.ContainsKey($Number)) {
        return $issuePriorityByNumber[$Number]
    }

    $issue = Invoke-GhJson -Arguments @("issue", "view", "$Number", "--repo", $Repository, "--json", "labels")
    $labelNames = @($issue.labels | ForEach-Object { $_.name })
    $priorityLabels = @(Get-PriorityLabels -Labels $labelNames)

    $priority = if ($priorityLabels.Count -eq 1) {
        @($priorityLabels)[0]
    } else {
        $null
    }

    $issuePriorityByNumber[$Number] = $priority
    return $priority
}

$priorityRank = @{
    "Priority I" = 1
    "Priority II" = 2
    "Priority III" = 3
    "Priority IV" = 4
    "Priority V" = 5
}

$projectList = Invoke-GhJson -Arguments @("project", "list", "--owner", $Owner, "--format", "json")
$project = @($projectList.projects | Where-Object { $_.number -eq $ProjectNumber }) | Select-Object -First 1
if ($null -eq $project) {
    throw "Project number $ProjectNumber was not found for owner $Owner."
}

$fieldList = Invoke-GhJson -Arguments @("project", "field-list", "$ProjectNumber", "--owner", $Owner, "--format", "json")
$priorityField = @($fieldList.fields | Where-Object { $_.name -eq "Priority" }) | Select-Object -First 1
if ($null -eq $priorityField) {
    throw "Project $ProjectNumber does not expose a Priority field."
}

$priorityOptionByName = @{}
foreach ($option in @($priorityField.options)) {
    $priorityOptionByName[$option.name] = $option.id
}

$itemList = Invoke-GhJson -Arguments @("project", "item-list", "$ProjectNumber", "--owner", $Owner, "--limit", "$Limit", "--format", "json")
$items = @($itemList.items)

$issuePriorityByNumber = @{}
foreach ($item in $items) {
    if ($item.content.type -ne "Issue") {
        continue
    }

    $labelNames = @($item.labels)
    $priorityLabels = @(Get-PriorityLabels -Labels $labelNames)
    if ($priorityLabels.Count -eq 1) {
        $issuePriorityByNumber[[int]$item.content.number] = @($priorityLabels)[0]
    }
}

$audit = foreach ($item in $items) {
    $content = $item.content
    if ($null -eq $content) {
        continue
    }

    $expectedPriority = $null
    $reason = $null
    $references = @()

    if ($content.type -eq "Issue") {
        $labelNames = @($item.labels)
        $priorityLabels = @(Get-PriorityLabels -Labels $labelNames)

        if ($priorityLabels.Count -eq 1) {
            $expectedPriority = @($priorityLabels)[0]
            $reason = "issue-label"
        } elseif ($priorityLabels.Count -eq 0) {
            $reason = "issue-missing-priority-label"
        } else {
            $reason = "issue-multiple-priority-labels"
        }
    } elseif ($content.type -eq "PullRequest") {
        $references = Get-ReferencedIssueNumbers -Body $content.body
        $referencedPriorities = @($references | ForEach-Object { Get-IssuePriority -Number $_ } | Where-Object { $_ })

        if ($referencedPriorities.Count -gt 0) {
            $expectedPriority = @($referencedPriorities | Sort-Object { $priorityRank[$_] })[0]
            $reason = "pr-referenced-issue"
        } else {
            $expectedPriority = "Priority V"
            $reason = "pr-no-derived-issue-fallback"
        }
    } else {
        $reason = "unsupported-content-type"
    }

    $actualPriority = if ($item.PSObject.Properties.Name -contains "priority") {
        $item.priority
    } else {
        $null
    }

    $needsUpdate = $false
    if ($expectedPriority -and $actualPriority -ne $expectedPriority) {
        $needsUpdate = $true
    }

    [pscustomobject]@{
        itemId = $item.id
        contentType = $content.type
        number = $content.number
        title = $content.title
        url = $content.url
        status = $item.status
        actualPriority = $actualPriority
        expectedPriority = $expectedPriority
        reason = $reason
        references = ($references -join ", ")
        needsUpdate = $needsUpdate
    }
}

$updates = @($audit | Where-Object { $_.needsUpdate })

if ($Apply) {
    foreach ($update in $updates) {
        if (-not $priorityOptionByName.ContainsKey($update.expectedPriority)) {
            throw "Priority option '$($update.expectedPriority)' is not available in project $ProjectNumber."
        }

        $applyOutput = & $gh.Source project item-edit `
            --id $update.itemId `
            --project-id $project.id `
            --field-id $priorityField.id `
            --single-select-option-id $priorityOptionByName[$update.expectedPriority] 2>&1

        if ($LASTEXITCODE -ne 0) {
            if ("$applyOutput" -match "missing required scopes \[project\]") {
                throw "Project priority write failed because gh is missing the 'project' scope. Run: gh auth refresh -s project"
            }

            throw "Failed to update project item $($update.itemId): $applyOutput"
        }
    }
}

if ($Json) {
    [pscustomobject]@{
        owner = $Owner
        projectNumber = $ProjectNumber
        projectId = $project.id
        projectTitle = $project.title
        scanned = $items.Count
        needsUpdate = $updates.Count
        applied = if ($Apply) { $updates.Count } else { 0 }
        items = $updates
    } | ConvertTo-Json -Depth 8
    return
}

Write-Host "# Taskdeck Project Priority Audit"
Write-Host ""
Write-Host "Project: $($project.title) (#$ProjectNumber)"
Write-Host "Items scanned: $($items.Count)"
Write-Host "Items needing priority sync: $($updates.Count)"
if ($Apply) {
    Write-Host "Items updated: $($updates.Count)"
}
Write-Host ""

if ($updates.Count -eq 0) {
    Write-Host "All scanned issue/PR items have the expected Priority field value."
    return
}

foreach ($update in $updates | Select-Object -First 50) {
    $actual = if ($update.actualPriority) { $update.actualPriority } else { "<empty>" }
    $number = if ($update.number) { "#$($update.number)" } else { "<no-number>" }
    Write-Host "- $($update.contentType) ${number}: $($update.title)"
    Write-Host "  Actual: $actual; expected: $($update.expectedPriority); reason: $($update.reason)"
    Write-Host "  $($update.url)"
}

if ($updates.Count -gt 50) {
    Write-Host ""
    Write-Host "Showing first 50 updates. Re-run with -Json for the full list."
}

if (-not $Apply) {
    Write-Host ""
    Write-Host "To apply these updates, run with -Apply. Project writes require: gh auth refresh -s project"
}
