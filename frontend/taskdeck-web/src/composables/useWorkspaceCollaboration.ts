import { computed, ref } from 'vue'
import { workspaceApi } from '../api/workspaceApi'
import type { WorkspaceCollaboration } from '../types/workspace'

/**
 * Lifecycle of the collaboration-membership answer.
 *
 * - `idle`        nothing has been asked for yet
 * - `loading`     a request is in flight and the answer is not yet known
 * - `ready`       the server answered and `hasCollaborators` is authoritative
 * - `unavailable` the request failed or answered in a shape we cannot trust
 *
 * Only `ready` carries a usable answer. Consumers must treat `idle`, `loading`
 * and `unavailable` as "unknown" and fail open - never remove a control on a
 * value we do not have (#1940).
 */
export type CollaborationState = 'idle' | 'loading' | 'ready' | 'unavailable'

/** Minimum gap between opportunistic refreshes, so a tab-switch storm cannot hammer the API. */
export const COLLABORATION_REFRESH_THROTTLE_MS = 30_000

/**
 * Accepts a payload only when it is both well-typed AND internally consistent
 * with the documented contract: `memberCount` is a whole number of at least 1
 * (the caller always counts), and `hasCollaborators` is exactly
 * `memberCount > 1`.
 *
 * A well-typed but self-contradicting answer such as
 * `{ memberCount: 2, hasCollaborators: false }` must not be trusted: taking the
 * boolean at face value would hide All/Mine on a workspace the same payload
 * says has two members. Anything that fails here is reported as unknown, which
 * fails open.
 */
function isConsistentCollaboration(value: unknown): value is WorkspaceCollaboration {
  if (typeof value !== 'object' || value === null) return false
  const candidate = value as Partial<WorkspaceCollaboration>
  if (typeof candidate.hasCollaborators !== 'boolean') return false
  if (typeof candidate.memberCount !== 'number') return false
  if (!Number.isInteger(candidate.memberCount) || candidate.memberCount < 1) return false
  return candidate.hasCollaborators === (candidate.memberCount > 1)
}

/**
 * Reads the server-computed collaboration-membership contract
 * (`GET /api/workspace/collaboration`) and exposes it with explicit
 * loading / unknown / failure semantics.
 *
 * There is no realtime membership event to subscribe to: the only SignalR hub
 * is per-board and broadcasts card/column mutations and presence, and nothing
 * is published when board access is granted or revoked. Reactivity is therefore
 * a mount-time read plus a throttled refresh whenever the document becomes
 * visible again, which covers returning to the tab after sharing a board
 * elsewhere. `refresh()` is exported for any caller with a better trigger.
 */
export function useWorkspaceCollaboration() {
  const state = ref<CollaborationState>('idle')
  const memberCount = ref<number | null>(null)
  const hasCollaborators = ref<boolean | null>(null)

  let inFlight: Promise<void> | null = null
  let lastAttemptAt = 0

  /** True only when the server positively reported a single-member workspace. */
  const isSoloWorkspace = computed(
    () => state.value === 'ready' && hasCollaborators.value === false,
  )

  /** True only when an authoritative answer is currently held. */
  const isMembershipKnown = computed(() => state.value === 'ready')

  function markUnknown() {
    // A failed or malformed answer invalidates the previous one. Reverting to
    // "unknown" can re-reveal a control that was hidden a moment ago, which is
    // the safe direction: a stale hide would silently withhold a filter.
    memberCount.value = null
    hasCollaborators.value = null
    state.value = 'unavailable'
  }

  function refresh(): Promise<void> {
    if (inFlight) return inFlight

    state.value = 'loading'
    lastAttemptAt = Date.now()
    inFlight = (async () => {
      try {
        const payload = await workspaceApi.getCollaboration()
        if (!isConsistentCollaboration(payload)) {
          markUnknown()
          return
        }
        memberCount.value = payload.memberCount
        hasCollaborators.value = payload.hasCollaborators
        state.value = 'ready'
      } catch {
        markUnknown()
      } finally {
        lastAttemptAt = Date.now()
        inFlight = null
      }
    })()

    return inFlight
  }

  function onVisibilityChange() {
    if (typeof document === 'undefined') return
    if (document.visibilityState !== 'visible') return
    if (Date.now() - lastAttemptAt < COLLABORATION_REFRESH_THROTTLE_MS) return
    void refresh()
  }

  /** Reads the contract once and starts watching for out-of-band membership changes. */
  function start(): Promise<void> {
    if (typeof document !== 'undefined') {
      document.addEventListener('visibilitychange', onVisibilityChange)
    }
    return refresh()
  }

  function stop() {
    if (typeof document !== 'undefined') {
      document.removeEventListener('visibilitychange', onVisibilityChange)
    }
  }

  return {
    state,
    memberCount,
    hasCollaborators,
    isSoloWorkspace,
    isMembershipKnown,
    refresh,
    start,
    stop,
  }
}
