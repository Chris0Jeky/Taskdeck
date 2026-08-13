[CmdletBinding()]
param(
    [string]$Owner = "Chris0Jeky",
    [int]$ProjectNumber = 1,
    [string]$Repository = "Chris0Jeky/Taskdeck",
    [ValidateRange(0, [int]::MaxValue)]
    [int]$Limit = 0,
    # Retained for CLI compatibility; canonical Priority V fallback is now unconditional.
    [switch]$StrictFallbackPriority,
    [switch]$Apply,
    [switch]$Json,
    [switch]$SelfTest
)

$ErrorActionPreference = "Stop"

class ProjectSnapshotDriftException : System.Exception {
    [string]$DriftKind
    [int]$PageNumber

    ProjectSnapshotDriftException([string]$Message, [string]$DriftKind, [int]$PageNumber) : base($Message) {
        $this.DriftKind = $DriftKind
        $this.PageNumber = $PageNumber
    }
}

class ProjectSnapshotRestartExhaustedException : System.Exception {
    [object[]]$Diagnostics

    ProjectSnapshotRestartExhaustedException([string]$Message, [object[]]$Diagnostics) : base($Message) {
        $this.Diagnostics = $Diagnostics
    }
}

$script:MaxProjectSnapshotRestarts = 2

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

function ConvertFrom-GitHubIssueApiTarget {
    param(
        [Parameter(Mandatory = $true)]
        [AllowNull()]
        [object]$Target,
        [Parameter(Mandatory = $true)]
        [string]$RequestedRepository,
        [Parameter(Mandatory = $true)]
        [int]$RequestedNumber
    )

    $requestedKey = Get-IssueReferenceKey -RepositoryName $RequestedRepository -Number $RequestedNumber
    if ($null -eq $Target -or
        -not ($Target.PSObject.Properties.Name -contains "number") -or
        -not ($Target.PSObject.Properties.Name -contains "repository_url") -or
        [string]::IsNullOrWhiteSpace([string]$Target.repository_url)) {
        throw "GitHub returned an incomplete typed reference target for '$RequestedRepository#$RequestedNumber'."
    }

    try {
        $repositoryUri = [System.Uri][string]$Target.repository_url
        $repositoryPath = $repositoryUri.AbsolutePath.Trim("/")
    } catch {
        throw "GitHub returned an invalid repository identity '$($Target.repository_url)' for '$RequestedRepository#$RequestedNumber'."
    }
    $repositorySegments = @($repositoryPath.Split("/"))
    if ($repositorySegments.Count -ne 3 -or
        -not [string]::Equals($repositorySegments[0], "repos", [System.StringComparison]::OrdinalIgnoreCase) -or
        [string]::IsNullOrWhiteSpace($repositorySegments[1]) -or
        [string]::IsNullOrWhiteSpace($repositorySegments[2])) {
        throw "GitHub returned an invalid repository identity '$($Target.repository_url)' for '$RequestedRepository#$RequestedNumber'."
    }

    $canonicalRepository = "$($repositorySegments[1])/$($repositorySegments[2])"
    try {
        $targetNumber = [int]$Target.number
        $targetKey = Get-IssueReferenceKey -RepositoryName $canonicalRepository -Number $targetNumber
    } catch {
        throw "GitHub returned an invalid repository or number identity for '$RequestedRepository#$RequestedNumber'."
    }
    if (-not [string]::Equals($requestedKey, $targetKey, [System.StringComparison]::Ordinal)) {
        throw "GitHub reference identity mismatch for '$RequestedRepository#$RequestedNumber': returned '$canonicalRepository#$targetNumber'."
    }

    $hasPullRequestMetadata = $Target.PSObject.Properties.Name -contains "pull_request"
    if ($hasPullRequestMetadata -and $null -eq $Target.pull_request) {
        throw "GitHub returned null PullRequest metadata for '$RequestedRepository#$RequestedNumber'."
    }
    $contentType = if ($hasPullRequestMetadata) { "PullRequest" } else { "Issue" }
    $labels = if ($contentType -eq "Issue") {
        if (-not ($Target.PSObject.Properties.Name -contains "labels") -or $null -eq $Target.labels) {
            throw "GitHub returned an Issue without label data for '$RequestedRepository#$RequestedNumber'."
        }
        @($Target.labels | ForEach-Object {
            if ($null -eq $_ -or
                -not ($_.PSObject.Properties.Name -contains "name") -or
                [string]::IsNullOrWhiteSpace([string]$_.name)) {
                throw "GitHub returned malformed Issue label data for '$RequestedRepository#$RequestedNumber'."
            }
            [string]$_.name
        })
    } else {
        @()
    }

    [pscustomobject]@{
        type = $contentType
        repository = $canonicalRepository
        number = $targetNumber
        labels = $labels
    }
}

