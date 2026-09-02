<#
.SYNOPSIS
Runs one delegated read-only inventory lane under the Taskdeck checkout fingerprint guard.

.DESCRIPTION
Coordinator entry point used by the batch orchestrator skills. It captures the bounded non-ignored
status-artifact fingerprint of the checkout, runs the lane, and then - inside a `finally` block that no
lane `exit`, `break`, or `throw` can skip - compares the checkout against the fingerprint and cleans the
authenticated state file up only when the comparison is clean.

Exit-code contract:
  - a Capture failure exits with the guard's code before the lane ever runs;
  - a Compare or Cleanup failure exits with the guard's code, preserves the state file for investigation,
    reports its path on stderr, and supersedes whatever the lane returned;
  - otherwise a lane that threw rethrows, a lane that failed exits with its own nonzero code (or 1), and a
    clean successful lane exits 0.

Guarantee boundary: this is accidental-mutation accountability for a same-account lane, not a security
boundary against a hostile process. `[Environment]::Exit` or a process kill inside the lane is outside it.

Why the shape is exactly this (pinned by Test-Assert-TaskdeckCheckoutFingerprint.ps1):
  - Compare and Cleanup live in `finally`. A lane that calls `exit` (or `break`) raises a flow-control
    exception that `catch` cannot see and that skips every statement after the `try`, so finalization
    placed after the try/catch would never run. `finally` is the only control flow PowerShell still
    guarantees on those paths, and an `exit` inside `finally` supersedes the lane's in-flight exit code,
    so a guard failure can never be masked by the lane's own status.
  - `$global:LASTEXITCODE = 255` before every guard call is a fail-closed sentinel: if the guard could not
    be launched at all, `$LASTEXITCODE` would otherwise still hold the previous command's success code and
    an unguarded lane would be accepted.
  - The final gate tests only the lane's own success flag. `$LASTEXITCODE` is process-global and survives
    any native command the lane handled internally, so `$?` is the only signal that describes the lane
    itself; the saved lane exit code is consulted only once `$?` has already said the lane failed.
  - The lane error text is written to stderr before a superseding guard `exit`, because that `exit`
    unwinds past the rethrow and a lane that both threw and mutated the checkout would otherwise be
    reported only as a mutation.

The fingerprint covers HEAD's commit and symbolic ref plus exact non-ignored Git status-listed regular
files, subject to the guard's limits. It detects same-path overwrite, deletion, creation, `ref-moved`, and
`head-moved`. Any unreadable, reparse, malformed, limit, state-authentication, or checkout-identity
uncertainty fails closed.

.PARAMETER LaneCommand
The lane to run, as a script block. Typically wraps scripts/github/Invoke-TaskdeckReadOnlyInventory.ps1.

.PARAMETER CheckoutPath
Checkout to guard. Defaults to `git rev-parse --show-toplevel` of the current location.

.PARAMETER Token
One nonempty caller token authenticating the state file. Generated when omitted. Never put it, the state
payload, or its digest in a handoff.

.PARAMETER FingerprintTool
Path to Assert-TaskdeckCheckoutFingerprint.ps1. Defaults to the copy under the guarded checkout.

.EXAMPLE
& scripts/agentic/Invoke-TaskdeckGuardedLane.ps1 -LaneCommand {
    & scripts/github/Invoke-TaskdeckReadOnlyInventory.ps1 -Command @('gh', 'pr', 'list', '--state', 'open')
}
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [scriptblock]$LaneCommand,

    [string]$CheckoutPath = '',

    [string]$Token = '',

    [string]$FingerprintTool = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($CheckoutPath)) {
    $checkout = ([string](& git rev-parse --show-toplevel)).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($checkout)) {
        throw 'could not resolve the checkout to guard; pass -CheckoutPath'
    }
}
else {
    $checkout = [IO.Path]::GetFullPath($CheckoutPath)
}

if ([string]::IsNullOrWhiteSpace($FingerprintTool)) {
    $fingerprintTool = [IO.Path]::GetFullPath((Join-Path -Path $checkout -ChildPath 'scripts/agentic/Assert-TaskdeckCheckoutFingerprint.ps1'))
}
else {
    $fingerprintTool = [IO.Path]::GetFullPath($FingerprintTool)
}
if (-not [IO.Path]::IsPathRooted($fingerprintTool) -or -not (Test-Path -LiteralPath $fingerprintTool -PathType Leaf)) {
    throw 'checkout fingerprint guard path is not a valid absolute file'
}

$inventoryToken = $Token
if ([string]::IsNullOrWhiteSpace($inventoryToken)) {
    $inventoryToken = [Guid]::NewGuid().ToString('N')
}

# 255 is the fail-closed sentinel described in the header.
$global:LASTEXITCODE = 255
$capture = & $fingerprintTool -Mode Capture -CheckoutPath $checkout -Token $inventoryToken
$captureExit = $LASTEXITCODE
if ($captureExit -ne 0) { exit $captureExit }
$inventoryState = ($capture | ConvertFrom-Json).path

# Compare and Cleanup MUST stay inside `finally`; do not add a bare `exit`
# between the lane call and that block (see the header).
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
    $global:LASTEXITCODE = 255
    & $fingerprintTool -Mode Compare -CheckoutPath $checkout -Token $inventoryToken -StatePath $inventoryState
    $compareExit = $LASTEXITCODE
    if ($compareExit -ne 0) {
        $guardExit = $compareExit # preserves state for investigation
        [Console]::Error.WriteLine('Checkout fingerprint state preserved for investigation: ' + $inventoryState)
    }
    else {
        $global:LASTEXITCODE = 255
        & $fingerprintTool -Mode Cleanup -CheckoutPath $checkout -Token $inventoryToken -StatePath $inventoryState
        $cleanupExit = $LASTEXITCODE
        if ($cleanupExit -ne 0) { $guardExit = $cleanupExit }
    }
    if ($guardExit -ne 0) {
        # This `exit` unwinds the whole frame, so the statements after the
        # try/catch never run. Surface the lane exception text here.
        if ($null -ne $laneError) {
            [Console]::Error.WriteLine('Lane error superseded by guard disposition: ' + $laneError.Exception.Message)
        }
        exit $guardExit
    }
}

if ($null -ne $laneError) { throw $laneError }
if (-not $laneSucceeded) {
    # $LASTEXITCODE is only consulted when the lane itself failed: a successful
    # lane can still carry a handled native probe's stale exit code.
    if ($null -ne $laneExit -and $laneExit -ne 0) { exit $laneExit }
    exit 1
}
