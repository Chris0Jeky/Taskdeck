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
 * A background refresh should never occupy the review surface indefinitely.
 * This is deliberately below the 15 s poll cadence, leaving each later tick a
 * chance to recover instead of accumulating hung reads.
 */
export const REVIEW_QUEUE_REQUEST_DEADLINE_MS = 8_000

/**
 * One missed poll is not useful user-facing information. After three
 * consecutive transient failures, retain the last trustworthy queue but say
 * that it may no longer be current.
 */
export const REVIEW_QUEUE_CONSECUTIVE_FAILURE_THRESHOLD = 3

/**
 * A tagged result for callers that must know whether an explicit queue read
 * actually became the rendered authority. The ordinary `loadProposals` wrapper
 * intentionally keeps its historical `Promise<void>` contract for action
 * composables that only need a best-effort refresh.
 */
export type ProposalLoadOutcome = 'landed' | 'failed' | 'superseded' | 'aborted'

/**
 * Cancellation and retry controls for an explicit queue read whose caller owns
 * a deadline.
 *
 * `aborted` is reported instead of `failed` so a caller that cancelled its own
 * read never mistakes it for a server or transport failure: only the caller
 * knows why it aborted, and only the caller can tell a deadline apart from any
 * other cancellation. The composable deliberately makes no such judgement.
 *
 * `skipRetry` matters for a deadline-bounded caller: the shared interceptor
 * retries an idempotent read up to `MAX_RETRIES` times with doubling backoff,
 * which can consume most of a caller's budget and turn a recoverable failure
 * into a reported timeout. A bounded caller wants the first honest answer.
 */
export interface ProposalLoadOptions {
  signal?: AbortSignal
  skipRetry?: boolean
}

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

/**
 * A 403 on the review-queue read means board access was revoked, not a blip.
 * Deliberately narrower than `isAccessDeniedError` (403 OR 404): a 404 from the
 * LIST endpoint is not a permission signal, and treating it as one would tear
 * down a queue over a routing mistake.
 */
function isForbiddenError(err: unknown): boolean {
  if (typeof err !== 'object' || err === null) return false
  return (err as { response?: { status?: number } }).response?.status === 403
}

/**
 * A 400 on a PROPOSAL-LEVEL read means the id the URL names is not one this
 * route can bind: `GetProposal(Guid id)` under `[ApiController]` answers a
 * non-GUID `#proposal-<id>` with a model-binding 400 before the handler runs,
 * and nothing client-side validates the hash. That is a permanent fact about
 * the requested target, exactly like a 403 or a 404 — no later tick makes a
 * malformed id readable. Routing it to the queue-level failure branch instead
 * threw away a list answer that had already arrived, and did so silently: a 400
 * is not transient, so the failure counter reset rather than climbing to the
 * degraded threshold, and the refresh froze with no indication (#2214 item 8).
 *
 * Deliberately 400 alone. Only a malformed id is provably unusable; a 405 would
 * be a routing defect affecting the whole surface rather than one target, and
 * "gone" is already what a 404 says here. Neither is emitted by this route, so
 * giving them pin-level meaning would be guessing.
 *
 * Sound for the BY-ID leg only, and it must not be moved to the outer catch: a
 * 400 from the LIST read means the query was rejected (a malformed `boardId`,
 * say), which says nothing about any pinned target and would silently downgrade
 * a whole-queue failure into a pin-level outcome.
 */
function isMalformedTargetError(err: unknown): boolean {
  if (typeof err !== 'object' || err === null) return false
  return (err as { response?: { status?: number } }).response?.status === 400
}

function isTransientQueueRefreshFailure(err: unknown): boolean {
  const status = (err as { response?: { status?: number } } | null)?.response?.status
  // Network failures and deadline errors have no HTTP response. For HTTP,
  // retain the usual retry-safe family rather than mislabelling a malformed
  // request or another authoritative 4xx as a stale queue.
  return status === undefined || status === 408 || status === 429 || status >= 500
}

/**
 * A LIST read the server ANSWERED and refused (#2214 item 2).
 *
 * The transient predicate above deliberately excludes this family, and
 * `recordQueueRefreshFailure` then resets its run and returns — which leaves
 * the worst failure mode of all completely silent. `?boardId=not-a-guid`
 * reaches `GetProposals([FromQuery] Guid? boardId)` under `[ApiController]`
 * (`normalizeBoardIdQueryParam` only trims), so it is a model-binding 400 on
 * EVERY tick: the poll keeps running, the counter keeps resetting, no degraded
 * state ever rises, and the surface goes on showing rows the server has not
 * confirmed since the reviewer arrived. A 404, 405 or 410 on the same read is
 * the same class of fact — the query is being refused, and no later tick makes
 * it acceptable.
 *
 * Excluded, both with an owner elsewhere:
 *  - 403 is the authority path. `refreshProposals` intercepts it before this
 *    accounting is reached: it clears the queue, raises `queueAccessRevoked`
 *    and suspends the poll. Counting it here as well would put two disclosures
 *    on screen for one fact, one of which ("reload, or check the board
 *    filter") is wrong for revoked access.
 *  - 401 is `api/http.ts`'s: it clears the session and redirects to login.
 *    Telling a reviewer on their way out to check the board filter is false.
 *
 * 408 and 429 need no exclusion — they are transient above, and this predicate
 * is only consulted for failures that are not.
 */
