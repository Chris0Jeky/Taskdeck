param(
    [string]$DefaultServers = 'docker,docker-docs,openapi,time,jetbrains,filesystem,SQLite,terraform',
    [string]$OptionalServers = 'postman,dockerhub',
    [switch]$IncludeOptional,
    [switch]$FailOnOptionalErrors
)

$ErrorActionPreference = 'Stop'

Write-Host '=== Docker MCP Enabled Servers ==='
docker mcp server ls
if ($LASTEXITCODE -ne 0) {
    throw 'Failed to list Docker MCP servers.'
}

Write-Host ''
Write-Host "=== Docker MCP Gateway Dry-Run (Default): $DefaultServers ==="
docker mcp gateway run --dry-run --servers $DefaultServers
if ($LASTEXITCODE -ne 0) {
    throw 'Default Docker MCP server dry-run failed.'
}

if ($IncludeOptional) {
    Write-Host ''
    Write-Host "=== Docker MCP Gateway Dry-Run (Optional): $OptionalServers ==="
    docker mcp gateway run --dry-run --servers $OptionalServers
    $optionalExit = $LASTEXITCODE
    if ($optionalExit -ne 0) {
        if ($FailOnOptionalErrors) {
            throw 'Optional Docker MCP server dry-run failed.'
        }

        Write-Warning 'Optional Docker MCP dry-run failed. Verify credentials/config for optional servers.'
        exit 0
    }
}

Write-Host ''
Write-Host 'Docker MCP profile checks passed.'

