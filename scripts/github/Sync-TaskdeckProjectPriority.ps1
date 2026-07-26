[CmdletBinding()]
param(
    [string]$Owner = "Chris0Jeky",
    [int]$ProjectNumber = 1,
    [string]$Repository = "Chris0Jeky/Taskdeck",
    [ValidateRange(0, [int]::MaxValue)]
    [int]$Limit = 0,
    [switch]$StrictFallbackPriority,
    [switch]$Apply,
    [switch]$Json,
    [switch]$SelfTest
)

$ErrorActionPreference = "Stop"

$script:ProjectItemsQuery = @'
query($projectId: ID!, $after: String) {
  node(id: $projectId) {
    ... on ProjectV2 {
      id
      number
      title
      updatedAt
      items(first: 100, after: $after) {
        totalCount
        pageInfo {
          hasNextPage
          endCursor
        }
        nodes {
          id
          fieldValues(first: 100) {
            totalCount
            pageInfo {
              hasNextPage
              endCursor
            }
            nodes {
              ... on ProjectV2ItemFieldSingleSelectValue {
                name
                field {
                  ... on ProjectV2SingleSelectField {
                    id
                    name
                  }
                }
              }
            }
          }
          content {
            __typename
            ... on Issue {
              number
              title
              url
              labels(first: 100) {
                totalCount
                pageInfo {
                  hasNextPage
                  endCursor
                }
                nodes {
                  name
                }
              }
            }
            ... on PullRequest {
              number
              title
              url
              body
              labels(first: 100) {
                totalCount
                pageInfo {
                  hasNextPage
                  endCursor
                }
                nodes {
                  name
                }
              }
            }
          }
        }
      }
    }
  }
}
'@

$script:ProjectStampQuery = @'
query($projectId: ID!) {
  node(id: $projectId) {
    ... on ProjectV2 {
      id
      updatedAt
      items(first: 1) {
        totalCount
      }
    }
  }
}
'@

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

    return (($output -join [Environment]::NewLine) | ConvertFrom-Json)
}

function Invoke-ProjectItemsPage {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectId,
        [AllowNull()]
        [string]$After
    )

    $arguments = @(
        "api",
        "graphql",
        "-f",
        "query=$script:ProjectItemsQuery",
        "-F",
        "projectId=$ProjectId"
    )

    if (-not [string]::IsNullOrWhiteSpace($After)) {
        $arguments += @("-f", "after=$After")
    }

    Invoke-GhJson -Arguments $arguments
}

function Get-ProjectStamp {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectId
    )

    $response = Invoke-GhJson -Arguments @(
        "api",
        "graphql",
        "-f",
        "query=$script:ProjectStampQuery",
        "-F",
        "projectId=$ProjectId"
    )

    if ($response.PSObject.Properties.Name -contains "errors" -and @($response.errors).Count -gt 0) {
        throw "Project stamp query returned GraphQL errors: $($response.errors | ConvertTo-Json -Compress -Depth 4)"
    }

    $projectNode = $response.data.node
    if ($null -eq $projectNode -or
        -not [string]::Equals([string]$projectNode.id, $ProjectId, [System.StringComparison]::Ordinal)) {
        throw "Project stamp query did not return ProjectV2 '$ProjectId'."
    }

    if ([string]::IsNullOrWhiteSpace([string]$projectNode.updatedAt) -or
        $null -eq $projectNode.items -or
        -not ($projectNode.items.PSObject.Properties.Name -contains "totalCount")) {
        throw "Project stamp query returned an incomplete ProjectV2 shape for '$ProjectId'."
    }

    [pscustomobject]@{
        projectId = [string]$projectNode.id
        projectUpdatedAt = [string]$projectNode.updatedAt
        totalCount = [int]$projectNode.items.totalCount
    }
}

