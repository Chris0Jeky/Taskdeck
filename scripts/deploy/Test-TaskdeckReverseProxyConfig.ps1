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

    # Percent-encoded prefix letters need both views of the request at once (the first non-empty raw
    # segment after the leading separator run, and what the path decoded to), which one `if` cannot
    # express, so the config states it as a conjunction of two maps feeding a third. All three are
    # read out and executed below.
    $encodedSegmentMap = [regex]::Match(
        $content,
        '(?m)^map \$request_uri \$td_encoded_first_segment \{\r?\n[ \t]*default 0;\r?\n[ \t]*"(?<re>[^"]+)" 1;\r?\n\}')
    if (-not $encodedSegmentMap.Success) {
        throw "$sourceName must classify a percent escape in the first non-empty raw path segment after the leading separator run; the map is missing."
    }

    $machineUriMap = [regex]::Match(
        $content,
        '(?m)^map \$uri \$td_machine_uri \{\r?\n[ \t]*default 0;\r?\n[ \t]*"(?<re>[^"]+)" 1;\r?\n\}')
    if (-not $machineUriMap.Success) {
        throw "$sourceName must classify the decoded path as machine-facing; the map is missing."
    }

    if ($content -notmatch '(?m)^[ \t]*if \(\$td_encoded_machine_prefix\) \{\r?\n[ \t]*return 404;\r?\n[ \t]*\}') {
        throw "$sourceName computes the encoded-prefix conjunction but never returns 404 on it."
    }

    if ($content -notmatch '(?ms)^map "\$td_encoded_first_segment\$td_machine_uri" \$td_encoded_machine_prefix \{.*?"11" 1;') {
        throw "$sourceName must reject only the conjunction (raw escape AND machine path), so an ordinary encoded SPA route keeps working."
    }

    return @{
        Guard              = $guard.Groups['re'].Value
        GuardBlock         = Normalize-Config $guard.Value
        CaseVariant        = $caseVariant.Groups['re'].Value
        CaseBlock          = Normalize-Config $caseVariant.Value
        EncodedFirstSegment = $encodedSegmentMap.Groups['re'].Value
        MachineUri         = $machineUriMap.Groups['re'].Value
        EncodedBlocks      = (Normalize-Config $encodedSegmentMap.Value) + "`n" + (Normalize-Config $machineUriMap.Value)
    }
}

<#
.SYNOPSIS
Reproduces nginx's routing decision for one raw request URI: 'api', 'web', or '404'.

.DESCRIPTION
Models the four steps that decide it, in nginx's order: the server-level raw-URI guard (the only
point at which an encoded slash or a duplicated leading separator is still visible), then
percent-decoding and merge_slashes, then regex location matching against the resulting path in
declaration order (exact-lowercase machine locations first, then the case-insensitive 404), then the
SPA prefix catch-all.
#>
function ConvertTo-NginxRegex([string]$mapKey) {
    # A map key is an nginx match marker plus the pattern: "~" case-sensitive, "~*" insensitive.
    if ($mapKey -cmatch '^~\*(?<re>.*)$') {
        return [regex]::new($Matches['re'], [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    }
    if ($mapKey -cmatch '^~(?<re>.*)$') {
        return [regex]::new($Matches['re'])
    }
    throw "Map key '$mapKey' is not a regex match; the fail-closed rules must be regex keys."
}

function Resolve-ProxyTarget([string]$requestUri, $rules, $machineRegexes) {
    if ($requestUri -imatch $rules.Guard) {
        return '404'
    }

    # nginx percent-decodes the URI (including %2F) before location matching, and matches the path
    # without the query string.
    $decoded = [System.Uri]::UnescapeDataString(($requestUri -split '\?')[0])

    # merge_slashes is on by default, so a run of separators collapses to one BEFORE a location is
    # chosen. Modelling this is load-bearing: without it the simulator would report //api/boards as
    # SPA-bound and would agree with a config that has no leading-separator guard at all.
    $decoded = $decoded -replace '/{2,}', '/'

    # Percent-encoded prefix letters: the conjunction the three maps compute. Evaluated here in the
    # same order nginx does -- both maps read their variable, the third combines them, and the
    # server-level `if` returns 404 -- all before a location is selected.
    if ((ConvertTo-NginxRegex $rules.EncodedFirstSegment).IsMatch($requestUri) -and
        (ConvertTo-NginxRegex $rules.MachineUri).IsMatch($decoded)) {
        return '404'
    }

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
Assert-Equal $standaloneFailClosed.EncodedBlocks $renderedFailClosed.EncodedBlocks 'Standalone and rendered encoded-prefix maps must remain in parity.'

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
    # Leading duplicate or encoded separators: decoding plus merge_slashes collapse these onto the
    # machine location, but proxy_pass forwards the raw form, which the API reads as an SPA path
    # with an empty first segment. Only the raw guard can see them.
    @{ Path = '//api/boards'; Expected = '404' },
    @{ Path = '//api'; Expected = '404' },
    @{ Path = '///api/boards'; Expected = '404' },
    @{ Path = '//hubs/board'; Expected = '404' },
    @{ Path = '//health/live'; Expected = '404' },
    @{ Path = '//mcp/messages'; Expected = '404' },
    @{ Path = '//API/x'; Expected = '404' },
    @{ Path = '/%2fapi/boards'; Expected = '404' },
    @{ Path = '/%2Fapi/boards'; Expected = '404' },
    @{ Path = '/%2fmcp'; Expected = '404' },
    @{ Path = '/%2f%2fapi/boards'; Expected = '404' },
    # Percent-encoded prefix letters: decoded to the canonical path by nginx AND by the API, so
    # nothing downstream can tell them apart -- only the raw first segment still carries the escape.
    @{ Path = '/%61pi/boards'; Expected = '404' },
    @{ Path = '/ap%69/boards'; Expected = '404' },
    @{ Path = '/%6Dcp/messages'; Expected = '404' },
    @{ Path = '/%6dcp'; Expected = '404' },
    @{ Path = '/hub%73/board'; Expected = '404' },
    @{ Path = '/%68ealth/live'; Expected = '404' },
    @{ Path = '/%41PI/boards'; Expected = '404' },
    # Combined duplicate leading separators plus encoded prefix letters must still be rejected.
    # nginx merges the separators before matching $uri, so the raw map is the only remaining witness.
    @{ Path = '//%61pi/boards'; Expected = '404' },
    @{ Path = '///%6Dcp/messages'; Expected = '404' },
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
    # A duplicated separator that does not open onto a machine prefix is merged and served the SPA,
    # so the boundary on the guard's second alternation must not swallow it.
    @{ Path = '//apidocs'; Expected = 'web' },
    @{ Path = '//workspace/review'; Expected = 'web' },
    @{ Path = '//'; Expected = 'web' },
    # An escape in the first segment of a NON-machine path is ordinary SPA routing: the conjunction
    # is what keeps these out of the rejected set.
    @{ Path = '/caf%C3%A9'; Expected = 'web' },
    @{ Path = '/a%20b'; Expected = 'web' },
    @{ Path = '/%61pidocs'; Expected = 'web' },
    @{ Path = '//%61pidocs'; Expected = 'web' },
    @{ Path = '///caf%C3%A9'; Expected = 'web' },
    # An escape deeper in a machine path is route data, not a spelling of the prefix, so it still
    # reaches the API.
    @{ Path = '/api/board%20s'; Expected = 'api' },
    @{ Path = '/api/%62oards'; Expected = 'api' },
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
