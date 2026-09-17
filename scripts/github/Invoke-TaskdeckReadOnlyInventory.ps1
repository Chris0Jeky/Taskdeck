[CmdletBinding(DefaultParameterSetName = "Run")]
param(
    [Parameter(Mandatory = $true, ParameterSetName = "Run")]
    [ValidateNotNullOrEmpty()]
    [string[]]$Command,

    [Parameter(ParameterSetName = "Run")]
    [switch]$ValidateOnly,

    [Parameter(Mandatory = $true, ParameterSetName = "SelfTest")]
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Windows PowerShell and PowerShell 7 can bind an empty string passed to a managed
# string parameter as null. Keep that conversion outside the P/Invoke boundary:
# the managed wrapper reconstructs String.Empty before calling the native setter.
if ($null -eq ("TaskdeckInventoryNativeEnvironment" -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class TaskdeckInventoryNativeEnvironment
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetEnvironmentVariableW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowsNative(string name, string value);

    public static bool SetWindows(string name, string value)
    {
        return SetWindowsNative(name, value ?? string.Empty);
    }

    [DllImport("libc", CharSet = CharSet.Ansi, SetLastError = true, EntryPoint = "setenv")]
    private static extern int SetUnixNative(string name, string value, int overwrite);

    public static int SetUnix(string name, string value, int overwrite)
    {
        return SetUnixNative(name, value ?? string.Empty, overwrite);
    }
}
"@
}

$implementation = Join-Path $PSScriptRoot "Invoke-TaskdeckReadOnlyInventory.Core.ps1"
if (-not (Test-Path -LiteralPath $implementation -PathType Leaf)) {
    throw "Read-only inventory implementation is missing: $implementation"
}

& $implementation @PSBoundParameters
