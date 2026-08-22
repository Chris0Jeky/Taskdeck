import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { reactive, ref } from 'vue'
import PaperSidebar from '../../../components/paper/PaperSidebar.vue'
import { workspaceApi } from '../../../api/workspaceApi'
import { captureApi } from '../../../api/captureApi'
import { useCaptureStore } from '../../../store/captureStore'
import { useWorkspaceStore } from '../../../store/workspaceStore'
import { resetProductVersionForTests } from '../../../composables/useProductVersion'
import type { HomeSummary, HomeWorkloadSummary } from '../../../types/workspace'
import type { FeatureFlags } from '../../../types/feature-flags'
import type { ViewportMode } from '../../../composables/useViewportMode'

/**
 * Sidebar Inbox badge freshness (#1974).
 *
 * The badge reads `workspaceStore.homeSummary.workload`, which `AppShell`
 * fetches once when the session authenticates. Before the fix, a capture saved
 * mid-session left the badge on its pre-capture number until a full page
 * reload — the reporter watched it stay at `Inbox · 1` while two `NEW` rows
 * sat in the inbox.
 *
 * Unlike the sibling `PaperSidebar.spec.ts`, this file runs the REAL workspace
 * and capture stores and stubs only the HTTP transports, because the defect
 * lives in the wiring between them: a spec against a mocked workspace store
 * cannot fail on it.
 */

const mockRoute = reactive({ path: '/workspace/home' })

const mockFeatureFlags = {
  isEnabled: vi.fn<(flag: keyof FeatureFlags) => boolean>(() => true),
}

const mockPaperTheme = reactive({
  mode: 'paper' as 'off' | 'paper' | 'paper-night' | 'auto',
  isOn: true,
  activeClass: 'paper' as 'paper' | 'paper-night' | null,
  toggleNight: vi.fn(),
})

const mockViewportMode = ref<ViewportMode>('desktop')

vi.mock('vue-router', () => ({
  useRoute: () => mockRoute,
}))

vi.mock('../../../store/featureFlagStore', () => ({
  useFeatureFlagStore: () => mockFeatureFlags,
}))

vi.mock('../../../store/paperThemeStore', () => ({
  usePaperThemeStore: () => mockPaperTheme,
}))

vi.mock('../../../composables/useViewportMode', () => ({
  useViewportMode: () => ({ mode: mockViewportMode }),
}))

vi.mock('../../../api/versionApi', () => ({
  versionApi: { getProductVersion: vi.fn(async () => null) },
}))

vi.mock('../../../api/workspaceApi', () => ({
  workspaceApi: {
    getHomeSummary: vi.fn(),
    getTodaySummary: vi.fn(),
    getCalendar: vi.fn(),
    getPreferences: vi.fn(),
    updatePreferences: vi.fn(),
    updateOnboarding: vi.fn(),
  },
}))

vi.mock('../../../api/captureApi', () => ({
  captureApi: {
    createItem: vi.fn(),
    listItems: vi.fn(),
    getItem: vi.fn(),
    ignoreItem: vi.fn(),
    cancelItem: vi.fn(),
    enqueueTriage: vi.fn(),
    batchTriage: vi.fn(),
    updateSuggestion: vi.fn(),
  },
}))

function homeSummaryWith(workload: Partial<HomeWorkloadSummary>): HomeSummary {
  return {
    workspaceMode: 'guided',
    isFirstRun: false,
    onboarding: {
      visibility: 'dismissed',
      isComplete: true,
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
      ...workload,
    },
    boards: { totalBoards: 0, recentBoardsCount: 0, recentBoards: [] },
    recommendedActions: [],
  }
}

function mountSidebar() {
  return mount(PaperSidebar, {
    global: {
      stubs: {
        RouterLink: {
          props: ['to'],
          template: '<a :href="to" v-bind="$attrs"><slot /></a>',
        },
      },
    },
  })
}

function inboxBadgeText(wrapper: ReturnType<typeof mountSidebar>): string | null {
  const inboxItem = wrapper
    .findAll('[data-group="primary"] .paper-sidebar__item')
    .find((item) => item.text().includes('Inbox'))
  const badge = inboxItem?.find('.paper-sidebar__badge')
  return badge?.exists() ? badge.text() : null
}

