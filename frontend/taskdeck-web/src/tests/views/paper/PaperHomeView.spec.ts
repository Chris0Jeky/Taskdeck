import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
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
      totalBoards: 0,
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
  })

  describe('queue cards', () => {
    it('renders the empty state when nothing is queued', () => {
      mockWorkspaceStore.homeSummary = buildSummary()
      const wrapper = mount(PaperHomeView)

      expect(wrapper.find('[data-testid="paper-home-empty"]').exists()).toBe(true)
      expect(wrapper.find('[data-testid="paper-home-empty"]').text()).toContain('Nothing waiting')
      expect(wrapper.find('[data-testid="paper-home-card-proposal"]').exists()).toBe(false)
      expect(wrapper.find('[data-testid="paper-home-card-carryover"]').exists()).toBe(false)
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
    it('does not dispatch on empty Enter', async () => {
      const wrapper = mount(PaperHomeView)
      const input = wrapper.get('[data-testid="paper-home-capture-input"]')

      await input.setValue('   ')
      await wrapper.get('form').trigger('submit.prevent')

      expect(mockCaptureStore.createItem).not.toHaveBeenCalled()
    })

    it('dispatches a typed capture on submit', async () => {
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
    })
  })
})
