import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { workspaceApi } from '../../api/workspaceApi'
import { useWorkspaceStore } from '../../store/workspaceStore'
import { WORKSPACE_MODE_STORAGE_KEY } from '../../utils/storageKeys'

const TEST_USER_ID = '00000000-0000-0000-0000-000000000001'

const toastMocks = vi.hoisted(() => ({
  warning: vi.fn(),
}))

const sessionMock = vi.hoisted(() => ({
  isAuthenticated: true,
}))

function buildOnboarding(overrides?: Partial<ReturnType<typeof createOnboarding>>) {
  return {
    ...createOnboarding(),
    ...overrides,
  }
}

function createOnboarding() {
  return {
    visibility: 'active' as const,
    isComplete: false,
    currentStepId: 'create-first-board',
    dismissedAt: null,
    completedAt: null,
    steps: [
      {
        stepId: 'create-first-board',
        title: 'Create your first board',
        description: 'Create a board.',
        targetSurface: 'boards' as const,
        isComplete: false,
      },
    ],
  }
}

vi.mock('../../api/workspaceApi', () => ({
  workspaceApi: {
    getHomeSummary: vi.fn(),
    getTodaySummary: vi.fn(),
    getPreferences: vi.fn(),
    updatePreferences: vi.fn(),
    updateOnboarding: vi.fn(),
  },
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => toastMocks,
}))

vi.mock('../../store/sessionStore', () => ({
  useSessionStore: () => sessionMock,
}))

function createDeferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((innerResolve, innerReject) => {
    resolve = innerResolve
    reject = innerReject
  })

  return { promise, resolve, reject }
}

