import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import TodayCadence from '../../../../views/paper/today/TodayCadence.vue'
import type { DossierCadence } from '../../../../composables/useTodayDossier'

const CADENCE: DossierCadence = {
  weights: Array.from({ length: 24 }, (_, i) => (i === 13 ? 4 : i % 5)),
  peakHourIndex: 13,
  firstAction: '08:00 · capture',
  peakAction: '13:00 · 7 events',
  lastAction: '17:00 · seal',
}

const ORIGINAL_MATCH_MEDIA = window.matchMedia

function setReducedMotion(prefers: boolean) {
  // Stub matchMedia for the prefers-reduced-motion query.
  // Other queries fall back to the default implementation.
  window.matchMedia = vi.fn().mockImplementation((query: string) => {
    if (query === '(prefers-reduced-motion: reduce)') {
      return {
        matches: prefers,
        media: query,
        onchange: null,
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
        addListener: vi.fn(),
        removeListener: vi.fn(),
        dispatchEvent: vi.fn(),
      } as unknown as MediaQueryList
    }
    return {
      matches: false,
      media: query,
      onchange: null,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      addListener: vi.fn(),
      removeListener: vi.fn(),
      dispatchEvent: vi.fn(),
    } as unknown as MediaQueryList
  })
}

describe('TodayCadence', () => {
  beforeEach(() => {
    setReducedMotion(false)
  })

  afterEach(() => {
    window.matchMedia = ORIGINAL_MATCH_MEDIA
  })

  it('renders 24 SVG bars (one per hour)', () => {
    const wrapper = mount(TodayCadence, { props: { cadence: CADENCE } })
    const bars = wrapper.findAll('rect.today-cadence__bar')
    expect(bars).toHaveLength(24)
  })

  it('marks the peak hour bar with data-peak', () => {
    const wrapper = mount(TodayCadence, { props: { cadence: CADENCE } })
    const peak = wrapper.findAll('rect.today-cadence__bar').find(b => b.attributes('data-peak') === 'true')
    expect(peak?.attributes('data-hour')).toBe('13')
  })

  it('disables the ember pulse when prefers-reduced-motion is on', async () => {
    setReducedMotion(true)
    const wrapper = mount(TodayCadence, { props: { cadence: CADENCE } })
    await wrapper.vm.$nextTick()
    expect(wrapper.find('.today-cadence').attributes('data-pulse')).toBe('off')
  })

  it('keeps the ember pulse on when reduced motion is not requested', async () => {
    setReducedMotion(false)
    const wrapper = mount(TodayCadence, { props: { cadence: CADENCE } })
    await wrapper.vm.$nextTick()
    expect(wrapper.find('.today-cadence').attributes('data-pulse')).toBe('on')
  })
})
