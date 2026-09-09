import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useTelemetryStore } from './telemetryStore'

const api = vi.hoisted(() => ({ getConfig: vi.fn(), sendEvents: vi.fn() }))
vi.mock('../api/telemetryApi', () => ({ telemetryApi: api }))

describe('telemetry consent ownership', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    vi.clearAllMocks()
    localStorage.clear()
    setActivePinia(createPinia())
    api.getConfig.mockResolvedValue({
      telemetry: { enabled: true },
      analytics: { enabled: false },
      sentry: { enabled: false },
    })
  })

  afterEach(() => {
    vi.clearAllTimers()
    vi.restoreAllMocks()
    vi.useRealTimers()
  })

  async function activeStore() {
    const store = useTelemetryStore()
    await store.loadConfig()
    store.setConsent(true)
    return store
  }

  function pendingSend() {
    let reject!: (reason: Error) => void
    api.sendEvents.mockImplementationOnce(() => new Promise<void>((_resolve, fail) => {
      reject = fail
    }))
    return () => reject(new Error('offline'))
  }

  it('does not requeue a failed request after withdrawal', async () => {
    const store = await activeStore()
    const reject = pendingSend()
    store.emit('app.started')
    const flushing = store.flush()
    store.setConsent(false)
    reject()
    await flushing
    expect(store.eventBuffer).toHaveLength(0)
    expect(store.isActive).toBe(false)
  })

  it('does not revive old events after withdrawal and a fresh opt-in', async () => {
    const store = await activeStore()
    const reject = pendingSend()
    const oldSession = store.sessionId
    store.emit('old.event')
    const flushing = store.flush()
    store.setConsent(false)
    store.setConsent(true)
    store.emit('new.event')
    reject()
    await flushing
    expect(store.eventBuffer.map((event) => event.event)).toEqual(['new.event'])
    expect(store.sessionId).not.toBe(oldSession)
  })

  it('retains retry behavior within uninterrupted consent', async () => {
    const store = await activeStore()
    api.sendEvents.mockRejectedValueOnce(new Error('offline'))
    store.emit('retry.event')
    await store.flush()
    expect(store.eventBuffer.map((event) => event.event)).toEqual(['retry.event'])
  })

  it('revokes and stops the timer even when persistence fails', async () => {
    const store = await activeStore()
    store.emit('buffered.event')
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('storage unavailable')
    })
    expect(() => store.setConsent(false)).not.toThrow()
    expect(store.eventBuffer).toHaveLength(0)
    expect(store.isActive).toBe(false)
    expect(vi.getTimerCount()).toBe(0)
  })

  it('does not restore consent when storage cannot be read', () => {
    const store = useTelemetryStore()
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('storage unavailable')
    })
    expect(() => store.restoreConsent()).not.toThrow()
    expect(store.consentGiven).toBe(false)
  })
})
