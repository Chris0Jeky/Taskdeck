/**
 * workspaceStore — mode persistence, concurrent preference requests, and
 * summary clearing integration tests.
 *
 * These tests exercise:
 * - localStorage persistence of workspace mode
 * - Concurrent/overlapping preference requests (version guards)
 * - clearHomeSummary / clearTodaySummary behavior
 * - Mode fallback when localStorage contains invalid values
 * - Loading state during multiple overlapping requests
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
      capturesNeedingTriage: 0,
      capturesInProgress: 0,
      capturesReadyForFollowUp: 0,
      proposalsPendingReview: 0,
    },
    boards: {
      totalBoards: 1,
      recentBoardsCount: 1,
      recentBoards: [],
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

describe('workspaceStore — mode persistence and extended scenarios', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    localStorage.clear()
  })

  // ── localStorage mode persistence ─────────────────────────────────────────

  describe('mode persistence in localStorage', () => {
    it('persists mode to localStorage when updateMode is called', async () => {
      vi.mocked(http.put).mockResolvedValue({ data: makePreferencePayload('workbench') })

      const store = useWorkspaceStore()
      await store.updateMode('workbench')

      expect(localStorage.getItem(WORKSPACE_MODE_STORAGE_KEY)).toBe('workbench')
    })

    it('reads mode from localStorage on store initialization', () => {
      localStorage.setItem(WORKSPACE_MODE_STORAGE_KEY, 'agent')

      const store = useWorkspaceStore()

      expect(store.mode).toBe('agent')
    })

    it('defaults to guided when localStorage contains an invalid mode string', () => {
      localStorage.setItem(WORKSPACE_MODE_STORAGE_KEY, 'invalid-mode')

      const store = useWorkspaceStore()

      expect(store.mode).toBe('guided')
    })

    it('defaults to guided when localStorage is empty', () => {
      const store = useWorkspaceStore()

      expect(store.mode).toBe('guided')
    })

    it('persists mode from hydratePreferences response', async () => {
      vi.mocked(http.get).mockResolvedValue({ data: makePreferencePayload('agent') })

      const store = useWorkspaceStore()
      await store.hydratePreferences()

      expect(localStorage.getItem(WORKSPACE_MODE_STORAGE_KEY)).toBe('agent')
    })
  })

  // ── concurrent preference requests (version guard) ────────────────────────

  describe('concurrent preference requests', () => {
    it('only applies the most recent updateMode response when calls overlap', async () => {
      const store = useWorkspaceStore()
      let firstResolve!: (val: unknown) => void
      let secondResolve!: (val: unknown) => void

      vi.mocked(http.put)
        .mockReturnValueOnce(
          new Promise<unknown>((resolve) => { firstResolve = resolve }),
        )
        .mockReturnValueOnce(
          new Promise<unknown>((resolve) => { secondResolve = resolve }),
        )

      // Fire two concurrent updateMode calls
      const first = store.updateMode('workbench')
      const second = store.updateMode('agent')

      // Local mode should reflect the latest call immediately
      expect(store.mode).toBe('agent')

      // Resolve the second (latest) first
      secondResolve({ data: makePreferencePayload('agent') })
      await Promise.resolve()
      await Promise.resolve()

      // Resolve the first (stale) after
      firstResolve({ data: makePreferencePayload('workbench') })

      await first
      await second

      // The stale response should be discarded by the version guard
      expect(store.mode).toBe('agent')
      expect(localStorage.getItem(WORKSPACE_MODE_STORAGE_KEY)).toBe('agent')
    })
  })

  // ── clearHomeSummary / clearTodaySummary ──────────────────────────────────

  describe('clearHomeSummary', () => {
    it('resets homeSummary and homeError to null', async () => {
      vi.mocked(http.get).mockResolvedValue({ data: makeHomeSummary() })

      const store = useWorkspaceStore()
      await store.fetchHomeSummary()
      expect(store.homeSummary).not.toBeNull()

      store.clearHomeSummary()

      expect(store.homeSummary).toBeNull()
      expect(store.homeError).toBeNull()
    })

    it('resets badge counts to zero after clearing home summary', async () => {
      vi.mocked(http.get).mockResolvedValue({
        data: makeHomeSummary({
          workload: {
            capturesNeedingTriage: 5,
            capturesInProgress: 0,
            capturesReadyForFollowUp: 0,
            proposalsPendingReview: 3,
          },
        }),
      })

      const store = useWorkspaceStore()
      await store.fetchHomeSummary()
      expect(store.inboxBadgeCount).toBe(5)

      store.clearHomeSummary()

      expect(store.inboxBadgeCount).toBe(0)
      expect(store.reviewBadgeCount).toBe(0)
    })
  })

  describe('clearTodaySummary', () => {
    it('resets todaySummary and todayError to null', async () => {
      vi.mocked(http.get).mockResolvedValue({ data: makeTodaySummary() })

      const store = useWorkspaceStore()
      await store.fetchTodaySummary()
      expect(store.todaySummary).not.toBeNull()

      store.clearTodaySummary()

      expect(store.todaySummary).toBeNull()
      expect(store.todayError).toBeNull()
    })
  })

  // ── home/today loading state ──────────────────────────────────────────────

  describe('homeLoading transitions', () => {
    it('sets homeLoading=true during fetchHomeSummary and clears after', async () => {
      let loadingDuringFetch = false
      vi.mocked(http.get).mockImplementation(async () => {
        const store = useWorkspaceStore()
        loadingDuringFetch = store.homeLoading
        return { data: makeHomeSummary() }
      })

      const store = useWorkspaceStore()
      await store.fetchHomeSummary()

      expect(loadingDuringFetch).toBe(true)
      expect(store.homeLoading).toBe(false)
    })

    it('clears homeLoading even when fetchHomeSummary fails', async () => {
      vi.mocked(http.get).mockRejectedValue(new Error('timeout'))

      const store = useWorkspaceStore()
      await expect(store.fetchHomeSummary()).rejects.toBeInstanceOf(Error)

      expect(store.homeLoading).toBe(false)
    })
  })

  describe('todayLoading transitions', () => {
    it('sets todayLoading=true during fetchTodaySummary and clears after', async () => {
      let loadingDuringFetch = false
      vi.mocked(http.get).mockImplementation(async () => {
        const store = useWorkspaceStore()
        loadingDuringFetch = store.todayLoading
        return { data: makeTodaySummary() }
      })

      const store = useWorkspaceStore()
      await store.fetchTodaySummary()

      expect(loadingDuringFetch).toBe(true)
      expect(store.todayLoading).toBe(false)
    })
  })

  // ── fetchHomeSummary syncs mode from server ─────────────────────────────

  describe('fetchHomeSummary mode sync', () => {
    it('overrides local mode with the mode from home summary', async () => {
      localStorage.setItem(WORKSPACE_MODE_STORAGE_KEY, 'guided')

      vi.mocked(http.get).mockResolvedValue({
        data: makeHomeSummary({ workspaceMode: 'agent' }),
      })

      const store = useWorkspaceStore()
      expect(store.mode).toBe('guided')

      await store.fetchHomeSummary()

      expect(store.mode).toBe('agent')
      expect(store.preferencesHydrated).toBe(true)
      expect(localStorage.getItem(WORKSPACE_MODE_STORAGE_KEY)).toBe('agent')
    })
  })

  // ── resetForLogout ────────────────────────────────────────────────────────

  describe('resetForLogout preserves localStorage mode', () => {
    it('restores mode from localStorage after reset', async () => {
      localStorage.setItem(WORKSPACE_MODE_STORAGE_KEY, 'workbench')

      vi.mocked(http.get).mockResolvedValue({
        data: makeHomeSummary({ workspaceMode: 'agent' }),
      })

      const store = useWorkspaceStore()
      await store.fetchHomeSummary()
      expect(store.mode).toBe('agent')

      // fetchHomeSummary calls applyMode('agent') which persists 'agent' to localStorage,
      // overwriting the original 'workbench' value. So after resetForLogout, mode reads
      // from localStorage which now contains 'agent'.
      store.resetForLogout()

      expect(store.mode).toBe('agent')
      expect(store.homeSummary).toBeNull()
      expect(store.todaySummary).toBeNull()
      expect(store.preferencesHydrated).toBe(false)
    })

    it('falls back to localStorage mode that was not overwritten by server sync', async () => {
      localStorage.setItem(WORKSPACE_MODE_STORAGE_KEY, 'workbench')

      const store = useWorkspaceStore()
      expect(store.mode).toBe('workbench')

      // Directly change in-memory mode without persisting (simulating a partial state)
      // Actually, updateMode persists, so we test the normal flow:
      // the store initializes from localStorage, and resetForLogout re-reads it.
      store.resetForLogout()

      expect(store.mode).toBe('workbench')
      expect(localStorage.getItem(WORKSPACE_MODE_STORAGE_KEY)).toBe('workbench')
    })
  })

  // ── onboarding sync across summaries ──────────────────────────────────────

  describe('onboarding sync across home and today summaries', () => {
    it('updateOnboarding patches both home and today summaries simultaneously', async () => {
      const store = useWorkspaceStore()
      store.homeSummary = makeHomeSummary()
      store.todaySummary = makeTodaySummary()

      const dismissed = makeOnboarding({ visibility: 'dismissed' })
      vi.mocked(http.put).mockResolvedValue({ data: dismissed })

      await store.updateOnboarding('dismiss')

      expect(store.onboarding?.visibility).toBe('dismissed')
      expect(store.homeSummary?.onboarding.visibility).toBe('dismissed')
      expect(store.todaySummary?.onboarding.visibility).toBe('dismissed')
    })

    it('updateOnboarding does not throw when home summary is null', async () => {
      const store = useWorkspaceStore()
      store.homeSummary = null
      store.todaySummary = makeTodaySummary()

      const replayed = makeOnboarding({ visibility: 'active', currentStepId: 'step-2' })
      vi.mocked(http.put).mockResolvedValue({ data: replayed })

      await store.updateOnboarding('replay')

      expect(store.onboarding?.currentStepId).toBe('step-2')
      expect(store.todaySummary?.onboarding.currentStepId).toBe('step-2')
    })
  })

  // ── late summary vs. newer local mode choice (issue #1343) ────────────────
  // A Home/Today summary request that started before a newer local mode choice
  // (updateMode) must not overwrite that choice when it resolves afterward.
  // Genuinely newer summaries — those that start after the local action has
  // settled — must still apply server truth. Ordering is made deterministic by
  // controlling exactly when each request resolves, so these are not timing
  // races: they exercise the store's version guard directly.

  describe('late summary cannot overwrite a newer local mode choice', () => {
    it('keeps a failed-save mode choice when a late Home summary resolves afterward', async () => {
      // Home request begins first (carries the stale server mode 'guided').
      let resolveHome!: (value: { data: HomeSummary }) => void
      vi.mocked(http.get).mockReturnValueOnce(
        new Promise<{ data: HomeSummary }>((resolve) => { resolveHome = resolve }),
      )
      // The user's preference save then fails, so the local selection is kept.
      vi.mocked(http.put).mockRejectedValue(new Error('Failed to save workspace preferences'))

      const store = useWorkspaceStore()
      const homeRequest = store.fetchHomeSummary()

      // User picks 'workbench' while the Home request is still in flight.
      await store.updateMode('workbench')
      expect(store.mode).toBe('workbench')
      // Failed save keeps the local selection and records the error (drives the warning).
      expect(store.preferenceError).toBe('Failed to save workspace preferences')
      expect(store.preferencesHydrated).toBe(false)

      // The older Home response finally resolves carrying the stale 'guided' mode.
      resolveHome({ data: makeHomeSummary({ workspaceMode: 'guided' }) })
      await homeRequest

      // The newer local choice survives; the late summary does not overwrite it.
      expect(store.mode).toBe('workbench')
      expect(localStorage.getItem(WORKSPACE_MODE_STORAGE_KEY)).toBe('workbench')
      // Summary data still applied (only mode is ordering-guarded), but the failed
      // save's hydration state is not clobbered back to true by the late summary.
      expect(store.homeSummary).not.toBeNull()
      expect(store.preferencesHydrated).toBe(false)
    })

    it('keeps a newer mode choice when a late Today summary resolves afterward', async () => {
      let resolveToday!: (value: { data: TodaySummary }) => void
      vi.mocked(http.get).mockReturnValueOnce(
        new Promise<{ data: TodaySummary }>((resolve) => { resolveToday = resolve }),
      )
      vi.mocked(http.put).mockResolvedValue({ data: makePreferencePayload('workbench') })

      const store = useWorkspaceStore()
      const todayRequest = store.fetchTodaySummary()

      await store.updateMode('workbench')
      expect(store.mode).toBe('workbench')

      resolveToday({ data: makeTodaySummary({ workspaceMode: 'guided' }) })
      await todayRequest

      expect(store.mode).toBe('workbench')
      expect(localStorage.getItem(WORKSPACE_MODE_STORAGE_KEY)).toBe('workbench')
      // Today summary data is still populated even though its stale mode was dropped.
      expect(store.todaySummary).not.toBeNull()
    })

    it('applies a Home summary mode that starts after the local choice has settled', async () => {
      // Opposite interleaving: the local choice is fully settled first, then a
      // Home summary begins — it is genuinely newer, so server truth applies.
      vi.mocked(http.put).mockResolvedValue({ data: makePreferencePayload('workbench') })

      const store = useWorkspaceStore()
      await store.updateMode('workbench')
      expect(store.mode).toBe('workbench')

      vi.mocked(http.get).mockResolvedValue({ data: makeHomeSummary({ workspaceMode: 'agent' }) })
      await store.fetchHomeSummary()

      expect(store.mode).toBe('agent')
      expect(store.preferencesHydrated).toBe(true)
      expect(localStorage.getItem(WORKSPACE_MODE_STORAGE_KEY)).toBe('agent')
    })

    it('applies a Today summary mode that starts after the local choice has settled', async () => {
      vi.mocked(http.put).mockResolvedValue({ data: makePreferencePayload('workbench') })

      const store = useWorkspaceStore()
      await store.updateMode('workbench')
      expect(store.mode).toBe('workbench')

      vi.mocked(http.get).mockResolvedValue({ data: makeTodaySummary({ workspaceMode: 'agent' }) })
      await store.fetchTodaySummary()

      expect(store.mode).toBe('agent')
      expect(localStorage.getItem(WORKSPACE_MODE_STORAGE_KEY)).toBe('agent')
    })

    it('drops a late Home mode even when the newer preference save succeeds', async () => {
      // Ordering guard must fire regardless of whether the concurrent save
      // succeeds or fails — it is the ordering, not the save outcome, that wins.
      let resolveHome!: (value: { data: HomeSummary }) => void
      vi.mocked(http.get).mockReturnValueOnce(
        new Promise<{ data: HomeSummary }>((resolve) => { resolveHome = resolve }),
      )
      vi.mocked(http.put).mockResolvedValue({ data: makePreferencePayload('workbench') })

      const store = useWorkspaceStore()
      const homeRequest = store.fetchHomeSummary()

      await store.updateMode('workbench')
      expect(store.mode).toBe('workbench')
      expect(store.preferencesHydrated).toBe(true)

      resolveHome({ data: makeHomeSummary({ workspaceMode: 'guided' }) })
      await homeRequest

      expect(store.mode).toBe('workbench')
      expect(localStorage.getItem(WORKSPACE_MODE_STORAGE_KEY)).toBe('workbench')
    })
  })
})
