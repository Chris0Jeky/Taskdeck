param()

$ErrorActionPreference = 'Stop'

$profileScript = Join-Path $PSScriptRoot 'Test-DockerMcpProfile.ps1'
$credentialScript = Join-Path $PSScriptRoot 'Set-MarketplaceMcpCredentials.ps1'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$drillScript = Join-Path $repoRoot 'scripts\drills\drill-mcp-invalid-credentials.sh'
$powerShellExecutable = (Get-Process -Id $PID).Path
$bashCommand = Get-Command -Name bash -CommandType Application -TotalCount 1 -ErrorAction SilentlyContinue
$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("taskdeck-mcp-profile-tests-$([Guid]::NewGuid().ToString('N'))")
$fakeDockerScript = Join-Path $fixtureRoot 'FakeDocker.ps1'
$fakeDockerCommand = Join-Path $fixtureRoot 'docker.cmd'
$fakeDrillDockerCommand = Join-Path $fixtureRoot 'docker'
$fakeDrillPowerShellCommand = Join-Path $fixtureRoot 'powershell.exe'
$fakeStatePath = Join-Path $fixtureRoot 'state.txt'
$fakeLogPath = Join-Path $fixtureRoot 'docker.log'
$fakeUserProfile = Join-Path $fixtureRoot 'user-profile'

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-Equal {
    param(
        $Expected,
        $Actual,
        [string]$Message
    )

    if ($Expected -cne $Actual) {
        throw "$Message Expected '$Expected', got '$Actual'."
    }
}

function Assert-Contains {
    param(
        [string]$Text,
        [string]$ExpectedSubstring,
        [string]$Message
    )

    if ($Text.IndexOf($ExpectedSubstring, [System.StringComparison]::Ordinal) -lt 0) {
        throw "$Message Missing '$ExpectedSubstring'.`n$Text"
    }
}

function Get-FakeDockerCommands {
    if (-not (Test-Path -LiteralPath $fakeLogPath -PathType Leaf)) {
        return @()
    }

    return @(Get-Content -LiteralPath $fakeLogPath | ForEach-Object { $_ -replace [char]31, ' ' })
}

function Convert-ToBashPath {
    param([string]$WindowsPath)

    if ($null -eq $bashCommand) {
        return ''
    }
    if ($bashCommand.Source -like "$env:SystemRoot\System32\*") {
        $driveName = $WindowsPath.Substring(0, 1).ToLowerInvariant()
        return "/mnt/$driveName/$($WindowsPath.Substring(3).Replace('\', '/'))"
    }
    return $WindowsPath.Replace('\', '/')
}

function Convert-ToBashLiteral {
    param([string]$Value)

    if ($Value.Contains("'")) {
        throw "Bash fixture value contains an unsupported apostrophe: $Value"
    }
    return "'$Value'"
}

function Assert-NoGatewayOrContainerMutation {
    param([string]$Context)

    $commands = Get-FakeDockerCommands
    $unsafe = @($commands | Where-Object {
        $_ -match '(^| )mcp gateway( |$)' -or
        $_ -match '(^| )(rm|stop|kill)( |$)' -or
        $_ -match '(^| )container (rm|stop|kill)( |$)'
    })
    Assert-Equal 0 $unsafe.Count "$Context invoked an unsafe Docker lifecycle command."
}

function Reset-FakeDocker {
    param([string]$Scenario)

    foreach ($path in @($fakeStatePath, $fakeLogPath)) {
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            Remove-Item -LiteralPath $path
        }
    }
    $env:TASKDECK_FAKE_DOCKER_SCENARIO = $Scenario
    $env:TASKDECK_FAKE_DOCKER_STATE = $fakeStatePath
    $env:TASKDECK_FAKE_DOCKER_LOG = $fakeLogPath
}

