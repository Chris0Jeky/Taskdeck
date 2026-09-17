import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import http from '../../api/http'
import { useWorkspaceStore } from '../../store/workspaceStore'
import type {
  HomeSummary,
  TodaySummary,
  WorkspaceOnboarding,
  WorkspaceOnboardingStep,
} from '../../types/workspace'

const sessionState = vi.hoisted(() => ({ isAuthenticated: true }))

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    patch: vi.fn(),
    delete: vi.fn(),
  },
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => ({ error: vi.fn(), success: vi.fn(), warning: vi.fn(), info: vi.fn() }),
}))

vi.mock('../../store/sessionStore', () => ({
  useSessionStore: () => sessionState,
}))

vi.mock('../../utils/demoMode', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../utils/demoMode')>()
  return { ...actual, isDemoMode: false }
})

const steps: WorkspaceOnboardingStep[] = [
  {
    stepId: 'create-first-board',
    title: 'Create a board',
    description: 'Create the first board.',
    targetSurface: 'boards',
    isComplete: false,
  },
]

function makeOnboarding(overrides: Partial<WorkspaceOnboarding> = {}): WorkspaceOnboarding {
  return {
    visibility: 'active',
    isComplete: false,
    currentStepId: 'create-first-board',
    dismissedAt: null,
    completedAt: null,
    steps,
    ...overrides,
  }
}

function makeDeferredOnboarding(): WorkspaceOnboarding {
  return makeOnboarding({
    visibility: 'dismissed',
    currentStepId: null,
    dismissedAt: '2026-09-01T10:00:00Z',
    steps: [],
  })
}

function makeHomeSummary(overrides: Partial<HomeSummary> = {}): HomeSummary {
  return {
    workspaceMode: 'guided',
    isFirstRun: false,
    onboarding: makeOnboarding(),
    workload: {
      capturesNeedingTriage: 0,
      capturesInProgress: 0,
      capturesReadyForFollowUp: 0,
      proposalsPendingReview: 0,
    },
    boards: { totalBoards: 0, recentBoardsCount: 0, recentBoards: [] },
    recommendedActions: [],
    ...overrides,
  }
}

function makeTodaySummary(overrides: Partial<TodaySummary> = {}): TodaySummary {
  return {
    workspaceMode: 'guided',
    onboarding: makeOnboarding(),
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
    ...overrides,
  }
}

