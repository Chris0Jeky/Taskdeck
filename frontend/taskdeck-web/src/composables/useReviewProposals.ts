import { computed, nextTick, onScopeDispose, ref, watch } from 'vue'
import { isNavigationFailure, NavigationFailureType, useRoute, useRouter } from 'vue-router'
import { automationApi } from '../api/automationApi'
import { boardsApi } from '../api/boardsApi'
import type { ReviewSummaryCard } from '../components/review/ReviewSummaryCards.vue'
import { useToastStore } from '../store/toastStore'
import {
  normalizeProposalSourceType,
  normalizeProposalStatus,
} from '../utils/automation'
import { buildInputAssistOptions } from '../utils/inputAssist'
import { normalizeBoardIdQueryParam } from '../utils/navigation'
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

// Approve shares Reject's precondition today (a live, non-expired PendingReview proposal),
// but is named distinctly so the two can diverge without one silently aliasing the other.
export function isProposalApproveActionable(proposal: ApiProposal, isExpired: boolean): boolean {
  return normalizeProposalStatus(proposal.status) === 'PendingReview' && !isExpired
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

  const proposals = ref<ApiProposal[]>([])
  const proposalsLoading = ref(false)
  let latestProposalLoadRequestId = 0
  const availableBoards = ref<Board[]>([])
  const loadingBoards = ref(false)
  const boardFilterInput = ref('')
  const activeBoardFilter = computed(() => normalizeBoardIdQueryParam(route.query.boardId))
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
      return new Date(proposal.expiresAt).getTime() <= nowMs.value
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
      if (status === 'Dismissed') return false
      if (isProposalExpired(proposal)) return true
      if (isProposalDeferred(proposal) && proposal.id !== hashTargetId) return false
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

    return [
      { id: 'pending-review', label: 'Pending review', value: pendingReview, helper: 'Changes waiting for an explicit decision.' },
      { id: 'ready-to-execute', label: 'Ready to execute', value: readyToExecute, helper: 'Approved proposals that can now land on boards.' },
      { id: 'capture-linked', label: 'Capture-linked', value: captureLinked, helper: 'Review items that came through the inbox loop.' },
      { id: 'applied', label: 'Applied', value: appliedRecently, helper: 'Proposals already executed successfully.' },
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
    proposals.value
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
    const element = document.getElementById(`proposal-${proposalId}`)
    element?.scrollIntoView({ block: 'nearest' })
  }

  function upsertProposal(proposal: ApiProposal) {
    const existingIndex = proposals.value.findIndex((current) => current.id === proposal.id)
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
    if (!proposalId) return

    const currentProposal = proposals.value.find((p) => p.id === proposalId)
    if (currentProposal) {
      if (!matchesActiveBoardFilter(currentProposal.boardId)) {
        await safeReplace({ name: 'workspace-review', query: route.query })
        return
      }
      await scrollToProposalFromHash()
      return
    }

    try {
      const fetchedProposal = await automationApi.getProposal(proposalId)
      if (getProposalIdFromHash(route.hash) !== proposalId) return
      if (!matchesActiveBoardFilter(fetchedProposal.boardId)) {
        await safeReplace({ name: 'workspace-review', query: route.query })
        return
      }
      upsertProposal(fetchedProposal)
      await nextTick()
      await scrollToProposalFromHash()
    } catch (e: unknown) {
      if (getProposalIdFromHash(route.hash) !== proposalId) return
      if (isHttpNotFound(e)) {
        await safeReplace({ name: 'workspace-review', query: route.query })
        return
      }
      toast.error(getErrorDisplay(e, 'Failed to load proposal').message)
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
    if (getProposalIdFromHash(route.hash) !== proposalId) return
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
      toast.error(getErrorDisplay(e, 'Failed to load proposals').message)
    } finally {
      if (requestId === latestProposalLoadRequestId) proposalsLoading.value = false
      reviewLoadPerf.end()
    }

    if (requestId === latestProposalLoadRequestId) {
      await openProposalFromHash()
    }
  }

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
    const encodedBoardId = boardId ? encodeURIComponent(boardId) : null
    const query = encodedBoardId ? `?boardId=${encodedBoardId}` : ''
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
    const query = proposal.boardId ?? activeBoardFilter.value
    const encodedProposalId = encodeURIComponent(proposal.id)
    return query
      ? `/workspace/review?boardId=${encodeURIComponent(query)}#proposal-${encodedProposalId}`
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

  function clearBoardFilter() {
    boardFilterInput.value = ''
    safeNavigate({ name: 'workspace-review' })
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
    availableBoards,
    loadingBoards,
    boardFilterInput,
    activeBoardFilter,
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
    loadBoardOptions,
    startClock,
    stopClock,
    openInbox,
    proposalHref,
    captureHrefForProposal,
    openRoute,
    openBoard,
    applyBoardFilter,
    clearBoardFilter,
  }
}
