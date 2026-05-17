import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { runInNewContext } from 'node:vm'
import { describe, expect, it, vi } from 'vitest'

type FetchEvent = {
  request: {
    method: string
    url: string
    headers: Headers
    formData: () => Promise<{ get: (name: string) => string | null }>
  }
  response?: Promise<TestResponse> | TestResponse
  respondWith: (response: Promise<TestResponse> | TestResponse) => void
}

class TestResponse {
  readonly body: unknown
  readonly status: number
  readonly headers: Headers
  readonly redirectedTo: string | null

  constructor(body: unknown = null, init: ResponseInit = {}) {
    this.body = body
    this.status = init.status ?? 200
    this.headers = new Headers(init.headers)
    this.redirectedTo = null
  }

  static redirect(url: string, status = 302): TestResponse {
    const response = new TestResponse(null, { status })
    ;(response as { redirectedTo: string }).redirectedTo = url
    return response
  }
}

function loadFetchHandler() {
  let fetchHandler: ((event: FetchEvent) => void) | null = null
  const cachePut = vi.fn()

  const context = {
    URL,
    Headers,
    Response: TestResponse,
    JSON,
    Math,
    Date,
    String,
    encodeURIComponent,
    caches: {
      open: vi.fn(async () => ({ put: cachePut })),
    },
    self: {
      location: { origin: 'https://taskdeck.test' },
      crypto: { randomUUID: () => 'share-id-1' },
      clients: { matchAll: vi.fn(async () => []) },
      addEventListener: vi.fn((type: string, handler: (event: FetchEvent) => void) => {
        if (type === 'fetch') {
          fetchHandler = handler
        }
      }),
    },
  }

  const scriptPath = resolve(process.cwd(), 'public/share-target-handler.js')
  runInNewContext(readFileSync(scriptPath, 'utf8'), context)

  if (!fetchHandler) {
    throw new Error('share-target fetch handler was not registered')
  }

  return { fetchHandler, cachesOpen: context.caches.open, cachePut }
}

function makeEvent(headers: Record<string, string> = {}): FetchEvent {
  const form = new Map([
    ['title', 'Shared title'],
    ['text', 'Shared text'],
    ['url', 'https://example.test/item'],
  ])

  return {
    request: {
      method: 'POST',
      url: 'https://taskdeck.test/capture/share',
      headers: new Headers(headers),
      formData: async () => ({ get: (name: string) => form.get(name) ?? null }),
    },
    respondWith(response) {
      this.response = response
    },
  }
}

describe('share-target service worker handler', () => {
  it('rejects cross-site POST attempts before caching shared content', async () => {
    const { fetchHandler, cachesOpen } = loadFetchHandler()
    const event = makeEvent({
      Origin: 'https://attacker.test',
      'Sec-Fetch-Site': 'cross-site',
    })

    fetchHandler(event)
    const response = await event.response

    expect(response?.status).toBe(403)
    expect(cachesOpen).not.toHaveBeenCalled()
  })

  it('rejects POST attempts with missing fetch metadata and no same-origin signal', async () => {
    const { fetchHandler, cachesOpen } = loadFetchHandler()
    const event = makeEvent()

    fetchHandler(event)
    const response = await event.response

    expect(response?.status).toBe(403)
    expect(cachesOpen).not.toHaveBeenCalled()
  })

  it('accepts OS share-target POSTs with browser fetch metadata', async () => {
    const { fetchHandler, cachesOpen, cachePut } = loadFetchHandler()
    const event = makeEvent({ 'Sec-Fetch-Site': 'none' })

    fetchHandler(event)
    const response = await event.response

    expect(response?.status).toBe(303)
    expect(response?.redirectedTo).toBe('/capture/share?fromShareTarget=1&shareId=share-id-1')
    expect(cachesOpen).toHaveBeenCalledWith('taskdeck-share-target')
    expect(cachePut).toHaveBeenCalledWith(
      '/capture/share-data/share-id-1',
      expect.objectContaining({
        status: 200,
      }),
    )
  })

  it('accepts same-origin POSTs when fetch metadata is unavailable', async () => {
    const { fetchHandler, cachePut } = loadFetchHandler()
    const event = makeEvent({ Origin: 'https://taskdeck.test' })

    fetchHandler(event)
    const response = await event.response

    expect(response?.status).toBe(303)
    expect(cachePut).toHaveBeenCalledWith(
      '/capture/share-data/share-id-1',
      expect.any(TestResponse),
    )
  })

  it('accepts same-origin POSTs and still caches under a unique share id', async () => {
    const { fetchHandler, cachePut } = loadFetchHandler()
    const event = makeEvent({
      Origin: 'https://taskdeck.test',
      'Sec-Fetch-Site': 'same-origin',
    })

    fetchHandler(event)
    const response = await event.response

    expect(response?.status).toBe(303)
    expect(cachePut).toHaveBeenCalledWith(
      '/capture/share-data/share-id-1',
      expect.any(TestResponse),
    )
  })
})
