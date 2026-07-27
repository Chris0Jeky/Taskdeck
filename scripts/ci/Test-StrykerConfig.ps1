[CmdletBinding()]
param(
    [string]$ConfigPath,
    [string]$WorkflowPath,
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path (Join-Path $repositoryRoot 'backend') 'stryker-config.json'
}

if ([string]::IsNullOrWhiteSpace($WorkflowPath)) {
    $workflowsDirectory = Join-Path (Join-Path $repositoryRoot '.github') 'workflows'
    $WorkflowPath = Join-Path $workflowsDirectory 'mutation-testing.yml'
}

function Test-StrykerConfig {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $resolvedPath = Resolve-Path -LiteralPath $Path -ErrorAction Stop

    try {
        $config = Get-Content -LiteralPath $resolvedPath -Raw | ConvertFrom-Json -ErrorAction Stop
    } catch {
        throw "Stryker configuration '$resolvedPath' is not valid JSON: $($_.Exception.Message)"
    }

    $strykerConfig = $config.'stryker-config'
    if ($null -eq $strykerConfig) {
        throw "Stryker configuration '$resolvedPath' must contain a stryker-config object."
    }

    foreach ($obsoleteKey in @('ignored-methods', 'excluded-mutations')) {
        if ($null -ne $strykerConfig.PSObject.Properties[$obsoleteKey]) {
            $replacementKey = $obsoleteKey -replace '^excluded-', 'ignore-' -replace '^ignored-', 'ignore-'
            throw "Stryker configuration '$resolvedPath' uses obsolete '$obsoleteKey'. Use '$replacementKey' instead."
        }
    }

    foreach ($solutionContextKey in @('solution', 'test-projects')) {
        if ($null -ne $strykerConfig.PSObject.Properties[$solutionContextKey]) {
            throw "Stryker configuration '$resolvedPath' must omit '$solutionContextKey' so the workflow stays in Taskdeck.Domain.Tests context."
        }
    }

    $projectProperty = $strykerConfig.PSObject.Properties['project']
    if ($null -eq $projectProperty -or $projectProperty.Value -cne 'Taskdeck.Domain.csproj') {
        throw "Stryker configuration '$resolvedPath' must target project 'Taskdeck.Domain.csproj'."
    }

    $mutationLevelProperty = $strykerConfig.PSObject.Properties['mutation-level']
    if ($null -eq $mutationLevelProperty -or $mutationLevelProperty.Value -cne 'Standard') {
        throw "Stryker configuration '$resolvedPath' must preserve mutation-level 'Standard'."
    }

    $reportersProperty = $strykerConfig.PSObject.Properties['reporters']
    if ($null -eq $reportersProperty -or $reportersProperty.Value -isnot [System.Array]) {
        throw "Stryker configuration '$resolvedPath' must define reporters as an array."
    }

    $reporters = @($reportersProperty.Value)
    if (($reporters -join ',') -cne 'html,json,progress,cleartext') {
        throw "Stryker configuration '$resolvedPath' must preserve html, json, progress, and cleartext reporters."
    }

    $thresholdsProperty = $strykerConfig.PSObject.Properties['thresholds']
    $thresholds = $thresholdsProperty.Value
    if ($null -eq $thresholdsProperty -or
        $null -eq $thresholds -or
        $thresholds.high -ne 80 -or
        $thresholds.low -ne 60 -or
        $thresholds.break -ne 0) {
        throw "Stryker configuration '$resolvedPath' must preserve thresholds high=80, low=60, break=0."
    }

    foreach ($requiredEmptyKey in @('ignore-methods', 'ignore-mutations')) {
        $property = $strykerConfig.PSObject.Properties[$requiredEmptyKey]
        if ($null -eq $property -or $property.Value -isnot [System.Array]) {
            throw "Stryker configuration '$resolvedPath' must contain '$requiredEmptyKey' as an array."
        }

        if ($property.Value.Count -ne 0) {
            throw "Stryker configuration '$resolvedPath' must preserve the empty '$requiredEmptyKey' list."
        }
    }
}

function Assert-ExactWorkflowLine {
    param(
        [Parameter(Mandatory)]
        [string]$Block,
        [Parameter(Mandatory)]
        [string]$ExpectedLine,
        [Parameter(Mandatory)]
        [string]$ContractName
    )

    $count = @(($Block -split "`n") | Where-Object { $_ -ceq $ExpectedLine }).Count
    if ($count -ne 1) {
        throw "Mutation workflow must contain exactly one $ContractName line '$ExpectedLine' in backend-mutation; found $count."
    }
}

function Assert-ExactWorkflowFragment {
    param(
        [Parameter(Mandatory)]
        [string]$Block,
        [Parameter(Mandatory)]
        [string]$ExpectedFragment,
        [Parameter(Mandatory)]
        [string]$ContractName
    )

    $count = 0
    $searchIndex = 0
    while ($true) {
        $matchIndex = $Block.IndexOf($ExpectedFragment, $searchIndex, [System.StringComparison]::Ordinal)
        if ($matchIndex -lt 0) {
            break
        }

        $count++
        $searchIndex = $matchIndex + $ExpectedFragment.Length
    }

    if ($count -ne 1) {
        throw "Mutation workflow must contain exactly one $ContractName block in backend-mutation; found $count."
    }
}

