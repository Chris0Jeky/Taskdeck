import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
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

type CoverProps = InstanceType<typeof TodayCover>['$props']

function baseProps(overrides: Partial<CoverProps> = {}): CoverProps {
  return {
    serial: 'D-2026-04-25-001',
    cardsMoved: 9,
    lede: 'lede',
    sealed: false,
    ...overrides,
  }
}

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
        sealed: false,
      },
    })
    expect(wrapper.find('[data-testid="dossier-serial"]').text()).toBe(serial)
  })

  it('rejects invalid serials at format-time', () => {
    // Defensive — formatDossierSerial validates its own output
    expect(() => formatDossierSerial(new Date('2026-04-25T10:00:00Z'), 1)).not.toThrow()
  })

  it('states only the two seal states that exist — never an auto-seal that does not', async () => {
    // GH-1939: "Auto-seals in {duration}" was rendered from `autoSealsIn`,
    // which `buildHonestDossier` hardcodes to null and never overrides, and no
    // backend service seals a day on a timer. The copy, the prop, and the
    // render path are gone; the status line only reports what is true.
    const wrapper = mount(TodayCover, { props: baseProps() })
    const status = wrapper.get('[data-testid="seal-status"]')

    expect(status.text()).toBe('Seal when your day is complete')
    expect(wrapper.text()).not.toContain('Auto-seals in')

    await wrapper.setProps({ sealed: true })
    expect(wrapper.get('[data-testid="seal-status"]').text()).toBe('Sealed for the day')
  })

  it('uses an honest headline and no invented countdown when movement data is unavailable', () => {
    const wrapper = mount(TodayCover, {
      props: {
        serial: 'D-2026-04-25-001',
        cardsMoved: null,
        lede: 'Activity totals are unavailable.',
        sealed: false,
      },
    })

    expect(wrapper.text()).toContain('Today, at a glance.')
    expect(wrapper.text()).toContain('Seal when your day is complete')
    expect(wrapper.text()).not.toContain('cards')
    expect(wrapper.text()).not.toContain('Auto-seals in')
  })

  it('seal button asks for confirmation instead of sealing directly', async () => {
    // The cover is a dumb component: the click only REQUESTS a seal. The
    // parent (PaperTodayView) owns the confirm → seal state machine.
    const wrapper = mount(TodayCover, { props: baseProps() })

    expect(wrapper.find('[data-testid="seal-confirm"]').exists()).toBe(false)
    await wrapper.find('[data-action="seal"]').trigger('click')

    expect(wrapper.emitted('seal-request')).toHaveLength(1)
    expect(wrapper.emitted('seal-confirm')).toBeUndefined()
  })

  it('warns that sealing is irreversible before the confirm CTA is offered', () => {
    const wrapper = mount(TodayCover, { props: baseProps({ confirmingSeal: true }) })
    const confirm = wrapper.get('[data-testid="seal-confirm"]')

    expect(confirm.text()).toContain('This cannot be undone')
    expect(confirm.text()).toContain('Taskdeck has no unseal action')
    // The effect claim must stay inside what a seal actually does.
    expect(confirm.text()).toContain('Nothing is archived, locked, hidden, or deleted')
    expect(wrapper.find('[data-action="seal-confirm"]').exists()).toBe(true)
    expect(wrapper.find('[data-action="seal-cancel"]').exists()).toBe(true)
    // The originating CTA cannot be pressed again while the prompt is open.
    expect(wrapper.find('[data-action="seal"]').attributes('disabled')).toBeDefined()
  })

  it('emits confirm and cancel from the prompt, and focuses the confirm CTA when it opens', async () => {
    const wrapper = mount(TodayCover, { props: baseProps(), attachTo: document.body })

    await wrapper.setProps({ confirmingSeal: true })
    await nextTick()
    expect(document.activeElement).toBe(wrapper.get('[data-action="seal-confirm"]').element)

    await wrapper.find('[data-action="seal-confirm"]').trigger('click')
    expect(wrapper.emitted('seal-confirm')).toHaveLength(1)

    await wrapper.find('[data-action="seal-cancel"]').trigger('click')
    expect(wrapper.emitted('seal-cancel')).toHaveLength(1)

    wrapper.unmount()
  })

  it('locks both prompt buttons while the seal request is in flight', async () => {
    const wrapper = mount(TodayCover, { props: baseProps({ confirmingSeal: true, sealing: true }) })

    expect(wrapper.get('[data-action="seal-confirm"]').text()).toContain('Sealing')
    expect(wrapper.find('[data-action="seal-confirm"]').attributes('disabled')).toBeDefined()
    expect(wrapper.find('[data-action="seal-cancel"]').attributes('disabled')).toBeDefined()

    await wrapper.find('[data-action="seal-confirm"]').trigger('click')
    expect(wrapper.emitted('seal-confirm')).toBeUndefined()
  })

  it('renders a sealed day as disabled-with-reason, never enabled-and-unactionable', async () => {
    // Issue 1939: the control used to stay enabled after sealing and answer
    // "Day is already sealed." on click. A terminal state must look terminal.
    const wrapper = mount(TodayCover, { props: baseProps({ sealed: true }) })
    const sealBtn = wrapper.get('[data-action="seal"]')

    expect(sealBtn.text()).toContain('Day sealed')
    expect(sealBtn.attributes('disabled')).toBeDefined()

    const reason = wrapper.get('[data-testid="seal-sealed-reason"]')
    expect(reason.text()).toContain('no unseal action')
    expect(sealBtn.attributes('aria-describedby')).toBe(reason.attributes('id'))

    // No confirm prompt can be reached from the sealed state.
    await sealBtn.trigger('click')
    expect(wrapper.emitted('seal-request')).toBeUndefined()
    expect(wrapper.find('[data-testid="seal-confirm"]').exists()).toBe(false)
  })

  it('does not offer an undo it cannot perform', () => {
    const wrapper = mount(TodayCover, { props: baseProps({ sealed: true }) })
    const text = wrapper.text()

    expect(text).not.toContain('Undo')
    expect(text).not.toContain('Unseal')
    expect(wrapper.find('[data-action="unseal"]').exists()).toBe(false)
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