function Get-LiveReferenceTarget {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryName,
        [Parameter(Mandatory = $true)]
        [int]$Number
    )

    Get-IssueReferenceKey -RepositoryName $RepositoryName -Number $Number | Out-Null
    $target = Invoke-GhJson -Arguments @("api", "repos/$RepositoryName/issues/$Number")
    ConvertFrom-GitHubIssueApiTarget `
        -Target $target `
        -RequestedRepository $RepositoryName `
        -RequestedNumber $Number
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

    $hasNextPage = $Connection.pageInfo.hasNextPage
    if ($null -eq $hasNextPage -or $hasNextPage.GetType() -ne [bool]) {
        $actualType = if ($null -eq $hasNextPage) { "null" } else { $hasNextPage.GetType().FullName }
        throw "$Context returned malformed hasNextPage metadata; expected Boolean, got $actualType."
    }

    $nodes = @($Connection.nodes)
    $totalCount = [int]$Connection.totalCount
    if ($totalCount -lt 0) {
        throw "$Context returned a negative totalCount."
    }

    if ($hasNextPage -or $nodes.Count -ne $totalCount) {
        throw "$Context was truncated: received $($nodes.Count) of $totalCount nodes (hasNextPage=$hasNextPage)."
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

        $hasNextPage = $projectNode.items.pageInfo.hasNextPage
        if ($null -eq $hasNextPage -or $hasNextPage.GetType() -ne [bool]) {
            $actualType = if ($null -eq $hasNextPage) { "null" } else { $hasNextPage.GetType().FullName }
            throw "ProjectV2 page $pageCount returned malformed hasNextPage metadata; expected Boolean, got $actualType."
        }

        $pageTotalCount = [int]$projectNode.items.totalCount
        $pageUpdatedAt = [string]$projectNode.updatedAt
        if ($pageTotalCount -lt 0) {
            throw "ProjectV2 page $pageCount returned a negative totalCount."
        }
        if ($ItemLimit -gt 0 -and $pageTotalCount -gt $ItemLimit) {
            throw "Project contains $pageTotalCount items, exceeding -Limit $ItemLimit. Completeness cannot be established within the configured ceiling."
        }

        if ($null -eq $expectedTotalCount) {
            $expectedTotalCount = $pageTotalCount
            $expectedUpdatedAt = $pageUpdatedAt
            $projectTitle = [string]$projectNode.title
            $projectNumberFromGraphQl = [int]$projectNode.number
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

        $endCursor = $null
        if ($hasNextPage) {
            $endCursor = [string]$projectNode.items.pageInfo.endCursor
            if ([string]::IsNullOrWhiteSpace($endCursor)) {
                throw "ProjectV2 page $pageCount reported hasNextPage without an endCursor."
            }
            if ([string]::Equals($endCursor, $after, [System.StringComparison]::Ordinal) -or $seenCursors.Contains($endCursor)) {
                throw "ProjectV2 pagination cursor did not advance at page $pageCount ('$endCursor')."
            }
        }

        # Intrinsic page-integrity faults above are never retryable, even when the
        # same page also reports a changed snapshot stamp or count.
        if ($pageCount -gt 1) {
            if ($pageTotalCount -ne $expectedTotalCount) {
                throw [ProjectSnapshotDriftException]::new(
                    "Project totalCount drifted during pagination: expected $expectedTotalCount, page $pageCount reported $pageTotalCount.",
                    "totalCount",
                    $pageCount)
            }
            if ($pageUpdatedAt -ne $expectedUpdatedAt) {
                throw [ProjectSnapshotDriftException]::new(
                    "Project updatedAt drifted during pagination: expected '$expectedUpdatedAt', page $pageCount reported '$pageUpdatedAt'.",
                    "updatedAt",
                    $pageCount)
            }
        }

        if ($normalizedItems.Count -gt $expectedTotalCount) {
            throw "ProjectV2 pagination returned more items ($($normalizedItems.Count)) than totalCount ($expectedTotalCount)."
        }
        if (-not $hasNextPage) {
            break
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

function Get-CompleteProjectSnapshotWithRestart {
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
        [scriptblock]$PageProvider,
        [ValidateRange(0, 10)]
        [int]$MaxRestarts = $script:MaxProjectSnapshotRestarts
    )

    $restartDiagnostics = [System.Collections.Generic.List[object]]::new()
    for ($attempt = 1; $attempt -le ($MaxRestarts + 1); $attempt++) {
        try {
            $snapshot = Get-CompleteProjectSnapshot `
                -ProjectId $ProjectId `
                -PriorityFieldId $PriorityFieldId `
                -StatusFieldId $StatusFieldId `
                -ItemLimit $ItemLimit `
                -PageProvider $PageProvider

            $snapshot | Add-Member -NotePropertyName snapshotRestartCount -NotePropertyValue $restartDiagnostics.Count -Force
            $snapshot | Add-Member -NotePropertyName snapshotRestartDiagnostics -NotePropertyValue $restartDiagnostics.ToArray() -Force
            return $snapshot
        } catch [ProjectSnapshotDriftException] {
            $drift = $_.Exception
            $restartDiagnostics.Add([pscustomobject]@{
                attempt = $attempt
                kind = $drift.DriftKind
                page = $drift.PageNumber
                message = $drift.Message
            }) | Out-Null

            if ($attempt -gt $MaxRestarts) {
                $diagnosticText = @($restartDiagnostics | ForEach-Object {
                    "attempt $($_.attempt): $($_.kind) drift at page $($_.page)"
                }) -join "; "
                throw [ProjectSnapshotRestartExhaustedException]::new(
                    "Project snapshot restart bound exhausted after $MaxRestarts restart(s): $diagnosticText.",
                    $restartDiagnostics.ToArray())
            }
        }
    }

    throw "Project snapshot restart loop terminated unexpectedly."
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
    $canonicalRepository = "Chris0Jeky/Taskdeck"
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
            [object]$LabelsHasNextPage = $false
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

    function New-SelfTestReferenceTarget {
        param(
            [ValidateSet("Issue", "PullRequest")]
            [string]$Type = "Issue",
            [string]$RepositoryName = "Chris0Jeky/Taskdeck",
            [int]$Number = 1,
            [string[]]$Labels = @("Priority I")
        )

        [pscustomobject]@{
            type = $Type
            repository = $RepositoryName
            number = $Number
            labels = @($Labels)
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
            [object]$FieldValuesHasNextPage = $false,
            [int]$LabelTotalCount = -1,
            [object]$LabelsHasNextPage = $false,
            [object[]]$ClosingIssues = @(),
            [int]$ClosingIssueTotalCount = -1,
            [object]$ClosingIssuesHasNextPage = $false
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
            [object]$HasNextPage,
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

    function Get-SelfTestSnapshotWithRestart {
        param(
            [Parameter(Mandatory = $true)]
            [scriptblock]$PageProvider,
            [int]$TestLimit = 0,
            [int]$MaxRestarts = 2
        )

        Get-CompleteProjectSnapshotWithRestart `
            -ProjectId $projectId `
            -PriorityFieldId $priorityFieldId `
            -StatusFieldId $statusFieldId `
            -ItemLimit $TestLimit `
            -MaxRestarts $MaxRestarts `
            -PageProvider $PageProvider
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

    $rawIssueTarget = ConvertFrom-GitHubIssueApiTarget `
        -Target ([pscustomobject]@{
            number = 42
            repository_url = "https://api.github.com/repos/Chris0Jeky/Taskdeck"
            labels = @(
                [pscustomobject]@{ name = "Priority II" },
                [pscustomobject]@{ name = "bug" }
            )
        }) `
        -RequestedRepository $canonicalRepository `
        -RequestedNumber 42
    $checks += Assert-SelfTest `
        -Condition ($rawIssueTarget.type -ceq "Issue" -and
            $rawIssueTarget.repository -ceq $canonicalRepository -and
            $rawIssueTarget.number -eq 42 -and
            [string]::Join("|", @($rawIssueTarget.labels)) -ceq "Priority II|bug") `
        -Message "raw REST Issue normalization must preserve canonical identity and labels"

    $rawPullRequestTarget = ConvertFrom-GitHubIssueApiTarget `
        -Target ([pscustomobject]@{
            number = 43
            repository_url = "https://api.github.com/repos/Chris0Jeky/Taskdeck"
            labels = @([pscustomobject]@{ name = "Priority I" })
            pull_request = [pscustomobject]@{ url = "https://api.github.com/repos/Chris0Jeky/Taskdeck/pulls/43" }
        }) `
        -RequestedRepository $canonicalRepository `
        -RequestedNumber 43
    $checks += Assert-SelfTest `
        -Condition ($rawPullRequestTarget.type -ceq "PullRequest" -and
            $rawPullRequestTarget.repository -ceq $canonicalRepository -and
            $rawPullRequestTarget.number -eq 43 -and
            @($rawPullRequestTarget.labels).Count -eq 0) `
        -Message "raw REST PullRequest normalization must ignore coincidental Priority labels"

    $checks += Assert-SelfTestThrows -Action {
        ConvertFrom-GitHubIssueApiTarget `
            -Target ([pscustomobject]@{
                repository_url = "https://api.github.com/repos/Chris0Jeky/Taskdeck"
                labels = @()
            }) `
            -RequestedRepository $canonicalRepository `
            -RequestedNumber 42
    } -MessagePattern "incomplete typed reference target"
    $checks += Assert-SelfTestThrows -Action {
        ConvertFrom-GitHubIssueApiTarget `
            -Target ([pscustomobject]@{
                number = 42
                repository_url = "https://api.github.com/repositories/123"
                labels = @()
            }) `
            -RequestedRepository $canonicalRepository `
            -RequestedNumber 42
    } -MessagePattern "invalid repository identity"
    $checks += Assert-SelfTestThrows -Action {
        ConvertFrom-GitHubIssueApiTarget `
            -Target ([pscustomobject]@{
                number = 42
                repository_url = "https://api.github.com/repos/Chris0Jeky/Taskdeck"
            }) `
            -RequestedRepository $canonicalRepository `
            -RequestedNumber 42
    } -MessagePattern "Issue without label data"
    $checks += Assert-SelfTestThrows -Action {
        ConvertFrom-GitHubIssueApiTarget `
            -Target ([pscustomobject]@{
                number = 42
                repository_url = "https://api.github.com/repos/Chris0Jeky/Taskdeck"
                labels = @([pscustomobject]@{ name = "" })
            }) `
            -RequestedRepository $canonicalRepository `
            -RequestedNumber 42
    } -MessagePattern "malformed Issue label data"
    $checks += Assert-SelfTestThrows -Action {
        ConvertFrom-GitHubIssueApiTarget `
            -Target ([pscustomobject]@{
                number = 42
                repository_url = "https://api.github.com/repos/Chris0Jeky/Taskdeck"
                labels = @()
                pull_request = $null
            }) `
            -RequestedRepository $canonicalRepository `
            -RequestedNumber 42
    } -MessagePattern "null PullRequest metadata"
    $checks += Assert-SelfTestThrows -Action {
        ConvertFrom-GitHubIssueApiTarget `
            -Target ([pscustomobject]@{
                number = 42
                repository_url = "https://api.github.com/repos/owner/other"
                labels = @()
            }) `
            -RequestedRepository $canonicalRepository `
            -RequestedNumber 42
    } -MessagePattern "identity mismatch"
    $checks += Assert-SelfTestThrows -Action {
        ConvertFrom-GitHubIssueApiTarget `
            -Target ([pscustomobject]@{
                number = 99
                repository_url = "https://api.github.com/repos/Chris0Jeky/Taskdeck"
                labels = @()
            }) `
            -RequestedRepository $canonicalRepository `
            -RequestedNumber 42
    } -MessagePattern "identity mismatch"

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
    $largeRestartProvider = {
        param($requestedProjectId, $after)
        $key = if ([string]::IsNullOrWhiteSpace([string]$after)) { "<start>" } else { [string]$after }
        $largePages[$key]
    }
    $largeRestartSnapshot = Get-SelfTestSnapshotWithRestart `
        -PageProvider $largeRestartProvider `
        -TestLimit 1001 `
        -MaxRestarts 0
    $checks += Assert-SelfTest `
        -Condition ($largeRestartSnapshot.complete -and $largeRestartSnapshot.items.Count -eq 1001) `
        -Message "restart wrapper must preserve the CLI ItemLimit range above ten"

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

    $outerMalformedCases = @(
        [pscustomobject]@{ name = "null"; value = $null },
        [pscustomobject]@{ name = "string"; value = "false" },
        [pscustomobject]@{ name = "number"; value = 0 },
        [pscustomobject]@{ name = "object"; value = [pscustomobject]@{} }
    )
    foreach ($malformedCase in $outerMalformedCases) {
        $malformedPages = @{
            "<start>" = New-SelfTestResponse `
                -TotalCount 1 `
                -Nodes @($itemA) `
                -HasNextPage $malformedCase.value `
                -EndCursor $null
        }
        $checks += Assert-SelfTestThrows `
            -Action { Get-SelfTestSnapshot -Pages $malformedPages } `
            -MessagePattern "malformed hasNextPage metadata"
    }

    $nestedFieldValuesMalformedItem = New-SelfTestItem -Id "nested-field-values-malformed" -FieldValuesHasNextPage "false"
    $nestedLabelsMalformedItem = New-SelfTestItem -Id "nested-labels-malformed" -LabelsHasNextPage $null
    $nestedClosingIssueLabelsMalformed = New-SelfTestClosingIssue `
        -RepositoryName $canonicalRepository `
        -Number 44 `
        -LabelsHasNextPage ([pscustomobject]@{})
    $nestedClosingReferencesMalformedItem = New-SelfTestItem `
        -Id "nested-closing-references-malformed" `
        -ContentType "PullRequest" `
        -ClosingIssues @($nestedClosingIssueLabelsMalformed) `
        -ClosingIssuesHasNextPage 0
    $nestedMalformedCases = @(
        [pscustomobject]@{ name = "fieldValues"; item = $nestedFieldValuesMalformedItem },
        [pscustomobject]@{ name = "labels"; item = $nestedLabelsMalformedItem },
        [pscustomobject]@{ name = "closingIssuesReferences"; item = $nestedClosingReferencesMalformedItem }
    )
    foreach ($malformedCase in $nestedMalformedCases) {
        $nestedMalformedPages = @{
            "<start>" = New-SelfTestResponse `
                -TotalCount 1 `
                -Nodes @($malformedCase.item) `
                -HasNextPage $false `
                -EndCursor $null
        }
        $checks += Assert-SelfTestThrows `
            -Action { Get-SelfTestSnapshot -Pages $nestedMalformedPages } `
            -MessagePattern "malformed hasNextPage metadata"
    }

    $nestedClosingLabelsMalformedItem = New-SelfTestItem `
        -Id "nested-closing-labels-malformed" `
        -ContentType "PullRequest" `
        -ClosingIssues @(
            (New-SelfTestClosingIssue `
                -RepositoryName $canonicalRepository `
                -Number 45 `
                -LabelsHasNextPage ([pscustomobject]@{}))
        )
    $nestedClosingLabelsMalformedPages = @{
        "<start>" = New-SelfTestResponse `
            -TotalCount 1 `
            -Nodes @($nestedClosingLabelsMalformedItem) `
            -HasNextPage $false `
            -EndCursor $null
    }
    $checks += Assert-SelfTestThrows `
        -Action { Get-SelfTestSnapshot -Pages $nestedClosingLabelsMalformedPages } `
        -MessagePattern "malformed hasNextPage metadata"

    $mixedOuterMalformedState = [pscustomobject]@{ starts = 0; calls = 0 }
    $mixedOuterMalformedProvider = {
        param($requestedProjectId, $after)
        $mixedOuterMalformedState.calls++
        if ([string]::IsNullOrWhiteSpace([string]$after)) {
            $mixedOuterMalformedState.starts++
            return (New-SelfTestResponse -TotalCount 2 -Nodes @($itemA) -HasNextPage $true -EndCursor "mixed-malformed")
        }
        New-SelfTestResponse `
            -TotalCount 3 `
            -Nodes @($itemB) `
            -HasNextPage "false" `
            -EndCursor $null `
            -UpdatedAt "2026-07-26T00:00:01Z"
    }
    $checks += Assert-SelfTestThrows -Action {
        Get-SelfTestSnapshotWithRestart -PageProvider $mixedOuterMalformedProvider -MaxRestarts 2
    } -MessagePattern "malformed hasNextPage metadata"
    $checks += Assert-SelfTest `
        -Condition ($mixedOuterMalformedState.calls -eq 2 -and $mixedOuterMalformedState.starts -eq 1) `
        -Message "outer malformed pagination metadata must not retry when the same page also reports drift"

    $mixedNestedMalformedState = [pscustomobject]@{ starts = 0; calls = 0 }
    $mixedNestedMalformedItem = New-SelfTestItem -Id "mixed-nested-malformed" -FieldValuesHasNextPage "false"
    $mixedNestedMalformedProvider = {
        param($requestedProjectId, $after)
        $mixedNestedMalformedState.calls++
        if ([string]::IsNullOrWhiteSpace([string]$after)) {
            $mixedNestedMalformedState.starts++
            return (New-SelfTestResponse -TotalCount 2 -Nodes @($itemA) -HasNextPage $true -EndCursor "mixed-nested-malformed")
        }
        New-SelfTestResponse `
            -TotalCount 3 `
            -Nodes @($mixedNestedMalformedItem) `
            -HasNextPage $false `
            -EndCursor $null `
            -UpdatedAt "2026-07-26T00:00:01Z"
    }
    $checks += Assert-SelfTestThrows -Action {
        Get-SelfTestSnapshotWithRestart -PageProvider $mixedNestedMalformedProvider -MaxRestarts 2
    } -MessagePattern "malformed hasNextPage metadata"
    $checks += Assert-SelfTest `
        -Condition ($mixedNestedMalformedState.calls -eq 2 -and $mixedNestedMalformedState.starts -eq 1) `
        -Message "nested malformed pagination metadata must not retry when the same page also reports drift"

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

    $mixedDuplicateState = [pscustomobject]@{ starts = 0; calls = 0 }
    $mixedDuplicateProvider = {
        param($requestedProjectId, $after)
        $mixedDuplicateState.calls++
        if ([string]::IsNullOrWhiteSpace([string]$after)) {
            $mixedDuplicateState.starts++
            if ($mixedDuplicateState.starts -eq 1) {
                return (New-SelfTestResponse -TotalCount 2 -Nodes @($itemA) -HasNextPage $true -EndCursor "mixed-duplicate")
            }
            return (New-SelfTestResponse -TotalCount 1 -Nodes @($itemB) -HasNextPage $false -EndCursor $null)
        }
        New-SelfTestResponse -TotalCount 3 -Nodes @($itemA) -HasNextPage $false -EndCursor $null
    }
    $checks += Assert-SelfTestThrows -Action {
        Get-SelfTestSnapshotWithRestart -PageProvider $mixedDuplicateProvider -MaxRestarts 2
    } -MessagePattern "duplicate item id"
    $checks += Assert-SelfTest `
        -Condition ($mixedDuplicateState.calls -eq 2 -and $mixedDuplicateState.starts -eq 1) `
        -Message "duplicate identity faults must not retry when the same page also reports drift"

    $mixedCursorState = [pscustomobject]@{ starts = 0; calls = 0 }
    $mixedCursorProvider = {
        param($requestedProjectId, $after)
        $mixedCursorState.calls++
        if ([string]::IsNullOrWhiteSpace([string]$after)) {
            $mixedCursorState.starts++
            if ($mixedCursorState.starts -eq 1) {
                return (New-SelfTestResponse -TotalCount 2 -Nodes @($itemA) -HasNextPage $true -EndCursor "mixed-cursor")
            }
            return (New-SelfTestResponse -TotalCount 1 -Nodes @($itemB) -HasNextPage $false -EndCursor $null)
        }
        New-SelfTestResponse `
            -TotalCount 2 `
            -Nodes @($itemB) `
            -HasNextPage $true `
            -EndCursor $null `
            -UpdatedAt "2026-07-26T00:00:01Z"
    }
    $checks += Assert-SelfTestThrows -Action {
        Get-SelfTestSnapshotWithRestart -PageProvider $mixedCursorProvider -MaxRestarts 2
    } -MessagePattern "hasNextPage without an endCursor"
    $checks += Assert-SelfTest `
        -Condition ($mixedCursorState.calls -eq 2 -and $mixedCursorState.starts -eq 1) `
        -Message "cursor faults must not retry when the same page also reports drift"

    $mixedTruncatedItem = New-SelfTestItem `
        -Id "mixed-truncated" `
        -Labels @("Priority I") `
        -LabelTotalCount 2 `
        -LabelsHasNextPage $true
    $mixedTruncatedState = [pscustomobject]@{ starts = 0; calls = 0 }
    $mixedTruncatedProvider = {
        param($requestedProjectId, $after)
        $mixedTruncatedState.calls++
        if ([string]::IsNullOrWhiteSpace([string]$after)) {
            $mixedTruncatedState.starts++
            if ($mixedTruncatedState.starts -eq 1) {
                return (New-SelfTestResponse -TotalCount 2 -Nodes @($itemA) -HasNextPage $true -EndCursor "mixed-truncated")
            }
            return (New-SelfTestResponse -TotalCount 1 -Nodes @($itemB) -HasNextPage $false -EndCursor $null)
        }
        New-SelfTestResponse `
            -TotalCount 2 `
            -Nodes @($mixedTruncatedItem) `
            -HasNextPage $false `
            -EndCursor $null `
            -UpdatedAt "2026-07-26T00:00:01Z"
    }
    $checks += Assert-SelfTestThrows -Action {
        Get-SelfTestSnapshotWithRestart -PageProvider $mixedTruncatedProvider -MaxRestarts 2
    } -MessagePattern "labels was truncated"
    $checks += Assert-SelfTest `
        -Condition ($mixedTruncatedState.calls -eq 2 -and $mixedTruncatedState.starts -eq 1) `
        -Message "nested-connection faults must not retry when the same page also reports drift"

    $mixedLimitState = [pscustomobject]@{ starts = 0; calls = 0 }
    $mixedLimitProvider = {
        param($requestedProjectId, $after)
        $mixedLimitState.calls++
        if ([string]::IsNullOrWhiteSpace([string]$after)) {
            $mixedLimitState.starts++
            if ($mixedLimitState.starts -eq 1) {
                return (New-SelfTestResponse -TotalCount 2 -Nodes @($itemA) -HasNextPage $true -EndCursor "mixed-limit")
            }
            return (New-SelfTestResponse -TotalCount 1 -Nodes @($itemB) -HasNextPage $false -EndCursor $null)
        }
        New-SelfTestResponse -TotalCount 3 -Nodes @($itemB) -HasNextPage $false -EndCursor $null
    }
    $checks += Assert-SelfTestThrows -Action {
        Get-SelfTestSnapshotWithRestart -PageProvider $mixedLimitProvider -TestLimit 2 -MaxRestarts 2
    } -MessagePattern "exceeding -Limit 2"
    $checks += Assert-SelfTest `
        -Condition ($mixedLimitState.calls -eq 2 -and $mixedLimitState.starts -eq 1) `
        -Message "limit faults must not retry when the same page also reports drift"

    $restartState = [pscustomobject]@{
        starts = 0
        cursors = [System.Collections.Generic.List[string]]::new()
    }
    $restartProvider = {
        param($requestedProjectId, $after)
        $cursor = if ([string]::IsNullOrWhiteSpace([string]$after)) { "<start>" } else { [string]$after }
        $restartState.cursors.Add($cursor) | Out-Null
        if ($cursor -eq "<start>") {
            $restartState.starts++
            if ($restartState.starts -eq 1) {
                return (New-SelfTestResponse -TotalCount 2 -Nodes @($itemA) -HasNextPage $true -EndCursor "late-drift")
            }
            return (New-SelfTestResponse -TotalCount 1 -Nodes @($itemB) -HasNextPage $false -EndCursor $null)
        }
        New-SelfTestResponse -TotalCount 3 -Nodes @($itemB) -HasNextPage $false -EndCursor $null
    }
    $restartSuccess = Get-SelfTestSnapshotWithRestart -PageProvider $restartProvider -MaxRestarts 2
    $checks += Assert-SelfTest `
        -Condition ($restartSuccess.snapshotRestartCount -eq 1 -and
            $restartSuccess.items.Count -eq 1 -and
            $restartSuccess.items[0].id -ceq "item-b" -and
            [string]::Join("|", $restartState.cursors) -ceq "<start>|late-drift|<start>") `
        -Message "recognized drift must restart from the first page and discard the partial snapshot"

    $exhaustionState = [pscustomobject]@{ starts = 0; calls = 0 }
    $exhaustionProvider = {
        param($requestedProjectId, $after)
        $exhaustionState.calls++
        if ([string]::IsNullOrWhiteSpace([string]$after)) {
            $exhaustionState.starts++
            return (New-SelfTestResponse -TotalCount 2 -Nodes @($itemA) -HasNextPage $true -EndCursor "always-drift")
        }
        New-SelfTestResponse -TotalCount 3 -Nodes @($itemB) -HasNextPage $false -EndCursor $null
    }
    $checks += Assert-SelfTestThrows -Action {
        Get-SelfTestSnapshotWithRestart -PageProvider $exhaustionProvider -MaxRestarts 2
    } -MessagePattern "restart bound exhausted.*attempt 1: totalCount drift at page 2; attempt 2: totalCount drift at page 2; attempt 3: totalCount drift at page 2"
    $checks += Assert-SelfTest `
        -Condition ($exhaustionState.starts -eq 3 -and $exhaustionState.calls -eq 6) `
        -Message "restart exhaustion must use only the explicit attempt bound"

    $nonDriftState = [pscustomobject]@{ starts = 0 }
    $nonDriftProvider = {
        param($requestedProjectId, $after)
        $nonDriftState.starts++
        if ([string]::IsNullOrWhiteSpace([string]$after)) {
            return (New-SelfTestResponse -TotalCount 2 -Nodes @($itemA) -HasNextPage $true -EndCursor "duplicate")
        }
        New-SelfTestResponse -TotalCount 2 -Nodes @($itemA) -HasNextPage $false -EndCursor $null
    }
    $checks += Assert-SelfTestThrows -Action {
        Get-SelfTestSnapshotWithRestart -PageProvider $nonDriftProvider -MaxRestarts 2
    } -MessagePattern "duplicate item id"
    $checks += Assert-SelfTest `
        -Condition ($nonDriftState.starts -eq 2) `
        -Message "non-drift completeness faults must not trigger a restart"

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

    $diagnosticOrderState = [pscustomobject]@{ providerCalls = 0 }
    $diagnosticOrderItems = Get-NormalizedSelfTestItems -RawItems @(
        (New-SelfTestItem `
            -Id "unreadable-reference-pr" `
            -ContentType "PullRequest" `
            -Number 103 `
            -Priority "Priority V" `
            -Body "Refs owner/other#999"),
        (New-SelfTestItem `
            -Id "invalid-project-issue-missing" `
            -Number 101 `
            -Priority "Priority V" `
            -Labels @()),
        (New-SelfTestItem `
            -Id "invalid-project-issue-multiple" `
            -Number 102 `
            -Priority "Priority II" `
            -Labels @("Priority I", "Priority II"))
    )
    $checks += Assert-SelfTestThrows -Action {
        New-PriorityAuditState `
            -Items $diagnosticOrderItems `
            -CanonicalRepository $canonicalRepository `
            -ReferenceProvider {
                $diagnosticOrderState.providerCalls++
                throw "reference resolution must not precede project Issue validation"
            }
    } -MessagePattern "2 issue item\(s\).*#101.*issue-missing-priority-label.*#102.*issue-multiple-priority-labels"
    $checks += Assert-SelfTest `
        -Condition ($diagnosticOrderState.providerCalls -eq 0) `
        -Message "all same-repository project Issue label defects must aggregate before PR reference resolution"

    $strictFallbackItems = Get-NormalizedSelfTestItems -RawItems @(
        (New-SelfTestItem `
            -Id "strict-fallback-pr" `
            -ContentType "PullRequest" `
            -Priority "Priority I" `
            -Body "No issue references")
    )
    $strictFallbackAudit = New-PriorityAuditState `
        -Items $strictFallbackItems `
        -CanonicalRepository $canonicalRepository `
        -ReferenceProvider { throw "Unexpected provider call" }
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
    $closingAudit = New-PriorityAuditState -Items $closingItems -CanonicalRepository $canonicalRepository -ReferenceProvider {
        param($repositoryName, $issueNumber)
        $closingProviderState.calls++
        throw "Body fallback should not run when closing links exist"
    }
    $checks += Assert-SelfTest `
        -Condition ($closingAudit.updates[0].expectedPriority -ceq "Priority II" -and
            $closingAudit.updates[0].reason -ceq "pr-closing-issue" -and
            $closingProviderState.calls -eq 0) `
        -Message "closing issues must win before body references"

    $sameRepoProviderState = [pscustomobject]@{ repository = $null; number = 0; calls = 0 }
    $sameRepoItems = Get-NormalizedSelfTestItems -RawItems @(
        (New-SelfTestItem `
            -Id "same-repo-pr" `
            -ContentType "PullRequest" `
            -Priority "Priority V" `
            -Body "Refs Chris0Jeky/Taskdeck#42")
    )
    $sameRepoAudit = New-PriorityAuditState -Items $sameRepoItems -CanonicalRepository $canonicalRepository -ReferenceProvider {
        param($repositoryName, $issueNumber)
        $sameRepoProviderState.repository = $repositoryName
        $sameRepoProviderState.number = $issueNumber
        $sameRepoProviderState.calls++
        New-SelfTestReferenceTarget `
            -RepositoryName $repositoryName `
            -Number $issueNumber `
            -Labels @("Priority I")
    }
    $checks += Assert-SelfTest `
        -Condition ($sameRepoProviderState.calls -eq 1 -and
            $sameRepoProviderState.repository -ceq $canonicalRepository -and
            $sameRepoProviderState.number -eq 42 -and
            $sameRepoAudit.updates[0].expectedPriority -ceq "Priority I") `
        -Message "same-repository qualified body references must preserve identity and derive Priority"

    $colonReferences = @(Get-BodyIssueReferences `
        -Body "Closes: #40`nRefs: Chris0Jeky/Taskdeck#41" `
        -DefaultRepository $canonicalRepository)
    $checks += Assert-SelfTest `
        -Condition ($colonReferences.Count -eq 2 -and
            $colonReferences[0].repository -ceq $canonicalRepository -and
            $colonReferences[0].number -eq 40 -and
            $colonReferences[1].repository -ceq $canonicalRepository -and
            $colonReferences[1].number -eq 41) `
        -Message "colon-form closing and reference directives must preserve every Issue reference"

    $multipleProviderState = [pscustomobject]@{ calls = 0; numbers = @() }
    $multipleItems = Get-NormalizedSelfTestItems -RawItems @(
        (New-SelfTestItem `
            -Id "multiple-body-reference-pr" `
            -ContentType "PullRequest" `
            -Priority "Priority V" `
            -Body "Refs #41, #42 and #43")
    )
    $multipleAudit = New-PriorityAuditState -Items $multipleItems -CanonicalRepository $canonicalRepository -ReferenceProvider {
        param($repositoryName, $issueNumber)
        $multipleProviderState.calls++
        $multipleProviderState.numbers += $issueNumber
        $priority = switch ($issueNumber) {
            41 { "Priority II" }
            42 { "Priority I" }
            43 { "Priority III" }
            default { throw "Unexpected multiple-reference Issue number $issueNumber" }
        }
        New-SelfTestReferenceTarget `
            -RepositoryName $repositoryName `
            -Number $issueNumber `
            -Labels @($priority)
    }
    $checks += Assert-SelfTest `
        -Condition ($multipleProviderState.calls -eq 3 -and
            (@($multipleProviderState.numbers) -join ",") -ceq "41,42,43" -and
            $multipleAudit.updates[0].expectedPriority -ceq "Priority I" -and
            $multipleAudit.updates[0].reason -ceq "pr-body-reference") `
        -Message "every Issue in a comma-and body clause must contribute to derived Priority"

    $mixedRepositoryProviderState = [pscustomobject]@{ calls = 0 }
    $mixedRepositoryItems = Get-NormalizedSelfTestItems -RawItems @(
        (New-SelfTestItem `
            -Id "mixed-repository-pr" `
            -ContentType "PullRequest" `
            -Priority "Priority V" `
            -Body "Refs #41, owner/other#42")
    )
    $mixedRepositoryAudit = New-PriorityAuditState `
        -Items $mixedRepositoryItems `
        -CanonicalRepository $canonicalRepository `
        -ReferenceProvider {
            param($repositoryName, $issueNumber)
            $mixedRepositoryProviderState.calls++
            $labels = if ($repositoryName -ceq $canonicalRepository) { @("Priority II") } else { @("Priority I") }
            New-SelfTestReferenceTarget -RepositoryName $repositoryName -Number $issueNumber -Labels $labels
        }
    $checks += Assert-SelfTest `
        -Condition ($mixedRepositoryProviderState.calls -eq 2 -and
            $mixedRepositoryAudit.updates[0].expectedPriority -ceq "Priority II" -and
            $mixedRepositoryAudit.updates[0].reason -ceq "pr-body-reference" -and
            $mixedRepositoryAudit.ignoredIssueReferences.Count -eq 1 -and
            $mixedRepositoryAudit.ignoredIssueReferences[0].repository -ceq "owner/other" -and
            $mixedRepositoryAudit.ignoredIssueReferences[0].number -eq 42 -and
            $mixedRepositoryAudit.audit[0].references -match "owner/other#42:Issue:ignored") `
        -Message "mixed same- and cross-repository references must rank only the canonical Issue and retain ignored external identity"

    $crossRepoProviderState = [pscustomobject]@{ calls = 0 }
    $crossRepoItems = Get-NormalizedSelfTestItems -RawItems @(
        (New-SelfTestItem `
            -Id "cross-repo-pr" `
            -ContentType "PullRequest" `
            -Priority "Priority I" `
            -Body "Refs: owner/other#42")
    )
    $crossRepoAudit = New-PriorityAuditState `
        -Items $crossRepoItems `
        -CanonicalRepository $canonicalRepository `
        -ReferenceProvider {
            param($repositoryName, $issueNumber)
            $crossRepoProviderState.calls++
            New-SelfTestReferenceTarget `
                -RepositoryName $repositoryName `
                -Number $issueNumber `
                -Labels @("Priority I")
        }
    $checks += Assert-SelfTest `
        -Condition ($crossRepoProviderState.calls -eq 1 -and
            $crossRepoAudit.updates.Count -eq 1 -and
            $crossRepoAudit.updates[0].expectedPriority -ceq "Priority V" -and
            $crossRepoAudit.updates[0].reason -ceq "pr-no-derived-issue-fallback" -and
            $crossRepoAudit.ignoredIssueReferences.Count -eq 1) `
        -Message "an external-only Issue reference must remain visible but produce canonical Priority V"

    $crossRepoCorrectItems = Get-NormalizedSelfTestItems -RawItems @(
        (New-SelfTestItem `
            -Id "cross-repo-correct-pr" `
            -ContentType "PullRequest" `
            -Priority "Priority V" `
            -Body "Refs: owner/other#42")
    )
    $crossRepoCorrectAudit = New-PriorityAuditState `
        -Items $crossRepoCorrectItems `
        -CanonicalRepository $canonicalRepository `
        -ReferenceProvider {
            param($repositoryName, $issueNumber)
            New-SelfTestReferenceTarget -RepositoryName $repositoryName -Number $issueNumber -Labels @("Priority I")
        }
    $checks += Assert-SelfTest `
        -Condition ($crossRepoCorrectAudit.updates.Count -eq 0 -and
            $crossRepoCorrectAudit.audit[0].expectedPriority -ceq "Priority V" -and
            $crossRepoCorrectAudit.audit[0].ignoredIssueReferenceCount -eq 1 -and
            $crossRepoCorrectAudit.audit[0].ignoredIssueReferences -ceq "body:owner/other#42") `
        -Message "a correctly assigned external-only PR must retain ignored identity even when no update is needed"

    $crossRepoPullRequestAudit = New-PriorityAuditState `
        -Items $crossRepoCorrectItems `
        -CanonicalRepository $canonicalRepository `
        -ReferenceProvider {
            param($repositoryName, $issueNumber)
            New-SelfTestReferenceTarget `
                -Type "PullRequest" `
                -RepositoryName $repositoryName `
                -Number $issueNumber `
                -Labels @("Priority I")
        }
    $checks += Assert-SelfTest `
        -Condition ($crossRepoPullRequestAudit.updates.Count -eq 0 -and
            $crossRepoPullRequestAudit.audit[0].expectedPriority -ceq "Priority V" -and
            $crossRepoPullRequestAudit.ignoredIssueReferences.Count -eq 0 -and
            $crossRepoPullRequestAudit.audit[0].references -match ":PullRequest$") `
        -Message "validated cross-repository PullRequest references must remain ignorable"

    $crossRepoClosingIssue = New-SelfTestClosingIssue `
        -RepositoryName "owner/other" `
        -Number 42 `
        -Labels @("Priority I")
    $crossRepoClosingItems = Get-NormalizedSelfTestItems -RawItems @(
        (New-SelfTestItem `
            -Id "cross-repo-closing-pr" `
            -ContentType "PullRequest" `
            -Priority "Priority V" `
            -Body "Refs #43" `
            -ClosingIssues @($crossRepoClosingIssue))
    )
    $crossRepoClosingProviderState = [pscustomobject]@{ calls = 0; number = 0 }
    $crossRepoClosingAudit = New-PriorityAuditState `
        -Items $crossRepoClosingItems `
        -CanonicalRepository $canonicalRepository `
        -ReferenceProvider {
            param($repositoryName, $issueNumber)
            $crossRepoClosingProviderState.calls++
            $crossRepoClosingProviderState.number = $issueNumber
            New-SelfTestReferenceTarget -RepositoryName $repositoryName -Number $issueNumber -Labels @("Priority III")
        }
    $checks += Assert-SelfTest `
        -Condition ($crossRepoClosingProviderState.calls -eq 1 -and
            $crossRepoClosingProviderState.number -eq 43 -and
            $crossRepoClosingAudit.updates[0].expectedPriority -ceq "Priority III" -and
            $crossRepoClosingAudit.updates[0].reason -ceq "pr-body-reference" -and
            $crossRepoClosingAudit.ignoredIssueReferences.Count -eq 1 -and
            $crossRepoClosingAudit.ignoredIssueReferences[0].source -ceq "closing") `
        -Message "external closing Issues must stay visible while allowing canonical body-reference fallback"

    $externalOnlyClosingIssue = New-SelfTestClosingIssue `
        -RepositoryName "owner/other" `
        -Number 44 `
        -Labels @("Priority I")
    $externalOnlyClosingIncorrectItems = Get-NormalizedSelfTestItems -RawItems @(
        (New-SelfTestItem `
            -Id "external-only-closing-incorrect" `
            -ContentType "PullRequest" `
            -Number 104 `
            -Priority "Priority I" `
            -ClosingIssues @($externalOnlyClosingIssue))
    )
    $externalOnlyClosingIncorrectAudit = New-PriorityAuditState `
        -Items $externalOnlyClosingIncorrectItems `
        -CanonicalRepository $canonicalRepository `
        -ReferenceProvider { throw "external closing Issue must not require a provider" }
    $checks += Assert-SelfTest `
        -Condition ($externalOnlyClosingIncorrectAudit.updates.Count -eq 1 -and
            $externalOnlyClosingIncorrectAudit.updates[0].expectedPriority -ceq "Priority V" -and
            $externalOnlyClosingIncorrectAudit.ignoredIssueReferences.Count -eq 1 -and
            $externalOnlyClosingIncorrectAudit.ignoredIssueReferences[0].key -ceq "owner/other#44") `
        -Message "an incorrect external-only closing Issue PR must fall back to Priority V without trusting external labels"

    $externalOnlyClosingCorrectItems = Get-NormalizedSelfTestItems -RawItems @(
        (New-SelfTestItem `
            -Id "external-only-closing-correct" `
            -ContentType "PullRequest" `
            -Number 105 `
            -Priority "Priority V" `
            -ClosingIssues @($externalOnlyClosingIssue))
    )
    $externalOnlyClosingCorrectAudit = New-PriorityAuditState `
        -Items $externalOnlyClosingCorrectItems `
        -CanonicalRepository $canonicalRepository `
        -ReferenceProvider { throw "external closing Issue must not require a provider" }
    $checks += Assert-SelfTest `
        -Condition ($externalOnlyClosingCorrectAudit.updates.Count -eq 0 -and
            $externalOnlyClosingCorrectAudit.audit[0].expectedPriority -ceq "Priority V" -and
            $externalOnlyClosingCorrectAudit.ignoredIssueReferences.Count -eq 1) `
        -Message "a correct external-only closing Issue PR must remain visible without a synthetic update"

    $crossRepoProjectIssueItems = Get-NormalizedSelfTestItems -RawItems @(
        (New-SelfTestItem `
            -Id "cross-repo-project-issue" `
            -ContentType "Issue" `
            -RepositoryName "owner/other" `
            -Number 42 `
            -Labels @("Priority I"))
    )
    $checks += Assert-SelfTestThrows -Action {
        New-PriorityAuditState `
            -Items $crossRepoProjectIssueItems `
            -CanonicalRepository $canonicalRepository `
            -ReferenceProvider { throw "Unexpected provider call" }
    } -MessagePattern "Cross-repository project content.*is disabled"

    $crossRepoProjectPullRequestItems = Get-NormalizedSelfTestItems -RawItems @(
        (New-SelfTestItem `
            -Id "cross-repo-project-pr" `
            -ContentType "PullRequest" `
            -RepositoryName "owner/other" `
            -Number 43 `
            -Priority "Priority V" `
            -Body "No issue references")
    )
    $checks += Assert-SelfTestThrows -Action {
        New-PriorityAuditState `
            -Items $crossRepoProjectPullRequestItems `
            -CanonicalRepository $canonicalRepository `
            -ReferenceProvider { throw "Unexpected provider call" }
    } -MessagePattern "Cross-repository project content.*is disabled"

    $checks += Assert-SelfTestThrows -Action {
        New-PriorityAuditState -Items $sameRepoItems -CanonicalRepository $canonicalRepository -ReferenceProvider {
            New-SelfTestReferenceTarget -RepositoryName $canonicalRepository -Number 42 -Labels @()
        }
    } -MessagePattern "has missing Priority labels"
    $checks += Assert-SelfTestThrows -Action {
        New-PriorityAuditState -Items $sameRepoItems -CanonicalRepository $canonicalRepository -ReferenceProvider {
            New-SelfTestReferenceTarget -RepositoryName $canonicalRepository -Number 42 -Labels @("Priority I", "Priority II")
        }
    } -MessagePattern "has multiple Priority labels"

    $pullRequestReferenceState = [pscustomobject]@{ calls = 0 }
    $pullRequestReferenceItems = Get-NormalizedSelfTestItems -RawItems @(
        (New-SelfTestItem `
            -Id "pull-request-reference" `
            -ContentType "PullRequest" `
            -Priority "Priority I" `
            -Body "Closes #123")
    )
    $pullRequestReferenceAudit = New-PriorityAuditState -Items $pullRequestReferenceItems -CanonicalRepository $canonicalRepository -ReferenceProvider {
        param($repositoryName, $issueNumber)
        $pullRequestReferenceState.calls++
        New-SelfTestReferenceTarget `
            -Type "PullRequest" `
            -RepositoryName $repositoryName `
            -Number $issueNumber `
            -Labels @("Priority I")
    }
    $checks += Assert-SelfTest `
        -Condition ($pullRequestReferenceState.calls -eq 1 -and
            $pullRequestReferenceAudit.updates.Count -eq 1 -and
            $pullRequestReferenceAudit.updates[0].expectedPriority -ceq "Priority V" -and
            $pullRequestReferenceAudit.updates[0].reason -ceq "pr-no-derived-issue-fallback" -and
            $pullRequestReferenceAudit.updates[0].references -match ":PullRequest$") `
        -Message "Priority-labelled PullRequest body targets must not contribute issue priority"

    $mixedReferenceState = [pscustomobject]@{ calls = 0 }
    $mixedReferenceItems = Get-NormalizedSelfTestItems -RawItems @(
        (New-SelfTestItem `
            -Id "mixed-reference" `
            -ContentType "PullRequest" `
            -Priority "Priority V" `
            -Body "Closes #123`nRefs #42")
    )
    $mixedReferenceAudit = New-PriorityAuditState -Items $mixedReferenceItems -CanonicalRepository $canonicalRepository -ReferenceProvider {
        param($repositoryName, $issueNumber)
        $mixedReferenceState.calls++
        if ($issueNumber -eq 123) {
            New-SelfTestReferenceTarget -Type "PullRequest" -RepositoryName $repositoryName -Number $issueNumber -Labels @("Priority I")
        } else {
            New-SelfTestReferenceTarget -Type "Issue" -RepositoryName $repositoryName -Number $issueNumber -Labels @("Priority II")
        }
    }
    $checks += Assert-SelfTest `
        -Condition ($mixedReferenceState.calls -eq 2 -and
            $mixedReferenceAudit.updates.Count -eq 1 -and
            $mixedReferenceAudit.updates[0].expectedPriority -ceq "Priority II" -and
            $mixedReferenceAudit.updates[0].reason -ceq "pr-body-reference") `
        -Message "mixed body references must ignore PullRequests and derive from actual Issues"

    $checks += Assert-SelfTestThrows -Action {
        New-PriorityAuditState -Items $sameRepoItems -CanonicalRepository $canonicalRepository -ReferenceProvider {
            New-SelfTestReferenceTarget -RepositoryName "Chris0Jeky/wrong" -Number 42 -Labels @("Priority I")
        }
    } -MessagePattern "identity mismatch"
    $checks += Assert-SelfTestThrows -Action {
        New-PriorityAuditState -Items $sameRepoItems -CanonicalRepository $canonicalRepository -ReferenceProvider { throw "synthetic unreadable target" }
    } -MessagePattern "Failed to read reference.*synthetic unreadable target"

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
                -Body "Refs #42")) `
            -HasNextPage $false `
            -EndCursor $null
    }
    $sourceSnapshot = Get-SelfTestSnapshot -Pages $strictSnapshotPages
    $sourceAuditInitial = New-PriorityAuditState -Items $sourceSnapshot.items -CanonicalRepository $canonicalRepository -ReferenceProvider {
        New-SelfTestReferenceTarget -RepositoryName $canonicalRepository -Number 42 -Labels @("Priority II")
    }
    $sourceAuditCurrent = New-PriorityAuditState -Items $sourceSnapshot.items -CanonicalRepository $canonicalRepository -ReferenceProvider {
        New-SelfTestReferenceTarget -RepositoryName $canonicalRepository -Number 42 -Labels @("Priority I")
    }
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

    $ignoredFingerprintInitialSnapshot = Get-SelfTestSnapshot -Pages @{
        "<start>" = New-SelfTestResponse `
            -TotalCount 1 `
            -Nodes @((New-SelfTestItem `
                -Id "ignored-fingerprint-pr" `
                -ContentType "PullRequest" `
                -Number 106 `
                -Priority "Priority I" `
                -Body "Refs owner/other#42")) `
            -HasNextPage $false `
            -EndCursor $null
    }
    $ignoredFingerprintCurrentSnapshot = Get-SelfTestSnapshot -Pages @{
        "<start>" = New-SelfTestResponse `
            -TotalCount 1 `
            -Nodes @((New-SelfTestItem `
                -Id "ignored-fingerprint-pr" `
                -ContentType "PullRequest" `
                -Number 106 `
                -Priority "Priority I" `
                -Body "Refs owner/other#43")) `
            -HasNextPage $false `
            -EndCursor $null
    }
    $ignoredFingerprintInitialAudit = New-PriorityAuditState `
        -Items $ignoredFingerprintInitialSnapshot.items `
        -CanonicalRepository $canonicalRepository `
        -ReferenceProvider {
            param($repositoryName, $issueNumber)
            New-SelfTestReferenceTarget -RepositoryName $repositoryName -Number $issueNumber -Labels @("Priority I")
        }
    $ignoredFingerprintCurrentAudit = New-PriorityAuditState `
        -Items $ignoredFingerprintCurrentSnapshot.items `
        -CanonicalRepository $canonicalRepository `
        -ReferenceProvider {
            param($repositoryName, $issueNumber)
            New-SelfTestReferenceTarget -RepositoryName $repositoryName -Number $issueNumber -Labels @("Priority I")
        }
    $checks += Assert-SelfTest `
        -Condition (-not [string]::Equals(
                $ignoredFingerprintInitialAudit.sourceFingerprint,
                $ignoredFingerprintCurrentAudit.sourceFingerprint,
                [System.StringComparison]::Ordinal) -and
            [string]::Equals(
                $ignoredFingerprintInitialAudit.planFingerprint,
                $ignoredFingerprintCurrentAudit.planFingerprint,
                [System.StringComparison]::Ordinal) -and
            $ignoredFingerprintInitialAudit.ignoredIssueReferences[0].key -ceq "owner/other#42" -and
            $ignoredFingerprintCurrentAudit.ignoredIssueReferences[0].key -ceq "owner/other#43") `
        -Message "ignored external Issue identity drift must change source evidence without changing the update plan"
    $ignoredFingerprintInitialState = New-PriorityExecutionState `
        -Snapshot $ignoredFingerprintInitialSnapshot `
        -AuditState $ignoredFingerprintInitialAudit `
        -PriorityFieldId $priorityFieldId `
        -StatusFieldId $statusFieldId `
        -OptionMap $testOptionMap
    $ignoredFingerprintCurrentState = New-PriorityExecutionState `
        -Snapshot $ignoredFingerprintCurrentSnapshot `
        -AuditState $ignoredFingerprintCurrentAudit `
        -PriorityFieldId $priorityFieldId `
        -StatusFieldId $statusFieldId `
        -OptionMap $testOptionMap
    $ignoredFingerprintReport = New-PriorityReportState `
        -InitialState $ignoredFingerprintInitialState `
        -ApplyOutcome $null
    $checks += Assert-SelfTest `
        -Condition ($ignoredFingerprintReport.ignoredIssueReferences.Count -eq 1 -and
            $ignoredFingerprintReport.ignoredIssueReferences[0].itemId -ceq "ignored-fingerprint-pr" -and
            $ignoredFingerprintReport.ignoredIssueReferences[0].pullRequestRepository -ceq $canonicalRepository -and
            $ignoredFingerprintReport.ignoredIssueReferences[0].pullRequestNumber -eq 106 -and
            $ignoredFingerprintReport.ignoredIssueReferences[0].source -ceq "body" -and
            $ignoredFingerprintReport.ignoredIssueReferences[0].repository -ceq "owner/other" -and
            $ignoredFingerprintReport.ignoredIssueReferences[0].number -eq 42 -and
            $ignoredFingerprintReport.ignoredIssueReferences[0].key -ceq "owner/other#42") `
        -Message "report output state must retain the exact ignored external Issue occurrence"
    $ignoredFingerprintGuardState = [pscustomobject]@{ currentChecks = 0; writes = 0 }
    $checks += Assert-SelfTestThrows -Action {
        Invoke-PriorityUpdatePlan `
            -InitialState $ignoredFingerprintInitialState `
            -OptionMap $testOptionMap `
            -CurrentStateProvider {
                $ignoredFingerprintGuardState.currentChecks++
                $ignoredFingerprintCurrentState
            } `
            -ItemWriter {
                $ignoredFingerprintGuardState.writes++
            }
    } -MessagePattern "derivation/write plan drifted"
    $checks += Assert-SelfTest `
        -Condition ($ignoredFingerprintGuardState.currentChecks -eq 1 -and $ignoredFingerprintGuardState.writes -eq 0) `
        -Message "ignored external Issue drift must abort Apply before the first write"

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

    $partialInitialState = [pscustomobject]@{
        guardFingerprint = "partial-guard"
        updates = @(
            [pscustomobject]@{ itemId = "partial-first"; expectedPriority = "Priority I" },
            [pscustomobject]@{ itemId = "partial-second"; expectedPriority = "Priority II" }
        )
        audit = @()
        snapshot = [pscustomobject]@{ projectUpdatedAt = "before-partial"; items = @() }
    }
    $exhaustionApplyState = [pscustomobject]@{
        starts = 0
        calls = 0
        currentChecks = 0
        writes = 0
        postAudits = 0
    }
    $exhaustionApplyProvider = {
        param($requestedProjectId, $after)
        $exhaustionApplyState.calls++
        if ([string]::IsNullOrWhiteSpace([string]$after)) {
            $exhaustionApplyState.starts++
            return (New-SelfTestResponse -TotalCount 2 -Nodes @($itemA) -HasNextPage $true -EndCursor "apply-exhaustion")
        }
        New-SelfTestResponse -TotalCount 3 -Nodes @($itemB) -HasNextPage $false -EndCursor $null
    }
    $checks += Assert-SelfTestThrows -Action {
        Invoke-PriorityApplyWithAudit `
            -InitialState $partialInitialState `
            -OptionMap $testOptionMap `
            -CurrentStateProvider {
                $exhaustionApplyState.currentChecks++
                Get-SelfTestSnapshotWithRestart `
                    -PageProvider $exhaustionApplyProvider `
                    -MaxRestarts 0 | Out-Null
                throw "snapshot unexpectedly completed"
            } `
            -ItemWriter {
                param($update, $optionId)
                $exhaustionApplyState.writes++
            } `
            -PostAuditProvider {
                $exhaustionApplyState.postAudits++
                throw "post-audit unexpectedly reached"
            }
    } -MessagePattern "restart bound exhausted"
    $checks += Assert-SelfTest `
        -Condition ($exhaustionApplyState.currentChecks -eq 1 -and
            $exhaustionApplyState.starts -eq 1 -and
            $exhaustionApplyState.calls -eq 2 -and
            $exhaustionApplyState.writes -eq 0 -and
            $exhaustionApplyState.postAudits -eq 0) `
        -Message "Apply must perform zero writes when its pre-write snapshot exhausts restarts"

    $malformedApplyState = [pscustomobject]@{
        starts = 0
        calls = 0
        currentChecks = 0
        writes = 0
        postAudits = 0
    }
    $malformedApplyProvider = {
        param($requestedProjectId, $after)
        $malformedApplyState.calls++
        if ([string]::IsNullOrWhiteSpace([string]$after)) {
            $malformedApplyState.starts++
            return (New-SelfTestResponse -TotalCount 2 -Nodes @($itemA) -HasNextPage $true -EndCursor "apply-malformed")
        }
        New-SelfTestResponse `
            -TotalCount 3 `
            -Nodes @($itemB) `
            -HasNextPage "false" `
            -EndCursor $null `
            -UpdatedAt "2026-07-26T00:00:01Z"
    }
    $checks += Assert-SelfTestThrows -Action {
        Invoke-PriorityApplyWithAudit `
            -InitialState $partialInitialState `
            -OptionMap $testOptionMap `
            -CurrentStateProvider {
                $malformedApplyState.currentChecks++
                Get-SelfTestSnapshotWithRestart `
                    -PageProvider $malformedApplyProvider `
                    -MaxRestarts 2 | Out-Null
                throw "snapshot unexpectedly completed"
            } `
            -ItemWriter {
                param($update, $optionId)
                $malformedApplyState.writes++
            } `
            -PostAuditProvider {
                $malformedApplyState.postAudits++
                throw "post-audit unexpectedly reached"
            }
    } -MessagePattern "malformed hasNextPage metadata"
    $checks += Assert-SelfTest `
        -Condition ($malformedApplyState.currentChecks -eq 1 -and
            $malformedApplyState.starts -eq 1 -and
            $malformedApplyState.calls -eq 2 -and
            $malformedApplyState.writes -eq 0 -and
            $malformedApplyState.postAudits -eq 0) `
        -Message "Apply must perform zero writes and no restart when malformed pagination metadata is mixed with drift"

    $partialPostState = [pscustomobject]@{
        guardFingerprint = "post-partial-guard"
        updates = @([pscustomobject]@{ itemId = "partial-second"; expectedPriority = "Priority II" })
        audit = @()
        snapshot = [pscustomobject]@{ projectUpdatedAt = "after-partial"; items = @() }
    }
    $partialApplyState = [pscustomobject]@{ writes = 0; postAudits = 0 }
    $checks += Assert-SelfTestThrows -Action {
        Invoke-PriorityApplyWithAudit `
            -InitialState $partialInitialState `
            -OptionMap $testOptionMap `
            -CurrentStateProvider { $partialInitialState } `
            -ItemWriter {
                param($update, $optionId)
                $partialApplyState.writes++
                if ($update.itemId -ceq "partial-second") {
                    throw "synthetic writer failure"
                }
            } `
            -PostAuditProvider {
                $partialApplyState.postAudits++
                $partialPostState
            }
    } -MessagePattern "failed after 1 of 2 planned writes.*synthetic writer failure.*post-Apply audit completed: remaining=1.*authoritative"
    $checks += Assert-SelfTest `
        -Condition ($partialApplyState.writes -eq 2 -and $partialApplyState.postAudits -eq 1) `
        -Message "writer failure must still invoke the complete post-Apply audit"

    $failedAuditState = [pscustomobject]@{ writes = 0; postAudits = 0 }
    $checks += Assert-SelfTestThrows -Action {
        Invoke-PriorityApplyWithAudit `
            -InitialState $partialInitialState `
            -OptionMap $testOptionMap `
            -CurrentStateProvider { $partialInitialState } `
            -ItemWriter {
                param($update, $optionId)
                $failedAuditState.writes++
                if ($update.itemId -ceq "partial-second") {
                    throw "synthetic writer failure"
                }
            } `
            -PostAuditProvider {
                $failedAuditState.postAudits++
                throw "synthetic post-audit failure"
            }
    } -MessagePattern "synthetic writer failure.*post-Apply audit failed: synthetic post-audit failure.*Final project state is unknown"
    $checks += Assert-SelfTest `
        -Condition ($failedAuditState.writes -eq 2 -and $failedAuditState.postAudits -eq 1) `
        -Message "writer and audit failures must both retain their invocation evidence"

    $successfulInitialState = [pscustomobject]@{
        guardFingerprint = "successful-guard"
        updates = @(
            [pscustomobject]@{ itemId = "ordered-first"; expectedPriority = "Priority II" },
            [pscustomobject]@{ itemId = "ordered-second"; expectedPriority = "Priority I" }
        )
        audit = @(
            [pscustomobject]@{ itemId = "ordered-first"; needsUpdate = $true },
            [pscustomobject]@{ itemId = "ordered-second"; needsUpdate = $true }
        )
        snapshot = [pscustomobject]@{
            projectUpdatedAt = "before-success"
            totalCount = 2
            pageCount = 1
            items = @("before-item-1", "before-item-2")
        }
    }
    $successfulPostState = [pscustomobject]@{
        guardFingerprint = "successful-post-guard"
        updates = @()
        audit = @(
            [pscustomobject]@{ itemId = "ordered-first"; needsUpdate = $false },
            [pscustomobject]@{ itemId = "ordered-second"; needsUpdate = $false }
        )
        snapshot = [pscustomobject]@{
            projectUpdatedAt = "after-ordered-success"
            totalCount = 2
            pageCount = 2
            items = @("after-item-1", "after-item-2")
        }
    }
    $successfulApplyState = [pscustomobject]@{
        currentChecks = 0
        writes = [System.Collections.Generic.List[string]]::new()
        postAudits = 0
    }
    $successfulOutcome = Invoke-PriorityApplyWithAudit `
        -InitialState $successfulInitialState `
        -OptionMap $testOptionMap `
        -CurrentStateProvider {
            $successfulApplyState.currentChecks++
            $successfulInitialState
        } `
        -ItemWriter {
            param($update, $optionId)
            $successfulApplyState.writes.Add("$($update.itemId)|$optionId")
        } `
        -PostAuditProvider {
            $successfulApplyState.postAudits++
            $successfulPostState
        }
    $successfulReport = New-PriorityReportState -InitialState $successfulInitialState -ApplyOutcome $successfulOutcome
    $checks += Assert-SelfTest `
        -Condition ($successfulApplyState.currentChecks -eq 1 -and
            $successfulApplyState.writes.Count -eq 2 -and
            $successfulApplyState.writes[0] -ceq "ordered-first|option-ii" -and
            $successfulApplyState.writes[1] -ceq "ordered-second|option-i" -and
            $successfulApplyState.postAudits -eq 1) `
        -Message "successful Apply must preserve planned item order and map each expected Priority to its exact option id"
    $checks += Assert-SelfTest `
        -Condition ($successfulOutcome.plannedCount -eq 2 -and
            $successfulOutcome.attemptedCount -eq 2 -and
            $successfulOutcome.succeededCount -eq 2 -and
            $null -eq $successfulOutcome.failedItemId -and
            $null -eq $successfulOutcome.writeErrorMessage -and
            $successfulApplyState.postAudits -eq 1 -and
            $successfulOutcome.postApplyVerified -and
            $successfulOutcome.remainingCount -eq 0) `
        -Message "successful Apply must retain exact writer counts and complete post-audit evidence"
    $checks += Assert-SelfTest `
        -Condition ($successfulReport.postApplyVerified -and
            $successfulReport.plannedCount -eq 2 -and
            $successfulReport.attemptedCount -eq 2 -and
            $successfulReport.appliedCount -eq 2 -and
            $successfulReport.updates.Count -eq 0 -and
            $successfulReport.snapshot.projectUpdatedAt -ceq "after-ordered-success" -and
            $successfulReport.snapshot.items.Count -eq 2) `
        -Message "successful Apply reports current post-audit truth and retains planned/applied evidence"

    Write-Host "SelfTest passed: $checks checks."
}

