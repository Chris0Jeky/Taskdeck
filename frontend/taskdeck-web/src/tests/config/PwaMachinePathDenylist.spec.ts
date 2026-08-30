import { describe, expect, it } from 'vitest'
// `?raw` rather than node:fs: this project type-checks specs with
// `types: ["vite/client", "vite-plugin-pwa/client"]` and no node types (see the rationale in
// tsconfig.vitest.json), and adding the file to that project's quarantine list to dodge the check
// is explicitly not allowed there. Importing vite.config.ts itself is not an option either — it
// pulls in the Vite plugin graph.
import viteConfig from '../../../vite.config.ts?raw'

/**
 * Pins the service worker's navigation-fallback denylist to the machine-path contract the rest of
 * the stack enforces (#1992).
 *
 * An installed PWA answers navigation requests from its own precache before the request reaches the
 * network, so a prefix missing from this list gets `index.html` for a path the backend owns — the
 * browser-side twin of the SPA-fallback defect fixed in #1971, and invisible to any server-side
 * test. The four prefixes and their boundary must match `deploy/nginx/reverse-proxy.conf`
 * (`^/<prefix>(?:/|$)`, verified by `scripts/deploy/Test-TaskdeckReverseProxyConfig.ps1`) and
 * `PipelineConfiguration.NonSpaPathPrefixes`.
 *
 * The regexes are read out of `vite.config.ts` and executed rather than string-matched: the config
 * cannot be imported here (it pulls in the Vite plugin graph), and asserting on its text would pass
 * for a pattern that is spelled right and behaves wrong.
 */

/**
 * Matches one JavaScript regex literal, treating `[...]` as an atom so a character class containing
 * `/` (as `[/?]` does) does not terminate the literal early.
 */
const REGEX_LITERAL = /\/(?:\\.|\[(?:\\.|[^\]\\])*\]|[^/\\\n])+\/[a-z]*/g

function readDenylist(): RegExp[] {
  const block = viteConfig.match(/navigateFallbackDenylist:\s*\[([\s\S]*?)\],\r?\n/)
  expect(block, 'navigateFallbackDenylist array not found in vite.config.ts').not.toBeNull()

  const literals = block![1].match(REGEX_LITERAL) ?? []
  // Self-check on the extraction: a parser that silently found the wrong thing would make every
  // assertion below vacuous.
  expect(literals).toHaveLength(4)

  return literals.map((literal: string) => {
    const end = literal.lastIndexOf('/')
    return new RegExp(literal.slice(1, end), literal.slice(end + 1))
  })
}

const denylist = readDenylist()

/** Workbox's NavigationRoute tests the denylist against `url.pathname + url.search`. */
function isDenied(pathAndSearch: string): boolean {
  return denylist.some((pattern) => pattern.test(pathAndSearch))
}

