import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import workerScript from '../../../public/api-cache-cleanup.js?raw'
import {
  API_CACHE_POLICY_QUERY,
  API_CACHE_POLICY_RETIRED,
  API_CACHE_SKIP_WAITING,
} from '../../pwa/legacyApiCacheWorker'

/** Minimal ServiceWorker stand-in: answers the policy handshake, or stays silent. */
function worker(options: { retired: boolean; state?: string }) {
  const listeners = new Map<string, Set<() => void>>()
  return {
    state: options.state ?? 'activated',
    posted: [] as unknown[],
    postMessage(message: unknown, transfer?: MessagePort[]) {
      this.posted.push(message)
      if (!options.retired || !transfer) return
      transfer[0].postMessage({ policy: API_CACHE_POLICY_RETIRED })
    },
    addEventListener(type: string, listener: () => void) {
      const bucket = listeners.get(type) ?? new Set()
      bucket.add(listener)
      listeners.set(type, bucket)
    },
    removeEventListener(type: string, listener: () => void) {
      listeners.get(type)?.delete(listener)
    },
    emit(type: string) {
      for (const listener of [...(listeners.get(type) ?? [])]) listener()
    },
  }
}

function registration(overrides: Record<string, unknown> = {}) {
  const listeners = new Map<string, Set<() => void>>()
  return {
    installing: null as unknown,
    waiting: null as unknown,
    update: vi.fn(async () => undefined),
    unregister: vi.fn(async () => true),
    addEventListener(type: string, listener: () => void) {
      const bucket = listeners.get(type) ?? new Set()
      bucket.add(listener)
      listeners.set(type, bucket)
    },
    removeEventListener(type: string, listener: () => void) {
      listeners.get(type)?.delete(listener)
    },
    emit(type: string) {
      for (const listener of [...(listeners.get(type) ?? [])]) listener()
    },
    ...overrides,
  }
}

function installServiceWorkerContainer(state: { controller: unknown; registration: unknown }) {
  const listeners = new Map<string, Set<() => void>>()
  const container = {
    get controller() {
      return state.controller
    },
    getRegistration: vi.fn(async () => state.registration),
    addEventListener: vi.fn((type: string, listener: () => void) => {
      const bucket = listeners.get(type) ?? new Set()
      bucket.add(listener)
      listeners.set(type, bucket)
    }),
    removeEventListener: vi.fn((type: string, listener: () => void) => {
      listeners.get(type)?.delete(listener)
    }),
    emit(type: string) {
      for (const listener of [...(listeners.get(type) ?? [])]) listener()
    },
  }
  Object.defineProperty(navigator, 'serviceWorker', { configurable: true, value: container })
  return container
}

describe('legacy API cache worker retirement', () => {
  beforeEach(() => {
    vi.resetModules()
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
    Reflect.deleteProperty(navigator, 'serviceWorker')
  })

  async function load() {
    return (await import('../../pwa/legacyApiCacheWorker')).retireLegacyApiCacheWorker
  }

  /** Runs out the 1.5 s handshake timeout the silent legacy worker forces. */
  async function silence() {
    await vi.advanceTimersByTimeAsync(1_500)
    await Promise.resolve()
  }

  it('accepts a page that no worker controls', async () => {
    installServiceWorkerContainer({ controller: null, registration: null })
    await expect((await load())()).resolves.toBe(true)
  })

  it('accepts a controller that reports the retirement policy', async () => {
    const controller = worker({ retired: true })
    installServiceWorkerContainer({ controller, registration: null })

    await expect((await load())()).resolves.toBe(true)
    expect(controller.posted).toEqual([{ type: API_CACHE_POLICY_QUERY }])
  })

  it('sends skip-waiting to a replacement that only reaches "installed" after update() resolves', async () => {
    // registration.update() resolves inside Install, before the install event's
    // lifetime promises settle, so `waiting` is normally still null when it returns.
    // A one-shot read there would never deliver the message.
    const legacy = worker({ retired: false })
    const replacement = worker({ retired: true, state: 'installing' })
    const registered = registration()
    const state: { controller: unknown; registration: unknown } = {
      controller: legacy,
      registration: registered,
    }
    const container = installServiceWorkerContainer(state)
    registered.update = vi.fn(async () => {
      registered.installing = replacement
      registered.emit('updatefound')
    })

    const retire = (await load())()
    await silence()

    replacement.state = 'installed'
    replacement.emit('statechange')
    await Promise.resolve()
    expect(replacement.posted).toContainEqual({ type: API_CACHE_SKIP_WAITING })

    state.controller = replacement
    container.emit('controllerchange')

    await expect(retire).resolves.toBe(true)
    expect(registered.unregister).not.toHaveBeenCalled()
  })

  it('forces an already-waiting replacement to activate', async () => {
    const legacy = worker({ retired: false })
    const waiting = worker({ retired: true, state: 'installed' })
    const registered = registration({ waiting })
    const state: { controller: unknown; registration: unknown } = {
      controller: legacy,
      registration: registered,
    }
    const container = installServiceWorkerContainer(state)

    const retire = (await load())()
    await silence()
    expect(waiting.posted).toContainEqual({ type: API_CACHE_SKIP_WAITING })

    state.controller = waiting
    container.emit('controllerchange')

    await expect(retire).resolves.toBe(true)
  })

  it('accepts a replacement that claimed this page while the handshake was pending', async () => {
    // Another tab's migration claims every client, so the controller can change
    // mid-handshake. This covers the re-read of navigator.serviceWorker.controller;
    // the controllerchange latch itself is covered by the forced-activation cases.
    const legacy = worker({ retired: false })
    const replacement = worker({ retired: true })
    const registered = registration()
    const state: { controller: unknown; registration: unknown } = {
      controller: legacy,
      registration: registered,
    }
    installServiceWorkerContainer(state)

    const retire = (await load())()
    state.controller = replacement
    await silence()

    await expect(retire).resolves.toBe(true)
    expect(registered.unregister).not.toHaveBeenCalled()
    expect(registered.update).not.toHaveBeenCalled()
  })

  it('does not unregister a replacement that is installed but has not taken over', async () => {
    const legacy = worker({ retired: false })
    const waiting = worker({ retired: true, state: 'installed' })
    const registered = registration({ waiting })
    installServiceWorkerContainer({ controller: legacy, registration: registered })

    const retire = (await load())()
    await silence()
    await vi.advanceTimersByTimeAsync(12_000)

    await expect(retire).resolves.toBe(false)
    expect(registered.unregister).not.toHaveBeenCalled()
  })

  it('unregisters and fails closed when nothing is coming', async () => {
    const legacy = worker({ retired: false })
    const registered = registration()
    installServiceWorkerContainer({ controller: legacy, registration: registered })

    const retire = (await load())()
    await silence()
    await vi.advanceTimersByTimeAsync(12_000)

    await expect(retire).resolves.toBe(false)
    expect(registered.unregister).toHaveBeenCalled()
  })

  it('does not report success when the registration is gone but a silent worker still controls', async () => {
    // unregister() removes the registration but never releases a page the worker
    // already controls, so a missing registration is not by itself safe.
    const legacy = worker({ retired: false })
    installServiceWorkerContainer({ controller: legacy, registration: undefined })

    const retire = (await load())()
    await silence()

    await expect(retire).resolves.toBe(false)
  })

  it('accepts a missing registration once nothing controls the page', async () => {
    const legacy = worker({ retired: false })
    const state: { controller: unknown; registration: unknown } = {
      controller: legacy,
      registration: undefined,
    }
    installServiceWorkerContainer(state)

    const retire = (await load())()
    state.controller = null
    await silence()

    await expect(retire).resolves.toBe(true)
  })

  it('never blocks the app for longer than its deadline when update() stalls', async () => {
    // Session restore and the router guard both await this, so an unbounded update
    // fetch would pin the app on its loading state with a reload that re-enters it.
    const legacy = worker({ retired: false })
    const registered = registration({ update: vi.fn(() => new Promise<undefined>(() => undefined)) })
    installServiceWorkerContainer({ controller: legacy, registration: registered })

    const retire = (await load())()
    await vi.advanceTimersByTimeAsync(12_000)

    await expect(retire).resolves.toBe(false)
  })
})