function Get-PriorityLabels {
    param(
        [object[]]$Labels
    )

    @($Labels | Where-Object { $priorityRank.ContainsKey([string]$_) })
}

function Get-RepositoryKey {
    param([string]$RepositoryName)

    if ([string]::IsNullOrWhiteSpace($RepositoryName) -or
        $RepositoryName -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
        throw "Invalid repository identity '$RepositoryName'."
    }

    $RepositoryName.ToLowerInvariant()
}

function Get-IssueReferenceKey {
    param(
        [string]$RepositoryName,
        [int]$Number
    )

    if ($Number -le 0) {
        throw "Invalid issue reference '$RepositoryName#$Number'."
    }

    "$((Get-RepositoryKey -RepositoryName $RepositoryName))#$Number"
}

function Assert-CanonicalIssueReference {
    param(
        [string]$RepositoryName,
        [int]$Number,
        [string]$CanonicalRepository
    )

    Get-IssueReferenceKey -RepositoryName $RepositoryName -Number $Number | Out-Null
    $repositoryKey = Get-RepositoryKey -RepositoryName $RepositoryName
    $canonicalRepositoryKey = Get-RepositoryKey -RepositoryName $CanonicalRepository
    if (-not [string]::Equals($repositoryKey, $canonicalRepositoryKey, [System.StringComparison]::Ordinal)) {
        throw "Cross-repository Issue reference '$RepositoryName#$Number' is disabled; canonical repository is '$CanonicalRepository'."
    }
}

