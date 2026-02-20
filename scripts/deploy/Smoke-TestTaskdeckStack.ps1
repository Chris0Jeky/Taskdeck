param(
    [int]$Port = 8080
)

$ErrorActionPreference = 'Stop'

function Invoke-ExpectedStatus {
    param(
        [Parameter(Mandatory = $true)] [string]$Uri,
        [Parameter(Mandatory = $true)] [int]$ExpectedStatus,
        [string]$Method = 'GET',
        [string]$Body = '',
        [string]$ContentType = 'application/json'
    )

    try {
        if ($Method -eq 'GET') {
            $response = Invoke-WebRequest -Uri $Uri -UseBasicParsing
        }
        else {
            $response = Invoke-WebRequest -Uri $Uri -Method $Method -Body $Body -ContentType $ContentType -UseBasicParsing
        }

        $statusCode = [int]$response.StatusCode
        $content = $response.Content
    }
    catch {
        if (-not $_.Exception.Response) {
            throw
        }

        $statusCode = [int]$_.Exception.Response.StatusCode
        $streamReader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $content = $streamReader.ReadToEnd()
        $streamReader.Close()
    }

    if ($statusCode -ne $ExpectedStatus) {
        throw "Unexpected status for '$Uri'. Expected $ExpectedStatus but got $statusCode. Body: $content"
    }

    return $content
}

$baseUrl = "http://localhost:$Port"

$rootContent = Invoke-ExpectedStatus -Uri "$baseUrl/" -ExpectedStatus 200
$healthContent = Invoke-ExpectedStatus -Uri "$baseUrl/health/ready" -ExpectedStatus 200
$boardsContent = Invoke-ExpectedStatus -Uri "$baseUrl/api/boards" -ExpectedStatus 401
$negotiateContent = Invoke-ExpectedStatus -Uri "$baseUrl/hubs/boards/negotiate?negotiateVersion=1" -ExpectedStatus 401 -Method 'POST' -Body '{}'

Write-Host 'Smoke checks passed.'
Write-Host "Root payload bytes: $($rootContent.Length)"
Write-Host "Health payload bytes: $($healthContent.Length)"
Write-Host "Boards unauthorized payload: $boardsContent"
Write-Host "SignalR negotiate unauthorized payload: $negotiateContent"
