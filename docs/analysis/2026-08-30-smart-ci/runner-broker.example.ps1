<#
.SYNOPSIS
  Safe skeleton for starting/stopping isolated Taskdeck CI VMs.

.DESCRIPTION
  This script intentionally does not register a GitHub runner, request a token,
  call GitHub APIs, or contain machine secrets. Reconcile VM names and health
  checks locally. Keep it outside privileged release/signing environments.
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet('Start', 'Stop', 'Status')]
    [string]$Action = 'Status',

    [string[]]$VmNames = @('td-ci-linux', 'td-ci-windows'),

    [int]$StartupTimeoutSeconds = 180
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-RunnerVmState {
    param([Parameter(Mandatory)][string]$Name)
    $vm = Get-VM -Name $Name -ErrorAction SilentlyContinue
    if (-not $vm) {
        return [pscustomobject]@{ Name = $Name; Exists = $false; State = 'Missing' }
    }
    return [pscustomobject]@{ Name = $Name; Exists = $true; State = [string]$vm.State }
}

function Start-RunnerVm {
    param([Parameter(Mandatory)][string]$Name)
    $state = Get-RunnerVmState -Name $Name
    if (-not $state.Exists) { throw "Runner VM '$Name' does not exist." }
    if ($state.State -eq 'Running') { return }

    if ($PSCmdlet.ShouldProcess($Name, 'Start isolated CI VM')) {
        Start-VM -Name $Name | Out-Null
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds($StartupTimeoutSeconds)
        do {
            Start-Sleep -Seconds 2
            $state = Get-RunnerVmState -Name $Name
            if ($state.State -eq 'Running') { return }
        } while ([DateTimeOffset]::UtcNow -lt $deadline)
        throw "Runner VM '$Name' did not reach Running within $StartupTimeoutSeconds seconds."
    }
}

function Stop-RunnerVm {
    param([Parameter(Mandatory)][string]$Name)
    $state = Get-RunnerVmState -Name $Name
    if (-not $state.Exists -or $state.State -eq 'Off') { return }

    if ($PSCmdlet.ShouldProcess($Name, 'Request graceful stop of isolated CI VM')) {
        Stop-VM -Name $Name -Shutdown -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 10
        $state = Get-RunnerVmState -Name $Name
        if ($state.State -ne 'Off') {
            Write-Warning "VM '$Name' did not stop gracefully. Inspect active CI jobs before forcing it off."
        }
    }
}

switch ($Action) {
    'Start'  { foreach ($name in $VmNames) { Start-RunnerVm -Name $name } }
    'Stop'   { foreach ($name in $VmNames) { Stop-RunnerVm -Name $name } }
    'Status' { $VmNames | ForEach-Object { Get-RunnerVmState -Name $_ } | Format-Table -AutoSize }
}
