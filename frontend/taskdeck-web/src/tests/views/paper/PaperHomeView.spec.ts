import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { nextTick, reactive } from 'vue'
import PaperHomeView from '../../../views/paper/PaperHomeView.vue'
import type {
  HomeSummary,
  WorkspaceOnboarding,
  WorkspaceOnboardingAction,
  WorkspaceOnboardingStep,
} from '../../../types/workspace'

/**
 * PaperHomeView — vitest coverage for greeting, queue rendering, empty
 * state, capture dispatch guard, and ember-accent rules.
 *
 * The view reads from sessionStore, workspaceStore, captureStore, and
 * vue-router; we mock all four with lightweight reactive stand-ins. The
 * greeting is time-of-day sensitive, so each test pins the system clock
 * with vi.useFakeTimers / setSystemTime.
 */

const routerMocks = vi.hoisted(() => ({
  push: vi.fn(),
}))

const mockSessionStore = reactive({
  username: 'daniel' as string | null,
})

const mockWorkspaceStore = reactive({
  homeSummary: null as HomeSummary | null,
  homeLoading: false,
  homeError: null as string | null,
  hasHomeSummary: false,
  onboarding: null as WorkspaceOnboarding | null,
  fetchHomeSummary: vi.fn<() => Promise<void>>(),
  updateOnboarding: vi.fn<(action: WorkspaceOnboardingAction) => Promise<void>>(),
})

const mockCaptureStore = {
  createItem: vi.fn(),
}

vi.mock('../../../store/sessionStore', () => ({
  useSessionStore: () => mockSessionStore,
}))

vi.mock('../../../store/workspaceStore', () => ({
  useWorkspaceStore: () => mockWorkspaceStore,
}))

vi.mock('../../../store/captureStore', () => ({
  useCaptureStore: () => mockCaptureStore,
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: routerMocks.push }),
}))

function buildSummary(overrides?: Partial<HomeSummary>): HomeSummary {
  return {
    workspaceMode: 'guided',
    isFirstRun: false,
    onboarding: {
      visibility: 'active',
      isComplete: false,
      currentStepId: null,
      dismissedAt: null,
      completedAt: null,
      steps: [],
    },
    workload: {
      capturesNeedingTriage: 0,
      capturesInProgress: 0,
      capturesReadyForFollowUp: 0,
      proposalsPendingReview: 0,
    },
    boards: {
      totalBoards: 1,
      recentBoardsCount: 0,
      recentBoards: [],
    },
    recommendedActions: [],
    ...overrides,
  }
}

/**
 * The exact steps `WorkspaceService.BuildOnboardingSteps` emits, so these tests
 * track the real capture→review→apply contract rather than invented fixtures.
 * `completedCount` ticks that many from the front, matching the server's order.
 */
function buildOnboardingSteps(completedCount: number): WorkspaceOnboardingStep[] {
  const steps: WorkspaceOnboardingStep[] = [
    {
      stepId: 'create-first-board',
      title: 'Create your first board',
      description: 'Start with a real destination so captures and proposals can land somewhere useful.',
      targetSurface: 'boards',
      isComplete: false,
    },
    {
      stepId: 'capture-first-item',
      title: 'Capture one real task',
      description: 'Drop a note, task, or follow-up into Inbox so the review loop has something to shape.',
      targetSurface: 'capture',
      isComplete: false,
    },
    {
      stepId: 'review-first-proposal',
      title: 'Review your first proposal',
      description: 'Use Review to decide what should reach a board before anything is applied.',
      targetSurface: 'review',
      isComplete: false,
    },
    {
      stepId: 'apply-first-proposal',
      title: 'Apply your first proposal',
      description: 'Approve and apply a proposal so the change reaches your board — the full capture-to-board loop.',
      targetSurface: 'board',
      isComplete: false,
    },
  ]

  return steps.map((step, index) => ({ ...step, isComplete: index < completedCount }))
}

function buildOnboarding(overrides?: Partial<WorkspaceOnboarding>): WorkspaceOnboarding {
  return {
    visibility: 'active',
    isComplete: false,
    currentStepId: null,
    dismissedAt: null,
    completedAt: null,
    steps: buildOnboardingSteps(0),
    ...overrides,
  }
}

