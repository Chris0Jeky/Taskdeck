import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import TodayView from '../../views/TodayView.vue'
import type { TodaySummary, WorkspaceOnboarding } from '../../types/workspace'

const routerMocks = vi.hoisted(() => ({
  push: vi.fn(),
}))

function buildOnboarding(overrides?: Partial<WorkspaceOnboarding>): WorkspaceOnboarding {
  return {
    visibility: 'active',
    isComplete: false,
    currentStepId: 'create-first-board',
    dismissedAt: null,
    completedAt: null,
    steps: [
      {
        stepId: 'create-first-board',
        title: 'Create your first board',
        description: 'Start with a board.',
        targetSurface: 'boards',
        isComplete: false,
      },
      {
        stepId: 'review-first-proposal',
        title: 'Review your first proposal',
        description: 'Use Review before a board changes.',
        targetSurface: 'review',
        isComplete: false,
      },
    ],
    ...overrides,
  }
}

const mockWorkspaceStore = reactive({
  onboarding: buildOnboarding(),
  todaySummary: null as TodaySummary | null,
  todayLoading: false,
  todayError: null as string | null,
  hasTodaySummary: false,
  fetchTodaySummary: vi.fn<() => Promise<void>>(),
  updateOnboarding: vi.fn<(action: 'dismiss' | 'replay') => Promise<void>>(),
})

vi.mock('../../store/workspaceStore', () => ({
  useWorkspaceStore: () => mockWorkspaceStore,
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({
    push: routerMocks.push,
  }),
}))

vi.mock('../../components/workspace/WorkspaceSetupModal.vue', () => ({
  default: {
    template: '<div data-testid="workspace-setup-modal" />',
    props: ['isOpen'],
  },
}))

async function waitForUi() {
  await Promise.resolve()
  await Promise.resolve()
}

describe('TodayView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockWorkspaceStore.onboarding = buildOnboarding()
    mockWorkspaceStore.todayLoading = false
    mockWorkspaceStore.todayError = null
    mockWorkspaceStore.hasTodaySummary = false
    mockWorkspaceStore.todaySummary = {
      workspaceMode: 'guided',
      onboarding: buildOnboarding(),
      summary: {
        capturesNeedingTriage: 2,
        proposalsPendingReview: 1,
        overdueCards: 1,
        dueTodayCards: 1,
        blockedCards: 1,
      },
      overdueCards: [
        {
          boardId: 'board-1',
          boardName: 'Alpha Board',
          cardId: 'card-overdue',
          title: 'Past due',
          dueDate: new Date().toISOString(),
          blockReason: null,
          updatedAt: new Date().toISOString(),
        },
      ],
      dueTodayCards: [
        {
          boardId: 'board-2',
          boardName: 'Today Board',
          cardId: 'card-today',
          title: 'Ship today',
          dueDate: new Date().toISOString(),
          blockReason: null,
          updatedAt: new Date().toISOString(),
        },
      ],
      blockedCards: [
        {
          boardId: 'board-3',
          boardName: 'Blocked Board',
          cardId: 'card-blocked',
          title: 'Waiting on input',
          dueDate: null,
          blockReason: 'Waiting on dependency',
          updatedAt: new Date().toISOString(),
        },
      ],
      recommendedActions: [
        {
          actionId: 'review-proposals',
          title: 'Review pending proposals',
          description: 'Decide proposed changes.',
          targetSurface: 'review',
        },
      ],
    }
    mockWorkspaceStore.fetchTodaySummary.mockResolvedValue(undefined)
    mockWorkspaceStore.updateOnboarding.mockResolvedValue(undefined)
  })

  it('loads today summary on mount when needed', async () => {
    mount(TodayView)
    await waitForUi()

    expect(mockWorkspaceStore.fetchTodaySummary).toHaveBeenCalledTimes(1)
  })

  it('refreshes today summary even when cached data already exists', async () => {
    mockWorkspaceStore.hasTodaySummary = true

    mount(TodayView)
    await waitForUi()

    expect(mockWorkspaceStore.fetchTodaySummary).toHaveBeenCalledTimes(1)
  })

  it('renders onboarding, stats, and agenda sections', async () => {
    const wrapper = mount(TodayView)
    await waitForUi()

    expect(wrapper.text()).toContain('Onboarding loop')
    expect(wrapper.text()).toContain('Pending review')
    expect(wrapper.text()).toContain('Overdue cards')
    expect(wrapper.text()).toContain('1 proposal waiting in Review.')
    expect(wrapper.text()).toContain('2 captures ready for Inbox triage.')
    expect(wrapper.text()).toContain('Past due')
    expect(wrapper.text()).toContain('Review pending proposals')
  })

  it('routes from hero actions, onboarding steps, recommended actions, and agenda cards', async () => {
    const wrapper = mount(TodayView)
    await waitForUi()

    await wrapper.get('.td-today__hero-actions .td-btn--primary').trigger('click')
    await wrapper.get('.td-today-step:nth-of-type(2)').trigger('click')
    await wrapper.get('.td-today-recommendation').trigger('click')
    await wrapper.get('.td-today-item').trigger('click')

    expect(routerMocks.push).toHaveBeenCalledWith('/workspace/review')
    expect(routerMocks.push).toHaveBeenCalledWith('/workspace/review')
    expect(routerMocks.push).toHaveBeenCalledWith('/workspace/review')
    expect(routerMocks.push).toHaveBeenCalledWith('/workspace/boards/board-1')
  })

  it('dismisses or replays onboarding', async () => {
    const wrapper = mount(TodayView)
    await waitForUi()

    await wrapper.get('.td-today__onboarding-actions .td-btn--ghost').trigger('click')
    expect(mockWorkspaceStore.updateOnboarding).toHaveBeenCalledWith('dismiss')

    mockWorkspaceStore.todaySummary = {
      ...mockWorkspaceStore.todaySummary!,
      onboarding: buildOnboarding({ visibility: 'dismissed' }),
    }

    const dismissedWrapper = mount(TodayView)
    await waitForUi()
    await dismissedWrapper.get('.td-today__onboarding-actions .td-btn--primary').trigger('click')

    expect(mockWorkspaceStore.updateOnboarding).toHaveBeenCalledWith('replay')
  })
})