function Assert-CompleteNestedConnection {
    param(
        [AllowNull()]
        [object]$Connection,
        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    if ($null -eq $Connection -or
        -not ($Connection.PSObject.Properties.Name -contains "totalCount") -or
        -not ($Connection.PSObject.Properties.Name -contains "nodes") -or
        $null -eq $Connection.pageInfo -or
        -not ($Connection.pageInfo.PSObject.Properties.Name -contains "hasNextPage")) {
        throw "$Context returned an incomplete connection shape."
    }

    $nodes = @($Connection.nodes)
    $totalCount = [int]$Connection.totalCount
    if ($totalCount -lt 0) {
        throw "$Context returned a negative totalCount."
    }

    if ([bool]$Connection.pageInfo.hasNextPage -or $nodes.Count -ne $totalCount) {
        throw "$Context was truncated: received $($nodes.Count) of $totalCount nodes (hasNextPage=$([bool]$Connection.pageInfo.hasNextPage))."
    }
}

function ConvertFrom-ProjectV2Item {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Item,
        [Parameter(Mandatory = $true)]
        [string]$PriorityFieldId,
        [Parameter(Mandatory = $true)]
        [string]$StatusFieldId
    )

    if ([string]::IsNullOrWhiteSpace([string]$Item.id)) {
        throw "ProjectV2 returned an item without an id."
    }

    Assert-CompleteNestedConnection -Connection $Item.fieldValues -Context "Project item '$($Item.id)' fieldValues"
    $fieldValueNodes = @($Item.fieldValues.nodes)

    $priorityValues = @($fieldValueNodes | Where-Object {
        $null -ne $_.field -and
        [string]::Equals([string]$_.field.id, $PriorityFieldId, [System.StringComparison]::Ordinal)
    })
    if ($priorityValues.Count -gt 1) {
        throw "Project item '$($Item.id)' returned multiple values for Priority field '$PriorityFieldId'."
    }

    $statusValues = @($fieldValueNodes | Where-Object {
        $null -ne $_.field -and
        [string]::Equals([string]$_.field.id, $StatusFieldId, [System.StringComparison]::Ordinal)
    })
    if ($statusValues.Count -gt 1) {
        throw "Project item '$($Item.id)' returned multiple values for Status field '$StatusFieldId'."
    }

    $content = $Item.content
    $normalizedContent = $null
    $labels = @()
    if ($null -ne $content) {
        $contentType = [string]$content.__typename
        if ($contentType -eq "Issue" -or $contentType -eq "PullRequest") {
            Assert-CompleteNestedConnection -Connection $content.labels -Context "Project item '$($Item.id)' $contentType labels"
            $labels = @($content.labels.nodes | ForEach-Object { [string]$_.name })
        }

        $normalizedContent = [pscustomobject]@{
            type = $contentType
            number = $content.number
            title = $content.title
            url = $content.url
            body = if ($content.PSObject.Properties.Name -contains "body") { [string]$content.body } else { $null }
        }
    }

    [pscustomobject]@{
        id = [string]$Item.id
        labels = $labels
        priority = if ($priorityValues.Count -eq 1) { [string]$priorityValues[0].name } else { $null }
        status = if ($statusValues.Count -eq 1) { [string]$statusValues[0].name } else { $null }
        content = $normalizedContent
    }
}

