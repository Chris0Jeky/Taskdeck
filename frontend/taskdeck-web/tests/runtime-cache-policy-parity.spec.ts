import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

/**
 * `src/pwa/runtimeCachePolicy.ts` is not imported by any shipping module: Workbox
 * serializes the `urlPattern` callbacks out of `vite.config.ts`, so the inline copies
 * there are what actually reaches the service worker. Without this check a security
 * fix applied to the policy module alone would ship nothing while its own spec stayed
 * green. `tests/pwa-generated-worker.spec.ts` proves the emitted worker; this proves
 * the two sources cannot drift apart in the first place.
 */
const MATCHERS = ['API_PATH', 'LOCALE_CATALOG_PATH', 'STATIC_ASSET_PATH'] as const

function read(relative: string): string {
  const projectRoot = resolve(fileURLToPath(import.meta.url), '..', '..')
  return readFileSync(resolve(projectRoot, relative), 'utf8')
}

function declaredPattern(source: string, name: string): string {
  const match = new RegExp(`^const ${name} =\\s*\\n?\\s*(.+)$`, 'm').exec(source)
  if (!match) throw new Error(`runtimeCachePolicy.ts no longer declares ${name}`)
  return match[1].trim()
}

describe('runtime cache policy parity', () => {
  it('ships the exact matchers the policy module and its spec exercise', () => {
    const policy = read('src/pwa/runtimeCachePolicy.ts')
    const viteConfig = read('vite.config.ts')

    for (const name of MATCHERS) {
      const pattern = declaredPattern(policy, name)
      expect(pattern.startsWith('/')).toBe(true)
      expect(
        viteConfig.includes(pattern),
        `vite.config.ts must inline ${name} verbatim: ${pattern}`,
      ).toBe(true)
    }
  })

  it('keeps the service worker handshake literals aligned with the worker script', () => {
    const module = read('src/pwa/legacyApiCacheWorker.ts')
    const worker = read('public/api-cache-cleanup.js')

    for (const literal of ['taskdeck:api-cache-policy', 'legacy-api-cache-retired', 'taskdeck:skip-waiting']) {
      expect(module.includes(`'${literal}'`)).toBe(true)
      expect(worker.includes(`'${literal}'`)).toBe(true)
    }

    // The activation hook evicts against the same static-asset rule the route admits.
    expect(worker.includes(declaredPattern(read('src/pwa/runtimeCachePolicy.ts'), 'STATIC_ASSET_PATH'))).toBe(true)
  })
})