function Test-MutationWorkflowContract {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $resolvedPath = Resolve-Path -LiteralPath $Path -ErrorAction Stop
    $workflow = [System.IO.File]::ReadAllText($resolvedPath)
    $normalizedWorkflow = $workflow.Replace("`r`n", "`n").Replace("`r", "`n")
    $backendMarker = "  backend-mutation:`n"
    $frontendMarker = "  frontend-mutation:`n"
    $backendStart = $normalizedWorkflow.IndexOf($backendMarker, [System.StringComparison]::Ordinal)
    $frontendStart = $normalizedWorkflow.IndexOf($frontendMarker, [System.StringComparison]::Ordinal)

    if ($backendStart -lt 0 -or $frontendStart -le $backendStart) {
        throw "Mutation workflow '$resolvedPath' must contain backend-mutation before frontend-mutation."
    }

    $backendBlock = $normalizedWorkflow.Substring($backendStart, $frontendStart - $backendStart)
    $requiredLines = [ordered]@{
        'finite timeout' = '    timeout-minutes: 180'
        'pinned tool install' = '        run: dotnet tool install --global dotnet-stryker --version 4.16.0'
        'configuration self-test' = '        run: ./scripts/ci/Test-StrykerConfig.ps1 -SelfTest'
    }

    foreach ($entry in $requiredLines.GetEnumerator()) {
        Assert-ExactWorkflowLine -Block $backendBlock -ExpectedLine $entry.Value -ContractName $entry.Key
    }

    $strykerStep = @(
        '      - name: Run Stryker.NET',
        '        working-directory: backend/tests/Taskdeck.Domain.Tests',
        '        run: dotnet stryker --config-file ../../stryker-config.json --output ../../StrykerOutput'
    ) -join "`n"
    Assert-ExactWorkflowFragment -Block $backendBlock -ExpectedFragment $strykerStep -ContractName 'test-project Stryker step'

    $artifactStep = @(
        '      - name: Upload Stryker report',
        '        if: always()',
        '        uses: actions/upload-artifact@v7',
        '        with:',
        '          name: stryker-net-report',
        '          path: backend/StrykerOutput/**/reports/',
        '          if-no-files-found: error',
        '          retention-days: 30'
    ) -join "`n"
    Assert-ExactWorkflowFragment -Block $backendBlock -ExpectedFragment $artifactStep -ContractName 'fail-closed report artifact step'

    $strykerCommandCount = [regex]::Matches(
        $backendBlock,
        '(?m)^\s+run:\s+(?:dotnet stryker|(?:\S+/)?dotnet-stryker)(?:\s|$)'
    ).Count
    if ($strykerCommandCount -ne 1) {
        throw "Mutation workflow '$resolvedPath' must invoke Stryker exactly once in backend-mutation; found $strykerCommandCount."
    }
}

function Assert-Rejected {
    param(
        [Parameter(Mandatory)]
        [scriptblock]$Action,
        [Parameter(Mandatory)]
        [string]$Scenario,
        [Parameter(Mandatory)]
        [string]$ExpectedMessage
    )

    try {
        & $Action
    } catch {
        if (-not $_.Exception.Message.Contains($ExpectedMessage)) {
            throw "The Stryker preflight rejected '$Scenario' for an unexpected reason: $($_.Exception.Message)"
        }

        return
    }

    throw "The Stryker preflight accepted invalid scenario '$Scenario'."
}

function Write-TextVariant {
    param(
        [Parameter(Mandatory)]
        [string]$Source,
        [Parameter(Mandatory)]
        [string]$Destination,
        [Parameter(Mandatory)]
        [string]$Expected,
        [Parameter(Mandatory)]
        [string]$Replacement
    )

    $content = [System.IO.File]::ReadAllText($Source)
    $firstMatch = $content.IndexOf($Expected, [System.StringComparison]::Ordinal)
    $lastMatch = $content.LastIndexOf($Expected, [System.StringComparison]::Ordinal)
    if ($firstMatch -lt 0 -or $firstMatch -ne $lastMatch) {
        throw "Self-test fixture expected exactly one source fragment '$Expected'."
    }

    [System.IO.File]::WriteAllText($Destination, $content.Replace($Expected, $Replacement))
}

