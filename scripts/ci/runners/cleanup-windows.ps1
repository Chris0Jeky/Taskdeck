<#
.SYNOPSIS
Clears bounded Taskdeck Windows runner state from immutable guest policy.

.DESCRIPTION
The fixed hook starts one background worker and gives it 120 seconds. It never accepts deletion
roots from arguments or process state, and reports only stable action codes and counts.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ConfigPath = 'C:\ProgramData\TaskdeckRunner\Policy\RunnerPolicy.psd1'
$HookPath = 'C:\ProgramData\TaskdeckRunner\Policy\Hooks\cleanup-windows.ps1'

function Write-CleanupEvent {
    param([Parameter(Mandatory = $true)][string]$Text)
    Write-Output "RUNNER_CLEANUP $Text"
}

function Stop-Cleanup {
    param([Parameter(Mandatory = $true)][string]$Code)
    throw [InvalidOperationException]::new("cleanup:$Code")
}

function Assert-InstalledHookPath {
    $invokedPath = [IO.Path]::GetFullPath($PSCommandPath)
    if (-not $invokedPath.Equals($HookPath, [StringComparison]::OrdinalIgnoreCase)) {
        Stop-Cleanup 'hook_path'
    }
    $hookItem = Get-Item -LiteralPath $HookPath -Force -ErrorAction Stop
    if (($hookItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        Stop-Cleanup 'hook_reparse'
    }
}

$worker = {
    param([Parameter(Mandatory = $true)][bool]$DryRun)

    Set-StrictMode -Version Latest
    $ErrorActionPreference = 'Stop'

    $runnerAccount = 'taskdeck-runner'
    $policyContainer = 'C:\ProgramData\TaskdeckRunner'
    $policyRoot = 'C:\ProgramData\TaskdeckRunner\Policy'
    $hookRoot = 'C:\ProgramData\TaskdeckRunner\Policy\Hooks'
    $configPath = 'C:\ProgramData\TaskdeckRunner\Policy\RunnerPolicy.psd1'
    $hookPath = 'C:\ProgramData\TaskdeckRunner\Policy\Hooks\cleanup-windows.ps1'
    $stateRoot = 'C:\TaskdeckRunner'
    $accountProfileRoot = 'C:\TaskdeckRunner\Profile'
    $profileStateRoot = 'C:\TaskdeckRunner\ProfileState'
    $workRoot = 'C:\TaskdeckRunner\Work'
    $tempRoot = 'C:\TaskdeckRunner\Temp'
    $cacheRoot = 'C:\TaskdeckRunner\Cache'
    $npmCacheRoot = 'C:\TaskdeckRunner\Cache\npm'
    $nugetCacheRoot = 'C:\TaskdeckRunner\Cache\nuget'
    $playwrightCacheRoot = 'C:\TaskdeckRunner\Cache\playwright'
    $systemSid = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
    $administratorsSid = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
    $usersSid = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-545')

    function Stop-Worker {
        param([Parameter(Mandatory = $true)][string]$Code)
        throw [InvalidOperationException]::new("worker:$Code")
    }

    function Assert-GuestLocalPath {
        param([Parameter(Mandatory = $true)][string]$Path)

        if ($Path -notmatch '^[A-Za-z]:\\' -or $Path.StartsWith('\\')) {
            Stop-Worker 'path_not_local_absolute'
        }
        $fullPath = [IO.Path]::GetFullPath($Path)
        $pathRoot = [IO.Path]::GetPathRoot($fullPath)
        if ($fullPath.TrimEnd('\') -eq $pathRoot.TrimEnd('\')) {
            Stop-Worker 'path_is_filesystem_root'
        }
        if ($fullPath.StartsWith('C:\Users\', [StringComparison]::OrdinalIgnoreCase) -or
            $fullPath.StartsWith('C:\Documents and Settings\', [StringComparison]::OrdinalIgnoreCase)) {
            Stop-Worker 'path_is_user_profile'
        }
        $drive = [IO.DriveInfo]::new($pathRoot)
        if ($drive.DriveType -ne [IO.DriveType]::Fixed) {
            Stop-Worker 'path_drive_not_fixed'
        }

        $current = $pathRoot
        $relative = $fullPath.Substring($pathRoot.Length)
        foreach ($segment in $relative.Split([char]'\', [StringSplitOptions]::RemoveEmptyEntries)) {
            $current = Join-Path -Path $current -ChildPath $segment
            if (-not (Test-Path -LiteralPath $current)) {
                Stop-Worker 'path_missing'
            }
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                Stop-Worker 'path_reparse'
            }
        }
    }

    function Assert-ProtectedAcl {
        param(
            [Parameter(Mandatory = $true)][string]$Path,
            [Parameter(Mandatory = $true)][Security.Principal.SecurityIdentifier]$RunnerSid,
            [Parameter(Mandatory = $true)][bool]$RunnerWritable
        )

        $acl = Get-Acl -LiteralPath $Path
        if (-not $acl.AreAccessRulesProtected) {
            Stop-Worker 'acl_inheritance'
        }
        $owner = $acl.GetOwner([Security.Principal.SecurityIdentifier])
        if ($owner.Value -notin @($systemSid.Value, $administratorsSid.Value)) {
            Stop-Worker 'acl_owner'
        }

        $allowedSids = @($systemSid.Value, $administratorsSid.Value, $RunnerSid.Value)
        $rules = @($acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]))
        if ($rules.Count -lt 3 -or $rules.Where({ $_.IsInherited }).Count -ne 0) {
            Stop-Worker 'acl_rules'
        }
        $runnerRuleSeen = $false
        $runnerCanWrite = $false
        foreach ($rule in $rules) {
            if ($rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow -or
                $rule.IdentityReference.Value -notin $allowedSids) {
                Stop-Worker 'acl_unexpected_principal'
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
                    Stop-Worker 'acl_runner_can_delete_root'
                }
            }
        }
        if (-not $runnerRuleSeen -or $runnerCanWrite -ne $RunnerWritable) {
            Stop-Worker 'acl_runner_rights'
        }
    }

    function Clear-Children {
        param(
            [Parameter(Mandatory = $true)][string]$Root,
            [Parameter(Mandatory = $true)][string]$ActionCode
        )

        $children = @(Get-ChildItem -LiteralPath $Root -Force -ErrorAction Stop)
        foreach ($item in $children) {
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                Stop-Worker 'child_reparse'
            }
        }
        if (-not $DryRun) {
            foreach ($item in $children) {
                Remove-Item -LiteralPath $item.FullName -Recurse -Force -ErrorAction Stop
            }
            if (@(Get-ChildItem -LiteralPath $Root -Force -ErrorAction Stop).Count -ne 0) {
                Stop-Worker 'clear_incomplete'
            }
        }
        return "ACTION code=$ActionCode count=$($children.Count)"
    }

    try {
        foreach ($path in @($policyContainer, $policyRoot, $hookRoot, $configPath, $hookPath,
                $stateRoot, $accountProfileRoot, $profileStateRoot, $workRoot, $tempRoot, $cacheRoot, $npmCacheRoot,
                $nugetCacheRoot, $playwrightCacheRoot)) {
            Assert-GuestLocalPath -Path $path
        }

        try {
            $account = Get-LocalUser -Name $runnerAccount -ErrorAction Stop
        }
        catch {
            Stop-Worker 'account_missing'
        }
        if ($null -eq $account.SID) {
            Stop-Worker 'account_missing'
        }
        $currentSid = [Security.Principal.WindowsIdentity]::GetCurrent().User
        if ($currentSid.Value -ne $account.SID.Value) {
            Stop-Worker 'runner_identity'
        }

        try {
            $memberships = @(
                foreach ($group in @(Get-LocalGroup -ErrorAction Stop)) {
                    $members = @(Get-LocalGroupMember -Group $group -ErrorAction Stop)
                    foreach ($member in $members) {
                        if ($null -eq $member.SID -or $null -eq $group.SID) {
                            Stop-Worker 'account_group_unverifiable'
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
            Stop-Worker 'account_group_unverifiable'
        }
        if ($memberships.Count -ne 1 -or $memberships[0] -ne $usersSid.Value) {
            Stop-Worker 'account_group_not_permitted'
        }

        $profileKey = "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\$($account.SID.Value)"
        try {
            $profilePath = [string](Get-ItemPropertyValue -LiteralPath $profileKey -Name 'ProfileImagePath' -ErrorAction Stop)
        }
        catch {
            Stop-Worker 'account_profile_unverifiable'
        }
        if (-not $profilePath.Equals($accountProfileRoot, [StringComparison]::OrdinalIgnoreCase)) {
            Stop-Worker 'account_profile_root'
        }

        foreach ($path in @($policyContainer, $policyRoot, $hookRoot, $configPath, $hookPath, $stateRoot, $cacheRoot)) {
            Assert-ProtectedAcl -Path $path -RunnerSid $account.SID -RunnerWritable $false
        }
        foreach ($path in @($profileStateRoot, $workRoot, $tempRoot, $npmCacheRoot, $nugetCacheRoot, $playwrightCacheRoot)) {
            Assert-ProtectedAcl -Path $path -RunnerSid $account.SID -RunnerWritable $true
        }

        $policy = Import-PowerShellDataFile -LiteralPath $configPath
        $expected = [ordered]@{
            PolicyVersion = 1
            AccountProfileRoot = $accountProfileRoot
            ProfileStateRoot = $profileStateRoot
            WorkRoot = $workRoot
            TempRoot = $tempRoot
            NpmCacheRoot = $npmCacheRoot
            NugetCacheRoot = $nugetCacheRoot
            PlaywrightCacheRoot = $playwrightCacheRoot
        }
        if ($policy.Count -ne $expected.Count) {
            Stop-Worker 'policy_incomplete'
        }
        foreach ($key in $expected.Keys) {
            if (-not $policy.ContainsKey($key) -or $policy[$key] -ne $expected[$key]) {
                Stop-Worker 'policy_value'
            }
        }

        $events = @(
            Clear-Children -Root $profileStateRoot -ActionCode 'profile_state_clear'
            Clear-Children -Root $workRoot -ActionCode 'work_clear'
            Clear-Children -Root $tempRoot -ActionCode 'temp_clear'
            Clear-Children -Root $npmCacheRoot -ActionCode 'npm_clear'
            Clear-Children -Root $nugetCacheRoot -ActionCode 'nuget_clear'
            Clear-Children -Root $playwrightCacheRoot -ActionCode 'playwright_clear'
        )
        [pscustomobject]@{ Success = $true; Code = 'complete'; Events = $events }
    }
    catch {
        $code = 'unexpected'
        if ($_.Exception.Message -match '^worker:([a-z0-9_]+)$') {
            $code = $Matches[1]
        }
        [pscustomobject]@{ Success = $false; Code = $code; Events = @() }
    }
}

$job = $null
try {
    Assert-InstalledHookPath
    $job = Start-Job -ScriptBlock $worker -ArgumentList @([bool]$WhatIfPreference) -ErrorAction Stop
    $completed = Wait-Job -Job $job -Timeout 120 -ErrorAction Stop
    if ($null -eq $completed) {
        Stop-Job -Job $job -ErrorAction Stop
        Stop-Cleanup 'timeout'
    }
    if ($job.State -ne [Management.Automation.JobState]::Completed) {
        Stop-Cleanup 'worker_state'
    }

    $result = @(Receive-Job -Job $job -ErrorAction Stop)
    if ($result.Count -ne 1 -or $null -eq $result[0].Success) {
        Stop-Cleanup 'worker_result'
    }
    if (-not $result[0].Success) {
        Stop-Cleanup ([string]$result[0].Code)
    }
    foreach ($event in @($result[0].Events)) {
        Write-CleanupEvent ([string]$event)
    }
    Write-CleanupEvent 'OK code=complete'
}
catch {
    $code = 'unexpected'
    if ($_.Exception.Message -match '^cleanup:([a-z0-9_]+)$') {
        $code = $Matches[1]
    }
    Write-CleanupEvent "ERROR code=$code"
    exit 1
}
finally {
    if ($null -ne $job) {
        try {
            if ($job.State -eq [Management.Automation.JobState]::Running) {
                Stop-Job -Job $job -ErrorAction Stop
            }
            Remove-Job -Job $job -Force -ErrorAction Stop
        }
        catch {
            Write-CleanupEvent 'ERROR code=job_cleanup'
            exit 1
        }
    }
}