describe('PWA navigation fallback denylist', () => {
  it.each([
    // Bare prefixes: the reverse proxy sends these to the API container, so the shell must not
    // answer them. `/api` was the gap this test was written for — it was `^\/api\//`, trailing
    // slash required, while `/hubs` and `/health` had already been widened.
    '/api',
    '/hubs',
    '/health',
    '/mcp',
    // Trailing slash and descendants.
    '/api/',
    '/api/boards',
    '/hubs/boards',
    '/health/ready',
    '/mcp/',
    '/mcp/messages',
    // A query string directly on the bare prefix: workbox matches `pathname + search`, so the
    // boundary has to accept `?` as well as `/`.
    '/api?probe=1',
    '/health?full=true',
    // Percent-encoded descendants: workbox sees the still-encoded pathname, nginx location-matches
    // the decoded URI and routes these to the API — the two layers must agree (#1992).
    '/mcp%2Fmessages',
    '/mcp%2fmessages',
    '/api%2Fboards',
    '/hubs%2fboards',
    '/health%2Fready',
    // Case variants (#1992 q-10 A, ADR-0064). These are not machine paths — the prefixes are exact
    // lowercase — but they are not client-side routes either: nginx and the API both answer them
    // 404. The shell must not stand in front of that 404 from the precache.
    '/API',
    '/Api',
    '/MCP',
    '/Mcp',
    '/HUBS',
    '/Health',
    '/API/boards',
    '/Api/boards',
    '/MCP/messages',
    '/Hubs/boards',
    '/HEALTH/ready',
    '/API?probe=1',
    // Both variants at once.
    '/MCP%2Fmessages',
    '/Api%2fboards',
    // Leading duplicate or encoded separators (#1992 round 1). nginx decodes and then merges
    // slashes, so these reach the API container, while the raw pathname the service worker sees
    // still carries the duplicated separator.
    '//api/boards',
    '//api',
    '///api/boards',
    '//hubs/board',
    '//health/live',
    '//mcp/messages',
    '//API/boards',
    '/%2fapi/boards',
    '/%2Fapi/boards',
    '/%2fmcp',
    '/%2f%2fapi/boards',
    // Percent-encoded prefix letters (#1992 round 1). nginx and Kestrel both decode before they
    // match, so these ARE the canonical path everywhere downstream; only the pathname the service
    // worker sees still carries the escape.
    '/%61pi/boards',
    '/ap%69/boards',
    '/%61%70%69/boards',
    '/%6Dcp/messages',
    '/%6dcp',
    '/hub%73/board',
    '/%68ealth/live',
    '/%41PI/boards',
    '//%61pi/boards',
    '/%2f%61pi/boards',
  ])('keeps %s off the SPA fallback', (path) => {
    expect(isDenied(path)).toBe(true)
  })

  it.each([
    // Client-side routes must still be served the shell offline — that is what the fallback is for.
    '/',
    '/workspace/review',
    '/workspace/boards/abc-123',
    '/settings',
    // Prefix-shaped but not machine paths. `/mcp` was `^\/mcp` with no boundary, so every one of
    // these starting with a machine prefix plus more letters was wrongly denied.
    '/apidocs',
    '/hubsy',
    '/healthy',
    '/mcpx',
    // The same, in other casings: the fail-closed rule keys on the segment boundary, so widening
    // the match to any case must not swallow a client-side route that merely starts with a
    // machine prefix's letters.
    '/Apidocs',
    '/HubsY',
    '/Healthy',
    '/McpX',
    // A duplicated separator that does not open onto a machine prefix: nginx merges it and serves
    // the SPA, so the boundary must survive the widened leading-separator match.
    '//apidocs',
    '//mcpx',
    '//workspace/review',
    '//',
    // An escape in the first segment that does not decode to a machine prefix is an ordinary
    // client-side route and must still be served the shell offline — the per-character alternation
    // is what keeps this true while `/%61pi/boards` is denied.
    '/%61pidocs',
    '/caf%C3%A9',
    '/a%20b',
    '/%6Dcpx',
    // Double-encoded: nginx decodes once, leaving literal `%2F` text after the prefix, so the
    // proxy does not treat it as machine surface either — the layers agree it is SPA-side.
    '/mcp%252Fmessages',
  ])('still serves the SPA fallback for %s', (path) => {
    expect(isDenied(path)).toBe(false)
  })
})

/**
 * The runtime API cache is the other half of the browser-side contract (#1992 round 1). The denylist
 * governs NAVIGATIONS; this rule governs the fetches the app makes, and it used to be
 * case-insensitive — so an installed PWA could hold a cached `200` under `/API/...` and keep
 * replaying it for a URL that now answers `404` at every layer.
 */
describe('PWA runtime API cache', () => {
  const rule = viteConfig.match(
    /urlPattern:\s*(?<pattern>\/\^https\?[^\n]*?),\s*\n\s*handler:\s*'NetworkFirst'[\s\S]*?cacheName:\s*'(?<cacheName>[^']+)'/,
  )

  it('extracts the NetworkFirst API rule', () => {
    expect(rule, 'NetworkFirst API runtime rule not found in vite.config.ts').not.toBeNull()
  })

  it('matches canonical /api/ requests only, case-sensitively', () => {
    const literal = rule!.groups!.pattern
    const end = literal.lastIndexOf('/')
    const flags = literal.slice(end + 1)
    const pattern = new RegExp(literal.slice(1, end), flags)

    expect(flags, 'the i flag would let a /API/... response into the cache').not.toContain('i')
    expect(pattern.test('https://taskdeck.example/api/boards')).toBe(true)
    expect(pattern.test('https://taskdeck.example/API/boards')).toBe(false)
    expect(pattern.test('https://taskdeck.example/Api/boards')).toBe(false)
  })

  it('uses a versioned cache name so pre-contract entries are never read again', () => {
    // Bumping the name is what retires responses cached under the old, case-insensitive rule. They
    // are orphaned rather than deleted: cleanupOutdatedCaches covers precaches, not runtime caches.
    expect(rule!.groups!.cacheName).toBe('taskdeck-api-cache-v2')
    expect(rule!.groups!.cacheName).not.toBe('taskdeck-api-cache')
  })
})
