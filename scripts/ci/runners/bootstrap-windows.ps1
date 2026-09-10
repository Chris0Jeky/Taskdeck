<#
.SYNOPSIS
Checks or applies the fixed Taskdeck Windows runner image contract.

.DESCRIPTION
Check is the non-mutating default. Apply only creates the fixed guest-local directory, policy and
hook layout. Toolchain installation and runner association remain separate maintainer operations.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [ValidateSet('Check', 'Apply')]
    [string]$Action = 'Check'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RunnerAccount = 'taskdeck-runner'
$PolicyContainer = 'C:\ProgramData\TaskdeckRunner'
$PolicyRoot = 'C:\ProgramData\TaskdeckRunner\Policy'
$HookRoot = 'C:\ProgramData\TaskdeckRunner\Policy\Hooks'
$ConfigPath = 'C:\ProgramData\TaskdeckRunner\Policy\RunnerPolicy.psd1'
$HookPath = 'C:\ProgramData\TaskdeckRunner\Policy\Hooks\cleanup-windows.ps1'
$StateRoot = 'C:\TaskdeckRunner'
$AccountProfileRoot = 'C:\TaskdeckRunner\Profile'
$ProfileStateRoot = 'C:\TaskdeckRunner\ProfileState'
$WorkRoot = 'C:\TaskdeckRunner\Work'
$TempRoot = 'C:\TaskdeckRunner\Temp'
$CacheRoot = 'C:\TaskdeckRunner\Cache'
$NpmCacheRoot = 'C:\TaskdeckRunner\Cache\npm'
$NugetCacheRoot = 'C:\TaskdeckRunner\Cache\nuget'
$PlaywrightCacheRoot = 'C:\TaskdeckRunner\Cache\playwright'

$SystemSid = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
$AdministratorsSid = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
$UsersSid = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-545')

function Write-ContractEvent {
    param([Parameter(Mandatory = $true)][string]$Text)
    Write-Output "RUNNER_BOOTSTRAP $Text"
}

