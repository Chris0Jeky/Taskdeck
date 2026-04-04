/**
 * Tests for workspace mode and route stability (issue #687).
 *
 * Covers:
 * - Workspace mode persists in localStorage across simulated reloads/fresh navigation.
 * - Workspace mode does not silently drift when hydratePreferences resolves.
 * - resetForLogout() clears server-backed state and falls back to localStorage.
 * - fetchBoardMetrics does not affect workspace mode or current-route (path) stability.
 * - workspaceStore.$reset()-equivalent behavior on session expiry.
 * - Authenticated navigation to any workspace route never produces an unexpected redirect.
 */
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useWorkspaceStore } from '../../store/workspaceStore'
import { useMetricsStore } from '../../store/metricsStore'
import { WORKSPACE_MODE_STORAGE_KEY } from '../../utils/storageKeys'
import { workspaceApi } from '../../api/workspaceApi'
import { metricsApi } from '../../api/metricsApi'
import { isTokenExpired } from '../../utils/jwt'
import * as tokenStorage from '../../utils/tokenStorage'
import type { WorkspaceMode } from '../../types/workspace'

// ─── module mocks ─────────────────────────────────────────────────────────────

const toastMocks = vi.hoisted(() => ({
  warning: vi.fn(),
  error: vi.fn(),
  info: vi.fn(),
  success: vi.fn(),
}))

const sessionMock = vi.hoisted(() => ({
  isAuthenticated: true,
}))

vi.mock('../../api/workspaceApi', () => ({
  workspaceApi: {
    getPreferences: vi.fn(),
    updatePreferences: vi.fn(),
    getHomeSummary: vi.fn(),
    getTodaySummary: vi.fn(),
    updateOnboarding: vi.fn(),
  },
}))

vi.mock('../../api/metricsApi', () => ({
  metricsApi: {
    getBoardMetrics: vi.fn(),
  },
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => toastMocks,
}))

vi.mock('../../store/sessionStore', () => ({
  useSessionStore: () => sessionMock,
}))

// ─── helpers ──────────────────────────────────────────────────────────────────

const TEST_USER_ID = '00000000-0000-0000-0000-000000000001'

function toBase64Url(value: string): string {
  return btoa(value).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '')
}

function fakeJwt(expOffsetSeconds = 3600): string {
  const header = toBase64Url(JSON.stringify({ alg: 'HS256', typ: 'JWT' }))
  const exp = Math.floor(Date.now() / 1000) + expOffsetSeconds
  const payload = toBase64Url(JSON.stringify({ exp }))
  return `${header}.${payload}.fakesig`
}

function makePreferenceResponse(mode: WorkspaceMode) {
  return {
    userId: TEST_USER_ID,
    workspaceMode: mode,
    onboarding: {
      visibility: 'dismissed' as const,
      isComplete: true,
      currentStepId: null,
      dismissedAt: null,
      completedAt: null,
      steps: [],
    },
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
  }
}

function makeMetricsResponse() {
  return {
    boardId: 'board-1',
    from: '2026-01-01',
    to: '2026-01-31',
    throughput: [],
    averageCycleTimeDays: 3.2,
    cycleTimeEntries: [],
    wipSnapshots: [],
    totalWip: 0,
    blockedCount: 0,
    blockedCards: [],
  }
}

/**
 * Mirrors the auth guard decision logic from router/index.ts.
 * Kept inline so tests exercise the logic directly without router side-effects.
 */
function authGuardDecision(
  to: { path: string; fullPath: string; meta: { public?: boolean } },
  opts: { token: string | null; demoActive?: boolean },
): { path: string; query?: Record<string, string> } | undefined {
  const isPublic = to.meta.public === true
  const demoActive = opts.demoActive ?? false
  const token = opts.token
  const tokenValid = !!token && !isTokenExpired(token)
  const hasValidSession = tokenValid || demoActive

  if (token && !tokenValid) tokenStorage.clearAll()

  if (!isPublic && !hasValidSession && to.path.startsWith('/workspace')) {
    return { path: '/login', query: { redirect: to.fullPath } }
  }

  if (isPublic && hasValidSession && (to.path === '/login' || to.path === '/register')) {
    return { path: '/workspace/home' }
  }

  return undefined
}

// ─── tests ────────────────────────────────────────────────────────────────────

