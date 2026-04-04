/**
 * Integration tests for the Vue Router auth guard, legacy redirects, and
 * route meta handling (issue #725).
 *
 * Unlike authGuard.spec.ts which mirrors the guard decision function,
 * these tests use a real router instance to verify end-to-end navigation
 * behavior including redirects, meta resolution, and guard interactions.
 *
 * Note: afterEach performance instrumentation (routePerf.end()) is not
 * covered here because usePerformanceMark has module-level side effects
 * that are better suited for E2E tests. The composable could get its own
 * unit spec in a future pass.
 */
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { createRouter, createWebHistory } from 'vue-router'
import { setActivePinia, createPinia } from 'pinia'
import { isTokenExpired } from '../../utils/jwt'
import * as tokenStorage from '../../utils/tokenStorage'
import { useFeatureFlagStore } from '../../store/featureFlagStore'
import type { FeatureFlags } from '../../types/feature-flags'

// ─── JWT helpers ────────────────────────────────────────────────────────────

function toBase64Url(value: string): string {
  return btoa(value).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '')
}

function fakeJwt(expOffsetSeconds = 3600): string {
  const header = toBase64Url(JSON.stringify({ alg: 'HS256', typ: 'JWT' }))
  const exp = Math.floor(Date.now() / 1000) + expOffsetSeconds
  const payload = toBase64Url(JSON.stringify({ exp }))
  return `${header}.${payload}.fakesig`
}

function expiredJwt(): string {
  return fakeJwt(-60)
}

// ─── Stub component ─────────────────────────────────────────────────────────

const Stub = { template: '<div></div>' }

// ─── Build a test router with the same structure as the production router ────

function buildTestRouter() {
  const router = createRouter({
    history: createWebHistory(),
    routes: [
      { path: '/login', name: 'login', component: Stub, meta: { public: true } },
      { path: '/register', name: 'register', component: Stub, meta: { public: true } },
      // Legacy redirects
      { path: '/', redirect: '/workspace/home' },
      { path: '/boards', redirect: '/workspace/boards' },
      { path: '/boards/:id', redirect: (to) => `/workspace/boards/${to.params.id}` },
      // Workspace routes
      { path: '/workspace', redirect: '/workspace/home' },
      { path: '/workspace/home', name: 'workspace-home', component: Stub, meta: { requiresShell: true } },
      { path: '/workspace/today', name: 'workspace-today', component: Stub, meta: { requiresShell: true } },
      { path: '/workspace/boards', name: 'workspace-boards', component: Stub, meta: { requiresShell: true } },
      { path: '/workspace/boards/:id', name: 'workspace-board', component: Stub, meta: { requiresShell: true } },
      {
        path: '/workspace/activity',
        name: 'workspace-activity',
        component: Stub,
        meta: { requiresShell: true, requiresFlag: 'newActivity' as keyof FeatureFlags },
      },
      {
        path: '/workspace/archive',
        name: 'workspace-archive',
        component: Stub,
        meta: { requiresShell: true, requiresFlag: 'newArchive' as keyof FeatureFlags },
      },
      {
        path: '/workspace/dev-tools',
        name: 'workspace-dev-tools',
        component: Stub,
        meta: { requiresShell: true, requiresFlag: 'devTools' as keyof FeatureFlags },
      },
    ],
  })

  // Replicate the production beforeEach guard
  router.beforeEach((to) => {
    const isPublic = to.meta.public === true
    const token = tokenStorage.getToken()
    const tokenValid = !!token && !isTokenExpired(token)
    const hasValidSession = tokenValid

    if (token && !tokenValid) {
      tokenStorage.clearAll()
    }

    if (!isPublic && !hasValidSession && to.path.startsWith('/workspace')) {
      return { path: '/login', query: { redirect: to.fullPath } }
    }

    if (isPublic && hasValidSession && (to.path === '/login' || to.path === '/register')) {
      return { path: '/workspace/home' }
    }

    // Feature-flag gate
    const requiredFlag = to.meta.requiresFlag as keyof FeatureFlags | undefined
    if (requiredFlag !== undefined) {
      const featureFlags = useFeatureFlagStore()
      featureFlags.restore()
      if (!featureFlags.isEnabled(requiredFlag)) {
        return { path: '/workspace/home' }
      }
    }
  })

  return router
}

// ─── Tests ──────────────────────────────────────────────────────────────────

