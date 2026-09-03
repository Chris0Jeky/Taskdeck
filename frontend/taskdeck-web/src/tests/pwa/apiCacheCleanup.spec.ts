import { describe, expect, it, vi } from 'vitest'
import workerScript from '../../../public/api-cache-cleanup.js?raw'

describe('legacy API cache service-worker activation', () => {
  it('deletes every API cache version and the static cache while preserving share-target', async () => {
    let activate: ((event: { waitUntil: (promise: Promise<unknown>) => void }) => void) | undefined
    const deleted: string[] = []
    let activation: Promise<unknown> | undefined
    const self = {
      addEventListener: vi.fn((type: string, listener: typeof activate) => {
        if (type === 'activate') activate = listener
      }),
      skipWaiting: vi.fn(),
      clients: { claim: vi.fn(async () => undefined) },
    }
    const caches = {
      keys: vi.fn().mockResolvedValue([
        'taskdeck-api-cache',
        'taskdeck-api-cache-v2',
        'taskdeck-api-cache-future',
        'taskdeck-share-target',
        'taskdeck-static-assets',
      ]),
      has: vi.fn(async () => false),
      open: vi.fn(),
      delete: vi.fn(async (cacheName: string) => {
        deleted.push(cacheName)
        return true
      }),
    }

    new Function('self', 'caches', workerScript)(self, caches)
    activate!({ waitUntil: (promise) => { activation = promise } })
    await activation

    expect(deleted).toEqual([
      'taskdeck-api-cache',
      'taskdeck-api-cache-v2',
      'taskdeck-api-cache-future',
      'taskdeck-static-assets',
    ])
  })

  it('rejects service-worker activation when cache storage rejects', async () => {
    let activate: ((event: { waitUntil: (promise: Promise<unknown>) => void }) => void) | undefined
    let activation: Promise<unknown> | undefined
    const self = {
      addEventListener: vi.fn((type: string, listener: typeof activate) => {
        if (type === 'activate') activate = listener
      }),
      skipWaiting: vi.fn(),
      clients: { claim: vi.fn(async () => undefined) },
    }
    const caches = { keys: vi.fn().mockRejectedValue(new Error('storage unavailable')), delete: vi.fn() }
    const warning = vi.spyOn(console, 'warn').mockImplementation(() => undefined)

    new Function('self', 'caches', workerScript)(self, caches)
    activate!({ waitUntil: (promise) => { activation = promise } })

    await expect(activation).rejects.toThrow('Legacy API cache cleanup failed.')
    expect(warning).toHaveBeenCalledWith('Unable to remove legacy API caches during activation.')
  })

  it('rejects service-worker activation when deleting a legacy cache rejects', async () => {
    let activate: ((event: { waitUntil: (promise: Promise<unknown>) => void }) => void) | undefined
    let activation: Promise<unknown> | undefined
    const self = {
      addEventListener: vi.fn((type: string, listener: typeof activate) => {
        if (type === 'activate') activate = listener
      }),
      skipWaiting: vi.fn(),
      clients: { claim: vi.fn(async () => undefined) },
    }
    const caches = {
      keys: vi.fn().mockResolvedValue(['taskdeck-api-cache-v2']),
      has: vi.fn(async () => false),
      open: vi.fn(),
      delete: vi.fn().mockRejectedValue(new Error('storage unavailable')),
    }

    new Function('self', 'caches', workerScript)(self, caches)
    activate!({ waitUntil: (promise) => { activation = promise } })

    await expect(activation).rejects.toThrow('Legacy API cache cleanup failed.')
  })
})
