import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useTelemetryStore } from './telemetryStore'
import { useToastStore } from './toastStore'

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
    vi.unstubAllGlobals()
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

  function stubStorageMethod(method: 'getItem' | 'setItem') {
    const methodMock = vi.fn(() => {
      throw new Error('storage unavailable')
    })
    vi.stubGlobal('localStorage', { [method]: methodMock } as unknown as Storage)
    return methodMock
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

  it('drops a failed batch when server telemetry becomes inactive in flight', async () => {
    const store = await activeStore()
    const reject = pendingSend()
    store.emit('config-loss.event')
    const flushing = store.flush()
    store.serverConfig = null
    reject()
    await flushing

    expect(store.consentGiven).toBe(true)
    expect(store.isActive).toBe(false)
    expect(store.eventBuffer).toHaveLength(0)
  })

  it('revokes and warns even when the disabled preference cannot be persisted', async () => {
    const store = await activeStore()
    const toast = useToastStore()
    store.emit('buffered.event')
    const setItem = stubStorageMethod('setItem')

    expect(() => store.setConsent(false)).not.toThrow()

    expect(setItem).toHaveBeenCalledWith('taskdeck_telemetry_consent', 'false')
    expect(store.eventBuffer).toHaveLength(0)
    expect(store.isActive).toBe(false)
    expect(vi.getTimerCount()).toBe(0)
    expect(toast.toasts).toHaveLength(1)
    expect(toast.toasts[0]).toMatchObject({
      type: 'warning',
      duration: 0,
      title: 'Telemetry preference not saved',
      label: 'warning',
      message:
        'Telemetry is disabled for this session, but that choice could not be saved. It may be enabled again after reload.',
    })
  })

  it('warns that an unpersisted opt-in applies only to the current session', async () => {
    const store = useTelemetryStore()
    const toast = useToastStore()
    await store.loadConfig()
    const setItem = stubStorageMethod('setItem')

    expect(() => store.setConsent(true)).not.toThrow()

    expect(setItem).toHaveBeenCalledWith('taskdeck_telemetry_consent', 'true')
    expect(store.isActive).toBe(true)
    expect(toast.toasts).toHaveLength(1)
    expect(toast.toasts[0]).toMatchObject({
      type: 'warning',
      duration: 0,
      title: 'Telemetry preference not saved',
      label: 'warning',
      message:
        'Telemetry is enabled for this session, but that choice could not be saved. It may be disabled after reload.',
    })
  })

  it('replaces the prior persistence warning without removing unrelated toasts', () => {
    const store = useTelemetryStore()
    const toast = useToastStore()
    const unrelatedToastId = toast.info('Background sync is delayed', 0)
    const setItem = stubStorageMethod('setItem')

    store.setConsent(true)
    const firstWarning = toast.toasts.find(
      (candidate) => candidate.title === 'Telemetry preference not saved',
    )
    expect(firstWarning?.message).toContain('Telemetry is enabled for this session')

    store.setConsent(false)

    expect(setItem).toHaveBeenNthCalledWith(1, 'taskdeck_telemetry_consent', 'true')
    expect(setItem).toHaveBeenNthCalledWith(2, 'taskdeck_telemetry_consent', 'false')
    const persistenceWarnings = toast.toasts.filter(
      (candidate) => candidate.title === 'Telemetry preference not saved',
    )
    expect(persistenceWarnings).toHaveLength(1)
    expect(persistenceWarnings[0]?.id).not.toBe(firstWarning?.id)
    expect(persistenceWarnings[0]?.message).toBe(
      'Telemetry is disabled for this session, but that choice could not be saved. It may be enabled again after reload.',
    )
    expect(toast.toasts.some((candidate) => candidate.id === unrelatedToastId)).toBe(true)
  })

  it('clears only the persistence warning after a later successful consent write', () => {
    const store = useTelemetryStore()
    const toast = useToastStore()
    const unrelatedToastId = toast.info('Background sync remains delayed', 0)
    const setItem = vi
      .fn()
      .mockImplementationOnce(() => {
        throw new Error('storage unavailable')
      })
      .mockImplementationOnce(() => undefined)
    vi.stubGlobal('localStorage', { setItem } as unknown as Storage)

    store.setConsent(true)
    expect(
      toast.toasts.filter((candidate) => candidate.title === 'Telemetry preference not saved'),
    ).toHaveLength(1)

    store.setConsent(false)

    expect(store.consentGiven).toBe(false)
    expect(vi.getTimerCount()).toBe(0)
    expect(
      toast.toasts.filter((candidate) => candidate.title === 'Telemetry preference not saved'),
    ).toHaveLength(0)
    expect(toast.toasts).toHaveLength(1)
    expect(toast.toasts.some((candidate) => candidate.id === unrelatedToastId)).toBe(true)
  })

  it('does not restore consent when storage cannot be read', () => {
    window.localStorage.setItem('taskdeck_telemetry_consent', 'true')
    const getItem = stubStorageMethod('getItem')
    const store = useTelemetryStore()

    expect(() => store.restoreConsent()).not.toThrow()

    expect(getItem).toHaveBeenCalledWith('taskdeck_telemetry_consent')
    expect(store.consentGiven).toBe(false)
  })
})
