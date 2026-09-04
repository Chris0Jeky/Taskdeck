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

    // Two sweeps by design: one at script evaluation (during install) and an
    // unconditional one at activation, because the old worker can still poison the
    // static cache in between.
    expect([...new Set(deleted)]).toEqual([
      'taskdeck-api-cache',
      'taskdeck-api-cache-v2',
      'taskdeck-api-cache-future',
      'taskdeck-static-assets',
    ])
    expect(deleted.filter((name) => name === 'taskdeck-static-assets')).toHaveLength(2)
    expect(deleted).not.toContain('taskdeck-share-target')
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
    const caches = {
      keys: vi.fn().mockRejectedValue(new Error('storage unavailable')),
      has: vi.fn(async () => false),
      open: vi.fn(),
      delete: vi.fn(),
    }
    const warning = vi.spyOn(console, 'warn').mockImplementation(() => undefined)

    new Function('self', 'caches', workerScript)(self, caches)
    activate!({ waitUntil: (promise) => { activation = promise } })

    // The original failure is surfaced rather than replaced, so an operator sees
    // which storage call actually broke the migration.
    await expect(activation).rejects.toThrow('storage unavailable')
    expect(warning).toHaveBeenCalledWith(
      'Unable to retire legacy Taskdeck runtime caches.',
      expect.any(Error),
    )
    warning.mockRestore()
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
    const warning = vi.spyOn(console, 'warn').mockImplementation(() => undefined)

    new Function('self', 'caches', workerScript)(self, caches)
    activate!({ waitUntil: (promise) => { activation = promise } })

    await expect(activation).rejects.toThrow('storage unavailable')
    warning.mockRestore()
  })
})
