[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [int]$IssueNumber,

    [Parameter(Mandatory = $true)]
    [string]$Slug,

    [string]$BaseBranch = "origin/main",
    [string]$WorktreeRoot = ".worktrees",
    [string]$BranchName
)

$ErrorActionPreference = "Stop"

$gitCommand = Get-Command git -CommandType Application -All -ErrorAction SilentlyContinue |
    Where-Object { [System.IO.Path]::GetExtension($_.Source) -notin @('.cmd', '.bat') } |
    Select-Object -First 1
if ($null -eq $gitCommand) {
    throw "No argv-safe Git executable was found on PATH; .cmd and .bat shims are not supported."
}

$gitExecutable = $gitCommand.Source

function ConvertTo-NativeArgument {
    param(
        [AllowEmptyString()]
        [string]$Argument
    )

    if ([string]::IsNullOrEmpty($Argument)) {
        return '""'
    }
    if ($Argument -notmatch '[\s"]') {
        return $Argument
    }

    $builder = [System.Text.StringBuilder]::new()
    [void]$builder.Append('"')
    $backslashCount = 0
    foreach ($character in $Argument.ToCharArray()) {
        if ($character -eq [char]'\') {
            $backslashCount++
            continue
        }

        $escapedBackslashCount = if ($character -eq [char]'"') {
            ($backslashCount * 2) + 1
        }
        else {
            $backslashCount
        }
        for ($index = 0; $index -lt $escapedBackslashCount; $index++) {
            [void]$builder.Append([char]'\')
        }
        [void]$builder.Append($character)
        $backslashCount = 0
    }

    for ($index = 0; $index -lt ($backslashCount * 2); $index++) {
        [void]$builder.Append([char]'\')
    }
    [void]$builder.Append('"')
    return $builder.ToString()
}

function Invoke-GitCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $process = $null
    try {
        $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = $gitExecutable
        $startInfo.WorkingDirectory = (Get-Location).Path
        $startInfo.UseShellExecute = $false
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true

        if ($null -ne $startInfo.PSObject.Properties['ArgumentList']) {
            foreach ($argument in $Arguments) {
                $startInfo.ArgumentList.Add($argument)
            }
        }
        else {
            $startInfo.Arguments = (($Arguments | ForEach-Object { ConvertTo-NativeArgument $_ }) -join ' ')
        }

        $process = [System.Diagnostics.Process]::new()
        $process.StartInfo = $startInfo
        if (-not $process.Start()) {
            throw "Git process did not start."
        }

        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $rawStdout = $stdoutTask.GetAwaiter().GetResult()
        $stdout = $rawStdout.Trim()
        $stderr = $stderrTask.GetAwaiter().GetResult().Trim()
        $output = (@($stdout, $stderr) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join "`n"

        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            Stdout = $stdout
            RawStdout = $rawStdout
            Stderr = $stderr
            Output = $output
        }
    }
    catch {
        return [pscustomobject]@{
            ExitCode = -1
            Stdout = ""
            RawStdout = ""
            Stderr = $_.Exception.Message
            Output = $_.Exception.Message
        }
    }
    finally {
        if ($null -ne $process) {
            $process.Dispose()
        }
    }
}

function Invoke-GitBlobBytes {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Repository,

        [Parameter(Mandatory = $true)]
        [string]$Blob
    )

    $process = $null
    $memoryStream = $null
    try {
        $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = $gitExecutable
        $startInfo.WorkingDirectory = $Repository
        $startInfo.UseShellExecute = $false
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $arguments = @("-C", $Repository, "cat-file", "blob", $Blob)

        if ($null -ne $startInfo.PSObject.Properties['ArgumentList']) {
            foreach ($argument in $arguments) {
                $startInfo.ArgumentList.Add($argument)
            }
        }
        else {
            $startInfo.Arguments = (($arguments | ForEach-Object { ConvertTo-NativeArgument $_ }) -join ' ')
        }

        $process = [System.Diagnostics.Process]::new()
        $process.StartInfo = $startInfo
        if (-not $process.Start()) {
            throw "Git process did not start."
        }

        $memoryStream = [System.IO.MemoryStream]::new()
        $stdoutTask = $process.StandardOutput.BaseStream.CopyToAsync($memoryStream)
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult().Trim()

        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            Bytes = $memoryStream.ToArray()
            Output = $stderr
        }
    }
    catch {
        return [pscustomobject]@{
            ExitCode = -1
            Bytes = [byte[]]@()
            Output = $_.Exception.Message
        }
    }
    finally {
        if ($null -ne $memoryStream) {
            $memoryStream.Dispose()
        }
        if ($null -ne $process) {
            $process.Dispose()
        }
    }
}

function Test-ByteArrayEqual {
    param(
        [Parameter(Mandatory = $true)]
        [byte[]]$Left,

        [Parameter(Mandatory = $true)]
        [byte[]]$Right
    )

    if ($Left.Length -ne $Right.Length) {
        return $false
    }
    for ($index = 0; $index -lt $Left.Length; $index++) {
        if ($Left[$index] -ne $Right[$index]) {
            return $false
        }
    }
    return $true
}