function Get-BodyIssueReferences {
    param(
        [string]$Body,
        [string]$DefaultRepository
    )

    if ([string]::IsNullOrWhiteSpace($Body)) {
        return @()
    }

    $directivePattern = '(?im)\b(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?|refs?|references?)(?:\s*:\s*|\s+)(?<references>(?:(?:[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+)?#\d+)(?:\s*(?:,\s*(?:and\s+)?|\band\s+)(?:(?:[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+)?#\d+))*)'
    $referencePattern = '(?:(?<repository>[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+))?#(?<number>\d+)'
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $references = @()
    foreach ($directive in [regex]::Matches($Body, $directivePattern)) {
        foreach ($reference in [regex]::Matches($directive.Groups["references"].Value, $referencePattern)) {
            $repositoryName = if ($reference.Groups["repository"].Success) {
                $reference.Groups["repository"].Value
            } else {
                $DefaultRepository
            }
            $number = [int]$reference.Groups["number"].Value
            $key = Get-IssueReferenceKey -RepositoryName $repositoryName -Number $number
            if ($seen.Add($key)) {
                $references += [pscustomobject]@{
                    source = "body"
                    repository = $repositoryName
                    number = $number
                    key = $key
                }
            }
        }
    }

    @($references)
}

function Get-PullRequestIssueReferences {
    param(
        [object]$Content,
        [Parameter(Mandatory = $true)]
        [string]$CanonicalRepository
    )

    $canonicalRepositoryKey = Get-RepositoryKey -RepositoryName $CanonicalRepository
    $closingReferences = @($Content.closingIssues | ForEach-Object {
        $key = Get-IssueReferenceKey -RepositoryName $_.repository -Number $_.number
        [pscustomobject]@{
            source = "closing"
            repository = $_.repository
            number = [int]$_.number
            key = $key
        }
    })

    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $distinctClosingReferences = @($closingReferences | Where-Object { $seen.Add($_.key) })
    $hasCanonicalClosingIssue = @($distinctClosingReferences | Where-Object {
        [string]::Equals(
            (Get-RepositoryKey -RepositoryName ([string]$_.repository)),
            $canonicalRepositoryKey,
            [System.StringComparison]::Ordinal)
    }).Count -gt 0
    if ($hasCanonicalClosingIssue) {
        return $distinctClosingReferences
    }

    $bodyReferences = @(Get-BodyIssueReferences -Body $Content.body -DefaultRepository $Content.repository)
    $additionalBodyReferences = @($bodyReferences | Where-Object { $seen.Add($_.key) })
    @($distinctClosingReferences + $additionalBodyReferences)
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
    param(
        [object[]]$Items,
        [Parameter(Mandatory = $true)]
        [string]$CanonicalRepository
    )

    $cache = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($item in $Items) {
        if ($null -eq $item.content) {
            continue
        }
        if ($item.content.type -eq "Issue") {
            Add-IssueLabelsToCache -Cache $cache -RepositoryName $item.content.repository -Number $item.content.number -Labels $item.labels
        } elseif ($item.content.type -eq "PullRequest") {
            foreach ($closingIssue in @($item.content.closingIssues)) {
                if ([string]::Equals(
                        (Get-RepositoryKey -RepositoryName ([string]$closingIssue.repository)),
                        (Get-RepositoryKey -RepositoryName $CanonicalRepository),
                        [System.StringComparison]::Ordinal)) {
                    Add-IssueLabelsToCache -Cache $cache -RepositoryName $closingIssue.repository -Number $closingIssue.number -Labels $closingIssue.labels
                }
            }
        }
    }
    return ,$cache
}

