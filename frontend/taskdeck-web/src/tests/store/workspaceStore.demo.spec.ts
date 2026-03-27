import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'

vi.mock('../../utils/demoMode', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../utils/demoMode')>()
  return {
    ...actual,
    isDemoMode: true,
  }
})

vi.mock('../../api/workspaceApi', () => ({
  workspaceApi: {
    getHomeSummary: vi.fn(),
    getTodaySummary: vi.fn(),
    getPreferences: vi.fn(),
    updatePreferences: vi.fn(),
    updateOnboarding: vi.fn(),
  },
}))

const toastMocks = vi.hoisted(() => ({
  warning: vi.fn(),
  info: vi.fn(),
}))

const sessionMock = vi.hoisted(() => ({
  isAuthenticated: true,
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => toastMocks,
}))

vi.mock('../../store/sessionStore', () => ({
  useSessionStore: () => sessionMock,
}))

import { useWorkspaceStore } from '../../store/workspaceStore'
import { workspaceApi } from '../../api/workspaceApi'

describe('workspaceStore demo mode', () => {
  let store: ReturnType<typeof useWorkspaceStore>

  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    sessionMock.isAuthenticated = true
    localStorage.clear()
    store = useWorkspaceStore()
  })

  it('hydratePreferences sets guided mode without calling API', async () => {
    await store.hydratePreferences()

    expect(store.mode).toBe('guided')
    expect(store.preferencesHydrated).toBe(true)
    expect(store.onboarding).not.toBeNull()
    expect(workspaceApi.getPreferences).not.toHaveBeenCalled()
  })

  it('fetchHomeSummary returns demo data without calling API', async () => {
    const summary = await store.fetchHomeSummary()

    expect(summary.workspaceMode).toBe('guided')
    expect(summary.boards.recentBoards.length).toBeGreaterThan(0)
    expect(store.homeSummary).not.toBeNull()
    expect(workspaceApi.getHomeSummary).not.toHaveBeenCalled()
  })

  it('fetchTodaySummary returns demo data with relative overdue dates', async () => {
    const summary = await store.fetchTodaySummary()

    expect(summary.overdueCards).toHaveLength(1)
    const overdueDate = new Date(summary.overdueCards[0].dueDate!)
    expect(overdueDate.getTime()).toBeLessThan(Date.now())
    expect(workspaceApi.getTodaySummary).not.toHaveBeenCalled()
  })

  it('updateMode updates local state without calling API', async () => {
    await store.updateMode('workbench')

    expect(store.mode).toBe('workbench')
    expect(workspaceApi.updatePreferences).not.toHaveBeenCalled()
  })

  it('updateOnboarding updates local state without calling API', async () => {
    const result = await store.updateOnboarding('dismiss')

    expect(result.visibility).toBe('dismissed')
    expect(store.onboarding?.visibility).toBe('dismissed')
    expect(workspaceApi.updateOnboarding).not.toHaveBeenCalled()
  })
})
