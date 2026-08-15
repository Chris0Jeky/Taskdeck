[CmdletBinding()]
param(
    [string]$ConfigPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path (Split-Path -Parent $PSScriptRoot) '.codex\config.toml'
}

function Throw-TrustFailure {
    param([string]$Message)

    throw "[codex-path-trust] $Message"
}

function Get-ConfiguredPath {
    param([string]$Path)

    $content = [System.IO.File]::ReadAllText($Path)
    $sectionMatches = [regex]::Matches(
        $content,
        '(?ms)^\[shell_environment_policy\.set\]\s*\r?\n(?<body>.*?)(?=^\[|\z)'
    )
    if ($sectionMatches.Count -ne 1) {
        Throw-TrustFailure "Expected one [shell_environment_policy.set] section in $Path."
    }

    $pathMatches = [regex]::Matches(
        $sectionMatches[0].Groups['body'].Value,
        '(?m)^\s*PATH\s*=\s*(?<literal>"(?:\\.|[^"\\])*")\s*(?:#.*)?$'
    )
    if ($pathMatches.Count -ne 1) {
        Throw-TrustFailure "Expected one basic-string PATH assignment in $Path."
    }

    try {
        return ($pathMatches[0].Groups['literal'].Value | ConvertFrom-Json)
    }
    catch {
        Throw-TrustFailure "Could not decode the PATH basic string in ${Path}: $($_.Exception.Message)"
    }
}

