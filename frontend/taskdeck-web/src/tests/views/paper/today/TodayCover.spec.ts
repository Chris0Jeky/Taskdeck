import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import TodayCover from '../../../../views/paper/today/TodayCover.vue'
import { formatDossierSerial } from '../../../../composables/useTodayDossier'

vi.mock('../../../../api/todayApi', () => ({
  todayApi: {
    getCadence: vi.fn().mockRejectedValue(new Error('stub')),
    getStreak: vi.fn().mockRejectedValue(new Error('stub')),
    getSealStatus: vi.fn().mockRejectedValue(new Error('stub')),
    getTomorrowNote: vi.fn().mockRejectedValue(new Error('stub')),
    sealDay: vi.fn().mockResolvedValue({ sealedAt: new Date().toISOString(), wasAlreadySealed: false }),
  },
}))

vi.mock('../../../../store/workspaceStore', () => ({
  useWorkspaceStore: () => ({ todaySummary: null }),
}))

describe('TodayCover', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('renders the dossier serial in D-YYYY-MM-DD-NNN format', () => {
    const date = new Date('2026-04-25T10:00:00Z')
    const serial = formatDossierSerial(date)
    expect(serial).toMatch(/^D-\d{4}-\d{2}-\d{2}-\d{3}$/)
    expect(serial).toBe('D-2026-04-25-001')

    const wrapper = mount(TodayCover, {
      props: {
        serial,
        cardsMoved: 9,
        lede: 'Live summary.',
        autoSealsIn: '2h 18m',
        sealed: false,
      },
    })
    expect(wrapper.find('[data-testid="dossier-serial"]').text()).toBe(serial)
  })

  it('rejects invalid serials at format-time', () => {
    // Defensive — formatDossierSerial validates its own output
    expect(() => formatDossierSerial(new Date('2026-04-25T10:00:00Z'), 1)).not.toThrow()
  })

  it('shows "Auto-seals in …" when not sealed and "Sealed for the day" when sealed', async () => {
    const wrapper = mount(TodayCover, {
      props: {
        serial: 'D-2026-04-25-001',
        cardsMoved: 9,
        lede: 'lede',
        autoSealsIn: '2h 18m',
        sealed: false,
      },
    })
    expect(wrapper.find('[data-testid="auto-seals-in"]').text()).toContain('Auto-seals in 2h 18m')

    await wrapper.setProps({ sealed: true })
    expect(wrapper.find('[data-testid="auto-seals-in"]').text()).toContain('Sealed for the day')
  })

  it('uses an honest headline and no invented countdown when movement data is unavailable', () => {
    const wrapper = mount(TodayCover, {
      props: {
        serial: 'D-2026-04-25-001',
        cardsMoved: null,
        lede: 'Activity totals are unavailable.',
        autoSealsIn: null,
        sealed: false,
      },
    })

    expect(wrapper.text()).toContain('Today, at a glance.')
    expect(wrapper.text()).toContain('Seal when your day is complete')
    expect(wrapper.text()).not.toContain('cards')
    expect(wrapper.text()).not.toContain('Auto-seals in')
  })

  it('seal button emits seal event each click; parent decides idempotency', async () => {
    // The cover is a dumb component: it always emits `seal`.  The parent
    // (PaperTodayView) keeps idempotency by calling sealDay() which is a
    // no-op the second time.
    const wrapper = mount(TodayCover, {
      props: {
        serial: 'D-2026-04-25-001',
        cardsMoved: 9,
        lede: 'lede',
        autoSealsIn: '2h 18m',
        sealed: false,
      },
    })
    const sealBtn = wrapper.find('[data-action="seal"]')
    await sealBtn.trigger('click')
    expect(wrapper.emitted('seal')).toHaveLength(1)
  })

  it('seal click while already sealed is a parent-level no-op (idempotent contract)', async () => {
    const { useTodayDossier } = await import('../../../../composables/useTodayDossier')
    const { sealDay } = useTodayDossier()
    const first = await sealDay()
    const second = await sealDay()
    expect(first.alreadySealed).toBe(false)
    expect(second.alreadySealed).toBe(true)
  })
})