function ConvertTo-CrlfBytes {
    param(
        [Parameter(Mandatory = $true)]
        [byte[]]$Bytes
    )

    $memoryStream = [System.IO.MemoryStream]::new()
    try {
        $previousValue = -1
        foreach ($value in $Bytes) {
            if ($value -eq 10 -and $previousValue -ne 13) {
                $memoryStream.WriteByte(13)
            }
            $memoryStream.WriteByte($value)
            $previousValue = $value
        }
        return ,$memoryStream.ToArray()
    }
    finally {
        $memoryStream.Dispose()
    }
}

function Format-GitContext {
    param([string]$Output)

    if ([string]::IsNullOrWhiteSpace($Output)) {
        return ""
    }

    return "`nGit: $Output"
}

function Resolve-BaseCommit {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Repository,

        [Parameter(Mandatory = $true)]
        [string]$Reference,

        [Parameter(Mandatory = $true)]
        [string]$DisplayName
    )

    $baseCommitExpression = "${Reference}^{commit}"
    $baseLookupResult = Invoke-GitCommand -Arguments @("-C", $Repository, "rev-parse", "--verify", "--end-of-options", $baseCommitExpression)
    if ($baseLookupResult.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($baseLookupResult.Stdout)) {
        throw "Base commit not found: $DisplayName$(Format-GitContext $baseLookupResult.Output)"
    }

    $resolvedCommit = $baseLookupResult.Stdout.Trim()
    $baseTypeResult = Invoke-GitCommand -Arguments @("-C", $Repository, "cat-file", "-t", $resolvedCommit)
    if ($baseTypeResult.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($baseTypeResult.Stdout) -or $baseTypeResult.Stdout.Trim() -cne "commit") {
        throw "Base does not resolve to a commit: $DisplayName$(Format-GitContext $baseTypeResult.Output)"
    }

    return $resolvedCommit
}

function Get-ReviewedHandoffArtifacts {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Repository
    )

    $sourceArtifacts = @(
        "scripts/git/New-CodexIssueWorktree.ps1",
        "scripts/worktree_guard.ps1",
        "scripts/git/Initialize-CodexIssueWorktree.ps1"
    )
    $selectedBaseArtifacts = @(
        "scripts/worktree_guard.ps1",
        "scripts/git/Initialize-CodexIssueWorktree.ps1"
    )
    $sourceHead = Resolve-BaseCommit -Repository $Repository -Reference "HEAD" -DisplayName "invoking checkout HEAD"
    $reviewedArtifacts = [ordered]@{}
    foreach ($artifact in $sourceArtifacts) {
        $objectExpression = "${sourceHead}:$artifact"
        $artifactLookupResult = Invoke-GitCommand -Arguments @("-C", $Repository, "rev-parse", "--verify", "--end-of-options", $objectExpression)
        if ($artifactLookupResult.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($artifactLookupResult.Stdout)) {
            throw "Invoking checkout HEAD does not contain required handoff artifact '$artifact'.$(Format-GitContext $artifactLookupResult.Output)"
        }

        $artifactBlob = $artifactLookupResult.Stdout.Trim()
        $artifactTypeResult = Invoke-GitCommand -Arguments @("-C", $Repository, "cat-file", "-t", $artifactBlob)
        if ($artifactTypeResult.ExitCode -ne 0 -or
            [string]::IsNullOrWhiteSpace($artifactTypeResult.Stdout) -or
            $artifactTypeResult.Stdout.Trim() -cne "blob") {
            throw "Invoking checkout HEAD handoff artifact '$artifact' is not a blob.$(Format-GitContext $artifactTypeResult.Output)"
        }

        $artifactPath = Join-Path $Repository $artifact
        if (-not (Test-Path -LiteralPath $artifactPath -PathType Leaf)) {
            throw "Reviewed handoff artifact '$artifact' is missing from the invoking checkout."
        }

        $indexResult = Invoke-GitCommand -Arguments @("-C", $Repository, "ls-files", "--stage", "--", $artifact)
        $indexEntries = @(
            $indexResult.Stdout -split '\r?\n' |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        )
        if ($indexResult.ExitCode -ne 0 -or $indexEntries.Count -ne 1 -or
            $indexEntries[0] -notmatch '^[0-9]{6} ([0-9a-fA-F]+) 0\t') {
            throw "Git could not inspect the index entry for reviewed handoff artifact '$artifact'.$(Format-GitContext $indexResult.Output)"
        }
        $indexBlob = $Matches[1]
        if ($indexBlob -cne $artifactBlob) {
            throw "Reviewed handoff artifact '$artifact' has staged changes in the invoking checkout."
        }

        $committedBlobResult = Invoke-GitBlobBytes -Repository $Repository -Blob $artifactBlob
        if ($committedBlobResult.ExitCode -ne 0) {
            throw "Git could not read reviewed HEAD blob for handoff artifact '$artifact'.$(Format-GitContext $committedBlobResult.Output)"
        }

        [byte[]]$committedBytes = $committedBlobResult.Bytes
        [byte[]]$workingBytes = [System.IO.File]::ReadAllBytes($artifactPath)
        [byte[]]$crlfBytes = ConvertTo-CrlfBytes -Bytes $committedBytes
        if (-not (Test-ByteArrayEqual -Left $workingBytes -Right $committedBytes) -and
            -not (Test-ByteArrayEqual -Left $workingBytes -Right $crlfBytes)) {
            throw "Reviewed handoff artifact '$artifact' working content does not match the invoking checkout HEAD blob."
        }

        if ($selectedBaseArtifacts -ccontains $artifact) {
            $reviewedArtifacts[$artifact] = $artifactBlob
        }
    }

    return $reviewedArtifacts
}

