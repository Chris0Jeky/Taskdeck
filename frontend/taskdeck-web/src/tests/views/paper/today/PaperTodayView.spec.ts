import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { todayApi } from '../../../../api/todayApi'
import { formatLocalDossierDate } from '../../../../composables/useTodayDossier'
import PaperTodayView from '../../../../views/paper/PaperTodayView.vue'

const toastMocks = vi.hoisted(() => ({
  success: vi.fn(),
  error: vi.fn(),
  info: vi.fn(),
}))

const mockWorkspaceStore = {
  todaySummary: null,
}
const mockSessionStore = {
  userId: 'user-1' as string | null,
}

vi.mock('../../../../api/todayApi', () => ({
  todayApi: {
    getCadence: vi.fn().mockRejectedValue(new Error('stub')),
    getStreak: vi.fn().mockRejectedValue(new Error('stub')),
    getSealStatus: vi.fn().mockRejectedValue(new Error('stub')),
    getTomorrowNote: vi.fn().mockRejectedValue(new Error('stub')),
    sealDay: vi.fn().mockResolvedValue({ sealedAt: new Date().toISOString(), wasAlreadySealed: false }),
    saveTomorrowNote: vi.fn().mockResolvedValue({ id: '1', date: '2026-01-01', text: '', updatedAt: '', createdAt: '' }),
  },
}))

vi.mock('../../../../store/workspaceStore', () => ({
  useWorkspaceStore: () => mockWorkspaceStore,
}))

vi.mock('../../../../store/sessionStore', () => ({
  useSessionStore: () => mockSessionStore,
}))

vi.mock('../../../../store/toastStore', () => ({
  useToastStore: () => toastMocks,
}))

describe('PaperTodayView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    localStorage.clear()
    mockSessionStore.userId = 'user-1'
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('renders live sections and honest empty states for unavailable dossier queries', () => {
    const wrapper = mount(PaperTodayView)

    expect(wrapper.find('[data-paper-today]').exists()).toBe(true)
    expect(wrapper.find('[data-section="cover"]').exists()).toBe(true)
    expect(wrapper.find('[data-section="line-for-tomorrow"]').exists()).toBe(true)

    for (const section of ['stats', 'cadence', 'ledger', 'decisions', 'boards', 'carry-over', 'streak']) {
      expect(wrapper.find(`[data-empty-state="${section}"]`).exists()).toBe(true)
    }
  })

  it('does not render any formerly fabricated dossier claims or a fake pin action', () => {
    const wrapper = mount(PaperTodayView)
    const text = wrapper.text()

    expect(text).toContain('Today, at a glance.')
    expect(text).toContain('No events are being invented')
    expect(text).not.toContain('A quiet Saturday')
    expect(text).not.toContain('haiku')
    expect(text).not.toContain('Sprint 12')
    expect(text).not.toContain('2h 14m')
    expect(text).not.toContain('C-072')
    expect(wrapper.find('[data-action="pin-tomorrow"]').exists()).toBe(false)
  })

  it('renders the dossier serial in the cover and footer', () => {
    const wrapper = mount(PaperTodayView)
    const serial = wrapper.find('[data-testid="dossier-serial"]').text()
    expect(serial).toMatch(/^D-\d{4}-\d{2}-\d{2}-\d{3}$/)
    // Footer also surfaces the same serial token
    expect(wrapper.text()).toContain(serial)
  })

  it('ignores stale local line-for-tomorrow storage in live-backed Paper view', () => {
    const today = formatLocalDossierDate(new Date())
    localStorage.setItem(`td.paper.line-for-tomorrow:user-1:${today}`, 'user-one note')
    localStorage.setItem(`td.paper.line-for-tomorrow:user-2:${today}`, 'user-two note')

    const wrapper = mount(PaperTodayView)
    const input = wrapper.find<HTMLTextAreaElement>('[data-testid="line-for-tomorrow-input"]')

    expect(input.element.value).toBe('')
    expect(input.element.value).not.toBe('user-two note')
  })

  it('does not advertise unimplemented global shortcuts in the footer', () => {
    const wrapper = mount(PaperTodayView)

    expect(wrapper.text()).not.toContain('PRESS S TO SEAL')
    expect(wrapper.text()).not.toContain('⌘L FOR LEDGER')
    expect(wrapper.text()).toContain('SEAL ABOVE')
  })

  it('formats dossier storage dates from local calendar parts', () => {
    const localEvening = new Date(2026, 3, 25, 23, 30)

    expect(formatLocalDossierDate(localEvening)).toBe('2026-04-25')
  })

  it('rolls the dossier serial to the next local day in long-lived sessions', async () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date(2026, 3, 25, 23, 59, 59))
    const wrapper = mount(PaperTodayView)

    expect(wrapper.find('[data-testid="dossier-serial"]').text()).toContain('2026-04-25')

    await vi.advanceTimersByTimeAsync(1_000)
    await wrapper.vm.$nextTick()

    expect(wrapper.find('[data-testid="dossier-serial"]').text()).toContain('2026-04-26')
  })

  it('suppresses duplicate seal failure toast while the first seal is in progress', async () => {
    let resolveSeal!: (value: { sealedAt: string; wasAlreadySealed: boolean }) => void
    vi.mocked(todayApi.sealDay).mockImplementationOnce(
      () => new Promise((resolve) => {
        resolveSeal = resolve
      }),
    )
    const wrapper = mount(PaperTodayView)
    const sealButton = wrapper.find('[data-action="seal"]')

    await sealButton.trigger('click')
    await sealButton.trigger('click')
    await flushPromises()

    expect(toastMocks.error).not.toHaveBeenCalled()

    resolveSeal({ sealedAt: new Date().toISOString(), wasAlreadySealed: false })
    await flushPromises()

    expect(toastMocks.success).toHaveBeenCalledTimes(1)
    expect(toastMocks.error).not.toHaveBeenCalled()
    expect(todayApi.sealDay).toHaveBeenCalledTimes(1)
  })
})
