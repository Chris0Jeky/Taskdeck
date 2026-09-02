import { describe, expect, it, vi } from 'vitest'
import workerScript from '../../../public/api-cache-cleanup.js?raw'

describe('legacy API cache service-worker activation', () => {
  it('deletes every API cache version while preserving share-target and static caches', async () => {
    let activate: ((event: { waitUntil: (promise: Promise<unknown>) => void }) => void) | undefined
    const deleted: string[] = []
    let activation: Promise<unknown> | undefined
    const self = {
      addEventListener: vi.fn((type: string, listener: typeof activate) => {
        if (type === 'activate') activate = listener
      }),
    }
    const caches = {
      keys: vi.fn().mockResolvedValue([
        'taskdeck-api-cache',
        'taskdeck-api-cache-v2',
        'taskdeck-api-cache-future',
        'taskdeck-share-target',
        'taskdeck-static-assets',
      ]),
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
    ])
  })

  it('does not block service-worker activation when cache storage rejects', async () => {
    let activate: ((event: { waitUntil: (promise: Promise<unknown>) => void }) => void) | undefined
    let activation: Promise<unknown> | undefined
    const self = {
      addEventListener: vi.fn((type: string, listener: typeof activate) => {
        if (type === 'activate') activate = listener
      }),
    }
    const caches = { keys: vi.fn().mockRejectedValue(new Error('storage unavailable')) }
    const warning = vi.spyOn(console, 'warn').mockImplementation(() => undefined)

    new Function('self', 'caches', workerScript)(self, caches)
    activate!({ waitUntil: (promise) => { activation = promise } })

    await expect(activation).resolves.toBeUndefined()
    expect(warning).toHaveBeenCalledWith('Unable to remove legacy API caches during activation.')
  })

  it('does not block service-worker activation when deleting a legacy cache rejects', async () => {
    let activate: ((event: { waitUntil: (promise: Promise<unknown>) => void }) => void) | undefined
    let activation: Promise<unknown> | undefined
    const self = {
      addEventListener: vi.fn((type: string, listener: typeof activate) => {
        if (type === 'activate') activate = listener
      }),
    }
    const caches = {
      keys: vi.fn().mockResolvedValue(['taskdeck-api-cache-v2']),
      delete: vi.fn().mockRejectedValue(new Error('storage unavailable')),
    }

    new Function('self', 'caches', workerScript)(self, caches)
    activate!({ waitUntil: (promise) => { activation = promise } })

    await expect(activation).resolves.toBeUndefined()
  })
})