function Assert-BaseMatchesReviewedHandoffArtifacts {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Repository,

        [Parameter(Mandatory = $true)]
        [string]$Commit,

        [Parameter(Mandatory = $true)]
        [string]$DisplayName,

        [Parameter(Mandatory = $true)]
        [System.Collections.IDictionary]$ReviewedArtifacts
    )

    foreach ($artifact in $ReviewedArtifacts.Keys) {
        $objectExpression = "${Commit}:$artifact"
        $artifactLookupResult = Invoke-GitCommand -Arguments @("-C", $Repository, "rev-parse", "--verify", "--end-of-options", $objectExpression)
        if ($artifactLookupResult.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($artifactLookupResult.Stdout)) {
            throw "Base commit '$DisplayName' does not contain required handoff artifact '$artifact'.$(Format-GitContext $artifactLookupResult.Output)"
        }

        $artifactBlob = $artifactLookupResult.Stdout.Trim()
        $artifactTypeResult = Invoke-GitCommand -Arguments @("-C", $Repository, "cat-file", "-t", $artifactBlob)
        if ($artifactTypeResult.ExitCode -ne 0 -or
            [string]::IsNullOrWhiteSpace($artifactTypeResult.Stdout) -or
            $artifactTypeResult.Stdout.Trim() -cne "blob") {
            throw "Base commit '$DisplayName' does not contain required handoff artifact '$artifact'.$(Format-GitContext $artifactTypeResult.Output)"
        }
        if ($artifactBlob -cne $ReviewedArtifacts[$artifact]) {
            throw "Base commit '$DisplayName' handoff artifact '$artifact' does not match the reviewed artifact in the invoking checkout HEAD."
        }
    }
}

function Assert-TargetMatchesReviewedHandoffArtifacts {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Repository,

        [Parameter(Mandatory = $true)]
        [string]$Worktree,

        [Parameter(Mandatory = $true)]
        [System.Collections.IDictionary]$ReviewedArtifacts
    )

    foreach ($artifact in $ReviewedArtifacts.Keys) {
        $artifactPath = Join-Path $Worktree $artifact
        if (-not (Test-Path -LiteralPath $artifactPath -PathType Leaf)) {
            throw "Helper-created worktree is missing required handoff artifact '$artifact'."
        }

        $blobResult = Invoke-GitBlobBytes -Repository $Repository -Blob $ReviewedArtifacts[$artifact]
        if ($blobResult.ExitCode -ne 0) {
            throw "Git could not read reviewed handoff blob for target artifact '$artifact'.$(Format-GitContext $blobResult.Output)"
        }

        [byte[]]$reviewedBytes = $blobResult.Bytes
        [byte[]]$targetBytes = [System.IO.File]::ReadAllBytes($artifactPath)
        [byte[]]$reviewedCrlfBytes = ConvertTo-CrlfBytes -Bytes $reviewedBytes
        if (-not (Test-ByteArrayEqual -Left $targetBytes -Right $reviewedBytes) -and
            -not (Test-ByteArrayEqual -Left $targetBytes -Right $reviewedCrlfBytes)) {
            throw "Helper-created worktree handoff artifact '$artifact' does not match the reviewed raw blob."
        }
    }
}

function Reserve-WorktreeTarget {
    param(
        [Parameter(Mandatory = $true)]
        [string]$WorktreeRoot,

        [Parameter(Mandatory = $true)]
        [string]$Worktree
    )

    New-Item -ItemType Directory -Force -Path $WorktreeRoot | Out-Null
    Assert-SafeWorktreeRoot -Path $WorktreeRoot
    try {
        New-Item -ItemType Directory -Path $Worktree -ErrorAction Stop | Out-Null
    }
    catch [System.IO.IOException] {
        throw "Worktree path already exists: $Worktree"
    }

    Assert-SafeWorktreeRoot -Path $WorktreeRoot
    $reservedTarget = Get-Item -LiteralPath $Worktree -Force -ErrorAction Stop
    if (-not $reservedTarget.PSIsContainer -or
        ($reservedTarget.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Worktree target is not a plain reserved directory: $Worktree"
    }
}

function Assert-WorktreeTargetAvailable {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Worktree
    )

    try {
        $null = Get-Item -LiteralPath $Worktree -Force -ErrorAction Stop
    }
    catch [System.Management.Automation.ItemNotFoundException] {
        return
    }
    catch {
        throw "Worktree path could not be inspected: $Worktree. $($_.Exception.Message)"
    }

    throw "Worktree path already exists: $Worktree"
}