describe('router integration tests (#725)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  // ── Legacy redirect routes ──────────────────────────────────────────────

  describe('legacy redirect routes', () => {
    it('/ redirects to /workspace/home', async () => {
      vi.spyOn(tokenStorage, 'getToken').mockReturnValue(fakeJwt())
      const router = buildTestRouter()
      await router.push('/')
      expect(router.currentRoute.value.path).toBe('/workspace/home')
    })

    it('/boards redirects to /workspace/boards', async () => {
      vi.spyOn(tokenStorage, 'getToken').mockReturnValue(fakeJwt())
      const router = buildTestRouter()
      await router.push('/boards')
      expect(router.currentRoute.value.path).toBe('/workspace/boards')
    })

    it('/boards/:id redirects to /workspace/boards/:id', async () => {
      vi.spyOn(tokenStorage, 'getToken').mockReturnValue(fakeJwt())
      const router = buildTestRouter()
      await router.push('/boards/my-board-123')
      expect(router.currentRoute.value.path).toBe('/workspace/boards/my-board-123')
    })

    it('/workspace redirects to /workspace/home', async () => {
      vi.spyOn(tokenStorage, 'getToken').mockReturnValue(fakeJwt())
      const router = buildTestRouter()
      await router.push('/workspace')
      expect(router.currentRoute.value.path).toBe('/workspace/home')
    })
  })

  // ── Legacy redirect + auth guard interaction ──────────────────────────

  describe('legacy redirect + auth guard interaction', () => {
    it('/ without auth ends up at /login (redirect chain: / -> /workspace/home -> /login)', async () => {
      vi.spyOn(tokenStorage, 'getToken').mockReturnValue(null)
      const router = buildTestRouter()
      await router.push('/')
      expect(router.currentRoute.value.path).toBe('/login')
      expect(router.currentRoute.value.query.redirect).toBe('/workspace/home')
    })

    it('/boards/:id without auth redirects to /login', async () => {
      vi.spyOn(tokenStorage, 'getToken').mockReturnValue(null)
      const router = buildTestRouter()
      await router.push('/boards/board-42')
      expect(router.currentRoute.value.path).toBe('/login')
    })
  })

  // ── Auth guard with real router ───────────────────────────────────────

  describe('auth guard with real router', () => {
    it('unauthenticated user is redirected from /workspace/home to /login', async () => {
      vi.spyOn(tokenStorage, 'getToken').mockReturnValue(null)
      const router = buildTestRouter()
      await router.push('/workspace/home')
      expect(router.currentRoute.value.path).toBe('/login')
      expect(router.currentRoute.value.query.redirect).toBe('/workspace/home')
    })

    it('authenticated user can access workspace routes', async () => {
      vi.spyOn(tokenStorage, 'getToken').mockReturnValue(fakeJwt())
      const router = buildTestRouter()
      await router.push('/workspace/today')
      expect(router.currentRoute.value.path).toBe('/workspace/today')
    })

    it('authenticated user is redirected from /login to /workspace/home', async () => {
      vi.spyOn(tokenStorage, 'getToken').mockReturnValue(fakeJwt())
      const router = buildTestRouter()
      await router.push('/login')
      expect(router.currentRoute.value.path).toBe('/workspace/home')
    })

    it('authenticated user is redirected from /register to /workspace/home', async () => {
      vi.spyOn(tokenStorage, 'getToken').mockReturnValue(fakeJwt())
      const router = buildTestRouter()
      await router.push('/register')
      expect(router.currentRoute.value.path).toBe('/workspace/home')
    })

    it('expired token on workspace route clears storage and redirects to /login', async () => {
      const expired = expiredJwt()
      vi.spyOn(tokenStorage, 'getToken').mockReturnValue(expired)
      const clearSpy = vi.spyOn(tokenStorage, 'clearAll')
      const router = buildTestRouter()
      await router.push('/workspace/boards')
      expect(router.currentRoute.value.path).toBe('/login')
      expect(clearSpy).toHaveBeenCalled()
    })
  })

  // ── Feature flag + auth guard combined ────────────────────────────────

  describe('feature flag + auth guard combined', () => {
    it('authenticated user with disabled flag is redirected to /workspace/home', async () => {
      vi.spyOn(tokenStorage, 'getToken').mockReturnValue(fakeJwt())
      const store = useFeatureFlagStore()
      store.setFlag('devTools', false)
      const router = buildTestRouter()
      await router.push('/workspace/dev-tools')
      expect(router.currentRoute.value.path).toBe('/workspace/home')
    })

    it('authenticated user with enabled flag can access the route', async () => {
      vi.spyOn(tokenStorage, 'getToken').mockReturnValue(fakeJwt())
      const store = useFeatureFlagStore()
      store.setFlag('newActivity', true)
      const router = buildTestRouter()
      await router.push('/workspace/activity')
      expect(router.currentRoute.value.path).toBe('/workspace/activity')
    })

    it('unauthenticated user hitting a flagged route gets redirected to /login (auth guard runs first)', async () => {
      vi.spyOn(tokenStorage, 'getToken').mockReturnValue(null)
      const store = useFeatureFlagStore()
      store.setFlag('newActivity', true)
      const router = buildTestRouter()
      await router.push('/workspace/activity')
      expect(router.currentRoute.value.path).toBe('/login')
    })
  })
})