function Stop-Contract {
    param([Parameter(Mandatory = $true)][string]$Code)
    throw [InvalidOperationException]::new("contract:$Code")
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-GuestLocalPath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [switch]$MayNotExist,
        [switch]$AllowUserProfile
    )

    if ($Path -notmatch '^[A-Za-z]:\\' -or $Path.StartsWith('\\')) {
        Stop-Contract 'path_not_local_absolute'
    }

    $fullPath = [IO.Path]::GetFullPath($Path)
    $pathRoot = [IO.Path]::GetPathRoot($fullPath)
    if ($fullPath.TrimEnd('\') -eq $pathRoot.TrimEnd('\')) {
        Stop-Contract 'path_is_filesystem_root'
    }
    if (-not $AllowUserProfile -and
        ($fullPath.StartsWith('C:\Users\', [StringComparison]::OrdinalIgnoreCase) -or
            $fullPath.StartsWith('C:\Documents and Settings\', [StringComparison]::OrdinalIgnoreCase))) {
        Stop-Contract 'path_is_user_profile'
    }

    $drive = [IO.DriveInfo]::new($pathRoot)
    if ($drive.DriveType -ne [IO.DriveType]::Fixed) {
        Stop-Contract 'path_drive_not_fixed'
    }

    $current = $pathRoot
    $relative = $fullPath.Substring($pathRoot.Length)
    foreach ($segment in $relative.Split([char]'\', [StringSplitOptions]::RemoveEmptyEntries)) {
        $current = Join-Path -Path $current -ChildPath $segment
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                Stop-Contract 'path_reparse'
            }
        }
        elseif (-not $MayNotExist) {
            Stop-Contract 'path_missing'
        }
    }
}

function Get-RunnerIdentity {
    try {
        $account = Get-LocalUser -Name $RunnerAccount -ErrorAction Stop
    }
    catch {
        Stop-Contract 'account_missing'
    }
    if (-not $account.Enabled -or $null -eq $account.SID -or $account.SID.Value.EndsWith('-500')) {
        Stop-Contract 'account_invalid'
    }

    try {
        $memberships = @(
            foreach ($group in @(Get-LocalGroup -ErrorAction Stop)) {
                $members = @(Get-LocalGroupMember -Group $group -ErrorAction Stop)
                foreach ($member in $members) {
                    if ($null -eq $member.SID -or $null -eq $group.SID) {
                        Stop-Contract 'account_group_unverifiable'
                    }
                    if ($member.SID.Value -eq $account.SID.Value) {
                        $group.SID.Value
                        break
                    }
                }
            }
        )
    }
    catch {
        Stop-Contract 'account_group_unverifiable'
    }
    if ($memberships.Count -ne 1 -or $memberships[0] -ne $UsersSid.Value) {
        Stop-Contract 'account_group_not_permitted'
    }

    $profileKey = "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\$($account.SID.Value)"
    try {
        $profilePath = [string](Get-ItemPropertyValue -LiteralPath $profileKey -Name 'ProfileImagePath' -ErrorAction Stop)
    }
    catch {
        Stop-Contract 'account_profile_unverifiable'
    }
    if (-not $profilePath.Equals($AccountProfileRoot, [StringComparison]::OrdinalIgnoreCase)) {
        Stop-Contract 'account_profile_root'
    }
    return $account.SID
}

function Assert-Toolchain {
    $nodePath = 'C:\Program Files\nodejs\node.exe'
    $dotnetPath = 'C:\Program Files\dotnet\dotnet.exe'
    $gitPath = 'C:\Program Files\Git\cmd\git.exe'

    foreach ($toolPath in @($nodePath, $dotnetPath, $gitPath)) {
        Assert-GuestLocalPath -Path $toolPath
    }

    $nodeVersion = (& $nodePath --version 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $nodeVersion -ne 'v24.13.1') {
        Stop-Contract 'node_version'
    }
    $nodeArchitecture = (& $nodePath -p 'process.arch' 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $nodeArchitecture -ne 'x64') {
        Stop-Contract 'node_architecture'
    }

    $dotnetVersion = (& $dotnetPath --version 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $dotnetVersion -ne '8.0.415') {
        Stop-Contract 'dotnet_version'
    }
    $dotnetInfo = (& $dotnetPath --info 2>$null | Out-String)
    if ($LASTEXITCODE -ne 0 -or $dotnetInfo -notmatch '(?m)^ RID:\s+win-x64\s*$') {
        Stop-Contract 'dotnet_architecture'
    }

    $gitVersion = (& $gitPath --version 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $gitVersion -notmatch '^git version \d+\.\d+') {
        Stop-Contract 'git_version'
    }
    $gitBuild = (& $gitPath version --build-options 2>$null | Out-String)
    if ($LASTEXITCODE -ne 0 -or $gitBuild -notmatch '(?m)^cpu:\s+x86_64\s*$') {
        Stop-Contract 'git_architecture'
    }

    try {
        $nativeArchitecture = [string](Get-ItemPropertyValue -LiteralPath 'Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Environment' -Name 'PROCESSOR_ARCHITECTURE' -ErrorAction Stop)
    }
    catch {
        Stop-Contract 'operating_system_probe'
    }
    if ($nativeArchitecture -ne 'AMD64' -or -not [Environment]::Is64BitProcess) {
        Stop-Contract 'operating_system_not_x64'
    }
}

function New-PolicyDirectorySecurity {
    param(
        [Parameter(Mandatory = $true)][Security.Principal.SecurityIdentifier]$RunnerSid,
        [Parameter(Mandatory = $true)][bool]$RunnerWritable
    )

    $security = [Security.AccessControl.DirectorySecurity]::new()
    $security.SetOwner($AdministratorsSid)
    $security.SetAccessRuleProtection($true, $false)
    $inheritance = [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
    $propagation = [Security.AccessControl.PropagationFlags]::None
    $allow = [Security.AccessControl.AccessControlType]::Allow

    foreach ($sid in @($SystemSid, $AdministratorsSid)) {
        $rule = [Security.AccessControl.FileSystemAccessRule]::new(
            $sid,
            [Security.AccessControl.FileSystemRights]::FullControl,
            $inheritance,
            $propagation,
            $allow)
        [void]$security.AddAccessRule($rule)
    }

    if ($RunnerWritable) {
        $rootRights = [Security.AccessControl.FileSystemRights]::ReadAndExecute -bor
            [Security.AccessControl.FileSystemRights]::Write -bor
            [Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles
        $rootRule = [Security.AccessControl.FileSystemAccessRule]::new(
            $RunnerSid,
            $rootRights,
            [Security.AccessControl.InheritanceFlags]::None,
            [Security.AccessControl.PropagationFlags]::None,
            $allow)
        [void]$security.AddAccessRule($rootRule)

        $childRule = [Security.AccessControl.FileSystemAccessRule]::new(
            $RunnerSid,
            [Security.AccessControl.FileSystemRights]::Modify,
            $inheritance,
            [Security.AccessControl.PropagationFlags]::InheritOnly,
            $allow)
        [void]$security.AddAccessRule($childRule)
    }
    else {
        $runnerRule = [Security.AccessControl.FileSystemAccessRule]::new(
            $RunnerSid,
            [Security.AccessControl.FileSystemRights]::ReadAndExecute,
            $inheritance,
            $propagation,
            $allow)
        [void]$security.AddAccessRule($runnerRule)
    }
    return $security
}

function New-PolicyFileSecurity {
    param([Parameter(Mandatory = $true)][Security.Principal.SecurityIdentifier]$RunnerSid)

    $security = [Security.AccessControl.FileSecurity]::new()
    $security.SetOwner($AdministratorsSid)
    $security.SetAccessRuleProtection($true, $false)
    $allow = [Security.AccessControl.AccessControlType]::Allow
    foreach ($sid in @($SystemSid, $AdministratorsSid)) {
        $rule = [Security.AccessControl.FileSystemAccessRule]::new(
            $sid,
            [Security.AccessControl.FileSystemRights]::FullControl,
            $allow)
        [void]$security.AddAccessRule($rule)
    }
    $runnerRule = [Security.AccessControl.FileSystemAccessRule]::new(
        $RunnerSid,
        [Security.AccessControl.FileSystemRights]::ReadAndExecute,
        $allow)
    [void]$security.AddAccessRule($runnerRule)
    return $security
}

function Assert-ProtectedAcl {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][Security.Principal.SecurityIdentifier]$RunnerSid,
        [Parameter(Mandatory = $true)][bool]$RunnerWritable
    )

    $acl = Get-Acl -LiteralPath $Path
    if (-not $acl.AreAccessRulesProtected) {
        Stop-Contract 'acl_inheritance'
    }
    $owner = $acl.GetOwner([Security.Principal.SecurityIdentifier])
    if ($owner.Value -notin @($SystemSid.Value, $AdministratorsSid.Value)) {
        Stop-Contract 'acl_owner'
    }

    $allowedSids = @($SystemSid.Value, $AdministratorsSid.Value, $RunnerSid.Value)
    $rules = @($acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]))
    if ($rules.Count -lt 3 -or $rules.Where({ $_.IsInherited }).Count -ne 0) {
        Stop-Contract 'acl_rules'
    }
    $runnerRuleSeen = $false
    $runnerCanWrite = $false
    foreach ($rule in $rules) {
        if ($rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow -or
            $rule.IdentityReference.Value -notin $allowedSids) {
            Stop-Contract 'acl_unexpected_principal'
        }
        if ($rule.IdentityReference.Value -eq $RunnerSid.Value) {
            $runnerRuleSeen = $true
            $writeMask = [Security.AccessControl.FileSystemRights]::Write -bor
                [Security.AccessControl.FileSystemRights]::Delete -bor
                [Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor
                [Security.AccessControl.FileSystemRights]::ChangePermissions -bor
                [Security.AccessControl.FileSystemRights]::TakeOwnership
            if (($rule.FileSystemRights -band $writeMask) -ne 0) {
                $runnerCanWrite = $true
            }
            if ($RunnerWritable -and
                $rule.PropagationFlags -ne [Security.AccessControl.PropagationFlags]::InheritOnly -and
                ($rule.FileSystemRights -band [Security.AccessControl.FileSystemRights]::Delete) -ne 0) {
                Stop-Contract 'acl_runner_can_delete_root'
            }
        }
    }
    if (-not $runnerRuleSeen -or $runnerCanWrite -ne $RunnerWritable) {
        Stop-Contract 'acl_runner_rights'
    }
}

function Assert-Layout {
    param([Parameter(Mandatory = $true)][Security.Principal.SecurityIdentifier]$RunnerSid)

    foreach ($path in @($PolicyContainer, $PolicyRoot, $HookRoot, $ConfigPath, $HookPath, $StateRoot, $AccountProfileRoot, $ProfileStateRoot, $WorkRoot, $TempRoot,
            $CacheRoot, $NpmCacheRoot, $NugetCacheRoot, $PlaywrightCacheRoot)) {
        Assert-GuestLocalPath -Path $path
    }

    foreach ($path in @($PolicyContainer, $PolicyRoot, $HookRoot, $ConfigPath, $HookPath, $StateRoot, $CacheRoot)) {
        Assert-ProtectedAcl -Path $path -RunnerSid $RunnerSid -RunnerWritable $false
    }
    foreach ($path in @($ProfileStateRoot, $WorkRoot, $TempRoot, $NpmCacheRoot, $NugetCacheRoot, $PlaywrightCacheRoot)) {
        Assert-ProtectedAcl -Path $path -RunnerSid $RunnerSid -RunnerWritable $true
    }

    $policy = Import-PowerShellDataFile -LiteralPath $ConfigPath
    $expected = [ordered]@{
        PolicyVersion = 1
        AccountProfileRoot = $AccountProfileRoot
        ProfileStateRoot = $ProfileStateRoot
        WorkRoot = $WorkRoot
        TempRoot = $TempRoot
        NpmCacheRoot = $NpmCacheRoot
        NugetCacheRoot = $NugetCacheRoot
        PlaywrightCacheRoot = $PlaywrightCacheRoot
    }
    if ($policy.Count -ne $expected.Count) {
        Stop-Contract 'policy_incomplete'
    }
    foreach ($key in $expected.Keys) {
        if (-not $policy.ContainsKey($key) -or $policy[$key] -ne $expected[$key]) {
            Stop-Contract 'policy_value'
        }
    }
}

function Apply-Layout {
    param([Parameter(Mandatory = $true)][Security.Principal.SecurityIdentifier]$RunnerSid)

    $cleanupSource = Join-Path -Path $PSScriptRoot -ChildPath 'cleanup-windows.ps1'
    Assert-GuestLocalPath -Path $cleanupSource -AllowUserProfile
    if (-not (Test-Path -LiteralPath $cleanupSource -PathType Leaf)) {
        Stop-Contract 'cleanup_source'
    }

    foreach ($path in @($PolicyContainer, $PolicyRoot, $HookRoot, $StateRoot, $ProfileStateRoot, $WorkRoot, $TempRoot, $CacheRoot,
            $NpmCacheRoot, $NugetCacheRoot, $PlaywrightCacheRoot)) {
        [void](New-Item -ItemType Directory -Path $path -Force -ErrorAction Stop)
    }

    foreach ($path in @($PolicyContainer, $PolicyRoot, $HookRoot, $StateRoot, $CacheRoot)) {
        Set-Acl -LiteralPath $path -AclObject (New-PolicyDirectorySecurity -RunnerSid $RunnerSid -RunnerWritable $false)
    }
    foreach ($path in @($ProfileStateRoot, $WorkRoot, $TempRoot, $NpmCacheRoot, $NugetCacheRoot, $PlaywrightCacheRoot)) {
        Set-Acl -LiteralPath $path -AclObject (New-PolicyDirectorySecurity -RunnerSid $RunnerSid -RunnerWritable $true)
    }

    Copy-Item -LiteralPath $cleanupSource -Destination $HookPath -Force -ErrorAction Stop
    $configStage = Join-Path -Path $PolicyRoot -ChildPath 'RunnerPolicy.stage'
    try {
        [IO.File]::WriteAllLines($configStage, @(
                '@{',
                '    PolicyVersion = 1',
                "    AccountProfileRoot = '$AccountProfileRoot'",
                "    ProfileStateRoot = '$ProfileStateRoot'",
                "    WorkRoot = '$WorkRoot'",
                "    TempRoot = '$TempRoot'",
                "    NpmCacheRoot = '$NpmCacheRoot'",
                "    NugetCacheRoot = '$NugetCacheRoot'",
                "    PlaywrightCacheRoot = '$PlaywrightCacheRoot'",
                '}'
            ), [Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath $configStage -Destination $ConfigPath -Force -ErrorAction Stop
    }
    finally {
        if (Test-Path -LiteralPath $configStage) {
            Remove-Item -LiteralPath $configStage -Force -ErrorAction Stop
        }
    }

    foreach ($path in @($ConfigPath, $HookPath)) {
        Set-Acl -LiteralPath $path -AclObject (New-PolicyFileSecurity -RunnerSid $RunnerSid)
    }
}

function Invoke-Main {
    foreach ($path in @($PolicyContainer, $PolicyRoot, $HookRoot, $ConfigPath, $HookPath, $StateRoot, $AccountProfileRoot, $ProfileStateRoot, $WorkRoot, $TempRoot,
            $CacheRoot, $NpmCacheRoot, $NugetCacheRoot, $PlaywrightCacheRoot)) {
        Assert-GuestLocalPath -Path $path -MayNotExist
    }
    Assert-Toolchain
    $runnerSid = Get-RunnerIdentity
    Assert-GuestLocalPath -Path $AccountProfileRoot

    if ($Action -eq 'Apply') {
        if (-not (Test-IsAdministrator)) {
            Stop-Contract 'apply_requires_administrator'
        }
        if (-not $PSCmdlet.ShouldProcess('runner-image-policy', 'Apply fixed runner image contract')) {
            Write-ContractEvent 'OK code=whatif_complete'
            return
        }
        Apply-Layout -RunnerSid $runnerSid
    }

    Assert-Layout -RunnerSid $runnerSid
    Write-ContractEvent 'OK code=verified'
    Write-ContractEvent 'ACTION code=playwright_repository_job_proof_required'
}

try {
    Invoke-Main
}
catch {
    $code = 'unexpected'
    if ($_.Exception.Message -match '^contract:([a-z0-9_]+)$') {
        $code = $Matches[1]
    }
    Write-ContractEvent "ERROR code=$code"
    exit 1
}