function Resolve-PriorityReferenceTarget {
    param(
        [object]$Reference,
        [object]$IssueLabelCache,
        [Parameter(Mandatory = $true)]
        [string]$CanonicalRepository,
        [scriptblock]$ReferenceProvider
    )

    $canonicalRepositoryKey = Get-RepositoryKey -RepositoryName $CanonicalRepository
    $referenceRepositoryKey = Get-RepositoryKey -RepositoryName ([string]$Reference.repository)
    $isCanonicalReference = [string]::Equals(
        $referenceRepositoryKey,
        $canonicalRepositoryKey,
        [System.StringComparison]::Ordinal)

    if (-not $isCanonicalReference -and $Reference.source -ceq "closing") {
        return [pscustomobject]@{
            type = "Issue"
            priority = $null
            ignoredIssue = $true
        }
    }

    if ($IssueLabelCache.ContainsKey($Reference.key)) {
        Assert-CanonicalIssueReference `
            -RepositoryName $Reference.repository `
            -Number $Reference.number `
            -CanonicalRepository $CanonicalRepository
    } else {
        if ($null -eq $ReferenceProvider) {
            throw "No provider is available for reference '$($Reference.repository)#$($Reference.number)'."
        }
        try {
            $targets = @(& $ReferenceProvider $Reference.repository $Reference.number)
        } catch {
            throw "Failed to read reference '$($Reference.repository)#$($Reference.number)': $($_.Exception.Message)"
        }

        if ($targets.Count -ne 1 -or $null -eq $targets[0]) {
            throw "Reference provider returned $($targets.Count) targets for '$($Reference.repository)#$($Reference.number)'; expected exactly one typed target."
        }

        $target = $targets[0]
        if (-not ($target.PSObject.Properties.Name -contains "type") -or
            -not ($target.PSObject.Properties.Name -contains "repository") -or
            -not ($target.PSObject.Properties.Name -contains "number")) {
            throw "Reference provider returned an incomplete typed target for '$($Reference.repository)#$($Reference.number)'."
        }

        $targetType = [string]$target.type
        if ($targetType -cne "Issue" -and $targetType -cne "PullRequest") {
            throw "Reference provider returned unsupported type '$targetType' for '$($Reference.repository)#$($Reference.number)'."
        }

        $targetKey = Get-IssueReferenceKey -RepositoryName ([string]$target.repository) -Number ([int]$target.number)
        if (-not [string]::Equals($Reference.key, $targetKey, [System.StringComparison]::Ordinal)) {
            throw "Reference provider identity mismatch for '$($Reference.repository)#$($Reference.number)': returned '$($target.repository)#$($target.number)'."
        }

        if ($targetType -ceq "PullRequest") {
            if ($Reference.source -ceq "closing") {
                throw "Closing issue reference '$($Reference.repository)#$($Reference.number)' resolved as PullRequest."
            }
            return [pscustomobject]@{
                type = "PullRequest"
                priority = $null
                ignoredIssue = $false
            }
        }

        if (-not $isCanonicalReference) {
            return [pscustomobject]@{
                type = "Issue"
                priority = $null
                ignoredIssue = $true
            }
        }

        if (-not ($target.PSObject.Properties.Name -contains "labels") -or $null -eq $target.labels) {
            throw "Reference provider returned an Issue without label data for '$($Reference.repository)#$($Reference.number)'."
        }
        Assert-CanonicalIssueReference `
            -RepositoryName ([string]$target.repository) `
            -Number ([int]$target.number) `
            -CanonicalRepository $CanonicalRepository
        Add-IssueLabelsToCache `
            -Cache $IssueLabelCache `
            -RepositoryName ([string]$target.repository) `
            -Number ([int]$target.number) `
            -Labels @($target.labels)
    }

    $entry = $IssueLabelCache[$Reference.key]
    $priorityLabels = @(Get-PriorityLabels -Labels $entry.labels)
    if ($priorityLabels.Count -ne 1) {
        $reason = if ($priorityLabels.Count -eq 0) { "missing" } else { "multiple" }
        throw "Referenced issue '$($Reference.repository)#$($Reference.number)' has $reason Priority labels; refusing Priority V fallback."
    }
    [pscustomobject]@{
        type = "Issue"
        priority = [string]$priorityLabels[0]
        ignoredIssue = $false
    }
}

