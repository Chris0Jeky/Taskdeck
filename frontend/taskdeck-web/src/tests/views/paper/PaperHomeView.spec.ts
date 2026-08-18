import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick, reactive } from 'vue'
import PaperHomeView from '../../../views/paper/PaperHomeView.vue'
import type { HomeSummary } from '../../../types/workspace'

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
  fetchHomeSummary: vi.fn<() => Promise<void>>(),
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

describe('PaperHomeView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockSessionStore.username = 'daniel'
    mockWorkspaceStore.homeSummary = buildSummary()
    mockWorkspaceStore.homeLoading = false
    mockWorkspaceStore.homeError = null
    mockWorkspaceStore.hasHomeSummary = true
    mockWorkspaceStore.fetchHomeSummary.mockResolvedValue(undefined)
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
      // Mirror the exact steps WorkspaceService.BuildOnboardingSteps emits so the test
      // tracks the real capture→review→apply contract rather than invented fixtures.
      mockWorkspaceStore.homeSummary = buildSummary({
        onboarding: {
          visibility: 'active',
          isComplete: false,
          currentStepId: 'review-first-proposal',
          dismissedAt: null,
          completedAt: null,
          steps: [
            {
              stepId: 'create-first-board',
              title: 'Create your first board',
              description: 'Start with a real destination so captures and proposals can land somewhere useful.',
              targetSurface: 'boards',
              isComplete: true,
            },
            {
              stepId: 'capture-first-item',
              title: 'Capture one real task',
              description: 'Drop a note, task, or follow-up into Inbox so the review loop has something to shape.',
              targetSurface: 'capture',
              isComplete: true,
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
          ],
        },
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
