/**
 * Tests for the feature-flag route guard logic (issue #524).
 *
 * The guard in router/index.ts reads `to.meta.requiresFlag` and redirects to
 * /workspace/home when the flag is disabled. These tests exercise the
 * featureFlagStore behaviour that the guard depends on, and verify the guard's
 * decision table directly via a simulated meta object — without needing a full
 * vue-router instance.
 */
import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useFeatureFlagStore } from '../../store/featureFlagStore'
import type { FeatureFlags } from '../../types/feature-flags'

// ─── helper that mirrors the guard's decision logic ───────────────────────────
function guardDecision(
  meta: { requiresFlag?: keyof FeatureFlags },
  store: ReturnType<typeof useFeatureFlagStore>,
): { path: string } | undefined {
  const requiredFlag = meta.requiresFlag
  if (requiredFlag !== undefined) {
    store.restore()
    if (!store.isEnabled(requiredFlag)) {
      return { path: '/workspace/home' }
    }
  }
  return undefined // allow navigation
}

// ─── routes that are gated by feature flags (mirrors router/index.ts) ─────────
const FLAGGED_ROUTES: { path: string; flag: keyof FeatureFlags }[] = [
  { path: '/workspace/activity', flag: 'newActivity' },
  { path: '/workspace/activity/board/123', flag: 'newActivity' },
  { path: '/workspace/activity/entity/task/456', flag: 'newActivity' },
  { path: '/workspace/activity/user', flag: 'newActivity' },
  { path: '/workspace/automations/queue', flag: 'newAutomation' },
  { path: '/workspace/review', flag: 'newAutomation' },
  { path: '/workspace/automations/chat', flag: 'newAutomation' },
  { path: '/workspace/ops/cli', flag: 'newOps' },
  { path: '/workspace/ops/endpoints', flag: 'newOps' },
  { path: '/workspace/ops/logs', flag: 'newOps' },
  { path: '/workspace/settings/profile', flag: 'newAuth' },
  { path: '/workspace/settings/access', flag: 'newAccess' },
  { path: '/workspace/archive', flag: 'newArchive' },
]

describe('feature-flag route guard (#524)', () => {
  let store: ReturnType<typeof useFeatureFlagStore>

  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
    store = useFeatureFlagStore()
  })

  describe('routes without a flag requirement', () => {
    it('allows navigation when meta.requiresFlag is undefined', () => {
      expect(guardDecision({}, store)).toBeUndefined()
    })
  })

  describe('routes with a flag requirement — flag enabled', () => {
    it.each(FLAGGED_ROUTES)(
      'allows $path when $flag is enabled',
      ({ path: _path, flag }) => {
        store.setFlag(flag, true)
        expect(guardDecision({ requiresFlag: flag }, store)).toBeUndefined()
      },
    )
  })

  describe('routes with a flag requirement — flag disabled', () => {
    it.each(FLAGGED_ROUTES)(
      'redirects $path to /workspace/home when $flag is disabled',
      ({ path: _path, flag }) => {
        store.setFlag(flag, false)
        expect(guardDecision({ requiresFlag: flag }, store)).toEqual({
          path: '/workspace/home',
        })
      },
    )
  })

  describe('hard-refresh scenario — restore() called by guard before check', () => {
    it('reads disabled flag from localStorage before App.vue mounts', () => {
      // Persist a disabled flag directly to localStorage (simulating a prior session).
      localStorage.setItem(
        'taskdeck_feature_flags',
        JSON.stringify({ newOps: false }),
      )
      // A fresh store instance starts with defaults — newOps default is false, but
      // let's explicitly test that restore() picks up what localStorage says.
      setActivePinia(createPinia())
      const freshStore = useFeatureFlagStore()
      // Guard calls restore() before isEnabled() — simulate that.
      expect(guardDecision({ requiresFlag: 'newOps' }, freshStore)).toEqual({
        path: '/workspace/home',
      })
    })

    it('reads enabled flag from localStorage before App.vue mounts', () => {
      localStorage.setItem(
        'taskdeck_feature_flags',
        JSON.stringify({ newActivity: true }),
      )
      setActivePinia(createPinia())
      const freshStore = useFeatureFlagStore()
      expect(guardDecision({ requiresFlag: 'newActivity' }, freshStore)).toBeUndefined()
    })
  })

  describe('guard coverage — all flagged routes have a flag entry', () => {
    it('FLAGGED_ROUTES list is non-empty', () => {
      expect(FLAGGED_ROUTES.length).toBeGreaterThan(0)
    })

    it('every route in FLAGGED_ROUTES references a valid FeatureFlags key', () => {
      // If a key is invalid, TypeScript would have already caught it, but this
      // runtime check guards against future key renames at the type level.
      const validFlags = [
        'newShell', 'newAuth', 'newAccess', 'newActivity',
        'newOps', 'newAutomation', 'newArchive',
      ] as const
      for (const { flag } of FLAGGED_ROUTES) {
        expect(validFlags).toContain(flag)
      }
    })
  })
})