describe('PaperSidebar inbox badge freshness', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    mockRoute.path = '/workspace/home'
    mockFeatureFlags.isEnabled = vi.fn(() => true)
    mockPaperTheme.activeClass = 'paper'
    mockViewportMode.value = 'desktop'
    resetProductVersionForTests()
  })

  it('updates the rendered badge after a capture is created, without a remount', async () => {
    const workspace = useWorkspaceStore()
    const capture = useCaptureStore()

    // Session start: one capture awaiting triage.
    vi.mocked(workspaceApi.getHomeSummary).mockResolvedValue(
      homeSummaryWith({ capturesNeedingTriage: 1 }),
    )
    await workspace.fetchHomeSummary()

    const wrapper = mountSidebar()
    await flushPromises()
    expect(inboxBadgeText(wrapper)).toBe('· 1')

    // The user saves a capture; the server now counts two.
    vi.mocked(workspaceApi.getHomeSummary).mockResolvedValue(
      homeSummaryWith({ capturesNeedingTriage: 2 }),
    )
    vi.mocked(captureApi.createItem).mockResolvedValue({
      id: 'c-new',
      userId: 'u1',
      boardId: null,
      status: 'New',
      source: 'Typed',
      textExcerpt: 'Just captured',
      rawText: 'Just captured',
      createdAt: new Date().toISOString(),
      processedAt: null,
      retryCount: 0,
      provenance: null,
    })

    await capture.createItem({ boardId: null, text: 'Just captured' })
    await flushPromises()

    // Same wrapper — never remounted, never reloaded.
    expect(inboxBadgeText(wrapper)).toBe('· 2')
    wrapper.unmount()
  })

  it('updates the rendered badge after a capture is triaged away from the queue', async () => {
    const workspace = useWorkspaceStore()
    const capture = useCaptureStore()

    vi.mocked(workspaceApi.getHomeSummary).mockResolvedValue(
      homeSummaryWith({ capturesNeedingTriage: 2 }),
    )
    await workspace.fetchHomeSummary()

    const wrapper = mountSidebar()
    await flushPromises()
    expect(inboxBadgeText(wrapper)).toBe('· 2')

    vi.mocked(workspaceApi.getHomeSummary).mockResolvedValue(
      homeSummaryWith({ capturesNeedingTriage: 1 }),
    )
    vi.mocked(captureApi.enqueueTriage).mockResolvedValue({
      id: 'c-1',
      status: 'Triaging',
      alreadyTriaging: false,
    })
    vi.mocked(captureApi.getItem).mockResolvedValue({
      id: 'c-1',
      userId: 'u1',
      boardId: null,
      status: 'Triaging',
      source: 'Typed',
      textExcerpt: 'Triage me',
      rawText: 'Triage me',
      createdAt: new Date().toISOString(),
      processedAt: null,
      retryCount: 0,
      provenance: null,
    })

    await capture.triageItem('c-1')
    await flushPromises()

    expect(inboxBadgeText(wrapper)).toBe('· 1')
    wrapper.unmount()
  })

  it('refreshes only the workload slice, leaving workspace mode untouched', async () => {
    // The refresh must not run the preference-ordering machinery: a background
    // badge update may never re-apply a server mode over newer local intent.
    const workspace = useWorkspaceStore()

    vi.mocked(workspaceApi.getHomeSummary).mockResolvedValue(
      homeSummaryWith({ capturesNeedingTriage: 1 }),
    )
    await workspace.fetchHomeSummary()

    const stale = homeSummaryWith({ capturesNeedingTriage: 4 })
    stale.workspaceMode = 'workbench'
    vi.mocked(workspaceApi.getHomeSummary).mockResolvedValue(stale)

    await workspace.refreshWorkloadCounts()

    expect(workspace.inboxBadgeCount).toBe(4)
    expect(workspace.mode).toBe('guided')
    expect(workspace.homeLoading).toBe(false)
  })

  it('is a no-op before any summary exists, so it cannot invent a badge', async () => {
    const workspace = useWorkspaceStore()

    await workspace.refreshWorkloadCounts()

    expect(workspaceApi.getHomeSummary).not.toHaveBeenCalled()
    expect(workspace.inboxBadgeCount).toBe(0)
  })

  it('keeps the last known counts when the refresh fails', async () => {
    const workspace = useWorkspaceStore()

    vi.mocked(workspaceApi.getHomeSummary).mockResolvedValue(
      homeSummaryWith({ capturesNeedingTriage: 3 }),
    )
    await workspace.fetchHomeSummary()

    vi.mocked(workspaceApi.getHomeSummary).mockRejectedValue(new Error('offline'))
    await expect(workspace.refreshWorkloadCounts()).resolves.toBeUndefined()

    expect(workspace.inboxBadgeCount).toBe(3)
    expect(workspace.homeError).toBeNull()
  })
})