function Remove-EmptyReservedWorktreeTarget {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Worktree
    )

    if (-not (Test-Path -LiteralPath $Worktree -PathType Container)) {
        return
    }

    $reservedTarget = Get-Item -LiteralPath $Worktree -Force -ErrorAction Stop
    if (($reservedTarget.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing to remove reparse-point worktree reservation: $Worktree"
    }
    if (@(Get-ChildItem -LiteralPath $Worktree -Force).Count -ne 0) {
        throw "Refusing to remove non-empty failed worktree reservation: $Worktree"
    }
    Remove-Item -LiteralPath $Worktree -Force -ErrorAction Stop
}

function Get-WorktreeStatusLines {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Worktree
    )

    $statusResult = Invoke-GitCommand -Arguments @(
        "-C", $Worktree, "status", "--porcelain=v1", "--untracked-files=all", "--ignored=matching"
    )
    if ($statusResult.ExitCode -ne 0) {
        throw "Could not inspect helper-created worktree '$Worktree' before cleanup.$(Format-GitContext $statusResult.Output)"
    }

    return @(
        $statusResult.RawStdout -split '\r?\n' |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
}

function Assert-HelperCreatedWorktreeIdentity {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Worktree,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedHead
    )

    $pathComparison = if ([System.IO.Path]::DirectorySeparatorChar -eq [char]'\') {
        [System.StringComparison]::OrdinalIgnoreCase
    }
    else {
        [System.StringComparison]::Ordinal
    }
    $expectedWorktreePath = [System.IO.Path]::GetFullPath($Worktree).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $topLevelResult = Invoke-GitCommand -Arguments @("-C", $Worktree, "rev-parse", "--show-toplevel")
    $headResult = Invoke-GitCommand -Arguments @("-C", $Worktree, "rev-parse", "--verify", "HEAD")
    $gitDirectoryResult = Invoke-GitCommand -Arguments @("-C", $Worktree, "rev-parse", "--absolute-git-dir")
    $commonDirectoryResult = Invoke-GitCommand -Arguments @("-C", $Worktree, "rev-parse", "--path-format=absolute", "--git-common-dir")
    $symbolicHeadResult = Invoke-GitCommand -Arguments @("-C", $Worktree, "symbolic-ref", "--quiet", "HEAD")

    if ($topLevelResult.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($topLevelResult.Stdout) -or
        $headResult.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($headResult.Stdout) -or
        $gitDirectoryResult.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($gitDirectoryResult.Stdout) -or
        $commonDirectoryResult.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($commonDirectoryResult.Stdout) -or
        $symbolicHeadResult.ExitCode -ne 1) {
        throw "Refusing to clean up '$Worktree' because its helper-created detached-worktree identity could not be verified."
    }

    $actualTopLevel = [System.IO.Path]::GetFullPath($topLevelResult.Stdout.Trim()).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $actualGitDirectory = [System.IO.Path]::GetFullPath($gitDirectoryResult.Stdout.Trim()).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $actualCommonDirectory = [System.IO.Path]::GetFullPath($commonDirectoryResult.Stdout.Trim()).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    if (-not $actualTopLevel.Equals($expectedWorktreePath, $pathComparison) -or
        -not $headResult.Stdout.Trim().Equals($ExpectedHead, [System.StringComparison]::OrdinalIgnoreCase) -or
        $actualGitDirectory.Equals($actualCommonDirectory, $pathComparison)) {
        throw "Refusing to clean up '$Worktree' because it is not the expected helper-created detached worktree at '$ExpectedHead'."
    }
}

function Remove-HelperCreatedWorktree {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Worktree,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedHead,

        [Parameter(Mandatory = $true)]
        [System.Collections.IDictionary]$ReviewedArtifacts
    )

    Assert-HelperCreatedWorktreeIdentity -Worktree $Worktree -ExpectedHead $ExpectedHead
    $statusLines = @(Get-WorktreeStatusLines -Worktree $Worktree)
    $neutralizedArtifacts = @()
    foreach ($statusLine in $statusLines) {
        if ($statusLine.Length -lt 4) {
            throw "Refusing to remove helper-created worktree '$Worktree' with unrecognized dirt: $statusLine"
        }

        $statusCode = $statusLine.Substring(0, 2)
        $statusPath = $statusLine.Substring(3)
        if ($statusCode -notin @(" M", " D") -or -not $ReviewedArtifacts.Contains($statusPath)) {
            throw "Refusing to remove helper-created worktree '$Worktree' with unexpected dirt: $statusLine"
        }

        $indexResult = Invoke-GitCommand -Arguments @("-C", $Worktree, "ls-files", "--stage", "--", $statusPath)
        $indexEntries = @(
            $indexResult.Stdout -split '\r?\n' |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        )
        if ($indexResult.ExitCode -ne 0 -or $indexEntries.Count -ne 1 -or
            $indexEntries[0] -notmatch '^[0-9]{6} ([0-9a-fA-F]+) 0\t' -or
            $Matches[1] -cne [string]$ReviewedArtifacts[$statusPath]) {
            throw "Refusing to neutralize unverified handoff-artifact dirtiness in '$Worktree': $statusPath"
        }
        $neutralizedArtifacts += $statusPath
    }

    $skipWorktreeApplied = $false
    try {
        if ($neutralizedArtifacts.Count -gt 0) {
            $skipArguments = @("-C", $Worktree, "update-index", "--skip-worktree", "--") + $neutralizedArtifacts
            $skipResult = Invoke-GitCommand -Arguments $skipArguments
            if ($skipResult.ExitCode -ne 0) {
                throw "Could not neutralize verified handoff-artifact dirtiness in '$Worktree'.$(Format-GitContext $skipResult.Output)"
            }
            $skipWorktreeApplied = $true

            $remainingStatus = @(Get-WorktreeStatusLines -Worktree $Worktree)
            if ($remainingStatus.Count -ne 0) {
                throw "Refusing to remove helper-created worktree '$Worktree' because dirt remained after bounded handoff-artifact neutralization: $($remainingStatus -join '; ')"
            }
        }

        $removeResult = Invoke-GitCommand -Arguments @("worktree", "remove", $Worktree)
        if ($removeResult.ExitCode -ne 0) {
            throw "Could not remove helper-created worktree '$Worktree' after handoff verification failed (exit code $($removeResult.ExitCode)).$(Format-GitContext $removeResult.Output)"
        }
    }
    catch {
        $cleanupFailure = $_
        if ($skipWorktreeApplied -and (Test-Path -LiteralPath $Worktree -PathType Container)) {
            $restoreArguments = @("-C", $Worktree, "update-index", "--no-skip-worktree", "--") + $neutralizedArtifacts
            $restoreResult = Invoke-GitCommand -Arguments $restoreArguments
            if ($restoreResult.ExitCode -ne 0) {
                throw "$($cleanupFailure.Exception.Message) The bounded skip-worktree flags also could not be restored.$(Format-GitContext $restoreResult.Output)"
            }
        }
        throw $cleanupFailure
    }
}

