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

      // Writes confirm, never re-apply (issue #1343): the action's visibility
      // intent applies optimistically from the known local base; the response's
      // server-computed detail (currentStepId) does NOT re-apply. Server truth
      // arrives via the next clean read.
      expect(store.onboarding?.visibility).toBe('active')
      expect(store.onboarding?.currentStepId).toBe('create-first-board')
      expect(store.todaySummary?.onboarding.visibility).toBe('active')

      vi.mocked(http.get).mockResolvedValue({
        data: makeTodaySummary({ onboarding: replayed }),
      })
      await store.fetchTodaySummary()
      expect(store.onboarding?.currentStepId).toBe('step-2')
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
      // Summary data still applied (mode AND onboarding are ordering-guarded),
      // but the failed save's hydration state is not clobbered back to true by
      // the late summary.
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

  // ── unsaved local choice survives SUBSEQUENT summaries (issue #1343, FIX A) ─
  // After a FAILED save, the preference version is stable while the server still
  // holds the old mode. Summaries fetched AFTER the failure capture the current
  // version, so the version guard alone cannot block them — the session-scoped
  // unsaved-choice flag must.

  describe('failed save keeps the local choice against subsequent summaries', () => {
    it('keeps the unsaved mode when summaries fetched AFTER the failed save resolve', async () => {
      vi.mocked(http.put).mockRejectedValue(new Error('save failed'))

      const store = useWorkspaceStore()
      await store.updateMode('workbench')
      expect(store.mode).toBe('workbench')

      // Fresh fetches after the failure: version is current, server still 'guided'.
      vi.mocked(http.get)
        .mockResolvedValueOnce({ data: makeHomeSummary({ workspaceMode: 'guided' }) })
        .mockResolvedValueOnce({ data: makeTodaySummary({ workspaceMode: 'guided' }) })

      await store.fetchHomeSummary()
      expect(store.mode).toBe('workbench')
      expect(store.preferencesHydrated).toBe(false)
      expect(localStorage.getItem(WORKSPACE_MODE_STORAGE_KEY)).toBe('workbench')

      await store.fetchTodaySummary()
      expect(store.mode).toBe('workbench')
      // Summary data itself still lands (only preference state is guarded).
      expect(store.homeSummary).not.toBeNull()
      expect(store.todaySummary).not.toBeNull()
    })

    it('clears the unsaved flag when a later save succeeds so newer summaries apply again', async () => {
      vi.mocked(http.put).mockRejectedValueOnce(new Error('save failed'))

      const store = useWorkspaceStore()
      await store.updateMode('workbench')

      vi.mocked(http.put).mockResolvedValueOnce({ data: makePreferencePayload('workbench') })
      await store.updateMode('workbench')

      vi.mocked(http.get).mockResolvedValue({ data: makeHomeSummary({ workspaceMode: 'agent' }) })
      await store.fetchHomeSummary()

      expect(store.mode).toBe('agent')
      expect(store.preferencesHydrated).toBe(true)
    })

    it('hydratePreferences keeps the unsaved local mode while the server still disagrees', async () => {
      vi.mocked(http.put).mockRejectedValue(new Error('save failed'))

      const store = useWorkspaceStore()
      await store.updateMode('workbench')

      vi.mocked(http.get).mockResolvedValueOnce({ data: makePreferencePayload('guided') })
      await store.hydratePreferences()

      expect(store.mode).toBe('workbench')
      expect(store.preferencesHydrated).toBe(false)
      // Onboarding has no unsaved local state, so the fresh server copy applies.
      expect(store.onboarding).not.toBeNull()
    })

    it('hydratePreferences confirming the local mode clears the unsaved flag', async () => {
      vi.mocked(http.put).mockRejectedValue(new Error('save failed'))

      const store = useWorkspaceStore()
      await store.updateMode('workbench')

      // Server now matches the local choice (e.g. the save actually committed
      // despite the error response, or another client saved the same mode).
      vi.mocked(http.get).mockResolvedValueOnce({ data: makePreferencePayload('workbench') })
      await store.hydratePreferences()
      expect(store.mode).toBe('workbench')
      expect(store.preferencesHydrated).toBe(true)

      // Flag cleared: a genuinely newer summary applies server truth again.
      vi.mocked(http.get).mockResolvedValueOnce({ data: makeHomeSummary({ workspaceMode: 'agent' }) })
      await store.fetchHomeSummary()
      expect(store.mode).toBe('agent')
    })

    it('rejects a summary resolving while the preference save is still in flight', async () => {
      // The summary starts AFTER the save began, so its captured version is
      // current — but its payload may predate the save's server-side commit.
      // The pending-preference-request check at apply time must reject it; the
      // save's own resolution applies the authoritative state.
      let resolveSave!: (value: unknown) => void
      vi.mocked(http.put).mockReturnValueOnce(
        new Promise<unknown>((resolve) => { resolveSave = resolve }),
      )
      vi.mocked(http.get).mockResolvedValue({ data: makeHomeSummary({ workspaceMode: 'guided' }) })

      const store = useWorkspaceStore()
      const saveRequest = store.updateMode('workbench')
      expect(store.mode).toBe('workbench')

      await store.fetchHomeSummary()
      expect(store.mode).toBe('workbench')
      expect(store.preferencesHydrated).toBe(false)

      resolveSave({ data: makePreferencePayload('workbench') })
      await saveRequest
      expect(store.mode).toBe('workbench')
      expect(store.preferencesHydrated).toBe(true)
    })
  })

  // ── summaries that STARTED during a pending save (issue #1343, Codex P2#1) ──
  // A summary GET that begins after a save bumped the version but before the
  // PUT commits can read the OLD server mode. If the save then settles before
  // the summary resolves, version/pending/dirty are all clean at apply time —
  // only the pending-at-start capture can identify the summary as suspect.

  describe('summaries that started during a pending save', () => {
    it('rejects a Home summary that started during a save even when the save settles first', async () => {
      let resolveSave!: (value: unknown) => void
      let resolveHome!: (value: { data: HomeSummary }) => void
      vi.mocked(http.put).mockReturnValueOnce(
        new Promise<unknown>((resolve) => { resolveSave = resolve }),
      )
      vi.mocked(http.get).mockReturnValueOnce(
        new Promise<{ data: HomeSummary }>((resolve) => { resolveHome = resolve }),
      )

      const store = useWorkspaceStore()
      const saveRequest = store.updateMode('workbench')
      // Summary begins while the save is in flight: its server-side read may
      // predate the save's commit, so its payload carries 'guided'.
      const homeRequest = store.fetchHomeSummary()

      // The save settles FIRST: version stable, nothing pending, flag clean.
      resolveSave({ data: makePreferencePayload('workbench') })
      await saveRequest
      expect(store.mode).toBe('workbench')
      expect(store.preferencesHydrated).toBe(true)

      // The overlapped summary resolves LAST with the stale mode.
      resolveHome({ data: makeHomeSummary({ workspaceMode: 'guided' }) })
      await homeRequest

      expect(store.mode).toBe('workbench')
      expect(localStorage.getItem(WORKSPACE_MODE_STORAGE_KEY)).toBe('workbench')
      expect(store.homeSummary).not.toBeNull()
    })

    it('rejects a Today summary that started during a save even when the save settles first', async () => {
      let resolveSave!: (value: unknown) => void
      let resolveToday!: (value: { data: TodaySummary }) => void
      vi.mocked(http.put).mockReturnValueOnce(
        new Promise<unknown>((resolve) => { resolveSave = resolve }),
      )
      vi.mocked(http.get).mockReturnValueOnce(
        new Promise<{ data: TodaySummary }>((resolve) => { resolveToday = resolve }),
      )

      const store = useWorkspaceStore()
      const saveRequest = store.updateMode('workbench')
      const todayRequest = store.fetchTodaySummary()

      resolveSave({ data: makePreferencePayload('workbench') })
      await saveRequest
      expect(store.mode).toBe('workbench')

      resolveToday({ data: makeTodaySummary({ workspaceMode: 'guided' }) })
      await todayRequest

      expect(store.mode).toBe('workbench')
      expect(localStorage.getItem(WORKSPACE_MODE_STORAGE_KEY)).toBe('workbench')
      expect(store.todaySummary).not.toBeNull()
    })
  })

  // ── onboarding actions must not supersede mode saves (Codex P2#2) ──────────
  // The mode and onboarding guards are versioned per field: an onboarding
  // dismiss/replay that starts while a mode save is in flight must neither
  // suppress the save's success/failure handling nor be reverted by the save's
  // incidental onboarding echo.

  describe('onboarding actions do not supersede mode saves', () => {
    it('an onboarding action during a pending save does not suppress the failed-save handling', async () => {
      let rejectSave!: (reason: unknown) => void
      vi.mocked(http.put)
        .mockReturnValueOnce(
          new Promise<unknown>((_resolve, reject) => { rejectSave = reject }),
        )
        .mockResolvedValueOnce({ data: makeOnboarding({ visibility: 'dismissed' }) })

      const store = useWorkspaceStore()
      const saveRequest = store.updateMode('workbench')

      // User dismisses the setup guide while the save is still in flight
      // (reachable: the topbar fires updateMode un-awaited and Home setup
      // actions stay clickable during the retry window).
      await store.updateOnboarding('dismiss')
      expect(store.onboarding?.visibility).toBe('dismissed')

      // The save now fails. Its handling must still run: error + unsaved flag.
      rejectSave(new Error('save failed'))
      await saveRequest
      expect(store.preferenceError).toBe('save failed')
      expect(store.preferencesHydrated).toBe(false)

      // Because the flag was set, a subsequent stale-mode summary cannot revert.
      vi.mocked(http.get).mockResolvedValue({
        data: makeHomeSummary({
          workspaceMode: 'guided',
          onboarding: makeOnboarding({ visibility: 'dismissed' }),
        }),
      })
      await store.fetchHomeSummary()
      expect(store.mode).toBe('workbench')
      expect(store.onboarding?.visibility).toBe('dismissed')
    })

    it('a mode save settling after an onboarding dismissal keeps both results', async () => {
      let resolveSave!: (value: unknown) => void
      vi.mocked(http.put)
        .mockReturnValueOnce(
          new Promise<unknown>((resolve) => { resolveSave = resolve }),
        )
        .mockResolvedValueOnce({ data: makeOnboarding({ visibility: 'dismissed' }) })

      const store = useWorkspaceStore()
      const saveRequest = store.updateMode('workbench')
      await store.updateOnboarding('dismiss')

      // The save's response echoes onboarding as of ITS commit — before the
      // dismissal. The echo must not revert the newer explicit dismissal, but
      // the save's own mode confirmation must still land.
      resolveSave({ data: makePreferencePayload('workbench') })
      await saveRequest

      expect(store.mode).toBe('workbench')
      expect(store.preferencesHydrated).toBe(true)
      expect(store.onboarding?.visibility).toBe('dismissed')
    })
  })

  // ── writes confirm, never re-apply (issue #1343, round 4) ──────────────────
  // Write responses never write field values back into the store, so no echo
  // can cross fields or revert newer intent; each field's read guard is fully
  // independent of the other field's in-flight writes.

  describe('write responses confirm without re-applying', () => {
    it('a mode save echo cannot revert an onboarding action that was already in flight', async () => {
      // The onboarding action starts BEFORE the mode save, so the save's echo
      // reflects pre-dismissal server state. Once the dismissal settles, the
      // later-resolving save must not resurrect the old onboarding.
      let resolveDismiss!: (value: unknown) => void
      let resolveSave!: (value: unknown) => void
      vi.mocked(http.put)
        .mockReturnValueOnce(
          new Promise<unknown>((resolve) => { resolveDismiss = resolve }),
        )
        .mockReturnValueOnce(
          new Promise<unknown>((resolve) => { resolveSave = resolve }),
        )

      const store = useWorkspaceStore()
      store.homeSummary = makeHomeSummary() // local onboarding base ('active')

      const dismissRequest = store.updateOnboarding('dismiss')
      const saveRequest = store.updateMode('workbench')

      resolveDismiss({ data: makeOnboarding({ visibility: 'dismissed' }) })
      await dismissRequest
      expect(store.onboarding?.visibility).toBe('dismissed')

      // The save resolves LAST; its payload carries pre-dismissal onboarding.
      resolveSave({ data: makePreferencePayload('workbench') })
      await saveRequest

      expect(store.mode).toBe('workbench')
      expect(store.preferencesHydrated).toBe(true)
      expect(store.onboarding?.visibility).toBe('dismissed')
      expect(store.homeSummary?.onboarding.visibility).toBe('dismissed')
    })

    it('a later failed onboarding action is not clobbered by an earlier overlapping success', async () => {
      let resolveFirst!: (value: unknown) => void
      let rejectSecond!: (reason: unknown) => void
      vi.mocked(http.put)
        .mockReturnValueOnce(
          new Promise<unknown>((resolve) => { resolveFirst = resolve }),
        )
        .mockReturnValueOnce(
          new Promise<unknown>((_resolve, reject) => { rejectSecond = reject }),
        )

      const store = useWorkspaceStore()
      store.homeSummary = makeHomeSummary() // local onboarding base ('active')

      const dismiss = store.updateOnboarding('dismiss') // intent: dismissed
      const replay = store.updateOnboarding('replay')   // latest intent: active

      resolveFirst({ data: makeOnboarding({ visibility: 'dismissed' }) })
      await dismiss
      // The earlier success must not re-apply its result over the later intent.
      expect(store.onboarding?.visibility).toBe('active')

      rejectSecond(new Error('replay failed'))
      await expect(replay).rejects.toThrow('replay failed')
      expect(store.preferenceError).toBe('replay failed')
      // The latest local intent stands (kept + flagged unsaved).
      expect(store.onboarding?.visibility).toBe('active')

      // And the unsaved intent survives a subsequent summary carrying the
      // server's actual state (dismissed — the first action committed).
      vi.mocked(http.get).mockResolvedValue({
        data: makeHomeSummary({ onboarding: makeOnboarding({ visibility: 'dismissed' }) }),
      })
      await store.fetchHomeSummary()
      expect(store.onboarding?.visibility).toBe('active')
    })

    it('a pending onboarding action does not block a summary from hydrating mode', async () => {
      let resolveDismiss!: (value: unknown) => void
      vi.mocked(http.put).mockReturnValueOnce(
        new Promise<unknown>((resolve) => { resolveDismiss = resolve }),
      )
      vi.mocked(http.get).mockResolvedValue({ data: makeHomeSummary({ workspaceMode: 'agent' }) })

      const store = useWorkspaceStore()
      store.todaySummary = makeTodaySummary() // local onboarding base
      const dismiss = store.updateOnboarding('dismiss') // onboarding write pending

      // The summary starts and resolves entirely during the onboarding write.
      await store.fetchHomeSummary()

      // Per-field guard: the pending ONBOARDING write must not block MODE.
      expect(store.mode).toBe('agent')
      expect(store.preferencesHydrated).toBe(true)
      // The summary's onboarding overlapped the write and is blocked; the
      // local dismissal intent stays visible.
      expect(store.onboarding?.visibility).toBe('dismissed')

      resolveDismiss({ data: makeOnboarding({ visibility: 'dismissed' }) })
      await dismiss
      expect(store.onboarding?.visibility).toBe('dismissed')
    })
  })

  // ── stale summaries cannot revert onboarding either (issue #1343, FIX B) ───
  // Guarding only the mode would let a stale summary revert a newer onboarding
  // change (a dismissed setup guide reappearing) and make mode/onboarding
  // diverge. Explicit onboarding actions version the same guard.

  describe('late summary cannot overwrite newer onboarding state', () => {
    it('stale summary cannot revert a newer onboarding dismissal', async () => {
      let resolveHome!: (value: { data: HomeSummary }) => void
      vi.mocked(http.get).mockReturnValueOnce(
        new Promise<{ data: HomeSummary }>((resolve) => { resolveHome = resolve }),
      )
      vi.mocked(http.put).mockResolvedValue({ data: makeOnboarding({ visibility: 'dismissed' }) })

      const store = useWorkspaceStore()
      const homeRequest = store.fetchHomeSummary()

      // User dismisses the setup guide while the Home request is in flight.
      await store.updateOnboarding('dismiss')
      expect(store.onboarding?.visibility).toBe('dismissed')

      resolveHome({
        data: makeHomeSummary({ onboarding: makeOnboarding({ visibility: 'active' }) }),
      })
      await homeRequest

      // The dismissal survives, and the stored summary is patched to match the
      // guarded onboarding rather than carrying the stale 'active' copy.
      expect(store.onboarding?.visibility).toBe('dismissed')
      expect(store.homeSummary?.onboarding.visibility).toBe('dismissed')
    })

    it('a stale summary blocks both mode and onboarding after a dismissal and a failed save (no divergence)', async () => {
      // The summary starts FIRST, so its payload legitimately predates both the
      // dismissal and the (failed) mode save: it carries 'active' + 'guided'.
      let resolveHome!: (value: { data: HomeSummary }) => void
      vi.mocked(http.get).mockReturnValueOnce(
        new Promise<{ data: HomeSummary }>((resolve) => { resolveHome = resolve }),
      )
      vi.mocked(http.put)
        .mockResolvedValueOnce({ data: makeOnboarding({ visibility: 'dismissed' }) })
        .mockRejectedValueOnce(new Error('save failed'))

      const store = useWorkspaceStore()
      const homeRequest = store.fetchHomeSummary()

      await store.updateOnboarding('dismiss')
      await store.updateMode('workbench')

      resolveHome({
        data: makeHomeSummary({
          workspaceMode: 'guided',
          onboarding: makeOnboarding({ visibility: 'active' }),
        }),
      })
      await homeRequest

      // Neither half of the preference state reverts: no mode/onboarding divergence.
      expect(store.mode).toBe('workbench')
      expect(store.onboarding?.visibility).toBe('dismissed')
      expect(store.homeSummary?.onboarding.visibility).toBe('dismissed')
    })

    it('a fresh summary after a failed save applies current onboarding while the unsaved mode survives', async () => {
      // Per-field independence: the failed MODE save must not freeze onboarding
      // sync. A summary fetched after the failure carries the server's CURRENT
      // onboarding (the dismissal committed) — it applies — while its stale
      // mode is still blocked by the unsaved-choice flag.
      vi.mocked(http.put)
        .mockResolvedValueOnce({ data: makeOnboarding({ visibility: 'dismissed' }) })
        .mockRejectedValueOnce(new Error('save failed'))

      const store = useWorkspaceStore()
      await store.updateOnboarding('dismiss')
      await store.updateMode('workbench')

      vi.mocked(http.get).mockResolvedValue({
        data: makeHomeSummary({
          workspaceMode: 'guided',
          onboarding: makeOnboarding({ visibility: 'dismissed', currentStepId: 'step-3' }),
        }),
      })
      await store.fetchHomeSummary()

      expect(store.mode).toBe('workbench')
      expect(store.preferencesHydrated).toBe(false)
      // The genuinely-newer onboarding copy applied.
      expect(store.onboarding?.currentStepId).toBe('step-3')
    })
  })

  // ── Today dual-guard combinations (issue #1343, FIX C) ─────────────────────
  // fetchTodaySummary carries two guards: todayRequestVersion (Today-vs-Today
  // ordering, #1333) and the preference guard. Pin each combination.

  describe('Today dual-guard combinations', () => {
    it('applies nothing when todayRequestVersion is stale even though the preference version is current', async () => {
      let resolveOld!: (value: { data: TodaySummary }) => void
      vi.mocked(http.get).mockImplementationOnce(
        () => new Promise((resolve) => { resolveOld = resolve }),
      )

      const store = useWorkspaceStore()
      const oldRequest = store.fetchTodaySummary()
      // Bumps todayRequestVersion; the preference version is untouched.
      store.clearTodaySummary()

      resolveOld({
        data: makeTodaySummary({
          workspaceMode: 'agent',
          onboarding: makeOnboarding({ visibility: 'dismissed' }),
        }),
      })
      await oldRequest

      expect(store.mode).toBe('guided')
      expect(store.todaySummary).toBeNull()
      expect(store.onboarding).toBeNull()
    })

    it('applies nothing when both todayRequestVersion and the preference version are stale', async () => {
      let resolveOld!: (value: { data: TodaySummary }) => void
      vi.mocked(http.get).mockImplementationOnce(
        () => new Promise((resolve) => { resolveOld = resolve }),
      )
      vi.mocked(http.put).mockResolvedValue({ data: makePreferencePayload('workbench') })

      const store = useWorkspaceStore()
      const oldRequest = store.fetchTodaySummary()
      store.clearTodaySummary()
      await store.updateMode('workbench')

      resolveOld({
        data: makeTodaySummary({
          workspaceMode: 'agent',
          onboarding: makeOnboarding({ visibility: 'dismissed' }),
        }),
      })
      await oldRequest

      expect(store.mode).toBe('workbench')
      expect(store.todaySummary).toBeNull()
      // Nothing has synced onboarding: the stale summary is discarded wholesale
      // and (round 4) the save's response echo never applies field values.
      expect(store.onboarding).toBeNull()
    })
  })
})
