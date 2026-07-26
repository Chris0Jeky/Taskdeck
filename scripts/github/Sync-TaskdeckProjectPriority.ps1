[CmdletBinding()]
param(
    [string]$Owner = "Chris0Jeky",
    [int]$ProjectNumber = 1,
    [string]$Repository = "Chris0Jeky/Taskdeck",
    [ValidateRange(0, [int]::MaxValue)]
    [int]$Limit = 0,
    [switch]$Apply,
    [switch]$Json,
    [switch]$SelfTest
)

$ErrorActionPreference = "Stop"

$priorityRank = [System.Collections.Generic.Dictionary[string, int]]::new([System.StringComparer]::Ordinal)
$priorityRank.Add("Priority I", 1)
$priorityRank.Add("Priority II", 2)
$priorityRank.Add("Priority III", 3)
$priorityRank.Add("Priority IV", 4)
$priorityRank.Add("Priority V", 5)

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
              repository {
                nameWithOwner
              }
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
              repository {
                nameWithOwner
              }
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
              closingIssuesReferences(first: 20) {
                totalCount
                pageInfo {
                  hasNextPage
                  endCursor
                }
                nodes {
                  id
                  number
                  title
                  url
                  repository {
                    nameWithOwner
                  }
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
    $repositoryName = $null
    $closingIssues = @()
    if ($null -ne $content) {
        $contentType = [string]$content.__typename
        if ($contentType -eq "Issue" -or $contentType -eq "PullRequest") {
            Assert-CompleteNestedConnection -Connection $content.labels -Context "Project item '$($Item.id)' $contentType labels"
            $labels = @($content.labels.nodes | ForEach-Object { [string]$_.name })
            $repositoryName = [string]$content.repository.nameWithOwner
            if ([string]::IsNullOrWhiteSpace($repositoryName)) {
                throw "Project item '$($Item.id)' $contentType did not expose repository identity."
            }
        }

        if ($contentType -eq "PullRequest") {
            Assert-CompleteNestedConnection `
                -Connection $content.closingIssuesReferences `
                -Context "Project item '$($Item.id)' PullRequest closingIssuesReferences"

            $closingIssues = @($content.closingIssuesReferences.nodes | ForEach-Object {
                $closingIssueRepository = [string]$_.repository.nameWithOwner
                if ([string]::IsNullOrWhiteSpace([string]$_.id) -or
                    [string]::IsNullOrWhiteSpace($closingIssueRepository) -or
                    [int]$_.number -le 0) {
                    throw "Project item '$($Item.id)' returned an incomplete closing issue identity."
                }

                Assert-CompleteNestedConnection `
                    -Connection $_.labels `
                    -Context "Project item '$($Item.id)' closing issue '$closingIssueRepository#$($_.number)' labels"

                [pscustomobject]@{
                    id = [string]$_.id
                    repository = $closingIssueRepository
                    number = [int]$_.number
                    title = [string]$_.title
                    url = [string]$_.url
                    labels = @($_.labels.nodes | ForEach-Object { [string]$_.name })
                }
            })
        }

        $normalizedContent = [pscustomobject]@{
            type = $contentType
            repository = $repositoryName
            number = $content.number
            title = $content.title
            url = $content.url
            body = if ($content.PSObject.Properties.Name -contains "body") { [string]$content.body } else { $null }
            closingIssues = $closingIssues
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

    function New-SelfTestClosingIssue {
        param(
            [string]$RepositoryName,
            [int]$Number,
            [string[]]$Labels = @("Priority I"),
            [int]$LabelTotalCount = -1,
            [bool]$LabelsHasNextPage = $false
        )

        $labelNodes = @($Labels | ForEach-Object { [pscustomobject]@{ name = $_ } })
        if ($LabelTotalCount -lt 0) {
            $LabelTotalCount = $labelNodes.Count
        }

        [pscustomobject]@{
            id = "issue-$($RepositoryName.Replace('/', '-'))-$Number"
            number = $Number
            title = "Issue $RepositoryName#$Number"
            url = "https://example.test/$RepositoryName/issues/$Number"
            repository = [pscustomobject]@{ nameWithOwner = $RepositoryName }
            labels = [pscustomobject]@{
                totalCount = $LabelTotalCount
                pageInfo = [pscustomobject]@{ hasNextPage = $LabelsHasNextPage; endCursor = $null }
                nodes = $labelNodes
            }
        }
    }

    function New-SelfTestItem {
        param(
            [string]$Id,
            [string]$ContentType = "Issue",
            [string]$RepositoryName = "Chris0Jeky/Taskdeck",
            [int]$Number = 1,
            [string]$Priority = "Priority I",
            [string]$Status = "Pending",
            [string[]]$Labels = @("Priority I"),
            [string]$Body = "",
            [int]$FieldValueTotalCount = 3,
            [bool]$FieldValuesHasNextPage = $false,
            [int]$LabelTotalCount = -1,
            [bool]$LabelsHasNextPage = $false,
            [object[]]$ClosingIssues = @(),
            [int]$ClosingIssueTotalCount = -1,
            [bool]$ClosingIssuesHasNextPage = $false
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
        if ($ClosingIssueTotalCount -lt 0) {
            $ClosingIssueTotalCount = $ClosingIssues.Count
        }

        $content = [pscustomobject]@{
            __typename = $ContentType
            number = $Number
            title = "Item $Number"
            url = "https://example.test/items/$Number"
            body = $Body
            repository = [pscustomobject]@{ nameWithOwner = $RepositoryName }
            labels = [pscustomobject]@{
                totalCount = $LabelTotalCount
                pageInfo = [pscustomobject]@{ hasNextPage = $LabelsHasNextPage; endCursor = $null }
                nodes = $labelNodes
            }
            closingIssuesReferences = [pscustomobject]@{
                totalCount = $ClosingIssueTotalCount
                pageInfo = [pscustomobject]@{ hasNextPage = $ClosingIssuesHasNextPage; endCursor = $null }
                nodes = @($ClosingIssues)
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

    function Get-NormalizedSelfTestItems {
        param([object[]]$RawItems)

        $pages = @{
            "<start>" = New-SelfTestResponse `
                -TotalCount $RawItems.Count `
                -Nodes $RawItems `
                -HasNextPage $false `
                -EndCursor $null
        }
        @((Get-SelfTestSnapshot -Pages $pages).items)
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

    $strictFallbackItems = Get-NormalizedSelfTestItems -RawItems @(
        (New-SelfTestItem `
            -Id "strict-fallback-pr" `
            -ContentType "PullRequest" `
            -Priority "Priority I" `
            -Body "No issue references")
    )
    $strictFallbackAudit = New-PriorityAuditState `
        -Items $strictFallbackItems `
        -IssueLabelProvider { throw "Unexpected provider call" }
    $checks += Assert-SelfTest `
        -Condition ($strictFallbackAudit.updates.Count -eq 1 -and
            $strictFallbackAudit.updates[0].expectedPriority -ceq "Priority V" -and
            $strictFallbackAudit.updates[0].reason -ceq "pr-no-derived-issue-fallback") `
        -Message "no-reference PRs must enforce Priority V by default"

    $closingProviderState = [pscustomobject]@{ calls = 0 }
    $closingIssue = New-SelfTestClosingIssue `
        -RepositoryName "Chris0Jeky/Taskdeck" `
        -Number 42 `
        -Labels @("Priority II")
    $closingItems = Get-NormalizedSelfTestItems -RawItems @(
        (New-SelfTestItem `
            -Id "closing-pr" `
            -ContentType "PullRequest" `
            -Priority "Priority V" `
            -Body "Refs other/repo#99" `
            -ClosingIssues @($closingIssue))
    )
    $closingAudit = New-PriorityAuditState -Items $closingItems -IssueLabelProvider {
        param($repositoryName, $issueNumber)
        $closingProviderState.calls++
        throw "Body fallback should not run when closing links exist"
    }
    $checks += Assert-SelfTest `
        -Condition ($closingAudit.updates[0].expectedPriority -ceq "Priority II" -and
            $closingAudit.updates[0].reason -ceq "pr-closing-issue" -and
            $closingProviderState.calls -eq 0) `
        -Message "closing issues must win before body references"

    $crossRepoProviderState = [pscustomobject]@{ repository = $null; number = 0; calls = 0 }
    $crossRepoItems = Get-NormalizedSelfTestItems -RawItems @(
        (New-SelfTestItem `
            -Id "cross-repo-pr" `
            -ContentType "PullRequest" `
            -Priority "Priority V" `
            -Body "Refs owner/other#42")
    )
    $crossRepoAudit = New-PriorityAuditState -Items $crossRepoItems -IssueLabelProvider {
        param($repositoryName, $issueNumber)
        $crossRepoProviderState.repository = $repositoryName
        $crossRepoProviderState.number = $issueNumber
        $crossRepoProviderState.calls++
        @("Priority I")
    }
    $checks += Assert-SelfTest `
        -Condition ($crossRepoProviderState.calls -eq 1 -and
            $crossRepoProviderState.repository -ceq "owner/other" -and
            $crossRepoProviderState.number -eq 42 -and
            $crossRepoAudit.updates[0].expectedPriority -ceq "Priority I") `
        -Message "qualified body references must preserve repository identity"

    $checks += Assert-SelfTestThrows -Action {
        New-PriorityAuditState -Items $crossRepoItems -IssueLabelProvider { @() }
    } -MessagePattern "has missing Priority labels"
    $checks += Assert-SelfTestThrows -Action {
        New-PriorityAuditState -Items $crossRepoItems -IssueLabelProvider { @("Priority I", "Priority II") }
    } -MessagePattern "has multiple Priority labels"

    $truncatedClosing = New-SelfTestClosingIssue -RepositoryName "Chris0Jeky/Taskdeck" -Number 43
    $truncatedClosingPages = @{
        "<start>" = New-SelfTestResponse `
            -TotalCount 1 `
            -Nodes @((New-SelfTestItem `
                -Id "closing-truncated" `
                -ContentType "PullRequest" `
                -ClosingIssues @($truncatedClosing) `
                -ClosingIssueTotalCount 2 `
                -ClosingIssuesHasNextPage $true)) `
            -HasNextPage $false `
            -EndCursor $null
    }
    $checks += Assert-SelfTestThrows -Action {
        Get-SelfTestSnapshot -Pages $truncatedClosingPages
    } -MessagePattern "closingIssuesReferences was truncated"

    $testOptionMap = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
    $testOptionMap.Add("Priority I", "option-i")
    $testOptionMap.Add("Priority II", "option-ii")
    $testOptionMap.Add("Priority V", "option-v")
    $strictSnapshotPages = @{
        "<start>" = New-SelfTestResponse `
            -TotalCount 1 `
            -Nodes @((New-SelfTestItem `
                -Id "source-drift-pr" `
                -ContentType "PullRequest" `
                -Priority "Priority V" `
                -Body "Refs owner/other#42")) `
            -HasNextPage $false `
            -EndCursor $null
    }
    $sourceSnapshot = Get-SelfTestSnapshot -Pages $strictSnapshotPages
    $sourceAuditInitial = New-PriorityAuditState -Items $sourceSnapshot.items -IssueLabelProvider { @("Priority II") }
    $sourceAuditCurrent = New-PriorityAuditState -Items $sourceSnapshot.items -IssueLabelProvider { @("Priority I") }
    $sourceInitialState = New-PriorityExecutionState `
        -Snapshot $sourceSnapshot `
        -AuditState $sourceAuditInitial `
        -PriorityFieldId $priorityFieldId `
        -StatusFieldId $statusFieldId `
        -OptionMap $testOptionMap
    $sourceCurrentState = New-PriorityExecutionState `
        -Snapshot $sourceSnapshot `
        -AuditState $sourceAuditCurrent `
        -PriorityFieldId $priorityFieldId `
        -StatusFieldId $statusFieldId `
        -OptionMap $testOptionMap
    $sourceGuardState = [pscustomobject]@{ providerCalls = 0; writes = 0 }
    $checks += Assert-SelfTestThrows -Action {
        Invoke-PriorityUpdatePlan `
            -InitialState $sourceInitialState `
            -OptionMap $testOptionMap `
            -CurrentStateProvider {
                $sourceGuardState.providerCalls++
                $sourceCurrentState
            } `
            -ItemWriter {
                param($update, $optionId)
                $sourceGuardState.writes++
            }
    } -MessagePattern "derivation/write plan drifted"
    $checks += Assert-SelfTest `
        -Condition ($sourceGuardState.providerCalls -eq 1 -and $sourceGuardState.writes -eq 0) `
        -Message "source drift must abort before item-edit"

    $missingOptionMap = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
    $missingOptionMap.Add("Priority I", "option-i")
    $missingOptionState = [pscustomobject]@{
        guardFingerprint = "unchanged"
        updates = @(
            [pscustomobject]@{ itemId = "first"; expectedPriority = "Priority I" },
            [pscustomobject]@{ itemId = "second"; expectedPriority = "Priority II" }
        )
    }
    $missingOptionGuard = [pscustomobject]@{ providerCalls = 0; writes = 0 }
    $checks += Assert-SelfTestThrows -Action {
        Invoke-PriorityUpdatePlan `
            -InitialState $missingOptionState `
            -OptionMap $missingOptionMap `
            -CurrentStateProvider {
                $missingOptionGuard.providerCalls++
                $missingOptionState
            } `
            -ItemWriter {
                param($update, $optionId)
                $missingOptionGuard.writes++
            }
    } -MessagePattern "Priority option 'Priority II' is not available"
    $checks += Assert-SelfTest `
        -Condition ($missingOptionGuard.providerCalls -eq 0 -and $missingOptionGuard.writes -eq 0) `
        -Message "all option ids must validate before recheck or writes"

    Write-Host "SelfTest passed: $checks checks."
}

function Get-PriorityLabels {
    param(
        [object[]]$Labels
    )

    @($Labels | Where-Object { $priorityRank.ContainsKey([string]$_) })
}

function Get-IssueReferenceKey {
    param(
        [string]$RepositoryName,
        [int]$Number
    )

    if ([string]::IsNullOrWhiteSpace($RepositoryName) -or
        $RepositoryName -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$' -or
        $Number -le 0) {
        throw "Invalid issue reference '$RepositoryName#$Number'."
    }

    "$($RepositoryName.ToLowerInvariant())#$Number"
}

function Get-BodyIssueReferences {
    param(
        [string]$Body,
        [string]$DefaultRepository
    )

    if ([string]::IsNullOrWhiteSpace($Body)) {
        return @()
    }

    $pattern = '(?im)\b(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?|refs?|references?)\s+(?:(?<repository>[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+))?#(?<number>\d+)'
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    @([regex]::Matches($Body, $pattern) | ForEach-Object {
        $repositoryName = if ($_.Groups["repository"].Success) {
            $_.Groups["repository"].Value
        } else {
            $DefaultRepository
        }
        $number = [int]$_.Groups["number"].Value
        $key = Get-IssueReferenceKey -RepositoryName $repositoryName -Number $number
        if ($seen.Add($key)) {
            [pscustomobject]@{
                source = "body"
                repository = $repositoryName
                number = $number
                key = $key
            }
        }
    })
}

function Get-PullRequestIssueReferences {
    param(
        [object]$Content
    )

    $closingReferences = @($Content.closingIssues | ForEach-Object {
        $key = Get-IssueReferenceKey -RepositoryName $_.repository -Number $_.number
        [pscustomobject]@{
            source = "closing"
            repository = $_.repository
            number = [int]$_.number
            key = $key
        }
    })
    if ($closingReferences.Count -gt 0) {
        $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
        return @($closingReferences | Where-Object { $seen.Add($_.key) })
    }

    @(Get-BodyIssueReferences -Body $Content.body -DefaultRepository $Content.repository)
}

function Get-LabelSignature {
    param([object[]]$Labels)

    [string[]]$sorted = @($Labels | ForEach-Object { [string]$_ })
    [Array]::Sort($sorted, [System.StringComparer]::Ordinal)
    $sorted -join [char]31
}

function Add-IssueLabelsToCache {
    param(
        [object]$Cache,
        [string]$RepositoryName,
        [int]$Number,
        [object[]]$Labels
    )

    $key = Get-IssueReferenceKey -RepositoryName $RepositoryName -Number $Number
    $signature = Get-LabelSignature -Labels $Labels
    if ($Cache.ContainsKey($key)) {
        if (-not [string]::Equals($Cache[$key].labelSignature, $signature, [System.StringComparison]::Ordinal)) {
            throw "Issue label source drifted within the same snapshot for '$RepositoryName#$Number'."
        }
        return
    }

    $Cache.Add($key, [pscustomobject]@{
        repository = $RepositoryName
        number = $Number
        labels = @($Labels)
        labelSignature = $signature
    })
}

function New-IssueLabelCache {
    param([object[]]$Items)

    $cache = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($item in $Items) {
        if ($null -eq $item.content) {
            continue
        }
        if ($item.content.type -eq "Issue") {
            Add-IssueLabelsToCache -Cache $cache -RepositoryName $item.content.repository -Number $item.content.number -Labels $item.labels
        } elseif ($item.content.type -eq "PullRequest") {
            foreach ($closingIssue in @($item.content.closingIssues)) {
                Add-IssueLabelsToCache -Cache $cache -RepositoryName $closingIssue.repository -Number $closingIssue.number -Labels $closingIssue.labels
            }
        }
    }
    return ,$cache
}

function Get-IssuePriority {
    param(
        [object]$Reference,
        [object]$IssueLabelCache,
        [scriptblock]$IssueLabelProvider
    )

    if (-not $IssueLabelCache.ContainsKey($Reference.key)) {
        if ($null -eq $IssueLabelProvider) {
            throw "No provider is available for referenced issue '$($Reference.repository)#$($Reference.number)'."
        }
        try {
            $labels = @(& $IssueLabelProvider $Reference.repository $Reference.number)
        } catch {
            throw "Failed to read referenced issue '$($Reference.repository)#$($Reference.number)': $($_.Exception.Message)"
        }
        Add-IssueLabelsToCache -Cache $IssueLabelCache -RepositoryName $Reference.repository -Number $Reference.number -Labels $labels
    }

    $entry = $IssueLabelCache[$Reference.key]
    $priorityLabels = @(Get-PriorityLabels -Labels $entry.labels)
    if ($priorityLabels.Count -ne 1) {
        $reason = if ($priorityLabels.Count -eq 0) { "missing" } else { "multiple" }
        throw "Referenced issue '$($Reference.repository)#$($Reference.number)' has $reason Priority labels; refusing Priority V fallback."
    }
    [string]$priorityLabels[0]
}

function Get-PriorityAuditFingerprints {
    param(
        [object[]]$AuditItems,
        [object]$IssueLabelCache
    )

    $auditById = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
    foreach ($item in $AuditItems) {
        $auditById.Add([string]$item.itemId, [pscustomobject]@{
            itemId = [string]$item.itemId
            contentType = [string]$item.contentType
            repository = [string]$item.repository
            number = $item.number
            status = [string]$item.status
            actualPriority = [string]$item.actualPriority
            expectedPriority = [string]$item.expectedPriority
            reason = [string]$item.reason
            references = [string]$item.references
            needsUpdate = [bool]$item.needsUpdate
        })
    }
    [string[]]$auditIds = @($auditById.Keys)
    [Array]::Sort($auditIds, [System.StringComparer]::Ordinal)
    $orderedAudit = @($auditIds | ForEach-Object { $auditById[$_] })

    [string[]]$issueKeys = @($IssueLabelCache.Keys)
    [Array]::Sort($issueKeys, [System.StringComparer]::Ordinal)
    $orderedSources = @($issueKeys | ForEach-Object {
        [pscustomobject]@{ key = $_; labels = $IssueLabelCache[$_].labelSignature }
    })

    $updatesById = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
    foreach ($item in @($AuditItems | Where-Object { $_.needsUpdate })) {
        $updatesById.Add([string]$item.itemId, [pscustomobject]@{
            itemId = [string]$item.itemId
            actualPriority = [string]$item.actualPriority
            expectedPriority = [string]$item.expectedPriority
        })
    }
    [string[]]$updateIds = @($updatesById.Keys)
    [Array]::Sort($updateIds, [System.StringComparer]::Ordinal)
    $orderedPlan = @($updateIds | ForEach-Object { $updatesById[$_] })

    [pscustomobject]@{
        source = ([pscustomobject]@{ audit = $orderedAudit; issueLabels = $orderedSources } | ConvertTo-Json -Compress -Depth 8)
        plan = ($orderedPlan | ConvertTo-Json -Compress -Depth 5)
    }
}

function New-PriorityAuditState {
    param(
        [object[]]$Items,
        [scriptblock]$IssueLabelProvider
    )

    $issueLabelCache = New-IssueLabelCache -Items $Items
    $audit = @(foreach ($item in $Items) {
        $content = $item.content
        if ($null -eq $content) {
            continue
        }

        $expectedPriority = $null
        $reason = $null
        $references = @()

        if ($content.type -eq "Issue") {
            $priorityLabels = @(Get-PriorityLabels -Labels $item.labels)
            if ($priorityLabels.Count -eq 1) {
                $expectedPriority = [string]$priorityLabels[0]
                $reason = "issue-label"
            } elseif ($priorityLabels.Count -eq 0) {
                $reason = "issue-missing-priority-label"
            } else {
                $reason = "issue-multiple-priority-labels"
            }
        } elseif ($content.type -eq "PullRequest") {
            $references = @(Get-PullRequestIssueReferences -Content $content)
            if ($references.Count -gt 0) {
                $referencedPriorities = @($references | ForEach-Object {
                    Get-IssuePriority -Reference $_ -IssueLabelCache $issueLabelCache -IssueLabelProvider $IssueLabelProvider
                })
                $expectedPriority = [string](@($referencedPriorities | Sort-Object { $priorityRank[$_] })[0])
                $reason = if ($references[0].source -eq "closing") { "pr-closing-issue" } else { "pr-body-reference" }
            } else {
                $expectedPriority = "Priority V"
                $reason = "pr-no-derived-issue-fallback"
            }
        } else {
            $reason = "unsupported-content-type"
        }

        $actualPriority = [string]$item.priority
        $needsUpdate = -not [string]::IsNullOrWhiteSpace($expectedPriority) -and
            -not [string]::Equals($actualPriority, $expectedPriority, [System.StringComparison]::Ordinal)
        $referenceText = @($references | ForEach-Object { "$($_.source):$($_.repository)#$($_.number)" }) -join ", "

        [pscustomobject]@{
            itemId = $item.id
            contentType = $content.type
            repository = $content.repository
            number = $content.number
            title = $content.title
            url = $content.url
            status = $item.status
            actualPriority = $actualPriority
            expectedPriority = $expectedPriority
            reason = $reason
            references = $referenceText
            needsUpdate = $needsUpdate
        }
    })

    Assert-AuditableIssuePriorities -AuditItems $audit
    $fingerprints = Get-PriorityAuditFingerprints -AuditItems $audit -IssueLabelCache $issueLabelCache
    [pscustomobject]@{
        audit = $audit
        updates = @($audit | Where-Object { $_.needsUpdate })
        sourceFingerprint = $fingerprints.source
        planFingerprint = $fingerprints.plan
    }
}

function Get-PriorityOptionMap {
    param([object]$PriorityField)

    $map = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
    $ids = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($option in @($PriorityField.options)) {
        $name = [string]$option.name
        $id = [string]$option.id
        if ([string]::IsNullOrWhiteSpace($name) -or [string]::IsNullOrWhiteSpace($id)) {
            throw "Priority field contains an option with a blank name or id."
        }
        if ($map.ContainsKey($name) -or -not $ids.Add($id)) {
            throw "Priority field contains duplicate option name/id data for '$name'."
        }
        $map.Add($name, $id)
    }
    return ,$map
}

function Get-PriorityOptionFingerprint {
    param([object]$OptionMap)

    [string[]]$names = @($OptionMap.Keys)
    [Array]::Sort($names, [System.StringComparer]::Ordinal)
    @($names | ForEach-Object { "$_=$($OptionMap[$_])" }) -join [char]31
}

function Assert-PriorityOptionsForUpdates {
    param(
        [object[]]$Updates,
        [object]$OptionMap
    )

    foreach ($expectedPriority in @($Updates.expectedPriority | Select-Object -Unique)) {
        if ([string]::IsNullOrWhiteSpace([string]$expectedPriority) -or
            -not $OptionMap.ContainsKey([string]$expectedPriority) -or
            [string]::IsNullOrWhiteSpace([string]$OptionMap[[string]$expectedPriority])) {
            throw "Priority option '$expectedPriority' is not available for the complete update plan."
        }
    }
}

function New-PriorityExecutionState {
    param(
        [object]$Snapshot,
        [object]$AuditState,
        [string]$PriorityFieldId,
        [string]$StatusFieldId,
        [object]$OptionMap
    )

    $guard = [pscustomobject]@{
        projectId = $Snapshot.projectId
        totalCount = $Snapshot.totalCount
        projectUpdatedAt = $Snapshot.projectUpdatedAt
        priorityFieldId = $PriorityFieldId
        statusFieldId = $StatusFieldId
        options = Get-PriorityOptionFingerprint -OptionMap $OptionMap
        sources = $AuditState.sourceFingerprint
        plan = $AuditState.planFingerprint
    } | ConvertTo-Json -Compress -Depth 5

    [pscustomobject]@{
        guardFingerprint = $guard
        audit = $AuditState.audit
        updates = $AuditState.updates
        snapshot = $Snapshot
    }
}

function Invoke-PriorityUpdatePlan {
    param(
        [object]$InitialState,
        [object]$OptionMap,
        [scriptblock]$CurrentStateProvider,
        [scriptblock]$ItemWriter
    )

    Assert-PriorityOptionsForUpdates -Updates $InitialState.updates -OptionMap $OptionMap
    $currentState = & $CurrentStateProvider
    if ($null -eq $currentState -or
        -not [string]::Equals($InitialState.guardFingerprint, $currentState.guardFingerprint, [System.StringComparison]::Ordinal)) {
        throw "Project priority derivation/write plan drifted after the audit; refusing -Apply before the first write."
    }

    $applied = 0
    foreach ($update in @($InitialState.updates)) {
        try {
            & $ItemWriter $update $OptionMap[$update.expectedPriority] | Out-Null
            $applied++
        } catch {
            throw "Project priority apply failed after $applied of $($InitialState.updates.Count) writes. Writes are non-transactional: $($_.Exception.Message)"
        }
    }
    $applied
}

function Get-LivePriorityExecutionContext {
    param(
        [string]$OwnerName,
        [int]$Number,
        [string]$ProjectId,
        [int]$ItemLimit,
        [scriptblock]$IssueLabelProvider
    )

    $fieldList = Invoke-GhJson -Arguments @("project", "field-list", "$Number", "--owner", $OwnerName, "--format", "json")
    $priorityField = @($fieldList.fields | Where-Object { $_.name -eq "Priority" }) | Select-Object -First 1
    if ($null -eq $priorityField -or [string]::IsNullOrWhiteSpace([string]$priorityField.id)) {
        throw "Project $Number does not expose a Priority field."
    }
    $statusField = @($fieldList.fields | Where-Object { $_.name -eq "Status" }) | Select-Object -First 1
    if ($null -eq $statusField -or [string]::IsNullOrWhiteSpace([string]$statusField.id)) {
        throw "Project $Number does not expose a Status field."
    }
    $optionMap = Get-PriorityOptionMap -PriorityField $priorityField

    $pageProvider = {
        param($requestedProjectId, $after)
        Invoke-ProjectItemsPage -ProjectId $requestedProjectId -After $after
    }
    $snapshot = Get-CompleteProjectSnapshot `
        -ProjectId $ProjectId `
        -PriorityFieldId $priorityField.id `
        -StatusFieldId $statusField.id `
        -ItemLimit $ItemLimit `
        -PageProvider $pageProvider
    if ($snapshot.projectNumber -ne $Number) {
        throw "ProjectV2 '$ProjectId' reported number $($snapshot.projectNumber), expected $Number."
    }

    $auditState = New-PriorityAuditState -Items @($snapshot.items) -IssueLabelProvider $IssueLabelProvider
    $executionState = New-PriorityExecutionState `
        -Snapshot $snapshot `
        -AuditState $auditState `
        -PriorityFieldId $priorityField.id `
        -StatusFieldId $statusField.id `
        -OptionMap $optionMap

    [pscustomobject]@{
        priorityField = $priorityField
        statusField = $statusField
        optionMap = $optionMap
        executionState = $executionState
    }
}

if ($SelfTest) {
    Invoke-SelfTest
    return
}

$gh = Get-Command gh -ErrorAction SilentlyContinue
if ($null -eq $gh) {
    throw "GitHub CLI 'gh' is required. Install it or use GitHub MCP as the fallback."
}

$projectList = Invoke-GhJson -Arguments @("project", "list", "--owner", $Owner, "--format", "json")
$project = @($projectList.projects | Where-Object { $_.number -eq $ProjectNumber }) | Select-Object -First 1
if ($null -eq $project) {
    throw "Project number $ProjectNumber was not found for owner $Owner."
}

$issueLabelProvider = {
    param($repositoryName, $issueNumber)
    $issue = Invoke-GhJson -Arguments @("issue", "view", "$issueNumber", "--repo", $repositoryName, "--json", "labels")
    @($issue.labels | ForEach-Object { [string]$_.name })
}

$context = Get-LivePriorityExecutionContext `
    -OwnerName $Owner `
    -Number $ProjectNumber `
    -ProjectId $project.id `
    -ItemLimit $Limit `
    -IssueLabelProvider $issueLabelProvider
$executionState = $context.executionState
$snapshot = $executionState.snapshot
$items = @($snapshot.items)
$audit = @($executionState.audit)
$updates = @($executionState.updates)
$appliedCount = 0
$postApplyVerified = $false

if ($Apply) {
    $currentStateProvider = {
        (Get-LivePriorityExecutionContext `
            -OwnerName $Owner `
            -Number $ProjectNumber `
            -ProjectId $project.id `
            -ItemLimit $Limit `
            -IssueLabelProvider $issueLabelProvider).executionState
    }
    $itemWriter = {
        param($update, $optionId)
        $applyOutput = & $gh.Source project item-edit `
            --id $update.itemId `
            --project-id $project.id `
            --field-id $context.priorityField.id `
            --single-select-option-id $optionId 2>&1
        if ($LASTEXITCODE -ne 0) {
            if ("$applyOutput" -match "missing required scopes \[project\]") {
                throw "GitHub CLI is missing the 'project' scope. Run: gh auth refresh -s project"
            }
            throw "Failed to update project item $($update.itemId): $applyOutput"
        }
    }

    $appliedCount = Invoke-PriorityUpdatePlan `
        -InitialState $executionState `
        -OptionMap $context.optionMap `
        -CurrentStateProvider $currentStateProvider `
        -ItemWriter $itemWriter

    try {
        $postApplyContext = Get-LivePriorityExecutionContext `
            -OwnerName $Owner `
            -Number $ProjectNumber `
            -ProjectId $project.id `
            -ItemLimit $Limit `
            -IssueLabelProvider $issueLabelProvider
        if ($postApplyContext.executionState.updates.Count -ne 0) {
            throw "post-Apply audit still reports $($postApplyContext.executionState.updates.Count) update(s)"
        }
        $postApplyVerified = $true
    } catch {
        throw "Applied $appliedCount writes, but the complete post-Apply audit failed. Writes are non-transactional: $($_.Exception.Message)"
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
        applied = $appliedCount
        postApplyVerified = $postApplyVerified
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
    Write-Host "Items updated: $appliedCount"
    Write-Host "Post-Apply complete audit: VERIFIED"
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
