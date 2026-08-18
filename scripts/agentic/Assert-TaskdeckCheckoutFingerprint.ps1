[CmdletBinding()]
param(
    [ValidateSet('Capture', 'Compare', 'Cleanup')]
    [string]$Mode,

    [string]$CheckoutPath,

    [string]$Token,

    [string]$StatePath,

    [ValidateRange(1, 100000)]
    [int]$MaxFiles = 512,

    [ValidateRange(1, 1073741824)]
    [Int64]$MaxBytesPerFile = 10485760,

    [ValidateRange(1, 4294967296)]
    [Int64]$MaxTotalBytes = 52428800,

    [ValidateRange(1024, 16777216)]
    [Int64]$MaxGitOutputBytes = 16777216
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function ConvertTo-FingerprintRecord {
    param(
        [string]$Path,
        [string]$Classification,
        [Int64]$Count
    )

    # ConvertTo-Json escapes record-breaking filename characters. Never add free-form text here.
    return ([ordered]@{
            path = $Path
            classification = $Classification
            count = $Count
        } | ConvertTo-Json -Compress)
}

function Write-FingerprintRecord {
    param(
        [string]$Path,
        [string]$Classification,
        [Int64]$Count
    )

    [Console]::Out.WriteLine((ConvertTo-FingerprintRecord -Path $Path -Classification $Classification -Count $Count))
}

function Get-Utf8Text {
    param([byte[]]$Bytes)

    $encoding = New-Object System.Text.UTF8Encoding($false, $true)
    return $encoding.GetString($Bytes)
}

function Invoke-GitBytes {
    param(
        [string]$Checkout,
        [string]$Arguments,
        [Int64]$OutputLimit = $MaxGitOutputBytes
    )

    if ($Checkout.Contains('"')) {
        throw 'checkout identity is malformed'
    }

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = 'git'
    $startInfo.Arguments = '-C "' + $Checkout + '" ' + $Arguments
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    $output = New-Object System.IO.MemoryStream
    $started = $false
    try {
        $started = $process.Start()
        if (-not $started) {
            throw 'could not start git'
        }

        $errorTask = $process.StandardError.ReadToEndAsync()
        $buffer = New-Object byte[] 8192
        while (($read = $process.StandardOutput.BaseStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            if ($output.Length -gt ($OutputLimit - $read)) {
                try { $process.Kill() } catch { }
                throw 'git output exceeds the configured byte limit'
            }
            $output.Write($buffer, 0, $read)
        }
        $process.WaitForExit()
        [void]$errorTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            throw 'git could not establish checkout identity'
        }

        return ,$output.ToArray()
    }
    finally {
        if ($started) {
            try {
                if (-not $process.HasExited) { $process.Kill() }
            }
            catch { }
        }
        $output.Dispose()
        $process.Dispose()
    }
}

function Get-CanonicalCheckout {
    param([string]$Candidate)

    if ([string]::IsNullOrWhiteSpace($Candidate)) {
        throw 'checkout path is required'
    }

    $item = Get-Item -LiteralPath $Candidate -Force -ErrorAction Stop
    if (-not $item.PSIsContainer -or (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
        throw 'checkout identity is uncertain'
    }

    $candidateFull = [IO.Path]::GetFullPath($item.FullName).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $gitRoot = (Get-Utf8Text (Invoke-GitBytes -Checkout $candidateFull -Arguments 'rev-parse --show-toplevel')).TrimEnd("`r", "`n")
    if ([string]::IsNullOrWhiteSpace($gitRoot)) {
        throw 'checkout identity is uncertain'
    }

    $rootItem = Get-Item -LiteralPath $gitRoot -Force -ErrorAction Stop
    $rootFull = [IO.Path]::GetFullPath($rootItem.FullName).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        -not [string]::Equals($candidateFull, $rootFull, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'checkout identity is uncertain'
    }

    return $rootFull
}

function Get-StatusPaths {
    param([string]$Checkout)

    $text = Get-Utf8Text (Invoke-GitBytes -Checkout $Checkout -Arguments 'status --porcelain=v1 -z --untracked-files=all --ignored=no')
    $parts = $text.Split([char]0)
    $paths = New-Object 'System.Collections.Generic.List[string]'
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)

    for ($index = 0; $index -lt ($parts.Length - 1); $index++) {
        $entry = $parts[$index]
        if ($entry.Length -lt 4 -or $entry[2] -ne ' ') {
            throw 'git status record is malformed'
        }

        $code = $entry.Substring(0, 2)
        if ($code -eq '!!') {
            throw 'git returned an ignored artifact despite ignored=no'
        }

        $relativePath = $entry.Substring(3)
        if ([string]::IsNullOrEmpty($relativePath)) {
            throw 'git status record is malformed'
        }

        if ($code.Contains('R') -or $code.Contains('C')) {
            $index++
            if ($index -ge ($parts.Length - 1) -or [string]::IsNullOrEmpty($parts[$index])) {
                throw 'git rename record is malformed'
            }
        }

        if (-not $seen.Add($relativePath)) {
            throw 'git status path identity is ambiguous'
        }

        $paths.Add($relativePath)
    }

    if ($parts[$parts.Length - 1].Length -ne 0) {
        throw 'git status record is malformed'
    }

    return $paths
}

function Get-ArtifactPath {
    param(
        [string]$Checkout,
        [string]$RelativePath
    )

    if ([IO.Path]::IsPathRooted($RelativePath) -or $RelativePath.Contains([char]0)) {
        throw 'git status path is malformed'
    }

    $rootWithSeparator = $Checkout + [IO.Path]::DirectorySeparatorChar
    $fullPath = [IO.Path]::GetFullPath((Join-Path -Path $Checkout -ChildPath $RelativePath))
    if (-not $fullPath.StartsWith($rootWithSeparator, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'git status path escapes the checkout'
    }

    return $fullPath
}

function Get-ArtifactFingerprint {
    param(
        [string]$FullPath,
        [Int64]$ExpectedLength
    )

    $item = Get-Item -LiteralPath $FullPath -Force -ErrorAction Stop
    if ($item.PSIsContainer -or (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
        throw 'status artifact is not a regular file'
    }

    $stream = [IO.File]::Open($FullPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        $length = $stream.Length
        if ($length -ne $ExpectedLength) {
            throw 'status artifact changed before it could be fingerprinted'
        }
        $hasher = [Security.Cryptography.SHA256]::Create()
        try {
            $digest = $hasher.ComputeHash($stream)
        }
        finally {
            $hasher.Dispose()
        }

        if ($stream.Length -ne $length) {
            throw 'status artifact changed while it was fingerprinted'
        }

        return [pscustomobject]@{
            length = [Int64]$length
            hash = ([BitConverter]::ToString($digest).Replace('-', '').ToLowerInvariant())
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Get-Inventory {
    param(
        [string]$Checkout,
        [int]$FileLimit,
        [Int64]$PerFileLimit,
        [Int64]$TotalLimit,
        [hashtable]$MissingBaselinePaths = $null
    )

    $records = New-Object 'System.Collections.Generic.List[object]'
    [Int64]$total = 0
    foreach ($relativePath in (Get-StatusPaths -Checkout $Checkout)) {
        if ($records.Count -ge $FileLimit) {
            throw 'status artifact count exceeds the configured limit'
        }

        $fullPath = Get-ArtifactPath -Checkout $Checkout -RelativePath $relativePath
        try {
            $statusItem = Get-Item -LiteralPath $fullPath -Force -ErrorAction Stop
        }
        catch [System.Management.Automation.ItemNotFoundException] {
            if ($null -ne $MissingBaselinePaths -and $MissingBaselinePaths.ContainsKey($relativePath)) {
                continue
            }
            throw 'status artifact disappeared before it could be fingerprinted'
        }

        if ($statusItem.PSIsContainer -or (($statusItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
            throw 'status artifact is not a regular file'
        }

        $metadataLength = [Int64]$statusItem.Length
        if ($metadataLength -gt $PerFileLimit) {
            throw 'status artifact exceeds the per-file byte limit'
        }

        if ($metadataLength -gt ($TotalLimit - $total)) {
            throw 'status artifact total exceeds the configured byte limit'
        }

        $fingerprint = Get-ArtifactFingerprint -FullPath $fullPath -ExpectedLength $metadataLength
        $total += $fingerprint.length
        $records.Add([pscustomobject]@{
                path = $relativePath
                length = $fingerprint.length
                hash = $fingerprint.hash
            })
    }

    return ,$records.ToArray()
}

function Get-CanonicalTempRoot {
    $root = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $item = Get-Item -LiteralPath $root -Force -ErrorAction Stop
    if (-not $item.PSIsContainer -or (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
        throw 'operating-system temp root identity is uncertain'
    }

    return $root
}

function Assert-StateOutsideWorktrees {
    param(
        [string]$Checkout,
        [string]$FullStatePath
    )

    $text = Get-Utf8Text (Invoke-GitBytes -Checkout $Checkout -Arguments 'worktree list --porcelain')
    $worktrees = @($text -split "`r?`n" | Where-Object { $_.StartsWith('worktree ') })
    if ($worktrees.Count -eq 0) {
        throw 'linked worktree identity is uncertain'
    }

    foreach ($line in $worktrees) {
        $worktree = [IO.Path]::GetFullPath($line.Substring(9)).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
        $prefix = $worktree + [IO.Path]::DirectorySeparatorChar
        if ([string]::Equals($FullStatePath, $worktree, [StringComparison]::OrdinalIgnoreCase) -or
            $FullStatePath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'state path must be outside every linked worktree'
        }
    }
}

function Get-ValidatedStatePath {
    param(
        [string]$Checkout,
        [string]$Candidate,
        [bool]$MustExist
    )

    if ([string]::IsNullOrWhiteSpace($Candidate)) {
        throw 'state path is required'
    }

    $tempRoot = Get-CanonicalTempRoot
    $fullPath = [IO.Path]::GetFullPath($Candidate)
    $parent = [IO.Path]::GetDirectoryName($fullPath)
    if (-not [string]::Equals($parent, $tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'state path must be a direct child of the operating-system temp root'
    }
    if ([IO.Path]::GetFileName($fullPath) -notmatch '^taskdeck-checkout-fingerprint-[0-9a-f]{32}\.json$') {
        throw 'state path does not have the required unique GUID name'
    }

    Assert-StateOutsideWorktrees -Checkout $Checkout -FullStatePath $fullPath
    $exists = Test-Path -LiteralPath $fullPath
    if ($MustExist -and -not $exists) {
        throw 'state file is missing'
    }
    if (-not $MustExist -and $exists) {
        throw 'state file already exists'
    }
    if ($exists) {
        $item = Get-Item -LiteralPath $fullPath -Force -ErrorAction Stop
        if ($item.PSIsContainer -or (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
            throw 'state file identity is uncertain'
        }
    }

    return $fullPath
}

function Get-Hmac {
    param(
        [string]$Payload,
        [string]$Secret
    )

    $key = [Text.Encoding]::UTF8.GetBytes($Secret)
    $bytes = [Text.Encoding]::UTF8.GetBytes($Payload)
    $hmac = New-Object Security.Cryptography.HMACSHA256(, $key)
    try {
        return ([BitConverter]::ToString($hmac.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant())
    }
    finally {
        $hmac.Dispose()
    }
}

function Test-FixedTimeString {
    param([string]$Left, [string]$Right)

    if ($Left.Length -ne $Right.Length) {
        return $false
    }

    [int]$difference = 0
    for ($index = 0; $index -lt $Left.Length; $index++) {
        $difference = $difference -bor ([int][char]$Left[$index] -bxor [int][char]$Right[$index])
    }

    return $difference -eq 0
}

function Test-ExactPropertyNames {
    param(
        [object]$Object,
        [string[]]$Names
    )

    if ($null -eq $Object) {
        return $false
    }

    $actual = @($Object.PSObject.Properties.Name)
    if ($actual.Count -ne $Names.Count) {
        return $false
    }

    foreach ($name in $Names) {
        if ($actual -notcontains $name) {
            return $false
        }
    }

    return $true
}

function Read-AuthenticatedState {
    param(
        [string]$Checkout,
        [string]$Candidate,
        [string]$Secret
    )

    $fullPath = Get-ValidatedStatePath -Checkout $Checkout -Candidate $Candidate -MustExist $true
    $stateItem = Get-Item -LiteralPath $fullPath -Force -ErrorAction Stop
    if ($stateItem.Length -gt 4194304) {
        throw 'state file exceeds the bounded size limit'
    }

    try {
        $wrapper = [IO.File]::ReadAllText($fullPath, [Text.Encoding]::UTF8) | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw 'state file is malformed'
    }

    if (-not (Test-ExactPropertyNames -Object $wrapper -Names @('version', 'payload', 'hmac')) -or
        $wrapper.version -ne 1 -or
        [string]::IsNullOrEmpty($wrapper.payload) -or
        $wrapper.hmac -notmatch '^[0-9a-f]{64}$' -or
        -not (Test-FixedTimeString -Left $wrapper.hmac -Right (Get-Hmac -Payload $wrapper.payload -Secret $Secret))) {
        throw 'state file authentication failed'
    }

    try {
        $payload = $wrapper.payload | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw 'state payload is malformed'
    }

    if (-not (Test-ExactPropertyNames -Object $payload -Names @('version', 'checkoutPath', 'files')) -or
        $payload.version -ne 1 -or -not [string]::Equals($payload.checkoutPath, $Checkout, [StringComparison]::OrdinalIgnoreCase) -or
        $null -eq $payload.files) {
        throw 'state payload identity is uncertain'
    }

    $files = @($payload.files)
    if ($files.Count -gt 100000) {
        throw 'state payload exceeds the bounded file limit'
    }

    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $files) {
        if (-not (Test-ExactPropertyNames -Object $file -Names @('path', 'length', 'hash')) -or
            [string]::IsNullOrEmpty($file.path) -or
            $file.hash -notmatch '^[0-9a-f]{64}$' -or $null -eq $file.length -or [Int64]$file.length -lt 0) {
            throw 'state payload is malformed'
        }

        [void](Get-ArtifactPath -Checkout $Checkout -RelativePath $file.path)
        if (-not $seen.Add($file.path)) {
            throw 'state payload path identity is ambiguous'
        }
    }

    return [pscustomobject]@{ path = $fullPath; files = $files }
}

function Invoke-Capture {
    param([string]$Checkout, [string]$Secret)

    $inventory = Get-Inventory -Checkout $Checkout -FileLimit $MaxFiles -PerFileLimit $MaxBytesPerFile -TotalLimit $MaxTotalBytes
    $payload = [ordered]@{
        version = 1
        checkoutPath = $Checkout
        files = @($inventory)
    } | ConvertTo-Json -Compress -Depth 5
    $state = [IO.Path]::Combine((Get-CanonicalTempRoot), ('taskdeck-checkout-fingerprint-' + [Guid]::NewGuid().ToString('N') + '.json'))
    $state = Get-ValidatedStatePath -Checkout $Checkout -Candidate $state -MustExist $false
    $wrapper = [ordered]@{
        version = 1
        payload = $payload
        hmac = Get-Hmac -Payload $payload -Secret $Secret
    } | ConvertTo-Json -Compress -Depth 4

    $bytes = [Text.Encoding]::UTF8.GetBytes($wrapper)
    $stream = New-Object IO.FileStream($state, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $stream.Write($bytes, 0, $bytes.Length)
    }
    finally {
        $stream.Dispose()
    }

    # Capture is assigned by the mandatory coordinator recipe so it must emit
    # through the PowerShell success pipeline, not directly to Console.Out.
    Write-Output (ConvertTo-FingerprintRecord -Path $state -Classification 'captured' -Count $inventory.Count)
}

function Invoke-Compare {
    param([string]$Checkout, [string]$Candidate, [string]$Secret)

    $state = Read-AuthenticatedState -Checkout $Checkout -Candidate $Candidate -Secret $Secret
    $before = @{}
    foreach ($file in $state.files) { $before[$file.path] = $file }
    $current = Get-Inventory -Checkout $Checkout -FileLimit $MaxFiles -PerFileLimit $MaxBytesPerFile -TotalLimit $MaxTotalBytes -MissingBaselinePaths $before
    $after = @{}
    foreach ($file in $current) { $after[$file.path] = $file }

    $changes = New-Object 'System.Collections.Generic.List[object]'
    foreach ($path in ($before.Keys | Sort-Object)) {
        if (-not $after.ContainsKey($path)) {
            $changes.Add([pscustomobject]@{ path = $path; classification = 'deleted' })
        }
        elseif ([Int64]$before[$path].length -ne [Int64]$after[$path].length -or $before[$path].hash -cne $after[$path].hash) {
            $changes.Add([pscustomobject]@{ path = $path; classification = 'overwritten' })
        }
    }
    foreach ($path in ($after.Keys | Sort-Object)) {
        if (-not $before.ContainsKey($path)) {
            $changes.Add([pscustomobject]@{ path = $path; classification = 'created' })
        }
    }

    if ($changes.Count -eq 0) {
        Write-FingerprintRecord -Path '' -Classification 'unchanged' -Count $current.Count
        return 0
    }

    foreach ($change in $changes) {
        Write-FingerprintRecord -Path $change.path -Classification $change.classification -Count 1
    }
    return 2
}

function Invoke-Cleanup {
    param([string]$Checkout, [string]$Candidate, [string]$Secret)

    $state = Read-AuthenticatedState -Checkout $Checkout -Candidate $Candidate -Secret $Secret
    Remove-Item -LiteralPath $state.path -Force -ErrorAction Stop
    Write-FingerprintRecord -Path $state.path -Classification 'cleaned' -Count 0
}

if ($MyInvocation.InvocationName -ne '.') {
    try {
        if ([string]::IsNullOrWhiteSpace($Mode) -or [string]::IsNullOrWhiteSpace($Token)) {
            throw 'mode and a nonempty caller token are required'
        }

        $checkout = Get-CanonicalCheckout -Candidate $CheckoutPath
        switch ($Mode) {
            'Capture' { Invoke-Capture -Checkout $checkout -Secret $Token; exit 0 }
            'Compare' { exit (Invoke-Compare -Checkout $checkout -Candidate $StatePath -Secret $Token) }
            'Cleanup' { Invoke-Cleanup -Checkout $checkout -Candidate $StatePath -Secret $Token; exit 0 }
        }
    }
    catch {
        [Console]::Error.WriteLine('Checkout fingerprint failed: ' + $_.Exception.Message)
        exit 1
    }
}
