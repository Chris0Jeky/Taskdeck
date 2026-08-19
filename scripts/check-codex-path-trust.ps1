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

$script:CurrentIdentity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
$script:CurrentPrincipal = [System.Security.Principal.WindowsPrincipal]::new($script:CurrentIdentity)
$script:CurrentUserSid = $script:CurrentIdentity.User.Value
$script:AlwaysWeakSids = @(
    'S-1-1-0',       # Everyone
    'S-1-5-4',       # Interactive
    'S-1-5-11',      # Authenticated Users
    'S-1-5-32-545',  # BUILTIN\Users
    $script:CurrentUserSid
)
$script:EnabledTokenGroupSids = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
foreach ($group in $script:CurrentIdentity.Groups) {
    if ($script:CurrentPrincipal.IsInRole($group)) {
        [void]$script:EnabledTokenGroupSids.Add($group.Value)
    }
}
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

# Generic-rights bits (winnt.h) and the standard file-object GENERIC_MAPPING they expand to.
# An ACE that grants only GENERIC_WRITE or GENERIC_ALL carries none of the file-specific write
# bits, so a raw -band against $script:WriteMask false-passes it.
#
# Provenance, measured on this host rather than assumed. Windows maps generic bits when a DACL is
# APPLIED to a file object, so an effective ACE written through Set-Acl reads back already mapped:
# `(A;;GW;;;WD)` goes in as 0x40000000 and comes back as 0x00120116. Generic bits still reach the
# managed reader verbatim in two shapes that were measured here:
#   * ACEs stored for propagation rather than applied to the object - `Get-Acl C:\` returns
#     `(A;OICIIOID;SDGXGWGR;;;AU)` (raw 0xE0010000) and `(A;OICIIOID;GA;;;BA)` (raw 0x10000000);
#   * descriptors materialised in memory from SDDL - SetSecurityDescriptorSddlForm keeps
#     0x40000000 until Set-Acl applies it, which is also how the regression controls below build
#     their synthetic ACEs.
# Whether any given ACE reached its DACL pre-mapped is a property of the writer and the resource
# manager, which this verifier cannot observe from the mask alone. Map defensively before every
# mask test rather than assuming the reader's masks are already expanded.
$script:GenericAllBit = 0x10000000
$script:GenericExecuteBit = 0x20000000
$script:GenericWriteBit = 0x40000000
$script:GenericReadBit = [int]::MinValue  # 0x80000000 as a signed AccessMask
$script:FileAllAccess = 0x001F01FF
$script:FileGenericRead = 0x00120089
$script:FileGenericWrite = 0x00120116
$script:FileGenericExecute = 0x001200A0

