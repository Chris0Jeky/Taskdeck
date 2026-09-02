import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import workerScript from '../../../public/api-cache-cleanup.js?raw'
import {
  API_CACHE_POLICY_QUERY,
  API_CACHE_POLICY_RETIRED,
  API_CACHE_SKIP_WAITING,
} from '../../pwa/legacyApiCacheWorker'

/** Minimal ServiceWorker stand-in: answers the policy handshake, or stays silent. */
function worker(options: { retired: boolean }) {
  return {
    posted: [] as unknown[],
    postMessage(message: unknown, transfer: MessagePort[]) {
      this.posted.push(message)
      if (!options.retired) return
      transfer[0].postMessage({ policy: API_CACHE_POLICY_RETIRED })
    },
  }
}

function installServiceWorkerContainer(state: {
  controller: unknown
  registration: unknown
}) {
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

  it('forces a waiting replacement to activate when the legacy worker is in control', async () => {
    const legacy = worker({ retired: false })
    const replacement = worker({ retired: true })
    const waiting = { postMessage: vi.fn() }
    const state: { controller: unknown; registration: unknown } = {
      controller: legacy,
      registration: { update: vi.fn(async () => undefined), waiting, unregister: vi.fn() },
    }
    const container = installServiceWorkerContainer(state)

    const retire = (await load())()
    // The legacy worker never answers the handshake; the timeout is the answer.
    await vi.advanceTimersByTimeAsync(2_000)
    await Promise.resolve()
    expect(waiting.postMessage).toHaveBeenCalledWith({ type: API_CACHE_SKIP_WAITING })

    state.controller = replacement
    container.emit('controllerchange')

    await expect(retire).resolves.toBe(true)
    expect((state.registration as { unregister: () => void }).unregister).not.toHaveBeenCalled()
  })

  it('fails closed and unregisters when no replacement takes over', async () => {
    const legacy = worker({ retired: false })
    const unregister = vi.fn(async () => true)
    installServiceWorkerContainer({
      controller: legacy,
      registration: { update: vi.fn(async () => undefined), waiting: null, unregister },
    })

    const retire = (await load())()
    await vi.advanceTimersByTimeAsync(2_000)
    await vi.advanceTimersByTimeAsync(10_000)

    await expect(retire).resolves.toBe(false)
    expect(unregister).toHaveBeenCalled()
  })
})

describe('service worker handshake contract', () => {
  it('answers the policy query and the forced-activation message', async () => {
    const listeners = new Map<string, (event: unknown) => void>()
    const skipWaiting = vi.fn()
    const self = {
      addEventListener: vi.fn((type: string, listener: (event: unknown) => void) => {
        listeners.set(type, listener)
      }),
      skipWaiting,
      clients: { claim: vi.fn(async () => undefined) },
    }
    new Function('self', 'caches', workerScript)(self, { keys: async () => [] })

    const replies: unknown[] = []
    listeners.get('message')!({
      data: { type: API_CACHE_POLICY_QUERY },
      ports: [{ postMessage: (value: unknown) => replies.push(value) }],
    })
    expect(replies).toEqual([{ policy: API_CACHE_POLICY_RETIRED }])

    listeners.get('message')!({ data: { type: API_CACHE_SKIP_WAITING }, ports: [] })
    expect(skipWaiting).toHaveBeenCalledTimes(1)

    // An unrelated message must not trigger either behaviour.
    listeners.get('message')!({ data: { type: 'something-else' }, ports: [] })
    expect(skipWaiting).toHaveBeenCalledTimes(1)
    expect(replies).toHaveLength(1)
  })

  it('claims open clients so a page under the legacy worker stops getting replay', async () => {
    let activate: ((event: { waitUntil: (promise: Promise<unknown>) => void }) => void) | undefined
    let activation: Promise<unknown> | undefined
    const claim = vi.fn(async () => undefined)
    const self = {
      addEventListener: vi.fn((type: string, listener: typeof activate) => {
        if (type === 'activate') activate = listener
      }),
      skipWaiting: vi.fn(),
      clients: { claim },
    }
    new Function('self', 'caches', workerScript)(self, { keys: async () => [], delete: async () => true })

    activate!({ waitUntil: (promise) => { activation = promise } })
    await activation

    expect(claim).toHaveBeenCalledTimes(1)
  })
})