function Assert-RemoteBaseExistsWithoutFetch {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Remote,

        [Parameter(Mandatory = $true)]
        [string]$Branch,

        [Parameter(Mandatory = $true)]
        [string]$DisplayName
    )

    $remoteReference = "refs/heads/$Branch"
    $remoteLookupResult = Invoke-GitCommand -Arguments @("ls-remote", "--exit-code", "--heads", "--", $Remote, $remoteReference)
    $matchingRemoteRefs = @(
        $remoteLookupResult.Stdout -split '\r?\n' |
            Where-Object {
                $fields = $_ -split "`t", 2
                $fields.Count -eq 2 -and $fields[1] -ceq $remoteReference -and
                    $fields[0] -match '^[0-9a-fA-F]+$'
            }
    )
    if ($remoteLookupResult.ExitCode -ne 0 -or $matchingRemoteRefs.Count -ne 1) {
        throw "Base commit not found: $DisplayName. The explicit remote base could not be verified without updating refs.$(Format-GitContext $remoteLookupResult.Output)"
    }
}

function Resolve-RemoteDefaultBranchWithoutFetch {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Remote,

        [Parameter(Mandatory = $true)]
        [string]$DisplayName
    )

    $defaultLookupResult = Invoke-GitCommand -Arguments @("ls-remote", "--symref", "--", $Remote, "HEAD")
    $defaultReference = @(
        $defaultLookupResult.Stdout -split '\r?\n' |
            Where-Object { $_ -match '^ref:\s+(refs/heads/[^\s]+)\s+HEAD$' } |
            ForEach-Object { $Matches[1] }
    )
    if ($defaultLookupResult.ExitCode -ne 0 -or $defaultReference.Count -ne 1) {
        throw "Remote default branch could not be resolved for $DisplayName without updating refs.$(Format-GitContext $defaultLookupResult.Output)"
    }

    $branch = $defaultReference[0].Substring('refs/heads/'.Length)
    $branchValidation = Invoke-GitCommand -Arguments @("check-ref-format", "--branch", $branch)
    if ($branchValidation.ExitCode -ne 0 -or
        [string]::IsNullOrWhiteSpace($branchValidation.Stdout) -or
        $branchValidation.Stdout.Trim() -cne $branch) {
        throw "Remote default branch is invalid for $DisplayName.$(Format-GitContext $branchValidation.Output)"
    }
    Assert-RemoteBaseExistsWithoutFetch -Remote $Remote -Branch $branch -DisplayName $DisplayName
    return $branch
}

function Assert-SafeWorktreeRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    try {
        $rootItem = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    }
    catch [System.Management.Automation.ItemNotFoundException] {
        return
    }
    catch {
        throw "Unsafe worktree root: '$Path' could not be inspected. $($_.Exception.Message)"
    }
    if (-not $rootItem.PSIsContainer) {
        throw "Unsafe worktree root: '$Path' exists but is not a directory."
    }
    if (($rootItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Unsafe worktree root: '$Path' is a reparse point or symbolic link."
    }
}

$repoRootResult = Invoke-GitCommand -Arguments @("rev-parse", "--show-toplevel")
if ($repoRootResult.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($repoRootResult.Stdout)) {
    throw "Run this script from inside a git repository.$(Format-GitContext $repoRootResult.Output)"
}