describe('workspaceStore residual preference interleavings (#1410)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    localStorage.clear()
    sessionState.isAuthenticated = true
  })

  it('adopts the authoritative replay payload when the optimistic base is deferred', async () => {
    const deferred = makeDeferredOnboarding()
    const replayed = makeOnboarding({ dismissedAt: null })
    const store = useWorkspaceStore()
    store.onboarding = deferred
    store.homeSummary = makeHomeSummary({ onboarding: deferred })
    vi.mocked(http.put).mockResolvedValueOnce({ data: replayed })

    await store.updateOnboarding('replay')

    expect(store.onboarding).toEqual(replayed)
    expect(store.homeSummary?.onboarding).toEqual(replayed)
  })

  it('invalidates an older mode write when logout resets the workspace store', async () => {
    let rejectOldWrite!: (reason: unknown) => void
    vi.mocked(http.put).mockReturnValueOnce(
      new Promise((_resolve, reject) => { rejectOldWrite = reject }),
    )
    const store = useWorkspaceStore()
    const oldWrite = store.updateMode('workbench')

    sessionState.isAuthenticated = false
    store.resetForLogout()
    rejectOldWrite(new Error('old session write failed'))
    await oldWrite

    sessionState.isAuthenticated = true
    vi.mocked(http.get).mockResolvedValueOnce({
      data: makeHomeSummary({ workspaceMode: 'agent' }),
    })
    await store.fetchHomeSummary()

    expect(store.preferenceError).toBeNull()
    expect(store.mode).toBe('agent')
    expect(store.preferencesHydrated).toBe(true)
  })

  it('invalidates an older onboarding write when logout resets the workspace store', async () => {
    let rejectOldWrite!: (reason: unknown) => void
    vi.mocked(http.put).mockReturnValueOnce(
      new Promise((_resolve, reject) => { rejectOldWrite = reject }),
    )
    const store = useWorkspaceStore()
    store.onboarding = makeOnboarding()
    const oldWrite = store.updateOnboarding('dismiss')

    sessionState.isAuthenticated = false
    store.resetForLogout()
    rejectOldWrite(new Error('old session onboarding failed'))
    await expect(oldWrite).rejects.toThrow('old session onboarding failed')

    sessionState.isAuthenticated = true
    const newOnboarding = makeOnboarding({ currentStepId: 'create-first-board' })
    vi.mocked(http.get).mockResolvedValueOnce({
      data: makeHomeSummary({ onboarding: newOnboarding }),
    })
    await store.fetchHomeSummary()

    expect(store.preferenceError).toBeNull()
    expect(store.onboarding).toEqual(newOnboarding)
  })

  it('drops a pre-logout Home response while the next login fetch is pending', async () => {
    let resolveOldHome!: (value: { data: HomeSummary }) => void
    let resolveFreshHome!: (value: { data: HomeSummary }) => void
    vi.mocked(http.get)
      .mockReturnValueOnce(
        new Promise<{ data: HomeSummary }>((resolve) => {
          resolveOldHome = resolve
        }),
      )
      .mockReturnValueOnce(
        new Promise<{ data: HomeSummary }>((resolve) => {
          resolveFreshHome = resolve
        }),
      )

    const store = useWorkspaceStore()
    const oldHomeRequest = store.fetchHomeSummary()

    sessionState.isAuthenticated = false
    store.resetForLogout()
    expect(store.homeLoading).toBe(false)
    sessionState.isAuthenticated = true
    const freshSummary = makeHomeSummary({
      workload: { ...makeHomeSummary().workload, capturesNeedingTriage: 2 },
    })
    const freshHomeRequest = store.fetchHomeSummary()

    resolveOldHome({
      data: makeHomeSummary({
        workload: { ...makeHomeSummary().workload, capturesNeedingTriage: 1 },
      }),
    })
    await oldHomeRequest

    expect(store.homeSummary).toBeNull()
    expect(store.homeLoading).toBe(true)

    resolveFreshHome({ data: freshSummary })
    await freshHomeRequest

    expect(store.homeSummary).toEqual(freshSummary)
    expect(store.inboxBadgeCount).toBe(2)
    expect(store.homeLoading).toBe(false)
  })

  it('lets a clean Home summary confirm a dirty mode that already reached the server', async () => {
    vi.mocked(http.put).mockRejectedValueOnce(new Error('response lost after commit'))
    const store = useWorkspaceStore()
    await store.updateMode('workbench')
    expect(store.preferencesHydrated).toBe(false)

    vi.mocked(http.get).mockResolvedValueOnce({
      data: makeHomeSummary({ workspaceMode: 'workbench' }),
    })
    await store.fetchHomeSummary()
    expect(store.mode).toBe('workbench')
    expect(store.preferencesHydrated).toBe(true)

    vi.mocked(http.get).mockResolvedValueOnce({
      data: makeHomeSummary({ workspaceMode: 'agent' }),
    })
    await store.fetchHomeSummary()
    expect(store.mode).toBe('agent')
  })

  it('lets a clean Today summary confirm a dirty mode that already reached the server', async () => {
    vi.mocked(http.put).mockRejectedValueOnce(new Error('response lost after commit'))
    const store = useWorkspaceStore()
    await store.updateMode('workbench')
    expect(store.preferencesHydrated).toBe(false)

    vi.mocked(http.get).mockResolvedValueOnce({
      data: makeTodaySummary({ workspaceMode: 'workbench' }),
    })
    await store.fetchTodaySummary()
    expect(store.mode).toBe('workbench')
    expect(store.preferencesHydrated).toBe(true)

    vi.mocked(http.get).mockResolvedValueOnce({
      data: makeTodaySummary({ workspaceMode: 'agent' }),
    })
    await store.fetchTodaySummary()
    expect(store.mode).toBe('agent')
  })
})
