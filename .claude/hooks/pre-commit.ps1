$ErrorActionPreference = "Stop"

$stdin = [Console]::In.ReadToEnd()
if (-not [string]::IsNullOrWhiteSpace($stdin)) {
    try {
        $payload = $stdin | ConvertFrom-Json
        $toolName = [string]$payload.tool_name
        $command = [string]$payload.tool_input.command
        if ($toolName -ne "Bash" -or $command -notmatch "\bgit\s+commit\b") {
            exit 0
        }
    }
    catch {
        exit 0
    }
}

$root = git rev-parse --show-toplevel
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($root)) {
    [Console]::Error.WriteLine("Unable to resolve git repository root.")
    exit 2
}
Set-Location $root.Trim()

$staged = git diff --cached --name-only 2>$null
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine("Unable to inspect staged files.")
    exit 2
}

$hasCs = $false
$hasVueOrTs = $false
foreach ($path in $staged) {
    if ($path -match "\.cs$") {
        $hasCs = $true
    }
    if ($path -match "\.(vue|ts)$") {
        $hasVueOrTs = $true
    }
}

$errors = @()

if ($hasCs) {
    $result = dotnet build backend/Taskdeck.sln -c Release --nologo -v q 2>&1
    if ($LASTEXITCODE -ne 0) {
        $errors += "Backend build failed:`n$result"
    }
}

if ($hasVueOrTs) {
    Push-Location frontend/taskdeck-web
    try {
        $result = npx vue-tsc --noEmit 2>&1
        $code = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
    if ($code -ne 0) {
        $errors += "Frontend typecheck failed:`n$result"
    }
}

if ($errors.Count -gt 0) {
    [Console]::Error.WriteLine("PRE-COMMIT CHECK FAILED:")
    foreach ($errorText in $errors) {
        [Console]::Error.WriteLine($errorText)
    }
    exit 2
}

exit 0