function Invoke-ProfileFixture {
    param(
        [string]$Scenario,
        [string]$DefaultServers = 'time,sqlite',
        [string[]]$AdditionalArguments = @()
    )

    Reset-FakeDocker -Scenario $Scenario
    $arguments = @(
        '-NoLogo',
        '-NoProfile',
        '-NonInteractive',
        '-File',
        $profileScript,
        '-DefaultServers',
        $DefaultServers,
        '-DockerExecutable',
        $fakeDockerCommand,
        '-CiMode'
    ) + $AdditionalArguments

    $global:LASTEXITCODE = $null
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $rawOutput = @(& $powerShellExecutable @arguments 2>&1)
        $childExitCode = $global:LASTEXITCODE
        $output = @($rawOutput | ForEach-Object { [string]$_ })
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    if ($null -eq $childExitCode) {
        throw "Profile fixture did not return an exit code. Executable: $powerShellExecutable Output: $($output -join ' | ')"
    }

    return [pscustomobject]@{
        ExitCode = $childExitCode
        Output = $output -join "`n"
    }
}

function Invoke-CredentialFixture {
    Reset-FakeDocker -Scenario 'normal'
    $previousUserProfile = $env:USERPROFILE
    $env:USERPROFILE = $fakeUserProfile
    try {
        $arguments = @(
            '-NoLogo',
            '-NoProfile',
            '-NonInteractive',
            '-File',
            $credentialScript,
            '-PostmanApiKey',
            'fixture-secret-value',
            '-Verify',
            '-DockerExecutable',
            $fakeDockerCommand
        )

        $global:LASTEXITCODE = $null
        $previousErrorActionPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            $rawOutput = @(& $powerShellExecutable @arguments 2>&1)
            $childExitCode = $global:LASTEXITCODE
            $output = @($rawOutput | ForEach-Object { [string]$_ })
        }
        finally {
            $ErrorActionPreference = $previousErrorActionPreference
        }
        if ($null -eq $childExitCode) {
            throw 'Credential fixture did not return an exit code.'
        }

        return [pscustomobject]@{
            ExitCode = $childExitCode
            Output = $output -join "`n"
        }
    }
    finally {
        $env:USERPROFILE = $previousUserProfile
    }
}

function Invoke-DrillFixture {
    param(
        [ValidateSet('pass', 'fail')]
        [string]$BaselineMode
    )

    if ($null -eq $bashCommand) {
        throw 'Bash is required for the credential-drill regression.'
    }

    $drillArgument = Convert-ToBashPath -WindowsPath $drillScript
    $repoArgument = Convert-ToBashPath -WindowsPath $repoRoot
    $fixtureArgument = Convert-ToBashPath -WindowsPath $fixtureRoot
    $runnerPath = Join-Path $fixtureRoot "run-drill-$BaselineMode.sh"
    $runnerArgument = Convert-ToBashPath -WindowsPath $runnerPath
    $runnerLines = @(
        '#!/usr/bin/env bash'
        ('export PATH={0}:"$PATH"' -f (Convert-ToBashLiteral -Value $fixtureArgument))
        ('export TASKDECK_DRILL_BASELINE_MODE={0}' -f (Convert-ToBashLiteral -Value $BaselineMode))
        ('exec bash {0} {1}' -f (Convert-ToBashLiteral -Value $drillArgument), (Convert-ToBashLiteral -Value $repoArgument))
    )
    $runnerSource = ($runnerLines -join "`n") + "`n"
    [System.IO.File]::WriteAllText($runnerPath, $runnerSource, (New-Object System.Text.UTF8Encoding($false)))

    $global:LASTEXITCODE = $null
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $rawOutput = @(& $bashCommand.Source $runnerArgument 2>&1)
        $childExitCode = $global:LASTEXITCODE
        $output = @($rawOutput | ForEach-Object { [string]$_ })
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    if ($null -eq $childExitCode) {
        throw 'Credential-drill fixture did not return an exit code.'
    }

    return [pscustomobject]@{
        ExitCode = $childExitCode
        Output = $output -join "`n"
    }
}

function Complete-Test {
    param([string]$Name)
    Write-Host "PASS: $Name"
}

New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
New-Item -ItemType Directory -Path $fakeUserProfile | Out-Null

