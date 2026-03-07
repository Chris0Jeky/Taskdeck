import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import HomeView from '../../views/HomeView.vue'
import type { HomeSummary } from '../../types/workspace'

const routerMocks = vi.hoisted(() => ({
  push: vi.fn(),
}))

const mockWorkspaceStore = reactive({
  homeSummary: null as HomeSummary | null,
  homeLoading: false,
  homeError: null as string | null,
  hasHomeSummary: false,
  fetchHomeSummary: vi.fn<() => Promise<void>>(),
})

vi.mock('../../store/workspaceStore', () => ({
  useWorkspaceStore: () => mockWorkspaceStore,
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({
    push: routerMocks.push,
  }),
}))

async function waitForUi() {
  await Promise.resolve()
  await Promise.resolve()
}

describe('HomeView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockWorkspaceStore.homeLoading = false
    mockWorkspaceStore.homeError = null
    mockWorkspaceStore.hasHomeSummary = false
    mockWorkspaceStore.homeSummary = {
      workspaceMode: 'guided',
      isFirstRun: true,
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
  })

  it('loads the home summary on mount when needed', async () => {
    mount(HomeView)
    await waitForUi()

    expect(mockWorkspaceStore.fetchHomeSummary).toHaveBeenCalledTimes(1)
  })

  it('renders first-run guidance, workload, and recent boards', async () => {
    const wrapper = mount(HomeView)
    await waitForUi()

    expect(wrapper.text()).toContain('Start here')
    expect(wrapper.text()).toContain('Needs triage')
    expect(wrapper.text()).toContain('Needs follow-up')
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

  it('navigates from recommended actions and board cards', async () => {
    const wrapper = mount(HomeView)
    await waitForUi()

    await wrapper.get('.td-home-action').trigger('click')
    await wrapper.get('.td-home-board').trigger('click')

    expect(routerMocks.push).toHaveBeenCalledWith('/workspace/review')
    expect(routerMocks.push).toHaveBeenCalledWith('/workspace/boards/board-1')
  })
})
