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
    expect(text).toContain('no events are being invented')
    expect(text).toContain('Not recorded yet')
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

  it('surfaces an initial summary failure with an accessible retry and honest fallback dossier', async () => {
    mockWorkspaceStore.todayError = 'Request failed with status code 500'

    const wrapper = mount(PaperTodayView)
    const error = wrapper.get('[data-testid="paper-today-error"]')

    expect(error.attributes('role')).toBe('alert')
    expect(error.text()).toContain('live summary could not be loaded')
    expect(error.text()).toContain('Request failed with status code 500')
    expect(wrapper.find('[data-section="cover"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('Activity totals are unavailable')
    expect(wrapper.text()).toContain('independent dossier sections remain available')

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

    await wrapper.find('[data-action="seal"]').trigger('click')
    const confirmButton = wrapper.find('[data-action="seal-confirm"]')
    await confirmButton.trigger('click')
    await confirmButton.trigger('click')
    await flushPromises()

    expect(toastMocks.error).not.toHaveBeenCalled()

    resolveSeal({ sealedAt: new Date().toISOString(), wasAlreadySealed: false })
    await flushPromises()

    expect(toastMocks.success).toHaveBeenCalledTimes(1)
    expect(toastMocks.error).not.toHaveBeenCalled()
    expect(todayApi.sealDay).toHaveBeenCalledTimes(1)
  })

  // --- issue 1939: seal state machine + empty-state legibility -------------

  it('never seals on the first click — the irreversible step is behind a confirm', async () => {
    const wrapper = mount(PaperTodayView)

    expect(wrapper.find('[data-testid="seal-confirm"]').exists()).toBe(false)

    await wrapper.find('[data-action="seal"]').trigger('click')
    await flushPromises()

    expect(todayApi.sealDay).not.toHaveBeenCalled()
    const confirm = wrapper.get('[data-testid="seal-confirm"]')
    expect(confirm.text()).toContain('This cannot be undone')
    expect(confirm.text()).toContain('Taskdeck has no unseal action')
  })

  it('cancelling the confirm leaves the day open and seals nothing', async () => {
    const wrapper = mount(PaperTodayView)

    await wrapper.find('[data-action="seal"]').trigger('click')
    await wrapper.find('[data-action="seal-cancel"]').trigger('click')
    await flushPromises()

    expect(todayApi.sealDay).not.toHaveBeenCalled()
    expect(wrapper.find('[data-testid="seal-confirm"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="seal-sealed-reason"]').exists()).toBe(false)
    expect(wrapper.find('[data-action="seal"]').attributes('disabled')).toBeUndefined()
  })

  it('drives unsealed → confirming → sealed, ending disabled-with-reason', async () => {
    const wrapper = mount(PaperTodayView)

    // unsealed: actionable
    expect(wrapper.find('[data-action="seal"]').attributes('disabled')).toBeUndefined()

    // confirming
    await wrapper.find('[data-action="seal"]').trigger('click')
    expect(wrapper.find('[data-testid="seal-confirm"]').exists()).toBe(true)

    // sealed
    await wrapper.find('[data-action="seal-confirm"]').trigger('click')
    await flushPromises()

    expect(todayApi.sealDay).toHaveBeenCalledTimes(1)
    expect(wrapper.find('[data-testid="seal-confirm"]').exists()).toBe(false)

    // The regression this issue was filed for: a sealed CTA that stayed
    // clickable and only answered "Day is already sealed." The `disabled`
    // assertion below is the one that carries that — it is what fails if the
    // terminal state stops looking terminal.
    const sealButton = wrapper.get('[data-action="seal"]')
    expect(sealButton.text()).toContain('Day sealed')
    expect(sealButton.attributes('disabled')).toBeDefined()
    expect(wrapper.get('[data-testid="seal-sealed-reason"]').text()).toContain('no unseal action')

    // Corroboration, not coverage: with the CTA disabled these two cannot
    // change, so they document the contract rather than test it.
    await sealButton.trigger('click')
    await flushPromises()
    expect(todayApi.sealDay).toHaveBeenCalledTimes(1)
    expect(toastMocks.info).not.toHaveBeenCalled()
  })

  it('renders an already-sealed day as disabled-with-reason on first paint', async () => {
    // Returning to a day sealed earlier (or on another device): the seal state
    // arrives from `getSealStatus`, not from a click in this session, and the
    // terminal rendering has to hold on that path too. `mockResolvedValueOnce`
    // because `vi.clearAllMocks()` keeps implementations — a persistent one
    // would leak a sealed day into every later spec in this file.
    vi.mocked(todayApi.getSealStatus).mockResolvedValueOnce({
      date: formatLocalDossierDate(new Date()),
      isSealed: true,
      sealedAt: '2026-04-25T18:30:00Z',
    })
    const wrapper = mount(PaperTodayView)
    await flushPromises()

    const sealButton = wrapper.get('[data-action="seal"]')
    expect(sealButton.text()).toContain('Day sealed')
    expect(sealButton.attributes('disabled')).toBeDefined()
    expect(wrapper.get('[data-testid="seal-sealed-reason"]').text()).toContain('no unseal action')

    // There is nothing left to confirm, so the confirm step must be unreachable.
    await sealButton.trigger('click')
    await flushPromises()
    expect(wrapper.find('[data-testid="seal-confirm"]').exists()).toBe(false)
    expect(todayApi.sealDay).not.toHaveBeenCalled()
  })


  it('promises no archiving in the seal success toast — a seal only stamps the day', async () => {
    const wrapper = mount(PaperTodayView)

    await wrapper.find('[data-action="seal"]').trigger('click')
    await wrapper.find('[data-action="seal-confirm"]').trigger('click')
    await flushPromises()

    const message = toastMocks.success.mock.calls[0]?.[0] as string
    expect(message).toContain('cannot be unsealed')
    expect(message).not.toContain('archived')
  })

  it('keeps the seal control usable after a failed seal', async () => {
    vi.mocked(todayApi.sealDay).mockRejectedValueOnce(new Error('offline'))
    const wrapper = mount(PaperTodayView)

    await wrapper.find('[data-action="seal"]').trigger('click')
    await wrapper.find('[data-action="seal-confirm"]').trigger('click')
    await flushPromises()

    expect(toastMocks.error).toHaveBeenCalledTimes(1)
    expect(toastMocks.success).not.toHaveBeenCalled()
    // Still confirming, still retryable — a failure must not fake a sealed day.
    expect(wrapper.find('[data-testid="seal-confirm"]').exists()).toBe(true)
    expect(wrapper.find('[data-action="seal-confirm"]').attributes('disabled')).toBeUndefined()
    expect(wrapper.find('[data-testid="seal-sealed-reason"]').exists()).toBe(false)
  })

  it('closes an open confirm prompt when the local day rolls over', async () => {
    // The prompt names the day it was opened on. `useTodayDossier` resets its
    // own seal state across midnight; if the view-local prompt survived, the
    // confirm click would POST the NEW day's date and irreversibly seal a day
    // the warning never described.
    vi.useFakeTimers()
    vi.setSystemTime(new Date(2026, 3, 25, 23, 59, 59))
    const wrapper = mount(PaperTodayView)

    await wrapper.find('[data-action="seal"]').trigger('click')
    expect(wrapper.find('[data-testid="seal-confirm"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="dossier-serial"]').text()).toContain('2026-04-25')

    await vi.advanceTimersByTimeAsync(1_000)
    await wrapper.vm.$nextTick()

    expect(wrapper.find('[data-testid="dossier-serial"]').text()).toContain('2026-04-26')
    expect(wrapper.find('[data-testid="seal-confirm"]').exists()).toBe(false)
    expect(todayApi.sealDay).not.toHaveBeenCalled()
  })

  it('hands focus back to the seal control on cancel, and to the sealed reason on success', async () => {
    // Every exit from the prompt destroys the element holding focus. Without an
    // explicit hand-off the caret lands on <body> and a keyboard user is parked
    // at the top of the document.
    const wrapper = mount(PaperTodayView, { attachTo: document.body })

    await wrapper.find('[data-action="seal"]').trigger('click')
    await flushPromises()
    expect(document.activeElement).toBe(wrapper.get('[data-action="seal-confirm"]').element)

    await wrapper.find('[data-action="seal-cancel"]').trigger('click')
    await flushPromises()
    expect(document.activeElement).toBe(wrapper.get('[data-action="seal"]').element)

    await wrapper.find('[data-action="seal"]').trigger('click')
    await wrapper.find('[data-action="seal-confirm"]').trigger('click')
    await flushPromises()

    // Sealed is terminal: focus goes to the role="status" that explains why the
    // CTA it came from is now disabled.
    const reason = wrapper.get('[data-testid="seal-sealed-reason"]')
    expect(reason.attributes('tabindex')).toBe('-1')
    expect(document.activeElement).toBe(reason.element)

    wrapper.unmount()
  })

  it('returns focus to the confirm CTA when a failed seal re-enables it', async () => {
    let rejectSeal!: (error: unknown) => void
    vi.mocked(todayApi.sealDay).mockImplementationOnce(
      () => new Promise((_resolve, reject) => {
        rejectSeal = reject
      }),
    )
    const wrapper = mount(PaperTodayView, { attachTo: document.body })

    await wrapper.find('[data-action="seal"]').trigger('click')
    await wrapper.find('[data-action="seal-confirm"]').trigger('click')
    await flushPromises()

    // A real browser drops focus off an element the moment it becomes
    // disabled; happy-dom leaves it there, so move it away explicitly —
    // otherwise this spec would pass on focus that never actually left.
    const elsewhere = wrapper.get('[data-action="note"]').element as HTMLElement
    elsewhere.focus()
    expect(document.activeElement).toBe(elsewhere)

    rejectSeal(new Error('offline'))
    await flushPromises()

    expect(toastMocks.error).toHaveBeenCalledTimes(1)
    expect(wrapper.find('[data-action="seal-confirm"]').attributes('disabled')).toBeUndefined()
    expect(document.activeElement).toBe(wrapper.get('[data-action="seal-confirm"]').element)

    wrapper.unmount()
  })

  it('separates "not built yet" panels from panels whose live data did not load', () => {
    const wrapper = mount(PaperTodayView)

    // No query exists behind these three — say so plainly, and tag it.
    for (const section of ['ledger', 'decisions', 'boards']) {
      const panel = wrapper.get(`[data-empty-state="${section}"]`)
      expect(panel.find('[data-not-built]').text()).toBe('Not built yet')
      expect(panel.text()).toContain('Taskdeck does not record')
      expect(panel.text()).not.toContain('not available yet')
    }

    // These do have a live query; it failed. Different sentence, no tag.
    for (const section of ['cadence', 'streak']) {
      const panel = wrapper.get(`[data-empty-state="${section}"]`)
      expect(panel.text()).toContain('could not be loaded')
      expect(panel.text()).toContain('rather than a missing feature')
      expect(panel.find('[data-not-built]').exists()).toBe(false)
    }
  })

  it('points each unbuilt panel at the surface that does hold the truth', () => {
    const wrapper = mount(PaperTodayView)

    expect(wrapper.get('[data-empty-state="ledger"]').text()).toContain('Inbox and Review')
    expect(wrapper.get('[data-empty-state="decisions"]').text()).toContain('Open Review')
    expect(wrapper.get('[data-empty-state="boards"]').text()).toContain('Open Boards')
  })

  it('sends "Write a note" to the field it actually writes to', async () => {
    const wrapper = mount(PaperTodayView, { attachTo: document.body })
    const input = wrapper.get('[data-testid="line-for-tomorrow-input"]')

    expect(document.activeElement).not.toBe(input.element)
    await wrapper.find('[data-action="note"]').trigger('click')

    expect(document.activeElement).toBe(input.element)
    // The old behaviour was a toast promising a briefing that never arrives.
    expect(toastMocks.info).not.toHaveBeenCalled()

    wrapper.unmount()
  })

  it('describes the note lifecycle as same-day, matching what the API does', () => {
    const wrapper = mount(PaperTodayView)

    expect(wrapper.get('[data-testid="line-for-tomorrow-lifecycle"]').text())
      .toBe('saved with today’s date')
    expect(wrapper.text()).not.toContain('tomorrow-self')
    expect(wrapper.text()).not.toContain('morning briefing')
  })
})
