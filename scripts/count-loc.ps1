<# 

## Utility Scripts

Count git-aware lines of code (respects `.gitignore` and skips common generated/IDE folders):

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\count-loc.ps1
```

Examples:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\count-loc.ps1 -Top 15
powershell -ExecutionPolicy Bypass -File .\scripts\count-loc.ps1 -IncludeExtension .cs,.ts,.vue
```

#> 

param(
    [string]$RepoRoot,
    [string[]]$IncludeExtension,
    [ValidateRange(1, 500)]
    [int]$Top = 20
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-GitPath {
    $candidate = "C:\Program Files\Git\cmd\git.exe"
    if (Test-Path $candidate) {
        return $candidate
    }

    return (Get-Command git -ErrorAction Stop).Source
}

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryPath,
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $excludeFile = Join-Path $RepositoryPath ".git\info\exclude"
    $gitArguments = @(
        "-c",
        "safe.directory=*",
        "-c",
        "core.quotePath=false",
        "-c",
        "core.excludesfile=$excludeFile",
        "-C",
        $RepositoryPath
    ) + $Arguments
    $previousGlobalConfig = $env:GIT_CONFIG_GLOBAL
    $env:GIT_CONFIG_GLOBAL = "NUL"

    try {
        $output = & $script:GitExe @gitArguments
        if ($LASTEXITCODE -ne 0) {
            $argString = $gitArguments -join " "
            throw "Git command failed: $script:GitExe $argString"
        }

        return @($output)
    } finally {
        if ($null -eq $previousGlobalConfig) {
            Remove-Item Env:GIT_CONFIG_GLOBAL -ErrorAction SilentlyContinue
        } else {
            $env:GIT_CONFIG_GLOBAL = $previousGlobalConfig
        }
    }
}

function Should-SkipByDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RelativePath,
        [Parameter(Mandatory = $true)]
        [System.Collections.Generic.HashSet[string]]$IgnoredDirectories
    )

    $segments = $RelativePath -split "[/\\]"
    foreach ($segment in $segments) {
        if ($IgnoredDirectories.Contains($segment)) {
            return $true
        }
    }

    return $false
}

function Test-BinaryFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $sampleSize = 4096
        $buffer = New-Object byte[] $sampleSize
        $bytesRead = $stream.Read($buffer, 0, $sampleSize)
        for ($index = 0; $index -lt $bytesRead; $index++) {
            if ($buffer[$index] -eq 0) {
                return $true
            }
        }

        return $false
    } finally {
        $stream.Dispose()
    }
}

function Get-LineCount {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $lineCount = 0
    $reader = [System.IO.File]::OpenText($Path)
    try {
        while ($null -ne $reader.ReadLine()) {
            $lineCount++
        }
    } finally {
        $reader.Dispose()
    }

    return $lineCount
}

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Join-Path $PSScriptRoot ".."
}

$RepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)
if (-not (Test-Path $RepoRoot -PathType Container)) {
    throw "Repository path does not exist: $RepoRoot"
}

$script:GitExe = Get-GitPath

$extensionFilter = $null
if ($IncludeExtension -and $IncludeExtension.Count -gt 0) {
    $extensionFilter = New-Object "System.Collections.Generic.HashSet[string]" ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($extension in $IncludeExtension) {
        if ([string]::IsNullOrWhiteSpace($extension)) {
            continue
        }

        $tokens = $extension -split "[,; ]+"
        foreach ($token in $tokens) {
            if ([string]::IsNullOrWhiteSpace($token)) {
                continue
            }

            $normalized = $token.Trim()
            if (-not $normalized.StartsWith(".")) {
                $normalized = ".{0}" -f $normalized
            }

            [void]$extensionFilter.Add($normalized)
        }
    }

    if ($extensionFilter.Count -eq 0) {
        throw "IncludeExtension filter was provided but no valid extensions were found."
    }
}