function Get-CompleteProjectSnapshot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectId,
        [Parameter(Mandatory = $true)]
        [string]$PriorityFieldId,
        [Parameter(Mandatory = $true)]
        [string]$StatusFieldId,
        [ValidateRange(0, [int]::MaxValue)]
        [int]$ItemLimit = 0,
        [Parameter(Mandatory = $true)]
        [scriptblock]$PageProvider
    )

    $after = $null
    $expectedTotalCount = $null
    $expectedUpdatedAt = $null
    $projectTitle = $null
    $projectNumberFromGraphQl = $null
    $pageCount = 0
    $seenItemIds = [System.Collections.Generic.Dictionary[string, int]]::new([System.StringComparer]::Ordinal)
    $seenCursors = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $normalizedItems = [System.Collections.Generic.List[object]]::new()

    while ($true) {
        $response = & $PageProvider $ProjectId $after
        $pageCount++

        if ($null -eq $response) {
            throw "ProjectV2 page $pageCount returned no response."
        }
        if ($response.PSObject.Properties.Name -contains "errors" -and @($response.errors).Count -gt 0) {
            throw "ProjectV2 page $pageCount returned GraphQL errors: $($response.errors | ConvertTo-Json -Compress -Depth 4)"
        }

        $projectNode = $response.data.node
        if ($null -eq $projectNode -or
            -not [string]::Equals([string]$projectNode.id, $ProjectId, [System.StringComparison]::Ordinal)) {
            throw "ProjectV2 page $pageCount did not return project '$ProjectId'."
        }
        if ([string]::IsNullOrWhiteSpace([string]$projectNode.updatedAt) -or
            $null -eq $projectNode.items -or
            -not ($projectNode.items.PSObject.Properties.Name -contains "totalCount") -or
            -not ($projectNode.items.PSObject.Properties.Name -contains "nodes") -or
            $null -eq $projectNode.items.pageInfo -or
            -not ($projectNode.items.pageInfo.PSObject.Properties.Name -contains "hasNextPage")) {
            throw "ProjectV2 page $pageCount returned an incomplete pagination shape."
        }

        $pageTotalCount = [int]$projectNode.items.totalCount
        $pageUpdatedAt = [string]$projectNode.updatedAt
        if ($pageTotalCount -lt 0) {
            throw "ProjectV2 page $pageCount returned a negative totalCount."
        }

        if ($null -eq $expectedTotalCount) {
            $expectedTotalCount = $pageTotalCount
            $expectedUpdatedAt = $pageUpdatedAt
            $projectTitle = [string]$projectNode.title
            $projectNumberFromGraphQl = [int]$projectNode.number

            if ($ItemLimit -gt 0 -and $expectedTotalCount -gt $ItemLimit) {
                throw "Project contains $expectedTotalCount items, exceeding -Limit $ItemLimit. Completeness cannot be established within the configured ceiling."
            }
        } else {
            if ($pageTotalCount -ne $expectedTotalCount) {
                throw "Project totalCount drifted during pagination: expected $expectedTotalCount, page $pageCount reported $pageTotalCount."
            }
            if ($pageUpdatedAt -ne $expectedUpdatedAt) {
                throw "Project updatedAt drifted during pagination: expected '$expectedUpdatedAt', page $pageCount reported '$pageUpdatedAt'."
            }
        }

        foreach ($item in @($projectNode.items.nodes)) {
            $itemId = [string]$item.id
            if ([string]::IsNullOrWhiteSpace($itemId)) {
                throw "ProjectV2 page $pageCount returned an item without an id."
            }
            if ($seenItemIds.ContainsKey($itemId)) {
                throw "ProjectV2 pagination returned duplicate item id '$itemId' (first seen on page $($seenItemIds[$itemId]), repeated on page $pageCount after cursor '$after'; totalCount=$expectedTotalCount, updatedAt='$expectedUpdatedAt')."
            }

            $seenItemIds[$itemId] = $pageCount
            $normalizedItems.Add((ConvertFrom-ProjectV2Item `
                -Item $item `
                -PriorityFieldId $PriorityFieldId `
                -StatusFieldId $StatusFieldId)) | Out-Null
        }

        if ($normalizedItems.Count -gt $expectedTotalCount) {
            throw "ProjectV2 pagination returned more items ($($normalizedItems.Count)) than totalCount ($expectedTotalCount)."
        }

        $hasNextPage = [bool]$projectNode.items.pageInfo.hasNextPage
        if (-not $hasNextPage) {
            break
        }

        $endCursor = [string]$projectNode.items.pageInfo.endCursor
        if ([string]::IsNullOrWhiteSpace($endCursor)) {
            throw "ProjectV2 page $pageCount reported hasNextPage without an endCursor."
        }
        if ([string]::Equals($endCursor, $after, [System.StringComparison]::Ordinal) -or $seenCursors.Contains($endCursor)) {
            throw "ProjectV2 pagination cursor did not advance at page $pageCount ('$endCursor')."
        }

        $seenCursors.Add($endCursor) | Out-Null
        $after = $endCursor
    }

    if ($normalizedItems.Count -ne $expectedTotalCount) {
        throw "ProjectV2 pagination ended prematurely: received $($normalizedItems.Count) of $expectedTotalCount items."
    }
    if ($seenItemIds.Count -ne $expectedTotalCount) {
        throw "ProjectV2 pagination identity check failed: received $($seenItemIds.Count) unique item ids for totalCount $expectedTotalCount."
    }

    [pscustomobject]@{
        projectId = $ProjectId
        projectNumber = $projectNumberFromGraphQl
        projectTitle = $projectTitle
        projectUpdatedAt = $expectedUpdatedAt
        totalCount = $expectedTotalCount
        pageCount = $pageCount
        complete = $true
        items = $normalizedItems.ToArray()
    }
}

function Assert-AuditableIssuePriorities {
    param(
        [object[]]$AuditItems
    )

    $invalidIssues = @($AuditItems | Where-Object {
        $_.contentType -eq "Issue" -and
        [string]::IsNullOrWhiteSpace([string]$_.expectedPriority)
    })
    if ($invalidIssues.Count -eq 0) {
        return
    }

    $diagnostics = @($invalidIssues | ForEach-Object {
        $number = if ($_.number) { "#$($_.number)" } else { "<no-number>" }
        $actual = if ($_.actualPriority) { $_.actualPriority } else { "<empty>" }
        "$number ($($_.reason); project Priority=$actual)"
    }) -join "; "

    throw "Priority audit cannot determine an expected value for $($invalidIssues.Count) issue item(s); refusing to report clean or apply updates. Fix each issue to have exactly one Priority label. Items: $diagnostics"
}

function Invoke-SelfTest {
    $priorityFieldId = "priority-field"
    $statusFieldId = "status-field"
    $projectId = "project-id"
    $stableUpdatedAt = "2026-07-26T00:00:00Z"
    $checks = 0

    function Assert-SelfTest {
        param(
            [bool]$Condition,
            [string]$Message
        )

        if (-not $Condition) {
            throw "SelfTest assertion failed: $Message"
        }
        return 1
    }

    function Assert-SelfTestThrows {
        param(
            [scriptblock]$Action,
            [string]$MessagePattern
        )

        $didThrow = $false
        $actualMessage = $null
        try {
            & $Action | Out-Null
        } catch {
            $didThrow = $true
            $actualMessage = $_.Exception.Message
        }

        if (-not $didThrow) {
            throw "SelfTest expected an exception matching '$MessagePattern'."
        }
        if ($actualMessage -notmatch $MessagePattern) {
            throw "SelfTest exception '$actualMessage' did not match '$MessagePattern'."
        }
        return 1
    }

    function New-SelfTestItem {
        param(
            [string]$Id,
            [string]$ContentType = "Issue",
            [int]$Number = 1,
            [string]$Priority = "Priority I",
            [string]$Status = "Pending",
            [string[]]$Labels = @("Priority I"),
            [string]$Body = "",
            [int]$FieldValueTotalCount = 3,
            [bool]$FieldValuesHasNextPage = $false,
            [int]$LabelTotalCount = -1,
            [bool]$LabelsHasNextPage = $false
        )

        $fieldNodes = @(
            [pscustomobject]@{},
            [pscustomobject]@{
                name = $Status
                field = [pscustomobject]@{ id = $statusFieldId; name = "Status" }
            },
            [pscustomobject]@{
                name = $Priority
                field = [pscustomobject]@{ id = $priorityFieldId; name = "Priority" }
            }
        )
        $labelNodes = @($Labels | ForEach-Object { [pscustomobject]@{ name = $_ } })
        if ($LabelTotalCount -lt 0) {
            $LabelTotalCount = $labelNodes.Count
        }

        $content = [pscustomobject]@{
            __typename = $ContentType
            number = $Number
            title = "Item $Number"
            url = "https://example.test/items/$Number"
            body = $Body
            labels = [pscustomobject]@{
                totalCount = $LabelTotalCount
                pageInfo = [pscustomobject]@{ hasNextPage = $LabelsHasNextPage; endCursor = $null }
                nodes = $labelNodes
            }
        }

        [pscustomobject]@{
            id = $Id
            fieldValues = [pscustomobject]@{
                totalCount = $FieldValueTotalCount
                pageInfo = [pscustomobject]@{ hasNextPage = $FieldValuesHasNextPage; endCursor = $null }
                nodes = $fieldNodes
            }
            content = $content
        }
    }

    function New-SelfTestResponse {
        param(
            [int]$TotalCount,
            [object[]]$Nodes,
            [bool]$HasNextPage,
            [AllowNull()]
            [string]$EndCursor,
            [string]$UpdatedAt = $stableUpdatedAt
        )

        [pscustomobject]@{
            data = [pscustomobject]@{
                node = [pscustomobject]@{
                    id = $projectId
                    number = 1
                    title = "SelfTest Project"
                    updatedAt = $UpdatedAt
                    items = [pscustomobject]@{
                        totalCount = $TotalCount
                        pageInfo = [pscustomobject]@{
                            hasNextPage = $HasNextPage
                            endCursor = $EndCursor
                        }
                        nodes = @($Nodes)
                    }
                }
            }
        }
    }

    function Get-SelfTestSnapshot {
        param(
            [hashtable]$Pages,
            [int]$TestLimit = 0
        )

        $provider = {
            param($requestedProjectId, $after)
            $key = if ([string]::IsNullOrWhiteSpace([string]$after)) { "<start>" } else { [string]$after }
            if (-not $Pages.ContainsKey($key)) {
                throw "SelfTest has no page for cursor '$key'."
            }
            $Pages[$key]
        }

        Get-CompleteProjectSnapshot `
            -ProjectId $projectId `
            -PriorityFieldId $priorityFieldId `
            -StatusFieldId $statusFieldId `
            -ItemLimit $TestLimit `
            -PageProvider $provider
    }

    $allItems = @(for ($index = 0; $index -lt 1001; $index++) {
        New-SelfTestItem -Id "item-$index" -Number ($index + 1)
    })
    $allItems[0] = New-SelfTestItem `
        -Id "item-0" `
        -ContentType "PullRequest" `
        -Number 1 `
        -Priority "Priority II" `
        -Status "Review" `
        -Labels @("testing", "Priority II") `
        -Body "Closes #42"

    $largePages = @{}
    $pageSize = 100
    $largePageCount = [int][Math]::Ceiling($allItems.Count / [double]$pageSize)
    for ($pageIndex = 0; $pageIndex -lt $largePageCount; $pageIndex++) {
        $offset = $pageIndex * $pageSize
        $take = [Math]::Min($pageSize, $allItems.Count - $offset)
        $pageNodes = @($allItems[$offset..($offset + $take - 1)])
        $hasNextPage = $pageIndex -lt ($largePageCount - 1)
        $endCursor = if ($hasNextPage) { "cursor-$pageIndex" } else { $null }
        $inputCursor = if ($pageIndex -eq 0) { "<start>" } else { "cursor-$($pageIndex - 1)" }
        $largePages[$inputCursor] = New-SelfTestResponse `
            -TotalCount $allItems.Count `
            -Nodes $pageNodes `
            -HasNextPage $hasNextPage `
            -EndCursor $endCursor
    }

    $largeSnapshot = Get-SelfTestSnapshot -Pages $largePages
    $checks += Assert-SelfTest -Condition ($largeSnapshot.complete -and $largeSnapshot.totalCount -eq 1001 -and $largeSnapshot.items.Count -eq 1001) -Message "more than 1,000 items should be complete"
    $checks += Assert-SelfTest -Condition ($largeSnapshot.pageCount -eq 11) -Message "more than 1,000 items should span all pages"
    $checks += Assert-SelfTest -Condition ($largeSnapshot.items[0].content.type -eq "PullRequest" -and $largeSnapshot.items[0].content.body -eq "Closes #42") -Message "content shape normalization should preserve PR type and body"
    $checks += Assert-SelfTest -Condition ($largeSnapshot.items[0].priority -eq "Priority II" -and $largeSnapshot.items[0].status -eq "Review") -Message "field normalization should use exact Priority and Status field ids"
    $checks += Assert-SelfTest -Condition (@($largeSnapshot.items[0].labels).Count -eq 2 -and $largeSnapshot.items[0].labels[1] -eq "Priority II") -Message "label normalization should preserve the complete label set"

    $itemA = New-SelfTestItem -Id "item-a"
    $itemB = New-SelfTestItem -Id "item-b" -Number 2

    $caseDistinctPages = @{
        "<start>" = New-SelfTestResponse `
            -TotalCount 2 `
            -Nodes @(
                (New-SelfTestItem -Id "PVTI_lAHOA47lx84BPH_rzgoqVOw"),
                (New-SelfTestItem -Id "PVTI_lAHOA47lx84BPH_rzgoqVow" -Number 2)
            ) `
            -HasNextPage $false `
            -EndCursor $null
    }
    $caseDistinctSnapshot = Get-SelfTestSnapshot -Pages $caseDistinctPages
    $checks += Assert-SelfTest `
        -Condition ($caseDistinctSnapshot.items.Count -eq 2 -and
            $caseDistinctSnapshot.items[0].id -ceq "PVTI_lAHOA47lx84BPH_rzgoqVOw" -and
            $caseDistinctSnapshot.items[1].id -ceq "PVTI_lAHOA47lx84BPH_rzgoqVow") `
        -Message "ProjectV2 node ids must use ordinal case-sensitive identity"

    $prematurePages = @{
        "<start>" = New-SelfTestResponse -TotalCount 2 -Nodes @($itemA) -HasNextPage $false -EndCursor $null
    }
    $checks += Assert-SelfTestThrows -Action { Get-SelfTestSnapshot -Pages $prematurePages } -MessagePattern "ended prematurely"

    $duplicatePages = @{
        "<start>" = New-SelfTestResponse -TotalCount 2 -Nodes @($itemA) -HasNextPage $true -EndCursor "duplicate-next"
        "duplicate-next" = New-SelfTestResponse -TotalCount 2 -Nodes @($itemA) -HasNextPage $false -EndCursor $null
    }
    $checks += Assert-SelfTestThrows -Action { Get-SelfTestSnapshot -Pages $duplicatePages } -MessagePattern "duplicate item id"

    $totalDriftPages = @{
        "<start>" = New-SelfTestResponse -TotalCount 2 -Nodes @($itemA) -HasNextPage $true -EndCursor "total-next"
        "total-next" = New-SelfTestResponse -TotalCount 3 -Nodes @($itemB) -HasNextPage $false -EndCursor $null
    }
    $checks += Assert-SelfTestThrows -Action { Get-SelfTestSnapshot -Pages $totalDriftPages } -MessagePattern "totalCount drifted"

    $stampDriftPages = @{
        "<start>" = New-SelfTestResponse -TotalCount 2 -Nodes @($itemA) -HasNextPage $true -EndCursor "stamp-next"
        "stamp-next" = New-SelfTestResponse -TotalCount 2 -Nodes @($itemB) -HasNextPage $false -EndCursor $null -UpdatedAt "2026-07-26T00:00:01Z"
    }
    $checks += Assert-SelfTestThrows -Action { Get-SelfTestSnapshot -Pages $stampDriftPages } -MessagePattern "updatedAt drifted"

    $cursorPages = @{
        "<start>" = New-SelfTestResponse -TotalCount 3 -Nodes @($itemA) -HasNextPage $true -EndCursor "same-cursor"
        "same-cursor" = New-SelfTestResponse -TotalCount 3 -Nodes @($itemB) -HasNextPage $true -EndCursor "same-cursor"
    }
    $checks += Assert-SelfTestThrows -Action { Get-SelfTestSnapshot -Pages $cursorPages } -MessagePattern "cursor did not advance"

    $missingCursorPages = @{
        "<start>" = New-SelfTestResponse -TotalCount 2 -Nodes @($itemA) -HasNextPage $true -EndCursor $null
    }
    $checks += Assert-SelfTestThrows -Action { Get-SelfTestSnapshot -Pages $missingCursorPages } -MessagePattern "without an endCursor"

    $truncatedLabels = New-SelfTestItem -Id "labels-truncated" -Labels @("Priority I") -LabelTotalCount 2 -LabelsHasNextPage $true
    $truncatedLabelPages = @{
        "<start>" = New-SelfTestResponse -TotalCount 1 -Nodes @($truncatedLabels) -HasNextPage $false -EndCursor $null
    }
    $checks += Assert-SelfTestThrows -Action { Get-SelfTestSnapshot -Pages $truncatedLabelPages } -MessagePattern "labels was truncated"

    $truncatedFields = New-SelfTestItem -Id "fields-truncated" -FieldValueTotalCount 4 -FieldValuesHasNextPage $true
    $truncatedFieldPages = @{
        "<start>" = New-SelfTestResponse -TotalCount 1 -Nodes @($truncatedFields) -HasNextPage $false -EndCursor $null
    }
    $checks += Assert-SelfTestThrows -Action { Get-SelfTestSnapshot -Pages $truncatedFieldPages } -MessagePattern "fieldValues was truncated"

    $checks += Assert-SelfTestThrows -Action { Get-SelfTestSnapshot -Pages $largePages -TestLimit 1000 } -MessagePattern "exceeding -Limit 1000"

    $checks += Assert-SelfTestThrows -Action {
        Assert-AuditableIssuePriorities -AuditItems @(
            [pscustomobject]@{
                contentType = "Issue"
                number = 101
                expectedPriority = $null
                actualPriority = $null
                reason = "issue-missing-priority-label"
            }
        )
    } -MessagePattern "issue-missing-priority-label"

    $checks += Assert-SelfTestThrows -Action {
        Assert-AuditableIssuePriorities -AuditItems @(
            [pscustomobject]@{
                contentType = "Issue"
                number = 102
                expectedPriority = $null
                actualPriority = "Priority II"
                reason = "issue-multiple-priority-labels"
            }
        )
    } -MessagePattern "issue-multiple-priority-labels"

    Write-Host "SelfTest passed: $checks checks."
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

if ($SelfTest) {
    Invoke-SelfTest
    return
}

$gh = Get-Command gh -ErrorAction SilentlyContinue
if ($null -eq $gh) {
    throw "GitHub CLI 'gh' is required. Install it or use GitHub MCP as the fallback."
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
if ($null -eq $priorityField -or [string]::IsNullOrWhiteSpace([string]$priorityField.id)) {
    throw "Project $ProjectNumber does not expose a Priority field."
}
$statusField = @($fieldList.fields | Where-Object { $_.name -eq "Status" }) | Select-Object -First 1
if ($null -eq $statusField -or [string]::IsNullOrWhiteSpace([string]$statusField.id)) {
    throw "Project $ProjectNumber does not expose a Status field."
}

$priorityOptionByName = @{}
foreach ($option in @($priorityField.options)) {
    $priorityOptionByName[$option.name] = $option.id
}

$pageProvider = {
    param($requestedProjectId, $after)
    Invoke-ProjectItemsPage -ProjectId $requestedProjectId -After $after
}
$snapshot = Get-CompleteProjectSnapshot `
    -ProjectId $project.id `
    -PriorityFieldId $priorityField.id `
    -StatusFieldId $statusField.id `
    -ItemLimit $Limit `
    -PageProvider $pageProvider

if ($snapshot.projectNumber -ne $ProjectNumber) {
    throw "ProjectV2 '$($project.id)' reported number $($snapshot.projectNumber), expected $ProjectNumber."
}

$items = @($snapshot.items)

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

    if ($content.type -eq "PullRequest" -and
        $reason -eq "pr-no-derived-issue-fallback" -and
        $actualPriority -and
        -not $StrictFallbackPriority) {
        $needsUpdate = $false
        $reason = "pr-no-derived-issue-existing-priority"
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

Assert-AuditableIssuePriorities -AuditItems @($audit)
$updates = @($audit | Where-Object { $_.needsUpdate })

if ($Apply) {
    $preApplyStamp = Get-ProjectStamp -ProjectId $project.id
    if ($preApplyStamp.totalCount -ne $snapshot.totalCount -or
        $preApplyStamp.projectUpdatedAt -ne $snapshot.projectUpdatedAt) {
        throw "Project changed after the complete audit snapshot; refusing -Apply. Expected totalCount=$($snapshot.totalCount), updatedAt='$($snapshot.projectUpdatedAt)'; current totalCount=$($preApplyStamp.totalCount), updatedAt='$($preApplyStamp.projectUpdatedAt)'."
    }

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
        complete = $snapshot.complete
        reportedTotalCount = $snapshot.totalCount
        scanned = $items.Count
        pages = $snapshot.pageCount
        projectUpdatedAt = $snapshot.projectUpdatedAt
        limit = $Limit
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
Write-Host "Completeness: COMPLETE ($($items.Count)/$($snapshot.totalCount) items across $($snapshot.pageCount) pages; project updatedAt $($snapshot.projectUpdatedAt))"
Write-Host "Items needing priority sync: $($updates.Count)"
if ($Apply) {
    Write-Host "Items updated: $($updates.Count)"
}
Write-Host ""

if ($updates.Count -eq 0) {
    Write-Host "Complete audit confirmed all issue/PR items have the expected Priority field value."
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
