import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { nextTick, reactive } from 'vue'
import PaperHomeView from '../../../views/paper/PaperHomeView.vue'
import type { HomeSummary } from '../../../types/workspace'

/**
 * Paper-parent Escape regression (finding M2).
 *
 * The setup modal must join the GLOBAL Escape stack when opened from Paper Home.
 * PaperHomeView keeps `WorkspaceSetupModal` mounted and toggles `is-open`, so the
 * modal's non-immediate `watch(isOpen)` observes false→true and calls
 * `registerEscapeHandler`. This test uses the REAL modal and the REAL escape stack
 * (no stubs) and dispatches a real Escape keydown — if the modal were mounted only
 * when open with `:is-open="true"`, the watch would never fire and Escape would not close it.
 */

const mockSessionStore = reactive({ username: 'daniel' as string | null })

const mockWorkspaceStore = reactive({
  homeSummary: null as HomeSummary | null,
  homeLoading: false,
  homeError: null as string | null,
  hasHomeSummary: true,
  fetchHomeSummary: vi.fn<() => Promise<void>>(),
  clearHomeSummary: vi.fn(),
  clearTodaySummary: vi.fn(),
})

const mockCaptureStore = { createItem: vi.fn() }
const mockBoardStore = { createBoard: vi.fn() }
const mockToastStore = { success: vi.fn(), warning: vi.fn() }

vi.mock('../../../store/sessionStore', () => ({ useSessionStore: () => mockSessionStore }))
vi.mock('../../../store/workspaceStore', () => ({ useWorkspaceStore: () => mockWorkspaceStore }))
vi.mock('../../../store/captureStore', () => ({ useCaptureStore: () => mockCaptureStore }))
vi.mock('../../../store/boardStore', () => ({ useBoardStore: () => mockBoardStore }))
vi.mock('../../../store/toastStore', () => ({ useToastStore: () => mockToastStore }))
vi.mock('../../../api/starterPacksApi', () => ({
  starterPacksApi: { getCatalog: vi.fn(), applyStarterPack: vi.fn() },
}))
vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn() }) }))

function buildFirstRunSummary(): HomeSummary {
  return {
    workspaceMode: 'guided',
    isFirstRun: true,
    onboarding: {
      visibility: 'active',
      isComplete: false,
      currentStepId: 'create-first-board',
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
    boards: { totalBoards: 0, recentBoardsCount: 0, recentBoards: [] },
    recommendedActions: [],
  }
}

describe('PaperHomeView — Escape closes the guided setup modal', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockWorkspaceStore.homeSummary = buildFirstRunSummary()
    mockWorkspaceStore.homeLoading = false
    mockWorkspaceStore.homeError = null
    mockWorkspaceStore.hasHomeSummary = true
    mockWorkspaceStore.fetchHomeSummary.mockResolvedValue(undefined)
  })

  afterEach(() => {
    document.body.innerHTML = ''
  })

  it('registers the global Escape handler when opened and closes on Escape', async () => {
    const wrapper = mount(PaperHomeView, { attachTo: document.body })

    // The modal starts mounted-but-closed: no overlay in the teleport target.
    expect(document.body.querySelector('.td-overlay')).toBeNull()

    await wrapper.get('[data-testid="paper-home-setup-cta"]').trigger('click')
    await nextTick()
    await flushPromises()

    // Opening moves the modal into the DOM.
    expect(document.body.querySelector('.td-overlay')).not.toBeNull()

    // A real global Escape (capture phase, window-level) must reach the modal's
    // registered handler — proving it joined the escape stack.
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
    await nextTick()
    await flushPromises()

    expect(document.body.querySelector('.td-overlay')).toBeNull()

    wrapper.unmount()
  })
})
