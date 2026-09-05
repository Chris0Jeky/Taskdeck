import { readFileSync } from 'node:fs'
import { execFileSync } from 'node:child_process'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { beforeAll, describe, expect, it } from 'vitest'

type RuntimeMatcher = RegExp

function deserializeRuntimeMatcher(source: string): RuntimeMatcher {
  const closingDelimiter = source.lastIndexOf('/')
  return new RegExp(source.slice(1, closingDelimiter), source.slice(closingDelimiter + 1))
}

function loadGeneratedWorker(): string {
  const projectRoot = resolve(fileURLToPath(import.meta.url), '..', '..')
  return readFileSync(resolve(projectRoot, 'dist', 'sw.js'), 'utf8')
}

/** The emitted copy of `public/api-cache-cleanup.js` that the worker importScripts. */
function loadGeneratedCleanupScript(): string {
  const projectRoot = resolve(fileURLToPath(import.meta.url), '..', '..')
  return readFileSync(resolve(projectRoot, 'dist', 'api-cache-cleanup.js'), 'utf8')
}

/** Runtime cache names as the *generated* worker spells them, not as the source hopes. */
function generatedRuntimeCacheNames(): string[] {
  return [...loadGeneratedWorker().matchAll(/"?cacheName"?\s*:\s*"([^"]+)"/g)].map((match) => match[1])
}

interface FakeCache {
  keys: () => Promise<{ url: string }[]>
}

interface FakeCacheStorage {
  storage: Map<string, Set<string>>
  has: (name: string) => Promise<boolean>
  keys: () => Promise<string[]>
  open: (name: string) => Promise<FakeCache>
  delete: (name: string) => Promise<boolean>
}

function createFakeCacheStorage(seed: Record<string, string[]>): FakeCacheStorage {
  const storage = new Map<string, Set<string>>(
    Object.entries(seed).map(([name, urls]) => [name, new Set(urls)]),
  )
  return {
    storage,
    has: (name) => Promise.resolve(storage.has(name)),
    keys: () => Promise.resolve([...storage.keys()]),
    open: (name) => {
      if (!storage.has(name)) storage.set(name, new Set())
      const entries = storage.get(name)!
      return Promise.resolve({ keys: () => Promise.resolve([...entries].map((url) => ({ url }))) })
    },
    delete: (name) => Promise.resolve(storage.delete(name)),
  }
}

/**
 * Evaluates the real emitted cleanup script against a fake `CacheStorage` and hands back the
 * listeners it registered. Nothing here dispatches `activate` unless a case asks for it, so the
 * evaluation-time sweep is exercised on its own: a build that retires the caches ONLY from an
 * `activate` listener fails the case below.
 *
 * These are handler-contract cases. A dispatched fake event cannot prove that the real worker's
 * listener is attached in time to receive the real one - only the emitted worker's structure can,
 * which is what the top-level-importScripts case above asserts.
 */
async function evaluateCleanupWithoutActivate(
  cacheStorage: FakeCacheStorage,
): Promise<{ activateListeners: number }> {
  const listeners: Record<string, ((event: any) => void)[]> = {}
  const workerScope = {
    addEventListener: (type: string, handler: (event: any) => void) => {
      listeners[type] = [...(listeners[type] ?? []), handler]
    },
    registration: { active: null },
    clients: { claim: () => Promise.resolve() },
    skipWaiting: () => {},
  }

  const pending: Promise<unknown>[] = []
  const trackedCaches = new Proxy(cacheStorage as unknown as Record<string, unknown>, {
    get(target, property) {
      const value = Reflect.get(target, property)
      if (typeof value !== 'function') return value
      return (...args: unknown[]) => {
        const result = (value as (...a: unknown[]) => unknown).apply(target, args)
        if (result instanceof Promise) pending.push(result)
        return result
      }
    },
  })

  const evaluate = new Function('self', 'caches', 'console', loadGeneratedCleanupScript())
  evaluate(workerScope, trackedCaches, { warn: () => {} })

  // Drain whatever the script started at evaluation time.
  for (let index = 0; index < 25; index += 1) {
    await Promise.allSettled([...pending])
    await Promise.resolve()
  }

  return {
    activateListeners: listeners.activate?.length ?? 0,
    /** Dispatches `activate` and awaits whatever the handler passed to waitUntil. */
    dispatchActivate: async () => {
      let activation: Promise<unknown> = Promise.resolve()
      const handler = listeners.activate?.[0]
      if (!handler) throw new Error('the worker script registered no activate listener')
      handler({ waitUntil: (promise: Promise<unknown>) => { activation = promise } })
      await activation
    },
  }
}

