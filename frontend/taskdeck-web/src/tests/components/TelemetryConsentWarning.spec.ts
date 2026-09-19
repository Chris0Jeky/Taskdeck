import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import PaperToastContainer from '../../components/paper/PaperToastContainer.vue'
import { useTelemetryStore } from '../../store/telemetryStore'

const api = vi.hoisted(() => ({ getConfig: vi.fn(), sendEvents: vi.fn() }))
vi.mock('../../api/telemetryApi', () => ({ telemetryApi: api }))

describe('telemetry consent persistence warning surface', () => {
  let wrapper: VueWrapper | null = null

  beforeEach(() => {
    setActivePinia(createPinia())
    vi.useFakeTimers()
  })

  afterEach(() => {
    wrapper?.unmount()
    wrapper = null
    vi.clearAllTimers()
    vi.unstubAllGlobals()
    vi.useRealTimers()
  })

  it('shows only the latest failed choice and removes it after persistence recovers', async () => {
    let writesFail = true
    const setItem = vi.fn(() => {
      if (writesFail) {
        throw new Error('storage unavailable')
      }
    })
    vi.stubGlobal('localStorage', { setItem } as unknown as Storage)

    wrapper = mount(PaperToastContainer)
    const telemetry = useTelemetryStore()

    telemetry.setConsent(true)
    await nextTick()
    await flushPromises()
    await nextTick()

    let cards = wrapper.findAll('.paper-toast')
    expect(cards).toHaveLength(1)
    expect(cards[0]?.text()).toContain('Telemetry is enabled for this session')

    telemetry.setConsent(false)
    await nextTick()
    await flushPromises()
    await nextTick()

    cards = wrapper.findAll('.paper-toast')
    expect(cards).toHaveLength(1)
    expect(cards[0]?.text()).toContain('Telemetry is disabled for this session')
    expect(cards[0]?.text()).not.toContain('Telemetry is enabled for this session')

    writesFail = false
    telemetry.setConsent(false)
    await nextTick()
    await flushPromises()
    await nextTick()

    expect(setItem).toHaveBeenCalledTimes(3)
    expect(wrapper.findAll('.paper-toast')).toHaveLength(0)
  })
})