describe('workspace mode persistence across navigation (#687)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    sessionMock.isAuthenticated = true
    localStorage.clear()
  })

  // ── Reload simulation ──────────────────────────────────────────────────────

  describe('mode survives simulated page reload', () => {
    it('reads the persisted mode from localStorage on store creation (fresh navigation)', () => {
      // Simulate a prior session that saved 'workbench' to localStorage.
      localStorage.setItem(WORKSPACE_MODE_STORAGE_KEY, 'workbench')

      // A fresh Pinia (simulating a page reload) must pick up the saved value.
      setActivePinia(createPinia())
      const freshStore = useWorkspaceStore()

      expect(freshStore.mode).toBe('workbench')
    })

    it('reads "agent" mode from localStorage on fresh instantiation', () => {
      localStorage.setItem(WORKSPACE_MODE_STORAGE_KEY, 'agent')
      setActivePinia(createPinia())
      const freshStore = useWorkspaceStore()

      expect(freshStore.mode).toBe('agent')
    })

    it('falls back to "guided" when localStorage has no mode key', () => {
      // localStorage is already empty from beforeEach
      setActivePinia(createPinia())
      const freshStore = useWorkspaceStore()

      expect(freshStore.mode).toBe('guided')
    })

    it('falls back to "guided" when localStorage has an unrecognised mode value', () => {
      localStorage.setItem(WORKSPACE_MODE_STORAGE_KEY, 'unknown_future_mode')
      setActivePinia(createPinia())
      const freshStore = useWorkspaceStore()

      expect(freshStore.mode).toBe('guided')
    })
  })

  // ── Mode stays stable during hydratePreferences ────────────────────────────

  describe('mode does not drift silently during hydratePreferences', () => {
    it('applies the server mode and writes it to localStorage', async () => {
      const store = useWorkspaceStore()
      vi.mocked(workspaceApi.getPreferences).mockResolvedValue(makePreferenceResponse('workbench'))

      await store.hydratePreferences()

      expect(store.mode).toBe('workbench')
      expect(localStorage.getItem(WORKSPACE_MODE_STORAGE_KEY)).toBe('workbench')
    })

    it('a late-resolving hydration does not overwrite a subsequent user mode change', async () => {
      const store = useWorkspaceStore()

      let hydrateResolve!: (v: ReturnType<typeof makePreferenceResponse>) => void
      vi.mocked(workspaceApi.getPreferences).mockReturnValue(
        new Promise((resolve) => { hydrateResolve = resolve }),
      )
      vi.mocked(workspaceApi.updatePreferences).mockResolvedValue(makePreferenceResponse('agent'))

      // Start a slow hydration, then the user switches mode before it completes.
      const hydratePromise = store.hydratePreferences()
      await store.updateMode('agent') // user's explicit choice

      expect(store.mode).toBe('agent')

      // Late-arriving hydration response returns 'guided' — must be ignored.
      hydrateResolve(makePreferenceResponse('guided'))
      await hydratePromise

      expect(store.mode).toBe('agent')
      expect(localStorage.getItem(WORKSPACE_MODE_STORAGE_KEY)).toBe('agent')
    })

    it('preferencesHydrated remains false when not authenticated', async () => {
      sessionMock.isAuthenticated = false
      const store = useWorkspaceStore()

      await store.hydratePreferences()

      expect(store.preferencesHydrated).toBe(false)
      expect(workspaceApi.getPreferences).not.toHaveBeenCalled()
    })
  })

  // ── Reset on logout / session expiry ──────────────────────────────────────

  describe('resetForLogout clears server-backed state', () => {
    it('clears homeSummary and todaySummary on logout', async () => {
      const store = useWorkspaceStore()

      vi.mocked(workspaceApi.getHomeSummary).mockResolvedValue({
        workspaceMode: 'workbench',
        isFirstRun: false,
        onboarding: {
          visibility: 'dismissed' as const,
          isComplete: true,
          currentStepId: null,
          dismissedAt: null,
          completedAt: null,
          steps: [],
        },
        workload: { capturesNeedingTriage: 3, capturesInProgress: 1, capturesReadyForFollowUp: 0, proposalsPendingReview: 2 },
        boards: { totalBoards: 1, recentBoardsCount: 1, recentBoards: [] },
        recommendedActions: [],
      })
      await store.fetchHomeSummary()
      expect(store.homeSummary).not.toBeNull()

      store.resetForLogout()

      expect(store.homeSummary).toBeNull()
      expect(store.todaySummary).toBeNull()
      expect(store.preferencesHydrated).toBe(false)
      expect(store.onboarding).toBeNull()
    })

    it('resetForLogout restores mode from localStorage (reads current localStorage, not in-memory state)', async () => {
      const store = useWorkspaceStore()

      // Hydrate with 'agent' — this sets both the store and localStorage to 'agent'.
      vi.mocked(workspaceApi.getPreferences).mockResolvedValue(makePreferenceResponse('agent'))
      await store.hydratePreferences()
      expect(store.mode).toBe('agent')
      expect(localStorage.getItem(WORKSPACE_MODE_STORAGE_KEY)).toBe('agent')

      // Simulate an external change to localStorage BEFORE logout
      // (e.g. another tab wrote a different value).
      // This proves resetForLogout() re-reads from localStorage rather than
      // keeping the in-memory value.
      localStorage.setItem(WORKSPACE_MODE_STORAGE_KEY, 'workbench')

      store.resetForLogout()

      // Must reflect what localStorage contains at logout time ('workbench'),
      // NOT the prior in-memory server-hydrated value ('agent').
      expect(store.mode).toBe('workbench')
      expect(store.mode).toBe(localStorage.getItem(WORKSPACE_MODE_STORAGE_KEY))
    })

    it('resetForLogout clears preferenceError', async () => {
      const store = useWorkspaceStore()
      vi.mocked(workspaceApi.updatePreferences).mockRejectedValue(new Error('network error'))
      await store.updateMode('agent')
      expect(store.preferenceError).not.toBeNull()

      store.resetForLogout()

      expect(store.preferenceError).toBeNull()
    })
  })

  // ── Workspace mode persistence across route changes ────────────────────────

  describe('mode persistence across route changes within a workspace', () => {
    it('mode stays stable after clearHomeSummary (simulating navigation away from home)', async () => {
      const store = useWorkspaceStore()
      vi.mocked(workspaceApi.getPreferences).mockResolvedValue(makePreferenceResponse('workbench'))
      await store.hydratePreferences()

      // Simulate navigating away — home summary is cleared
      store.clearHomeSummary()

      // Mode must not have changed
      expect(store.mode).toBe('workbench')
      expect(localStorage.getItem(WORKSPACE_MODE_STORAGE_KEY)).toBe('workbench')
    })

    it('mode stays stable after clearTodaySummary', async () => {
      const store = useWorkspaceStore()
      vi.mocked(workspaceApi.getPreferences).mockResolvedValue(makePreferenceResponse('agent'))
      await store.hydratePreferences()

      store.clearTodaySummary()

      expect(store.mode).toBe('agent')
      expect(localStorage.getItem(WORKSPACE_MODE_STORAGE_KEY)).toBe('agent')
    })

    it('updateMode persists mode to localStorage (can survive a reload)', async () => {
      const store = useWorkspaceStore()
      vi.mocked(workspaceApi.updatePreferences).mockResolvedValue(makePreferenceResponse('agent'))

      await store.updateMode('agent')

      // A new store instance (simulating reload) must see 'agent'.
      setActivePinia(createPinia())
      const reloadedStore = useWorkspaceStore()
      expect(reloadedStore.mode).toBe('agent')
    })
  })
})

