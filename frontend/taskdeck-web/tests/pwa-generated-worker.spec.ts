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
 * Evaluates the real emitted cleanup script the way the generated worker loads it:
 * via `importScripts()` from inside vite-plugin-pwa's asynchronous AMD `define()`
 * factory, i.e. AFTER the worker's lifecycle events have already been dispatched.
 * Nothing here dispatches `activate`, which is the whole point - a build that only
 * retires the caches from an `activate` listener never retires them at all, which is
 * exactly what a real Chromium showed before this was fixed.
 */
async function evaluateCleanupWithoutActivate(
  cacheStorage: FakeCacheStorage,
): Promise<{ activateListeners: number }> {
  const listeners: Record<string, ((event: unknown) => void)[]> = {}
  const workerScope = {
    addEventListener: (type: string, handler: (event: unknown) => void) => {
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

  return { activateListeners: listeners.activate?.length ?? 0 }
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

  it('excludes a prefixed API base that the /api denial cannot see', () => {
    const [, staticMatcher] = loadGeneratedRuntimeMatchers()

    expect(staticMatcher.test('https://taskdeck.example/assets/api/users/by-username/alice.png')).toBe(false)
    expect(staticMatcher.test('https://taskdeck.example/assets/api/boards/1/cover.svg')).toBe(false)
    expect(staticMatcher.test('https://taskdeck.example/assets/%61pi/users/by-username/alice.png')).toBe(false)
    expect(staticMatcher.test('https://taskdeck.example/icons/icon-192x192.png')).toBe(true)
  })
})
