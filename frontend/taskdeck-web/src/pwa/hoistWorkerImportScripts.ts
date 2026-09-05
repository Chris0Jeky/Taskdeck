/**
 * Build-time repair for the inert `activate` listener in the generated service worker (#2639).
 *
 * `vite-plugin-pwa`'s `generateSW` strategy emits the configured `workbox.importScripts` call at
 * the top of the worker SOURCE, but then bundles that source through Rollup's off-main-thread
 * plugin, which wraps the whole module in an asynchronous AMD `define()` factory. The emitted
 * `dist/sw.js` therefore reaches `importScripts('api-cache-cleanup.js', ...)` from inside a promise
 * continuation rather than during the worker's initial synchronous evaluation, and every listener
 * that file registers is attached after the lifecycle events have been dispatched. Measured in
 * Chromium (PR #2416, `__proofActivateFired: false`; reproduced under #2475): the `message`
 * listener answers normally and the `install` listener still receives its event, but `activate`
 * never fires, so the forced re-sweep inside `event.waitUntil` never runs.
 *
 * This function moves that one call to offset 0 of the emitted worker, ahead of the AMD shim and
 * the `define()` call. Nothing else about the generated worker changes: the precache manifest, the
 * navigation fallback and denylist, both runtime-caching handlers, the share-target handler and the
 * skip-waiting message listener are all emitted exactly as before.
 *
 * It is deliberately strict. A `vite-plugin-pwa` or `workbox-build` upgrade that changes the shape
 * of the emitted call must fail the build loudly rather than silently restore the vulnerability, so
 * every unexpected shape throws instead of returning the source unchanged.
 */

function escapeForRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}

/**
 * Moves the `importScripts(...)` call for `specifiers` to the very top of `source`.
 *
 * @param source Contents of the emitted `dist/sw.js`.
 * @param specifiers The `workbox.importScripts` list exactly as configured in `vite.config.ts`.
 * @returns The rewritten worker source; the input unchanged when the call is already at offset 0.
 * @throws When the call is missing, appears more than once, or sits somewhere the removal cannot be
 *   proven safe - all of which mean the emitted worker no longer matches this repair.
 */
export function hoistWorkerImportScripts(source: string, specifiers: readonly string[]): string {
  if (specifiers.length === 0) {
    throw new Error('hoistWorkerImportScripts: no importScripts specifiers were configured.')
  }

  // Matches the minified emission (`importScripts("a.js","b.js"),`) and the unminified one
  // (`importScripts(\n  "a.js",\n  "b.js"\n);`) that workbox emits when it does not run terser.
  const call = new RegExp(
    'importScripts\\(\\s*' +
      specifiers.map((specifier) => `["']${escapeForRegExp(specifier)}["']`).join('\\s*,\\s*') +
      '\\s*\\)\\s*[;,]?',
    'g',
  )

  const matches = [...source.matchAll(call)]
  if (matches.length !== 1) {
    throw new Error(
      `hoistWorkerImportScripts: expected exactly one importScripts(${specifiers.join(', ')}) call ` +
        `in the generated worker, found ${matches.length}. The vite-plugin-pwa output shape ` +
        'changed; re-check that the cleanup script is still loaded during initial evaluation (#2639).',
    )
  }

  const [match] = matches
  const start = match.index!
  if (start === 0) return source

  // Removing the call must not leave a dangling operand. Every shape this repair understands has
  // the call as the first statement of a block or straight after a statement terminator, so the
  // preceding non-whitespace character is one of these. Anything else means the emission moved
  // into an expression position and cutting it out would produce broken JavaScript.
  const preceding = source.slice(0, start).trimEnd().slice(-1)
  if (preceding !== '' && preceding !== '{' && preceding !== ';') {
    throw new Error(
      'hoistWorkerImportScripts: the importScripts call is not in statement position ' +
        `(preceded by ${JSON.stringify(preceding)}); refusing to rewrite the generated worker (#2639).`,
    )
  }

  const hoisted =
    `importScripts(${specifiers.map((specifier) => JSON.stringify(specifier)).join(',')});\n`

  return hoisted + source.slice(0, start) + source.slice(start + match[0].length)
}