// ─── Metrics board selection does not affect workspace mode or path ────────────

describe('metrics board selection route stability (#687)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    sessionMock.isAuthenticated = true
    localStorage.clear()
  })

  it('fetchBoardMetrics does not change workspace mode', async () => {
    const store = useWorkspaceStore()
    const metricsStore = useMetricsStore()

    vi.mocked(workspaceApi.getPreferences).mockResolvedValue(makePreferenceResponse('workbench'))
    await store.hydratePreferences()
    expect(store.mode).toBe('workbench')

    vi.mocked(metricsApi.getBoardMetrics).mockResolvedValue(makeMetricsResponse() as any)

    await metricsStore.fetchBoardMetrics({ boardId: 'board-1', from: '2026-01-01', to: '2026-01-31' })

    // Workspace mode must not have drifted
    expect(store.mode).toBe('workbench')
    expect(localStorage.getItem(WORKSPACE_MODE_STORAGE_KEY)).toBe('workbench')
  })

  it('fetchBoardMetrics loads data without raising an error on success', async () => {
    const metricsStore = useMetricsStore()
    vi.mocked(metricsApi.getBoardMetrics).mockResolvedValue(makeMetricsResponse() as any)

    await metricsStore.fetchBoardMetrics({ boardId: 'board-1', from: '2026-01-01', to: '2026-01-31' })

    expect(metricsStore.error).toBeNull()
    expect(metricsStore.loading).toBe(false)
    expect(metricsStore.metrics).not.toBeNull()
  })

  it('fetching metrics for a second board completes cleanly (no stale error)', async () => {
    const metricsStore = useMetricsStore()
    vi.mocked(metricsApi.getBoardMetrics).mockResolvedValue(makeMetricsResponse() as any)

    await metricsStore.fetchBoardMetrics({ boardId: 'board-1', from: '2026-01-01', to: '2026-01-31' })
    expect(metricsStore.metrics).not.toBeNull()

    // Select a different board
    const response2 = { ...makeMetricsResponse(), boardId: 'board-2' } satisfies ReturnType<typeof makeMetricsResponse>
    vi.mocked(metricsApi.getBoardMetrics).mockResolvedValue(response2 as any)

    await metricsStore.fetchBoardMetrics({ boardId: 'board-2', from: '2026-01-01', to: '2026-01-31' })

    expect(metricsStore.metrics).not.toBeNull()
    expect(metricsStore.error).toBeNull()
  })

  it('metrics $reset clears data without affecting workspaceStore mode', async () => {
    const store = useWorkspaceStore()
    const metricsStore = useMetricsStore()

    vi.mocked(workspaceApi.getPreferences).mockResolvedValue(makePreferenceResponse('agent'))
    await store.hydratePreferences()

    vi.mocked(metricsApi.getBoardMetrics).mockResolvedValue(makeMetricsResponse() as any)
    await metricsStore.fetchBoardMetrics({ boardId: 'board-1', from: '2026-01-01', to: '2026-01-31' })

    metricsStore.$reset()

    expect(metricsStore.metrics).toBeNull()
    expect(metricsStore.error).toBeNull()
    // Workspace mode must be untouched
    expect(store.mode).toBe('agent')
  })
})