describe('PaperHomeView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockSessionStore.username = 'daniel'
    mockWorkspaceStore.homeSummary = buildSummary()
    mockWorkspaceStore.homeLoading = false
    mockWorkspaceStore.homeError = null
    mockWorkspaceStore.hasHomeSummary = true
    mockWorkspaceStore.onboarding = null
    mockWorkspaceStore.fetchHomeSummary.mockResolvedValue(undefined)
    // Mirror the real store: `updateOnboarding` applies the requested
    // visibility optimistically to BOTH the onboarding ref and the cached home
    // summary (workspaceStore.syncOnboarding) before its request settles.
    mockWorkspaceStore.updateOnboarding.mockImplementation(async (action) => {
      const visibility = action === 'dismiss' ? 'dismissed' : 'active'
      const base = mockWorkspaceStore.homeSummary?.onboarding ?? mockWorkspaceStore.onboarding
      if (!base) return
      const next: WorkspaceOnboarding = { ...base, visibility }
      mockWorkspaceStore.onboarding = next
      if (mockWorkspaceStore.homeSummary) {
        mockWorkspaceStore.homeSummary = { ...mockWorkspaceStore.homeSummary, onboarding: next }
      }
    })
    mockCaptureStore.createItem.mockReset()
    mockCaptureStore.createItem.mockResolvedValue({ id: 'capture-1' })
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  describe('greeting period', () => {
    it.each([
      ['08:00 local', new Date(2026, 3, 25, 8, 0, 0), 'Good morning', 'morning'],
      ['12:30 local', new Date(2026, 3, 25, 12, 30, 0), 'Good afternoon', 'afternoon'],
      ['19:00 local', new Date(2026, 3, 25, 19, 0, 0), 'Good evening', 'evening'],
    ])('picks %s', (_label, fixedNow, opener, period) => {
      vi.useFakeTimers()
      vi.setSystemTime(fixedNow as Date)

      const wrapper = mount(PaperHomeView)
      const greeting = wrapper.get('[data-testid="paper-home-greeting"]')
      const eyebrow = wrapper.get('[data-testid="paper-home-period"]')

      expect(greeting.text()).toContain(opener as string)
      expect(greeting.text()).toContain('Daniel')
      expect(eyebrow.text()).toContain(period as string)
    })

    it('falls back to "Hello" when no first name is available', () => {
      vi.useFakeTimers()
      vi.setSystemTime(new Date(2026, 3, 25, 9, 0, 0))
      mockSessionStore.username = null

      const wrapper = mount(PaperHomeView)
      const greeting = wrapper.get('[data-testid="paper-home-greeting"]')

      expect(greeting.text()).toBe('Hello.')
      expect(greeting.text()).not.toContain('Good morning')
    })

    it('supports Unicode first-name tokens', () => {
      vi.useFakeTimers()
      vi.setSystemTime(new Date(2026, 3, 25, 9, 0, 0))
      mockSessionStore.username = 'élodie.smith@example.test'

      const wrapper = mount(PaperHomeView)

      expect(wrapper.get('[data-testid="paper-home-greeting"]').text()).toContain('Élodie')
    })

    it('refreshes the greeting period while the view stays active', async () => {
      vi.useFakeTimers()
      vi.setSystemTime(new Date(2026, 3, 25, 11, 59, 30))

      const wrapper = mount(PaperHomeView)
      expect(wrapper.get('[data-testid="paper-home-period"]').text()).toContain('morning')

      await vi.advanceTimersByTimeAsync(60_000)
      await nextTick()

      expect(wrapper.get('[data-testid="paper-home-period"]').text()).toContain('afternoon')
    })
  })

  describe('queue cards', () => {
    it('renders loading instead of an empty queue while summary is missing', () => {
      mockWorkspaceStore.homeSummary = null
      mockWorkspaceStore.hasHomeSummary = false
      mockWorkspaceStore.homeLoading = true

      const wrapper = mount(PaperHomeView)

      expect(wrapper.find('[data-testid="paper-home-loading"]').exists()).toBe(true)
      expect(wrapper.find('[data-testid="paper-home-empty"]').exists()).toBe(false)
      expect(wrapper.find('.paper-home__queue').exists()).toBe(false)
    })

    it('renders the store error instead of an empty queue when summary loading fails', () => {
      mockWorkspaceStore.homeSummary = null
      mockWorkspaceStore.hasHomeSummary = false
      mockWorkspaceStore.homeError = 'Failed to load workspace summary'

      const wrapper = mount(PaperHomeView)

      expect(wrapper.find('[data-testid="paper-home-error"]').text()).toContain('Failed to load workspace summary')
      expect(wrapper.find('[data-testid="paper-home-empty"]').exists()).toBe(false)
      expect(wrapper.find('.paper-home__queue').exists()).toBe(false)
    })

    it('renders refresh errors even when a cached summary exists', () => {
      mockWorkspaceStore.homeSummary = buildSummary({
        workload: {
          capturesNeedingTriage: 0,
          capturesInProgress: 0,
          capturesReadyForFollowUp: 0,
          proposalsPendingReview: 0,
        },
      })
      mockWorkspaceStore.hasHomeSummary = true
      mockWorkspaceStore.homeError = 'Could not refresh workspace summary'

      const wrapper = mount(PaperHomeView)

      expect(wrapper.find('[data-testid="paper-home-error"]').text()).toContain('Could not refresh workspace summary')
      expect(wrapper.find('[data-testid="paper-home-empty"]').exists()).toBe(false)
      expect(wrapper.get('[data-testid="paper-home-lede"]').text()).toContain('Could not refresh workspace summary')
    })

    it('renders the empty state when nothing is queued', () => {
      mockWorkspaceStore.homeSummary = buildSummary()
      const wrapper = mount(PaperHomeView)

      expect(wrapper.find('[data-testid="paper-home-empty"]').exists()).toBe(true)
      expect(wrapper.find('[data-testid="paper-home-empty"]').text()).toContain('Nothing waiting')
      expect(wrapper.text().match(/Nothing waiting\. Good\./g) ?? []).toHaveLength(1)
      expect(wrapper.find('[data-testid="paper-home-card-proposal"]').exists()).toBe(false)
      expect(wrapper.find('[data-testid="paper-home-card-carryover"]').exists()).toBe(false)
    })

    it('opens the reused setup flow when a fresh user has no boards', async () => {
      mockWorkspaceStore.homeSummary = buildSummary({
        boards: {
          totalBoards: 0,
          recentBoardsCount: 0,
          recentBoards: [],
        },
      })
      const wrapper = mount(PaperHomeView, {
        global: {
          stubs: {
            Teleport: true,
            // isOpen-aware stub: the modal is now kept mounted and toggled via the
            // is-open prop, so the stub must respect it to prove the CTA opens it.
            WorkspaceSetupModal: {
              props: ['isOpen'],
              template: '<div v-if="isOpen" data-testid="workspace-setup-modal-stub" />',
            },
          },
        },
      })

      expect(wrapper.find('[data-testid="paper-home-first-board"]').exists()).toBe(true)
      expect(wrapper.find('[data-testid="paper-home-empty"]').exists()).toBe(false)
      expect(wrapper.find('[data-testid="workspace-setup-modal-stub"]').exists()).toBe(false)
      await wrapper.get('[data-testid="paper-home-setup-cta"]').trigger('click')

      expect(wrapper.find('[data-testid="workspace-setup-modal-stub"]').exists()).toBe(true)
    })

    it('shows the local first-loop milestones from the real backend payload', () => {
      mockWorkspaceStore.homeSummary = buildSummary({
        onboarding: buildOnboarding({
          currentStepId: 'review-first-proposal',
          steps: buildOnboardingSteps(2),
        }),
      })

      const wrapper = mount(PaperHomeView)

      expect(wrapper.get('[data-testid="paper-home-milestones"]').text()).toContain('2/4 complete')
      expect(wrapper.findAll('.paper-home__milestone')).toHaveLength(4)
      expect(wrapper.findAll('.paper-home__milestone--complete')).toHaveLength(2)
      expect(wrapper.text()).toContain('Apply your first proposal')
      expect(wrapper.text()).toContain('not sent as analytics')
    })

    it('only marks proposal entries with the ember halo, not carry-overs', () => {
      mockWorkspaceStore.homeSummary = buildSummary({
        workload: {
          capturesNeedingTriage: 2,
          capturesInProgress: 0,
          capturesReadyForFollowUp: 0,
          proposalsPendingReview: 1,
        },
        recommendedActions: [
          {
            actionId: 'review-proposals',
            title: 'Review pending proposals',
            description: 'One awaits decision.',
            targetSurface: 'review',
            attentionCount: 1,
          },
        ],
      })

      const wrapper = mount(PaperHomeView)
      const proposalCards = wrapper.findAll('[data-testid="paper-home-card-proposal"]')
      const carryoverCards = wrapper.findAll('[data-testid="paper-home-card-carryover"]')

      expect(proposalCards).toHaveLength(1)
      expect(carryoverCards.length).toBeGreaterThan(0)

      // Proposals carry the ember halo; carry-overs do not.
      expect(proposalCards[0].classes()).toContain('halo-ember')
      carryoverCards.forEach((card) => {
        expect(card.classes()).not.toContain('halo-ember')
      })
    })
  })

  /**
   * Issue #1936 — Home read as a one-time tutorial because the first-loop block
   * stayed the largest element on the page at 4/4 complete, with no way to make
   * it go away.
   *
   * The pinned contract: the block is prominent onboarding while it is
   * unfinished, recedes to a single line the moment it is finished, and stays
   * gone when the user dismisses it — via the SERVER-persisted workspace
   * onboarding visibility, not a component-local flag.
   */
  describe('completed milestones recede (#1936)', () => {
    function mountWithMilestones(onboarding: WorkspaceOnboarding) {
      mockWorkspaceStore.homeSummary = buildSummary({ onboarding })
      mockWorkspaceStore.onboarding = onboarding
      return mount(PaperHomeView)
    }

    it('keeps an unfinished block whole, prominent, and undismissable', () => {
      const wrapper = mountWithMilestones(
        buildOnboarding({ currentStepId: 'apply-first-proposal', steps: buildOnboardingSteps(3) }),
      )

      const section = wrapper.get('[data-testid="paper-home-milestones"]')
      expect(section.attributes('data-milestones-state')).toBe('expanded')
      expect(wrapper.findAll('.paper-home__milestone')).toHaveLength(4)
      expect(section.text()).toContain('3/4 complete')
      expect(section.text()).toContain('From thought to trusted action')
      // No escape hatches while the loop is still real onboarding.
      expect(wrapper.find('[data-testid="paper-home-milestones-toggle"]').exists()).toBe(false)
      expect(wrapper.find('[data-testid="paper-home-milestones-dismiss"]').exists()).toBe(false)
    })

    it('auto-collapses to a single line once the server marks the loop complete', () => {
      const wrapper = mountWithMilestones(
        buildOnboarding({
          isComplete: true,
          completedAt: '2026-08-22T10:00:00Z',
          steps: buildOnboardingSteps(4),
        }),
      )

      const section = wrapper.get('[data-testid="paper-home-milestones"]')
      expect(section.attributes('data-milestones-state')).toBe('collapsed')
      expect(section.classes()).toContain('paper-home__milestones--collapsed')
      // The body — the part that made this the biggest element on the page — is
      // gone from the DOM, not merely hidden.
      expect(wrapper.find('#paper-home-milestones-body').exists()).toBe(false)
      expect(wrapper.findAll('.paper-home__milestone')).toHaveLength(0)
      expect(wrapper.text()).not.toContain('Apply your first proposal')
      // What survives: the receipt, and the controls.
      expect(section.text()).toContain('Your first loop is complete')
      expect(section.text()).toContain('4/4 complete')
      const toggle = wrapper.get('[data-testid="paper-home-milestones-toggle"]')
      expect(toggle.attributes('aria-expanded')).toBe('false')
      expect(toggle.attributes('aria-controls')).toBe('paper-home-milestones-body')
      expect(wrapper.find('[data-testid="paper-home-milestones-dismiss"]').exists()).toBe(true)
    })

    it('collapses on every step being ticked even if the isComplete flag lags', () => {
      const wrapper = mountWithMilestones(
        buildOnboarding({ isComplete: false, steps: buildOnboardingSteps(4) }),
      )

      expect(
        wrapper.get('[data-testid="paper-home-milestones"]').attributes('data-milestones-state'),
      ).toBe('collapsed')
    })

    it('re-expands on demand and collapses again', async () => {
      const wrapper = mountWithMilestones(
        buildOnboarding({ isComplete: true, steps: buildOnboardingSteps(4) }),
      )

      await wrapper.get('[data-testid="paper-home-milestones-toggle"]').trigger('click')

      expect(wrapper.find('#paper-home-milestones-body').exists()).toBe(true)
      expect(wrapper.findAll('.paper-home__milestone')).toHaveLength(4)
      // The honest-framing footnote (#1936 asked for it to be preserved).
      expect(wrapper.text()).toContain('not sent as analytics')
      expect(
        wrapper.get('[data-testid="paper-home-milestones-toggle"]').attributes('aria-expanded'),
      ).toBe('true')

      await wrapper.get('[data-testid="paper-home-milestones-toggle"]').trigger('click')

      expect(wrapper.find('#paper-home-milestones-body').exists()).toBe(false)
    })

    it('dismisses through the persisted workspace onboarding preference', async () => {
      const wrapper = mountWithMilestones(
        buildOnboarding({ isComplete: true, steps: buildOnboardingSteps(4) }),
      )

      await wrapper.get('[data-testid="paper-home-milestones-dismiss"]').trigger('click')
      await nextTick()

      // The persistence mechanism is the existing server-backed workspace
      // preference write, not localStorage and not component state.
      expect(mockWorkspaceStore.updateOnboarding).toHaveBeenCalledWith('dismiss')
      expect(wrapper.find('[data-testid="paper-home-milestones"]').exists()).toBe(false)
    })

    it('stays dismissed across a remount', async () => {
      const wrapper = mountWithMilestones(
        buildOnboarding({ isComplete: true, steps: buildOnboardingSteps(4) }),
      )
      await wrapper.get('[data-testid="paper-home-milestones-dismiss"]').trigger('click')
      await nextTick()
      wrapper.unmount()

      // Fresh mount, same store state the dismissal left behind: nothing
      // component-local is carrying the decision.
      const remounted = mount(PaperHomeView)

      expect(remounted.find('[data-testid="paper-home-milestones"]').exists()).toBe(false)
      expect(mockWorkspaceStore.updateOnboarding).toHaveBeenCalledTimes(1)
    })

    it('honours a dismissal made on another surface', () => {
      // Legacy Home and Today already write this visibility; Paper used to
      // ignore it and render the block anyway.
      const wrapper = mountWithMilestones(
        buildOnboarding({ visibility: 'dismissed', steps: buildOnboardingSteps(2) }),
      )

      expect(wrapper.find('[data-testid="paper-home-milestones"]').exists()).toBe(false)
    })

    it('keeps the optimistic dismissal when the preference write fails', async () => {
      // The real store applies the intent locally, flags it unsaved and raises
      // its own warning toast before rejecting — the view must not re-throw.
      //
      // The DOM alone cannot witness that: the optimistic write has already
      // removed the block, and Vue diverts a rejected async click handler to
      // the app error handler rather than failing the test. So watch the
      // channel that DOES change when the view's catch goes away.
      const onboarding = buildOnboarding({ isComplete: true, steps: buildOnboardingSteps(4) })
      mockWorkspaceStore.homeSummary = buildSummary({ onboarding })
      mockWorkspaceStore.onboarding = onboarding

      const appError = vi.fn()
      const wrapper = mount(PaperHomeView, {
        global: { config: { errorHandler: appError } },
      })

      mockWorkspaceStore.updateOnboarding.mockImplementationOnce(async () => {
        const base = mockWorkspaceStore.homeSummary!.onboarding
        mockWorkspaceStore.homeSummary = {
          ...mockWorkspaceStore.homeSummary!,
          onboarding: { ...base, visibility: 'dismissed' },
        }
        throw new Error('network down')
      })

      await wrapper.get('[data-testid="paper-home-milestones-dismiss"]').trigger('click')
      await flushPromises()

      // Swallowed, not re-thrown: a rejection escaping dismissMilestones lands
      // here instead.
      expect(appError).not.toHaveBeenCalled()
      expect(mockWorkspaceStore.updateOnboarding).toHaveBeenCalledWith('dismiss')
      // And the optimistic dismissal survives the failed write.
      expect(wrapper.find('[data-testid="paper-home-milestones"]').exists()).toBe(false)
    })
  })

  /**
   * Issue #1768 — Home reported "1 carry-over from yesterday" seconds after the
   * very first same-day capture on a fresh account.
   *
   * Root cause: `workload.capturesNeedingTriage` is a pure STATUS count
   * (`NewCount + FailedCount` in WorkspaceService.GetHomeAsync) with no date
   * predicate in the chain. The view alone authored the "from yesterday" claim,
   * so a capture saved seconds ago was mislabelled — in every timezone.
   *
   * The pinned contract: Home's workload copy is date-neutral. It must not vary
   * with the local clock and must never assert a day-relative origin.
   */
  describe('day-boundary copy (#1768)', () => {
    // `vi.stubEnv` (restored by unstubAllEnvs) rather than touching `process.env`
    // directly: this tsconfig project has no node types, and CI type-checks specs.
    afterEach(() => {
      vi.unstubAllEnvs()
    })

    function ledeAt(systemTime: Date, tz: string, capturesNeedingTriage: number): string {
      vi.stubEnv('TZ', tz)
      vi.useFakeTimers()
      vi.setSystemTime(systemTime)
      mockWorkspaceStore.homeSummary = buildSummary({
        workload: {
          capturesNeedingTriage,
          capturesInProgress: 0,
          capturesReadyForFollowUp: 0,
          proposalsPendingReview: 0,
        },
      })
      const wrapper = mount(PaperHomeView)
      const text = wrapper.get('[data-testid="paper-home-lede"]').text()
      wrapper.unmount()
      vi.useRealTimers()
      return text
    }

    it('never calls a same-day capture a carry-over from yesterday', () => {
      // The live repro: fresh account, first capture saved at 02:43 local, read
      // back seconds later. Before the fix this rendered "1 carry-over from yesterday".
      const lede = ledeAt(new Date(2026, 7, 19, 2, 43, 12), 'UTC', 1)

      expect(lede).toBe('1 awaiting triage')
      expect(lede.toLowerCase()).not.toContain('yesterday')
      expect(lede.toLowerCase()).not.toContain('carry-over')
    })

    /**
     * Each row straddles a boundary: instants seconds either side of local
     * midnight, and offsets where the UTC calendar day differs from the local
     * one in both directions (Kiritimati is UTC+14, Midway UTC-11).
     *
     * `utcDayShift` is asserted first so the timezone dimension is load-bearing:
     * it proves the runtime really adopted the offset. Without it a runtime that
     * ignored the TZ stub would still pass every copy assertion below and the
     * row would be decorative.
     */
    // Wall-clock parts, not Date objects: a `new Date(...)` in this table would be
    // constructed at collection time under the ambient zone, before the TZ swap.
    it.each<[string, [number, number, number, number, number, number], string, number]>([
      ['one second before local midnight, UTC', [2026, 7, 19, 23, 59, 59], 'UTC', 0],
      ['one second after local midnight, UTC', [2026, 7, 20, 0, 0, 1], 'UTC', 0],
      ['UTC+14 — UTC is still on the previous day', [2026, 7, 19, 12, 0, 0], 'Pacific/Kiritimati', -1],
      ['UTC-11 — UTC has already rolled to the next day', [2026, 7, 19, 20, 0, 0], 'Pacific/Midway', 1],
      ['UTC+5:30 — half-hour offset just past local midnight', [2026, 7, 20, 0, 15, 0], 'Asia/Kolkata', -1],
      ['DST-observing zone at the local boundary', [2026, 7, 19, 23, 59, 59], 'America/New_York', 1],
    ])('renders identical date-neutral copy: %s', (_label, parts, tz, utcDayShift) => {
      vi.stubEnv('TZ', tz)
      const local = new Date(parts[0], parts[1], parts[2], parts[3], parts[4], parts[5])
      expect(local.getUTCDate() - local.getDate()).toBe(utcDayShift)

      const lede = ledeAt(local, tz, 2)

      expect(lede).toBe('2 awaiting triage')
      expect(lede.toLowerCase()).not.toContain('yesterday')
    })

    it('keeps the queue card title date-neutral too', () => {
      vi.stubEnv('TZ', 'Pacific/Kiritimati')
      vi.useFakeTimers()
      vi.setSystemTime(new Date(2026, 7, 19, 2, 43, 12))
      mockWorkspaceStore.homeSummary = buildSummary({
        workload: {
          capturesNeedingTriage: 1,
          capturesInProgress: 0,
          capturesReadyForFollowUp: 0,
          proposalsPendingReview: 0,
        },
      })

      const wrapper = mount(PaperHomeView)
      const card = wrapper.get('[data-testid="paper-home-card-carryover"]')

      expect(card.text()).toContain('Triage 1 capture')
      expect(card.text().toLowerCase()).not.toContain('yesterday')
      expect(card.text().toUpperCase()).not.toContain('CARRY-OVER')
    })

    it('still reports nothing waiting when the workload is empty', () => {
      vi.stubEnv('TZ', 'Pacific/Midway')
      vi.useFakeTimers()
      vi.setSystemTime(new Date(2026, 7, 20, 0, 0, 1))
      mockWorkspaceStore.homeSummary = buildSummary({
        workload: {
          capturesNeedingTriage: 0,
          capturesInProgress: 0,
          capturesReadyForFollowUp: 0,
          proposalsPendingReview: 0,
        },
      })

      const wrapper = mount(PaperHomeView)

      // #1734: the empty state owns the message, so the lede is suppressed
      // rather than saying it a second time. The copy stays date-neutral (#1768).
      expect(wrapper.find('[data-testid="paper-home-lede"]').exists()).toBe(false)
      expect(wrapper.get('[data-testid="paper-home-empty"]').text()).toContain(
        'Nothing waiting. Good.',
      )

      wrapper.unmount()
      vi.useRealTimers()
    })
  })

  describe('quick capture', () => {
    it('cleans up the global capture shortcut listener on unmount', () => {
      const addSpy = vi.spyOn(window, 'addEventListener')
      const removeSpy = vi.spyOn(window, 'removeEventListener')

      const wrapper = mount(PaperHomeView)
      wrapper.unmount()

      expect(addSpy).toHaveBeenCalledWith('keydown', expect.any(Function))
      expect(removeSpy).toHaveBeenCalledWith('keydown', expect.any(Function))
    })

    it('does not dispatch on empty Enter', async () => {
      const wrapper = mount(PaperHomeView)
      const input = wrapper.get('[data-testid="paper-home-capture-input"]')

      await input.setValue('   ')
      await wrapper.get('form').trigger('submit.prevent')

      expect(mockCaptureStore.createItem).not.toHaveBeenCalled()
    })

    it('dispatches a typed capture and refreshes the home summary on submit', async () => {
      const wrapper = mount(PaperHomeView)
      const input = wrapper.get('[data-testid="paper-home-capture-input"]')

      await input.setValue('Refactor the queue store')
      await wrapper.get('form').trigger('submit.prevent')
      await Promise.resolve()

      expect(mockCaptureStore.createItem).toHaveBeenCalledTimes(1)
      expect(mockCaptureStore.createItem).toHaveBeenCalledWith({
        boardId: null,
        text: 'Refactor the queue store',
        source: 'Typed',
      })
      expect(mockWorkspaceStore.fetchHomeSummary).toHaveBeenCalledTimes(1)
    })
  })
})
