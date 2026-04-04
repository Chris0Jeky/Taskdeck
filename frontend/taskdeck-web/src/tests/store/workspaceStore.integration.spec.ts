/**
 * workspaceStore integration tests — store + real workspaceApi module, HTTP layer mocked.
 *
 * These tests exercise the full store → workspaceApi → http path.  Mocking http
 * (not the API module) means any mismatch between the API response shape and
 * what the store state expects will be caught here.
 */
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import http from '../../api/http'
import { useWorkspaceStore } from '../../store/workspaceStore'
import { WORKSPACE_MODE_STORAGE_KEY } from '../../utils/storageKeys'
import type { HomeSummary, TodaySummary, WorkspaceOnboarding } from '../../types/workspace'

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
  useSessionStore: () => ({ isAuthenticated: true }),
}))

vi.mock('../../utils/demoMode', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../utils/demoMode')>()
  return { ...actual, isDemoMode: false }
})

function makeOnboarding(overrides: Partial<WorkspaceOnboarding> = {}): WorkspaceOnboarding {
  return {
    visibility: 'active',
    isComplete: false,
    currentStepId: 'create-first-board',
    dismissedAt: null,
    completedAt: null,
    steps: [],
    ...overrides,
  }
}

function makePreferencePayload(mode: 'guided' | 'workbench' | 'agent' = 'guided') {
  return {
    userId: 'u-1',
    workspaceMode: mode,
    onboarding: makeOnboarding(),
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  }
}

function makeHomeSummary(overrides: Partial<HomeSummary> = {}): HomeSummary {
  return {
    workspaceMode: 'guided',
    isFirstRun: false,
    onboarding: makeOnboarding(),
    workload: {
      capturesNeedingTriage: 3,
      capturesInProgress: 1,
      capturesReadyForFollowUp: 0,
      proposalsPendingReview: 2,
    },
    boards: {
      totalBoards: 2,
      recentBoardsCount: 2,
      recentBoards: [
        { id: 'b-1', name: 'My Board', description: null, updatedAt: '2026-01-01T00:00:00Z' },
      ],
    },
    recommendedActions: [],
    ...overrides,
  }
}

function makeTodaySummary(overrides: Partial<TodaySummary> = {}): TodaySummary {
  return {
    workspaceMode: 'guided',
    onboarding: makeOnboarding(),
    summary: {
      capturesNeedingTriage: 1,
      proposalsPendingReview: 1,
      overdueCards: 2,
      dueTodayCards: 3,
      blockedCards: 0,
    },
    overdueCards: [],
    dueTodayCards: [],
    blockedCards: [],
    recommendedActions: [],
    ...overrides,
  }
}

