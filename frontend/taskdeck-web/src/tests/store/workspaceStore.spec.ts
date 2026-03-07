import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { workspaceApi } from '../../api/workspaceApi'
import { useWorkspaceStore } from '../../store/workspaceStore'
import { WORKSPACE_MODE_STORAGE_KEY } from '../../utils/storageKeys'

const toastMocks = vi.hoisted(() => ({
  warning: vi.fn(),
}))

const sessionMock = vi.hoisted(() => ({
  isAuthenticated: true,
}))

vi.mock('../../api/workspaceApi', () => ({
  workspaceApi: {
    getHomeSummary: vi.fn(),
    getPreferences: vi.fn(),
    updatePreferences: vi.fn(),
  },
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => toastMocks,
}))

vi.mock('../../store/sessionStore', () => ({
  useSessionStore: () => sessionMock,
}))

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
      userId: crypto.randomUUID(),
      workspaceMode: 'workbench',
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    })

    await store.hydratePreferences()

    expect(store.mode).toBe('workbench')
    expect(localStorage.getItem(WORKSPACE_MODE_STORAGE_KEY)).toBe('workbench')
    expect(store.preferencesHydrated).toBe(true)
  })

  it('updates mode through the API and persists it locally', async () => {
    const store = useWorkspaceStore()
    vi.mocked(workspaceApi.updatePreferences).mockResolvedValue({
      userId: crypto.randomUUID(),
      workspaceMode: 'agent',
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    })

    await store.updateMode('agent')

    expect(store.mode).toBe('agent')
    expect(localStorage.getItem(WORKSPACE_MODE_STORAGE_KEY)).toBe('agent')
    expect(workspaceApi.updatePreferences).toHaveBeenCalledWith({ workspaceMode: 'agent' })
  })

  it('keeps the local mode when server persistence fails', async () => {
    const store = useWorkspaceStore()
    vi.mocked(workspaceApi.updatePreferences).mockRejectedValue(new Error('save failed'))

    await store.updateMode('agent')

    expect(store.mode).toBe('agent')
    expect(toastMocks.warning).toHaveBeenCalledWith('save failed. Keeping the local selection for now.')
  })

  it('loads home summary and syncs the mode from the payload', async () => {
    const store = useWorkspaceStore()
    vi.mocked(workspaceApi.getHomeSummary).mockResolvedValue({
      workspaceMode: 'guided',
      isFirstRun: false,
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
    expect(store.mode).toBe('guided')
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