function buildWithNestedApiBase(): void {
  const projectRoot = resolve(fileURLToPath(import.meta.url), '..', '..')
  const viteBin = resolve(projectRoot, 'node_modules', 'vite', 'bin', 'vite.js')
  execFileSync(process.execPath, [viteBin, 'build'], {
    cwd: projectRoot,
    env: { ...process.env, VITE_API_BASE_URL: '/assets/api' },
    stdio: 'pipe',
  })
}

function loadGeneratedRuntimeMatchers(): RuntimeMatcher[] {
  const worker = loadGeneratedWorker()
  const sources = [...worker.matchAll(
    /registerRoute\((\/\^https\?:[\s\S]+?\/[a-z]*),\s*new\s+\w+\.(?:StaleWhileRevalidate|CacheFirst)/g,
  )].map((match) => deserializeRuntimeMatcher(match[1]))

  expect(sources).toHaveLength(2)
  return sources
}

describe('generated PWA worker runtime-cache contract', () => {
  beforeAll(() => {
    buildWithNestedApiBase()
  })

  it('does not generate any NetworkFirst strategy or legacy API cache', () => {
    const worker = loadGeneratedWorker()

    expect(worker).not.toMatch(/new\s+[$\w]+\.NetworkFirst\s*\(/)
    expect(worker).not.toContain('taskdeck-api-cache')
  })

  it('serializes self-contained matchers that still reject every API spelling', () => {
    const [localeMatcher, staticMatcher] = loadGeneratedRuntimeMatchers()

    expect(localeMatcher.test('https://taskdeck.example/assets/it-a.js')).toBe(true)
    expect(staticMatcher.test('https://taskdeck.example/assets/avatar.png')).toBe(true)

    for (const url of [
      'https://taskdeck.example/api/assets/it-a.js',
      'https://cdn.example/%61pi/avatar.png',
      'https://taskdeck.example/api%2Favatar.png',
      'https://taskdeck.example//api/avatar.png',
    ]) {
      expect(localeMatcher.test(url)).toBe(false)
      expect(staticMatcher.test(url)).toBe(false)
    }
  })

  it('loads the cleanup script from a top-level importScripts, ahead of the AMD factory', () => {
    // The repair for #2639. vite-plugin-pwa emits the configured `importScripts` call INSIDE the
    // asynchronous AMD `define()` factory it wraps the worker in, so the cleanup script's
    // `activate` listener was attached after the event had already been dispatched and the forced
    // re-sweep never ran (measured in Chromium on PR #2416 as `__proofActivateFired: false`).
    // The build hoists that call to the top of the emitted worker - see
    // src/pwa/hoistWorkerImportScripts.ts - so the listener exists during the worker's initial
    // synchronous evaluation, which is what gives `event.waitUntil` on activate its real meaning.
    //
    // This is a STRUCTURAL assertion on purpose: no fake event can prove attachment order, and the
    // handler-contract cases below dispatch `activate` by hand precisely because they cannot.
    const worker = loadGeneratedWorker()
    const cleanupImport = /importScripts\(\s*["']api-cache-cleanup\.js["']/g
    const matches = [...worker.matchAll(cleanupImport)]
    expect(matches).toHaveLength(1)

    const [match] = matches
    // Nothing at all may precede it: not the AMD shim, not the `define()` call, not a comment.
    expect(worker.slice(0, match.index)).toBe('')
    expect(worker.indexOf('define(')).toBeGreaterThan(match.index!)
  })

  it('retires the runtime caches the generated worker names, without an activate event', async () => {
    const runtimeCacheNames = generatedRuntimeCacheNames()
    expect(runtimeCacheNames).toContain('taskdeck-static-assets')

    const cacheStorage = createFakeCacheStorage({
      ...Object.fromEntries(
        runtimeCacheNames.map((name) => [name, ['https://taskdeck.example/seeded-' + name + '.png']]),
      ),
      'taskdeck-api-cache-v2': ['https://taskdeck.example/api/boards'],
      'taskdeck-share-target': ['https://taskdeck.example/queued-share'],
    })

    const { activateListeners } = await evaluateCleanupWithoutActivate(cacheStorage)

    // The listener must still exist for a worker that imports this file synchronously.
    expect(activateListeners).toBe(1)
    // ...but the sweep may not DEPEND on it: no activate event was dispatched here.
    const remaining = [...cacheStorage.storage.keys()]
    expect(remaining).not.toContain('taskdeck-static-assets')
    expect(remaining.some((name) => name.startsWith('taskdeck-api-cache'))).toBe(false)
    // The explicit offline share queue is never collateral.
    expect([...(cacheStorage.storage.get('taskdeck-share-target') ?? [])])
      .toContain('https://taskdeck.example/queued-share')
  })

  it('does not repeat the migration once the marker cache records it', async () => {
    const cacheStorage = createFakeCacheStorage({
      'taskdeck-pwa-cache-policy-v2': [],
      'taskdeck-static-assets': ['https://taskdeck.example/legitimate-asset.png'],
    })

    await evaluateCleanupWithoutActivate(cacheStorage)

    expect([...(cacheStorage.storage.get('taskdeck-static-assets') ?? [])])
      .toContain('https://taskdeck.example/legitimate-asset.png')
  })

  it('re-sweeps at activation even after the evaluation-time sweep wrote the marker', async () => {
    // The evaluation-time sweep runs during INSTALL, while the OLD vulnerable worker
    // is still the controller and can still store an identity-bound response in
    // taskdeck-static-assets. If activation reused that completed sweep - via a
    // memoised promise or by short-circuiting on the marker cache - anything cached
    // in that window would survive the migration, which is the PR's threat model.
    const cacheStorage = createFakeCacheStorage({ 'taskdeck-static-assets': [] })
    const { dispatchActivate } = await evaluateCleanupWithoutActivate(cacheStorage)

    // The install-time sweep has completed and recorded itself.
    expect([...cacheStorage.storage.keys()]).toContain('taskdeck-pwa-cache-policy-v2')

    // The old worker poisons the cache in the install-to-activate window.
    cacheStorage.storage.set('taskdeck-static-assets', new Set([
      'https://taskdeck.example/assets/api/users/by-username/alice.png',
    ]))
    cacheStorage.storage.set('taskdeck-api-cache-v2', new Set(['https://taskdeck.example/api/boards']))
    cacheStorage.storage.set('taskdeck-share-target', new Set(['https://taskdeck.example/queued-share']))

    await dispatchActivate()

    const remaining = [...cacheStorage.storage.keys()]
    expect(remaining).not.toContain('taskdeck-static-assets')
    expect(remaining.some((name) => name.startsWith('taskdeck-api-cache'))).toBe(false)
    expect([...(cacheStorage.storage.get('taskdeck-share-target') ?? [])])
      .toContain('https://taskdeck.example/queued-share')
    // The marker survives the forced sweep, so evaluation-time stays one-time.
    expect(remaining).toContain('taskdeck-pwa-cache-policy-v2')
  })

  it('rejects the activation promise when the forced sweep cannot complete', async () => {
    const cacheStorage = createFakeCacheStorage({ 'taskdeck-static-assets': [] })
    const { dispatchActivate } = await evaluateCleanupWithoutActivate(cacheStorage)

    cacheStorage.delete = () => Promise.reject(new Error('storage unavailable'))

    // The failure must reach the `waitUntil` promise rather than be swallowed. It does NOT abort
    // activation - the Service Worker spec only aborts on a rejected INSTALL - so this pins that
    // the sweep failure is surfaced (console warning plus an unhandled rejection), not that the
    // worker is prevented from controlling the page.
    await expect(dispatchActivate()).rejects.toThrow('storage unavailable')
  })

  it('keeps the migration marker versioned with the policy handshake constant', () => {
    // The marker name is a bare literal in the public worker script, so a future v3
    // bump of the handshake constant must fail loudly here rather than silently
    // leaving the migration keyed on the old version.
    const pageSideSource = readFileSync(
      resolve(fileURLToPath(import.meta.url), '..', '..', 'src', 'pwa', 'legacyApiCacheWorker.ts'),
      'utf8',
    )
    const policyVersion = /taskdeck-api-cache-policy-(v\d+)/.exec(pageSideSource)?.[1]
    expect(policyVersion).toBeTruthy()

    const markerVersion = /taskdeck-pwa-cache-policy-(v\d+)/.exec(loadGeneratedCleanupScript())?.[1]
    expect(markerVersion).toBe(policyVersion)
  })

  it('excludes a prefixed API base that the /api denial cannot see', () => {
    const [, staticMatcher] = loadGeneratedRuntimeMatchers()

    expect(staticMatcher.test('https://taskdeck.example/assets/api/users/by-username/alice.png')).toBe(false)
    expect(staticMatcher.test('https://taskdeck.example/assets/api/boards/1/cover.svg')).toBe(false)
    expect(staticMatcher.test('https://taskdeck.example/assets/%61pi/users/by-username/alice.png')).toBe(false)
    expect(staticMatcher.test('https://taskdeck.example/icons/icon-192x192.png')).toBe(true)
  })
})