$repoRoot = [System.IO.Path]::GetFullPath($repoRootResult.Stdout.Trim()).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
$pathComparison = if ([System.IO.Path]::DirectorySeparatorChar -eq [char]'\') {
    [System.StringComparison]::OrdinalIgnoreCase
}
else {
    [System.StringComparison]::Ordinal
}
$gitDirectoryResult = Invoke-GitCommand -Arguments @("-C", $repoRoot, "rev-parse", "--absolute-git-dir")
$gitCommonDirectoryResult = Invoke-GitCommand -Arguments @("-C", $repoRoot, "rev-parse", "--path-format=absolute", "--git-common-dir")
if ($gitDirectoryResult.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($gitDirectoryResult.Stdout) -or
    $gitCommonDirectoryResult.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($gitCommonDirectoryResult.Stdout)) {
    throw "Git could not verify that the helper is running from the main checkout.$(Format-GitContext "$($gitDirectoryResult.Output)`n$($gitCommonDirectoryResult.Output)")"
}
$gitDirectory = [System.IO.Path]::GetFullPath($gitDirectoryResult.Stdout.Trim()).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
$gitCommonDirectory = [System.IO.Path]::GetFullPath($gitCommonDirectoryResult.Stdout.Trim()).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
if (-not $gitDirectory.Equals($gitCommonDirectory, $pathComparison)) {
    throw "Run this helper from the repository's main checkout; linked source worktrees are not allowed."
}
Set-Location -LiteralPath $repoRoot
$expectedHelperPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "scripts/git/New-CodexIssueWorktree.ps1"))
$invokedHelperPath = [System.IO.Path]::GetFullPath($PSCommandPath)
if (-not $invokedHelperPath.Equals($expectedHelperPath, $pathComparison)) {
    throw "Invoked helper path '$invokedHelperPath' does not match the current repository's reviewed helper '$expectedHelperPath'."
}
$reviewedHandoffArtifacts = Get-ReviewedHandoffArtifacts -Repository $repoRoot

if ([System.IO.Path]::IsPathRooted($WorktreeRoot)) {
    throw "Invalid worktree root: '$WorktreeRoot'. Codex issue worktrees must use the repository's .worktrees directory."
}

$logicalWorktreeRoot = $WorktreeRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
if (-not $logicalWorktreeRoot.Equals(".worktrees", [System.StringComparison]::Ordinal)) {
    throw "Invalid worktree root: '$WorktreeRoot'. Codex issue worktrees must use the repository's .worktrees directory."
}

$expectedWorktreeRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot ".worktrees")).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
$requestedWorktreeRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $WorktreeRoot)).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
if (-not $requestedWorktreeRoot.Equals($expectedWorktreeRoot, $pathComparison)) {
    throw "Invalid worktree root: '$WorktreeRoot'. Codex issue worktrees must use the repository's .worktrees directory."
}
Assert-SafeWorktreeRoot -Path $requestedWorktreeRoot

if ($Slug -cnotmatch "^[a-z0-9][a-z0-9-]{1,60}\z") {
    throw "Invalid slug: '$Slug'. Use 2-61 lowercase letters, digits, or hyphens, starting with a letter or digit."
}

if ([string]::IsNullOrWhiteSpace($BranchName)) {
    $BranchName = "issue-$IssueNumber/$Slug"
}

$branchValidationResult = Invoke-GitCommand -Arguments @("check-ref-format", "--branch", $BranchName)
if ($branchValidationResult.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($branchValidationResult.Stdout) -or $branchValidationResult.Stdout.Trim() -cne $BranchName) {
    throw "Invalid branch name: $BranchName$(Format-GitContext $branchValidationResult.Output)"
}
$windowsInvalidRefCharacters = [char[]]@('<', '>', ':', '"', '\', '|', '?', '*')
$windowsReservedRefComponent = '^(?i:CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|COM(?:[1-9]|\u00B9|\u00B2|\u00B3)|LPT(?:[1-9]|\u00B9|\u00B2|\u00B3))(?:\.|$)'
$branchComponents = @($BranchName -split '/')
$hasWindowsIncompatibleRefComponent = $false
for ($componentIndex = 0; $componentIndex -lt $branchComponents.Count; $componentIndex++) {
    $component = $branchComponents[$componentIndex]
    $maximumComponentLength = if ($componentIndex -eq ($branchComponents.Count - 1)) { 250 } else { 255 }
    if ($component -match $windowsReservedRefComponent -or
        $component.EndsWith('.') -or
        $component.EndsWith(' ') -or
        $component.Length -gt $maximumComponentLength) {
        $hasWindowsIncompatibleRefComponent = $true
        break
    }
}
if ($BranchName.IndexOfAny($windowsInvalidRefCharacters) -ge 0 -or $hasWindowsIncompatibleRefComponent) {
    throw "Invalid branch name for Windows-compatible worktrees: $BranchName"
}

$worktreeDir = Join-Path $requestedWorktreeRoot "codex-$IssueNumber-$Slug"
Assert-WorktreeTargetAvailable -Worktree $worktreeDir
$branchLookupResult = Invoke-GitCommand -Arguments @("show-ref", "--verify", "--quiet", "refs/heads/$BranchName")
if ($branchLookupResult.ExitCode -eq 0) {
    throw "Branch already exists: $BranchName"
}
if ($branchLookupResult.ExitCode -ne 1) {
    throw "Git could not check branch '$BranchName' (exit code $($branchLookupResult.ExitCode)).$(Format-GitContext $branchLookupResult.Output)"
}
$branchInventoryResult = Invoke-GitCommand -Arguments @("for-each-ref", "--format=%(refname)", "--", "refs/heads/")
if ($branchInventoryResult.ExitCode -ne 0) {
    throw "Git could not inspect the local branch namespace.$(Format-GitContext $branchInventoryResult.Output)"
}
$namespaceConflicts = @(
    $branchInventoryResult.Stdout -split '\r?\n' |
        Where-Object { $_.StartsWith('refs/heads/', [System.StringComparison]::Ordinal) } |
        ForEach-Object { $_.Substring('refs/heads/'.Length) } |
        Where-Object {
            $_.Equals($BranchName, [System.StringComparison]::OrdinalIgnoreCase) -or
            $BranchName.StartsWith("$_/", [System.StringComparison]::OrdinalIgnoreCase) -or
            $_.StartsWith("$BranchName/", [System.StringComparison]::OrdinalIgnoreCase)
        }
)
if ($namespaceConflicts.Count -gt 0) {
    throw "Branch namespace conflicts with existing branch '$($namespaceConflicts[0])': $BranchName"
}

$candidateRemote = $null
$candidateBranch = $null
$remoteListResult = Invoke-GitCommand -Arguments @("remote")
if ($remoteListResult.ExitCode -ne 0) {
    throw "Git could not enumerate configured remotes.$(Format-GitContext $remoteListResult.Output)"
}
$configuredRemotes = @(
    $remoteListResult.Stdout -split '\r?\n' |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Sort-Object { $_.Length } -Descending
)
if (-not $BaseBranch.StartsWith("refs/", [System.StringComparison]::Ordinal)) {
    $remotePrefixMatches = @(
        foreach ($remoteNameCandidate in $configuredRemotes) {
            $remotePrefix = "$remoteNameCandidate/"
            if ($BaseBranch.Length -gt $remotePrefix.Length -and
                $BaseBranch.StartsWith($remotePrefix, $pathComparison)) {
                [pscustomobject]@{
                    Remote = $remoteNameCandidate
                    Prefix = $remotePrefix
                    ExactCase = $BaseBranch.StartsWith($remotePrefix, [System.StringComparison]::Ordinal)
                }
            }
        }
    )
    if ($remotePrefixMatches.Count -gt 0) {
        $longestPrefixLength = $remotePrefixMatches[0].Prefix.Length
        $longestMatches = @($remotePrefixMatches | Where-Object { $_.Prefix.Length -eq $longestPrefixLength })
        $exactCaseMatches = @($longestMatches | Where-Object { $_.ExactCase })
        if ($exactCaseMatches.Count -eq 1) {
            $selectedRemoteMatch = $exactCaseMatches[0]
        }
        elseif ($exactCaseMatches.Count -eq 0 -and $longestMatches.Count -eq 1) {
            $selectedRemoteMatch = $longestMatches[0]
        }
        else {
            throw "Base branch remote prefix is ambiguous by case: $BaseBranch"
        }

        $candidateRemote = $selectedRemoteMatch.Remote
        $remoteBranchCandidate = $BaseBranch.Substring($selectedRemoteMatch.Prefix.Length)
        if ($remoteBranchCandidate -ceq "HEAD") {
            $candidateBranch = Resolve-RemoteDefaultBranchWithoutFetch -Remote $candidateRemote -DisplayName $BaseBranch
        }
        else {
            $remoteBranchValidation = Invoke-GitCommand -Arguments @("check-ref-format", "--branch", $remoteBranchCandidate)
            if ($remoteBranchValidation.ExitCode -ne 0 -or
                [string]::IsNullOrWhiteSpace($remoteBranchValidation.Stdout) -or
                $remoteBranchValidation.Stdout.Trim() -cne $remoteBranchCandidate) {
                throw "Invalid remote branch in base: $BaseBranch$(Format-GitContext $remoteBranchValidation.Output)"
            }
            $candidateBranch = $remoteBranchCandidate
        }
    }
}

$baseCommitReference = if ($null -ne $candidateRemote) {
    "refs/remotes/$candidateRemote/$candidateBranch"
}
else {
    $BaseBranch
}

if ($WhatIfPreference) {
    if ($null -ne $candidateRemote) {
        Assert-RemoteBaseExistsWithoutFetch -Remote $candidateRemote -Branch $candidateBranch -DisplayName $BaseBranch
    }
    else {
        $whatIfBaseCommit = Resolve-BaseCommit -Repository $repoRoot -Reference $baseCommitReference -DisplayName $BaseBranch
        Assert-BaseMatchesReviewedHandoffArtifacts -Repository $repoRoot -Commit $whatIfBaseCommit -DisplayName $BaseBranch -ReviewedArtifacts $reviewedHandoffArtifacts
    }
}

if ($PSCmdlet.ShouldProcess($worktreeDir, "Refresh the base when remote and create a detached worktree from $BaseBranch")) {
    if ($null -ne $candidateRemote) {
        $remoteRefspec = "+refs/heads/$candidateBranch`:refs/remotes/$candidateRemote/$candidateBranch"
        $fetchResult = Invoke-GitCommand -Arguments @("fetch", "--no-tags", "--no-recurse-submodules", "--", $candidateRemote, $remoteRefspec)
        if ($fetchResult.ExitCode -ne 0) {
            throw "Base commit not found: $BaseBranch. Failed to refresh the explicit remote base.$(Format-GitContext $fetchResult.Output)"
        }
    }

    $baseCommit = Resolve-BaseCommit -Repository $repoRoot -Reference $baseCommitReference -DisplayName $BaseBranch
    Assert-BaseMatchesReviewedHandoffArtifacts -Repository $repoRoot -Commit $baseCommit -DisplayName $BaseBranch -ReviewedArtifacts $reviewedHandoffArtifacts

    $worktreeAdded = $false
    try {
        Reserve-WorktreeTarget -WorktreeRoot $requestedWorktreeRoot -Worktree $worktreeDir

        $worktreeAddResult = Invoke-GitCommand -Arguments @("worktree", "add", "--detach", $worktreeDir, $baseCommit)
        if ($worktreeAddResult.ExitCode -ne 0) {
            Remove-EmptyReservedWorktreeTarget -Worktree $worktreeDir
            throw "git worktree add failed for '$worktreeDir' from '$BaseBranch' (exit code $($worktreeAddResult.ExitCode)).$(Format-GitContext $worktreeAddResult.Output)"
        }
        $worktreeAdded = $true
        Assert-TargetMatchesReviewedHandoffArtifacts -Repository $repoRoot -Worktree $worktreeDir -ReviewedArtifacts $reviewedHandoffArtifacts
        if (-not [string]::IsNullOrWhiteSpace($worktreeAddResult.Output)) {
            Write-Host $worktreeAddResult.Output
        }
    }
    catch {
        $creationFailure = $_
        if ($worktreeAdded) {
            try {
                Remove-HelperCreatedWorktree -Worktree $worktreeDir -ExpectedHead $baseCommit -ReviewedArtifacts $reviewedHandoffArtifacts
            }
            catch {
                throw "$($creationFailure.Exception.Message) Cleanup also failed: $($_.Exception.Message)"
            }
        }
        throw $creationFailure
    }

    $escapedBranchName = $BranchName.Replace("'", "''")
    $escapedGitExecutable = $gitExecutable.Replace("'", "''")
    $escapedWorktreeDir = $worktreeDir.Replace("'", "''")
    $initializerPath = [System.IO.Path]::GetFullPath((Join-Path $worktreeDir "scripts/git/Initialize-CodexIssueWorktree.ps1"))
    $escapedInitializerPath = $initializerPath.Replace("'", "''")
    $initializerInvocation = "& '$escapedInitializerPath' -GitExecutable '$escapedGitExecutable' -BranchName '$escapedBranchName' -ExpectedWorktree '$escapedWorktreeDir' -ExpectedHead '$baseCommit'"
    $guardPath = [System.IO.Path]::GetFullPath((Join-Path $worktreeDir "scripts/worktree_guard.ps1"))
    $escapedGuardPath = $guardPath.Replace("'", "''")
    $guardInvocation = "& '$escapedGuardPath' -GitExecutable '$escapedGitExecutable'"

    Write-Host "Created detached Codex issue worktree."
    Write-Host "  issue:          #$IssueNumber"
    Write-Host "  base:           $BaseBranch ($baseCommit)"
    Write-Host "  planned branch: $BranchName (not created yet)"
    Write-Host "  worktree:       $worktreeDir"
    Write-Host ""
    $guardAllowRule = "PowerShell($guardInvocation)"
    $initializerAllowRule = "PowerShell($initializerInvocation)"
    Write-Host "Claude Code task-scoped handoff allow rules (additive PowerShell transport):"
    Write-Host "`$guardAllowRule = @'"
    Write-Host $guardAllowRule
    Write-Host "'@"
    Write-Host "`$initializerAllowRule = @'"
    Write-Host $initializerAllowRule
    Write-Host "'@"
    Write-Host '$handoffAllowRules = @($guardAllowRule, $initializerAllowRule)'
    Write-Host '# Pass as two argv values: claude ... --allowedTools $handoffAllowRules --permission-mode dontAsk <reviewed task prompt>'
    Write-Host ""
    Write-Host "PowerShell worker handoff (run this entire block unchanged):"
    Write-Host "  $guardInvocation"
    Write-Host '  $guardSucceeded = $?; $guardExitCode = $LASTEXITCODE'
    Write-Host '  if (-not $guardSucceeded -or $guardExitCode -ne 0) { if ($null -ne $guardExitCode -and $guardExitCode -ne 0) { exit $guardExitCode }; exit 1 }'
    Write-Host "  $initializerInvocation"
    Write-Host '  $handoffSucceeded = $?; $handoffExitCode = $LASTEXITCODE'
    Write-Host '  if (-not $handoffSucceeded -or $handoffExitCode -ne 0) { if ($null -ne $handoffExitCode -and $handoffExitCode -ne 0) { exit $handoffExitCode }; exit 1 }'
}
