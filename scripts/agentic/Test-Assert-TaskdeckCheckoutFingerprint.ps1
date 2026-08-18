[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$toolPath = Join-Path $PSScriptRoot 'Assert-TaskdeckCheckoutFingerprint.ps1'
$powerShellPath = Join-Path $PSHOME 'powershell.exe'
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
    param([string[]]$Arguments)

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $powerShellPath
    $startInfo.Arguments = '-NoProfile -ExecutionPolicy Bypass -File ' + (Quote-Argument $toolPath) + ' ' + (($Arguments | ForEach-Object { Quote-Argument $_ }) -join ' ')
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

Write-Output ("Fingerprint tests: {0} passed, {1} failed" -f $script:passed, $script:failed)
if ($script:failed -ne 0) {
    exit 1
}
