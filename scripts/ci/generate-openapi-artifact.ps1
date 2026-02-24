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
$stdoutReadTask = $null
$stderrReadTask = $null
try
{
    dotnet restore $apiProjectPath
    dotnet build $apiProjectPath -c Release --no-restore

    $processStartInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $processStartInfo.FileName = "dotnet"
    $processStartInfo.WorkingDirectory = $repoRoot
    $processStartInfo.UseShellExecute = $false
    $processStartInfo.RedirectStandardOutput = $true
    $processStartInfo.RedirectStandardError = $true
    $escapedProjectPath = '"' + ($apiProjectPath -replace '"', '\"') + '"'
    $processStartInfo.Arguments = "run --project $escapedProjectPath --configuration Release --no-build --no-launch-profile"

    $apiProcess = [System.Diagnostics.Process]::new()
    $apiProcess.StartInfo = $processStartInfo
    $apiProcess.EnableRaisingEvents = $true
    if (-not $apiProcess.Start())
    {
        throw "Failed to start Taskdeck.Api process for OpenAPI generation."
    }

    $stdoutReadTask = $apiProcess.StandardOutput.ReadToEndAsync()
    $stderrReadTask = $apiProcess.StandardError.ReadToEndAsync()

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
    $stdoutLogContent = ""
    $stderrLogContent = ""

    if ($apiProcess)
    {
        try
        {
            if (-not $apiProcess.HasExited)
            {
                $killWithTree = $apiProcess.GetType().GetMethod("Kill", [Type[]]@([bool]))
                if ($null -ne $killWithTree)
                {
                    $apiProcess.Kill($true)
                }
                else
                {
                    $apiProcess.Kill()
                }
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

        try
        {
            if ($stdoutReadTask)
            {
                $stdoutLogContent = $stdoutReadTask.GetAwaiter().GetResult()
            }
        }
        catch
        {
            $stdoutLogContent = "Failed to capture stdout: $($_.Exception.Message)"
        }

        try
        {
            if ($stderrReadTask)
            {
                $stderrLogContent = $stderrReadTask.GetAwaiter().GetResult()
            }
        }
        catch
        {
            $stderrLogContent = "Failed to capture stderr: $($_.Exception.Message)"
        }

        try
        {
            $apiProcess.Dispose()
        }
        catch
        {
            # Ignore dispose races.
        }
    }

    Set-Content -LiteralPath $stdoutLogPath -Value $stdoutLogContent -Encoding UTF8
    Set-Content -LiteralPath $stderrLogPath -Value $stderrLogContent -Encoding UTF8

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
