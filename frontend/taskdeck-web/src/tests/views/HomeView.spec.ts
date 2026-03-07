import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import HomeView from '../../views/HomeView.vue'
import type { HomeSummary, WorkspaceOnboarding } from '../../types/workspace'

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
        stepId: 'capture-first-item',
        title: 'Capture one real task',
        description: 'Capture something.',
        targetSurface: 'capture',
        isComplete: false,
      },
    ],
    ...overrides,
  }
}

const mockWorkspaceStore = reactive({
  onboarding: buildOnboarding(),
  homeSummary: null as HomeSummary | null,
  homeLoading: false,
  homeError: null as string | null,
  hasHomeSummary: false,
  fetchHomeSummary: vi.fn<() => Promise<void>>(),
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

describe('HomeView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockWorkspaceStore.onboarding = buildOnboarding()
    mockWorkspaceStore.homeLoading = false
    mockWorkspaceStore.homeError = null
    mockWorkspaceStore.hasHomeSummary = false
    mockWorkspaceStore.homeSummary = {
      workspaceMode: 'guided',
      isFirstRun: true,
      onboarding: buildOnboarding(),
      workload: {
        capturesNeedingTriage: 2,
        capturesInProgress: 1,
        capturesReadyForFollowUp: 3,
        proposalsPendingReview: 1,
      },
      boards: {
        totalBoards: 2,
        recentBoardsCount: 1,
        recentBoards: [
          {
            id: 'board-1',
            name: 'Alpha Board',
            description: 'First board',
            updatedAt: new Date().toISOString(),
          },
        ],
      },
      recommendedActions: [
        {
          actionId: 'review-proposals',
          title: 'Review pending proposals',
          description: 'Check the proposed changes before they land on your board.',
          targetSurface: 'review',
          attentionCount: 1,
        },
      ],
    }
    mockWorkspaceStore.fetchHomeSummary.mockResolvedValue(undefined)
    mockWorkspaceStore.updateOnboarding.mockResolvedValue(undefined)
  })

  it('loads the home summary on mount when needed', async () => {
    mount(HomeView)
    await waitForUi()

    expect(mockWorkspaceStore.fetchHomeSummary).toHaveBeenCalledTimes(1)
  })

  it('refreshes the home summary even when cached data already exists', async () => {
    mockWorkspaceStore.hasHomeSummary = true

    mount(HomeView)
    await waitForUi()

    expect(mockWorkspaceStore.fetchHomeSummary).toHaveBeenCalledTimes(1)
  })

  it('renders setup loop, workload, and recent boards', async () => {
    const wrapper = mount(HomeView)
    await waitForUi()

    expect(wrapper.text()).toContain('Setup loop')
    expect(wrapper.text()).toContain('Create your first board')
    expect(wrapper.text()).toContain('Needs triage')
    expect(wrapper.text()).toContain('Alpha Board')
    expect(wrapper.text()).toContain('Review pending proposals')
  })

  it('renders the store error state', async () => {
    mockWorkspaceStore.homeSummary = null
    mockWorkspaceStore.homeError = 'Failed to load workspace summary'
    const wrapper = mount(HomeView)
    await waitForUi()

    expect(wrapper.text()).toContain('Failed to load workspace summary')
  })

  it('shows a recent-boards empty state when boards exist but none were active recently', async () => {
    mockWorkspaceStore.homeSummary = {
      ...mockWorkspaceStore.homeSummary!,
      boards: {
        totalBoards: 2,
        recentBoardsCount: 0,
        recentBoards: [],
      },
    }

    const wrapper = mount(HomeView)
    await waitForUi()

    expect(wrapper.text()).toContain('No recently active boards yet.')
  })

  it('navigates from hero actions, onboarding steps, actions, and board cards', async () => {
    const wrapper = mount(HomeView)
    await waitForUi()

    await wrapper.get('.td-home__hero-actions .td-btn--primary').trigger('click')
    await wrapper.get('.td-home-step:nth-of-type(2)').trigger('click')
    await wrapper.get('.td-home-action').trigger('click')
    await wrapper.get('.td-home-board').trigger('click')

    expect(routerMocks.push).toHaveBeenCalledWith('/workspace/today')
    expect(routerMocks.push).toHaveBeenCalledWith('/workspace/inbox')
    expect(routerMocks.push).toHaveBeenCalledWith('/workspace/review')
    expect(routerMocks.push).toHaveBeenCalledWith('/workspace/boards/board-1')
  })

  it('dismisses or replays onboarding from the setup card', async () => {
    const wrapper = mount(HomeView)
    await waitForUi()

    await wrapper.get('.td-home__onboarding-actions .td-btn--ghost').trigger('click')
    expect(mockWorkspaceStore.updateOnboarding).toHaveBeenCalledWith('dismiss')

    mockWorkspaceStore.homeSummary = {
      ...mockWorkspaceStore.homeSummary!,
      onboarding: buildOnboarding({ visibility: 'dismissed' }),
    }

    const dismissedWrapper = mount(HomeView)
    await waitForUi()
    await dismissedWrapper.get('.td-home__onboarding-actions .td-btn--primary').trigger('click')

    expect(mockWorkspaceStore.updateOnboarding).toHaveBeenCalledWith('replay')
  })
})
