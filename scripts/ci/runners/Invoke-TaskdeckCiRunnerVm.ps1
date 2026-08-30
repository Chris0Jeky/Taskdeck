<#
.SYNOPSIS
  Start, stop or inspect the isolated Taskdeck CI runner VMs (Hyper-V) on demand.

.DESCRIPTION
  ADR-0066 / CI-04 (#2328). The self-hosted runners are an isolated execution plane
  for trusted heavy jobs, started before a PR is qualified and stopped afterwards.
  This broker deliberately does NOT register a GitHub Actions runner, request or
  store a registration token, call the GitHub API, mount host drives, or hold any
  secret. Runner registration is a maintainer action (OUTSTANDING_TASKS.md §J SC-7)
  performed inside the VM after the repository is private.

  VM names are parameters with placeholder defaults; reconcile them with the local
  Hyper-V inventory. Keep this script outside privileged release/signing contexts.

.PARAMETER Action
  Start | Stop | Status (default Status).

.PARAMETER VmNames
  Hyper-V VM names. Defaults: td-ci-linux, td-ci-windows.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts/ci/runners/Invoke-TaskdeckCiRunnerVm.ps1 -Action Status
.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts/ci/runners/Invoke-TaskdeckCiRunnerVm.ps1 -Action Start -VmNames td-ci-linux -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [ValidateSet('Start', 'Stop', 'Status')]
    [string]$Action = 'Status',

    [string[]]$VmNames = @('td-ci-linux', 'td-ci-windows'),

    [ValidateRange(10, 900)]
    [int]$StartupTimeoutSeconds = 180,

    [ValidateRange(10, 900)]
    [int]$ShutdownTimeoutSeconds = 120
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Get-Command -Name Get-VM -ErrorAction SilentlyContinue)) {
    throw 'Hyper-V PowerShell module is not available. Enable the Hyper-V management tools before using this broker.'
}

foreach ($name in $VmNames) {
    if ($name -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$') {
        throw "Refusing VM name '$name': only letters, digits, '.', '_' and '-' are accepted."
    }
}

function Get-RunnerVmState {
    param([Parameter(Mandatory = $true)][string]$Name)
    $vm = Get-VM -Name $Name -ErrorAction SilentlyContinue
    if ($null -eq $vm) {
        return [pscustomobject]@{ Name = $Name; Exists = $false; State = 'Missing'; Uptime = $null }
    }
    return [pscustomobject]@{ Name = $Name; Exists = $true; State = [string]$vm.State; Uptime = $vm.Uptime }
}

function Wait-ForRunnerVmState {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Expected,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds
    )
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        Start-Sleep -Seconds 2
        $state = Get-RunnerVmState -Name $Name
        if ($state.State -eq $Expected) { return $true }
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    return $false
}

function Start-RunnerVm {
    param([Parameter(Mandatory = $true)][string]$Name)
    $state = Get-RunnerVmState -Name $Name
    if (-not $state.Exists) { throw "Runner VM '$Name' does not exist. Create it from the golden image first (scripts/ci/runners/README.md)." }
    if ($state.State -eq 'Running') {
        Write-Host "VM '$Name' is already running."
        return
    }
    if ($PSCmdlet.ShouldProcess($Name, 'Start isolated CI runner VM')) {
        Start-VM -Name $Name | Out-Null
        if (-not (Wait-ForRunnerVmState -Name $Name -Expected 'Running' -TimeoutSeconds $StartupTimeoutSeconds)) {
            throw "Runner VM '$Name' did not reach Running within $StartupTimeoutSeconds seconds."
        }
        Write-Host "VM '$Name' is running. The runner service inside it connects outward to GitHub; nothing on the host is exposed."
    }
}

function Stop-RunnerVm {
    param([Parameter(Mandatory = $true)][string]$Name)
    $state = Get-RunnerVmState -Name $Name
    if (-not $state.Exists -or $state.State -eq 'Off') {
        Write-Host "VM '$Name' is already off or missing."
        return
    }
    if ($PSCmdlet.ShouldProcess($Name, 'Request graceful shutdown of the isolated CI runner VM')) {
        Stop-VM -Name $Name -Shutdown -Force:$false -ErrorAction SilentlyContinue
        if (-not (Wait-ForRunnerVmState -Name $Name -Expected 'Off' -TimeoutSeconds $ShutdownTimeoutSeconds)) {
            Write-Warning "VM '$Name' did not stop gracefully within $ShutdownTimeoutSeconds seconds. A CI job may still be running — inspect the Actions queue before forcing it off; a forced stop loses the job, never the repository."
        }
        else {
            Write-Host "VM '$Name' is off."
        }
    }
}

switch ($Action) {
    'Start' { foreach ($name in $VmNames) { Start-RunnerVm -Name $name } }
    'Stop' { foreach ($name in $VmNames) { Stop-RunnerVm -Name $name } }
    'Status' { $VmNames | ForEach-Object { Get-RunnerVmState -Name $_ } | Format-Table -AutoSize }
}
