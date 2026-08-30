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

<#
.SYNOPSIS
Extracts the two fail-closed rules a machine-path variant must hit (#1992 q-10 A, ADR-0064).

.DESCRIPTION
The rules are read OUT of the config and then executed against a request matrix below rather than
string-matched: a rule that is spelled plausibly and behaves wrong would pass a text assertion.
#>
function Get-FailClosedRules([string]$content, [string]$sourceName) {
    $guardPattern = '(?m)^[ \t]*if \(\$request_uri ~\* "(?<re>[^"]+)"\) \{\r?\n[ \t]*return 404;\r?\n[ \t]*\}'
    $guard = [regex]::Match($content, $guardPattern)
    if (-not $guard.Success) {
        throw "$sourceName must 404 a prefix-boundary encoded slash on the raw request URI before location matching; the server-level guard is missing."
    }

    $casePattern = '(?m)^[ \t]*location ~\* (?<re>\S+) \{\r?\n[ \t]*return 404;\r?\n[ \t]*\}'
    $caseVariant = [regex]::Match($content, $casePattern)
    if (-not $caseVariant.Success) {
        throw "$sourceName must 404 non-lowercase spellings of a machine prefix; the case-insensitive location block is missing."
    }

    # Ordering is the whole mechanism. The exact-lowercase locations are case-SENSITIVE and must be
    # declared before the case-insensitive one (nginx takes the first matching regex location), and
    # the case-insensitive one must precede the SPA catch-all it exists to keep variants out of.
    $machineLocations = [regex]::Matches($content, '(?m)^[ \t]*location ~ \^/(?:api|hubs|health|mcp)\(\?:/\|\$\) \{')
    if ($machineLocations.Count -ne 4) {
        throw "$sourceName must declare exactly four exact-lowercase machine locations; found $($machineLocations.Count)."
    }
    $lastMachineIndex = $machineLocations[$machineLocations.Count - 1].Index
    $spaIndex = $content.IndexOf('location / {', [StringComparison]::Ordinal)
    if ($spaIndex -lt 0) {
        throw "$sourceName is missing the SPA catch-all."
    }
    if ($caseVariant.Index -lt $lastMachineIndex) {
        throw "$sourceName declares the case-insensitive 404 location before an exact-lowercase machine location, which would shadow it and 404 the real machine surface."
    }
    if ($caseVariant.Index -gt $spaIndex) {
        throw "$sourceName declares the case-insensitive 404 location after the SPA catch-all, which would never be reached."
    }

    return @{
        Guard       = $guard.Groups['re'].Value
        GuardBlock  = Normalize-Config $guard.Value
        CaseVariant = $caseVariant.Groups['re'].Value
        CaseBlock   = Normalize-Config $caseVariant.Value
    }
}

<#
.SYNOPSIS
Reproduces nginx's routing decision for one raw request URI: 'api', 'web', or '404'.

.DESCRIPTION
Models the three steps that decide it, in nginx's order: the server-level raw-URI guard (the only
point at which an encoded slash is still visible), then regex location matching against the DECODED
path in declaration order (exact-lowercase machine locations first, then the case-insensitive 404),
then the SPA prefix catch-all.
#>
function Resolve-ProxyTarget([string]$requestUri, $rules, $machineRegexes) {
    if ($requestUri -imatch $rules.Guard) {
        return '404'
    }

    # nginx percent-decodes the URI (including %2F) before location matching, and matches the path
    # without the query string.
    $decoded = [System.Uri]::UnescapeDataString(($requestUri -split '\?')[0])

    foreach ($prefix in @('api', 'hubs', 'health', 'mcp')) {
        if ($machineRegexes[$prefix].IsMatch($decoded)) {
            return 'api'
        }
    }

    if ($decoded -imatch $rules.CaseVariant) {
        return '404'
    }

    return 'web'
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
Assert-Contains $standaloneRoutes.mcp 'proxy_buffering off;' '/mcp must disable proxy buffering for streaming responses.'
Assert-Contains $renderedRoutes.mcp 'proxy_buffering off;' 'Rendered /mcp must disable proxy buffering for streaming responses.'
foreach ($prefix in @('api', 'health', 'mcp')) {
    if ($standaloneRoutes[$prefix] -match 'proxy_set_header (Upgrade|Connection) ') {
        throw "/$prefix must not inherit /hubs WebSocket headers."
    }
}

# --- Fail-closed machine-path spelling contract (#1992 q-10 A, ADR-0064) ----------------------

$standaloneFailClosed = Get-FailClosedRules $standalone 'deploy/nginx/reverse-proxy.conf'
$renderedFailClosed = Get-FailClosedRules $rendered 'user_data.sh.tftpl rendered reverse-proxy.conf'
Assert-Equal $standaloneFailClosed.GuardBlock $renderedFailClosed.GuardBlock 'Standalone and rendered encoded-slash guards must remain in parity.'
Assert-Equal $standaloneFailClosed.CaseBlock $renderedFailClosed.CaseBlock 'Standalone and rendered case-variant 404 blocks must remain in parity.'

# Raw request URI -> upstream it must reach, executed through the rules read out of the config.
$expectedDispositions = @(
    # Canonical machine paths still reach the API container unchanged.
    @{ Path = '/api'; Expected = 'api' },
    @{ Path = '/api/'; Expected = 'api' },
    @{ Path = '/api/boards'; Expected = 'api' },
    @{ Path = '/hubs/board'; Expected = 'api' },
    @{ Path = '/health/live'; Expected = 'api' },
    @{ Path = '/health/ready'; Expected = 'api' },
    @{ Path = '/mcp'; Expected = 'api' },
    @{ Path = '/mcp/messages'; Expected = 'api' },
    @{ Path = '/api/boards?filter=1'; Expected = 'api' },
    # Case variants: 404 here rather than 200 + index.html from the SPA container, which is what
    # they used to get while the API resolved the very same path case-insensitively.
    @{ Path = '/API'; Expected = '404' },
    @{ Path = '/API/boards'; Expected = '404' },
    @{ Path = '/Api/boards'; Expected = '404' },
    @{ Path = '/Mcp'; Expected = '404' },
    @{ Path = '/MCP/messages'; Expected = '404' },
    @{ Path = '/Hubs/board'; Expected = '404' },
    @{ Path = '/HEALTH/live'; Expected = '404' },
    # Prefix-boundary encoded slashes: nginx decodes before location matching, so without the raw
    # guard these would be proxied to an API that reads them back as one non-machine segment.
    @{ Path = '/mcp%2Fmessages'; Expected = '404' },
    @{ Path = '/mcp%2fmessages'; Expected = '404' },
    @{ Path = '/api%2Fboards'; Expected = '404' },
    @{ Path = '/hubs%2Fboard'; Expected = '404' },
    @{ Path = '/health%2Flive'; Expected = '404' },
    @{ Path = '/mcp%2F'; Expected = '404' },
    @{ Path = '/MCP%2Fmessages'; Expected = '404' },
    # SPA paths, including prefix-shaped ones in any casing: the boundary is a segment, so these
    # are not machine surface at any layer and must still reach the web container.
    @{ Path = '/'; Expected = 'web' },
    @{ Path = '/workspace/review'; Expected = 'web' },
    @{ Path = '/settings'; Expected = 'web' },
    @{ Path = '/apidocs'; Expected = 'web' },
    @{ Path = '/Apidocs'; Expected = 'web' },
    @{ Path = '/healthy'; Expected = 'web' },
    @{ Path = '/mcpx'; Expected = 'web' },
    @{ Path = '/Mcpx'; Expected = 'web' },
    # Double-encoded: nginx decodes once, leaving the literal text %2F after the prefix, which is
    # not a prefix alias to nginx. The API collapses this onto the single-encoded form and answers
    # 404 there instead; that divergence is toward the closed answer and is recorded in ADR-0064.
    @{ Path = '/mcp%252Fmessages'; Expected = 'web' }
)

# An [ordered] hashtable is deliberately NOT used for the matrix above: PowerShell hashtable
# keys are case-INSENSITIVE, so '/api' and '/API' would collide -- the exact distinction the
# fail-closed contract turns on.
foreach ($case in $expectedDispositions) {
    $path = $case.Path
    $expected = $case.Expected
    Assert-Equal $expected (Resolve-ProxyTarget $path $standaloneFailClosed $machinePathRegexes) "Standalone config sends '$path' to the wrong upstream."
    Assert-Equal $expected (Resolve-ProxyTarget $path $renderedFailClosed $machinePathRegexes) "Rendered config sends '$path' to the wrong upstream."
}

Assert-Contains $standalone 'location / {' 'The SPA catch-all must remain present for non-machine paths.'
Assert-Contains $standalone 'proxy_pass http://web:8080/;' 'The SPA catch-all must continue to proxy to the web container.'
Write-Host 'Reverse-proxy static and rendered template contract passed.' -ForegroundColor Green
