import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { reactive } from 'vue'
import http from '../../../api/http'
import PaperHomeView from '../../../views/paper/PaperHomeView.vue'
import type { HomeSummary } from '../../../types/workspace'

const routerMocks = vi.hoisted(() => ({ push: vi.fn() }))

const mockSessionStore = reactive({ username: 'daniel' as string | null })
const mockWorkspaceStore = reactive({
  homeSummary: null as HomeSummary | null,
  homeLoading: false,
  homeError: null as string | null,
  hasHomeSummary: true,
  onboarding: null,
  fetchHomeSummary: vi.fn<() => Promise<void>>(),
  refreshWorkloadCounts: vi.fn<() => Promise<void>>(),
})

vi.mock('../../../api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    patch: vi.fn(),
    delete: vi.fn(),
  },
}))

vi.mock('../../../store/sessionStore', () => ({
  useSessionStore: () => mockSessionStore,
}))

vi.mock('../../../store/workspaceStore', () => ({
  useWorkspaceStore: () => mockWorkspaceStore,
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: routerMocks.push }),
}))

function buildSummary(): HomeSummary {
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
  }
}

describe('PaperHomeView quick-capture recovery', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    mockWorkspaceStore.homeSummary = buildSummary()
    mockWorkspaceStore.fetchHomeSummary.mockResolvedValue(undefined)
    mockWorkspaceStore.refreshWorkloadCounts.mockResolvedValue(undefined)
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('keeps the typed body and a persistent inline error when POST /capture/items fails', async () => {
    vi.useFakeTimers()
    vi.mocked(http.post).mockRejectedValueOnce({
      response: {
        status: 500,
        data: {
          errorCode: 'UnexpectedError',
          message: 'Capture service unavailable',
        },
      },
    })

    const wrapper = mount(PaperHomeView)
    const input = wrapper.get<HTMLInputElement>('[data-testid="paper-home-capture-input"]')
    await input.setValue('Keep this thought recoverable')
    await wrapper.get('form').trigger('submit.prevent')
    await flushPromises()

    expect(http.post).toHaveBeenCalledWith('/capture/items', {
      boardId: null,
      text: 'Keep this thought recoverable',
      source: 'Typed',
    })
    expect(input.element.value).toBe('Keep this thought recoverable')

    const error = wrapper.get('[data-testid="paper-home-capture-error"]')
    expect(error.attributes('role')).toBe('alert')
    expect(input.attributes('aria-describedby')).toBe('paper-home-capture-error')
    expect(error.text()).toContain('Capture not saved. Your text is still here.')
    expect(error.text()).toContain('Capture service unavailable')

    // The store's ordinary error toast expires after five seconds. Advancing
    // well past that boundary proves the inline receipt has no dismiss timer.
    await vi.advanceTimersByTimeAsync(30_000)
    await wrapper.vm.$nextTick()
    expect(wrapper.find('[data-testid="paper-home-capture-error"]').exists()).toBe(true)

    wrapper.unmount()
  })
})
