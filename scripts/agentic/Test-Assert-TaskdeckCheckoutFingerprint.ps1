[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$toolPath = Join-Path $PSScriptRoot 'Assert-TaskdeckCheckoutFingerprint.ps1'

function Resolve-PowerShellHostPath {
    # $PSHOME\powershell.exe does not exist under PowerShell 7, where the host
    # is pwsh, so relaunch through the host actually running this harness.
    $current = ''
    try {
        $current = [Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
    }
    catch {
        $current = ''
    }

    if (-not [string]::IsNullOrWhiteSpace($current)) {
        $name = [IO.Path]::GetFileNameWithoutExtension($current)
        if (($name -eq 'pwsh' -or $name -eq 'powershell') -and (Test-Path -LiteralPath $current -PathType Leaf)) {
            return $current
        }
    }

    foreach ($candidate in @('pwsh.exe', 'powershell.exe', 'pwsh', 'powershell')) {
        $path = Join-Path $PSHOME $candidate
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            return $path
        }
    }

    throw 'could not resolve a PowerShell host to relaunch the guard under'
}

$powerShellPath = Resolve-PowerShellHostPath
$script:passed = 0
$script:failed = 0

function Assert-True {
    param([bool]$Condition, [string]$Message)

    if (-not $Condition) {
        throw $Message
    }
}

function Quote-Argument {
    param([string]$Value)

    if ($Value.Contains('"')) {
        throw 'test argument contains an unsupported quote'
    }

    return '"' + $Value + '"'
}

function Invoke-FingerprintTool {
    param(
        [string[]]$Arguments,
        [string]$Tool = $script:toolPath
    )

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $powerShellPath
    $startInfo.Arguments = '-NoProfile -ExecutionPolicy Bypass -File ' + (Quote-Argument $Tool) + ' ' + (($Arguments | ForEach-Object { Quote-Argument $_ }) -join ' ')
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    [void]$process.Start()
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    $result = [pscustomobject]@{
        exitCode = $process.ExitCode
        stdout = $stdout
        stderr = $stderr
    }
    $process.Dispose()
    return $result
}

function Invoke-Git {
    param([string]$Repo, [string[]]$Arguments)

    & git -C $Repo @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw 'test git command failed'
    }
}

