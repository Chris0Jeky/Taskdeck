import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

/**
 * `vite.config.ts` evaluates the policy factories at build time and hands Workbox
 * their RegExp results. Workbox serializes those results, not the imported functions,
 * so `sw.js` remains self-contained while the source policy and build configuration
 * share one implementation. `tests/pwa-generated-worker.spec.ts` proves the emitted
 * worker; this test proves the build still consumes the shared factories.
 */
const RUNTIME_MATCHER_FACTORIES = [
  'createLocaleCatalogRuntimePattern',
  'createStaticAssetRuntimePattern',
] as const

function read(relative: string): string {
  const projectRoot = resolve(fileURLToPath(import.meta.url), '..', '..')
  return readFileSync(resolve(projectRoot, relative), 'utf8')
}

describe('runtime cache policy parity', () => {
  it('builds each runtime route from the shared policy factory', () => {
    const viteConfig = read('vite.config.ts')

    expect(viteConfig).toContain("from './src/pwa/runtimeCachePolicy.ts'")
    for (const factory of RUNTIME_MATCHER_FACTORIES) {
      expect(
        new RegExp(`urlPattern:\\s*${factory}\\(\\s*env\\.VITE_API_BASE_URL\\s*\\)`).test(viteConfig),
        `vite.config.ts must build a runtime route with ${factory}`,
      ).toBe(true)
    }
  })

  it('keeps the service worker handshake literals aligned with the worker script', () => {
    const module = read('src/pwa/legacyApiCacheWorker.ts')
    const worker = read('public/api-cache-cleanup.js')

    for (const literal of ['taskdeck:api-cache-policy', 'taskdeck-api-cache-policy-v2', 'taskdeck:skip-waiting']) {
      expect(module.includes(`'${literal}'`)).toBe(true)
      expect(worker.includes(`'${literal}'`)).toBe(true)
    }

    // The public worker cannot reconstruct the build-time API base, so it must
    // invalidate the whole static runtime cache instead of copying a partial matcher.
    expect(worker).toContain('await caches.delete(TASKDECK_STATIC_ASSET_CACHE)')
    expect(worker).not.toContain('TASKDECK_STATIC_ASSET_PATH')
  })
})