# AccessRule.AccessMask is protected; FileSystemRights is a straight cast of it in practice, but
# the raw property is read first so the check never depends on the enum surfacing generic values.
$script:AccessMaskProperty = [System.Security.AccessControl.AccessRule].GetProperty(
    'AccessMask',
    [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::NonPublic
)

function Get-RawAccessMask {
    param([System.Security.AccessControl.FileSystemAccessRule]$Ace)

    $mask = [int]$Ace.FileSystemRights
    if ($null -ne $script:AccessMaskProperty) {
        $mask = $mask -bor [int]$script:AccessMaskProperty.GetValue($Ace, $null)
    }
    return $mask
}

function Expand-GenericRights {
    param([int]$AccessMask)

    $expanded = $AccessMask
    if (($AccessMask -band $script:GenericAllBit) -ne 0) {
        $expanded = $expanded -bor $script:FileAllAccess
    }
    if (($AccessMask -band $script:GenericWriteBit) -ne 0) {
        $expanded = $expanded -bor $script:FileGenericWrite
    }
    if (($AccessMask -band $script:GenericReadBit) -ne 0) {
        $expanded = $expanded -bor $script:FileGenericRead
    }
    if (($AccessMask -band $script:GenericExecuteBit) -ne 0) {
        $expanded = $expanded -bor $script:FileGenericExecute
    }
    return $expanded
}

function Get-EffectiveAccessMask {
    param([System.Security.AccessControl.FileSystemAccessRule]$Ace)

    return (Expand-GenericRights -AccessMask (Get-RawAccessMask -Ace $Ace))
}

function New-SyntheticAccessRule {
    param(
        [System.Security.Principal.SecurityIdentifier]$Sid,
        [int]$AccessMask
    )

    # FileSystemAccessRule's public constructor rejects generic-rights values outright, so the
    # synthetic controls are built through AccessRuleFactory - the same path Get-Acl uses when it
    # materialises an on-disk ACE, and therefore the same object shape the real checks evaluate.
    return (New-Object System.Security.AccessControl.DirectorySecurity).AccessRuleFactory(
        $Sid,
        $AccessMask,
        $false,
        [System.Security.AccessControl.InheritanceFlags]::None,
        [System.Security.AccessControl.PropagationFlags]::None,
        [System.Security.AccessControl.AccessControlType]::Allow
    )
}

function Test-WeakIdentity {
    param([System.Security.Principal.IdentityReference]$Identity)

    $sid = $null
    try {
        $sid = $Identity.Translate([System.Security.Principal.SecurityIdentifier]).Value
    }
    catch {
        # Unresolved local identities still receive the conservative name check below.
    }

    if ($sid) {
        if ($script:AlwaysWeakSids -contains $sid) {
            return $true
        }
        if ($script:EnabledTokenGroupSids.Contains($sid)) {
            return $true
        }
    }

    return $Identity.Value -match '(?i)(^|\\)(Everyone|Users|Authenticated Users|INTERACTIVE|.*CodexSandbox.*)$'
}

function Test-WeakWritableAce {
    param(
        [System.Security.AccessControl.FileSystemAccessRule]$Ace,
        [int]$RightsMask
    )

    if ($Ace.AccessControlType -ne [System.Security.AccessControl.AccessControlType]::Allow) {
        return $false
    }
    # Generic rights are expanded BEFORE the write/delete/owner/DACL mask test, so an ACE granting
    # GENERIC_WRITE or GENERIC_ALL to a weak principal fails the check.
    if (((Get-EffectiveAccessMask -Ace $Ace) -band $RightsMask) -eq 0) {
        return $false
    }
    return (Test-WeakIdentity -Identity $Ace.IdentityReference)
}

function Get-WeakAllowAces {
    param(
        [string]$Path,
        [System.Security.AccessControl.FileSystemRights]$RightsMask
    )

    $maskBits = [int]$RightsMask
    foreach ($ace in (Get-Acl -LiteralPath $Path).Access) {
        if (Test-WeakWritableAce -Ace $ace -RightsMask $maskBits) {
            $ace
        }
    }
}

function Test-PathWeaklyWritable {
    param([string]$Path)

    $acl = Get-Acl -LiteralPath $Path
    $owner = New-Object System.Security.Principal.NTAccount($acl.Owner)
    if (Test-WeakIdentity -Identity $owner) {
        return $true
    }
    return (@(Get-WeakAllowAces -Path $Path -RightsMask $script:WriteMask).Count -gt 0)
}

function Assert-NoWeakAllow {
    param(
        [string]$Path,
        [System.Security.AccessControl.FileSystemRights]$RightsMask,
        [string]$Boundary
    )

    $acl = Get-Acl -LiteralPath $Path
    $owner = New-Object System.Security.Principal.NTAccount($acl.Owner)
    if (Test-WeakIdentity -Identity $owner) {
        Throw-TrustFailure "$Boundary is owned by a weak identity at ${Path}: $($acl.Owner)"
    }

    $weakAces = @(Get-WeakAllowAces -Path $Path -RightsMask $RightsMask)
    if ($weakAces.Count -gt 0) {
        $details = ($weakAces | ForEach-Object {
            '{0}:{1} (effective mask 0x{2:X8})' -f
                $_.IdentityReference.Value,
                $_.FileSystemRights,
                (Get-EffectiveAccessMask -Ace $_)
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

    # $fullPath has had its trailing separator trimmed, so a bare drive-root entry is 'C:' here and
    # GetPathRoot('C:') returns 'C:' - a drive-RELATIVE path, not the volume root. Normalise to the
    # rooted form before touching the filesystem or slicing the remainder off it.
    $root = [System.IO.Path]::GetPathRoot($fullPath + '\')
    $rootItem = Get-Item -LiteralPath $root -Force
    if (($rootItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        Throw-TrustFailure "PATH root is a reparse point: $root"
    }

    $current = $root
    $relative = ''
    if ($fullPath.Length -gt $root.Length) {
        $relative = $fullPath.Substring($root.Length).Trim('\')
    }
    if ([string]::IsNullOrEmpty($relative)) {
        # A bare drive root IS the PATH entry, and the per-segment loop below has no segment to walk
        # for it, so without this the entry would reach zero ACL validation (issue #1651 item 2).
        # Nested entries keep the narrower delete-child check on the root as a replacement boundary;
        # a volume root has no parent directory that could replace it.
        Assert-NoWeakAllow -Path $root -RightsMask $script:WriteMask -Boundary 'PATH volume root'
    }
    # The separator is cast to char[] deliberately. Windows PowerShell 5.1 has no
    # String.Split(char, StringSplitOptions) overload, so passing a bare [char] silently binds to
    # Split(params char[]) with RemoveEmptyEntries coerced to a SECOND separator character (U+0001,
    # measured) - the options are never applied. PowerShell 7 does have that overload and behaves
    # differently, which is what made the bare-drive-root gap above visible on one runtime only.
    foreach ($segment in $relative.Split([char[]]'\', [System.StringSplitOptions]::RemoveEmptyEntries)) {
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

function Select-PythonInterpreterFromInventory {
    param([object[]]$Inventory)

    # `py -0p` marks the interpreter the launcher will select with a trailing `*`. Prefer that
    # marked entry so the validated path is the one `py -3` actually runs; fall back to the first
    # Python 3 entry only when no marker is present.
    $pattern = '^\s*-(?:V:)?3(?:\.\d+)*(?<default>\s+\*)?\s+(?<path>[A-Za-z]:\\.+?\.exe)\s*$'
    $firstPython3 = $null
    foreach ($line in $Inventory) {
        $text = [string]$line
        if ($text -match $pattern) {
            if ($Matches.ContainsKey('default')) {
                return [System.IO.Path]::GetFullPath($Matches['path'])
            }
            if ($null -eq $firstPython3) {
                $firstPython3 = [System.IO.Path]::GetFullPath($Matches['path'])
            }
        }
    }
    if ($firstPython3) {
        return $firstPython3
    }

    Throw-TrustFailure "Python launcher did not enumerate a Python 3 interpreter: $($Inventory -join ' ')"
}

# Environment inputs that steer `py -3` away from the interpreter `py -0p` enumerated and this
# verifier validated.
#   PY_PYTHON / PY_PYTHON3          pick a different installed version.
#   PYLAUNCHER_ALLOW_INSTALL        lets an unsatisfied request install from the Microsoft Store.
#   PYLAUNCHER_ALWAYS_INSTALL       forces that install path even when a matching version exists,
#                                   so `py -3` can run an interpreter that was never in the
#                                   validated inventory.
#   PYLAUNCHER_DRYRUN               makes the launcher PRINT the command instead of running it.
#                                   Measured on this host: with it set, `py -3 -c "print(...)"`
#                                   emitted `C:\Python314\python.exe -c ...` and exited 0 without
#                                   executing - a validated execution silently becomes a no-op
#                                   that still looks successful to its caller.
# The PYLAUNCHER_* trio are documented as "if set" switches rather than value-carrying settings, so
# they are treated as divertive whenever they are PRESENT, including with an empty or whitespace
# value; PY_PYTHON* only divert when they actually carry a version. The stricter reading is the
# fail-closed one, and neither reading is cheap to observe from outside the launcher.
# py.ini has no equivalent of the PYLAUNCHER_* trio - its [defaults] keys mirror PY_PYTHON* only -
# and the py.ini check below fails closed on the presence of the whole file, so any ini-side
# selection key is already covered wholesale. Deliberately NOT gated: PYLAUNCHER_DEBUG (diagnostic
# output only) and PYLAUNCHER_NO_SEARCH_PATH (narrows the launcher's search instead of redirecting
# it); neither can point `py -3` at a different interpreter.
$script:PythonLauncherSelectionVariables = @(
    [pscustomobject]@{ Name = 'PY_PYTHON';                 TripsWhenSetEmpty = $false },
    [pscustomobject]@{ Name = 'PY_PYTHON3';                TripsWhenSetEmpty = $false },
    [pscustomobject]@{ Name = 'PYLAUNCHER_ALLOW_INSTALL';  TripsWhenSetEmpty = $true },
    [pscustomobject]@{ Name = 'PYLAUNCHER_ALWAYS_INSTALL'; TripsWhenSetEmpty = $true },
    [pscustomobject]@{ Name = 'PYLAUNCHER_DRYRUN';         TripsWhenSetEmpty = $true }
)

function Get-PythonLauncherConfigCandidates {
    param(
        [string]$LauncherPath,
        [string[]]$AdditionalConfigDirectories
    )

    # The launcher reads py.ini from %LOCALAPPDATA% and from the directory holding py.exe.
    $candidates = New-Object 'System.Collections.Generic.List[object]'
    foreach ($localAppData in @([System.Environment]::GetFolderPath('LocalApplicationData'), $env:LOCALAPPDATA)) {
        if (-not [string]::IsNullOrWhiteSpace($localAppData)) {
            # LOCALAPPDATA is user-owned by construction: any py.ini there diverts selection.
            $candidates.Add([pscustomobject]@{ Directory = $localAppData; AlwaysWeak = $true })
        }
    }
    $candidates.Add([pscustomobject]@{ Directory = (Split-Path -Parent $LauncherPath); AlwaysWeak = $false })
    foreach ($extra in $AdditionalConfigDirectories) {
        if (-not [string]::IsNullOrWhiteSpace($extra)) {
            $candidates.Add([pscustomobject]@{ Directory = $extra; AlwaysWeak = $false })
        }
    }
    return $candidates
}

function Get-PythonLauncherDiversions {
    param(
        [string]$LauncherPath,
        [string[]]$AdditionalConfigDirectories = @()
    )

    # `py -0p` enumerates installs, but execution-time selection is user-controllable: the launcher
    # environment inputs listed in $script:PythonLauncherSelectionVariables and a user-writable
    # py.ini both change what `py -3` actually runs, so the inventory-validated path would not be
    # the one that executes. Fail closed on any such input rather than trusting that the launcher
    # will pick what the verifier just validated.
    $diversions = New-Object 'System.Collections.Generic.List[string]'

    foreach ($variable in $script:PythonLauncherSelectionVariables) {
        foreach ($scope in @('Process', 'User', 'Machine')) {
            $value = [System.Environment]::GetEnvironmentVariable(
                $variable.Name,
                [System.EnvironmentVariableTarget]$scope
            )
            $isDivertive = if ($variable.TripsWhenSetEmpty) {
                $null -ne $value
            }
            else {
                -not [string]::IsNullOrWhiteSpace($value)
            }
            if ($isDivertive) {
                $diversions.Add("$($variable.Name) is set in the $scope environment ('$value')")
            }
        }
    }

    $seenDirectories = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    $candidates = @(Get-PythonLauncherConfigCandidates `
            -LauncherPath $LauncherPath `
            -AdditionalConfigDirectories $AdditionalConfigDirectories)
    foreach ($candidate in $candidates) {
        $directory = [System.IO.Path]::GetFullPath($candidate.Directory)
        if (-not $seenDirectories.Add($directory.TrimEnd('\'))) {
            continue
        }
        if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
            continue
        }
        $iniPath = Join-Path $directory 'py.ini'
        if (-not (Test-Path -LiteralPath $iniPath -PathType Leaf)) {
            continue
        }
        if (
            $candidate.AlwaysWeak -or
            (Test-PathWeaklyWritable -Path $directory) -or
            (Test-PathWeaklyWritable -Path $iniPath)
        ) {
            $diversions.Add("a user-writable py.ini exists at $iniPath")
        }
    }

    return $diversions.ToArray()
}

function Assert-TrustedPythonLauncherInputs {
    param([string]$LauncherPath)

    $diversions = @(Get-PythonLauncherDiversions -LauncherPath $LauncherPath)
    if ($diversions.Count -gt 0) {
        Throw-TrustFailure (
            'py -3 selection inputs are not trusted: {0}' -f ($diversions -join '; ')
        )
    }
}

function Get-SelectedPythonInterpreter {
    param([string]$LauncherPath)

    $global:LASTEXITCODE = $null
    $inventory = @(& $LauncherPath -0p 2>&1)
    $exitCode = $global:LASTEXITCODE
    if ($null -eq $exitCode -or $exitCode -ne 0) {
        Throw-TrustFailure "Trusted Python launcher inventory failed with exit '$exitCode': $($inventory -join ' ')"
    }

    return Select-PythonInterpreterFromInventory -Inventory $inventory
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
$aclCanary = Join-Path $tempBase ("taskdeck-codex-acl-canary-{0}" -f [guid]::NewGuid().ToString('N'))

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

    # Bare drive-root control (issue #1651 item 2). A volume-root PATH entry has no path segments
    # for the per-segment walk to visit, so without the explicit volume-root check it reaches ZERO
    # ACL validation. Under Windows PowerShell 5.1 the miscast Split above happened to mask that
    # (its stray empty segment made the walk re-check the root); the check no longer depends on
    # which runtime is parsing the script. The control is host-independent: it asserts the root is
    # subject to the same weak-write evaluation every other PATH directory gets, whatever that
    # evaluation concludes here. (It concludes "weakly writable" on this box, where C:\ grants
    # Authenticated Users Modify.)
    $volumeRootControl = [System.IO.Path]::GetPathRoot($env:SystemRoot)
    $volumeRootIsWeaklyWritable = Test-PathWeaklyWritable -Path $volumeRootControl
    $volumeRootRejected = $false
    try {
        Assert-TrustedDirectory -Path $volumeRootControl
    }
    catch {
        $volumeRootRejected = $_.Exception.Message -match 'owned by a weak identity|writable by a weak identity'
    }
    if ($volumeRootRejected -ne $volumeRootIsWeaklyWritable) {
        Throw-TrustFailure (
            "Bare drive-root PATH entry '$volumeRootControl' bypassed the directory ACL evaluation " +
            "(weakly writable: $volumeRootIsWeaklyWritable, rejected: $volumeRootRejected)."
        )
    }

    $canarySource = 'C:\Windows\System32\where.exe'
    foreach ($name in @('git', 'gh', 'docker', 'jq', 'python', 'python3', 'node', 'npm', 'npx', 'powershell')) {
        Copy-Item -LiteralPath $canarySource -Destination (Join-Path $forgedBin "$name.exe")
    }

    $env:PATH = "$forgedBin$([System.IO.Path]::PathSeparator)$configuredPath"
    foreach ($name in @('git', 'gh', 'docker', 'jq', 'python', 'python3', 'node', 'npm', 'npx', 'powershell')) {
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

    [void](New-Item -ItemType Directory -Path $aclCanary)
    $delegatedGroupSid = @($script:CurrentIdentity.Groups | Where-Object {
        $script:CurrentPrincipal.IsInRole($_) -and
        $script:AlwaysWeakSids -notcontains $_.Value
    } | Select-Object -First 1)
    if ($delegatedGroupSid.Count -ne 1) {
        Throw-TrustFailure 'Current token did not expose an enabled non-baseline group for the delegated-group ACL control.'
    }
    $delegatedGroupSid = $delegatedGroupSid[0]

    $acl = Get-Acl -LiteralPath $aclCanary
    $delegatedWriteRule = [System.Security.AccessControl.FileSystemAccessRule]::new(
        $delegatedGroupSid,
        [System.Security.AccessControl.FileSystemRights]::WriteData,
        [System.Security.AccessControl.InheritanceFlags]::None,
        [System.Security.AccessControl.PropagationFlags]::None,
        [System.Security.AccessControl.AccessControlType]::Allow
    )
    [void]$acl.AddAccessRule($delegatedWriteRule)
    Set-Acl -LiteralPath $aclCanary -AclObject $acl

    $delegatedGroupDetected = @(Get-WeakAllowAces -Path $aclCanary -RightsMask $script:WriteMask | Where-Object {
        try {
            $_.IdentityReference.Translate([System.Security.Principal.SecurityIdentifier]).Value -eq $delegatedGroupSid.Value
        }
        catch {
            $false
        }
    })
    if ($delegatedGroupDetected.Count -eq 0) {
        Throw-TrustFailure "Verifier did not reject write access delegated through enabled token group $($delegatedGroupSid.Value)."
    }

    $ownerRejected = $false
    try {
        Assert-NoWeakAllow -Path $aclCanary -RightsMask $script:WriteMask -Boundary 'ACL owner control'
    }
    catch {
        $ownerRejected = $_.Exception.Message -match 'owned by a weak identity'
    }
    if (-not $ownerRejected) {
        Throw-TrustFailure 'Verifier did not reject effective owner rights on the ACL control directory.'
    }

    $administratorsSid = [System.Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
    $administratorsExpectedWeak = $script:CurrentPrincipal.IsInRole($administratorsSid)
    if ((Test-WeakIdentity -Identity $administratorsSid) -ne $administratorsExpectedWeak) {
        Throw-TrustFailure 'Enabled-group control misclassified the Administrators SID for the current token.'
    }

    # Generic-rights mutation controls (issue #1651 HIGH-1). Windows maps generic bits when a DACL
    # is applied through Set-Acl, so an on-disk canary cannot reproduce the gap; the controls use
    # synthetic ACEs carrying the unmapped mask, exactly as the managed reader would surface them.
    $writeMaskBits = [int]$script:WriteMask
    $everyoneSid = [System.Security.Principal.SecurityIdentifier]::new('S-1-1-0')
    $systemSid = [System.Security.Principal.SecurityIdentifier]::new('S-1-5-18')
    $systemIsTrustedForThisToken = (
        $script:CurrentUserSid -ne $systemSid.Value -and
        -not $script:EnabledTokenGroupSids.Contains($systemSid.Value)
    )

    foreach ($writeGeneric in @(
        @{ Name = 'GENERIC_WRITE'; Mask = $script:GenericWriteBit },
        @{ Name = 'GENERIC_ALL'; Mask = $script:GenericAllBit }
    )) {
        $weakGenericAce = New-SyntheticAccessRule -Sid $everyoneSid -AccessMask $writeGeneric.Mask
        if (((Get-RawAccessMask -Ace $weakGenericAce) -band $writeMaskBits) -ne 0) {
            Throw-TrustFailure "$($writeGeneric.Name) control is not a regression test: its raw mask already carries file-specific write bits."
        }
        if (-not (Test-WeakWritableAce -Ace $weakGenericAce -RightsMask $writeMaskBits)) {
            Throw-TrustFailure "Verifier did not reject a weak-principal ACE granting $($writeGeneric.Name)."
        }
        if ($systemIsTrustedForThisToken) {
            $strongGenericAce = New-SyntheticAccessRule -Sid $systemSid -AccessMask $writeGeneric.Mask
            if (Test-WeakWritableAce -Ace $strongGenericAce -RightsMask $writeMaskBits) {
                Throw-TrustFailure "$($writeGeneric.Name) mapping misclassified a trusted principal (SYSTEM) as weak."
            }
        }
    }

    foreach ($readOnlyGeneric in @(
        @{ Name = 'GENERIC_READ'; Mask = $script:GenericReadBit },
        @{ Name = 'GENERIC_EXECUTE'; Mask = $script:GenericExecuteBit }
    )) {
        $readOnlyAce = New-SyntheticAccessRule -Sid $everyoneSid -AccessMask $readOnlyGeneric.Mask
        if (Test-WeakWritableAce -Ace $readOnlyAce -RightsMask $writeMaskBits) {
            Throw-TrustFailure "$($readOnlyGeneric.Name) mapping over-blocked: read-only generic rights are not a write grant."
        }
    }

    $untrustedPython = Join-Path $aclCanary 'python.exe'
    Copy-Item -LiteralPath $canarySource -Destination $untrustedPython
    $selectedCanaryPython = Select-PythonInterpreterFromInventory -Inventory @(" -V:3.13 *        $untrustedPython")
    if (-not $selectedCanaryPython.Equals($untrustedPython, [System.StringComparison]::OrdinalIgnoreCase)) {
        Throw-TrustFailure 'Python inventory control did not select the forged interpreter path.'
    }
    $untrustedPythonRejected = $false
    try {
        Assert-TrustedFile -Path $selectedCanaryPython
    }
    catch {
        $untrustedPythonRejected = $_.Exception.Message -match 'user-writable root|owned by a weak identity|writable by a weak identity'
    }
    if (-not $untrustedPythonRejected) {
        Throw-TrustFailure 'Verifier did not reject the forged Python interpreter before execution.'
    }

    # Launcher-selection controls (issue #1651 HIGH-2).
    $pythonLauncher = 'C:\Windows\py.exe'

    $markedInventory = @(
        ' -V:3.13          C:\NotDefault\python.exe',
        ' -V:3.14 *        C:\Default\python.exe'
    )
    $markedSelection = Select-PythonInterpreterFromInventory -Inventory $markedInventory
    if (-not $markedSelection.Equals('C:\Default\python.exe', [System.StringComparison]::OrdinalIgnoreCase)) {
        Throw-TrustFailure "Python inventory selection ignored the launcher's default marker: $markedSelection"
    }

    # Planted-py.ini control. Both halves assert on the PLANTED path specifically: a bare "at least
    # one diversion was reported" check passes for the wrong reason on a host that already has a
    # real PY_PYTHON* / PYLAUNCHER_* value or a real user-writable py.ini (issue #1651 item 4).
    $launcherIniCanary = [System.IO.Path]::GetFullPath((Join-Path $forgedBin 'py.ini'))
    $expectedIniDiversion = "a user-writable py.ini exists at $launcherIniCanary"
    $preplantIniDiversions = @(Get-PythonLauncherDiversions `
            -LauncherPath $pythonLauncher `
            -AdditionalConfigDirectories @($forgedBin))
    if ($preplantIniDiversions -contains $expectedIniDiversion) {
        Throw-TrustFailure "py.ini control is not a regression test: $launcherIniCanary was reported before it was planted."
    }
    Set-Content -LiteralPath $launcherIniCanary -Value '[defaults]' -Encoding ASCII
    try {
        $plantedIniDiversions = @(Get-PythonLauncherDiversions `
                -LauncherPath $pythonLauncher `
                -AdditionalConfigDirectories @($forgedBin))
        if ($plantedIniDiversions -notcontains $expectedIniDiversion) {
            Throw-TrustFailure "Verifier did not reject a py.ini planted in a user-writable directory: $launcherIniCanary"
        }
    }
    finally {
        Remove-Item -LiteralPath $launcherIniCanary -Force
    }

    # Launcher-input mutation controls, one per gated variable (issue #1651 item 1 extends these to
    # the PYLAUNCHER_* trio). Each asserts that setting the variable produces the diversion FOR THAT
    # VARIABLE, not merely that the host has some diversion, so the control still discriminates when
    # a real one is already present. Each control must also RESTORE any pre-existing value rather
    # than clear it: clearing would erase a real diversion from the process environment before
    # Assert-TrustedPythonLauncherInputs reads it, and the verifier would report "clean" on a host
    # that is not.
    $launcherCanaryValue = '3.0-codex-path-trust-canary'
    foreach ($launcherVariable in $script:PythonLauncherSelectionVariables) {
        $launcherVariableName = $launcherVariable.Name
        $expectedEnvDiversion =
            "$launcherVariableName is set in the Process environment ('$launcherCanaryValue')"
        $previousLauncherValue = [System.Environment]::GetEnvironmentVariable(
            $launcherVariableName,
            'Process'
        )
        try {
            [System.Environment]::SetEnvironmentVariable(
                $launcherVariableName,
                $launcherCanaryValue,
                'Process'
            )
            $envDiversions = @(Get-PythonLauncherDiversions -LauncherPath $pythonLauncher)
            if ($envDiversions -notcontains $expectedEnvDiversion) {
                Throw-TrustFailure "Verifier did not reject $launcherVariableName diverting py -3 interpreter selection."
            }
        }
        finally {
            # A $null value removes the variable, which is the correct restore when it was unset.
            [System.Environment]::SetEnvironmentVariable(
                $launcherVariableName,
                $previousLauncherValue,
                'Process'
            )
        }
    }

    $expectedApplications = @(
        @{ Name = 'git';        Path = 'C:\Program Files\Git\cmd\git.exe' },
        @{ Name = 'gh';         Path = 'C:\Program Files\GitHub CLI\gh.exe' },
        @{ Name = 'docker';     Path = 'C:\Program Files\Docker\Docker\resources\bin\docker.exe' },
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
    Invoke-CheckedNative -Path 'C:\Program Files\Docker\Docker\resources\bin\docker.exe' -Arguments @('--version')
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
    # Prove the launcher's selection inputs are clean BEFORE trusting its inventory, then validate
    # and execute the resulting ABSOLUTE interpreter path with -I -B. The documented contract stays
    # `py -3`; the verifier is what proves `py -3` cannot be steered elsewhere.
    # Residual (accepted, TOCTOU): this proves the launcher inputs are clean at verification time.
    # A py.ini written into %LOCALAPPDATA%, or any of the gated launcher variables set in a shell
    # started after this run, would divert a LATER `py -3` invocation. Re-run the verifier after any
    # change to the tool environment; it is a gate on the configuration, not a runtime monitor.
    Assert-TrustedPythonLauncherInputs -LauncherPath $pythonLauncher
    $selectedPython = Get-SelectedPythonInterpreter -LauncherPath $pythonLauncher
    Assert-TrustedFile -Path $selectedPython

    $global:LASTEXITCODE = $null
    $pythonProbeOutput = @(& $selectedPython -I -B -c $pythonProbe $resolvedConfig 2>&1)
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
    if (
        -not ([System.IO.Path]::GetFullPath($pythonInfo.executable)).Equals(
            $selectedPython,
            [System.StringComparison]::OrdinalIgnoreCase
        )
    ) {
        Throw-TrustFailure "Isolated Python probe executed '$($pythonInfo.executable)', expected '$selectedPython'."
    }
    Assert-TrustedFile -Path $pythonInfo.executable
    Write-Host "OK [codex-path-trust]: py -3 selection $($pythonInfo.version) -> $($pythonInfo.executable) (absolute -I execution)"

    Write-Host "OK [codex-path-trust]: $($pathEntries.Count) existing PATH roots are protected."
    Write-Host 'OK [codex-path-trust]: untrusted Python selection was rejected before execution.'
    Write-Host 'OK [codex-path-trust]: GENERIC_ALL/GENERIC_WRITE ACEs are mapped before the weak-write evaluation.'
    Write-Host (
        'OK [codex-path-trust]: py -3 selection inputs ({0}, user-writable py.ini) are clean.' -f
            (($script:PythonLauncherSelectionVariables | ForEach-Object { $_.Name }) -join ', ')
    )
    Write-Host 'OK [codex-path-trust]: a bare drive-root PATH entry is ACL-validated, not silently accepted.'
    Write-Host "OK [codex-path-trust]: enabled-group ACL mutation and deny-only-group control passed."
    Write-Host 'OK [codex-path-trust]: forged writable-bin executables cannot participate in the configured PATH.'
    Write-Host 'OK [codex-path-trust]: jq, python, and python3 fail closed; use py -3 for Windows Python.'
}
finally {
    $env:PATH = $originalPath
    foreach ($canaryPath in @($forgedBin, $aclCanary)) {
        if (-not (Test-Path -LiteralPath $canaryPath)) {
            continue
        }
        $resolvedCanary = [System.IO.Path]::GetFullPath($canaryPath)
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