describe('workspaceStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    sessionMock.isAuthenticated = true
    localStorage.clear()
  })

  it('hydrates preferences and persists the server-backed mode', async () => {
    const store = useWorkspaceStore()
    vi.mocked(workspaceApi.getPreferences).mockResolvedValue({
      userId: TEST_USER_ID,
      workspaceMode: 'workbench',
      onboarding: buildOnboarding(),
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    })

    await store.hydratePreferences()

    expect(store.mode).toBe('workbench')
    expect(store.onboarding?.currentStepId).toBe('create-first-board')
    expect(localStorage.getItem(WORKSPACE_MODE_STORAGE_KEY)).toBe('workbench')
    expect(store.preferencesHydrated).toBe(true)
  })

  it('updates mode through the API and persists it locally', async () => {
    const store = useWorkspaceStore()
    vi.mocked(workspaceApi.updatePreferences).mockResolvedValue({
      userId: TEST_USER_ID,
      workspaceMode: 'agent',
      onboarding: buildOnboarding({ visibility: 'dismissed', dismissedAt: new Date().toISOString() }),
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    })

    await store.updateMode('agent')

    expect(store.mode).toBe('agent')
    expect(store.onboarding?.visibility).toBe('dismissed')
    expect(localStorage.getItem(WORKSPACE_MODE_STORAGE_KEY)).toBe('agent')
    expect(workspaceApi.updatePreferences).toHaveBeenCalledWith({ workspaceMode: 'agent' })
  })

  it('keeps the latest mode when an older hydration request resolves after an update', async () => {
    const store = useWorkspaceStore()
    const hydration = createDeferred<Awaited<ReturnType<typeof workspaceApi.getPreferences>>>()
    const update = createDeferred<Awaited<ReturnType<typeof workspaceApi.updatePreferences>>>()

    vi.mocked(workspaceApi.getPreferences).mockReturnValue(hydration.promise)
    vi.mocked(workspaceApi.updatePreferences).mockReturnValue(update.promise)

    const hydratePromise = store.hydratePreferences()
    expect(store.preferenceLoading).toBe(true)

    const updatePromise = store.updateMode('agent')
    expect(store.mode).toBe('agent')
    expect(store.preferenceLoading).toBe(true)

    update.resolve({
      userId: TEST_USER_ID,
      workspaceMode: 'agent',
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    })
    await updatePromise

    expect(store.mode).toBe('agent')
    expect(store.preferenceLoading).toBe(true)

    hydration.resolve({
      userId: TEST_USER_ID,
      workspaceMode: 'guided',
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    })
    await hydratePromise

    expect(store.mode).toBe('agent')
    expect(localStorage.getItem(WORKSPACE_MODE_STORAGE_KEY)).toBe('agent')
    expect(store.preferencesHydrated).toBe(true)
    expect(store.preferenceLoading).toBe(false)
  })

  it('keeps the local mode when server persistence fails', async () => {
    const store = useWorkspaceStore()
    vi.mocked(workspaceApi.updatePreferences).mockRejectedValue(new Error('save failed'))

    await store.updateMode('agent')

    expect(store.mode).toBe('agent')
    expect(toastMocks.warning).toHaveBeenCalledWith('save failed. Keeping the local selection for now.')
  })

  it('loads home summary and syncs mode and onboarding from the payload', async () => {
    const store = useWorkspaceStore()
    vi.mocked(workspaceApi.getHomeSummary).mockResolvedValue({
      workspaceMode: 'guided',
      isFirstRun: false,
      onboarding: buildOnboarding(),
      workload: {
        capturesNeedingTriage: 1,
        capturesInProgress: 2,
        capturesReadyForFollowUp: 3,
        proposalsPendingReview: 2,
      },
      boards: {
        totalBoards: 2,
        recentBoardsCount: 1,
        recentBoards: [],
      },
      recommendedActions: [],
    })

    await store.fetchHomeSummary()

    expect(store.homeSummary?.workload.proposalsPendingReview).toBe(2)
    expect(store.onboarding?.currentStepId).toBe('create-first-board')
    expect(store.mode).toBe('guided')
    expect(store.inboxBadgeCount).toBe(1)
    expect(store.reviewBadgeCount).toBe(2)
  })

  it('returns zero badge counts when home summary is not loaded', () => {
    const store = useWorkspaceStore()

    expect(store.inboxBadgeCount).toBe(0)
    expect(store.reviewBadgeCount).toBe(0)
  })

  it('loads today summary and syncs onboarding state', async () => {
    const store = useWorkspaceStore()
    vi.mocked(workspaceApi.getTodaySummary).mockResolvedValue({
      workspaceMode: 'guided',
      onboarding: buildOnboarding({ isComplete: true, currentStepId: null }),
      summary: {
        capturesNeedingTriage: 1,
        proposalsPendingReview: 2,
        overdueCards: 1,
        dueTodayCards: 2,
        blockedCards: 1,
      },
      overdueCards: [],
      dueTodayCards: [],
      blockedCards: [],
      recommendedActions: [],
    })

    await store.fetchTodaySummary()

    expect(store.todaySummary?.summary.dueTodayCards).toBe(2)
    expect(store.onboarding?.isComplete).toBe(true)
  })

  it('updates onboarding and patches any loaded summaries', async () => {
    const store = useWorkspaceStore()
    store.homeSummary = {
      workspaceMode: 'guided',
      isFirstRun: true,
      onboarding: buildOnboarding(),
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
    }
    store.todaySummary = {
      workspaceMode: 'guided',
      onboarding: buildOnboarding(),
      summary: {
        capturesNeedingTriage: 0,
        proposalsPendingReview: 0,
        overdueCards: 0,
        dueTodayCards: 0,
        blockedCards: 0,
      },
      overdueCards: [],
      dueTodayCards: [],
      blockedCards: [],
      recommendedActions: [],
    }

    vi.mocked(workspaceApi.updateOnboarding).mockResolvedValue(
      buildOnboarding({
        visibility: 'dismissed',
        dismissedAt: new Date().toISOString(),
      }),
    )

    await store.updateOnboarding('dismiss')

    expect(store.onboarding?.visibility).toBe('dismissed')
    expect(store.homeSummary?.onboarding.visibility).toBe('dismissed')
    expect(store.todaySummary?.onboarding.visibility).toBe('dismissed')
  })

  it('keeps only the local mode when unauthenticated', async () => {
    const store = useWorkspaceStore()
    sessionMock.isAuthenticated = false
    localStorage.setItem(WORKSPACE_MODE_STORAGE_KEY, 'workbench')

    store.resetForLogout()
    await store.updateMode('workbench')

    expect(workspaceApi.updatePreferences).not.toHaveBeenCalled()
    expect(store.mode).toBe('workbench')
  })
})
