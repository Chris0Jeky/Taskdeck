import { computed, nextTick, onScopeDispose, ref, watch } from 'vue'
import { isNavigationFailure, NavigationFailureType, useRoute, useRouter } from 'vue-router'
import { automationApi } from '../api/automationApi'
import { boardsApi } from '../api/boardsApi'
import { i18n } from '../i18n'
import type { ReviewSummaryCard } from '../components/review/ReviewSummaryCards.vue'
import { useToastStore } from '../store/toastStore'
import {
  normalizeProposalSourceType,
  normalizeProposalStatus,
} from '../utils/automation'
import { buildInputAssistOptions } from '../utils/inputAssist'
import { normalizeBoardIdQueryParam } from '../utils/navigation'
import { proposalIdsEqual } from '../utils/proposalIdentity'
import type { Proposal as ApiProposal } from '../types/automation'
import type { Board } from '../types/board'
import { getErrorDisplay } from './useErrorMapper'
import { logError } from '../utils/errorReporting'
import { usePerformanceMark } from './usePerformanceMark'

/**
 * A proposal counts as "stale" once it has sat in PendingReview for 24 hours.
 * Centralised here so both the Paper and Legacy review surfaces agree (#1124
 * drift class — ADR-0038).
 */
export const STALE_PROPOSAL_MS = 24 * 60 * 60 * 1000

/**
 * How often the Review surface re-reads the queue while it is open and the tab
 * is visible (#2194).
 *
 * There is nothing to subscribe to: the only SignalR hub is `BoardsHub`
 * (`/hubs/boards`), its groups are strictly per-board, and it emits exactly
 * `boardMutation` / `boardPresence` / `toolStatus` over board/card/column/label
 * entities. `AutomationProposalService` never touches `IBoardRealtimeNotifier`,
 * so proposal creation is silent on the wire and the all-boards review queue has
 * no board group to join in the first place. A bounded poll is therefore the
 * whole available mechanism until a proposal event exists server-side.
 *
 * 15 s is short enough that a proposal created by Ask AI elsewhere appears
 * inside one glance -- #2194 measured 115 s of a false "Nothing waiting" -- and
 * long enough that an idle Review tab costs 4 requests/minute against an
 * endpoint the surface already calls on entry. Ticks are skipped entirely while
 * the tab is hidden, so a backgrounded tab costs nothing.
 */
export const REVIEW_QUEUE_REFRESH_MS = 15_000

/**
 * Decision rules shared by every review surface (Paper deep-review and the
 * Legacy card). These are pure functions so the prop-driven Legacy components
 * can reuse the exact same gating without importing the composable. The
 * reactive instances exposed by `useReviewProposals` are thin wrappers that
 * supply `isExpired`/`nowMs` from the composable's own reactive clock.
 *
 * `isExpired` is passed in explicitly because expiry depends on a reactive
 * clock that only the composable (or the parent view) owns. Callers must pass
 * the same `isProposalExpired(proposal)` value the rest of the surface uses,
 * including the Approved+expired #1124 case.
 */
export function isProposalApplyActionable(proposal: ApiProposal, isExpired: boolean): boolean {
  const status = normalizeProposalStatus(proposal.status)
  return (status === 'PendingReview' || status === 'Approved') && !isExpired
}

export function isProposalRejectActionable(proposal: ApiProposal, isExpired: boolean): boolean {
  return normalizeProposalStatus(proposal.status) === 'PendingReview' && !isExpired
}

// Approve requires a live, non-expired PendingReview proposal AND a structurally
// applyable one: a zero-operation proposal is rejected by Apply (and by `/diff`,
// #1376/#1395), so approving it only defers a guaranteed 400 — the rail must not
// offer it (#1397 LOW-3; backend approve-time validation tracked in #1416).
// `hasSavedRevision` is the #1235 escape hatch: a saved revision carries
// operations the backend renders/applies revision-aware even when the ORIGINAL
// operations are empty. Callers without revision knowledge (the Legacy card)
// omit it — a revised-in-Paper zero-op proposal viewed in Legacy is then
// conservatively un-approvable there until refreshed in Paper.
export function isProposalApproveActionable(
  proposal: ApiProposal,
  isExpired: boolean,
  options?: { hasSavedRevision?: boolean },
): boolean {
  if (normalizeProposalStatus(proposal.status) !== 'PendingReview' || isExpired) return false
  if ((proposal.operations?.length ?? 0) === 0 && !options?.hasSavedRevision) return false
  return true
}

