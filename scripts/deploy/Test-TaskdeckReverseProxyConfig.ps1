$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$standalonePath = Join-Path $repoRoot 'deploy/nginx/reverse-proxy.conf'
$userDataPath = Join-Path $repoRoot 'deploy/terraform/aws/modules/single_node/user_data.sh.tftpl'

function Normalize-Config([string]$content) {
    return (($content -replace "`r`n", "`n") -replace "`r", "`n").Trim()
}

function Assert-Equal([string]$expected, [string]$actual, [string]$message) {
    if ($expected -cne $actual) {
        throw "$message`nExpected: $expected`nActual:   $actual"
    }
}

function Assert-Contains([string]$content, [string]$needle, [string]$message) {
    if (-not $content.Contains($needle)) {
        throw "$message`nMissing: $needle"
    }
}

function Get-MachineRoutes([string]$content, [string]$sourceName) {
    $routePattern = '(?ms)^\s*location ~ \^/(?<prefix>api|hubs|health|mcp)\(\?:/\|\$\) \{\r?\n(?<body>.*?)^\s*\}'
    $matches = [regex]::Matches($content, $routePattern)
    if ($matches.Count -ne 4) {
        throw "$sourceName must contain exactly four machine-prefix location blocks; found $($matches.Count)."
    }

    $routes = @{}
    foreach ($match in $matches) {
        $prefix = $match.Groups['prefix'].Value
        if ($routes.ContainsKey($prefix)) {
            throw "$sourceName contains duplicate /$prefix machine-prefix location blocks."
        }
        $routes[$prefix] = Normalize-Config $match.Value
    }
    return $routes
}

$standalone = Normalize-Config (Get-Content -LiteralPath $standalonePath -Raw)
$userData = Get-Content -LiteralPath $userDataPath -Raw
$heredocMarker = "cat > /opt/taskdeck/reverse-proxy.conf <<'EOF'"
$markerStart = $userData.IndexOf($heredocMarker, [StringComparison]::Ordinal)
if ($markerStart -lt 0) {
    throw "Could not find the rendered reverse-proxy.conf heredoc in $userDataPath."
}

$configStart = $markerStart + $heredocMarker.Length
$configEnd = $userData.IndexOf("`nEOF", $configStart, [StringComparison]::Ordinal)
if ($configEnd -lt 0) {
    throw "Could not find the end of the rendered reverse-proxy.conf heredoc in $userDataPath."
}

$rendered = Normalize-Config $userData.Substring($configStart, $configEnd - $configStart)
$standaloneRoutes = Get-MachineRoutes $standalone 'deploy/nginx/reverse-proxy.conf'
$renderedRoutes = Get-MachineRoutes $rendered 'user_data.sh.tftpl rendered reverse-proxy.conf'

$expectedPrefixes = @('api', 'hubs', 'health', 'mcp')
$expectedPrefixSet = ($expectedPrefixes | Sort-Object) -join ','
Assert-Equal $expectedPrefixSet (($standaloneRoutes.Keys | Sort-Object) -join ',') 'Standalone config must cover all machine prefixes.'
Assert-Equal $expectedPrefixSet (($renderedRoutes.Keys | Sort-Object) -join ',') 'Rendered config must cover all machine prefixes.'

$machinePathRegexes = @{}
foreach ($prefix in $expectedPrefixes) {
    $machinePathRegexes[$prefix] = [regex]::new("^/$prefix(?:/|`$)")
    foreach ($path in @("/$prefix", "/$prefix/", "/$prefix/descendant")) {
        if (-not $machinePathRegexes[$prefix].IsMatch($path)) {
            throw "The /$prefix matcher does not cover $path."
        }
    }
    if ($machinePathRegexes[$prefix].IsMatch("/${prefix}x")) {
        throw "The /$prefix matcher must not capture /${prefix}x."
    }

    Assert-Equal $standaloneRoutes[$prefix] $renderedRoutes[$prefix] "Standalone and rendered /$prefix route blocks must remain in parity."
    $route = $standaloneRoutes[$prefix]
    Assert-Contains $route 'proxy_pass http://api:8080;' "/$prefix must proxy to the API without rewriting the URI."
    foreach ($header in @(
        'proxy_set_header Host $host;',
        'proxy_set_header X-Real-IP $remote_addr;',
        'proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;',
        'proxy_set_header X-Forwarded-Proto $scheme;',
        'proxy_set_header X-Forwarded-Host $host;'
    )) {
        Assert-Contains $route $header "/$prefix must preserve the forwarded-header contract."
    }
}

foreach ($prefix in @('api', 'hubs', 'mcp')) {
    Assert-Contains $standaloneRoutes[$prefix] 'proxy_read_timeout 120s;' "/$prefix must retain the 120s proxy timeout."
}
Assert-Contains $standaloneRoutes.health 'proxy_read_timeout 30s;' '/health must retain the 30s proxy timeout.'
Assert-Contains $standaloneRoutes.hubs 'proxy_set_header Upgrade $http_upgrade;' '/hubs must preserve WebSocket upgrade headers.'
Assert-Contains $standaloneRoutes.hubs 'proxy_set_header Connection $connection_upgrade;' '/hubs must preserve WebSocket connection semantics.'
foreach ($prefix in @('api', 'health', 'mcp')) {
    if ($standaloneRoutes[$prefix] -match 'proxy_set_header (Upgrade|Connection) ') {
        throw "/$prefix must not inherit /hubs WebSocket headers."
    }
}

Assert-Contains $standalone 'location / {' 'The SPA catch-all must remain present for non-machine paths.'
Assert-Contains $standalone 'proxy_pass http://web:8080/;' 'The SPA catch-all must continue to proxy to the web container.'
Write-Host 'Reverse-proxy static and rendered template contract passed.' -ForegroundColor Green