describe('workspaceStore — integration (real workspaceApi, mocked HTTP)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    localStorage.clear()
  })

  // ── hydratePreferences ────────────────────────────────────────────────────

  describe('hydratePreferences', () => {
    it('calls GET /workspace/preferences and applies mode and onboarding to state', async () => {
      vi.mocked(http.get).mockResolvedValue({ data: makePreferencePayload('workbench') })

      const store = useWorkspaceStore()
      await store.hydratePreferences()

      expect(store.mode).toBe('workbench')
      expect(store.preferencesHydrated).toBe(true)
      expect(store.onboarding?.currentStepId).toBe('create-first-board')
      expect(localStorage.getItem(WORKSPACE_MODE_STORAGE_KEY)).toBe('workbench')
      expect(http.get).toHaveBeenCalledWith(expect.stringContaining('/workspace/preferences'))
    })

    it('does not set preferencesHydrated to true when GET /workspace/preferences fails', async () => {
      vi.mocked(http.get).mockRejectedValue(new Error('Network Error'))

      const store = useWorkspaceStore()
      await store.hydratePreferences()

      expect(store.preferencesHydrated).toBe(false)
      // getErrorMessage extracts the Error's .message if present; fallback only used when the
      // error has no message. The raw Error('Network Error') propagates its .message field.
      expect(store.preferenceError).toBe('Network Error')
    })
  })

  // ── updateMode ────────────────────────────────────────────────────────────

  describe('updateMode', () => {
    it('immediately applies the new mode locally then confirms via PUT /workspace/preferences', async () => {
      const updated = makePreferencePayload('agent')
      vi.mocked(http.put).mockResolvedValue({ data: updated })

      const store = useWorkspaceStore()
      const promise = store.updateMode('agent')

      // Local mode must be applied before the PUT resolves
      expect(store.mode).toBe('agent')
      await promise

      expect(store.mode).toBe('agent')
      expect(store.preferencesHydrated).toBe(true)
      expect(http.put).toHaveBeenCalledWith(
        expect.stringContaining('/workspace/preferences'),
        expect.objectContaining({ workspaceMode: 'agent' }),
      )
    })

    it('keeps the locally-applied mode even when PUT /workspace/preferences fails', async () => {
      vi.mocked(http.put).mockRejectedValue(new Error('save failed'))

      const store = useWorkspaceStore()
      await store.updateMode('workbench')

      // Local mode must still be applied
      expect(store.mode).toBe('workbench')
      expect(store.preferenceError).toBe('save failed')
      expect(store.preferencesHydrated).toBe(false)
    })
  })

  // ── fetchHomeSummary ──────────────────────────────────────────────────────

  describe('fetchHomeSummary', () => {
    it('calls GET /workspace/home and maps payload into store state', async () => {
      const summary = makeHomeSummary()
      vi.mocked(http.get).mockResolvedValue({ data: summary })

      const store = useWorkspaceStore()
      await store.fetchHomeSummary()

      expect(store.homeSummary?.workload.capturesNeedingTriage).toBe(3)
      expect(store.homeSummary?.workload.proposalsPendingReview).toBe(2)
      expect(store.inboxBadgeCount).toBe(3)
      expect(store.reviewBadgeCount).toBe(2)
      expect(store.mode).toBe('guided')
      expect(store.homeLoading).toBe(false)
      expect(store.homeError).toBeNull()
      expect(http.get).toHaveBeenCalledWith('/workspace/home')
    })

    it('sets homeError and rethrows when GET /workspace/home fails', async () => {
      vi.mocked(http.get).mockRejectedValue(new Error('Network Error'))

      const store = useWorkspaceStore()
      await expect(store.fetchHomeSummary()).rejects.toBeInstanceOf(Error)

      // getErrorMessage propagates the Error's .message; fallback only for structureless errors
      expect(store.homeError).toBe('Network Error')
      expect(store.homeLoading).toBe(false)
    })

    it('syncs onboarding from home summary payload into store.onboarding', async () => {
      const summary = makeHomeSummary({
        onboarding: makeOnboarding({ isComplete: true, currentStepId: null }),
      })
      vi.mocked(http.get).mockResolvedValue({ data: summary })

      const store = useWorkspaceStore()
      await store.fetchHomeSummary()

      expect(store.onboarding?.isComplete).toBe(true)
      expect(store.onboarding?.currentStepId).toBeNull()
    })
  })

  // ── fetchTodaySummary ─────────────────────────────────────────────────────

  describe('fetchTodaySummary', () => {
    it('calls GET /workspace/today and populates todaySummary', async () => {
      const summary = makeTodaySummary()
      vi.mocked(http.get).mockResolvedValue({ data: summary })

      const store = useWorkspaceStore()
      await store.fetchTodaySummary()

      expect(store.todaySummary?.summary.overdueCards).toBe(2)
      expect(store.todaySummary?.summary.dueTodayCards).toBe(3)
      expect(store.todayLoading).toBe(false)
      expect(store.todayError).toBeNull()
      expect(http.get).toHaveBeenCalledWith('/workspace/today')
    })

    it('sets todayError when GET /workspace/today fails', async () => {
      vi.mocked(http.get).mockRejectedValue(new Error('Network Error'))

      const store = useWorkspaceStore()
      await expect(store.fetchTodaySummary()).rejects.toBeInstanceOf(Error)

      // getErrorMessage propagates Error.message; fallback only for structureless rejections
      expect(store.todayError).toBe('Network Error')
      expect(store.todayLoading).toBe(false)
    })
  })

  // ── updateOnboarding ──────────────────────────────────────────────────────

  describe('updateOnboarding', () => {
    it('calls PUT /workspace/onboarding and patches both home and today summaries', async () => {
      const dismissed = makeOnboarding({ visibility: 'dismissed', dismissedAt: '2026-02-01T00:00:00Z' })
      vi.mocked(http.put).mockResolvedValue({ data: dismissed })

      const store = useWorkspaceStore()
      store.homeSummary = makeHomeSummary()
      store.todaySummary = makeTodaySummary()

      await store.updateOnboarding('dismiss')

      expect(store.onboarding?.visibility).toBe('dismissed')
      expect(store.homeSummary?.onboarding.visibility).toBe('dismissed')
      expect(store.todaySummary?.onboarding.visibility).toBe('dismissed')
      expect(http.put).toHaveBeenCalledWith(
        expect.stringContaining('/workspace/onboarding'),
        expect.objectContaining({ action: 'dismiss' }),
      )
    })

    it('sets preferenceError and rethrows when PUT /workspace/onboarding fails', async () => {
      vi.mocked(http.put).mockRejectedValue(new Error('save failed'))

      const store = useWorkspaceStore()
      await expect(store.updateOnboarding('replay')).rejects.toBeInstanceOf(Error)

      // getErrorMessage propagates Error.message when present
      expect(store.preferenceError).toBe('save failed')
    })
  })

  // ── computed badge counts ─────────────────────────────────────────────────

  describe('badge count computed properties', () => {
    it('returns zero badge counts before home summary is loaded', () => {
      const store = useWorkspaceStore()

      expect(store.inboxBadgeCount).toBe(0)
      expect(store.reviewBadgeCount).toBe(0)
    })

    it('reflects workload numbers from the home summary payload', async () => {
      const summary = makeHomeSummary({
        workload: {
          capturesNeedingTriage: 7,
          capturesInProgress: 0,
          capturesReadyForFollowUp: 0,
          proposalsPendingReview: 4,
        },
      })
      vi.mocked(http.get).mockResolvedValue({ data: summary })

      const store = useWorkspaceStore()
      await store.fetchHomeSummary()

      expect(store.inboxBadgeCount).toBe(7)
      expect(store.reviewBadgeCount).toBe(4)
    })
  })

  // ── resetForLogout ────────────────────────────────────────────────────────

  describe('resetForLogout', () => {
    it('clears all summaries, onboarding, and hydrated flag', async () => {
      const summary = makeHomeSummary()
      vi.mocked(http.get).mockResolvedValue({ data: summary })

      const store = useWorkspaceStore()
      await store.fetchHomeSummary()
      expect(store.homeSummary).not.toBeNull()

      store.resetForLogout()

      expect(store.homeSummary).toBeNull()
      expect(store.todaySummary).toBeNull()
      expect(store.onboarding).toBeNull()
      expect(store.preferencesHydrated).toBe(false)
      expect(store.preferenceError).toBeNull()
    })
  })
})