function Get-PriorityAuditFingerprints {
    param(
        [object[]]$AuditItems,
        [object]$IssueLabelCache,
        [object[]]$IgnoredIssueReferences
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
            ignoredIssueReferenceCount = [int]$item.ignoredIssueReferenceCount
            ignoredIssueReferences = [string]$item.ignoredIssueReferences
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

    $ignoredByIdentity = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
    foreach ($ignored in @($IgnoredIssueReferences)) {
        $identity = "$([string]$ignored.itemId)$([char]31)$([string]$ignored.source)$([char]31)$([string]$ignored.key)"
        $ignoredByIdentity.Add($identity, [pscustomobject]@{
            itemId = [string]$ignored.itemId
            pullRequestRepository = [string]$ignored.pullRequestRepository
            pullRequestNumber = [int]$ignored.pullRequestNumber
            source = [string]$ignored.source
            repository = [string]$ignored.repository
            number = [int]$ignored.number
            key = [string]$ignored.key
        })
    }
    [string[]]$ignoredIdentities = @($ignoredByIdentity.Keys)
    [Array]::Sort($ignoredIdentities, [System.StringComparer]::Ordinal)
    $orderedIgnored = @($ignoredIdentities | ForEach-Object { $ignoredByIdentity[$_] })

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
        source = ([pscustomobject]@{
            audit = $orderedAudit
            issueLabels = $orderedSources
            ignoredIssueReferences = $orderedIgnored
        } | ConvertTo-Json -Compress -Depth 8)
        plan = ($orderedPlan | ConvertTo-Json -Compress -Depth 5)
    }
}

