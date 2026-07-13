import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { todayApi } from '../../../../api/todayApi'
import { formatLocalDossierDate } from '../../../../composables/useTodayDossier'
import type { TodaySummary } from '../../../../types/workspace'
import PaperTodayView from '../../../../views/paper/PaperTodayView.vue'

const toastMocks = vi.hoisted(() => ({
  success: vi.fn(),
  error: vi.fn(),
  info: vi.fn(),
}))

const mockWorkspaceStore = {
  todaySummary: null as TodaySummary | null,
  todayLoading: false,
  todayError: null as string | null,
  fetchTodaySummary: vi.fn(),
  clearTodaySummary: vi.fn(),
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

function makeTodaySummary(overdueCards = 1): TodaySummary {
  return {
    workspaceMode: 'guided',
    onboarding: {
      visibility: 'active',
      isComplete: false,
      currentStepId: null,
      dismissedAt: null,
      completedAt: null,
      steps: [],
    },
    summary: {
      capturesNeedingTriage: 2,
      proposalsPendingReview: 3,
      overdueCards,
      dueTodayCards: 4,
      blockedCards: 1,
    },
    overdueCards: [{
      boardId: 'board-1',
      boardName: 'Client onboarding',
      cardId: 'card-123456789',
      title: 'Confirm engagement letter',
      dueDate: '2026-01-14',
      blockReason: null,
      updatedAt: '2026-01-15T09:00:00Z',
    }],
    dueTodayCards: [],
    blockedCards: [],
    recommendedActions: [],
  }
}

describe('PaperTodayView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    localStorage.clear()
    mockSessionStore.userId = 'user-1'
    mockWorkspaceStore.todaySummary = null
    mockWorkspaceStore.todayLoading = false
    mockWorkspaceStore.todayError = null
    mockWorkspaceStore.fetchTodaySummary.mockImplementation(async () => {
      if (!mockWorkspaceStore.todaySummary) {
        mockWorkspaceStore.todaySummary = makeTodaySummary()
      }
      return mockWorkspaceStore.todaySummary
    })
    mockWorkspaceStore.clearTodaySummary.mockImplementation(() => {
      mockWorkspaceStore.todaySummary = null
    })
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
    expect(text).toContain('Live ledger unavailable')
    expect(text).toContain('Live carry-over unavailable')
    expect(text).not.toContain('0 entries')
    expect(text).not.toContain('A quiet Saturday')
    expect(text).not.toContain('haiku')
    expect(text).not.toContain('Sprint 12')
    expect(text).not.toContain('2h 14m')
    expect(text).not.toContain('C-072')
    expect(wrapper.find('[data-action="pin-tomorrow"]').exists()).toBe(false)
  })

  it('announces initial summary loading without presenting unavailable totals', () => {
    mockWorkspaceStore.todayLoading = true

    const wrapper = mount(PaperTodayView)
    const status = wrapper.get('[data-testid="paper-today-loading"]')

    expect(status.attributes('role')).toBe('status')
    expect(status.attributes('aria-live')).toBe('polite')
    expect(status.text()).toContain('Loading today’s dossier')
    expect(wrapper.find('[data-section="cover"]').exists()).toBe(false)
    expect(wrapper.attributes('aria-busy')).toBe('true')
  })

  it('surfaces an initial summary failure with an accessible retry', async () => {
    mockWorkspaceStore.todayError = 'Request failed with status code 500'

    const wrapper = mount(PaperTodayView)
    const error = wrapper.get('[data-testid="paper-today-error"]')

    expect(error.attributes('role')).toBe('alert')
    expect(error.text()).toContain('live summary could not be loaded')
    expect(error.text()).toContain('Request failed with status code 500')
    expect(wrapper.find('[data-section="cover"]').exists()).toBe(false)

    await error.get('button').trigger('click')
    expect(mockWorkspaceStore.fetchTodaySummary).toHaveBeenCalledTimes(1)
  })

  it('labels cached summary data as stale after a failed refresh and offers retry', async () => {
    mockWorkspaceStore.todaySummary = makeTodaySummary()
    mockWorkspaceStore.todayError = 'Network unavailable'

    const wrapper = mount(PaperTodayView)
    const stale = wrapper.get('[data-testid="paper-today-stale"]')

    expect(stale.attributes('role')).toBe('alert')
    expect(stale.text()).toContain('Showing previously loaded data, which may be stale')
    expect(wrapper.find('[data-section="cover"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('2 captures need triage')

    await stale.get('button').trigger('click')
    expect(mockWorkspaceStore.fetchTodaySummary).toHaveBeenCalledTimes(1)
  })

  it('announces refresh progress while retaining a previously loaded summary', () => {
    mockWorkspaceStore.todaySummary = makeTodaySummary()
    mockWorkspaceStore.todayLoading = true

    const wrapper = mount(PaperTodayView)
    const status = wrapper.get('[data-testid="paper-today-refreshing"]')

    expect(status.attributes('role')).toBe('status')
    expect(status.text()).toContain('Previously loaded data remains visible')
    expect(wrapper.find('[data-section="cover"]').exists()).toBe(true)
  })

  it('discloses when the live carry-over list is capped below the total', () => {
    mockWorkspaceStore.todaySummary = makeTodaySummary(7)

    const wrapper = mount(PaperTodayView)

    expect(wrapper.text()).toContain('Showing 1 of 7 live overdue cards')
    expect(wrapper.text()).toContain('7 cards are overdue')
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