function isRefusedQueueRefreshFailure(err: unknown): boolean {
  if (isTransientQueueRefreshFailure(err)) return false
  const status = (err as { response?: { status?: number } } | null)?.response?.status
  if (status === undefined || status === 401 || status === 403) return false
  return status >= 400 && status < 500
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

/**
 * WHICH disclosure a standing recovery sentence retracts (#2638 item 2).
 *
 * The two are not interchangeable, because they are retracted on different
 * evidence. 'degraded' ends the transient state, which is a claim about the
 * RENDERED QUEUE, so its sentence may say the rows on screen are current: it is
 * only raised by a read that completed and assigned `proposals.value`.
 * 'refused' ends the refusal disclosure, which is a claim about the LIST
 * REQUEST alone: it is raised the moment the list leg answers, on a tick whose
 * composite read may still bail at the pin leg without replacing the queue, so
 * its sentence must say the server is accepting refreshes again and NOTHING
 * about the contents (#2214, from PR #2694's round-2 verification: the shared
 * sentence's second clause overclaimed for up to two poll intervals on a
 * list-success/pin-fail loop).
 */
export type QueueRecoveryKind = 'degraded' | 'refused'

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
  // A deep link is an explicit request, not a selection preference. Hold the
  // requested id separately whenever the server has settled its fate, so the
  // surfaces can say what happened instead of presenting the ordinary empty
  // queue or a different actionable proposal. Three outcomes reach it, all
  // permanent facts about that one target rather than about the queue: a 404
  // (no such proposal, or it is gone), a 403 on the by-id read (this reviewer
  // may not see it), and a 400 (the id in the hash is not one the by-id route
  // can bind). A wrong-identity or cross-board answer fails closed into it too.
  // Explicit navigation is narrower on purpose and still marks only the 404.
  const unavailableProposalId = ref<string | null>(null)
  // WHY that pin is unavailable, because the id alone collapses two different
  // truths (#2214). A 403 or a 404 is about a proposal that exists or existed;
  // a 400 is the by-id route refusing to bind the id at all, so the link never
  // named a proposal and no later tick makes it work. Rendering "it may have
  // been applied, archived, or removed" for the second sends the reviewer to
  // wait for a recovery that cannot arrive.
  //
  // Kept in lockstep with the id through `markProposalUnavailable` /
  // `clearProposalUnavailable` rather than assigned at each of the write sites:
  // a reason that outlived its id would label the NEXT unavailable pin.
  const unavailableProposalMalformed = ref(false)

  function markProposalUnavailable(proposalId: string, reason: 'malformed' | 'refused') {
    unavailableProposalId.value = proposalId
    unavailableProposalMalformed.value = reason === 'malformed'
  }

  function clearProposalUnavailable() {
    unavailableProposalId.value = null
    unavailableProposalMalformed.value = false
  }
  // Set when a background read is refused with 403 (board access revoked
  // mid-session). The surfaces swap the ordinary "Nothing waiting" empty state
  // for an honest one -- clearing the queue without saying why would turn a
  // permission failure into a fresh false negative, which is the exact class
  // #2194 exists to remove.
  const queueAccessRevoked = ref(false)
  // Unlike `queueAccessRevoked`, this is not an authority result. It means the
  // last queue we could render is still shown while background reads retry.
  const queueRefreshStale = ref(false)
  // The second, SEPARATE threshold (#2214 item 2). `queueRefreshStale` belongs
  // to the transient counter and must keep belonging to it: "the network keeps
  // blipping, we are retrying" and "the server is answering and refusing the
  // query" are different facts with different remedies, and a reviewer who is
  // told the first while the second is true will wait for a recovery that
  // cannot arrive. Both can stand at once; the surfaces show the refusal,
  // which is the stronger and more actionable statement.
  const queueRefreshRefused = ref(false)
  // The EVENT that pairs with the two states above (#2214), and WHICH of them
  // it retracts (#2638 item 2). Clearing `queueRefreshStale` or
  // `queueRefreshRefused` unmounts the warning, which is silent: a reviewer who
  // was not looking at that corner is never told the queue is trustworthy
  // again, and a screen-reader user is told nothing at all. A kind is set only
  // after a disclosure was actually retracted by a successful read, so an
  // ordinary success never announces anything, and it falls back to null at the
  // next degraded or refusal onset so a second recovery is announced too (a
  // live region only speaks when its TEXT changes).
  //
  // It is deliberately NOT cleared on a 403: the surfaces already gate this
  // sentence on `!queueAccessRevoked`, the same guard the warning uses, so the
  // permission path keeps its single owner.
  const queueRefreshRecoveredKind = ref<QueueRecoveryKind | null>(null)
  const queueRefreshRecovered = computed(() => queueRefreshRecoveredKind.value !== null)
  // WHEN the sentence was raised, counted in BACKGROUND reads (#2638 item 2).
  //
  // THE RETIREMENT RULE: a standing recovery sentence is retired by the next
  // degraded or refusal onset, or by a BACKGROUND poll success belonging to a
  // read LATER than the one the sentence is stamped with — never by an explicit
  // load. The two stamps are set out below; the onsets are immediate for both,
  // because there the sentence is false rather than merely old.
  //
  // Explicit loads take the same `recordQueueRefreshSuccess` path and are
  // common within one poll interval of a recovery: the post-decision reloads in
  // useReviewActions, the batch composables, the board-filter watcher,
  // `dismissSettledElsewhereNotice` and the pre-decision refresh barrier all
  // call `loadProposals`. A reviewer's already-clicked Approve can therefore
  // land ~150 ms after the recovering poll and empty the region, and a polite
  // live region whose text is reverted that fast may never be spoken at all
  // (#2638 item 2, from PR #2630's verification pass). An explicit load that
  // succeeds while the sentence stands leaves it standing; it still RAISES one
  // when it is the read that ends a degraded state, which is the #2630
  // behaviour and is unchanged.
  //
  // Counting background reads rather than wall-clock keeps the composable free
  // of teardown state and still gives the sentence at least one full poll
  // interval of life, which is what #2630 intended:
  //
  //  - a POLL-raised sentence is stamped with the read it was raised in, so it
  //    survives that read and retires on the next later poll success;
  //  - an EXPLICIT-raised sentence is stamped with the read that has not
  //    started yet, so it survives the next poll success and retires on the one
  //    after.
  //
  // The asymmetry is the whole point (round-2 review finding). An explicit load
  // lands BETWEEN background reads, so the ordinal sitting on the counter names
  // a read that already finished: stamping that would let the very next tick
  // retire the sentence, and a post-decision reload that recovers the queue
  // 14.9 s into a 15 s cycle would be blanked 100 ms later — the same defect
  // this rule exists to close, with the roles swapped.
  //
  // One reachability-limited gap is accepted: if the poll never records another
  // COMPOSITE success — a hash-pinned by-id read failing every tick with a
  // status that is neither transient nor a pin-level outcome, so 405/410/409,
  // which this route is documented not to emit — nothing retires the sentence
  // for the rest of the session except a degraded or refusal onset, because an
  // explicit success no longer bounds it either.
  let backgroundQueueReadCount = 0
  let queueRecoveryRaisedAtBackgroundRead: number | null = null

  function raiseQueueRecovery(kind: QueueRecoveryKind, source: 'poll' | 'explicit') {
    queueRefreshRecoveredKind.value = kind
    queueRecoveryRaisedAtBackgroundRead =
      source === 'poll' ? backgroundQueueReadCount : backgroundQueueReadCount + 1
  }

  function retireQueueRecovery() {
    queueRefreshRecoveredKind.value = null
    queueRecoveryRaisedAtBackgroundRead = null
  }
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

  /**
   * The ORDERED ids of the proposals both skins count as "awaiting review"
   * (#2214 item 4).
   *
   * One source for the count AND for its identity (#1124 / ADR-0038): Legacy
   * read the number off `summaryCards`' `pending-review` card and Paper
   * recomputed this same predicate inline, and neither skin had any notion of
   * WHICH proposals the number stood for.
   *
   * That is the whole defect. Both queue live regions rendered a sentence
   * derived from the count alone, and a live region only speaks when its text
   * changes — so a poll that removed one pending proposal and added another in
   * the same response produced a byte-identical "3 proposals awaiting review.",
   * mutated nothing, and announced nothing. The queue moved under the reviewer
   * in silence, which is the false-negative class #2194 exists to remove.
   *
   * Ordered, not a set: the rail renders the queue in this order, so a reorder
   * is a queue that moved. A byte-identical answer is not.
   */
  const awaitingProposalIds = computed(() =>
    visibleProposals.value
      .filter(
        (proposal) =>
          normalizeProposalStatus(proposal.status) === 'PendingReview' &&
          !isProposalExpired(proposal),
      )
      .map((proposal) => proposal.id),
  )

  /**
   * The identity above as one primitive, for use as the `key` of the node each
   * skin's queue live region wraps around its sentence.
   *
   * A KEY rather than part of the spoken text, deliberately. Re-keying replaces
   * that node inside a live region that itself stays mounted, and a node
   * addition is exactly what `aria-live`'s default
   * `aria-relevant="additions text"` announces — so the same count-neutral
   * replacement is spoken once, with the sentence and its count unchanged from
   * what #2194 shipped. The alternative, blanking the text for a frame and
   * restoring it, recreates the "inserted together with its text" shape that
   * #2593 and #2630 both call unreliably announced.
   *
   * `\n` cannot appear in a proposal id, so distinct queues cannot collide on
   * one key.
   */
  const queueAnnouncementKey = computed(() => awaitingProposalIds.value.join('\n'))

  /**
   * The board scope of a queue read, as one comparable value. Lower-cased
   * because the scope is the BOARD, not the casing the query string happened to
   * carry — the same rule `matchesActiveBoardFilter` applies — and an empty
   * filter is the unscoped queue, exactly as `boardId: activeBoardFilter.value
   * || undefined` sends it.
   */
  function queueScopeOf(boardId: string | null | undefined): string | null {
    return boardId ? boardId.toLowerCase() : null
  }

  /**
   * The scope a queue read has actually LANDED for, or `undefined` while no read
   * has landed at all.
   */
  const landedQueueScope = ref<string | null | undefined>(undefined)

  /**
   * Whether a queue read has landed for the board scope currently on screen
   * (#2599 item 1). This is what the two skins' announcement gates need, and
   * `!proposalsLoading` was the wrong approximation of it.
   *
   * An explicit `loadProposals` raises `proposalsLoading` WITHOUT clearing
   * `proposals`, so gating on it unmounted the announcement node for the length
   * of every reload and remounted it with the identical sentence: the live
   * region wrote count -> '' -> count, and a node addition is exactly what
   * `aria-live` speaks. The reviewer heard the same figure read back after the
   * header Refresh and after filing away a settled proposal — reads they asked
   * for, about a queue that had not moved.
   *
   * The states this DOES withhold, all of them cases where the rendered count
   * is not a count of the queue on screen:
   *  - before the first read lands (the #2593 skeleton gate — the count is 0
   *    because nothing has been read, not because nothing awaits review);
   *  - after a board-filter change, until the new scope's read lands: the rows
   *    still rendered belong to the previous board;
   *  - for a scope no read has landed for, including one whose only read
   *    failed: the entry load, or the first read after a filter change.
   *
   * What it deliberately does NOT withhold is the count after a LATER read
   * failed. `landedQueueScope` is written only where a read replaced the queue
   * and cleared only by `recordQueueAccessRevoked`; the catch arm leaves it
   * alone. So an entry load that landed for board A followed by a header
   * Refresh that 500s keeps the count announceable — correctly, because the
   * rows on screen are still that landed answer. Re-withholding there would put
   * back the count -> '' -> count flicker this signal exists to remove, on a
   * read that changed nothing. A failing refresh has its own reports: the
   * degraded and refused disclosures (#2214).
   *
   * A same-scope reload keeps it settled, because the queue it is about is
   * still the one being counted. `queueAccessRevoked` keeps its own separate
   * gate: a revocation is a different fact with a different remedy.
   */
  const queueScopeLoaded = computed(
    () =>
      landedQueueScope.value !== undefined &&
      landedQueueScope.value === queueScopeOf(activeBoardFilter.value),
  )

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

  async function openProposalFromHash(options?: ProposalLoadOptions) {
    if (proposalsLoading.value) return
    const proposalId = getProposalIdFromHash(route.hash)
    if (!proposalId) {
      clearProposalUnavailable()
      return
    }

    // A different hash starts a fresh lookup. Keep the current unavailable
    // state only while it still describes the route the user asked for.
    if (!proposalIdsEqual(unavailableProposalId.value, proposalId)) {
      clearProposalUnavailable()
    }

    const currentProposal = proposals.value.find((p) => proposalIdsEqual(p.id, proposalId))
    if (currentProposal) {
      if (!matchesActiveBoardFilter(currentProposal.boardId)) {
        return
      }
      clearProposalUnavailable()
      await scrollToProposalFromHash()
      return
    }

    try {
      const fetchedProposal = options
        ? await automationApi.getProposal(proposalId, {
            signal: options.signal,
            skipRetry: options.skipRetry,
          })
        : await automationApi.getProposal(proposalId)
      if (!proposalIdsEqual(getProposalIdFromHash(route.hash), proposalId)) return
      // A route lookup may canonicalize GUID hex casing, but it may not return a
      // different record. Retain the hash as unavailable instead of upserting a
      // response whose identity does not match the requested proposal.
      if (!proposalIdsEqual(fetchedProposal.id, proposalId)) {
        // A wrong record is not a broken address: the id bound fine, the
        // server simply answered with something else.
        markProposalUnavailable(proposalId, 'refused')
        return
      }
      if (!matchesActiveBoardFilter(fetchedProposal.boardId)) {
        return
      }
      upsertProposal(fetchedProposal)
      clearProposalUnavailable()
      await nextTick()
      await scrollToProposalFromHash()
    } catch (e: unknown) {
      // A caller-owned cancellation is not a lookup failure: the deep-link read
      // was cut short deliberately and its caller reports the real outcome.
      if (options?.signal?.aborted) return
      if (!proposalIdsEqual(getProposalIdFromHash(route.hash), proposalId)) return
      // One outcome per status CLASS, using exactly the three predicates the
      // background pin leg uses, so the two paths cannot answer the same fact
      // two different ways (#2214). Before this, a 400 or a 403 on the
      // reviewer's own deep-link read raised a generic "Failed to load
      // proposal" toast and set no state at all: the surface fell back to the
      // ordinary empty queue, and the very next background tick converted the
      // identical refusal into the pin-unavailable panel. The toast named
      // neither fact and was gone seconds later, contradicted by a panel that
      // stayed.
      //
      // A settled fact about the target gets the panel and no toast, because
      // the panel is the durable report and two reports for one fact is the
      // asymmetry being removed. 404 already behaved this way.
      if (isMalformedTargetError(e)) {
        markProposalUnavailable(proposalId, 'malformed')
        return
      }
      if (isForbiddenError(e) || isHttpNotFound(e)) {
        // By-id authority over ONE target. The queue-level 403 and its
        // `queueAccessRevoked` teardown live on the list leg and are untouched.
        markProposalUnavailable(proposalId, 'refused')
        return
      }
      // 405, 410, 5xx and no response are not facts about this target — 405
      // and 410 are the route misbehaving rather than the id being refused
      // (#2658 draws the same line on the pin leg), and the rest may resolve on
      // a later tick. Pinning the target unavailable would be a false negative,
      // so the reviewer who asked keeps getting told the read failed.
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

  async function loadProposalsWithOutcome(
    options?: ProposalLoadOptions,
  ): Promise<ProposalLoadOutcome> {
    const signal = options?.signal
    if (signal?.aborted) return 'aborted'
    reviewLoadPerf.start()
    const requestId = ++latestProposalLoadRequestId
    let outcome: ProposalLoadOutcome = 'landed'

    try {
      proposalsLoading.value = true
      const filters = {
        limit: 200,
        boardId: activeBoardFilter.value || undefined,
      }
      // The scope this read is ASKING about, snapshotted before the await. A
      // late answer describes the board it queried, never whichever board is on
      // screen when it lands (#2599 item 1).
      const requestedScope = queueScopeOf(filters.boardId)
      // The second argument is forwarded ONLY when a caller supplied options,
      // so every existing call site keeps its exact single-argument shape.
      const loadedProposals = options
        ? await automationApi.getProposals(filters, {
            signal,
            skipRetry: options.skipRetry,
          })
        : await automationApi.getProposals(filters)
      if (requestId !== latestProposalLoadRequestId) return 'superseded'
      // An answer the caller stopped waiting for must not become the rendered
      // authority behind its back, and proves nothing about queue freshness.
      if (signal?.aborted) return 'aborted'
      proposals.value = loadedProposals
      // A read has now landed for that scope, so the count the surfaces render
      // is a real count of the queue on screen and may be announced (#2599
      // item 1).
      landedQueueScope.value = requestedScope
      // An explicit successful load is as trustworthy as a successful poll and
      // clears any older degraded indication without changing load semantics.
      // It goes through the same accounting as a successful poll so both exits
      // from the degraded state raise the recovery signal (#2214) — but it
      // never RETIRES a standing one, because it can land a few hundred
      // milliseconds after the poll that raised it and blank the live region
      // before it is spoken (#2638 item 2). No `source` is passed: the default
      // is 'explicit'.
      recordQueueRefreshSuccess()
      // An explicit load that succeeded is proof access is back.
      const accessWasRevoked = queueAccessRevoked.value
      queueAccessRevoked.value = false
      if (accessWasRevoked) resumeQueueRefreshAfterPermissionRecovery()
    } catch (e: unknown) {
      if (requestId !== latestProposalLoadRequestId) return 'superseded'
      // Cancellation by the caller's own deadline is not a queue failure. It
      // must not raise the failure toast, and it must not be reported as
      // `failed`, or the caller would blame the server for its own timeout.
      if (signal?.aborted) return 'aborted'
      // Only the POLL used to handle this, so a cold entry to a board whose
      // access had been revoked fell through to the generic toast and left the
      // authority state unset for a whole poll interval (round-2 review
      // finding). Worse, the hash lookup below then 403'd on the by-id read
      // and, since this slice made that a pin-level outcome, rendered "no
      // longer available to review; it may have been applied, archived, or
      // removed" about a proposal that was none of those things -- the board
      // simply was not this reviewer's any more.
      //
      //
      // ONE report for one fact (#2214, from PR #2694's round-2 verification).
      // `recordQueueAccessRevoked` raises a DURABLE panel that is the first
      // branch of both skins' empty chains and names the revocation and its
      // remedy; the generic "Failed to load proposals" toast beside it named
      // neither, was gone seconds later, and was contradicted by a panel that
      // stayed — the same two-reports-for-one-fact shape #2694 removed on the
      // pin leg. Every OTHER explicit failure keeps its toast: this read is one
      // the caller asked for, and nothing else on screen reports it.
      //
      // The outcome contract is untouched either way: every action composable
      // that calls `loadProposals` still gets its failure signal and its
      // 'failed' outcome.
      if (isForbiddenError(e)) {
        recordQueueAccessRevoked()
      } else {
        toast.error(getErrorDisplay(e, t('review.toast.loadProposalsFailed')).message)
      }
      outcome = 'failed'
    } finally {
      if (requestId === latestProposalLoadRequestId) proposalsLoading.value = false
      reviewLoadPerf.end()
    }

    if (signal?.aborted) return 'aborted'
    // A revoked queue has one owner and one explanation. Re-authorising a
    // hash-pinned row inside a board the server just refused wholesale can only
    // produce a second, narrower and wrong account of the same fact, so the
    // pin-level outcome is not even asked for. A successful load clears
    // `queueAccessRevoked` above, so this can only be true when THIS read
    // revoked it or an earlier one did and nothing has restored access.
    if (requestId === latestProposalLoadRequestId && !queueAccessRevoked.value) {
      await openProposalFromHash(options)
    }
    if (requestId !== latestProposalLoadRequestId) return 'superseded'
    if (signal?.aborted) return 'aborted'
    return outcome
  }

  async function loadProposals(): Promise<void> {
    await loadProposalsWithOutcome()
  }

  // --- Background queue refresh (#2194) ---------------------------------

  let refreshInterval: ReturnType<typeof setInterval> | null = null
  let refreshInFlight = false
  let consecutiveQueueRefreshFailures = 0
  let consecutiveQueueRefreshRefusals = 0
  // A 403 pauses the configured poll without making it forget how the owning
  // surface asked it to behave. Permanent stop/disposal clears this state so a
  // late successful explicit load cannot resurrect a surface that has left.
  let queueRefreshConfigured = false
  let queueRefreshSuspendedForPermission = false
  let shouldRefreshNow: (() => boolean) | null = null
  let refreshAbort: AbortController | null = null

  /**
   * `leg` names which request of the composite background read failed, because
   * the two thresholds ask different questions of it (#2214 item 2).
   *
   * The transient run counts both legs, exactly as it has since #2445. The
   * REFUSAL run is a claim about the LIST read alone, so a pin-leg failure not
   * only fails to count toward it, it breaks it: reaching the by-id request at
   * all means that tick's list read had already succeeded.
   */
  function recordQueueRefreshFailure(err: unknown, leg: 'list' | 'pin') {
    if (leg === 'list' && isRefusedQueueRefreshFailure(err)) {
      consecutiveQueueRefreshRefusals += 1
      if (consecutiveQueueRefreshRefusals >= REVIEW_QUEUE_CONSECUTIVE_FAILURE_THRESHOLD) {
        queueRefreshRefused.value = true
        // Same reason as the degraded onset below: a standing recovery
        // sentence of EITHER kind contradicts the warning beside it, and its
        // unchanged text would silence the next real recovery.
        retireQueueRecovery()
      }
    } else {
      // Anything that is not another qualifying list refusal interrupts the
      // uninterrupted run the disclosure claims. It resets the RUN only: a
      // risen disclosure stays up, because an interruption is not evidence
      // that the retained queue is current (the #2445 ruling, applied
      // symmetrically to this second threshold).
      consecutiveQueueRefreshRefusals = 0
    }
    if (!isTransientQueueRefreshFailure(err)) {
      // A non-transient failure breaks the uninterrupted transient run, but it
      // does not prove that the retained queue is fresh enough to clear a
      // visible degraded indication.
      consecutiveQueueRefreshFailures = 0
      return
    }
    consecutiveQueueRefreshFailures += 1
    if (consecutiveQueueRefreshFailures >= REVIEW_QUEUE_CONSECUTIVE_FAILURE_THRESHOLD) {
      queueRefreshStale.value = true
      // A degraded onset retires the previous recovery announcement, whichever
      // disclosure it retracted. Leaving it standing would both contradict the
      // warning beside it and stop the NEXT recovery from being announced,
      // because the live region's text would never change. This is the one
      // retirement that is deliberately not gated on the read kind (#2638): it
      // is not "the sentence has been up long enough", it is "the sentence is
      // now false".
      retireQueueRecovery()
    }
  }

  /**
   * A 403 on the QUEUE read is revoked board access, not a blip: stop polling
   * an endpoint that will keep refusing, drop rows the server no longer
   * authorises, and let the surface say exactly that.
   *
   * Shared by the poll's outer catch and the explicit load, because a 403 means
   * the same thing whoever asked (round-2 review finding). Duplicating the
   * three statements would be how the two legs drift into telling a reviewer
   * two different stories about one revocation.
   */
  function recordQueueAccessRevoked() {
    queueAccessRevoked.value = true
    proposals.value = []
    // What is rendered is no longer any read's answer, so no read has landed
    // for this scope any more (#2599 item 1). The revoked panel has its own
    // gate on both surfaces; this only stops the count coming back as speakable
    // the moment the panel clears for some other reason.
    landedQueueScope.value = undefined
    suspendQueueRefreshForPermission()
  }

  /**
   * The LIST leg answered, whatever the rest of the composite read goes on to
   * do. Returns whether it raised the recovery sentence.
   *
   * The refusal disclosure's clear is LIST-SCOPED while the transient state's
   * stays COMPOSITE-SCOPED, and the asymmetry is in what each one claims. The
   * refusal says "the server is refusing the refresh rather than failing
   * temporarily", which is a statement about the list REQUEST; one successful
   * list read falsifies it outright, and leaving it up afterwards is simply a
   * lie. The transient state says "the queue you are looking at may be out of
   * date", which is a statement about the RENDERED QUEUE; a composite read that
   * bailed at the pin leg never reached `proposals.value = next`, so the queue
   * on screen really is still the old one and clearing that warning would
   * fabricate a freshness the surface does not have. Same tick, two different
   * claims, two different pieces of evidence — so #2445's composite semantics
   * for the transient counter are deliberately untouched here.
   *
   * `source` is the read this list leg belongs to, and it only decides how long
   * the sentence raised here lives (the retirement rule at
   * `backgroundQueueReadCount`); the retraction itself is the same either way.
   */
  function recordQueueListReadSucceeded(source: 'poll' | 'explicit'): boolean {
    consecutiveQueueRefreshRefusals = 0
    if (!queueRefreshRefused.value) return false
    queueRefreshRefused.value = false
    // Retracting a disclosure silently is exactly the #2630 defect: the warning
    // is simply gone on the next render and a reviewer who was not watching
    // that corner is never told the refusal claim no longer holds.
    //
    // Its OWN sentence, not the queue one (#2638 item 2). All this evidence
    // supports is that the list request is being answered again: the pin leg
    // can still fail below and return before `proposals.value = next`, so
    // "showing current proposals" would be false for up to two more poll
    // intervals (#2214).
    raiseQueueRecovery('refused', source)
    return true
  }

  /**
   * The WHOLE composite read landed.
   *
   * `source` names the read that landed, because only a BACKGROUND one may
   * retire a standing recovery sentence, and because a sentence raised HERE is
   * stamped differently depending on it (#2638 item 2 — see the retirement rule
   * at `backgroundQueueReadCount`). It defaults to 'explicit' so every caller
   * that is not the poll is safe by construction.
   *
   * `recoveryAlreadyRaised` is passed by the poll when
   * `recordQueueListReadSucceeded` already announced a recovery earlier in this
   * same read, so the retirement branch below cannot retire the sentence its
   * own read just raised (#2694). The read counter states the same fact for
   * every kind of read; both are kept because they answer different questions —
   * "did THIS read raise it" and "was it raised by an EARLIER read".
   */
  function recordQueueRefreshSuccess(options?: {
    source?: 'poll' | 'explicit'
    recoveryAlreadyRaised?: boolean
  }) {
    const source = options?.source ?? 'explicit'
    consecutiveQueueRefreshFailures = 0
    // Idempotent: a no-op when the poll already ran it at the list-success
    // point, and the whole clear when an explicit load lands.
    const recoveryRaisedThisRead =
      recordQueueListReadSucceeded(source) || options?.recoveryAlreadyRaised === true
    // The QUEUE sentence is spoken only when a read that COMPLETED ended a
    // visible degraded state: `proposals.value` was replaced just above, so
    // "showing current proposals" is provable. Raising it on every success
    // would make both skins announce every 15 s, and raising it without the
    // degraded state having been up would announce a recovery from nothing.
    //
    // It therefore also overwrites a 'refused' kind raised moments earlier in
    // this same read, which is the stronger and now-provable statement. When
    // NO degraded state was up, a refusal cleared by this same completed read
    // keeps the refusal sentence instead: an under-claim, never a false one —
    // it says the server is accepting refreshes again and stays silent about
    // rows the reviewer can see for themselves.
    if (queueRefreshStale.value) {
      raiseQueueRecovery('degraded', source)
    } else if (
      source === 'poll' &&
      !recoveryRaisedThisRead &&
      queueRecoveryRaisedAtBackgroundRead !== null &&
      backgroundQueueReadCount > queueRecoveryRaisedAtBackgroundRead
    ) {
      // A LATER background success retires the sentence, so it lives for about
      // one poll interval instead of the whole session. An announcement is an
      // event; leaving its text standing indefinitely turns it into a claim
      // about the present that nothing is re-checking. Clearing it here rather
      // than on a timer keeps the composable free of teardown state, and the
      // clear is silent: a live region going empty announces nothing.
      retireQueueRecovery()
    }
    queueRefreshStale.value = false
  }

  /**
   * Bounds one request in a background composite read. Relying on AbortSignal
   * alone is insufficient: a transport or test double may ignore it and leave
   * the poll in flight forever. Racing the deadline also makes a late response
   * harmless because the shared controller is aborted before it can land.
   */
  async function awaitQueueRefreshRequest<T>(
    request: Promise<T>,
    controller: AbortController,
    onDeadline: () => void,
  ): Promise<T> {
    let deadlineTimer: ReturnType<typeof setTimeout> | null = null
    let rejectOnAbort: (() => void) | null = null
    const deadline = new Promise<never>((_resolve, reject) => {
      deadlineTimer = setTimeout(() => {
        onDeadline()
        controller.abort()
        reject(new Error('Review queue background request exceeded its deadline.'))
      }, REVIEW_QUEUE_REQUEST_DEADLINE_MS)
    })
    const aborted = new Promise<never>((_resolve, reject) => {
      rejectOnAbort = () => reject(new Error('Review queue background request was aborted.'))
      controller.signal.addEventListener('abort', rejectOnAbort, { once: true })
    })

    try {
      return await Promise.race([request, deadline, aborted])
    } finally {
      if (deadlineTimer !== null) clearTimeout(deadlineTimer)
      if (rejectOnAbort) controller.signal.removeEventListener('abort', rejectOnAbort)
    }
  }
  /**
   * Called synchronously immediately AFTER a background poll's answer has
   * replaced the queue (#2215 A). It is the only signal a surface has that the
   * queue moved without the reviewer asking: every reviewer-driven path (rail
   * click, filter change, deep link, decision) runs through the surface's own
   * handlers, while this one lands on its own.
   *
   * Deliberately fired AFTER the assignment rather than before it, so a surface
   * whose selection is a computed over `proposals` can compare the selection
   * Vue is about to render against the one its watcher reports as previous.
   */
  let onQueueReplacedByPoll: (() => void) | null = null
  // Bumped by any out-of-band write that a queue read started earlier would
  // clobber. The array-identity guard cannot catch every such write: saving a
  // proposal revision updates `useProposalRevisions` state and never touches
  // `proposals`, so a GET issued before the save would sail through the identity
  // check and restore the pre-revision summary, operations and latestRevisionId.
  let queueWriteGeneration = 0

  /**
   * Invalidate every queue read currently in flight. Callers are surfaces that
   * have just written something a pre-write read would undo -- today that is a
   * saved proposal revision. Deliberately NOT `latestProposalLoadRequestId`:
   * bumping that counter would make an in-flight `loadProposals` discard its own
   * result AND skip the `proposalsLoading = false` reset in its finally block,
   * wedging the surface in a permanent loading state.
   */
  function invalidateQueueReads() {
    queueWriteGeneration += 1
  }

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
   * deep-link target may legitimately sit outside the list page. When the list
   * omits that pinned row, its by-id read must re-authorise and refresh it before
   * anything lands; restoring the cached DTO would preserve revoked access or
   * stale revision content.
   */
  async function refreshProposals(): Promise<void> {
    // An explicit load is authoritative and about to replace the list wholesale.
    if (proposalsLoading.value || refreshInFlight) return
    // This read's ordinal among background reads (#2638 item 2). Counted at the
    // top, before any await, so a recovery raised anywhere inside this read
    // belongs to THIS number and only a later read can retire it. Reads that
    // return here never started, so they cannot age a standing sentence.
    backgroundQueueReadCount += 1
    // Snapshot the load counter rather than incrementing it: bumping it here
    // would make an in-flight `loadProposals` discard its own result AND skip
    // the `proposalsLoading = false` reset in its finally block, wedging the
    // surface in a permanent loading state.
    const observedLoadId = latestProposalLoadRequestId
    // Identity snapshot of the queue. EVERY single-proposal decision path
    // (approve/reject/defer/execute in useReviewActions) patches the row locally
    // with `proposals.value = proposals.value.map(...)`, assigning a NEW array
    // and never touching the load counter. So a read issued BEFORE the click can
    // resolve AFTER it, and writing its answer would revert the row to
    // PendingReview while the decision receipt on screen says approved. A changed
    // reference is the signal that a decision landed under this read.
    const observedProposals = proposals.value
    const observedWriteGeneration = queueWriteGeneration
    // The scope this read is ASKING about. A late answer -- success or 403 --
    // describes the board it queried, never whichever board is on screen now.
    const requestedBoardId = activeBoardFilter.value || null
    // A hash target is part of the question too. Hash navigation does not start
    // a queue load, so it needs its own snapshot to stop an old by-id answer from
    // inserting or marking unavailable whichever proposal is selected next.
    const requestedHash = route.hash
    const hashTargetId = getProposalIdFromHash(requestedHash)
    const controller = new AbortController()
    let refreshTimedOut = false
    // A list refusal is scoped to the queue query, not to whichever proposal is
    // selected. Hash navigation must not erase authority from a current-board
    // 403, while a load or board change still supersedes it.
    const isSupersededQueueRead = () =>
      observedLoadId !== latestProposalLoadRequestId ||
      proposalsLoading.value ||
      (activeBoardFilter.value || null) !== requestedBoardId
    const isSupersededCompositeRead = () =>
      isSupersededQueueRead() || route.hash !== requestedHash
    // The list and optional by-id request form one composite read. Every guard
    // is deliberately re-run after BOTH awaits so the second response cannot
    // bypass the protections already required for the list response.
    const isCurrentRead = () =>
      !controller.signal.aborted &&
      !isSupersededCompositeRead() &&
      proposals.value === observedProposals &&
      observedWriteGeneration === queueWriteGeneration &&
      (!shouldRefreshNow || shouldRefreshNow())
    refreshAbort = controller
    refreshInFlight = true
    try {
      const loadedProposals = await awaitQueueRefreshRequest(
        automationApi.getProposals(
          {
            limit: 200,
            boardId: activeBoardFilter.value || undefined,
          },
          // Fail fast and stay cancellable. The shared interceptor retries a
          // transient failure three times with backoff, which would keep a dead
          // poll running for seconds past the tick that asked for it and land its
          // answer arbitrarily late; `stopQueueRefresh` aborts whatever is open.
          { skipRetry: true, signal: controller.signal },
        ),
        controller,
        () => { refreshTimedOut = true },
      )
      if (!isCurrentRead()) return
      // The list leg answered and this read is still the current question, so
      // the refusal claim is falsified NOW -- before the pin leg gets a chance
      // to return early and strand it (round-2 review finding). The transient
      // accounting deliberately stays below, on the composite outcome.
      const listRecoveryRaised = recordQueueListReadSucceeded('poll')
      const next = [...loadedProposals]
      let pinUnavailable = false
      let pinMalformed = false
      if (
        hashTargetId &&
        !next.some((proposal) => proposalIdsEqual(proposal.id, hashTargetId))
      ) {
        try {
          const currentPinnedProposal = await awaitQueueRefreshRequest(
            automationApi.getProposal(
              hashTargetId,
              // This is part of the background poll, not an explicit navigation.
              // Keep it in the same cancellation and fail-fast envelope as the
              // list request so teardown cannot leave a retry chain behind.
              //
              // The three statuses below are this call's own contract, not
              // failures: each one is turned into the explicit "pin
              // unavailable" outcome a few lines down. Without naming them the
              // shared interceptor logs every refused or unbindable pin as
              // 'API Error:' on every tick, which reports a handled result as a
              // defect and buries the real ones (#2214 item 7). Scoped to this
              // background read alone -- `openProposalFromHash` is a read the
              // reviewer asked for and keeps its logging.
              {
                skipRetry: true,
                signal: controller.signal,
                expectedStatuses: [400, 403, 404],
              },
            ),
            controller,
            () => { refreshTimedOut = true },
          )
          if (!isCurrentRead()) return

          if (
            !proposalIdsEqual(currentPinnedProposal.id, hashTargetId) ||
            !matchesActiveBoardFilter(currentPinnedProposal.boardId)
          ) {
            // A wrong identity or cross-scope record is not authority to keep
            // showing the cached pin. Accept the readable list and fail closed
            // for this requested target.
            pinUnavailable = true
          } else {
            // Reinsert the current DTO at its createdAt position, by the same
            // rule `upsertProposal` uses. Appending would visibly move the row
            // under review to the bottom of the rail.
            const pinnedCreatedAt = new Date(currentPinnedProposal.createdAt).getTime()
            const insertIndex = next.findIndex(
              (current) => new Date(current.createdAt).getTime() < pinnedCreatedAt,
            )
            if (insertIndex >= 0) next.splice(insertIndex, 0, currentPinnedProposal)
            else next.push(currentPinnedProposal)
          }
        } catch (e: unknown) {
          // Teardown/supersession aborts are not failures. A deadline also
          // aborts the controller, so let that path fall through to the
          // transient accounting below.
          if (controller.signal.aborted && !refreshTimedOut) return
          if (!isCurrentRead() && !refreshTimedOut) return
          if (isForbiddenError(e) || isHttpNotFound(e) || isMalformedTargetError(e)) {
            // The list itself succeeded, so only the pin is unavailable. Do not
            // turn a proposal-level refusal — or a target this route cannot even
            // bind — into whole-queue revocation, and do not discard a queue
            // answer that already arrived.
            pinUnavailable = true
            // Only the 400 says the ADDRESS is wrong. A 403 or a 404 is about a
            // proposal that exists or existed, and the surfaces say different
            // things about the two (#2214).
            pinMalformed = isMalformedTargetError(e)
          } else {
            // The composite read is incomplete. Preserve the exact queue and
            // availability state currently rendered; a later tick can retry.
            if (isSupersededQueueRead()) return
            recordQueueRefreshFailure(e, 'pin')
            logError('Review deep-link background refresh failed:', e)
            return
          }
        }
      }

      if (!isCurrentRead()) return
      if (hashTargetId) {
        if (pinUnavailable) {
          markProposalUnavailable(hashTargetId, pinMalformed ? 'malformed' : 'refused')
        } else if (proposalIdsEqual(unavailableProposalId.value, hashTargetId)) {
          clearProposalUnavailable()
        }
      }
      proposals.value = next
      // `isCurrentRead` has already refused any answer whose scope moved, so
      // this read landed for the board on screen (#2599 item 1). The poll is a
      // landing site in its own right: after a failed entry load, it is what
      // makes the count speakable again without the reviewer reloading.
      landedQueueScope.value = queueScopeOf(requestedBoardId)
      recordQueueRefreshSuccess({ source: 'poll', recoveryAlreadyRaised: listRecoveryRaised })
      // The queue moved under a reviewer who did not ask for it. Surfaces use
      // this to notice that the row they were rendering has just been dropped
      // or reordered away, instead of silently sliding onto another one
      // (#2215 A).
      // A refused or invalid deep-link target has its own explicit unavailable
      // state. Arming the settled-row notice as well would let that notice win
      // the render branch and hide the authoritative pin-level outcome.
      if (!pinUnavailable) onQueueReplacedByPoll?.()
    } catch (e: unknown) {
      // An abort is this surface's own teardown, not a failure.
      if (controller.signal.aborted && !refreshTimedOut) return
      if (isForbiddenError(e)) {
        // Only trust a 403 that answers the question currently being asked.
        // Without this, a late refusal from a board the reviewer just left would
        // wipe the board they moved to and stop polling a scope that is fine.
        if (isSupersededQueueRead()) return
        // Board access was revoked. Stop polling rather than hammering an
        // endpoint that will keep refusing, drop rows the server no longer
        // authorises, and let the surface say so.
        recordQueueAccessRevoked()
        return
      }
      // A read for a board the reviewer has already left, or one superseded by
      // an explicit load, cannot make the current queue degraded.
      if (isSupersededQueueRead()) return
      recordQueueRefreshFailure(e, 'list')
      logError('Review queue background refresh failed:', e)
    } finally {
      refreshInFlight = false
      if (refreshAbort === controller) refreshAbort = null
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

  function clearQueueRefreshRuntime() {
    if (typeof document !== 'undefined') {
      document.removeEventListener('visibilitychange', onRefreshVisibilityChange)
    }
    if (refreshInterval !== null) {
      clearInterval(refreshInterval)
      refreshInterval = null
    }
    if (refreshAbort) {
      // Cancel a read that is still open, so a late answer cannot write into a
      // suspended or departed surface.
      refreshAbort.abort()
      refreshAbort = null
    }
  }

  function armQueueRefresh() {
    if (!queueRefreshConfigured || refreshInterval !== null) return
    if (typeof document !== 'undefined') {
      document.addEventListener('visibilitychange', onRefreshVisibilityChange)
    }
    refreshInterval = setInterval(() => {
      void maybeRefresh()
    }, REVIEW_QUEUE_REFRESH_MS)
  }

  function suspendQueueRefreshForPermission() {
    clearQueueRefreshRuntime()
    queueRefreshSuspendedForPermission = queueRefreshConfigured
  }

  function resumeQueueRefreshAfterPermissionRecovery() {
    if (!queueRefreshConfigured || !queueRefreshSuspendedForPermission) return
    queueRefreshSuspendedForPermission = false
    armQueueRefresh()
  }

  /**
   * Starts the bounded, visibility-aware queue poll. `shouldRefresh` lets the
   * owning surface hold a tick while the reviewer is mid-decision (a confirm
   * dialog open, an action in flight) so the record under the cursor cannot
   * change underneath the decision being made.
   *
   * `hooks.onQueueReplaced` fires once per landed poll, right after the queue
   * has been replaced, so a surface can tell a poll-driven selection change
   * apart from one the reviewer made (#2215 A).
   */
  function startQueueRefresh(
    shouldRefresh?: () => boolean,
    hooks?: { onQueueReplaced?: () => void },
  ) {
    // Guard against double-start exactly as startClock does. Configuration can
    // outlive the interval while a 403 suspension is active, so key this guard
    // to configuration rather than the runtime handle.
    if (queueRefreshConfigured) return
    queueRefreshConfigured = true
    shouldRefreshNow = shouldRefresh ?? null
    onQueueReplacedByPoll = hooks?.onQueueReplaced ?? null
    if (queueAccessRevoked.value) {
      queueRefreshSuspendedForPermission = true
      return
    }
    armQueueRefresh()
  }

  function stopQueueRefresh() {
    clearQueueRefreshRuntime()
    queueRefreshConfigured = false
    queueRefreshSuspendedForPermission = false
    shouldRefreshNow = null
    onQueueReplacedByPoll = null
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
    () => {
      // A revoked queue has ONE owner and ONE explanation — the same rule the
      // explicit load already applies at its own `openProposalFromHash` call
      // site. A `#proposal-` link followed while the revoked panel is up (a
      // stale rail row, a bookmark, the back button) can only ask the by-id
      // route about a target inside a board the server has refused wholesale,
      // and its answer can only write a second, narrower and wrong account of
      // that refusal into `unavailableProposalId`.
      //
      // Invisible today because the revoked panel is the first branch of both
      // skins' empty chains, which makes it a LATENT contradiction rather than
      // a visible one: the state is written, and the next surface to consume it
      // reads a lifecycle claim ("applied, archived, or removed") about a
      // proposal that was none of those things. A successful load clears
      // `queueAccessRevoked` and re-runs the hash lookup itself, so nothing is
      // lost by not asking here.
      if (queueAccessRevoked.value) return
      openProposalFromHash().catch(() => {})
    },
  )

  watch(
    () => activeBoardFilter.value,
    () => { loadProposals().catch(() => {}) },
  )

  return {
    proposals,
    proposalsLoading,
    unavailableProposalId,
    unavailableProposalMalformed,
    queueAccessRevoked,
    queueRefreshStale,
    queueRefreshRefused,
    queueRefreshRecovered,
    queueRefreshRecoveredKind,
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
    awaitingProposalIds,
    queueAnnouncementKey,
    queueScopeLoaded,
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
    loadProposalsWithOutcome,
    refreshProposals,
    invalidateQueueReads,
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