function Invoke-StrykerConfigSelfTest {
    $temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("taskdeck-stryker-config-$([guid]::NewGuid().ToString('N'))")
    $validConfigPath = Join-Path $temporaryDirectory 'valid.json'
    $validWorkflowPath = Join-Path $temporaryDirectory 'valid.yml'

    try {
        New-Item -ItemType Directory -Path $temporaryDirectory -ErrorAction Stop | Out-Null
        Copy-Item -LiteralPath $ConfigPath -Destination $validConfigPath -ErrorAction Stop
        Copy-Item -LiteralPath $WorkflowPath -Destination $validWorkflowPath -ErrorAction Stop
        Test-StrykerConfig -Path $validConfigPath
        Test-MutationWorkflowContract -Path $validWorkflowPath

        $configVariants = @(
            @('obsolete ignored-methods key', '    "ignore-methods": [],', '    "ignored-methods": [],', "uses obsolete 'ignored-methods'"),
            @('obsolete excluded-mutations key', '    "ignore-mutations": []', '    "excluded-mutations": []', "uses obsolete 'excluded-mutations'"),
            @('solution context', '    "project": "Taskdeck.Domain.csproj",', (@('    "project": "Taskdeck.Domain.csproj",', '    "solution": "Taskdeck.sln",') -join "`n"), "must omit 'solution'"),
            @('test-project selector', '    "project": "Taskdeck.Domain.csproj",', (@('    "project": "Taskdeck.Domain.csproj",', '    "test-projects": ["tests/Taskdeck.Domain.Tests/Taskdeck.Domain.Tests.csproj"],') -join "`n"), "must omit 'test-projects'"),
            @('wrong mutation target', '    "project": "Taskdeck.Domain.csproj",', '    "project": "Taskdeck.Application.csproj",', "must target project 'Taskdeck.Domain.csproj'"),
            @('changed empty ignore semantics', '    "ignore-mutations": []', '    "ignore-mutations": ["string"]', "must preserve the empty 'ignore-mutations' list"),
            @('changed mutation level', '    "mutation-level": "Standard",', '    "mutation-level": "Advanced",', "must preserve mutation-level 'Standard'"),
            @('missing JSON reporter', '      "json",', '      "dashboard",', 'must preserve html, json, progress, and cleartext reporters'),
            @('changed score threshold', '      "break": 0', '      "break": 60', 'must preserve thresholds high=80, low=60, break=0')
        )

        for ($configIndex = 0; $configIndex -lt $configVariants.Count; $configIndex++) {
            $configVariant = $configVariants[$configIndex]
            $variantPath = Join-Path $temporaryDirectory "config-$configIndex.json"
            Write-TextVariant -Source $ConfigPath -Destination $variantPath -Expected $configVariant[1] -Replacement $configVariant[2]
            Assert-Rejected -Scenario $configVariant[0] -ExpectedMessage $configVariant[3] -Action {
                Test-StrykerConfig -Path $variantPath
            }
        }

        $workflowVariants = @(
            @('timeout', '    timeout-minutes: 180', '    timeout-minutes: 60', 'finite timeout'),
            @('tool version', '        run: dotnet tool install --global dotnet-stryker --version 4.16.0', '        run: dotnet tool install --global dotnet-stryker --version 4.17.0', 'pinned tool install'),
            @('preflight mode', '        run: ./scripts/ci/Test-StrykerConfig.ps1 -SelfTest', '        run: ./scripts/ci/Test-StrykerConfig.ps1', 'configuration self-test'),
            @('working-directory', '        working-directory: backend/tests/Taskdeck.Domain.Tests', '        working-directory: backend', 'test-project Stryker step'),
            @('config path', '        run: dotnet stryker --config-file ../../stryker-config.json --output ../../StrykerOutput', '        run: dotnet stryker --config-file stryker-config.json', 'test-project Stryker step'),
            @('artifact path', '          path: backend/StrykerOutput/**/reports/', '          path: backend/tests/Taskdeck.Domain.Tests/StrykerOutput/**/reports/', 'fail-closed report artifact step'),
            @('missing artifact policy', '          if-no-files-found: error', '          if-no-files-found: warn', 'fail-closed report artifact step')
        )

        foreach ($workflowVariant in $workflowVariants) {
            $variantPath = Join-Path $temporaryDirectory "$($workflowVariant[0]).yml"
            Write-TextVariant -Source $WorkflowPath -Destination $variantPath -Expected $workflowVariant[1] -Replacement $workflowVariant[2]
            Assert-Rejected -Scenario "workflow $($workflowVariant[0]) drift" -ExpectedMessage $workflowVariant[3] -Action {
                Test-MutationWorkflowContract -Path $variantPath
            }
        }

        $rejectedFixtureCount = $configVariants.Count + $workflowVariants.Count
        Write-Host "Stryker preflight self-test passed: $($rejectedFixtureCount + 2) checks (2 valid contracts; $rejectedFixtureCount rejected drift fixtures)."
    } finally {
        if (Test-Path -LiteralPath $temporaryDirectory) {
            Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
        }
    }
}

Test-StrykerConfig -Path $ConfigPath
Test-MutationWorkflowContract -Path $WorkflowPath

if ($SelfTest) {
    Invoke-StrykerConfigSelfTest
}

Write-Host "Stryker configuration and workflow preflight passed: $ConfigPath; $WorkflowPath"