// A proposal is "read-only" once it is expired (client-side clock or domain
// Expired) or in any terminal status (Applied/Rejected/Failed/Expired/Dismissed).
// PR #1395 made the backend `/diff` reject these with 400 ("Proposal has
// expired"), so review surfaces must NOT fire the live diff for them — they
// present the stored `diffPreview` under an explicit read-only banner instead
// (#1397 maintainer decision: expired proposals stay inspectable via stored
// content without burdening the UI with a live request that 400s). Callers pass
// the same `isProposalExpired(proposal)` the rest of the surface uses so Paper
// and Legacy can never drift (#1124 / ADR-0038).
export function isProposalReadOnly(proposal: ApiProposal, isExpired: boolean): boolean {
  if (isExpired) return true
  const status = normalizeProposalStatus(proposal.status)
  return (
    status === 'Applied' ||
    status === 'Rejected' ||
    status === 'Failed' ||
    status === 'Expired' ||
    status === 'Dismissed'
  )
}

export function isProposalStale(proposal: ApiProposal, nowMs: number): boolean {
  if (!proposal || normalizeProposalStatus(proposal.status) !== 'PendingReview') return false
  // Guard against missing/invalid createdAt: a falsy value (new Date(null) is
  // the epoch) or an unparseable string (new Date(...).getTime() is NaN) would
  // otherwise mis-flag the proposal as wildly stale.
  if (!proposal.createdAt) return false
  const createdMs = new Date(proposal.createdAt).getTime()
  if (Number.isNaN(createdMs)) return false
  return nowMs - createdMs >= STALE_PROPOSAL_MS
}