function New-TestRepository {
    param([scriptblock]$Body)

    $repo = Join-Path ([IO.Path]::GetTempPath()) ('taskdeck-fingerprint-test-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $repo -ErrorAction Stop | Out-Null
    try {
        Invoke-Git -Repo $repo -Arguments @('init', '-q')
        Invoke-Git -Repo $repo -Arguments @('config', 'user.email', 'fingerprint-test@example.invalid')
        Invoke-Git -Repo $repo -Arguments @('config', 'user.name', 'Fingerprint test')
        [IO.File]::WriteAllText((Join-Path $repo 'tracked.txt'), 'tracked')
        Invoke-Git -Repo $repo -Arguments @('add', 'tracked.txt')
        Invoke-Git -Repo $repo -Arguments @('commit', '-q', '-m', 'initial test fixture')
        & $Body $repo
    }
    finally {
        Remove-Item -LiteralPath $repo -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Capture-State {
    param(
        [string]$Repo,
        [string]$Token = 'test-token',
        [string[]]$Limits = @()
    )

    $result = Invoke-FingerprintTool -Arguments (@('-Mode', 'Capture', '-CheckoutPath', $Repo, '-Token', $Token) + $Limits)
    Assert-True ($result.exitCode -eq 0) ('capture failed: ' + $result.stderr)
    Assert-True (-not $result.stdout.Contains($Token)) 'capture stdout exposed the caller token'
    Assert-True ($result.stdout -notmatch '[0-9a-f]{64}') 'capture stdout exposed a digest'
    $records = @($result.stdout.Trim() | ConvertFrom-Json)
    Assert-True ($records.Count -eq 1 -and $records[0].classification -eq 'captured') 'capture did not emit one captured record'
    return $records[0].path
}

function Cleanup-State {
    param([string]$Repo, [string]$State, [string]$Token = 'test-token')

    if (Test-Path -LiteralPath $State) {
        $result = Invoke-FingerprintTool -Arguments @('-Mode', 'Cleanup', '-CheckoutPath', $Repo, '-Token', $Token, '-StatePath', $State)
        Assert-True ($result.exitCode -eq 0) ('cleanup failed: ' + $result.stderr)
        Assert-True (-not (Test-Path -LiteralPath $State)) 'cleanup left the authenticated state file behind'
    }
}

function Compare-State {
    param([string]$Repo, [string]$State, [string]$Token = 'test-token')

    return Invoke-FingerprintTool -Arguments @('-Mode', 'Compare', '-CheckoutPath', $Repo, '-Token', $Token, '-StatePath', $State)
}

function Run-Test {
    param([string]$Name, [scriptblock]$Body)

    try {
        & $Body
        $script:passed++
        Write-Output ('PASS ' + $Name)
    }
    catch {
        $script:failed++
        [Console]::Error.WriteLine('FAIL ' + $Name + ': ' + $_.Exception.Message)
    }
}

Run-Test 'detects a same-status overwrite by length and SHA-256' {
    New-TestRepository {
        param($repo)
        $artifact = Join-Path $repo 'artifact.txt'
        [IO.File]::WriteAllText($artifact, 'aaaa')
        $beforeStatus = (& git -C $repo status --porcelain).Trim()
        $state = Capture-State -Repo $repo
        try {
            [IO.File]::WriteAllText($artifact, 'bbbb')
            $afterStatus = (& git -C $repo status --porcelain).Trim()
            Assert-True ($beforeStatus -eq $afterStatus) 'fixture status changed instead of remaining untracked at the same path'
            $result = Compare-State -Repo $repo -State $state
            Assert-True ($result.exitCode -eq 2) 'same-path overwrite did not fail comparison'
            Assert-True (($result.stdout | ConvertFrom-Json).classification -contains 'overwritten') 'overwrite classification was not emitted'
        }
        finally { Cleanup-State -Repo $repo -State $state }
    }
}

Run-Test 'accepts identical bytes at the same status path' {
    New-TestRepository {
        param($repo)
        [IO.File]::WriteAllText((Join-Path $repo 'artifact.txt'), 'same bytes')
        $state = Capture-State -Repo $repo
        try {
            $result = Compare-State -Repo $repo -State $state
            Assert-True ($result.exitCode -eq 0) ('unchanged bytes failed comparison: ' + $result.stderr)
            Assert-True (($result.stdout | ConvertFrom-Json).classification -eq 'unchanged') 'unchanged classification was not emitted'
        }
        finally { Cleanup-State -Repo $repo -State $state }
    }
}

Run-Test 'supports the documented in-process capture assignment' {
    New-TestRepository {
        param($repo)
        $token = 'in-process-capture-token'
        $state = $null
        try {
            $capture = & $toolPath -Mode Capture -CheckoutPath $repo -Token $token
            $captureExit = $LASTEXITCODE
            Assert-True ($captureExit -eq 0) 'in-process capture failed'
            Assert-True (@($capture).Count -eq 1) 'capture record bypassed the PowerShell success pipeline'
            $record = ([string]$capture | ConvertFrom-Json)
            Assert-True ($record.classification -eq 'captured') 'in-process capture emitted the wrong classification'
            $state = $record.path
            Assert-True (-not [string]::IsNullOrWhiteSpace($state) -and (Test-Path -LiteralPath $state)) 'in-process capture did not return its state path'
        }
        finally {
            if (-not [string]::IsNullOrWhiteSpace($state)) {
                Cleanup-State -Repo $repo -State $state -Token $token
            }
        }
    }
}

Run-Test 'detects deletion of a captured status artifact' {
    New-TestRepository {
        param($repo)
        $artifact = Join-Path $repo 'artifact.txt'
        [IO.File]::WriteAllText($artifact, 'delete me')
        $state = Capture-State -Repo $repo
        try {
            Remove-Item -LiteralPath $artifact -Force
            $result = Compare-State -Repo $repo -State $state
            Assert-True ($result.exitCode -eq 2) 'deletion did not fail comparison'
            Assert-True (($result.stdout | ConvertFrom-Json).classification -contains 'deleted') 'deletion classification was not emitted'
        }
        finally { Cleanup-State -Repo $repo -State $state }
    }
}

Run-Test 'detects a tracked status artifact deleted after capture' {
    New-TestRepository {
        param($repo)
        $artifact = Join-Path $repo 'tracked.txt'
        [IO.File]::WriteAllText($artifact, 'changed before capture')
        $state = Capture-State -Repo $repo
        try {
            Remove-Item -LiteralPath $artifact -Force
            $result = Compare-State -Repo $repo -State $state
            Assert-True ($result.exitCode -eq 2) 'tracked deletion did not fail comparison'
            Assert-True (($result.stdout | ConvertFrom-Json).classification -contains 'deleted') 'tracked deletion classification was not emitted'
        }
        finally { Cleanup-State -Repo $repo -State $state }
    }
}

Run-Test 'detects creation after capture' {
    New-TestRepository {
        param($repo)
        $state = Capture-State -Repo $repo
        try {
            [IO.File]::WriteAllText((Join-Path $repo 'new-artifact.txt'), 'created')
            $result = Compare-State -Repo $repo -State $state
            Assert-True ($result.exitCode -eq 2) 'creation did not fail comparison'
            Assert-True (($result.stdout | ConvertFrom-Json).classification -contains 'created') 'creation classification was not emitted'
        }
        finally { Cleanup-State -Repo $repo -State $state }
    }
}

Run-Test 'excludes ignored artifacts' {
    New-TestRepository {
        param($repo)
        [IO.File]::WriteAllText((Join-Path $repo '.gitignore'), 'ignored.txt')
        Invoke-Git -Repo $repo -Arguments @('add', '.gitignore')
        Invoke-Git -Repo $repo -Arguments @('commit', '-q', '-m', 'add ignored fixture')
        [IO.File]::WriteAllText((Join-Path $repo 'ignored.txt'), 'first')
        $state = Capture-State -Repo $repo
        try {
            [IO.File]::WriteAllText((Join-Path $repo 'ignored.txt'), 'second')
            $result = Compare-State -Repo $repo -State $state
            Assert-True ($result.exitCode -eq 0) 'ignored artifact affected comparison'
        }
        finally { Cleanup-State -Repo $repo -State $state }
    }
}

Run-Test 'does not count clean tracked files against the default status-artifact bound' {
    New-TestRepository {
        param($repo)
        for ($index = 0; $index -le 512; $index++) {
            [IO.File]::WriteAllText((Join-Path $repo ('clean-' + $index + '.txt')), 'clean')
        }
        Invoke-Git -Repo $repo -Arguments @('add', '.')
        Invoke-Git -Repo $repo -Arguments @('commit', '-q', '-m', 'add clean scale fixture')
        $state = Capture-State -Repo $repo
        try {
            $capture = Get-Content -LiteralPath $state -Raw | ConvertFrom-Json
            Assert-True (($capture.payload | ConvertFrom-Json).files.Count -eq 0) 'clean tracked files entered the fingerprint payload'
        }
        finally { Cleanup-State -Repo $repo -State $state }
    }
}

Run-Test 'fails closed when a status artifact exceeds a configured bound' {
    New-TestRepository {
        param($repo)
        [IO.File]::WriteAllBytes((Join-Path $repo 'large.bin'), [byte[]](0..255 + 0..255 + 0..255 + 0..255 + 0))
        $result = Invoke-FingerprintTool -Arguments @('-Mode', 'Capture', '-CheckoutPath', $repo, '-Token', 'test-token', '-MaxBytesPerFile', '1024')
        Assert-True ($result.exitCode -ne 0) 'bounded capture unexpectedly succeeded'
        Assert-True ([string]::IsNullOrWhiteSpace($result.stdout)) 'failed capture emitted an unsafe partial record'
    }
}

Run-Test 'checks metadata byte limits before invoking the file hasher' {
    New-TestRepository {
        param($repo)
        [IO.File]::WriteAllBytes((Join-Path $repo 'too-large.bin'), (New-Object byte[] 2048))
        $message = & {
            . $toolPath
            function Get-ArtifactFingerprint { throw 'file hasher was invoked before the metadata limit' }
            try {
                [void](Get-Inventory -Checkout $repo -FileLimit 512 -PerFileLimit 1024 -TotalLimit 4096)
                return 'capture unexpectedly succeeded'
            }
            catch {
                return $_.Exception.Message
            }
        }
        Assert-True ($message -eq 'status artifact exceeds the per-file byte limit') ('unexpected bound ordering: ' + $message)
    }
}

Run-Test 'bounds git status output before full materialization' {
    New-TestRepository {
        param($repo)
        for ($index = 0; $index -lt 40; $index++) {
            [IO.File]::WriteAllText((Join-Path $repo (('status-output-' + $index + '-').PadRight(60, 'x') + '.txt')), 'x')
        }
        $result = Invoke-FingerprintTool -Arguments @('-Mode', 'Capture', '-CheckoutPath', $repo, '-Token', 'test-token', '-MaxGitOutputBytes', '1024')
        Assert-True ($result.exitCode -ne 0) 'oversized Git status output was accepted'
        Assert-True ($result.stderr -match 'git output exceeds the configured byte limit') 'Git output bound did not fail with the expected classification'
    }
}

Run-Test 'requires both batch skill wrappers to propagate lane failure after cleanup' {
    $skills = @(
        (Join-Path $PSScriptRoot '..\..\.codex\skills\taskdeck-issue-batch-orchestrator\SKILL.md'),
        (Join-Path $PSScriptRoot '..\..\.claude\skills\taskdeck-issue-batch-orchestrator\SKILL.md')
    )
    foreach ($skill in $skills) {
        $text = Get-Content -LiteralPath $skill -Raw
        $errorSlot = $text.IndexOf('$laneError = $null', [StringComparison]::Ordinal)
        $lane = $text.IndexOf('& $laneCommand', $errorSlot, [StringComparison]::Ordinal)
        $saved = $text.IndexOf('$laneSucceeded = $?', [StringComparison]::Ordinal)
        $caught = $text.IndexOf('$laneError = $_', $lane, [StringComparison]::Ordinal)
        $compare = $text.IndexOf('-Mode Compare', $caught, [StringComparison]::Ordinal)
        $cleanup = $text.IndexOf('-Mode Cleanup', [StringComparison]::Ordinal)
        $rethrown = $text.IndexOf('throw $laneError', $cleanup, [StringComparison]::Ordinal)
        $propagated = $text.IndexOf('if (-not $laneSucceeded', [StringComparison]::Ordinal)
        Assert-True (
            $errorSlot -ge 0 -and $lane -gt $errorSlot -and $saved -gt $lane -and $caught -gt $saved -and
            $compare -gt $caught -and $cleanup -gt $compare -and $rethrown -gt $cleanup -and $propagated -gt $rethrown
        ) ('lane failure gate is missing or misordered in ' + $skill)
    }
}

Run-Test 'anchors the guard path before a lane changes location' {
    $skills = @(
        (Join-Path $PSScriptRoot '..\..\.codex\skills\taskdeck-issue-batch-orchestrator\SKILL.md'),
        (Join-Path $PSScriptRoot '..\..\.claude\skills\taskdeck-issue-batch-orchestrator\SKILL.md')
    )
    foreach ($skill in $skills) {
        $text = Get-Content -LiteralPath $skill -Raw
        $path = $text.IndexOf('$fingerprintTool = [IO.Path]::GetFullPath((Join-Path -Path $checkout', [StringComparison]::Ordinal)
        $validation = $text.IndexOf('Test-Path -LiteralPath $fingerprintTool -PathType Leaf', $path, [StringComparison]::Ordinal)
        $capture = $text.IndexOf('& $fingerprintTool -Mode Capture', $validation, [StringComparison]::Ordinal)
        $lane = $text.IndexOf('& $laneCommand', $capture, [StringComparison]::Ordinal)
        $compare = $text.IndexOf('& $fingerprintTool -Mode Compare', $lane, [StringComparison]::Ordinal)
        $cleanup = $text.IndexOf('& $fingerprintTool -Mode Cleanup', $compare, [StringComparison]::Ordinal)
        Assert-True ($path -ge 0 -and $validation -gt $path -and $capture -gt $validation -and $lane -gt $capture -and $compare -gt $lane -and $cleanup -gt $compare) ('guard path is not resolved before Capture and reused after the lane in ' + $skill)
        Assert-True ($text -notmatch '& scripts/agentic/Assert-TaskdeckCheckoutFingerprint\.ps1') ('relative guard invocation remains in ' + $skill)
    }

    New-TestRepository {
        param($repo)
        $artifact = Join-Path $repo 'artifact.txt'
        [IO.File]::WriteAllText($artifact, 'before lane')
        $state = Capture-State -Repo $repo
        try {
            $fingerprintTool = [IO.Path]::GetFullPath($toolPath)
            Push-Location ([IO.Path]::GetTempPath())
            try {
                [IO.File]::WriteAllText($artifact, 'after lane changes location')
                $result = Invoke-FingerprintTool -Arguments @('-Mode', 'Compare', '-CheckoutPath', $repo, '-Token', 'test-token', '-StatePath', $state) -Tool $fingerprintTool
            }
            finally { Pop-Location }
            Assert-True ($result.exitCode -eq 2) 'location-changing lane mutation was not detected through the anchored guard path'
            $records = @($result.stdout.Trim() | ConvertFrom-Json)
            Assert-True ($records.Count -eq 1 -and $records[0].classification -eq 'overwritten') 'location-changing lane mutation emitted the wrong classification'
        }
        finally { Cleanup-State -Repo $repo -State $state }
    }
}

Run-Test 'uses a direct canonical temp-root state file and rejects nested state paths' {
    New-TestRepository {
        param($repo)
        $state = Capture-State -Repo $repo
        try {
            $expectedParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
            Assert-True ([IO.Path]::GetDirectoryName($state) -eq $expectedParent) 'capture state was not a direct child of the OS temp root'
            $nestedDirectory = Join-Path $expectedParent ('taskdeck-fingerprint-nested-' + [Guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $nestedDirectory | Out-Null
            try {
                $nestedState = Join-Path $nestedDirectory 'state.json'
                [IO.File]::WriteAllText($nestedState, '{}')
                $result = Invoke-FingerprintTool -Arguments @('-Mode', 'Compare', '-CheckoutPath', $repo, '-Token', 'test-token', '-StatePath', $nestedState)
                Assert-True ($result.exitCode -ne 0) 'nested state path was accepted'
            }
            finally { Remove-Item -LiteralPath $nestedDirectory -Recurse -Force }
        }
        finally { Cleanup-State -Repo $repo -State $state }
    }
}

Run-Test 'rejects HMAC tampering without deleting state' {
    New-TestRepository {
        param($repo)
        $state = Capture-State -Repo $repo
        try {
            $tampered = (Get-Content -LiteralPath $state -Raw).Replace('"version":1', '"version":2')
            [IO.File]::WriteAllText($state, $tampered, [Text.Encoding]::UTF8)
            $result = Compare-State -Repo $repo -State $state
            Assert-True ($result.exitCode -ne 0) 'tampered state was accepted'
            Assert-True (Test-Path -LiteralPath $state) 'comparison removed a tampered state file'
        }
        finally { Remove-Item -LiteralPath $state -Force -ErrorAction SilentlyContinue }
    }
}

Run-Test 'refuses cleanup without and with a wrong token' {
    New-TestRepository {
        param($repo)
        $state = Capture-State -Repo $repo
        try {
            $missing = Invoke-FingerprintTool -Arguments @('-Mode', 'Cleanup', '-CheckoutPath', $repo, '-StatePath', $state)
            Assert-True ($missing.exitCode -ne 0 -and (Test-Path -LiteralPath $state)) 'cleanup accepted a missing token'
            $wrong = Invoke-FingerprintTool -Arguments @('-Mode', 'Cleanup', '-CheckoutPath', $repo, '-Token', 'wrong-token', '-StatePath', $state)
            Assert-True ($wrong.exitCode -ne 0 -and (Test-Path -LiteralPath $state)) 'cleanup accepted a wrong token'
        }
        finally { Cleanup-State -Repo $repo -State $state }
    }
}

Run-Test 'emits one JSON-safe record for newline and pipe paths without secrets or digests' {
    $record = & {
        . $toolPath
        ConvertTo-FingerprintRecord -Path ("line`npipe|path") -Classification 'created' -Count 1
    }
    Assert-True (@($record).Count -eq 1) 'record writer emitted more than one line'
    Assert-True (-not ([string]$record).Contains("`n")) 'record writer emitted a literal newline'
    $parsed = ([string]$record | ConvertFrom-Json)
    Assert-True ($parsed.path -eq "line`npipe|path" -and $parsed.classification -eq 'created' -and $parsed.count -eq 1) 'record JSON did not round-trip the path safely'
    Assert-True (([string]$record -notmatch '[0-9a-f]{64}') -and -not ([string]$record).Contains('test-token')) 'record writer exposed a digest or token'
}

function Get-FencedPowerShellBlock {
    param([string]$Text)

    $fence = $Text.IndexOf('```powershell', [StringComparison]::Ordinal)
    Assert-True ($fence -ge 0) 'the skill has no fenced PowerShell recipe'
    $start = $Text.IndexOf("`n", $fence) + 1
    $end = $Text.IndexOf('```', $start, [StringComparison]::Ordinal)
    Assert-True ($end -gt $start) 'the fenced PowerShell recipe is unterminated'
    return $Text.Substring($start, $end - $start)
}

function Get-MatchingBraceIndex {
    param([string]$Text, [int]$OpenIndex)

    Assert-True ($Text[$OpenIndex] -eq '{') 'brace scan did not start on an opening brace'
    $depth = 0
    for ($index = $OpenIndex; $index -lt $Text.Length; $index++) {
        if ($Text[$index] -eq '{') { $depth++ }
        elseif ($Text[$index] -eq '}') {
            $depth--
            if ($depth -eq 0) { return $index }
        }
    }

    throw 'the recipe block has unbalanced braces'
}

function Get-BatchSkillPath {
    return @(
        (Join-Path $PSScriptRoot '..\..\.codex\skills\taskdeck-issue-batch-orchestrator\SKILL.md'),
        (Join-Path $PSScriptRoot '..\..\.claude\skills\taskdeck-issue-batch-orchestrator\SKILL.md')
    )
}

Run-Test 'keeps guard finalization inside a finally no lane exit can skip' {
    foreach ($skill in (Get-BatchSkillPath)) {
        $recipe = Get-FencedPowerShellBlock -Text (Get-Content -LiteralPath $skill -Raw)
        $lane = $recipe.IndexOf('& $laneCommand', [StringComparison]::Ordinal)
        Assert-True ($lane -ge 0) ('the lane invocation is missing from ' + $skill)

        $finallyKeyword = $recipe.IndexOf('finally {', $lane, [StringComparison]::Ordinal)
        Assert-True ($finallyKeyword -gt $lane) ('guard finalization is not in a finally block in ' + $skill)
        $open = $recipe.IndexOf('{', $finallyKeyword)
        $close = Get-MatchingBraceIndex -Text $recipe -OpenIndex $open

        $compare = $recipe.IndexOf('& $fingerprintTool -Mode Compare', [StringComparison]::Ordinal)
        $cleanup = $recipe.IndexOf('& $fingerprintTool -Mode Cleanup', [StringComparison]::Ordinal)
        Assert-True ($compare -gt $open -and $compare -lt $close) ('Compare is outside the finally block in ' + $skill)
        Assert-True ($cleanup -gt $compare -and $cleanup -lt $close) ('Cleanup is outside the finally block in ' + $skill)

        # A statement-position exit between the lane and the finally would unwind
        # past guard finalization exactly like the lane exit this shape defends.
        $between = $recipe.Substring($lane, $finallyKeyword - $lane)
        Assert-True (-not ($between -cmatch '(?m)^\s*(exit|return|break|continue)\b')) ('an unwinding statement precedes guard finalization in ' + $skill)

        Assert-True ($recipe.IndexOf('$global:LASTEXITCODE = 255', [StringComparison]::Ordinal) -ge 0) ('the fail-closed guard exit sentinel is missing from ' + $skill)

        # IndexOf returns -1 when the text is absent, and -1 is less than any
        # $close, so a bare `-lt $close` assertion passes whether or not the
        # disposition exists. Both bounds are required for it to discriminate.
        $disposition = $recipe.IndexOf('if ($guardExit -ne 0) {', $open, [StringComparison]::Ordinal)
        Assert-True ($disposition -gt $cleanup -and $disposition -lt $close) ('the guard disposition does not supersede the lane exit in ' + $skill)
        $guardExitStatement = $recipe.IndexOf('exit $guardExit', $disposition, [StringComparison]::Ordinal)
        Assert-True ($guardExitStatement -gt $disposition -and $guardExitStatement -lt $close) ('the guard disposition never exits with the guard code in ' + $skill)

        # That `exit` unwinds past `throw $laneError`, so the recipe has to
        # surface the lane exception before the frame is discarded.
        $preserved = $recipe.IndexOf('$laneError.Exception.Message', $disposition, [StringComparison]::Ordinal)
        Assert-True ($preserved -gt $disposition -and $preserved -lt $guardExitStatement) ('the guard disposition discards the lane error text in ' + $skill)
    }
}

Run-Test 'dispatches the guard through structured exit-code propagation' {
    $text = Get-Content -LiteralPath $toolPath -Raw
    $entry = $text.IndexOf("if (`$MyInvocation.InvocationName -ne '.')", [StringComparison]::Ordinal)
    Assert-True ($entry -ge 0) 'the guard entrypoint is missing'
    $tryIndex = $text.IndexOf('try {', $entry, [StringComparison]::Ordinal)
    $catchEnd = $text.IndexOf('$exitCode = 1', $text.IndexOf('catch {', $tryIndex, [StringComparison]::Ordinal), [StringComparison]::Ordinal)
    $body = $text.Substring($tryIndex, $catchEnd - $tryIndex)
    Assert-True (-not ($body -cmatch '(?m)^\s*exit\b') -and -not $body.Contains('; exit ')) 'the guard dispatch still exits mid-flight instead of assigning a code'
    Assert-True ($text.IndexOf('exit $exitCode', $catchEnd, [StringComparison]::Ordinal) -gt $catchEnd) 'the guard has no single structured exit after its try/catch'
}

$script:wrapperSource = @'
[CmdletBinding()]
param(
    [string]$Repo,
    [string]$Tool,
    [string]$Token,
    # Deliberately not named $LaneExit: PowerShell variable names are
    # case-insensitive, so it would be clobbered by the recipe's own $laneExit.
    [int]$RequestedLaneExitCode,
    [string]$MutatePath = '',
    [string]$ThrowMessage = ''
)

# This mirrors the documented coordinator recipe. The static test above pins the
# recipe's shape; this executable copy proves the shape actually survives a lane
# that terminates the session with `exit`.
$global:LASTEXITCODE = 255
$capture = & $Tool -Mode Capture -CheckoutPath $Repo -Token $Token
$captureExit = $LASTEXITCODE
if ($captureExit -ne 0) { exit $captureExit }
$inventoryState = ([string]$capture | ConvertFrom-Json).path
[Console]::Out.WriteLine('STATE ' + $inventoryState)

$laneCommand = {
    if (-not [string]::IsNullOrEmpty($MutatePath)) {
        [IO.File]::WriteAllText($MutatePath, 'the lane overwrote this artifact')
    }
    [Console]::Out.WriteLine('LANE-RAN')
    if (-not [string]::IsNullOrEmpty($ThrowMessage)) { throw $ThrowMessage }
    exit $RequestedLaneExitCode
}

$laneSucceeded = $false
$laneExit = $null
$laneError = $null
$guardExit = 0
try {
    & $laneCommand
    $laneSucceeded = $?
    $laneExit = $LASTEXITCODE
}
catch {
    $laneError = $_
}
finally {
    [Console]::Out.WriteLine('FINALLY-RAN')
    $global:LASTEXITCODE = 255
    & $Tool -Mode Compare -CheckoutPath $Repo -Token $Token -StatePath $inventoryState
    $compareExit = $LASTEXITCODE
    if ($compareExit -ne 0) {
        $guardExit = $compareExit
    }
    else {
        $global:LASTEXITCODE = 255
        & $Tool -Mode Cleanup -CheckoutPath $Repo -Token $Token -StatePath $inventoryState
        $cleanupExit = $LASTEXITCODE
        if ($cleanupExit -ne 0) { $guardExit = $cleanupExit }
    }
    if ($guardExit -ne 0) {
        if ($null -ne $laneError) {
            [Console]::Error.WriteLine('Lane error superseded by guard disposition: ' + $laneError.Exception.Message)
        }
        exit $guardExit
    }
}

[Console]::Out.WriteLine('AFTER-TRY-RAN')
if ($null -ne $laneError) { throw $laneError }
if (-not $laneSucceeded) {
    if ($null -ne $laneExit -and $laneExit -ne 0) { exit $laneExit }
    exit 1
}
'@

function Invoke-ExitingLaneWrapper {
    param(
        [string]$Repo,
        [string]$Token,
        [int]$LaneExit,
        [string]$MutatePath = '',
        [string]$ThrowMessage = ''
    )

    $wrapper = Join-Path ([IO.Path]::GetTempPath()) ('taskdeck-fingerprint-wrapper-' + [Guid]::NewGuid().ToString('N') + '.ps1')
    [IO.File]::WriteAllText($wrapper, $script:wrapperSource, (New-Object System.Text.UTF8Encoding($false)))
    try {
        $arguments = @('-Repo', $Repo, '-Tool', $toolPath, '-Token', $Token, '-RequestedLaneExitCode', [string]$LaneExit)
        if (-not [string]::IsNullOrEmpty($MutatePath)) {
            $arguments += @('-MutatePath', $MutatePath)
        }
        if (-not [string]::IsNullOrEmpty($ThrowMessage)) {
            $arguments += @('-ThrowMessage', $ThrowMessage)
        }

        $result = Invoke-FingerprintTool -Arguments $arguments -Tool $wrapper
        $state = ''
        foreach ($line in ($result.stdout -split "`r?`n")) {
            if ($line.StartsWith('STATE ', [StringComparison]::Ordinal)) { $state = $line.Substring(6).Trim() }
        }

        return [pscustomobject]@{
            exitCode = $result.exitCode
            stdout = $result.stdout
            stderr = $result.stderr
            state = $state
        }
    }
    finally {
        Remove-Item -LiteralPath $wrapper -Force -ErrorAction SilentlyContinue
    }
}

Run-Test 'compares and preserves state when an exiting lane mutated the checkout' {
    New-TestRepository {
        param($repo)
        $token = 'exiting-lane-token'
        $artifact = Join-Path $repo 'artifact.txt'
        [IO.File]::WriteAllText($artifact, 'before the lane')
        $run = Invoke-ExitingLaneWrapper -Repo $repo -Token $token -LaneExit 0 -MutatePath $artifact
        try {
            Assert-True ($run.stdout.Contains('LANE-RAN')) ('the lane never ran: ' + $run.stderr)
            # The lane's own `exit 0` proves the unwind really happened: nothing
            # after the try/catch executed, yet the finally still did.
            Assert-True (-not $run.stdout.Contains('AFTER-TRY-RAN')) 'the lane exit did not unwind, so this is not the early-exit path'
            Assert-True ($run.stdout.Contains('FINALLY-RAN')) 'guard finalization was skipped by the lane exit'
            Assert-True ($run.stdout.Contains('"classification":"overwritten"')) ('Compare did not run or did not detect the mutation: ' + $run.stdout)
            Assert-True ($run.exitCode -eq 2) ('the lane exit code masked the guard disposition: ' + $run.exitCode)
            Assert-True (-not [string]::IsNullOrWhiteSpace($run.state)) 'the wrapper did not report its state path'
            Assert-True (Test-Path -LiteralPath $run.state) 'a failed comparison did not preserve its authenticated state'
        }
        finally {
            if (-not [string]::IsNullOrWhiteSpace($run.state)) {
                Cleanup-State -Repo $repo -State $run.state -Token $token
            }
        }
    }
}

Run-Test 'compares and cleans up when an exiting lane left the checkout alone' {
    New-TestRepository {
        param($repo)
        $token = 'clean-exiting-lane-token'
        [IO.File]::WriteAllText((Join-Path $repo 'artifact.txt'), 'untouched by the lane')
        $run = Invoke-ExitingLaneWrapper -Repo $repo -Token $token -LaneExit 0
        try {
            Assert-True ($run.stdout.Contains('LANE-RAN')) ('the lane never ran: ' + $run.stderr)
            Assert-True (-not $run.stdout.Contains('AFTER-TRY-RAN')) 'the lane exit did not unwind, so this is not the early-exit path'
            Assert-True ($run.stdout.Contains('"classification":"unchanged"')) ('Compare was skipped by the lane exit: ' + $run.stdout)
            Assert-True ($run.stdout.Contains('"classification":"cleaned"')) ('Cleanup was skipped by the lane exit: ' + $run.stdout)
            Assert-True (-not [string]::IsNullOrWhiteSpace($run.state)) 'the wrapper did not report its state path'
            Assert-True (-not (Test-Path -LiteralPath $run.state)) 'Cleanup left the authenticated state behind on the exit path'
            Assert-True ($run.exitCode -eq 0) ('a clean guarded exit lane did not preserve its own exit code: ' + $run.exitCode)
        }
        finally {
            if (-not [string]::IsNullOrWhiteSpace($run.state)) {
                Cleanup-State -Repo $repo -State $run.state -Token $token
            }
        }
    }
}

Run-Test 'lets a nonzero exiting lane keep its code once the guard is clean' {
    New-TestRepository {
        param($repo)
        $token = 'failing-exiting-lane-token'
        $run = Invoke-ExitingLaneWrapper -Repo $repo -Token $token -LaneExit 7
        try {
            Assert-True ($run.stdout.Contains('FINALLY-RAN')) 'guard finalization was skipped by the failing lane exit'
            Assert-True ($run.stdout.Contains('"classification":"cleaned"')) ('Cleanup was skipped by the failing lane exit: ' + $run.stdout)
            Assert-True ($run.exitCode -eq 7) ('the failing lane exit code was lost: ' + $run.exitCode)
        }
        finally {
            if (-not [string]::IsNullOrWhiteSpace($run.state)) {
                Cleanup-State -Repo $repo -State $run.state -Token $token
            }
        }
    }
}

Run-Test 'detects a clean-to-clean branch switch between capture and compare' {
    New-TestRepository {
        param($repo)
        $beforeStatus = (& git -C $repo status --porcelain)
        $state = Capture-State -Repo $repo
        try {
            Invoke-Git -Repo $repo -Arguments @('switch', '-q', '-c', 'guard-switch-fixture')
            $afterStatus = (& git -C $repo status --porcelain)
            Assert-True ([string]::IsNullOrWhiteSpace($beforeStatus) -and [string]::IsNullOrWhiteSpace($afterStatus)) 'the switch fixture was not clean on both sides'
            $result = Compare-State -Repo $repo -State $state
            Assert-True ($result.exitCode -eq 2) ('a clean-to-clean branch switch was reported as unchanged: ' + $result.stdout + $result.stderr)
            $records = @($result.stdout.Trim() -split "`r?`n" | ForEach-Object { $_ | ConvertFrom-Json })
            Assert-True (@($records | Where-Object { $_.classification -eq 'ref-moved' }).Count -eq 1) ('the moved symbolic ref was not reported: ' + $result.stdout)
        }
        finally { Cleanup-State -Repo $repo -State $state }
    }
}

Run-Test 'detects a clean-to-clean commit between capture and compare' {
    New-TestRepository {
        param($repo)
        $state = Capture-State -Repo $repo
        try {
            Invoke-Git -Repo $repo -Arguments @('commit', '-q', '--allow-empty', '-m', 'lane advanced the checkout')
            Assert-True ([string]::IsNullOrWhiteSpace((& git -C $repo status --porcelain))) 'the commit fixture left status artifacts behind'
            $result = Compare-State -Repo $repo -State $state
            Assert-True ($result.exitCode -eq 2) ('a clean-to-clean commit was reported as unchanged: ' + $result.stdout + $result.stderr)
            $records = @($result.stdout.Trim() -split "`r?`n" | ForEach-Object { $_ | ConvertFrom-Json })
            Assert-True (@($records | Where-Object { $_.classification -eq 'head-moved' }).Count -eq 1) ('the moved HEAD commit was not reported: ' + $result.stdout)
            Assert-True (@($records | Where-Object { $_.classification -eq 'ref-moved' }).Count -eq 0) ('a commit on the same branch reported a moved ref: ' + $result.stdout)
        }
        finally { Cleanup-State -Repo $repo -State $state }
    }
}

Run-Test 'classifies an overwrite of a clean tracked file as overwritten, not created' {
    New-TestRepository {
        param($repo)
        $state = Capture-State -Repo $repo
        try {
            # tracked.txt is committed and clean, so it is deliberately absent
            # from the baseline; absence must not be read as "did not exist".
            [IO.File]::WriteAllText((Join-Path $repo 'tracked.txt'), 'the lane overwrote clean tracked content')
            $result = Compare-State -Repo $repo -State $state
            Assert-True ($result.exitCode -eq 2) 'a clean tracked overwrite did not fail comparison'
            $records = @($result.stdout.Trim() -split "`r?`n" | ForEach-Object { $_ | ConvertFrom-Json })
            $tracked = @($records | Where-Object { $_.path -eq 'tracked.txt' })
            Assert-True ($tracked.Count -eq 1) ('the clean tracked overwrite was not reported: ' + $result.stdout)
            Assert-True ($tracked[0].classification -eq 'overwritten') ('a clean tracked overwrite was misclassified as ' + $tracked[0].classification)
        }
        finally { Cleanup-State -Repo $repo -State $state }
    }
}

Run-Test 'names what it found when a status artifact is not a regular file' {
    New-TestRepository {
        param($repo)
        $nested = Join-Path $repo 'nested-repository'
        New-Item -ItemType Directory -Path $nested | Out-Null
        Invoke-Git -Repo $nested -Arguments @('init', '-q')
        [IO.File]::WriteAllText((Join-Path $nested 'inner.txt'), 'inner')
        $result = Invoke-FingerprintTool -Arguments @('-Mode', 'Capture', '-CheckoutPath', $repo, '-Token', 'test-token')
        Assert-True ($result.exitCode -ne 0) 'a directory status entry was accepted as a regular file'
        Assert-True ($result.stderr -match 'is not a regular file: found a directory') ('the diagnostic did not name what was found: ' + $result.stderr)
        Assert-True ($result.stderr -match 'nested-repository') ('the diagnostic did not name where it was found: ' + $result.stderr)
    }
}

Run-Test 'revalidates the completed inventory before anyone trusts it' {
    New-TestRepository {
        param($repo)
        $artifact = Join-Path $repo 'artifact.txt'
        [IO.File]::WriteAllText($artifact, 'first')
        $outcome = & {
            . $toolPath
            $observed = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
            [void]$observed.Add('artifact.txt')
            $records = @([pscustomobject]@{ path = 'artifact.txt'; code = '??'; length = [Int64]5; hash = ('0' * 64) })
            [IO.File]::WriteAllText($artifact, 'grew behind the walking cursor')
            $mutated = 'stability revalidation accepted a mutated artifact'
            try { Assert-InventoryStability -Checkout $repo -Records $records -ObservedPaths $observed }
            catch { $mutated = $_.Exception.Message }

            [IO.File]::WriteAllText($artifact, 'first')
            $empty = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
            $entered = 'stability revalidation accepted a late arrival'
            try { Assert-InventoryStability -Checkout $repo -Records @() -ObservedPaths $empty }
            catch { $entered = $_.Exception.Message }

            return [pscustomobject]@{ mutated = $mutated; entered = $entered }
        }
        Assert-True ($outcome.mutated -like 'status inventory changed while it was fingerprinted*') ('unexpected mutated-artifact outcome: ' + $outcome.mutated)
        Assert-True ($outcome.entered -like 'status inventory changed while it was fingerprinted*') ('unexpected late-arrival outcome: ' + $outcome.entered)
    }
}

Run-Test 'preserves the lane error text when the guard disposition supersedes it' {
    New-TestRepository {
        param($repo)
        $token = 'throwing-lane-token'
        $artifact = Join-Path $repo 'artifact.txt'
        [IO.File]::WriteAllText($artifact, 'before the throwing lane')
        $run = Invoke-ExitingLaneWrapper -Repo $repo -Token $token -LaneExit 0 -MutatePath $artifact -ThrowMessage 'lane-specific failure detail'
        try {
            Assert-True ($run.stdout.Contains('LANE-RAN')) ('the lane never ran: ' + $run.stderr)
            Assert-True (-not $run.stdout.Contains('AFTER-TRY-RAN')) 'the guard disposition did not supersede the lane, so this is not the discard path'
            Assert-True ($run.exitCode -eq 2) ('the guard disposition did not win: ' + $run.exitCode)
            Assert-True ($run.stderr.Contains('lane-specific failure detail')) ('the lane error text was discarded by the guard exit: ' + $run.stderr)
        }
        finally {
            if (-not [string]::IsNullOrWhiteSpace($run.state)) {
                Cleanup-State -Repo $repo -State $run.state -Token $token
            }
        }
    }
}

Write-Output ("Fingerprint tests: {0} passed, {1} failed" -f $script:passed, $script:failed)
if ($script:failed -ne 0) {
    exit 1
}
