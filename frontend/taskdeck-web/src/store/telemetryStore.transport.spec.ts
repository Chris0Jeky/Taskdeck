import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useTelemetryStore } from './telemetryStore'
import type { ClientTelemetryConfig } from '../api/telemetryApi'

const http = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }))
vi.mock('../api/http', () => ({ default: http }))

const config: ClientTelemetryConfig = {
  telemetry: { enabled: true },
  sentry: { enabled: false, dsn: '', environment: 'test', tracesSampleRate: 0 },
  analytics: { enabled: false, provider: '', scriptUrl: '', siteId: '' },
}

// Emulate only transport settlement. The store and telemetryApi are real;
// the timeout is consumed from the actual request options, not injected there.
function stalledRequest(options?: { timeout?: number }): Promise<never> {
  return new Promise((_resolve, reject) => {
    if (options?.timeout) setTimeout(() => reject(new Error('transport timeout')), options.timeout)
  })
}

describe('telemetry transport deadlines (#2913)', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    setActivePinia(createPinia())
    vi.resetAllMocks()
    vi.stubGlobal('localStorage', { getItem: vi.fn(() => null), setItem: vi.fn() })
    http.get.mockResolvedValue({ data: config })
    http.post.mockResolvedValue({ data: { recorded: 1 } })
  })

  afterEach(() => {
    useTelemetryStore().stopFlushTimer()
    vi.clearAllTimers()
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  it('finishes initialization disabled when configuration transport stalls', async () => {
    http.get.mockImplementationOnce((_url, options) => stalledRequest(options))
    const store = useTelemetryStore()
    let settled = false
    const initialization = store.initialize().then(() => { settled = true })
    await vi.advanceTimersByTimeAsync(9_999)
    expect(settled).toBe(false)
    await vi.advanceTimersByTimeAsync(1)
    expect(settled).toBe(true)
    await initialization
    expect(store.configLoaded).toBe(true)
    expect(store.serverConfig).toBeNull()
    expect(store.isActive).toBe(false)
    expect(http.get).toHaveBeenCalledTimes(1)
  })

  it('releases the flush guard and preserves later events after transport timeout', async () => {
    const store = useTelemetryStore()
    await store.loadConfig()
    store.setConsent(true)
    store.emit('first')
    http.post.mockImplementationOnce((_url, _payload, options) => stalledRequest(options))
    let settled = false
    const first = store.flush().then(() => { settled = true })
    store.emit('second')
    await store.flush()
    expect(http.post).toHaveBeenCalledTimes(1)
    await vi.advanceTimersByTimeAsync(10_000)
    expect(settled).toBe(true)
    await first
    expect(store.eventBuffer.map(event => event.event)).toEqual(['first', 'second'])
    await store.flush()
    expect(http.post).toHaveBeenCalledTimes(2)
    expect(http.post.mock.calls[1]![1].events.map((event: { event: string }) => event.event)).toEqual(['first', 'second'])
    expect(store.eventBuffer).toEqual([])
  })

  it('cannot requeue a timed-out batch across withdrawal and renewed consent', async () => {
    const store = useTelemetryStore()
    await store.loadConfig()
    store.setConsent(true)
    store.emit('withdrawn')
    http.post.mockImplementationOnce((_url, _payload, options) => stalledRequest(options))
    let settled = false
    const old = store.flush().then(() => { settled = true })
    store.setConsent(false)
    store.setConsent(true)
    store.emit('fresh')
    await vi.advanceTimersByTimeAsync(10_000)
    expect(settled).toBe(true)
    await old
    expect(store.eventBuffer.map(event => event.event)).toEqual(['fresh'])
    await store.flush()
    expect(http.post.mock.calls[1]![1].events.map((event: { event: string }) => event.event)).toEqual(['fresh'])
  })
})