$fakeDockerSource = @'
$ErrorActionPreference = 'Stop'

$scenario = $env:TASKDECK_FAKE_DOCKER_SCENARIO
$statePath = $env:TASKDECK_FAKE_DOCKER_STATE
$logPath = $env:TASKDECK_FAKE_DOCKER_LOG
Add-Content -LiteralPath $logPath -Value ($args -join [char]31) -Encoding UTF8

$baseA = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
$baseB = 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'
$addedC = 'cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc'

if ($args.Count -ge 1 -and $args[0] -eq 'ps') {
    $callCount = 0
    if (Test-Path -LiteralPath $statePath -PathType Leaf) {
        $callCount = [int](Get-Content -LiteralPath $statePath -Raw)
    }
    $callCount++
    Set-Content -LiteralPath $statePath -Value $callCount -Encoding Ascii

    if ($scenario -eq 'after-snapshot-fails' -and $callCount -ge 2) {
        exit 17
    }

    Write-Output $baseA
    if (-not ($scenario -eq 'baseline-removed' -and $callCount -ge 2)) {
        Write-Output $baseB
    }
    if ($scenario -eq 'container-added' -and $callCount -ge 2) {
        Write-Output $addedC
    }
    exit 0
}

if ($args.Count -eq 2 -and $args[0] -eq 'mcp' -and $args[1] -eq '--help') {
    Write-Output 'Fake Docker MCP help'
    exit 0
}

if ($args.Count -ge 6 -and
    $args[0] -eq 'mcp' -and
    $args[1] -eq 'profile' -and
    $args[2] -eq 'server' -and
    $args[3] -eq 'ls') {
    $serverEntries = @()
    foreach ($name in @('time', 'SQLite', 'postman', 'dockerhub')) {
        $serverEntries += [ordered]@{
            type = 'image'
            secrets = @('raw-secret-metadata-must-not-be-printed')
            snapshot = [ordered]@{
                server = [ordered]@{
                    name = $name
                    description = 'sensitive-local-path-must-not-be-printed'
                }
            }
        }
    }
    $profile = [ordered]@{
        id = 'default'
        name = 'Default Profile'
        secrets = @('raw-profile-secret-metadata-must-not-be-printed')
        servers = $serverEntries
    }
    ConvertTo-Json -InputObject @($profile) -Depth 8 -Compress
    exit 0
}

if ($args.Count -ge 3 -and $args[0] -eq 'mcp' -and $args[1] -eq 'secret' -and $args[2] -eq 'ls') {
    if ($scenario -eq 'credential-helper') {
        Write-Output 'postman.postman-api-key | configured'
    }
    exit 0
}

if ($args.Count -ge 4 -and $args[0] -eq 'mcp' -and $args[1] -eq 'secret' -and $args[2] -eq 'set') {
    [Console]::In.ReadToEnd() | Out-Null
    exit 0
}

if ($args.Count -ge 2 -and $args[0] -eq 'mcp' -and $args[1] -eq 'gateway') {
    exit 90
}

exit 91
'@
Set-Content -LiteralPath $fakeDockerScript -Value $fakeDockerSource -Encoding UTF8

$fakeDockerCommandSource = @'
@echo off
powershell.exe -NoLogo -NoProfile -NonInteractive -File "%~dp0FakeDocker.ps1" %*
exit /b %errorlevel%
'@
Set-Content -LiteralPath $fakeDockerCommand -Value $fakeDockerCommandSource -Encoding Ascii

$fakeDrillDockerSource = @'
#!/usr/bin/env bash
if [[ "$1" == "mcp" ]] && [[ "$2" == "--help" ]]; then
    exit 0
fi
exit 91
'@
$utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($fakeDrillDockerCommand, ($fakeDrillDockerSource -replace "`r?`n", "`n"), $utf8WithoutBom)

