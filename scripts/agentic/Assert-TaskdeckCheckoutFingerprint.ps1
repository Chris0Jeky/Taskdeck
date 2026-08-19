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
    [Int64]$MaxGitOutputBytes = 16777216,

    [ValidateRange(1000, 3600000)]
    [int]$GitTimeoutMs = 120000
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

function ConvertTo-DiagnosticText {
    param([string]$Value)

    # Diagnostics reach stderr as one line. Control characters would break that
    # framing, so they are folded before the text is ever embedded in a message.
    $text = ($Value -replace '[\x00-\x1f\x7f]', '?')
    if ($text.Length -gt 200) {
        $text = $text.Substring(0, 200) + '...'
    }

    return $text
}

function Get-ArtifactKindText {
    param([object]$Item)

    $kinds = New-Object 'System.Collections.Generic.List[string]'
    if (($Item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        $kinds.Add('a reparse point (symlink or junction)')
    }
    if ($Item.PSIsContainer) {
        $kinds.Add('a directory')
    }
    if ($kinds.Count -eq 0) {
        $kinds.Add('an entry of an unexpected kind')
    }

    return ($kinds -join ' and ')
}

function Invoke-GitBytes {
    param(
        [string]$Checkout,
        [string]$Arguments,
        [Int64]$OutputLimit = $MaxGitOutputBytes,
        [int]$TimeoutMs = $GitTimeoutMs
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
    $clock = [Diagnostics.Stopwatch]::StartNew()
    # Every wait below draws from one per-subprocess deadline. Without it a git
    # that never writes and never exits blocks the guard forever, and a blocked
    # guard is an unguarded lane.
    $remaining = {
        $left = $TimeoutMs - [int][Math]::Min([double]$clock.ElapsedMilliseconds, [double]$TimeoutMs)
        if ($left -lt 1) { return 1 }
        return $left
    }
    try {
        $started = $process.Start()
        if (-not $started) {
            throw 'could not start git'
        }

        # Stderr is drained into Stream.Null: the guard never classifies on its
        # text, and an unbounded in-memory ReadToEnd would let a verbose or
        # wedged git exhaust memory even while stdout stayed under its own cap.
        $errorTask = $process.StandardError.BaseStream.CopyToAsync([IO.Stream]::Null)
        $buffer = New-Object byte[] 8192
        while ($true) {
            $readTask = $process.StandardOutput.BaseStream.ReadAsync($buffer, 0, $buffer.Length)
            if (-not $readTask.Wait((& $remaining))) {
                try { $process.Kill() } catch { }
                throw 'git exceeded the configured deadline'
            }
            $read = $readTask.Result
            if ($read -le 0) { break }
            if ($output.Length -gt ($OutputLimit - $read)) {
                try { $process.Kill() } catch { }
                throw 'git output exceeds the configured byte limit'
            }
            $output.Write($buffer, 0, $read)
        }
        if (-not $process.WaitForExit((& $remaining))) {
            try { $process.Kill() } catch { }
            throw 'git exceeded the configured deadline'
        }
        if (-not $errorTask.Wait((& $remaining))) {
            try { $process.Kill() } catch { }
            throw 'git exceeded the configured deadline'
        }
        if ($process.ExitCode -ne 0) {
            throw 'git could not establish checkout identity'
        }

        return ,$output.ToArray()
    }
    finally {
        $clock.Stop()
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

function Get-CheckoutHead {
    param([string]$Checkout)

    # A `git switch` or a commit between Capture and Compare leaves a clean
    # worktree on both sides, so a status-artifact inventory alone reports
    # `unchanged` for a checkout that now points at different content. HEAD and
    # its symbolic ref are captured so that mutation is visible; anything the
    # guard cannot read exactly fails closed like every other identity here.
    $commit = (Get-Utf8Text (Invoke-GitBytes -Checkout $Checkout -Arguments 'rev-parse HEAD')).TrimEnd("`r", "`n")
    if ($commit -notmatch '^[0-9a-f]{40}$' -and $commit -notmatch '^[0-9a-f]{64}$') {
        throw 'checkout HEAD identity is uncertain'
    }

    # Detached HEAD (every worktree this guard was written for) reports the
    # literal `HEAD`; an attached branch reports its full `refs/...` name.
    $ref = (Get-Utf8Text (Invoke-GitBytes -Checkout $Checkout -Arguments 'rev-parse --symbolic-full-name HEAD')).TrimEnd("`r", "`n")
    if ($ref -cne 'HEAD' -and -not $ref.StartsWith('refs/', [StringComparison]::Ordinal)) {
        throw 'checkout HEAD reference identity is uncertain'
    }
    if ($ref -match '[\x00-\x1f\x7f]' -or $ref.Length -gt 512) {
        throw 'checkout HEAD reference identity is uncertain'
    }

    return [pscustomobject]@{ commit = $commit; ref = $ref }
}

function Get-StatusPaths {
    param([string]$Checkout)

    $text = Get-Utf8Text (Invoke-GitBytes -Checkout $Checkout -Arguments 'status --porcelain=v1 -z --untracked-files=all --ignored=no')
    $parts = $text.Split([char]0)
    $paths = New-Object 'System.Collections.Generic.List[object]'
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

        # The two-letter code travels with the path so Compare can tell a lane
        # that created a brand-new artifact from a lane that overwrote a file
        # that was clean (and therefore outside the baseline) at capture time.
        $paths.Add([pscustomobject]@{ path = $relativePath; code = $code })
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
        throw ('status artifact is not a regular file: found ' + (Get-ArtifactKindText -Item $item))
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
    $observed = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    [Int64]$total = 0
    foreach ($entry in (Get-StatusPaths -Checkout $Checkout)) {
        $relativePath = $entry.path
        [void]$observed.Add($relativePath)
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
            throw ('status artifact is not a regular file: found ' + (Get-ArtifactKindText -Item $statusItem) +
                ' at ' + (ConvertTo-DiagnosticText -Value $relativePath) +
                ' (git status code ' + (ConvertTo-DiagnosticText -Value $entry.code) + ')')
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
                code = $entry.code
                length = $fingerprint.length
                hash = $fingerprint.hash
            })
    }

    Assert-InventoryStability -Checkout $Checkout -Records $records -ObservedPaths $observed
    return ,$records.ToArray()
}

function Assert-InventoryStability {
    param(
        [string]$Checkout,
        [object]$Records,
        [object]$ObservedPaths
    )

    # The walk above fingerprints one artifact at a time, so a mutation that
    # lands behind the cursor would otherwise be recorded as if it had always
    # been there. Revalidate the completed inventory before anyone trusts it:
    # nothing new may have entered status, and nothing already fingerprinted may
    # have changed kind or length.
    foreach ($entry in (Get-StatusPaths -Checkout $Checkout)) {
        if (-not $ObservedPaths.Contains($entry.path)) {
            throw ('status inventory changed while it was fingerprinted: ' +
                (ConvertTo-DiagnosticText -Value $entry.path) + ' entered git status')
        }
    }

    foreach ($record in $Records) {
        $fullPath = Get-ArtifactPath -Checkout $Checkout -RelativePath $record.path
        try {
            $item = Get-Item -LiteralPath $fullPath -Force -ErrorAction Stop
        }
        catch [System.Management.Automation.ItemNotFoundException] {
            throw ('status inventory changed while it was fingerprinted: ' +
                (ConvertTo-DiagnosticText -Value $record.path) + ' disappeared')
        }

        if ($item.PSIsContainer -or (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) -or
            [Int64]$item.Length -ne [Int64]$record.length) {
            throw ('status inventory changed while it was fingerprinted: ' +
                (ConvertTo-DiagnosticText -Value $record.path) + ' no longer matches its fingerprint')
        }
    }
}

function Assert-NoReparseAncestor {
    param([string]$FullPath)

    # A clean final directory proves nothing if any ancestor is a junction or
    # symlink: the whole subtree can be re-aimed elsewhere without the leaf ever
    # looking suspicious, which is exactly the containment this guard claims.
    $current = $FullPath
    $depth = 0
    while (-not [string]::IsNullOrEmpty($current)) {
        $depth++
        if ($depth -gt 64) {
            throw 'operating-system temp root ancestry is uncertain'
        }

        $item = Get-Item -LiteralPath $current -Force -ErrorAction Stop
        if (-not $item.PSIsContainer -or (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
            throw ('operating-system temp root ancestry is uncertain: ' +
                (ConvertTo-DiagnosticText -Value $current) + ' is not a plain directory')
        }

        $parent = [IO.Path]::GetDirectoryName($current)
        if ([string]::IsNullOrEmpty($parent) -or [string]::Equals($parent, $current, [StringComparison]::OrdinalIgnoreCase)) {
            break
        }
        $current = $parent
    }
}

function Get-CanonicalTempRoot {
    $root = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $item = Get-Item -LiteralPath $root -Force -ErrorAction Stop
    if (-not $item.PSIsContainer -or (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
        throw 'operating-system temp root identity is uncertain'
    }

    Assert-NoReparseAncestor -FullPath $root
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

    if (-not (Test-ExactPropertyNames -Object $payload -Names @('version', 'checkoutPath', 'head', 'headRef', 'files')) -or
        $payload.version -ne 2 -or -not [string]::Equals($payload.checkoutPath, $Checkout, [StringComparison]::OrdinalIgnoreCase) -or
        $null -eq $payload.files) {
        throw 'state payload identity is uncertain'
    }

    if (($payload.head -notmatch '^[0-9a-f]{40}$' -and $payload.head -notmatch '^[0-9a-f]{64}$') -or
        [string]::IsNullOrEmpty($payload.headRef) -or
        ($payload.headRef -cne 'HEAD' -and -not ([string]$payload.headRef).StartsWith('refs/', [StringComparison]::Ordinal))) {
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

    return [pscustomobject]@{ path = $fullPath; files = $files; head = $payload.head; headRef = $payload.headRef }
}

function Invoke-Capture {
    param([string]$Checkout, [string]$Secret)

    $head = Get-CheckoutHead -Checkout $Checkout
    $inventory = Get-Inventory -Checkout $Checkout -FileLimit $MaxFiles -PerFileLimit $MaxBytesPerFile -TotalLimit $MaxTotalBytes
    # The status code is a Compare-time classification aid, not part of the
    # authenticated identity; the persisted payload keeps its exact shape.
    $payload = [ordered]@{
        version = 2
        checkoutPath = $Checkout
        head = $head.commit
        headRef = $head.ref
        files = @($inventory | ForEach-Object { [ordered]@{ path = $_.path; length = $_.length; hash = $_.hash } })
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
    $head = Get-CheckoutHead -Checkout $Checkout
    $current = Get-Inventory -Checkout $Checkout -FileLimit $MaxFiles -PerFileLimit $MaxBytesPerFile -TotalLimit $MaxTotalBytes -MissingBaselinePaths $before
    $after = @{}
    foreach ($file in $current) { $after[$file.path] = $file }

    $changes = New-Object 'System.Collections.Generic.List[object]'
    # HEAD first: a clean-to-clean `git switch` or commit moves the checkout
    # without touching a single status artifact, and reporting `unchanged` for
    # it is the exact blind spot this ordering closes.
    if ($state.headRef -cne $head.ref) {
        $changes.Add([pscustomobject]@{ path = 'HEAD'; classification = 'ref-moved' })
    }
    if ($state.head -cne $head.commit) {
        $changes.Add([pscustomobject]@{ path = 'HEAD'; classification = 'head-moved' })
    }

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
            # Absent from the baseline is not the same as new on disk: a file
            # that was clean tracked content at capture time never entered the
            # baseline, so the lane overwrote it rather than creating it. Only
            # an untracked (`??`) or newly index-added (`A`) artifact is really
            # a creation.
            $code = [string]$after[$path].code
            $classification = 'overwritten'
            if ($code -eq '??' -or $code.StartsWith('A', [StringComparison]::Ordinal)) {
                $classification = 'created'
            }
            $changes.Add([pscustomobject]@{ path = $path; classification = $classification })
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
    # Structured exit-code propagation. No branch below calls `exit` while the
    # guard is mid-flight: a PowerShell `exit` raises a flow-control exception
    # that `catch` cannot see and that skips every remaining statement, so an
    # `exit` inside the dispatch would make any future `finally` the only
    # survivable place to put disposition work. Every mode instead assigns a
    # code and the single `exit` below runs after the try/catch has settled.
    # The initial value is nonzero so an unmatched mode fails closed.
    $exitCode = 1
    try {
        if ([string]::IsNullOrWhiteSpace($Mode) -or [string]::IsNullOrWhiteSpace($Token)) {
            throw 'mode and a nonempty caller token are required'
        }

        $checkout = Get-CanonicalCheckout -Candidate $CheckoutPath
        switch ($Mode) {
            # Invoke-Capture emits its record on the success pipeline, so it must
            # stay a bare statement here; only Invoke-Compare returns a code.
            'Capture' { Invoke-Capture -Checkout $checkout -Secret $Token; $exitCode = 0 }
            'Compare' { $exitCode = [int](Invoke-Compare -Checkout $checkout -Candidate $StatePath -Secret $Token) }
            'Cleanup' { Invoke-Cleanup -Checkout $checkout -Candidate $StatePath -Secret $Token; $exitCode = 0 }
            default { throw 'mode is not supported' }
        }
    }
    catch {
        [Console]::Error.WriteLine('Checkout fingerprint failed: ' + $_.Exception.Message)
        $exitCode = 1
    }

    exit $exitCode
}