function New-PriorityAuditState {
    param(
        [object[]]$Items,
        [Parameter(Mandatory = $true)]
        [string]$CanonicalRepository,
        [scriptblock]$ReferenceProvider
    )

    $canonicalRepositoryKey = Get-RepositoryKey -RepositoryName $CanonicalRepository
    foreach ($item in $Items) {
        $content = $item.content
        if ($null -eq $content) {
            continue
        }
        if (($content.type -ceq "Issue" -or $content.type -ceq "PullRequest") -and
            -not [string]::Equals(
                (Get-RepositoryKey -RepositoryName ([string]$content.repository)),
                $canonicalRepositoryKey,
                [System.StringComparison]::Ordinal)) {
            throw "Cross-repository project content '$($content.repository)#$($content.number)' is disabled; canonical repository is '$CanonicalRepository'."
        }
    }

    $projectIssueAudit = @(foreach ($item in $Items) {
        $content = $item.content
        if ($null -eq $content -or $content.type -cne "Issue") {
            continue
        }

        $priorityLabels = @(Get-PriorityLabels -Labels $item.labels)
        $expectedPriority = if ($priorityLabels.Count -eq 1) { [string]$priorityLabels[0] } else { $null }
        $reason = if ($priorityLabels.Count -eq 0) {
            "issue-missing-priority-label"
        } elseif ($priorityLabels.Count -gt 1) {
            "issue-multiple-priority-labels"
        } else {
            "issue-label"
        }
        [pscustomobject]@{
            contentType = "Issue"
            number = $content.number
            expectedPriority = $expectedPriority
            actualPriority = [string]$item.priority
            reason = $reason
        }
    })
    Assert-AuditableIssuePriorities -AuditItems $projectIssueAudit

    $issueLabelCache = New-IssueLabelCache -Items $Items -CanonicalRepository $CanonicalRepository
    $ignoredIssueReferences = [System.Collections.Generic.List[object]]::new()
    $audit = @(foreach ($item in $Items) {
        $content = $item.content
        if ($null -eq $content) {
            continue
        }

        $expectedPriority = $null
        $reason = $null
        $references = @()
        $referenceTargets = @()

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
            $references = @(Get-PullRequestIssueReferences -Content $content -CanonicalRepository $CanonicalRepository)
            if ($references.Count -gt 0) {
                $referenceTargets = @($references | ForEach-Object {
                    Resolve-PriorityReferenceTarget `
                        -Reference $_ `
                        -IssueLabelCache $issueLabelCache `
                        -CanonicalRepository $CanonicalRepository `
                        -ReferenceProvider $ReferenceProvider
                })
                for ($referenceIndex = 0; $referenceIndex -lt $references.Count; $referenceIndex++) {
                    if ([bool]$referenceTargets[$referenceIndex].ignoredIssue) {
                        $ignoredIssueReferences.Add([pscustomobject]@{
                            itemId = [string]$item.id
                            pullRequestRepository = [string]$content.repository
                            pullRequestNumber = [int]$content.number
                            source = [string]$references[$referenceIndex].source
                            repository = [string]$references[$referenceIndex].repository
                            number = [int]$references[$referenceIndex].number
                            key = [string]$references[$referenceIndex].key
                        }) | Out-Null
                    }
                }
                $authoritativeReferenceIndexes = @(for ($referenceIndex = 0; $referenceIndex -lt $references.Count; $referenceIndex++) {
                    if ($referenceTargets[$referenceIndex].type -ceq "Issue" -and
                        -not [bool]$referenceTargets[$referenceIndex].ignoredIssue) {
                        $referenceIndex
                    }
                })
                $referencedPriorities = @($authoritativeReferenceIndexes | ForEach-Object { $referenceTargets[$_].priority })
                if ($referencedPriorities.Count -gt 0) {
                    $expectedPriority = [string](@($referencedPriorities | Sort-Object { $priorityRank[$_] })[0])
                    $reason = if ($references[$authoritativeReferenceIndexes[0]].source -eq "closing") { "pr-closing-issue" } else { "pr-body-reference" }
                } else {
                    $expectedPriority = "Priority V"
                    $reason = "pr-no-derived-issue-fallback"
                }
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
        $referenceText = @(for ($referenceIndex = 0; $referenceIndex -lt $references.Count; $referenceIndex++) {
            $authority = if ([bool]$referenceTargets[$referenceIndex].ignoredIssue) { ":ignored" } else { "" }
            "$($references[$referenceIndex].source):$($references[$referenceIndex].repository)#$($references[$referenceIndex].number):$($referenceTargets[$referenceIndex].type)$authority"
        }) -join ", "
        $itemIgnoredIssueReferences = @($ignoredIssueReferences | Where-Object {
            [string]::Equals([string]$_.itemId, [string]$item.id, [System.StringComparison]::Ordinal)
        })

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
            ignoredIssueReferenceCount = $itemIgnoredIssueReferences.Count
            ignoredIssueReferences = @($itemIgnoredIssueReferences | ForEach-Object { "$($_.source):$($_.repository)#$($_.number)" }) -join ", "
            needsUpdate = $needsUpdate
        }
    })

    $ignoredIssueReferenceArray = $ignoredIssueReferences.ToArray()
    $fingerprints = Get-PriorityAuditFingerprints `
        -AuditItems $audit `
        -IssueLabelCache $issueLabelCache `
        -IgnoredIssueReferences $ignoredIssueReferenceArray
    [pscustomobject]@{
        audit = $audit
        updates = @($audit | Where-Object { $_.needsUpdate })
        ignoredIssueReferences = $ignoredIssueReferenceArray
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
        ignoredIssueReferences = @($AuditState.ignoredIssueReferences)
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

    $plannedItems = @($InitialState.updates)
    Assert-PriorityOptionsForUpdates -Updates $plannedItems -OptionMap $OptionMap
    $currentState = & $CurrentStateProvider
    if ($null -eq $currentState -or
        -not [string]::Equals($InitialState.guardFingerprint, $currentState.guardFingerprint, [System.StringComparison]::Ordinal)) {
        throw "Project priority derivation/write plan drifted after the audit; refusing -Apply before the first write."
    }

    $outcome = [pscustomobject]@{
        plannedItems = $plannedItems
        plannedCount = $plannedItems.Count
        attemptedCount = 0
        succeededCount = 0
        failedItemId = $null
        writeErrorMessage = $null
    }
    foreach ($update in $plannedItems) {
        $outcome.attemptedCount++
        try {
            & $ItemWriter $update $OptionMap[$update.expectedPriority] | Out-Null
            $outcome.succeededCount++
        } catch {
            $outcome.failedItemId = [string]$update.itemId
            $outcome.writeErrorMessage = [string]$_.Exception.Message
            break
        }
    }
    $outcome
}

function Invoke-PriorityApplyWithAudit {
    param(
        [object]$InitialState,
        [object]$OptionMap,
        [scriptblock]$CurrentStateProvider,
        [scriptblock]$ItemWriter,
        [scriptblock]$PostAuditProvider
    )

    $writeOutcome = Invoke-PriorityUpdatePlan `
        -InitialState $InitialState `
        -OptionMap $OptionMap `
        -CurrentStateProvider $CurrentStateProvider `
        -ItemWriter $ItemWriter

    $postState = $null
    $postAuditErrorMessage = $null
    try {
        $postStates = @(& $PostAuditProvider)
        if ($postStates.Count -ne 1 -or $null -eq $postStates[0]) {
            throw "Post-audit provider returned $($postStates.Count) states; expected exactly one complete state."
        }
        $postState = $postStates[0]
        if (-not ($postState.PSObject.Properties.Name -contains "snapshot") -or
            -not ($postState.PSObject.Properties.Name -contains "audit") -or
            -not ($postState.PSObject.Properties.Name -contains "updates") -or
            $null -eq $postState.snapshot) {
            throw "Post-audit provider returned an incomplete execution state."
        }
    } catch {
        $postAuditErrorMessage = [string]$_.Exception.Message
    }

    $remainingCount = if ($null -ne $postState) { @($postState.updates).Count } else { $null }
    $postApplyVerified = $null -eq $postAuditErrorMessage -and $remainingCount -eq 0
    $outcome = [pscustomobject]@{
        plannedItems = $writeOutcome.plannedItems
        plannedCount = $writeOutcome.plannedCount
        attemptedCount = $writeOutcome.attemptedCount
        succeededCount = $writeOutcome.succeededCount
        failedItemId = $writeOutcome.failedItemId
        writeErrorMessage = $writeOutcome.writeErrorMessage
        postAuditAttempted = $true
        postState = $postState
        postAuditErrorMessage = $postAuditErrorMessage
        remainingCount = $remainingCount
        postApplyVerified = $postApplyVerified
    }

    if ($null -ne $outcome.writeErrorMessage -or -not $outcome.postApplyVerified) {
        $writeStatus = if ($null -ne $outcome.writeErrorMessage) {
            "Project priority writer failed after $($outcome.succeededCount) of $($outcome.plannedCount) planned writes ($($outcome.attemptedCount) attempts; failed item '$($outcome.failedItemId)'). Writes are non-transactional: $($outcome.writeErrorMessage)."
        } else {
            "Project priority writer completed $($outcome.succeededCount) of $($outcome.plannedCount) planned writes. Writes are non-transactional."
        }
        $auditStatus = if ($null -ne $outcome.postAuditErrorMessage) {
            "Complete post-Apply audit failed: $($outcome.postAuditErrorMessage). Final project state is unknown."
        } else {
            "Complete post-Apply audit completed: remaining=$($outcome.remainingCount), updatedAt='$($outcome.postState.snapshot.projectUpdatedAt)'. Post-audit state is authoritative."
        }
        throw "$writeStatus $auditStatus"
    }

    $outcome
}

function New-PriorityReportState {
    param(
        [object]$InitialState,
        [AllowNull()]
        [object]$ApplyOutcome
    )

    $currentState = $InitialState
    $plannedItems = @($InitialState.updates)
    $attemptedCount = 0
    $appliedCount = 0
    $postApplyVerified = $false
    if ($null -ne $ApplyOutcome) {
        $currentState = $ApplyOutcome.postState
        $plannedItems = @($ApplyOutcome.plannedItems)
        $attemptedCount = [int]$ApplyOutcome.attemptedCount
        $appliedCount = [int]$ApplyOutcome.succeededCount
        $postApplyVerified = [bool]$ApplyOutcome.postApplyVerified
    }

    [pscustomobject]@{
        snapshot = $currentState.snapshot
        audit = @($currentState.audit)
        updates = @($currentState.updates)
        ignoredIssueReferences = @($currentState.ignoredIssueReferences)
        plannedItems = $plannedItems
        plannedCount = $plannedItems.Count
        attemptedCount = $attemptedCount
        appliedCount = $appliedCount
        postApplyVerified = $postApplyVerified
    }
}

function Get-LivePriorityExecutionContext {
    param(
        [string]$OwnerName,
        [int]$Number,
        [string]$ProjectId,
        [int]$ItemLimit,
        [Parameter(Mandatory = $true)]
        [string]$CanonicalRepository,
        [scriptblock]$ReferenceProvider
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
    $snapshot = Get-CompleteProjectSnapshotWithRestart `
        -ProjectId $ProjectId `
        -PriorityFieldId $priorityField.id `
        -StatusFieldId $statusField.id `
        -ItemLimit $ItemLimit `
        -PageProvider $pageProvider
    if ($snapshot.projectNumber -ne $Number) {
        throw "ProjectV2 '$ProjectId' reported number $($snapshot.projectNumber), expected $Number."
    }

    $auditState = New-PriorityAuditState `
        -Items @($snapshot.items) `
        -CanonicalRepository $CanonicalRepository `
        -ReferenceProvider $ReferenceProvider
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

$referenceProvider = {
    param($repositoryName, $issueNumber)
    Get-LiveReferenceTarget -RepositoryName $repositoryName -Number $issueNumber
}

$context = Get-LivePriorityExecutionContext `
    -OwnerName $Owner `
    -Number $ProjectNumber `
    -ProjectId $project.id `
    -ItemLimit $Limit `
    -CanonicalRepository $Repository `
    -ReferenceProvider $referenceProvider
$executionState = $context.executionState
$applyOutcome = $null

if ($Apply) {
    $currentStateProvider = {
        (Get-LivePriorityExecutionContext `
            -OwnerName $Owner `
            -Number $ProjectNumber `
            -ProjectId $project.id `
            -ItemLimit $Limit `
            -CanonicalRepository $Repository `
            -ReferenceProvider $referenceProvider).executionState
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

    $postAuditProvider = {
        (Get-LivePriorityExecutionContext `
            -OwnerName $Owner `
            -Number $ProjectNumber `
            -ProjectId $project.id `
            -ItemLimit $Limit `
            -CanonicalRepository $Repository `
            -ReferenceProvider $referenceProvider).executionState
    }

    $applyOutcome = Invoke-PriorityApplyWithAudit `
        -InitialState $executionState `
        -OptionMap $context.optionMap `
        -CurrentStateProvider $currentStateProvider `
        -ItemWriter $itemWriter `
        -PostAuditProvider $postAuditProvider
}