$fakeDrillPowerShellSource = @'
#!/usr/bin/env bash
requested_servers=""
while [[ $# -gt 0 ]]; do
    if [[ "$1" == "-DefaultServers" ]] && [[ $# -ge 2 ]]; then
        requested_servers="$2"
        shift 2
        continue
    fi
    shift
done

if [[ "$requested_servers" == "time" ]]; then
    if [[ "$TASKDECK_DRILL_BASELINE_MODE" == "fail" ]]; then
        echo "MCP_PROFILE_RESULT=FAIL"
        exit 1
    fi
    echo "MCP_PROFILE_RESULT=PASS"
    exit 0
fi

if [[ "$requested_servers" == "bogus-nonexistent-server-12345" ]]; then
    echo "MCP_PROFILE_RESULT=FAIL"
    exit 1
fi

exit 92
'@
[System.IO.File]::WriteAllText($fakeDrillPowerShellCommand, ($fakeDrillPowerShellSource -replace "`r?`n", "`n"), $utf8WithoutBom)

if ($null -ne $bashCommand) {
    $fakeDrillDockerArgument = Convert-ToBashPath -WindowsPath $fakeDrillDockerCommand
    $fakeDrillPowerShellArgument = Convert-ToBashPath -WindowsPath $fakeDrillPowerShellCommand
    $fakeDrillDockerLiteral = Convert-ToBashLiteral -Value $fakeDrillDockerArgument
    $fakeDrillPowerShellLiteral = Convert-ToBashLiteral -Value $fakeDrillPowerShellArgument
    & $bashCommand.Source -c "chmod +x $fakeDrillDockerLiteral $fakeDrillPowerShellLiteral"
    if ($LASTEXITCODE -ne 0) {
        throw 'Failed to mark credential-drill fixture commands executable.'
    }
}

try {
    $normal = Invoke-ProfileFixture -Scenario 'normal'
    Assert-Equal 0 $normal.ExitCode 'Read-only validation should pass when requested servers exist and container identity is unchanged.'
    Assert-Contains $normal.Output 'MCP_PROFILE_PROBE=READ_ONLY_PROFILE' 'CI output did not identify the non-starting probe.'
    Assert-Contains $normal.Output 'MCP_PROFILE_RESULT=PASS' 'CI output did not report normal success.'
    Assert-Contains $normal.Output 'MCP_PROFILE_CONTAINERS_BEFORE_COUNT=2' 'CI output omitted the before count.'
    Assert-Contains $normal.Output 'MCP_PROFILE_CONTAINERS_AFTER_COUNT=2' 'CI output omitted the after count.'
    Assert-Contains $normal.Output 'MCP_PROFILE_CONTAINERS_ADDED=0' 'CI output omitted the zero added-container proof.'
    Assert-Contains $normal.Output 'MCP_PROFILE_CONTAINERS_REMOVED=0' 'CI output omitted the zero removed-container proof.'
    Assert-True (-not $normal.Output.Contains('raw-secret-metadata-must-not-be-printed')) 'Profile validation printed raw secret metadata.'
    Assert-True (-not $normal.Output.Contains('sensitive-local-path-must-not-be-printed')) 'Profile validation printed raw profile content.'
    Assert-NoGatewayOrContainerMutation 'Normal validation'
    $normalCommands = Get-FakeDockerCommands
    Assert-Equal 3 $normalCommands.Count 'Normal validation should perform exactly two snapshots and one profile inventory.'
    Complete-Test 'normal success is read-only and proves exact container-state neutrality'

    $missing = Invoke-ProfileFixture -Scenario 'normal' -DefaultServers 'time,bogus-server'
    Assert-True ($missing.ExitCode -ne 0) 'A missing requested server must fail even when the profile-list command exits zero.'
    Assert-Contains $missing.Output 'Required Docker MCP server(s) are absent' 'Missing-server failure was not actionable.'
    Assert-Contains $missing.Output 'MCP_PROFILE_RESULT=FAIL' 'Missing-server failure omitted the CI failure marker.'
    Assert-Contains $missing.Output 'MCP_PROFILE_CONTAINERS_ADDED=0' 'Missing-server failure did not still prove the container delta.'
    Assert-NoGatewayOrContainerMutation 'Missing-server validation'
    Complete-Test 'missing server fails from parsed inventory rather than exit status'

    $added = Invoke-ProfileFixture -Scenario 'container-added'
    Assert-True ($added.ExitCode -ne 0) 'A new Docker MCP container must withhold PASS.'
    Assert-Contains $added.Output 'MCP_PROFILE_CONTAINERS_ADDED=1' 'Added-container failure omitted the bounded delta.'
    Assert-Contains $added.Output 'No cleanup was attempted because this invocation does not own any Docker MCP containers.' 'Added-container failure did not explain the ownership boundary.'
    Assert-Contains $added.Output 'MCP_PROFILE_RESULT=FAIL' 'Added-container drift omitted the CI failure marker.'
    Assert-NoGatewayOrContainerMutation 'Added-container validation'
    Complete-Test 'new container identity fails closed without unsafe cleanup'

    $removed = Invoke-ProfileFixture -Scenario 'baseline-removed'
    Assert-True ($removed.ExitCode -ne 0) 'Removal of a baseline Docker MCP container must withhold PASS.'
    Assert-Contains $removed.Output 'MCP_PROFILE_CONTAINERS_REMOVED=1' 'Removed-container failure omitted the bounded delta.'
    Assert-NoGatewayOrContainerMutation 'Removed-container validation'
    Complete-Test 'baseline container identity changes fail closed without mutation'

    $snapshotFailure = Invoke-ProfileFixture -Scenario 'after-snapshot-fails'
    Assert-True ($snapshotFailure.ExitCode -ne 0) 'An unprovable after-state must fail closed.'
    Assert-Contains $snapshotFailure.Output 'Unable to prove Docker MCP container state after validation' 'After-snapshot failure was not explicit.'
    Assert-Contains $snapshotFailure.Output 'MCP_PROFILE_RESULT=FAIL' 'After-snapshot failure omitted the CI failure marker.'
    Assert-True (-not ($snapshotFailure.Output -match 'MCP_PROFILE_RESULT=PASS(?:\r?$|_)')) 'After-snapshot failure also emitted a PASS marker.'
    Assert-NoGatewayOrContainerMutation 'After-snapshot failure'
    Complete-Test 'after-snapshot errors fail closed'

    $optionalWarning = Invoke-ProfileFixture `
        -Scenario 'normal' `
        -AdditionalArguments @('-IncludeOptional', '-OptionalServers', 'postman', '-SkipOptionalWhenMissingPrereqs')
    Assert-Equal 0 $optionalWarning.ExitCode 'Optional missing prerequisites should remain a warning in skip mode.'
    Assert-Contains $optionalWarning.Output 'MCP_PROFILE_RESULT=PASS_WITH_WARNINGS' 'Optional warning mode lost its CI contract.'
    Assert-NoGatewayOrContainerMutation 'Optional warning validation'
    Complete-Test 'optional prerequisite warning and skip behavior remains read-only'

    $optionalStrict = Invoke-ProfileFixture `
        -Scenario 'normal' `
        -AdditionalArguments @('-IncludeOptional', '-OptionalServers', 'postman', '-FailOnOptionalErrors')
    Assert-True ($optionalStrict.ExitCode -ne 0) 'Optional strict mode must still reject missing prerequisites.'
    Assert-Contains $optionalStrict.Output 'MCP_PROFILE_RESULT=FAIL' 'Optional strict failure lost its CI contract.'
    Assert-Contains $optionalStrict.Output 'MCP_PROFILE_CONTAINERS_ADDED=0' 'Optional strict failure did not prove the container delta.'
    Assert-NoGatewayOrContainerMutation 'Optional strict validation'
    Complete-Test 'optional strict behavior remains fail-closed and read-only'

    $credential = Invoke-CredentialFixture
    Assert-Equal 0 $credential.ExitCode 'Credential helper verification should delegate successfully to the read-only validator.'
    Assert-Contains $credential.Output 'MCP_PROFILE_PROBE=READ_ONLY_PROFILE' 'Credential helper verification did not delegate to the safe probe.'
    Assert-True (-not $credential.Output.Contains('fixture-secret-value')) 'Credential helper printed the supplied secret value.'
    Assert-NoGatewayOrContainerMutation 'Credential helper verification'
    $credentialCommands = Get-FakeDockerCommands
    Assert-True (@($credentialCommands | Where-Object { $_ -eq 'mcp secret set postman.postman-api-key' }).Count -eq 1) 'Credential helper fixture did not exercise the expected secret-setting path.'
    Assert-True (@($credentialCommands | Where-Object { $_ -eq 'mcp profile server ls --format json' }).Count -eq 1) 'Credential helper verification did not use the read-only profile inventory.'
    Complete-Test 'credential helper verification delegates to the safe contract'

    $validatorSource = Get-Content -LiteralPath $profileScript -Raw
    $credentialSource = Get-Content -LiteralPath $credentialScript -Raw
    $drillSource = Get-Content -LiteralPath $drillScript -Raw
    foreach ($source in @($validatorSource, $credentialSource, $drillSource)) {
        Assert-True (-not $source.Contains('gateway run --dry-run')) 'A validation path still invokes the persistent gateway dry-run.'
    }
    Assert-Contains $drillSource 'Test-DockerMcpProfile.ps1' 'Credential drill no longer routes through the shared validator.'
    if ($null -ne $bashCommand) {
        $drillArgument = Convert-ToBashPath -WindowsPath $drillScript
        & $bashCommand.Source -n $drillArgument
        Assert-Equal 0 $LASTEXITCODE 'Credential drill failed Bash syntax validation.'

        $baselineFailure = Invoke-DrillFixture -BaselineMode 'fail'
        Assert-True ($baselineFailure.ExitCode -ne 0) 'The drill must not pass when positive baseline validation fails.'
        Assert-Contains $baselineFailure.Output 'FAIL - Read-only profile validation failed' 'The drill did not classify baseline validator failure.'
        Assert-Contains $baselineFailure.Output 'Bogus-server rejection is not attributable because baseline validation also failed' 'The drill falsely credited a negative case after baseline failure.'
        Assert-True (-not $baselineFailure.Output.Contains('[drill-mcp-invalid-credentials] PASS')) 'The failed drill emitted its terminal PASS marker.'

        $baselineSuccess = Invoke-DrillFixture -BaselineMode 'pass'
        Assert-Equal 0 $baselineSuccess.ExitCode 'The drill should pass when positive validation succeeds and the nonexistent server is rejected.'
        Assert-Contains $baselineSuccess.Output 'Read-only profile validation succeeded (expected)' 'The drill did not prove its positive baseline.'
        Assert-Contains $baselineSuccess.Output 'Bogus server was correctly rejected by read-only validation' 'The drill did not prove its negative case.'
        Assert-Contains $baselineSuccess.Output '[drill-mcp-invalid-credentials] PASS' 'The valid drill path omitted its terminal PASS marker.'
    }
    Complete-Test 'validator, credential helper, and drill contain no gateway dry-run path and the drill gates its negative case on a positive baseline'

    Write-Host 'All Docker MCP profile validation tests passed.'
}
finally {
    foreach ($name in @('TASKDECK_FAKE_DOCKER_SCENARIO', 'TASKDECK_FAKE_DOCKER_STATE', 'TASKDECK_FAKE_DOCKER_LOG')) {
        Remove-Item -Path "Env:$name" -ErrorAction SilentlyContinue
    }

    $resolvedTempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    $resolvedFixtureRoot = [System.IO.Path]::GetFullPath($fixtureRoot)
    if (-not $resolvedFixtureRoot.StartsWith($resolvedTempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove test fixture outside the temporary directory: $resolvedFixtureRoot"
    }
    if (Test-Path -LiteralPath $resolvedFixtureRoot -PathType Container) {
        Remove-Item -LiteralPath $resolvedFixtureRoot -Recurse
    }
}
