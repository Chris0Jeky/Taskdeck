param(
    [string]$OutputPath = "artifacts/openapi/taskdeck-api.json",
    [int]$Port = 5079,
    [int]$StartupTimeoutSeconds = 90
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($PSScriptRoot, "..", ".."))
$resolvedOutputPath = if ([System.IO.Path]::IsPathRooted($OutputPath))
{
    $OutputPath
}
else
{
    Join-Path $repoRoot $OutputPath
}

$outputDirectory = Split-Path -Parent $resolvedOutputPath
New-Item -Path $outputDirectory -ItemType Directory -Force | Out-Null

$stdoutLogPath = Join-Path $outputDirectory "taskdeck-openapi-stdout.log"
$stderrLogPath = Join-Path $outputDirectory "taskdeck-openapi-stderr.log"

Remove-Item -LiteralPath $resolvedOutputPath, $stdoutLogPath, $stderrLogPath -ErrorAction SilentlyContinue

$apiProjectPath = Join-Path $repoRoot "backend/src/Taskdeck.Api/Taskdeck.Api.csproj"
$swaggerEndpoint = "http://127.0.0.1:$Port/swagger/v1/swagger.json"

$previousUrls = $env:ASPNETCORE_URLS
$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT

$env:ASPNETCORE_URLS = "http://127.0.0.1:$Port"
$env:ASPNETCORE_ENVIRONMENT = "Development"

$apiProcess = $null
try
{
    dotnet restore $apiProjectPath
    dotnet build $apiProjectPath -c Release --no-restore

    $apiProcess = Start-Process dotnet `
        -ArgumentList @("run", "--project", $apiProjectPath, "--configuration", "Release", "--no-build", "--no-launch-profile") `
        -PassThru `
        -RedirectStandardOutput $stdoutLogPath `
        -RedirectStandardError $stderrLogPath

    $isReady = $false
    for ($attempt = 1; $attempt -le $StartupTimeoutSeconds; $attempt += 1)
    {
        Start-Sleep -Seconds 1
        if ($apiProcess.HasExited)
        {
            throw "Taskdeck.Api exited before OpenAPI endpoint became available. See logs at $stdoutLogPath and $stderrLogPath."
        }

        try
        {
            Invoke-WebRequest -Uri $swaggerEndpoint -Method Get -TimeoutSec 5 -OutFile $resolvedOutputPath
            $isReady = $true
            break
        }
        catch
        {
            # Continue retrying until timeout.
        }
    }

    if (-not $isReady)
    {
        throw "Timed out waiting for OpenAPI endpoint at $swaggerEndpoint within $StartupTimeoutSeconds seconds."
    }

    & (Join-Path $PSScriptRoot "validate-openapi.ps1") -SpecPath $resolvedOutputPath

    Write-Host "Generated OpenAPI artifact at $resolvedOutputPath"
}
finally
{
    if ($apiProcess)
    {
        try
        {
            if (-not $apiProcess.HasExited)
            {
                Stop-Process -Id $apiProcess.Id -Force -ErrorAction SilentlyContinue
            }
        }
        catch
        {
            # Ignore process stop races during cleanup.
        }

        try
        {
            $apiProcess.WaitForExit()
        }
        catch
        {
            # Ignore wait races during cleanup.
        }
    }

    if ($null -eq $previousUrls)
    {
        Remove-Item Env:ASPNETCORE_URLS -ErrorAction SilentlyContinue
    }
    else
    {
        $env:ASPNETCORE_URLS = $previousUrls
    }

    if ($null -eq $previousEnvironment)
    {
        Remove-Item Env:ASPNETCORE_ENVIRONMENT -ErrorAction SilentlyContinue
    }
    else
    {
        $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment
    }
}