$script:CurrentUserSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$script:WeakSids = @(
    'S-1-1-0',       # Everyone
    'S-1-5-4',       # Interactive
    'S-1-5-11',      # Authenticated Users
    'S-1-5-32-545',  # BUILTIN\Users
    $script:CurrentUserSid
)
$script:WriteMask =
    [System.Security.AccessControl.FileSystemRights]::WriteData -bor
    [System.Security.AccessControl.FileSystemRights]::AppendData -bor
    [System.Security.AccessControl.FileSystemRights]::WriteAttributes -bor
    [System.Security.AccessControl.FileSystemRights]::WriteExtendedAttributes -bor
    [System.Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor
    [System.Security.AccessControl.FileSystemRights]::Delete -bor
    [System.Security.AccessControl.FileSystemRights]::ChangePermissions -bor
    [System.Security.AccessControl.FileSystemRights]::TakeOwnership
$script:DeleteChildMask =
    [System.Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles

function Test-WeakIdentity {
    param([System.Security.Principal.IdentityReference]$Identity)

    $sid = $null
    try {
        $sid = $Identity.Translate([System.Security.Principal.SecurityIdentifier]).Value
    }
    catch {
        # Unresolved local identities still receive the conservative name check below.
    }

    if ($sid -and $script:WeakSids -contains $sid) {
        return $true
    }

    return $Identity.Value -match '(?i)(^|\\)(Everyone|Users|Authenticated Users|INTERACTIVE|.*CodexSandbox.*)$'
}

function Get-WeakAllowAces {
    param(
        [string]$Path,
        [System.Security.AccessControl.FileSystemRights]$RightsMask
    )

    foreach ($ace in (Get-Acl -LiteralPath $Path).Access) {
        if (
            $ace.AccessControlType -eq [System.Security.AccessControl.AccessControlType]::Allow -and
            ($ace.FileSystemRights -band $RightsMask) -ne 0 -and
            (Test-WeakIdentity -Identity $ace.IdentityReference)
        ) {
            $ace
        }
    }
}

function Assert-NoWeakAllow {
    param(
        [string]$Path,
        [System.Security.AccessControl.FileSystemRights]$RightsMask,
        [string]$Boundary
    )

    $weakAces = @(Get-WeakAllowAces -Path $Path -RightsMask $RightsMask)
    if ($weakAces.Count -gt 0) {
        $details = ($weakAces | ForEach-Object {
            "$($_.IdentityReference.Value):$($_.FileSystemRights)"
        }) -join ', '
        Throw-TrustFailure "$Boundary is writable by a weak identity at ${Path}: $details"
    }
}

function Assert-TrustedDirectory {
    param([string]$Path)

    if (-not [System.IO.Path]::IsPathRooted($Path)) {
        Throw-TrustFailure "PATH entry is not absolute: $Path"
    }
    if ($Path.IndexOfAny([char[]]'*?') -ge 0) {
        Throw-TrustFailure "PATH entry contains a wildcard: $Path"
    }

    $fullPath = [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
    if ($fullPath.StartsWith('\\', [System.StringComparison]::Ordinal)) {
        Throw-TrustFailure "UNC PATH entries are not allowed: $Path"
    }
    if ($fullPath.Length -gt 2 -and $fullPath.Substring(2).Contains(':')) {
        Throw-TrustFailure "PATH entry contains an alternate data stream or device suffix: $Path"
    }

    foreach ($blockedRoot in @('C:\Users', 'C:\ProgramData')) {
        if (
            $fullPath.Equals($blockedRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
            $fullPath.StartsWith("$blockedRoot\", [System.StringComparison]::OrdinalIgnoreCase)
        ) {
            Throw-TrustFailure "PATH entry is inside a user-writable root: $Path"
        }
    }

    if (-not (Test-Path -LiteralPath $fullPath -PathType Container)) {
        Throw-TrustFailure "PATH entry must be an existing directory: $Path"
    }

    $root = [System.IO.Path]::GetPathRoot($fullPath)
    $rootItem = Get-Item -LiteralPath $root -Force
    if (($rootItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        Throw-TrustFailure "PATH root is a reparse point: $root"
    }

    $current = $root
    $relative = $fullPath.Substring($root.Length).Trim('\')
    foreach ($segment in $relative.Split([char]'\', [System.StringSplitOptions]::RemoveEmptyEntries)) {
        $parent = $current
        $current = Join-Path $current $segment
        $item = Get-Item -LiteralPath $current -Force
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            Throw-TrustFailure "PATH chain contains a reparse point: $current"
        }

        Assert-NoWeakAllow -Path $current -RightsMask $script:WriteMask -Boundary 'PATH directory'
        Assert-NoWeakAllow -Path $parent -RightsMask $script:DeleteChildMask -Boundary 'PATH parent replacement boundary'
    }
}

function Assert-TrustedFile {
    param([string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        Throw-TrustFailure "Required executable is missing: $fullPath"
    }

    Assert-TrustedDirectory -Path (Split-Path -Parent $fullPath)
    $item = Get-Item -LiteralPath $fullPath -Force
    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        Throw-TrustFailure "Executable is a reparse point: $fullPath"
    }
    Assert-NoWeakAllow -Path $fullPath -RightsMask $script:WriteMask -Boundary 'Executable'
}

function Resolve-ApplicationPath {
    param([string]$Name)

    $commands = @(Get-Command -Name $Name -CommandType Application -All -ErrorAction SilentlyContinue)
    if ($commands.Count -eq 0) {
        return $null
    }
    return [System.IO.Path]::GetFullPath($commands[0].Source)
}

function Assert-ApplicationResolution {
    param(
        [string]$Name,
        [string]$ExpectedPath
    )

    $actualPath = Resolve-ApplicationPath -Name $Name
    if (-not $actualPath) {
        Throw-TrustFailure "Required command '$Name' did not resolve."
    }
    if (-not $actualPath.Equals($ExpectedPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        Throw-TrustFailure "Command '$Name' resolved to '$actualPath', expected '$ExpectedPath'."
    }
    Assert-TrustedFile -Path $actualPath
}

function Assert-ApplicationAbsent {
    param([string]$Name)

    $actualPath = Resolve-ApplicationPath -Name $Name
    if ($actualPath) {
        Throw-TrustFailure "Optional command '$Name' must fail closed but resolved to '$actualPath'."
    }
}

function Invoke-CheckedNative {
    param(
        [string]$Path,
        [string[]]$Arguments
    )

    $global:LASTEXITCODE = $null
    $output = @(& $Path @Arguments 2>&1)
    $exitCode = $global:LASTEXITCODE
    if ($null -eq $exitCode -or $exitCode -ne 0) {
        Throw-TrustFailure "'$Path $($Arguments -join ' ')' failed with exit '$exitCode': $($output -join ' ')"
    }
    Write-Host "OK [codex-path-trust]: $([System.IO.Path]::GetFileName($Path)) $($output[0])"
}

$resolvedConfig = (Resolve-Path -LiteralPath $ConfigPath).Path
$configuredPath = Get-ConfiguredPath -Path $resolvedConfig
$pathEntries = @($configuredPath.Split([System.IO.Path]::PathSeparator))
if ($pathEntries.Count -eq 0 -or $pathEntries -contains '') {
    Throw-TrustFailure 'Configured PATH must not contain empty entries.'
}

$seen = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $pathEntries) {
    if (-not $seen.Add([System.IO.Path]::GetFullPath($entry).TrimEnd('\'))) {
        Throw-TrustFailure "Configured PATH contains a duplicate entry: $entry"
    }
    Assert-TrustedDirectory -Path $entry
}

$originalPath = $env:PATH
$tempBase = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\')
$forgedBin = Join-Path $tempBase ("taskdeck-codex-path-canary-{0}" -f [guid]::NewGuid().ToString('N'))

try {
    [void](New-Item -ItemType Directory -Path $forgedBin)
    $weakCanaryAces = @(Get-WeakAllowAces -Path $forgedBin -RightsMask $script:WriteMask)
    if ($weakCanaryAces.Count -eq 0) {
        Throw-TrustFailure "Writable canary directory did not expose a weak write ACL: $forgedBin"
    }
    $canaryRejected = $false
    try {
        Assert-TrustedDirectory -Path $forgedBin
    }
    catch {
        $canaryRejected = $_.Exception.Message -match 'user-writable root|writable by a weak identity'
    }
    if (-not $canaryRejected) {
        Throw-TrustFailure "Verifier did not reject the writable canary directory: $forgedBin"
    }

    $canarySource = 'C:\Windows\System32\where.exe'
    foreach ($name in @('git', 'gh', 'jq', 'python', 'python3', 'node', 'npm', 'npx', 'powershell')) {
        Copy-Item -LiteralPath $canarySource -Destination (Join-Path $forgedBin "$name.exe")
    }

    $env:PATH = "$forgedBin$([System.IO.Path]::PathSeparator)$configuredPath"
    foreach ($name in @('git', 'gh', 'jq', 'python', 'python3', 'node', 'npm', 'npx', 'powershell')) {
        $positiveControl = Resolve-ApplicationPath -Name $name
        $expectedCanary = [System.IO.Path]::GetFullPath((Join-Path $forgedBin "$name.exe"))
        if (
            -not $positiveControl -or
            -not $positiveControl.Equals($expectedCanary, [System.StringComparison]::OrdinalIgnoreCase)
        ) {
            Throw-TrustFailure "Forged '$name' positive control could not win from the prepended writable bin."
        }
    }

    $env:PATH = $configuredPath

    $expectedApplications = @(
        @{ Name = 'git';        Path = 'C:\Program Files\Git\cmd\git.exe' },
        @{ Name = 'gh';         Path = 'C:\Program Files\GitHub CLI\gh.exe' },
        @{ Name = 'dotnet';     Path = 'C:\Program Files\dotnet\dotnet.exe' },
        @{ Name = 'node';       Path = 'C:\Program Files\nodejs\node.exe' },
        @{ Name = 'npm';        Path = 'C:\Program Files\nodejs\npm.cmd' },
        @{ Name = 'npx';        Path = 'C:\Program Files\nodejs\npx.cmd' },
        @{ Name = 'py';         Path = 'C:\Windows\py.exe' },
        @{ Name = 'powershell'; Path = 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' }
    )
    foreach ($expected in $expectedApplications) {
        Assert-ApplicationResolution -Name $expected.Name -ExpectedPath $expected.Path
    }

    foreach ($name in @('jq', 'python', 'python3')) {
        Assert-ApplicationAbsent -Name $name
    }

    foreach ($packageManager in @('npm', 'npx')) {
        $candidates = @(Get-Command -Name $packageManager -All -ErrorAction Stop | Where-Object {
            $_.CommandType -eq 'Application' -or $_.CommandType -eq 'ExternalScript'
        })
        if ($candidates.Count -eq 0) {
            Throw-TrustFailure "Bare '$packageManager' did not resolve to a script or application."
        }
        foreach ($candidate in $candidates) {
            Assert-TrustedFile -Path $candidate.Source
        }
    }

    Invoke-CheckedNative -Path 'C:\Program Files\Git\cmd\git.exe' -Arguments @('--version')
    Invoke-CheckedNative -Path 'C:\Program Files\GitHub CLI\gh.exe' -Arguments @('--version')
    Invoke-CheckedNative -Path 'C:\Program Files\dotnet\dotnet.exe' -Arguments @('--version')
    Invoke-CheckedNative -Path 'C:\Program Files\nodejs\node.exe' -Arguments @('--version')
    Invoke-CheckedNative -Path 'C:\Program Files\nodejs\npm.cmd' -Arguments @('--version')
    Invoke-CheckedNative -Path 'C:\Program Files\nodejs\npx.cmd' -Arguments @('--version')
    Invoke-CheckedNative -Path 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -Arguments @(
        '-NoLogo', '-NoProfile', '-NonInteractive', '-Command', '$PSVersionTable.PSVersion.ToString()'
    )

    $pythonProbe = @'
import json
import pathlib
import sys
import tomllib

config_path = pathlib.Path(sys.argv[1])
with config_path.open('rb') as config_file:
    config = tomllib.load(config_file)
print(json.dumps({
    'executable': sys.executable,
    'path': config['shell_environment_policy']['set']['PATH'],
    'version': sys.version.split()[0],
}))
'@
    $global:LASTEXITCODE = $null
    $pythonProbeOutput = @(& 'C:\Windows\py.exe' -3 -B -c $pythonProbe $resolvedConfig 2>&1)
    $pythonProbeExit = $global:LASTEXITCODE
    if ($null -eq $pythonProbeExit -or $pythonProbeExit -ne 0) {
        Throw-TrustFailure "Trusted py -3 TOML probe failed with exit '$pythonProbeExit': $($pythonProbeOutput -join ' ')"
    }
    try {
        $pythonInfo = $pythonProbeOutput[-1] | ConvertFrom-Json
    }
    catch {
        Throw-TrustFailure "Trusted py -3 TOML probe returned invalid JSON: $($pythonProbeOutput -join ' ')"
    }
    if ($pythonInfo.path -cne $configuredPath) {
        Throw-TrustFailure 'The full TOML parse disagrees with the bootstrap PATH extraction.'
    }
    Assert-TrustedFile -Path $pythonInfo.executable
    Write-Host "OK [codex-path-trust]: py -3 $($pythonInfo.version) -> $($pythonInfo.executable)"

    Write-Host "OK [codex-path-trust]: $($pathEntries.Count) existing PATH roots are protected."
    Write-Host 'OK [codex-path-trust]: forged writable-bin executables cannot participate in the configured PATH.'
    Write-Host 'OK [codex-path-trust]: jq, python, and python3 fail closed; use py -3 for Windows Python.'
}
finally {
    $env:PATH = $originalPath
    if (Test-Path -LiteralPath $forgedBin) {
        $resolvedCanary = [System.IO.Path]::GetFullPath($forgedBin)
        if (
            $resolvedCanary.StartsWith("$tempBase\", [System.StringComparison]::OrdinalIgnoreCase) -and
            -not $resolvedCanary.Equals($tempBase, [System.StringComparison]::OrdinalIgnoreCase)
        ) {
            Remove-Item -LiteralPath $resolvedCanary -Recurse -Force
        }
        else {
            Throw-TrustFailure "Refusing to remove unexpected canary path: $resolvedCanary"
        }
    }
}
