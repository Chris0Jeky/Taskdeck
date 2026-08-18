[CmdletBinding()]
param(
    [string]$ConfigPath,
    [string]$WorkflowPath,
    [string]$ToolManifestPath,
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

if ([string]::IsNullOrWhiteSpace($ToolManifestPath)) {
    $ToolManifestPath = Join-Path (Join-Path $repositoryRoot '.config') 'dotnet-tools.json'
}

function Test-IsJsonNumber {
    param(
        [object]$Value
    )

    return $Value -is [System.Byte] -or
        $Value -is [System.SByte] -or
        $Value -is [System.Int16] -or
        $Value -is [System.UInt16] -or
        $Value -is [System.Int32] -or
        $Value -is [System.UInt32] -or
        $Value -is [System.Int64] -or
        $Value -is [System.UInt64] -or
        $Value -is [System.Single] -or
        $Value -is [System.Double] -or
        $Value -is [System.Decimal]
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
    if ($null -eq $thresholdsProperty -or $null -eq $thresholdsProperty.Value) {
        throw "Stryker configuration '$resolvedPath' must preserve thresholds high=80, low=60, break=0."
    }

    $thresholds = $thresholdsProperty.Value
    $expectedThresholds = [ordered]@{
        high = 80
        low = 60
        break = 0
    }

    foreach ($expectedThreshold in $expectedThresholds.GetEnumerator()) {
        $thresholdProperty = $thresholds.PSObject.Properties[$expectedThreshold.Key]
        if ($null -eq $thresholdProperty -or
            -not (Test-IsJsonNumber -Value $thresholdProperty.Value) -or
            [decimal]$thresholdProperty.Value -ne [decimal]$expectedThreshold.Value) {
            throw "Stryker configuration '$resolvedPath' must preserve thresholds high=80, low=60, break=0 as numeric values."
        }
    }

    foreach ($exclusionKey in @('ignore-methods', 'ignore-mutations')) {
        $property = $strykerConfig.PSObject.Properties[$exclusionKey]
        if ($null -eq $property -or $property.Value -isnot [System.Array]) {
            throw "Stryker configuration '$resolvedPath' must contain '$exclusionKey' as an array."
        }

        for ($entryIndex = 0; $entryIndex -lt $property.Value.Count; $entryIndex++) {
            $entry = $property.Value[$entryIndex]
            if ($entry -isnot [string] -or [string]::IsNullOrWhiteSpace($entry)) {
                throw "Stryker configuration '$resolvedPath' entry $entryIndex in '$exclusionKey' must be a non-empty string."
            }
        }
    }
}

function Test-StrykerToolManifest {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $resolvedPath = Resolve-Path -LiteralPath $Path -ErrorAction Stop

    try {
        $manifest = Get-Content -LiteralPath $resolvedPath -Raw | ConvertFrom-Json -ErrorAction Stop
    } catch {
        throw "Stryker tool manifest '$resolvedPath' is not valid JSON: $($_.Exception.Message)"
    }

    if ($manifest.version -ne 1 -or $manifest.isRoot -isnot [bool] -or -not $manifest.isRoot) {
        throw "Stryker tool manifest '$resolvedPath' must be a version 1 root manifest."
    }

    $toolsProperty = $manifest.PSObject.Properties['tools']
    $strykerToolProperty = if ($null -eq $toolsProperty) {
        $null
    } else {
        $toolsProperty.Value.PSObject.Properties['dotnet-stryker']
    }

    if ($null -eq $strykerToolProperty) {
        throw "Stryker tool manifest '$resolvedPath' must contain dotnet-stryker."
    }

    $strykerTool = $strykerToolProperty.Value
    if ($strykerTool.version -cne '4.16.0') {
        throw "Stryker tool manifest '$resolvedPath' must pin dotnet-stryker version '4.16.0'."
    }

    $commandsProperty = $strykerTool.PSObject.Properties['commands']
    if ($null -eq $commandsProperty -or
        $commandsProperty.Value -isnot [System.Array] -or
        @($commandsProperty.Value).Count -ne 1 -or
        $commandsProperty.Value[0] -cne 'dotnet-stryker') {
        throw "Stryker tool manifest '$resolvedPath' must expose only the dotnet-stryker command."
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

function Assert-ExactWorkflowStep {
    param(
        [Parameter(Mandatory)]
        [string]$Block,
        [Parameter(Mandatory)]
        [string]$ExpectedStep,
        [Parameter(Mandatory)]
        [string]$ContractName
    )

    $blockLines = @($Block -split "`n")
    $expectedLines = @($ExpectedStep -split "`n")
    $expectedHeader = $expectedLines[0]
    $headerIndices = @(
        for ($lineIndex = 0; $lineIndex -lt $blockLines.Count; $lineIndex++) {
            if ($blockLines[$lineIndex] -ceq $expectedHeader) {
                $lineIndex
            }
        }
    )

    if ($headerIndices.Count -ne 1) {
        throw "Mutation workflow must contain exactly one $ContractName step '$expectedHeader' in backend-mutation; found $($headerIndices.Count)."
    }

    $stepStart = $headerIndices[0]
    $stepEnd = $stepStart + 1
    while ($stepEnd -lt $blockLines.Count -and -not $blockLines[$stepEnd].StartsWith('      - ')) {
        $stepEnd++
    }

    $lastStepLine = $stepEnd - 1
    while ($lastStepLine -gt $stepStart -and [string]::IsNullOrEmpty($blockLines[$lastStepLine])) {
        $lastStepLine--
    }

    $actualStep = @($blockLines[$stepStart..$lastStepLine]) -join "`n"
    if ($actualStep -cne $ExpectedStep) {
        throw "Mutation workflow must preserve the complete $ContractName step in backend-mutation."
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
    }

    foreach ($entry in $requiredLines.GetEnumerator()) {
        Assert-ExactWorkflowLine -Block $backendBlock -ExpectedLine $entry.Value -ContractName $entry.Key
    }

    $toolRestoreStep = @(
        '      - name: Restore Stryker.NET tool',
        '        run: dotnet tool restore'
    ) -join "`n"
    Assert-ExactWorkflowStep -Block $backendBlock -ExpectedStep $toolRestoreStep -ContractName 'pinned tool restore'

    $preflightStep = @(
        '      - name: Validate Stryker.NET configuration',
        '        shell: pwsh',
        '        run: ./scripts/ci/Test-StrykerConfig.ps1 -SelfTest'
    ) -join "`n"
    Assert-ExactWorkflowStep -Block $backendBlock -ExpectedStep $preflightStep -ContractName 'configuration self-test'

    $strykerStep = @(
        '      - name: Run Stryker.NET',
        '        working-directory: backend/tests/Taskdeck.Domain.Tests',
        '        run: dotnet tool run dotnet-stryker -- --config-file ../../stryker-config.json --output ../../StrykerOutput'
    ) -join "`n"
    Assert-ExactWorkflowStep -Block $backendBlock -ExpectedStep $strykerStep -ContractName 'test-project Stryker'

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
    Assert-ExactWorkflowStep -Block $backendBlock -ExpectedStep $artifactStep -ContractName 'fail-closed report artifact'

    $strykerCommandCount = [regex]::Matches(
        $backendBlock,
        '(?m)^\s+run:\s+(?:dotnet stryker|dotnet tool run dotnet-stryker|(?:\S+/)?dotnet-stryker)(?:\s|$)'
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

    $content = ([System.IO.File]::ReadAllText($Source)).Replace("`r`n", "`n").Replace("`r", "`n")
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
    $validToolManifestPath = Join-Path $temporaryDirectory 'dotnet-tools.json'

    try {
        New-Item -ItemType Directory -Path $temporaryDirectory -ErrorAction Stop | Out-Null
        Copy-Item -LiteralPath $ConfigPath -Destination $validConfigPath -ErrorAction Stop
        Copy-Item -LiteralPath $WorkflowPath -Destination $validWorkflowPath -ErrorAction Stop
        Copy-Item -LiteralPath $ToolManifestPath -Destination $validToolManifestPath -ErrorAction Stop
        Test-StrykerConfig -Path $validConfigPath
        Test-MutationWorkflowContract -Path $validWorkflowPath
        Test-StrykerToolManifest -Path $validToolManifestPath

        $validExclusionVariants = @(
            @('ignore-methods', '    "ignore-methods": [],', '    "ignore-methods": ["ToString", "Console.Write*"],'),
            @('ignore-mutations', '    "ignore-mutations": []', '    "ignore-mutations": ["string", "logical"]')
        )

        for ($validIndex = 0; $validIndex -lt $validExclusionVariants.Count; $validIndex++) {
            $validVariant = $validExclusionVariants[$validIndex]
            $validVariantPath = Join-Path $temporaryDirectory "valid-$validIndex.json"
            Write-TextVariant -Source $ConfigPath -Destination $validVariantPath -Expected $validVariant[1] -Replacement $validVariant[2]
            Test-StrykerConfig -Path $validVariantPath
        }

        $configVariants = @(
            @('obsolete ignored-methods key', '    "ignore-methods": [],', '    "ignored-methods": [],', "uses obsolete 'ignored-methods'"),
            @('obsolete excluded-mutations key', '    "ignore-mutations": []', '    "excluded-mutations": []', "uses obsolete 'excluded-mutations'"),
            @('solution context', '    "project": "Taskdeck.Domain.csproj",', (@('    "project": "Taskdeck.Domain.csproj",', '    "solution": "Taskdeck.sln",') -join "`n"), "must omit 'solution'"),
            @('test-project selector', '    "project": "Taskdeck.Domain.csproj",', (@('    "project": "Taskdeck.Domain.csproj",', '    "test-projects": ["tests/Taskdeck.Domain.Tests/Taskdeck.Domain.Tests.csproj"],') -join "`n"), "must omit 'test-projects'"),
            @('wrong mutation target', '    "project": "Taskdeck.Domain.csproj",', '    "project": "Taskdeck.Application.csproj",', "must target project 'Taskdeck.Domain.csproj'"),
            @('ignore-methods scalar', '    "ignore-methods": [],', '    "ignore-methods": "ToString",', "must contain 'ignore-methods' as an array"),
            @('ignore-mutations scalar', '    "ignore-mutations": []', '    "ignore-mutations": "string"', "must contain 'ignore-mutations' as an array"),
            @('ignore-methods null entry', '    "ignore-methods": [],', '    "ignore-methods": [null],', "entry 0 in 'ignore-methods' must be a non-empty string"),
            @('ignore-mutations non-string entry', '    "ignore-mutations": []', '    "ignore-mutations": [42]', "entry 0 in 'ignore-mutations' must be a non-empty string"),
            @('ignore-methods empty entry', '    "ignore-methods": [],', '    "ignore-methods": [""],', "entry 0 in 'ignore-methods' must be a non-empty string"),
            @('ignore-mutations whitespace entry', '    "ignore-mutations": []', '    "ignore-mutations": ["   "]', "entry 0 in 'ignore-mutations' must be a non-empty string"),
            @('changed mutation level', '    "mutation-level": "Standard",', '    "mutation-level": "Advanced",', "must preserve mutation-level 'Standard'"),
            @('missing JSON reporter', '      "json",', '      "dashboard",', 'must preserve html, json, progress, and cleartext reporters'),
            @('changed score threshold', '      "break": 0', '      "break": 60', 'must preserve thresholds high=80, low=60, break=0'),
            @('quoted score threshold', '      "high": 80,', '      "high": "80",', 'must preserve thresholds high=80, low=60, break=0 as numeric values'),
            @('boolean score threshold', '      "break": 0', '      "break": false', 'must preserve thresholds high=80, low=60, break=0 as numeric values')
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
            @('tool restore', '        run: dotnet tool restore', '        run: dotnet tool install --global dotnet-stryker --version 4.16.0', 'pinned tool restore'),
            @('preflight mode', '        run: ./scripts/ci/Test-StrykerConfig.ps1 -SelfTest', '        run: ./scripts/ci/Test-StrykerConfig.ps1', 'configuration self-test'),
            @('working-directory', '        working-directory: backend/tests/Taskdeck.Domain.Tests', '        working-directory: backend', 'test-project Stryker step'),
            @('config path', '        run: dotnet tool run dotnet-stryker -- --config-file ../../stryker-config.json --output ../../StrykerOutput', '        run: dotnet tool run dotnet-stryker -- --config-file stryker-config.json', 'test-project Stryker'),
            @('expanded mutation scope', '        run: dotnet tool run dotnet-stryker -- --config-file ../../stryker-config.json --output ../../StrykerOutput', '        run: dotnet tool run dotnet-stryker -- --config-file ../../stryker-config.json --output ../../StrykerOutput --mutate SomeFile.cs', 'test-project Stryker'),
            @('masked Stryker failure', '        run: dotnet tool run dotnet-stryker -- --config-file ../../stryker-config.json --output ../../StrykerOutput', '        run: dotnet tool run dotnet-stryker -- --config-file ../../stryker-config.json --output ../../StrykerOutput || true', 'test-project Stryker'),
            @('artifact path', '          path: backend/StrykerOutput/**/reports/', '          path: backend/tests/Taskdeck.Domain.Tests/StrykerOutput/**/reports/', 'fail-closed report artifact step'),
            @('missing artifact policy', '          if-no-files-found: error', '          if-no-files-found: warn', 'fail-closed report artifact step'),
            @('artifact continue-on-error', (@('          if-no-files-found: error', '          retention-days: 30') -join "`n"), (@('          if-no-files-found: error', '          retention-days: 30', '        continue-on-error: true') -join "`n"), 'fail-closed report artifact')
        )

        foreach ($workflowVariant in $workflowVariants) {
            $variantPath = Join-Path $temporaryDirectory "$($workflowVariant[0]).yml"
            Write-TextVariant -Source $WorkflowPath -Destination $variantPath -Expected $workflowVariant[1] -Replacement $workflowVariant[2]
            Assert-Rejected -Scenario "workflow $($workflowVariant[0]) drift" -ExpectedMessage $workflowVariant[3] -Action {
                Test-MutationWorkflowContract -Path $variantPath
            }
        }

        $toolManifestVariants = @(
            @('tool version', '      "version": "4.16.0",', '      "version": "4.17.0",', "must pin dotnet-stryker version '4.16.0'"),
            @('tool command', (@('      "commands": [', '        "dotnet-stryker"', '      ]') -join "`n"), (@('      "commands": [', '        "other-command"', '      ]') -join "`n"), 'must expose only the dotnet-stryker command')
        )

        foreach ($toolManifestVariant in $toolManifestVariants) {
            $variantPath = Join-Path $temporaryDirectory "manifest-$($toolManifestVariant[0]).json"
            Write-TextVariant -Source $ToolManifestPath -Destination $variantPath -Expected $toolManifestVariant[1] -Replacement $toolManifestVariant[2]
            Assert-Rejected -Scenario "tool manifest $($toolManifestVariant[0]) drift" -ExpectedMessage $toolManifestVariant[3] -Action {
                Test-StrykerToolManifest -Path $variantPath
            }
        }

        $validContractCount = 3 + $validExclusionVariants.Count
        $rejectedFixtureCount = $configVariants.Count + $workflowVariants.Count + $toolManifestVariants.Count
        Write-Host "Stryker preflight self-test passed: $($validContractCount + $rejectedFixtureCount) checks ($validContractCount valid contracts; $rejectedFixtureCount rejected drift fixtures)."
    } finally {
        if (Test-Path -LiteralPath $temporaryDirectory) {
            Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
        }
    }
}

Test-StrykerConfig -Path $ConfigPath
Test-MutationWorkflowContract -Path $WorkflowPath
Test-StrykerToolManifest -Path $ToolManifestPath

if ($SelfTest) {
    Invoke-StrykerConfigSelfTest
}

Write-Host "Stryker configuration, workflow, and tool-manifest preflight passed: $ConfigPath; $WorkflowPath; $ToolManifestPath"