// ─── Unexpected origin/redirect protection ────────────────────────────────────

describe('unexpected origin and path redirect protection (#687)', () => {
  /**
   * Verifies that the auth guard never produces a redirect to an unexpected
   * destination for an authenticated user.  The only legitimate guard-initiated
   * redirects are:
   *   - Unauthenticated → /login (with redirect query)
   *   - Authenticated at /login or /register → /workspace/home
   *
   * Authenticated navigation to any workspace path must return undefined (allow).
   */

  const WORKSPACE_ROUTES = [
    '/workspace/home',
    '/workspace/today',
    '/workspace/boards',
    '/workspace/boards/board-1',
    '/workspace/metrics',
    '/workspace/inbox',
    '/workspace/review',
    '/workspace/activity',
    '/workspace/archive',
    '/workspace/views',
    '/workspace/notifications',
    '/workspace/settings/export-import',
  ]

  it.each(WORKSPACE_ROUTES)(
    'authenticated user navigating to %s is NOT redirected (no unexpected route bounce)',
    (path) => {
      const token = fakeJwt()
      const result = authGuardDecision(
        { path, fullPath: path, meta: {} },
        { token },
      )
      // Guard must allow navigation (undefined), not redirect to home or elsewhere
      expect(result).toBeUndefined()
    },
  )

  it('the only guard-initiated redirect for workspace routes is /login (not bare root)', () => {
    // When unauthenticated, guard redirects to /login — never to / or /workspace/home
    const result = authGuardDecision(
      { path: '/workspace/metrics', fullPath: '/workspace/metrics', meta: {} },
      { token: null },
    )
    expect(result?.path).toBe('/login')
    expect(result?.path).not.toBe('/')
    expect(result?.path).not.toBe('/workspace/home')
  })

  it('authenticated user at /workspace/metrics gets no redirect (metrics path stability)', () => {
    const token = fakeJwt()
    const result = authGuardDecision(
      { path: '/workspace/metrics', fullPath: '/workspace/metrics', meta: {} },
      { token },
    )
    expect(result).toBeUndefined()
  })

  it('demo-mode user at workspace routes gets no redirect', () => {
    const result = authGuardDecision(
      { path: '/workspace/metrics', fullPath: '/workspace/metrics', meta: {} },
      { token: null, demoActive: true },
    )
    expect(result).toBeUndefined()
  })
})
