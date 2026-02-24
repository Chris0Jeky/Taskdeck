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
$streamDrainTimeoutMilliseconds = 15000

Remove-Item -LiteralPath $resolvedOutputPath, $stdoutLogPath, $stderrLogPath -ErrorAction SilentlyContinue

$apiProjectPath = Join-Path $repoRoot "backend/src/Taskdeck.Api/Taskdeck.Api.csproj"
$swaggerEndpoint = "http://127.0.0.1:$Port/swagger/v1/swagger.json"

$previousUrls = $env:ASPNETCORE_URLS
$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT

$env:ASPNETCORE_URLS = "http://127.0.0.1:$Port"
$env:ASPNETCORE_ENVIRONMENT = "Development"

$apiProcess = $null
$stdoutLogStream = $null
$stderrLogStream = $null
$stdoutCopyTask = $null
$stderrCopyTask = $null

function Complete-CopyTask
{
    param(
        [Parameter(Mandatory = $true)]
        [System.Threading.Tasks.Task]$Task,
        [Parameter(Mandatory = $true)]
        [string]$LogPath,
        [Parameter(Mandatory = $true)]
        [string]$StreamName,
        [Parameter(Mandatory = $true)]
        [int]$TimeoutMilliseconds
    )

    try
    {
        $completed = $Task.Wait($TimeoutMilliseconds)
        if (-not $completed)
        {
            Add-Content -LiteralPath $LogPath -Value "Timed out waiting for $StreamName stream drain after $TimeoutMilliseconds ms."
            return
        }

        if ($Task.IsFaulted -and $Task.Exception)
        {
            Add-Content -LiteralPath $LogPath -Value "Failed to finalize $StreamName capture: $($Task.Exception.GetBaseException().Message)"
        }
        elseif ($Task.IsCanceled)
        {
            Add-Content -LiteralPath $LogPath -Value "Finalizing $StreamName capture was canceled."
        }
    }
    catch
    {
        Add-Content -LiteralPath $LogPath -Value "Failed to finalize $StreamName capture: $($_.Exception.Message)"
    }
}

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

    $stdoutLogStream = [System.IO.FileStream]::new(
        $stdoutLogPath,
        [System.IO.FileMode]::Create,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::ReadWrite)
    $stderrLogStream = [System.IO.FileStream]::new(
        $stderrLogPath,
        [System.IO.FileMode]::Create,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::ReadWrite)

    $stdoutCopyTask = $apiProcess.StandardOutput.BaseStream.CopyToAsync($stdoutLogStream)
    $stderrCopyTask = $apiProcess.StandardError.BaseStream.CopyToAsync($stderrLogStream)

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
            $processExitWaitMilliseconds = 5000
            if (-not $apiProcess.WaitForExit($processExitWaitMilliseconds))
            {
                Add-Content -LiteralPath $stderrLogPath -Value "Taskdeck.Api process did not exit within $processExitWaitMilliseconds ms during cleanup."
            }
        }
        catch
        {
            # Ignore wait races during cleanup.
        }
    }

    if ($stdoutCopyTask)
    {
        Complete-CopyTask -Task $stdoutCopyTask -LogPath $stdoutLogPath -StreamName "stdout" -TimeoutMilliseconds $streamDrainTimeoutMilliseconds
    }

    if ($stderrCopyTask)
    {
        Complete-CopyTask -Task $stderrCopyTask -LogPath $stderrLogPath -StreamName "stderr" -TimeoutMilliseconds $streamDrainTimeoutMilliseconds
    }

    if ($stdoutLogStream)
    {
        try
        {
            $stdoutLogStream.Flush()
            $stdoutLogStream.Dispose()
        }
        catch
        {
            # Ignore stdout stream cleanup races.
        }
    }

    if ($stderrLogStream)
    {
        try
        {
            $stderrLogStream.Flush()
            $stderrLogStream.Dispose()
        }
        catch
        {
            # Ignore stderr stream cleanup races.
        }
    }

    if ($apiProcess)
    {
        try
        {
            $apiProcess.Dispose()
        }
        catch
        {
            # Ignore process disposal races.
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