$ignoredDirectories = New-Object "System.Collections.Generic.HashSet[string]" ([System.StringComparer]::OrdinalIgnoreCase)
$defaultIgnoredDirectories = @(
    ".git",
    ".idea",
    ".vs",
    "node_modules",
    "bin",
    "obj",
    "dist",
    "coverage",
    "test-results",
    "playwright-report",
    "docs",
    "filesAndResources"
)

foreach ($directory in $defaultIgnoredDirectories) {
    [void]$ignoredDirectories.Add($directory)
}

$fileLines = Invoke-Git -RepositoryPath $RepoRoot -Arguments @(
    "ls-files",
    "--cached",
    "--others",
    "--exclude-standard"
)

$relativePaths = New-Object System.Collections.Generic.List[string]
foreach ($line in $fileLines) {
    $candidate = [string]$line
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        continue
    }

    $candidate = $candidate.Trim()
    if ($candidate.StartsWith("warning:", [System.StringComparison]::OrdinalIgnoreCase)) {
        continue
    }

    [void]$relativePaths.Add($candidate)
}

if ($relativePaths.Count -eq 0) {
    Write-Host "No files found for '$RepoRoot'."
    exit 0
}

$totalDiscovered = 0
$countedFiles = 0
$filterSkipped = 0
$binarySkipped = 0
$missingSkipped = 0
$errorSkipped = 0
[long]$totalLines = 0
$results = New-Object System.Collections.Generic.List[object]

foreach ($relativePath in $relativePaths) {
    $totalDiscovered++

    if (Should-SkipByDirectory -RelativePath $relativePath -IgnoredDirectories $ignoredDirectories) {
        $filterSkipped++
        continue
    }

    if ($extensionFilter) {
        $extension = [System.IO.Path]::GetExtension($relativePath)
        if (-not $extensionFilter.Contains($extension)) {
            $filterSkipped++
            continue
        }
    }

    $fullPath = Join-Path $RepoRoot $relativePath
    if (-not (Test-Path $fullPath -PathType Leaf)) {
        $missingSkipped++
        continue
    }

    try {
        if (Test-BinaryFile -Path $fullPath) {
            $binarySkipped++
            continue
        }

        $lines = Get-LineCount -Path $fullPath
        $countedFiles++
        $totalLines += $lines

        [void]$results.Add([PSCustomObject]@{
            Lines = $lines
            Path = $relativePath
        })
    } catch {
        $errorSkipped++
    }
}

$topLimit = [Math]::Max(1, $Top)
$topFiles = $results |
    Sort-Object -Property @{ Expression = "Lines"; Descending = $true }, @{ Expression = "Path"; Descending = $false } |
    Select-Object -First $topLimit

Write-Host ""
Write-Host "Repository: $RepoRoot"
Write-Host "Git executable: $script:GitExe"
Write-Host "Files discovered (git-aware): $totalDiscovered"
Write-Host "Files counted (text): $countedFiles"
Write-Host "Total lines: $totalLines"

if ($extensionFilter) {
    $extensionSummary = ($extensionFilter | Sort-Object) -join ", "
    Write-Host "Extension filter: $extensionSummary"
}

if ($filterSkipped -gt 0) {
    Write-Host "Skipped by filters: $filterSkipped"
}

if ($binarySkipped -gt 0) {
    Write-Host "Skipped binary files: $binarySkipped"
}

if ($missingSkipped -gt 0) {
    Write-Host "Skipped missing files: $missingSkipped"
}

if ($errorSkipped -gt 0) {
    Write-Host "Skipped unreadable files: $errorSkipped"
}

$topFiles = @($topFiles)
if ($topFiles.Count -gt 0) {
    Write-Host ""
    Write-Host "Top $($topFiles.Count) files by line count:"
    $topFiles | Format-Table -AutoSize
} else {
    Write-Host ""
    Write-Host "No text files matched the current filters."
}