export function useReviewProposals() {
  const route = useRoute()
  const router = useRouter()
  const toast = useToastStore()
  const reviewLoadPerf = usePerformanceMark('review-load')
  // Module-scoped i18n rather than `useI18n()` — see the note in
  // useReviewActions.ts. This composable is shared with the Legacy shell and is
  // exercised by specs that never mount a component.
  const t = i18n.global.t

  const proposals = ref<ApiProposal[]>([])
  const proposalsLoading = ref(false)
  // A deep link is an explicit request, not a selection preference. Preserve a
  // confirmed 404 separately so Paper can say what happened instead of
  // presenting the ordinary empty queue or a different actionable proposal.
  const unavailableProposalId = ref<string | null>(null)
  let latestProposalLoadRequestId = 0
  const availableBoards = ref<Board[]>([])
  const loadingBoards = ref(false)
  const boardFilterInput = ref('')
  const activeBoardFilter = computed(() => normalizeBoardIdQueryParam(route.query.boardId))
  const isArchivedHistory = computed(
    () => route.query.history === 'archived' && activeBoardFilter.value !== null,
  )
  const showCompleted = ref(false)

  // Reactive clock for client-side expiry detection -- updates every 60 s
  const nowMs = ref(Date.now())
  let clockInterval: ReturnType<typeof setInterval> | null = null

  function startClock() {
    // Guard against double-start: a second call would overwrite clockInterval
    // and permanently leak the first interval (neither stopClock nor
    // onScopeDispose can clear a handle they no longer hold).
    if (clockInterval !== null) return
    clockInterval = setInterval(() => {
      nowMs.value = Date.now()
    }, 60_000)
  }

  function stopClock() {
    if (clockInterval !== null) {
      clearInterval(clockInterval)
      clockInterval = null
    }
  }

  onScopeDispose(stopClock)

  const completedStatuses = new Set(['Applied', 'Rejected', 'Failed', 'Expired', 'Dismissed'])

  const boardOptions = computed(() =>
    buildInputAssistOptions(
      availableBoards.value.map((board) => ({
        value: board.id,
        label: board.name,
      })),
    ),
  )

  const activeBoardName = computed(() => {
    if (!activeBoardFilter.value) return ''
    const normalizedActiveId = normalizeBoardIdQueryParam(activeBoardFilter.value).toLowerCase()
    const board = availableBoards.value.find(
      (b) => normalizeBoardIdQueryParam(b.id).toLowerCase() === normalizedActiveId,
    )
    return board?.name ?? activeBoardFilter.value
  })

  function matchesActiveBoardFilter(boardId: string | null | undefined): boolean {
    if (!activeBoardFilter.value) return true
    const normalizedBoardId = normalizeBoardIdQueryParam(boardId).toLowerCase()
    return normalizedBoardId === activeBoardFilter.value.toLowerCase()
  }

  function isProposalExpired(proposal: ApiProposal): boolean {
    const normalized = normalizeProposalStatus(proposal.status)
    if (normalized === 'Expired') return true
    if (normalized === 'PendingReview' || normalized === 'Approved') {
      // Honor the server-authoritative `isExpired` flag in addition to the local
      // 60s clock: the client clock can lag inside the tick window or skew behind
      // server time, and if the server has already expired the proposal the live
      // `/diff` 400s — the read-only guards must classify it as expired so the
      // review surfaces present the stored preview instead (#1414 P2). The flag
      // is time-based and status-AGNOSTIC on the backend (`IsExpired => UtcNow >
      // ExpiresAt`), so it is consulted ONLY inside this Pending/Approved branch:
      // a terminal proposal whose expiry later passed must keep its terminal
      // classification (Applied/Rejected/…), never flip to "Expired" — otherwise
      // `visibleProposals`, the status labels, and the expired notice regress.
      return proposal.isExpired === true || new Date(proposal.expiresAt).getTime() <= nowMs.value
    }
    return false
  }

  // Reactive wrappers over the shared pure decision rules. They bind the
  // surface's own expiry/clock state so Paper and Legacy can never drift. #1124
  function isApplyActionable(proposal: ApiProposal): boolean {
    return isProposalApplyActionable(proposal, isProposalExpired(proposal))
  }

  function isRejectActionable(proposal: ApiProposal): boolean {
    return isProposalRejectActionable(proposal, isProposalExpired(proposal))
  }

  function isStaleProposal(proposal: ApiProposal): boolean {
    return isProposalStale(proposal, nowMs.value)
  }

  // A proposal is "deferred" (snoozed) only while it is a PendingReview proposal whose
  // deferredUntil is still in the future. The status gate keeps a decided/terminal proposal
  // that retained a stale snooze value from ever being hidden. The 60s `nowMs` clock
  // auto-resurfaces it in-session once the window elapses; the backend filter resurfaces
  // it cross-session.
  function isProposalDeferred(proposal: ApiProposal): boolean {
    return (
      !!proposal.deferredUntil &&
      new Date(proposal.deferredUntil).getTime() > nowMs.value &&
      normalizeProposalStatus(proposal.status) === 'PendingReview'
    )
  }

  const visibleProposals = computed(() => {
    // A deep-linked (hash-targeted) snoozed proposal must stay visible so the deep link renders it
    // — the backend serves deferred proposals by id (openProposalFromHash fetches + upserts it).
    // Without this carve-out the unconditional deferred filter would hide the very proposal the
    // hash points at, leaving an empty or unrelated review item.
    const hashTargetId = getProposalIdFromHash(route.hash)
    return proposals.value.filter((proposal) => {
      if (!matchesActiveBoardFilter(proposal.boardId)) return false
      const status = normalizeProposalStatus(proposal.status)
      const expired = isProposalExpired(proposal)
      if (isArchivedHistory.value) {
        return status === 'Approved' || completedStatuses.has(status) || expired
      }
      const isHashTarget = proposalIdsEqual(proposal.id, hashTargetId)
      if (status === 'Dismissed') return false
      if (expired) return true
      if (isProposalDeferred(proposal) && !isHashTarget) return false
      if (!showCompleted.value && completedStatuses.has(status)) return false
      return true
    })
  })

  function captureSourceReference(proposal: ApiProposal): string | null {
    if (normalizeProposalSourceType(proposal.sourceType) !== 'Queue') return null
    if (!proposal.sourceReferenceId) return null
    const trimmed = proposal.sourceReferenceId.trim()
    return trimmed.length > 0 ? trimmed : null
  }

  function hasProvenanceContext(proposal: ApiProposal): boolean {
    return !!captureSourceReference(proposal)
  }

  const summaryCards = computed<ReviewSummaryCard[]>(() => {
    let pendingReview = 0
    let readyToExecute = 0
    let captureLinked = 0
    let appliedRecently = 0

    for (const proposal of visibleProposals.value) {
      const normalizedStatus = normalizeProposalStatus(proposal.status)
      const expired = isProposalExpired(proposal)

      if (normalizedStatus === 'PendingReview' && !expired) pendingReview += 1
      else if (normalizedStatus === 'Approved' && !expired) readyToExecute += 1
      else if (normalizedStatus === 'Applied') appliedRecently += 1

      if (hasProvenanceContext(proposal)) captureLinked += 1
    }

    // The card ids are stable DOM/test contracts and never translate; only the
    // label and helper are copy. This computed re-runs on a language switch
    // because `t` reads the active locale.
    return [
      {
        id: 'pending-review',
        label: t('review.summary.pendingReview.label'),
        value: pendingReview,
        helper: t('review.summary.pendingReview.helper'),
      },
      {
        id: 'ready-to-execute',
        label: t('review.summary.readyToExecute.label'),
        value: readyToExecute,
        helper: t('review.summary.readyToExecute.helper'),
      },
      {
        id: 'capture-linked',
        label: t('review.summary.captureLinked.label'),
        value: captureLinked,
        helper: t('review.summary.captureLinked.helper'),
      },
      {
        id: 'applied',
        label: t('review.summary.applied.label'),
        value: appliedRecently,
        helper: t('review.summary.applied.helper'),
      },
    ]
  })

  function isProposalDismissable(proposal: ApiProposal): boolean {
    const status = normalizeProposalStatus(proposal.status)
    return (
      status === 'Applied' ||
      status === 'Rejected' ||
      status === 'Failed' ||
      status === 'Expired' ||
      // Mirror backend AutomationProposal.CanBeDismissed: an Approved proposal that
      // expired before it was executed can no longer be applied, so it stays
      // dismissable (otherwise it lingers in Review with no way to clear it). #1124
      (status === 'Approved' && isProposalExpired(proposal))
    )
  }

  const dismissableProposalIds = computed(() =>
    (isArchivedHistory.value ? [] : proposals.value)
      .filter((p) => isProposalDismissable(p))
      .filter((p) => matchesActiveBoardFilter(p.boardId))
      .map((p) => p.id),
  )

  // --- Data loading ---

  function getProposalIdFromHash(hash: string): string | null {
    if (!hash.startsWith('#proposal-')) return null
    const rawId = hash.slice('#proposal-'.length).trim()
    if (!rawId) return null
    try {
      return decodeURIComponent(rawId)
    } catch {
      return null
    }
  }

  async function scrollToProposalFromHash() {
    const proposalId = getProposalIdFromHash(route.hash)
    if (!proposalId) return
    await nextTick()
    const canonicalProposal = proposals.value.find((proposal) =>
      proposalIdsEqual(proposal.id, proposalId),
    )
    const element = canonicalProposal
      ? document.getElementById(`proposal-${canonicalProposal.id}`)
      : null
    element?.scrollIntoView({ block: 'nearest' })
  }

  function upsertProposal(proposal: ApiProposal) {
    const existingIndex = proposals.value.findIndex((current) =>
      proposalIdsEqual(current.id, proposal.id),
    )
    if (existingIndex >= 0) {
      proposals.value[existingIndex] = proposal
      return
    }
    const proposalCreatedAt = new Date(proposal.createdAt).getTime()
    const insertIndex = proposals.value.findIndex((current) => new Date(current.createdAt).getTime() < proposalCreatedAt)
    if (insertIndex >= 0) {
      proposals.value.splice(insertIndex, 0, proposal)
      return
    }
    proposals.value.push(proposal)
  }

  function isHttpNotFound(error: unknown): boolean {
    const candidate = error as { response?: { status?: number } } | null
    return candidate?.response?.status === 404
  }

  async function safeReplace(to: Parameters<typeof router.replace>[0]) {
    try {
      await router.replace(to)
    } catch (err) {
      if (!isNavigationFailure(err, NavigationFailureType.duplicated | NavigationFailureType.cancelled | NavigationFailureType.aborted)) {
        logError('Unexpected navigation failure:', err)
      }
    }
  }

  async function openProposalFromHash() {
    if (proposalsLoading.value) return
    const proposalId = getProposalIdFromHash(route.hash)
    if (!proposalId) {
      unavailableProposalId.value = null
      return
    }

    // A different hash starts a fresh lookup. Keep the current unavailable
    // state only while it still describes the route the user asked for.
    if (!proposalIdsEqual(unavailableProposalId.value, proposalId)) {
      unavailableProposalId.value = null
    }

    const currentProposal = proposals.value.find((p) => proposalIdsEqual(p.id, proposalId))
    if (currentProposal) {
      if (!matchesActiveBoardFilter(currentProposal.boardId)) {
        return
      }
      unavailableProposalId.value = null
      await scrollToProposalFromHash()
      return
    }

    try {
      const fetchedProposal = await automationApi.getProposal(proposalId)
      if (!proposalIdsEqual(getProposalIdFromHash(route.hash), proposalId)) return
      // A route lookup may canonicalize GUID hex casing, but it may not return a
      // different record. Retain the hash as unavailable instead of upserting a
      // response whose identity does not match the requested proposal.
      if (!proposalIdsEqual(fetchedProposal.id, proposalId)) {
        unavailableProposalId.value = proposalId
        return
      }
      if (!matchesActiveBoardFilter(fetchedProposal.boardId)) {
        return
      }
      upsertProposal(fetchedProposal)
      unavailableProposalId.value = null
      await nextTick()
      await scrollToProposalFromHash()
    } catch (e: unknown) {
      if (!proposalIdsEqual(getProposalIdFromHash(route.hash), proposalId)) return
      if (isHttpNotFound(e)) {
        unavailableProposalId.value = proposalId
        return
      }
      toast.error(getErrorDisplay(e, t('review.toast.loadProposalFailed')).message)
    }
  }

  // After a user action removes a deep-linked proposal from the queue (a SUCCESSFUL snooze via
  // Defer), drop the hash so the visibleProposals carve-out stops exempting it from the deferred
  // filter. Without this, a just-snoozed proposal you reached via #proposal-<id> stays visible —
  // with live action buttons — until the next navigation/refresh, contradicting the snooze.
  // Callers MUST gate this on success: clearing the hash for a proposal whose defer FAILED would
  // hide an already-snoozed deep-linked target (its prior deferredUntil still in effect) with no
  // retry path.
  async function clearProposalDeepLink(proposalId: string) {
    if (!proposalIdsEqual(getProposalIdFromHash(route.hash), proposalId)) return
    await safeReplace({ name: 'workspace-review', query: route.query })
  }

  async function loadProposals() {
    reviewLoadPerf.start()
    const requestId = ++latestProposalLoadRequestId

    try {
      proposalsLoading.value = true
      const loadedProposals = await automationApi.getProposals({
        limit: 200,
        boardId: activeBoardFilter.value || undefined,
      })
      if (requestId !== latestProposalLoadRequestId) return
      proposals.value = loadedProposals
    } catch (e: unknown) {
      if (requestId !== latestProposalLoadRequestId) return
      toast.error(getErrorDisplay(e, t('review.toast.loadProposalsFailed')).message)
    } finally {
      if (requestId === latestProposalLoadRequestId) proposalsLoading.value = false
      reviewLoadPerf.end()
    }

    if (requestId === latestProposalLoadRequestId) {
      await openProposalFromHash()
    }
  }

  // --- Background queue refresh (#2194) ---------------------------------

  let refreshInterval: ReturnType<typeof setInterval> | null = null
  let refreshInFlight = false
  let shouldRefreshNow: (() => boolean) | null = null

  function isDocumentVisible(): boolean {
    // No document (non-DOM test context) counts as NOT visible: a surface that
    // cannot be looked at must not generate background traffic.
    if (typeof document === 'undefined') return false
    return document.visibilityState === 'visible'
  }

  /**
   * A background re-read of the queue. Deliberately gentler than
   * `loadProposals`, because it fires under a reviewer who did not ask for it:
   *
   *  - it never raises `proposalsLoading`, so no skeleton flashes and no
   *    decision control blinks disabled mid-review;
   *  - it defers to any explicit load (route change, post-decision reload)
   *    rather than racing it, and drops its own answer if one started while it
   *    was in flight -- the explicit load is the fresher truth;
   *  - it stays silent on failure: a poll the user never requested must not
   *    raise a toast. The error is logged, never swallowed.
   *
   * Server truth wins for ordering and removals -- the response replaces the
   * list, exactly as `loadProposals` does -- with one carve-out: a `#proposal-`
   * deep-link target is fetched by id and may legitimately sit outside the list
   * page, so a refresh must not evict the very record the URL names.
   */
  async function refreshProposals(): Promise<void> {
    // An explicit load is authoritative and about to replace the list wholesale.
    if (proposalsLoading.value || refreshInFlight) return
    // Snapshot the load counter rather than incrementing it: bumping it here
    // would make an in-flight `loadProposals` discard its own result AND skip
    // the `proposalsLoading = false` reset in its finally block, wedging the
    // surface in a permanent loading state.
    const observedLoadId = latestProposalLoadRequestId
    refreshInFlight = true
    try {
      const loadedProposals = await automationApi.getProposals({
        limit: 200,
        boardId: activeBoardFilter.value || undefined,
      })
      // A newer explicit load started while this poll was in flight; its answer
      // supersedes this one.
      if (observedLoadId !== latestProposalLoadRequestId || proposalsLoading.value) return
      const hashTargetId = getProposalIdFromHash(route.hash)
      const next = [...loadedProposals]
      if (hashTargetId && !next.some((p) => proposalIdsEqual(p.id, hashTargetId))) {
        const pinned = proposals.value.find((p) => proposalIdsEqual(p.id, hashTargetId))
        if (pinned) next.push(pinned)
      }
      proposals.value = next
    } catch (e: unknown) {
      logError('Review queue background refresh failed:', e)
    } finally {
      refreshInFlight = false
    }
  }

  function onRefreshVisibilityChange() {
    // Re-entering a tab is the moment a stale "Nothing waiting" is most likely
    // to be on screen, so read immediately instead of waiting out the interval.
    if (!isDocumentVisible()) return
    void maybeRefresh()
  }

  function maybeRefresh(): Promise<void> {
    if (!isDocumentVisible()) return Promise.resolve()
    // The surface can veto a tick -- see the call site in PaperReviewView for
    // the mid-decision conditions it holds off on.
    if (shouldRefreshNow && !shouldRefreshNow()) return Promise.resolve()
    return refreshProposals()
  }

  /**
   * Starts the bounded, visibility-aware queue poll. `shouldRefresh` lets the
   * owning surface hold a tick while the reviewer is mid-decision (a confirm
   * dialog open, an action in flight) so the record under the cursor cannot
   * change underneath the decision being made.
   */
  function startQueueRefresh(shouldRefresh?: () => boolean) {
    // Guard against double-start exactly as startClock does: a second call
    // would overwrite the handle and leak the first interval forever.
    if (refreshInterval !== null) return
    shouldRefreshNow = shouldRefresh ?? null
    if (typeof document !== 'undefined') {
      document.addEventListener('visibilitychange', onRefreshVisibilityChange)
    }
    refreshInterval = setInterval(() => {
      void maybeRefresh()
    }, REVIEW_QUEUE_REFRESH_MS)
  }

  function stopQueueRefresh() {
    if (typeof document !== 'undefined') {
      document.removeEventListener('visibilitychange', onRefreshVisibilityChange)
    }
    if (refreshInterval !== null) {
      clearInterval(refreshInterval)
      refreshInterval = null
    }
    shouldRefreshNow = null
  }

  onScopeDispose(stopQueueRefresh)

  async function loadBoardOptions() {
    try {
      loadingBoards.value = true
      availableBoards.value = await boardsApi.getBoards(undefined, true)
    } catch {
      // Board options are non-critical
    } finally {
      loadingBoards.value = false
    }
  }

  // --- Navigation helpers ---

  function inboxPath(boardId?: string | null, captureItemId?: string): string {
    const params = new URLSearchParams()
    if (boardId) params.set('boardId', boardId)
    if (isArchivedHistory.value) params.set('history', 'archived')
    const queryString = params.toString()
    const query = queryString ? `?${queryString}` : ''
    const hash = captureItemId ? `#capture-${encodeURIComponent(captureItemId)}` : ''
    return `/workspace/inbox${query}${hash}`
  }

  function safeNavigate(to: Parameters<typeof router.push>[0]) {
    router.push(to).catch((err) => {
      if (!isNavigationFailure(err, NavigationFailureType.duplicated | NavigationFailureType.cancelled | NavigationFailureType.aborted)) {
        logError('Unexpected navigation failure:', err)
      }
    })
  }

  function openInbox() {
    safeNavigate(inboxPath(activeBoardFilter.value))
  }

  function proposalHref(proposal: ApiProposal): string {
    const boardId = proposal.boardId ?? activeBoardFilter.value
    const params = new URLSearchParams()
    if (boardId) params.set('boardId', boardId)
    if (isArchivedHistory.value) params.set('history', 'archived')
    const query = params.toString()
    const encodedProposalId = encodeURIComponent(proposal.id)
    return query
      ? `/workspace/review?${query}#proposal-${encodedProposalId}`
      : `/workspace/review#proposal-${encodedProposalId}`
  }

  function captureHrefForProposal(proposal: ApiProposal): string {
    const sourceReference = captureSourceReference(proposal)
    return sourceReference
      ? inboxPath(proposal.boardId ?? activeBoardFilter.value, sourceReference)
      : inboxPath(activeBoardFilter.value)
  }

  function openRoute(path: string) {
    safeNavigate(path)
  }

  function openProposal(proposalId: string) {
    safeNavigate({
      name: 'workspace-review',
      query: route.query,
      hash: `#proposal-${encodeURIComponent(proposalId)}`,
    })
  }

  function openBoard(boardId: string) {
    safeNavigate(`/workspace/boards/${boardId}`)
  }

  function applyBoardFilter(boardId: string) {
    const trimmed = boardId.trim()
    boardFilterInput.value = ''
    if (trimmed) {
      safeNavigate({ name: 'workspace-review', query: { boardId: trimmed } })
    } else {
      safeNavigate({ name: 'workspace-review' })
    }
  }

  async function clearBoardFilter() {
    boardFilterInput.value = ''
    // Read the mode BEFORE the query is rewritten -- `isArchivedHistory` is
    // derived from the route, so it flips as soon as the replace lands.
    const leavingArchivedHistory = isArchivedHistory.value
    const query = { ...route.query }
    delete query.boardId
    delete query.history
    // Leaving archived history takes any `#proposal-<id>` deep link with it.
    // Keeping the hash would hand an archived board's proposal to the UNSCOPED
    // queue: `openProposalFromHash` refetches it by id, `matchesActiveBoardFilter`
    // waves it through now that no board filter is set, and `upsertProposal`
    // reinserts it into a mutation-enabled Review where Apply/Reject act on an
    // archived board. Read-only is the whole point of the mode, so the exit
    // drops the target rather than smuggling it across the boundary.
    // Ordinary (non-archived) board clears keep their deep link as before.
    const hash = leavingArchivedHistory ? '' : route.hash
    await safeReplace({ name: 'workspace-review', query, hash })
  }

  // --- Watchers ---

  watch(
    () => route.hash,
    () => { openProposalFromHash().catch(() => {}) },
  )

  watch(
    () => activeBoardFilter.value,
    () => { loadProposals().catch(() => {}) },
  )

  return {
    proposals,
    proposalsLoading,
    unavailableProposalId,
    availableBoards,
    loadingBoards,
    boardFilterInput,
    activeBoardFilter,
    isArchivedHistory,
    activeBoardName,
    showCompleted,
    boardOptions,
    nowMs,
    visibleProposals,
    summaryCards,
    dismissableProposalIds,
    matchesActiveBoardFilter,
    isProposalExpired,
    isApplyActionable,
    isRejectActionable,
    isProposalDismissable,
    isProposalDeferred,
    isStaleProposal,
    clearProposalDeepLink,
    loadProposals,
    refreshProposals,
    loadBoardOptions,
    startClock,
    stopClock,
    startQueueRefresh,
    stopQueueRefresh,
    openInbox,
    proposalHref,
    captureHrefForProposal,
    openRoute,
    openProposal,
    openBoard,
    applyBoardFilter,
    clearBoardFilter,
  }
}