$reportState = New-PriorityReportState -InitialState $executionState -ApplyOutcome $applyOutcome
$snapshot = $reportState.snapshot
$items = @($snapshot.items)
$audit = @($reportState.audit)
$updates = @($reportState.updates)
$ignoredIssueReferences = @($reportState.ignoredIssueReferences)

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
        snapshotRestartCount = $snapshot.snapshotRestartCount
        snapshotRestartDiagnostics = @($snapshot.snapshotRestartDiagnostics)
        limit = $Limit
        ignoredIssueReferenceCount = $ignoredIssueReferences.Count
        ignoredIssueReferences = $ignoredIssueReferences
        needsUpdate = $updates.Count
        planned = $reportState.plannedCount
        writerAttempts = $reportState.attemptedCount
        applied = $reportState.appliedCount
        postApplyVerified = $reportState.postApplyVerified
        plannedItems = $reportState.plannedItems
        items = $updates
    } | ConvertTo-Json -Depth 8
    return
}

Write-Host "# Taskdeck Project Priority Audit"
Write-Host ""
Write-Host "Project: $($project.title) (#$ProjectNumber)"
Write-Host "Items scanned: $($items.Count)"
Write-Host "Completeness: COMPLETE ($($items.Count)/$($snapshot.totalCount) items across $($snapshot.pageCount) pages; project updatedAt $($snapshot.projectUpdatedAt))"
Write-Host "Snapshot restarts: $($snapshot.snapshotRestartCount)"
foreach ($restartDiagnostic in @($snapshot.snapshotRestartDiagnostics)) {
    Write-Host "- Restart $($restartDiagnostic.attempt): $($restartDiagnostic.kind) drift at page $($restartDiagnostic.page)"
}
Write-Host "Ignored cross-repository Issue references: $($ignoredIssueReferences.Count)"
Write-Host "Items needing priority sync: $($updates.Count)"
if ($Apply) {
    Write-Host "Initial updates planned: $($reportState.plannedCount)"
    Write-Host "Writer attempts: $($reportState.attemptedCount)"
    Write-Host "Writer-confirmed updates: $($reportState.appliedCount)"
    Write-Host "Post-Apply complete audit: VERIFIED"
}
Write-Host ""

foreach ($ignoredReference in $ignoredIssueReferences) {
    Write-Host "- Ignored $($ignoredReference.source) reference $($ignoredReference.repository)#$($ignoredReference.number) from PullRequest $($ignoredReference.pullRequestRepository)#$($ignoredReference.pullRequestNumber) (non-authoritative)"
}
if ($ignoredIssueReferences.Count -gt 0) {
    Write-Host ""
}

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
