import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const workerMocks = vi.hoisted(() => ({ retire: vi.fn() }))

vi.mock('../../pwa/legacyApiCacheWorker', () => ({
  retireLegacyApiCacheWorker: workerMocks.retire,
}))

interface CacheStorageState {
  names: string[]
  deleteImpl?: (name: string) => Promise<boolean>
}

function installCacheStorage(state: CacheStorageState) {
  const cacheStorage = {
    keys: vi.fn(async () => [...state.names]),
    delete: vi.fn(async (name: string) => {
      if (state.deleteImpl) return state.deleteImpl(name)
      state.names = state.names.filter((candidate) => candidate !== name)
      return true
    }),
  }
  Object.defineProperty(globalThis, 'caches', { configurable: true, value: cacheStorage })
  return cacheStorage
}

describe('legacy API cache purge', () => {
  beforeEach(() => {
    vi.resetModules()
    vi.clearAllMocks()
    workerMocks.retire.mockResolvedValue(true)
  })

  afterEach(() => {
    Reflect.deleteProperty(globalThis, 'caches')
  })

  async function load() {
    return (await import('../../pwa/legacyApiCache')).purgeLegacyApiCaches
  }

  it('removes every legacy namespace and leaves other caches alone', async () => {
    const state = {
      names: [
        'taskdeck-api-cache',
        'taskdeck-api-cache-v2',
        'taskdeck-share-target',
        'taskdeck-static-assets',
      ],
    }
    const cacheStorage = installCacheStorage(state)

    await expect((await load())()).resolves.toBe(true)
    expect(cacheStorage.delete.mock.calls.map(([name]) => name)).toEqual([
      'taskdeck-api-cache',
      'taskdeck-api-cache-v2',
    ])
  })

  it('refuses to report success when the worker retirement failed', async () => {
    // This is the ordering invariant the whole design rests on: a worker that still
    // holds the NetworkFirst API route would repopulate the namespace immediately.
    workerMocks.retire.mockResolvedValue(false)
    const cacheStorage = installCacheStorage({ names: ['taskdeck-api-cache'] })

    await expect((await load())()).resolves.toBe(false)
    expect(cacheStorage.delete).not.toHaveBeenCalled()
  })

  it('treats a cache another tab already removed as a successful purge', async () => {
    // CacheStorage.delete() reports false for a name that is already gone. The
    // namespace is equally safe in that case, so absence is the verdict.
    const state: CacheStorageState = { names: ['taskdeck-api-cache-v2'] }
    state.deleteImpl = async () => {
      state.names = []
      return false
    }
    installCacheStorage(state)

    await expect((await load())()).resolves.toBe(true)
  })

  it('fails closed when a legacy cache survives the purge', async () => {
    installCacheStorage({ names: ['taskdeck-api-cache-v2'], deleteImpl: async () => false })

    await expect((await load())()).resolves.toBe(false)
  })

  it('fails closed when cache storage rejects', async () => {
    Object.defineProperty(globalThis, 'caches', {
      configurable: true,
      value: { keys: vi.fn().mockRejectedValue(new Error('storage unavailable')), delete: vi.fn() },
    })

    await expect((await load())()).resolves.toBe(false)
  })

  it('shares one in-flight purge between concurrent callers', async () => {
    const cacheStorage = installCacheStorage({ names: ['taskdeck-api-cache'] })
    const purge = await load()

    const [first, second] = await Promise.all([purge(), purge()])

    expect(first).toBe(true)
    expect(second).toBe(true)
    expect(cacheStorage.delete).toHaveBeenCalledTimes(1)
  })
})