describe('service worker handshake contract', () => {
  function loadWorker(overrides: Record<string, unknown> = {}) {
    const listeners = new Map<string, (event: unknown) => void>()
    const self = {
      addEventListener: vi.fn((type: string, listener: (event: unknown) => void) => {
        listeners.set(type, listener)
      }),
      skipWaiting: vi.fn(),
      clients: { claim: vi.fn(async () => undefined) },
      ...overrides,
    }
    const caches = {
      keys: vi.fn(async () => [] as string[]),
      has: vi.fn(async () => false),
      open: vi.fn(),
      delete: vi.fn(async () => true),
    }
    new Function('self', 'caches', workerScript)(self, caches)
    return { self, caches, listeners }
  }

  it('answers the policy query and the forced-activation message', () => {
    const { self, listeners } = loadWorker()

    const replies: unknown[] = []
    listeners.get('message')!({
      data: { type: API_CACHE_POLICY_QUERY },
      ports: [{ postMessage: (value: unknown) => replies.push(value) }],
    })
    expect(replies).toEqual([{ policy: API_CACHE_POLICY_RETIRED }])

    listeners.get('message')!({ data: { type: API_CACHE_SKIP_WAITING }, ports: [] })
    expect(self.skipWaiting).toHaveBeenCalledTimes(1)

    // An unrelated message must not trigger either behaviour.
    listeners.get('message')!({ data: { type: 'something-else' }, ports: [] })
    expect(self.skipWaiting).toHaveBeenCalledTimes(1)
    expect(replies).toHaveLength(1)
  })

  it('claims open clients so a page under the legacy worker stops getting replay', async () => {
    const { self, listeners } = loadWorker()
    let activation: Promise<unknown> | undefined
    ;(listeners.get('activate') as (event: { waitUntil: (p: Promise<unknown>) => void }) => void)({
      waitUntil: (promise) => { activation = promise },
    })
    await activation

    expect(self.clients.claim).toHaveBeenCalledTimes(1)
  })

  it('evicts static-cache entries the pre-#2350 extension-only matcher admitted', async () => {
    // An authenticated response stored under a prefixed API base survives an account
    // switch for 30 days otherwise. The current route cannot serve it, but it is still
    // user A's data sitting in user B's browser.
    const deleted: string[] = []
    const cache = {
      keys: vi.fn(async () => [
        { url: 'https://taskdeck.example/taskdeck/api/users/by-username/alice.png' },
        { url: 'https://taskdeck.example/assets/avatar-a1b2.png' },
        { url: 'https://taskdeck.example/icons/icon-192x192.png' },
      ]),
      delete: vi.fn(async (request: { url: string }) => {
        deleted.push(request.url)
        return true
      }),
    }
    const { listeners, caches } = loadWorker()
    caches.has.mockResolvedValue(true)
    caches.open.mockResolvedValue(cache)

    let activation: Promise<unknown> | undefined
    ;(listeners.get('activate') as (event: { waitUntil: (p: Promise<unknown>) => void }) => void)({
      waitUntil: (promise) => { activation = promise },
    })
    await activation

    expect(deleted).toEqual(['https://taskdeck.example/taskdeck/api/users/by-username/alice.png'])
  })
})
