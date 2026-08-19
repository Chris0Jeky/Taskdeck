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
# bits, so a raw -band against $script:WriteMask false-passes it. Windows maps generic bits when a
# DACL is applied through SetSecurityInfo, but ACEs can still reach a DACL with the generic bits
# intact (native/backup-restore writers, security templates copied verbatim, descriptors authored
# as SDDL by another resource manager), and the managed ACL reader hands back the raw AccessMask
# unexpanded. Map defensively before every mask test rather than trusting the writer.
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

$script:PythonLauncherSelectionVariables = @('PY_PYTHON', 'PY_PYTHON3')

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

    # `py -0p` enumerates installs, but execution-time selection is user-controllable: PY_PYTHON /
    # PY_PYTHON3 and a user-writable py.ini both override which interpreter `py -3` launches, so the
    # inventory-validated path would not be the one that runs. Fail closed on any such input rather
    # than trusting that the launcher will pick what the verifier just validated.
    $diversions = New-Object 'System.Collections.Generic.List[string]'

    foreach ($name in $script:PythonLauncherSelectionVariables) {
        foreach ($scope in @('Process', 'User', 'Machine')) {
            $value = [System.Environment]::GetEnvironmentVariable(
                $name,
                [System.EnvironmentVariableTarget]$scope
            )
            if (-not [string]::IsNullOrWhiteSpace($value)) {
                $diversions.Add("$name is set in the $scope environment ('$value')")
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

    $launcherIniCanary = Join-Path $forgedBin 'py.ini'
    Set-Content -LiteralPath $launcherIniCanary -Value '[defaults]' -Encoding ASCII
    $plantedIniDiversions = @(Get-PythonLauncherDiversions `
            -LauncherPath $pythonLauncher `
            -AdditionalConfigDirectories @($forgedBin))
    if ($plantedIniDiversions.Count -eq 0) {
        Throw-TrustFailure "Verifier did not reject a py.ini planted in a user-writable directory: $launcherIniCanary"
    }
    Remove-Item -LiteralPath $launcherIniCanary -Force

    # The control must RESTORE any pre-existing value, not clear it: clearing would erase a real
    # diversion from the process environment before Assert-TrustedPythonLauncherInputs reads it,
    # and the verifier would report "clean" on a host that is not.
    $previousPyPython = [System.Environment]::GetEnvironmentVariable('PY_PYTHON', 'Process')
    $env:PY_PYTHON = '3.0-codex-path-trust-canary'
    try {
        $envDiversions = @(Get-PythonLauncherDiversions -LauncherPath $pythonLauncher)
        if ($envDiversions.Count -eq 0) {
            Throw-TrustFailure 'Verifier did not reject PY_PYTHON diverting py -3 interpreter selection.'
        }
    }
    finally {
        if ([string]::IsNullOrEmpty($previousPyPython)) {
            Remove-Item -LiteralPath 'Env:PY_PYTHON' -ErrorAction SilentlyContinue
        }
        else {
            $env:PY_PYTHON = $previousPyPython
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
    # A py.ini written into %LOCALAPPDATA%, or a PY_PYTHON/PY_PYTHON3 set in a shell started after
    # this run, would divert a LATER `py -3` invocation. Re-run the verifier after any change to the
    # tool environment; it is a gate on the configuration, not a continuous runtime monitor.
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
    Write-Host 'OK [codex-path-trust]: py -3 selection inputs (PY_PYTHON, PY_PYTHON3, user-writable py.ini) are clean.'
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
