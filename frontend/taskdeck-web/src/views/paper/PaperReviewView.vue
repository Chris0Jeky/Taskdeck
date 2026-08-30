<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import ReviewQueueRail, {
  type QueueFilter,
  type QueueRailItem,
} from './review/ReviewQueueRail.vue'
import type { RecentlyAppliedRow } from './review/ReviewRecentApplied.vue'
import ReviewMain from './review/ReviewMain.vue'
import type { ApplyPhase, EditLock } from './review/ReviewDecisionRail.vue'
import ApplyToBoardDialog from '../../components/review/ApplyToBoardDialog.vue'
import BatchApproveDialog from '../../components/review/BatchApproveDialog.vue'
import BatchExecuteDialog from '../../components/review/BatchExecuteDialog.vue'
import RejectProposalDialog from '../../components/review/RejectProposalDialog.vue'
import ReviewRevisionEditor from './review/ReviewRevisionEditor.vue'
import ReviewRightRail from './review/ReviewRightRail.vue'
import { useReviewProposals, isProposalReadOnly } from '../../composables/useReviewProposals'
import { useReviewApplyRate, useReviewCadence } from '../../composables/useReviewCadence'
import { useReviewActions } from '../../composables/useReviewActions'
import { useBatchApproveProposals } from '../../composables/useBatchApproveProposals'
import { useBatchExecuteProposals } from '../../composables/useBatchExecuteProposals'
import { usePaperReviewSelectors } from '../../composables/usePaperReviewSelectors'
import { useReviewKeymap } from '../../composables/useReviewKeymap'
import { useProposalRevisions } from '../../composables/useProposalRevisions'
import { useWorkspaceCollaboration } from '../../composables/useWorkspaceCollaboration'
import { getErrorDisplay, getValidationReason, isAccessDeniedError, isValidationError } from '../../composables/useErrorMapper'
import { automationApi } from '../../api/automationApi'
import { useSessionStore } from '../../store/sessionStore'
import { useWorkspaceStore } from '../../store/workspaceStore'
import { useToastStore } from '../../store/toastStore'
import {
  normalizeProposalSourceType,
  normalizeProposalStatus,
  sortProposalsByRisk,
} from '../../utils/automation'
import {
  proposalIdsEqual,
  proposalRevisionIdentity,
  proposalRevisionMoved,
} from '../../utils/proposalIdentity'
import type { Proposal as ApiProposal, ProposalOperation } from '../../types/automation'
import { proposalDisplayNames } from '../../composables/useProposalDisplayNames'
import { useRoute } from 'vue-router'
import type {
  ChangeAfterCard,
  ChangeBeforeCard,
  FieldDiff,
} from './review/ReviewChangeSection.vue'

/**
 * PaperReviewView — the deep-Review surface (PAPER-06 / #1002).
 *
 * 3-column grid (280 | flex | 320):
 *   - left  : ReviewQueueRail (filter pills + queue + recent + cadence)
 *   - main  : ReviewMain (header, sticky decision rail, sections I–V)
 *   - right : ReviewRightRail (author, why-now, similar-past, keys)
 *
 * The orchestrator owns:
 *   - proposal loading via `useReviewProposals`
 *   - action handlers via `useReviewActions`
 *   - extended selectors (provenance, side-effects, etc.) via
 *     `usePaperReviewSelectors` (mock-data feature flag — see backend-gap
 *     follow-ups in #1002)
 *   - the route-scoped keyboard map via `useReviewKeymap`. The keymap
 *     guards against firing while focus is in a text input.
 *
 * Ink-bleed note: PAPER-10 (`paper/10-ink-bleed`) is parallel work and not
 * merged into this branch. The header renders a static dried/stamped state
 * for awaiting proposals; once PAPER-10 lands, swap in the BleedStage in
 * `ReviewMain` above the title.  TODO(#996): wire ink-bleed when ready.
 */

const {
  proposals,
  proposalsLoading,
  unavailableProposalId,
  queueAccessRevoked,
  nowMs,
  visibleProposals,
  dismissableProposalIds,
  activeBoardFilter,
  activeBoardName,
  isArchivedHistory,
  matchesActiveBoardFilter,
  isProposalExpired,
  isApplyActionable,
  isRejectActionable,
  isProposalDeferred,
  isProposalDismissable,
  isStaleProposal,
  clearProposalDeepLink,
  loadProposals,
  invalidateQueueReads,
  loadBoardOptions,
  availableBoards,
  startClock,
  stopClock,
  startQueueRefresh,
  stopQueueRefresh,
  clearBoardFilter,
  openProposal,
} = useReviewProposals()
const session = useSessionStore()
const workspace = useWorkspaceStore()
const toast = useToastStore()
const route = useRoute()
const collaboration = useWorkspaceCollaboration()
const { t, locale } = useI18n()
const displayVersion = ref(0)
const technicalDetailsCopied = ref(false)

/**
 * Date/time formatting goes through `Intl` against the ACTIVE locale, never a
 * per-locale pattern catalog (ADR-0054 §4). Region is preserved where it agrees
 * with the chosen language — same shape as `BoardsListView` — so an `en-GB`
 * reviewer on app-locale `en` keeps their own clock and date order instead of
 * being silently switched to US formatting by turning i18n on.
 */
const dateLocale = computed(() => {
  const active = locale.value
  const preferred =
    typeof navigator === 'undefined' ? [] : (navigator.languages ?? [navigator.language])
  const regional = preferred.find(
    (tag) => typeof tag === 'string' && tag.toLowerCase().split('-')[0] === active,
  )
  return regional ?? active
})

/**
 * Backend status wire values are never catalog keys; only their RENDERED labels
 * are. This maps one to the other, so `review.status.*` / `review.statusInline.*`
 * stay the single place the display wording lives.
 */
function statusKeySuffix(status: string): string {
  return status.charAt(0).toLowerCase() + status.slice(1)
}

watch(
  [proposals, availableBoards],
  ([currentProposals, boards]) => {
    if (currentProposals.length === 0 && boards.length === 0) return
    void proposalDisplayNames.ensure(currentProposals, boards).then(() => {
      displayVersion.value += 1
    })
  },
  { deep: true, immediate: true },
)

// The dismiss endpoint rejects the WHOLE request with 403 if any id isn't owned
// by the caller, and a board-filtered proposal list deliberately includes other
// users' proposals on shared boards. So the file-away affordance (per-proposal
// and bulk) must be scoped to the caller's own settled proposals. #1161 (review)
function isOwnProposal(proposal: ApiProposal): boolean {
  return !!session.userId && proposal.requestedByUserId === session.userId
}
const ownedDismissableIds = computed(() =>
  dismissableProposalIds.value.filter((id) => {
    const proposal = proposals.value.find((p) => proposalIdsEqual(p.id, id))
    return !!proposal && isOwnProposal(proposal)
  }),
)

const {
  proposalActionBusyId,
  bulkDismissBusy,
  executeConfirmProposal,
  rejectPromptProposal,
  rejectRequiresReason,
  handleApproveProposal,
  requestRejectProposal,
  cancelRejectProposal,
  confirmRejectProposal,
  handleDeferProposal,
  requestExecuteProposal,
  cancelExecuteProposal,
  confirmExecuteProposal,
  handleDismissProposal,
  handleDismissApplied,
} = useReviewActions(proposals, ownedDismissableIds, loadProposals, isProposalExpired)

const currentUserId = computed<string | null>(() => session.userId ?? null)
const {
  eligibleIds: batchEligibleIds,
  selectedCount: batchSelectedCount,
  confirmationOpen: batchConfirmationOpen,
  busy: batchApproveBusy,
  clearSelection: clearBatchSelection,
  isSelected: isBatchSelected,
  toggleSelection: toggleBatchSelection,
  requestConfirmation: requestBatchApproval,
  cancelConfirmation: cancelBatchApproval,
  confirmApproval: confirmBatchApproval,
} = useBatchApproveProposals(proposals, currentUserId, nowMs, loadProposals)

// #1307, q-14 C. Separate from batch approve on purpose: approve and execute stay two explicit
// steps (ADR-0003 / GP-06), so this acts only on proposals that are ALREADY Approved and never
// approves anything itself.
const batchExecuteReviewScope = computed(() => JSON.stringify({
  boardId: activeBoardFilter.value?.toLowerCase() ?? null,
  history: isArchivedHistory.value ? 'archived' : 'live',
}))
const {
  executableCount: batchExecutableCount,
  confirmationCount: batchExecuteConfirmationCount,
  confirmationOpen: batchExecuteOpen,
  busy: batchExecuteBusy,
  receipts: batchExecuteReceipts,
  requestConfirmation: requestBatchExecute,
  cancelConfirmation: cancelBatchExecute,
  forceClose: forceCloseBatchExecute,
  confirmExecute: confirmBatchExecute,
} = useBatchExecuteProposals(
  proposals,
  currentUserId,
  nowMs,
  loadProposals,
  batchExecuteReviewScope,
  (proposal) => proposal.summary || t('review.queueItem.noSummary'),
)

// --- Active proposal ---------------------------------------------------

const explicitActiveId = ref<string | null>(null)
const queueFilter = ref<QueueFilter>('all')
type DecisionReceipt = 'approved' | 'applied' | 'rejected' | 'deferred'
const decisionReceipt = ref<{ proposalId: string; kind: DecisionReceipt } | null>(null)

const hashProposalId = computed(() => {
  const hash = route.hash ?? ''
  if (!hash.startsWith('#proposal-')) return null
  const id = hash.slice('#proposal-'.length)
  try {
    return id ? decodeURIComponent(id) : null
  } catch {
    return null
  }
})

const filteredVisibleProposals = computed(() => {
  let filtered: ApiProposal[]
  switch (queueFilter.value) {
    case 'mine':
      filtered = visibleProposals.value.filter(
        (proposal) => !!session.userId && proposal.requestedByUserId === session.userId,
      )
      break
    case 'stale':
      filtered = visibleProposals.value.filter(isStaleProposal)
      break
    case 'all':
    default:
      filtered = visibleProposals.value
      break
  }
  return sortProposalsByRisk(filtered)
})

/**
 * Whether the rendered queue still holds a record authored by somebody other
 * than this reviewer — that is, something "Mine" can actually isolate.
 *
 * This is a PRESERVE-visibility guard, never the membership source, so it does
 * not breach the recorded #1940 assumption against deriving filter visibility
 * from proposal authors. The server contract stays the sole prerequisite for
 * ever WITHDRAWING the pair; authorship may only keep on screen a control that
 * membership alone would have removed, which is the fail-open direction.
 *
 * It is needed because revoking a collaborator's board access deletes the
 * access row but leaves their proposals on the board, so the workspace can
 * legitimately report solo while a departed author's records are still
 * rendered and "Mine" still means something.
 *
 * Deliberately reads the pre-filter `visibleProposals`: keying it on the
 * filtered queue would make selecting "Mine" remove the foreign rows, withdraw
 * the pair, fall back to "All", and oscillate.
 */
const queueHasForeignAuthoredProposal = computed(
  () =>
    !!session.userId &&
    visibleProposals.value.some(
      (proposal) =>
        !!proposal.requestedByUserId && proposal.requestedByUserId !== session.userId,
    ),
)

/**
 * Whether the queue rail may offer the author partition ("All" vs "Mine").
 *
 * The prerequisite signal is only the server-computed collaboration-membership
 * contract, per the recorded #1940 prerequisite: proposal authorship, board ACL
 * rows on their own, and online presence are all wrong proxies for workspace
 * membership. The pair is withdrawn only when membership is known AND
 * single-member AND nothing foreign-authored is on screen; loading, unknown,
 * and failed lookups leave every control exactly as it is today.
 */
const authorPartitionAvailable = computed(
  () => !collaboration.isSoloWorkspace.value || queueHasForeignAuthoredProposal.value,
)

const activeFilterLabel = computed(() => t(`review.queueRail.filter.${queueFilter.value}`))
const boardScopeLabel = computed(() =>
  activeBoardFilter.value
    ? t('review.scope.board', { board: activeBoardName.value })
    : '',
)

const hasFilterEmptyState = computed(
  () => visibleProposals.value.length > 0 && filteredVisibleProposals.value.length === 0,
)

function preferredActiveProposalId(proposals: readonly ApiProposal[]): string | null {
  return (
    proposals.find(
      (proposal) =>
        normalizeProposalStatus(proposal.status) === 'PendingReview' && !isProposalExpired(proposal),
    )?.id ?? proposals[0]?.id ?? null
  )
}

/**
 * Set when a background queue poll dropped the proposal the reviewer was on
 * (#2215 A).
 *
 * Another session settling or withdrawing the active proposal used to slide the
 * selection onto the next pending row: `ReviewMain` is keyed on the proposal id,
 * so it was re-created and focus dropped out of the decision controls, while the
 * window-level review keymap stayed enabled for the newly selected record. The
 * next ⏎ / ⌫ / D / E therefore decided a proposal the reviewer never chose.
 *
 * While this holds an id the surface renders an explicit notice instead, with
 * `activeProposal` null — which is also what disables the keymap.
 */
const activeProposalSettledElsewhere = ref<string | null>(null)

const activeProposal = computed<ApiProposal | null>(() => {
  // A valid deep link names one exact proposal; it is never a hint to fall back
  // to the first actionable row. Match case-insensitively, but return the
  // canonical API object so actions and rendered ids retain its original case.
  if (hashProposalId.value) {
    return (
      proposals.value.find(
        (proposal) =>
          proposalIdsEqual(proposal.id, hashProposalId.value) &&
          matchesActiveBoardFilter(proposal.boardId),
      ) ?? null
    )
  }
  // A poll removed the row under the reviewer. Hold the surface on the notice
  // rather than promoting whatever sorted into its place (#2215 A). Ranked
  // below the deep link because a hash names one exact proposal explicitly, and
  // a poll cannot evict a hash target anyway — `refreshProposals` pins it.
  if (activeProposalSettledElsewhere.value) return null
  // Queue filters intentionally remove decided rows. Keep the exact item at
  // the decision locus instead of falling through to an unrelated proposal.
  if (decisionReceipt.value) {
    const receipted = proposals.value.find((proposal) =>
      proposalIdsEqual(proposal.id, decisionReceipt.value?.proposalId) &&
      matchesActiveBoardFilter(proposal.boardId),
    )
    if (receipted) return receipted
  }
  if (explicitActiveId.value) {
    const found = filteredVisibleProposals.value.find((p) =>
      proposalIdsEqual(p.id, explicitActiveId.value),
    )
    if (found) return found
  }
  // Default to the first pending-review item in the queue.
  const preferredId = preferredActiveProposalId(filteredVisibleProposals.value)
  return (
    filteredVisibleProposals.value.find((proposal) =>
      proposalIdsEqual(proposal.id, preferredId),
    ) ?? null
  )
})

const activeDecisionReceipt = computed<DecisionReceipt | null>(() => {
  const receipt = decisionReceipt.value
  if (
    !receipt ||
    !proposalIdsEqual(activeProposal.value?.id, receipt.proposalId) ||
    !matchesActiveBoardFilter(activeProposal.value?.boardId)
  ) return null
  return receipt.kind
})

const activeAppliedProposal = computed<ApiProposal | null>(() => {
  const proposal = activeProposal.value
  return proposal && normalizeProposalStatus(proposal.status) === 'Applied' ? proposal : null
})

function recordDecisionReceipt(proposalId: string, kind: DecisionReceipt) {
  decisionReceipt.value = { proposalId, kind }
  explicitActiveId.value = proposalId
}

/**
 * Armed for exactly one selection change by a landed background poll (#2215 A).
 *
 * A poll is the only thing that moves the queue without the reviewer asking, so
 * an active-proposal change observed while this is armed is by definition not
 * one they made. It is disarmed by the first firing of the watcher below, and —
 * for the case where the selection did NOT change and that watcher never runs —
 * on the tick after the flush the queue assignment scheduled.
 */
let queueReplacedByPoll = false

function onQueueReplacedByPoll() {
  queueReplacedByPoll = true
  void nextTick(() => {
    queueReplacedByPoll = false
  })
}

watch(
  () => activeProposal.value?.id,
  (id, previousId) => {
    const replacedByPoll = queueReplacedByPoll
    // Disarm before anything else: setting the notice below makes
    // `activeProposal` null, which re-triggers this same watcher, and a second
    // firing must not overwrite the recorded id with the fallback row's.
    queueReplacedByPoll = false
    if (replacedByPoll && previousId && !proposalIdsEqual(id, previousId)) {
      activeProposalSettledElsewhere.value = previousId
      return
    }
    if (id && !explicitActiveId.value) {
      // sync explicit id so subsequent action results stay anchored
      explicitActiveId.value = id
    }
  },
)

const settledElsewhereReturnRef = ref<HTMLButtonElement | null>(null)

/**
 * The notice replaces the whole decision column, so focus would otherwise land
 * on `<body>` with nothing announced. Move it to the notice's own control: that
 * is both the safe target the reviewer's next keystroke acts on and what makes
 * assistive tech read the notice out.
 */
watch(activeProposalSettledElsewhere, (id) => {
  if (!id) return
  void nextTick(() => {
    settledElsewhereReturnRef.value?.focus?.()
  })
})

/**
 * Leave the notice deliberately: drop the pin AND the stale explicit selection
 * so the ordinary "first pending row" default resumes.
 *
 * It also re-reads the queue explicitly, because the notice states only that
 * the row LEFT the queue — a single poll answer does not prove it was decided.
 * The list endpoint omits a PendingReview proposal whose `deferredUntil` is in
 * the future and is capped at 200 rows, so an authoritative read (which
 * `loadProposals` is, and which also re-runs `openProposalFromHash`) is the
 * honest way to find out what is actually there now (#2215 review round 1).
 */
function dismissSettledElsewhereNotice() {
  activeProposalSettledElsewhere.value = null
  explicitActiveId.value = null
  decisionReceipt.value = null
  void loadProposals()
}

watch(
  hashProposalId,
  (id) => {
    if (!id) return
    const target = proposals.value.find(
      (proposal) =>
        proposalIdsEqual(proposal.id, id) && matchesActiveBoardFilter(proposal.boardId),
    )
    if (target) {
      explicitActiveId.value = target.id
      // A deep link names one exact proposal; it outranks the #2215 A notice.
      activeProposalSettledElsewhere.value = null
    }
  },
  { immediate: true },
)

const selectors = usePaperReviewSelectors(activeProposal)

// --- Inline diff preview ----------------------------------------------
//
// Mirrors useReviewActions.handleToggleDiff: a per-proposal toggle that
// (a) hides the diff if it is already shown for the active proposal,
// (b) ignores stale async responses via a monotonic request counter, and
// (c) clears itself when the active proposal changes. The diff renders
// inline in the deep-review surface below the change section — no drawer.
const previewDiff = ref<string | null>(null)
const previewDiffProposalId = ref<string | null>(null)
const previewDiffLoading = ref(false)
const previewDiffSection = ref<HTMLElement | null>(null)
// How the diff pane presents (#1397): `live` fetched diff, `stored` read-only
// diffPreview for a terminal/expired proposal, or `invalid` when the backend's
// Apply-time gates reject the proposal. Null while nothing is shown.
const previewDiffMode = ref<'live' | 'stored' | 'invalid' | null>(null)
// The ACTUAL rejection reason for `invalid` mode — the backend's message for a
// /diff 400 ("Proposal has expired" vs "Proposal must contain at least one
// operation"), or the local zero-op wording for the pre-fetch guard. The pane
// must never hardcode one reason for every 400 (#1397 MEDIUM-1).
const previewDiffInvalidReason = ref<string | null>(null)
let latestDiffRequestId = 0

function clearPreviewDiff() {
  previewDiffProposalId.value = null
  previewDiff.value = null
  previewDiffMode.value = null
  previewDiffInvalidReason.value = null
  previewDiffLoading.value = false
}

// The read-only banner label for the active proposal's stored preview: 'Expired'
// when the clock/domain says so, otherwise its terminal status.
const previewReadOnlyLabel = computed(() => {
  const p = activeProposal.value
  if (!p) return ''
  if (isProposalExpired(p)) return t('review.status.expired')
  return t(`review.status.${statusKeySuffix(normalizeProposalStatus(p.status))}`)
})

// Read-only fallback when the proposal never captured a `diffPreview` (normal
// creation flows leave it null — Codex review on #1414): derive a minimal
// operation listing from the proposal's own recorded operations so a
// terminal/expired proposal that still HAS operations is inspectable instead of
// a dead "no stored preview" end. Local rendering only — the live `/diff` 400s
// for these proposals (#1397).
const storedOperationsFallback = computed(() => {
  void displayVersion.value
  if (previewDiffMode.value !== 'stored' || previewDiff.value) return null
  const ops = activeProposal.value?.operations ?? []
  if (ops.length === 0) return null
  return [...ops]
    .sort((a, b) => a.sequence - b.sequence)
    .map(
      (op, index) =>
        `${index + 1}. ${formatActionLabel(op.actionType)} ${op.targetType}${proposalDisplayNames.operationTargetLabel(activeProposal.value!, op) ? ` “${proposalDisplayNames.operationTargetLabel(activeProposal.value!, op)}”` : ''}`,
    )
    .join('\n')
})
// Guards against a double-click firing two feedback POSTs (the backend is idempotent as a backstop).
const reportingProposalId = ref<string | null>(null)

// Space can be pressed while the reviewer is looking at the top of a long
// review surface; the diff renders below the (often tall) deep-review content,
// so scroll it into view once it appears. Guarded for jsdom where the method
// is absent.
function scrollDiffIntoView() {
  void nextTick(() => {
    previewDiffSection.value?.scrollIntoView?.({ behavior: 'smooth', block: 'nearest' })
  })
}

// Clear the preview whenever the active proposal changes so a diff loaded
// for one proposal never leaks onto the next (mirrors legacy behaviour).
// #2215 B: the pane is keyed on the proposal AND its latest revision. Keying it
// on the id alone left a diff on screen that had been computed for the PREVIOUS
// revision once a poll brought in a revision another reviewer had saved — while
// Approve pins, and Apply executes, whatever the server holds latest. Dropping
// the pane is the honest answer: re-opening it fetches the revision-aware diff.
watch(
  () => [activeProposal.value?.id ?? null, proposalRevisionIdentity(activeProposal.value)] as const,
  (current, previous) => {
    if (!previewDiffProposalId.value) return
    const [id, revisionId] = current
    const [previousId, previousRevisionId] = previous ?? [null, null]
    const proposalChanged = !proposalIdsEqual(previewDiffProposalId.value, id)
    // Round 2: the key is the EFFECTIVE revision, and only a genuine move
    // counts. `latestRevisionId` is nulled on the wire the moment a proposal
    // leaves PendingReview, so approving a revised proposal used to read as a
    // collaborator revision change and wiped the reviewer's own open diff —
    // and, running before the read-only conversion watcher, it wiped the pane
    // rather than letting it convert to the decision-time presentation.
    const revisionChanged =
      proposalIdsEqual(previousId, id) && proposalRevisionMoved(previousRevisionId, revisionId)
    if (!proposalChanged && !revisionChanged) return
    latestDiffRequestId += 1
    clearPreviewDiff()
  },
)

// #1397 LOW-5: the pane's presentation is chosen when it opens, but the active
// proposal can turn read-only WHILE the pane is open — a status change (e.g.
// apply lands, a refresh maps a terminal state in) or the 60s expiry clock
// ticking past expiresAt. Re-derive: an open live/invalid pane flips to the
// stored read-only presentation the moment the classification flips.
watch(
  () => {
    const p = activeProposal.value
    if (!p || !proposalIdsEqual(previewDiffProposalId.value, p.id)) return false
    return isProposalReadOnly(p, isProposalExpired(p))
  },
  (readOnly) => {
    if (!readOnly) return
    // SEAM INVARIANT (#1397 round 3, aligned with the Legacy watcher): a
    // read-only conversion invalidates EVERY non-stored pane state. Paper sets
    // previewDiffMode 'live' synchronously before its fetch, so unlike Legacy a
    // null mode cannot coexist with an open pane id today — but the guard must
    // not depend on that: only an already-stored presentation is skippable.
    if (previewDiffMode.value === 'stored') return
    const p = activeProposal.value
    if (!p || !proposalIdsEqual(previewDiffProposalId.value, p.id)) return
    // Cancel any in-flight live fetch so a late response can't overwrite the
    // read-only presentation.
    const requestId = ++latestDiffRequestId
    previewDiff.value = p.diffPreview
    previewDiffMode.value = 'stored'
    previewDiffInvalidReason.value = null
    previewDiffLoading.value = false
    // #1414 P2: this conversion newly presents the stored preview, so re-check
    // access (parity with the Legacy watcher, which routes through
    // presentStoredPreview). The prior live pane was access-checked at open, but
    // access can be revoked in the window before it expires into read-only.
    void verifyStoredPreviewAccess(p.id, requestId)
  },
)

const {
  editing: revisionEditing,
  saving: revisionSaving,
  revisionCount,
  revisionsLoaded,
  latestRevision,
  startEditing: startRevisionEditing,
  cancelEditing: cancelRevisionEditing,
  saveRevision,
  loadRevisionState,
} = useProposalRevisions(activeProposal, { onRevisionSaved: invalidateQueueReads })

watch(isArchivedHistory, (readOnly) => {
  if (!readOnly) return
  clearBatchSelection()
  cancelExecuteProposal()
  cancelRejectProposal()
  cancelRevisionEditing()
  // Archived history is read-only: every mutation affordance is withdrawn, and a batch-apply
  // confirmation left open across the switch would offer a board write this mode forbids. Forced
  // rather than the ordinary cancel because the switch is a context change, not a reviewer
  // decision, and it must win even mid-request.
  forceCloseBatchExecute()
})

watch(activeBoardFilter, () => {
  clearBatchSelection()
  // A new board scope is a new queue; the notice describes the old one.
  activeProposalSettledElsewhere.value = null
})

const revisionBadge = computed(() =>
  t('review.revisionEditor.badge', { count: revisionCount.value }, revisionCount.value),
)

const editablePayload = computed(() => {
  const p = activeProposal.value
  if (!p) return '{}'
  if (latestRevision.value) return latestRevision.value.revisedPayload
  const ops = p.operations ?? []
  if (ops.length === 0) return '{}'
  return JSON.stringify({
    operations: [...ops]
      .sort((a, b) => a.sequence - b.sequence)
      .map((operation) => ({
        sequence: operation.sequence,
        actionType: operation.actionType,
        targetType: operation.targetType,
        targetId: operation.targetId,
        parameters: operation.parameters,
        idempotencyKey: operation.idempotencyKey,
        expectedVersion: operation.expectedVersion,
      })),
  })
})

// --- Queue rail data ---------------------------------------------------

const awaitingCount = computed(() => {
  return visibleProposals.value.filter(
    (p) =>
      normalizeProposalStatus(p.status) === 'PendingReview' && !isProposalExpired(p),
  ).length
})

const staleCount = computed(() =>
  // Route through the SHARED isStaleProposal (PendingReview + inclusive >=24h)
  // so the badge count and the 'stale' queue filter always agree — no third
  // inline copy that could drift at the 24h boundary (#1124 / ADR-0038).
  // visibleProposals is already board-scoped via matchesActiveBoardFilter.
  visibleProposals.value.filter(isStaleProposal).length,
)

function ageLabel(iso: string): string {
  const ms = nowMs.value - new Date(iso).getTime()
  if (ms < 60_000)
    return t('review.age.seconds', { value: Math.max(1, Math.floor(ms / 1000)) })
  if (ms < 60 * 60_000) return t('review.age.minutes', { value: Math.floor(ms / 60_000) })
  if (ms < 24 * 60 * 60_000) return t('review.age.hours', { value: Math.floor(ms / (60 * 60_000)) })
  return t('review.age.days', { value: Math.floor(ms / (24 * 60 * 60_000)) })
}

function summariseReach(proposal: ApiProposal): string {
  const ops = proposal.operations?.length ?? 0
  // An em dash is a glyph, not copy — it reads the same in every locale.
  if (ops === 0) return '—'
  return t('review.queueItem.reach', { count: ops }, ops)
}

const queueItems = computed<QueueRailItem[]>(() =>
  filteredVisibleProposals.value.map((p) => {
    const stale = isStaleProposal(p)
    return {
      id: p.id,
      serial: `#${p.id.slice(0, 4).toUpperCase()}`,
      title: p.summary || t('review.queueItem.noSummary'),
      who:
        normalizeProposalSourceType(p.sourceType) === 'Chat'
          ? t('review.queueItem.who.assistant')
          : t('review.queueItem.who.capture'),
      // Per-item rail confidence is not yet wired per-proposal — leave null until
      // the gap lands. Not contradictory with `authorMeta` below, which shows the
      // REAL aggregate /confidence breakdown for the single active proposal.
      confidence: null,
      age: ageLabel(p.createdAt),
      reach: summariseReach(p),
      mine: !!session.userId && p.requestedByUserId === session.userId,
      stale,
      batchEligible: !isArchivedHistory.value && batchEligibleIds.value.has(p.id),
      batchSelected: isBatchSelected(p.id),
    }
  }),
)

const recentlyApplied = computed<RecentlyAppliedRow[]>(() => {
  return proposals.value
    .filter((p) => matchesActiveBoardFilter(p.boardId))
    .filter((p) => normalizeProposalStatus(p.status) === 'Applied' && p.appliedAt)
    .sort((a, b) => new Date(b.appliedAt as string).getTime() - new Date(a.appliedAt as string).getTime())
    .map((p) => ({
      id: p.id,
      serial: `#${p.id.slice(0, 4).toUpperCase()}`,
      title: p.summary || t('review.recent.noSummary'),
      // Pass the backend ISO string straight through (same pattern as queueItems):
      // ageLabel degrades gracefully on an unparseable value, whereas the previous
      // Date→ms→Date→toISOString roundtrip threw RangeError on an invalid date.
      age: ageLabel(p.appliedAt as string),
    }))
    .slice(0, 4)
})

// File-away deliberately removes settled rows from `proposals` immediately so
// the queue and Recently applied rail respond without waiting for another read.
// Keep only the decided rows this mounted surface already knew before that
// removal: weekly metrics are history, not queue membership, and must not forget
// a decision merely because its row was filed. Current server rows win by id so
// a later refresh can still update the lifecycle fields we project.
const filedAwayDecisionHistory = ref<ApiProposal[]>([])

// A trusted 403 means the current board payload is no longer authorised. The
// computed guard suppresses retained metrics in the same reactive turn, while
// the synchronous clear prevents a later successful load from republishing the
// old board's history after `queueAccessRevoked` returns to false.
watch(
  queueAccessRevoked,
  (revoked) => {
    if (revoked) filedAwayDecisionHistory.value = []
  },
  { flush: 'sync' },
)

function upsertProposalById(target: ApiProposal[], proposal: ApiProposal) {
  const index = target.findIndex((item) => proposalIdsEqual(item.id, proposal.id))
  if (index >= 0) target[index] = proposal
  else target.push(proposal)
}

function retainKnownDecisions(candidates: readonly ApiProposal[]) {
  const retained = [...filedAwayDecisionHistory.value]
  for (const proposal of candidates) {
    if (!proposal.decidedAt || !proposal.decidedByUserId) continue
    upsertProposalById(retained, proposal)
  }
  filedAwayDecisionHistory.value = retained
}

const weeklyDecisionSource = computed<ApiProposal[]>(() => {
  if (queueAccessRevoked.value) return []
  const merged = [...filedAwayDecisionHistory.value]
  for (const proposal of proposals.value) upsertProposalById(merged, proposal)
  return merged
})

/**
 * Real 7-day cadence for the rail: how many proposals the CURRENT user decided
 * on each of the last seven calendar days, projected from the review-queue
 * payload already loaded (`decidedAt` / `decidedByUserId` on `ProposalDto`).
 * Board-scoped through the same `matchesActiveBoardFilter` the queue uses, so
 * the bars never describe a board the rail is not showing.
 *
 * `undefined` when there is nothing honest to draw — the mini-cadence then
 * hides itself rather than inventing a week (#1782 / #1796 contract).
 */
const cadence = useReviewCadence(
  weeklyDecisionSource,
  nowMs,
  () => session.userId,
  matchesActiveBoardFilter,
)
const applyRate = useReviewApplyRate(
  weeklyDecisionSource,
  nowMs,
  () => session.userId,
  matchesActiveBoardFilter,
)

// --- Main column data --------------------------------------------------

const titleParts = computed(() => {
  const p = activeProposal.value
  if (!p) return [{ text: '' }]
  // Render summary as a single emphasised serif italic span. Until the
  // backend annotates highlight ranges, we wrap any quoted phrase in <em>.
  return splitQuotedSummary(p.summary ?? '')
})

function splitQuotedSummary(summary: string): Array<{ text: string; emphasis?: boolean }> {
  if (!summary) return [{ text: '' }]
  const parts: Array<{ text: string; emphasis?: boolean }> = []
  let cursor = 0

  while (cursor < summary.length) {
    const straight = summary.indexOf('"', cursor)
    const curly = summary.indexOf('“', cursor)
    const startCandidates = [straight, curly].filter((index) => index >= 0)
    if (startCandidates.length === 0) break
    const start = Math.min(...startCandidates)
    const endQuote = summary[start] === '“' ? '”' : '"'
    const end = summary.indexOf(endQuote, start + 1)
    if (end < 0) break

    if (start > cursor) {
      parts.push({ text: summary.slice(cursor, start) })
    }
    parts.push({ text: `“${summary.slice(start + 1, end)}”`, emphasis: true })
    cursor = end + 1
  }

  if (cursor < summary.length) {
    parts.push({ text: summary.slice(cursor) })
  }
  return parts.length > 0 ? parts : [{ text: summary, emphasis: true }]
}

const lede = computed(
  () => activeProposal.value?.presentation?.plainSummary ?? t('review.main.ledeFallback'),
)

const decisionSummary = computed(() => {
  const p = activeProposal.value
  if (!p) return t('review.decisionRail.summary.none')
  const ops = p.operations?.length ?? 0
  return t('review.decisionRail.summary.operations', { count: ops }, ops)
})

const headerSerial = computed(() => {
  const p = activeProposal.value
  if (!p) return ''
  return `#${p.id.slice(0, 14)}`
})

const headerMeta = computed(() => {
  const p = activeProposal.value
  if (!p) return ''
  const status = normalizeProposalStatus(p.status)
  const created = new Date(p.createdAt)
  const time = created.toLocaleTimeString(dateLocale.value, { hour: '2-digit', minute: '2-digit' })
  return t('review.headerMeta', {
    time,
    status: t(`review.statusInline.${statusKeySuffix(status)}`),
  })
})

function formatActionLabel(actionType: string): string {
  return actionType
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/[_-]+/g, ' ')
    .trim()
}

function summarizeOperation(operation: ProposalOperation): string {
  const proposal = activeProposal.value
  if (!proposal) return t('review.change.after.noParameterPreview')
  void displayVersion.value
  return proposalDisplayNames.summarizeOperation(proposal, operation)
}

const before = computed<ChangeBeforeCard>(() => {
  void displayVersion.value
  const operationCount = activeProposal.value?.operations?.length ?? 0

  // `presentation` is status-UNAWARE: `AutomationProposalService.MapToDto` materializes it on
  // every response, and `BuildImpactSummary` composes it from the operation set alone, so it
  // reads prospectively ("... ready for approval", "N changes planned") even for a proposal
  // that was applied days ago. Historical copy therefore has to WIN for a settled record
  // rather than sit behind a `??` that a populated `impactSummary` can never fall through to
  // (#2117 — the residual left by #2101, whose applied copy was unreachable on API-shaped
  // payloads). Pending records are unchanged and still prefer the backend's impact summary.
  const body = activeAppliedProposal.value
    ? t('review.change.before.bodyApplied', { count: operationCount }, operationCount)
    : (activeProposal.value?.presentation?.impactSummary ??
      t('review.change.before.bodyFallback', { count: operationCount }))

  return {
    serial: activeProposal.value ? `#${activeProposal.value.id.slice(0, 8)}` : '—',
    title: activeProposal.value?.summary ?? t('review.change.before.titleFallback'),
    body,
    // `source` is the backend's own sourceType wire value when present, so it is
    // interpolated rather than translated; only the fallback word is copy.
    meta: t('review.change.before.meta', {
      board: proposalDisplayNames.boardLabel(activeProposal.value?.boardId),
      source: activeProposal.value?.sourceType ?? t('review.change.before.sourceFallback'),
    }),
  }
})

const after = computed<ChangeAfterCard[]>(() => {
  void displayVersion.value
  const p = activeProposal.value
  const operations = p?.operations ?? []
  if (operations.length === 0) {
    return [{
      serial: p ? `#${p.id.slice(0, 8)}.0` : '—',
      title: t('review.change.after.noPreviewTitle'),
      body: p?.diffPreview
        ? proposalDisplayNames.displayDiff(p, p.diffPreview)
        : t('review.change.after.noPreviewBody'),
      status: 'kept',
    }]
  }

  return [...operations]
    .sort((a, b) => a.sequence - b.sequence)
    .map((operation, index) => ({
      serial: `op-${index + 1}`,
      title: `${formatActionLabel(operation.actionType)} · ${operation.targetType}`,
      body: summarizeOperation(operation),
      status: operation.actionType.toLowerCase().startsWith('create') ? 'new' : 'kept',
    }))
})

const fields = computed<FieldDiff[]>(() => {
  void displayVersion.value
  const p = activeProposal.value
  const operations = p?.operations ?? []
  if (operations.length === 0) {
    return [{
      key: t('review.change.fields.operationsKey'),
      before: t('review.change.fields.none'),
      after: p?.diffPreview
        ? proposalDisplayNames.displayDiff(p, p.diffPreview)
        : t('review.change.fields.notProvided'),
      same: !p?.diffPreview,
    }]
  }

  return [...operations]
    .sort((a, b) => a.sequence - b.sequence)
    .map((operation) => ({
      key: formatActionLabel(operation.actionType),
      before: proposalDisplayNames.operationTargetLabel(p!, operation) ?? operation.targetType,
      after: summarizeOperation(operation),
    }))
})

const changeSubTitle = computed(() => {
  void displayVersion.value
  const ops = activeProposal.value?.operations?.length ?? 0
  return t(
    'review.change.subTitle',
    { count: ops, board: proposalDisplayNames.boardLabel(activeProposal.value?.boardId) },
    ops,
  )
})

const technicalDetails = computed(() => {
  void displayVersion.value
  return activeProposal.value ? proposalDisplayNames.technicalDetails(activeProposal.value) : ''
})

async function copyTechnicalDetails() {
  if (!technicalDetails.value || !navigator.clipboard?.writeText) return
  try {
    await navigator.clipboard.writeText(technicalDetails.value)
    technicalDetailsCopied.value = true
  } catch {
    technicalDetailsCopied.value = false
  }
}

const displayedPreviewDiff = computed(() => {
  void displayVersion.value
  const proposal = activeProposal.value
  return proposal && previewDiff.value
    ? proposalDisplayNames.displayDiff(proposal, previewDiff.value)
    : previewDiff.value
})

// --- Right rail data ---------------------------------------------------

const proposedDate = computed(() => {
  const p = activeProposal.value
  if (!p) return ''
  const d = new Date(p.createdAt)
  return d.toLocaleString(dateLocale.value, { month: 'short', day: '2-digit' })
})

const proposedTime = computed(() => {
  const p = activeProposal.value
  if (!p) return ''
  return new Date(p.createdAt).toLocaleTimeString(dateLocale.value, {
    hour: '2-digit',
    minute: '2-digit',
  })
})

const proposedNum = computed(() => {
  const p = activeProposal.value
  if (!p) return ''
  return p.id.slice(0, 4).toUpperCase()
})

const authorMeta = computed(() => {
  const c = selectors.confidenceBreakdown.value
  if (c.overall === null || !Number.isFinite(c.overall)) return ''
  if (c.source === 'model-reported') {
    return t('review.author.modelConfidence', { value: c.overall.toFixed(2) })
  }
  if (c.source === 'derived') {
    return t('review.author.derivedConfidence', { value: c.overall.toFixed(2) })
  }
  return ''
})

const authorName = computed(() => {
  const normalized = activeProposal.value
    ? normalizeProposalSourceType(activeProposal.value.sourceType)
    : null
  if (!normalized) return t('review.author.nameFallback')
  // Same actor split as the queue rail above: only chat-driven proposals come from
  // the configured AI provider; capture triage may be the deterministic extractor
  // (see ReviewProvenance), so it must not be attributed to "Assistant".
  // `normalized` is a backend wire value — compared, never translated, and
  // interpolated verbatim into the sentence.
  const actor =
    normalized === 'Chat' ? t('review.author.actor.assistant') : t('review.author.actor.capture')
  return t('review.author.name', { actor, source: normalized.toLowerCase() })
})

const whyNowBody = computed(() => {
  const p = activeProposal.value
  if (!p) return t('review.whyNow.noProposal')
  return p.presentation?.sourceCue ?? t('review.whyNow.fallback')
})

// --- Action wiring -----------------------------------------------------

const revisionBusy = computed(() => revisionEditing.value || revisionSaving.value)
// #1414 round 4 P2-A: the zero-op apply guard's revision-load await participates
// in the SHARED decision lock. While it is in flight the whole rail (buttons via
// :busy, keymap via its !busy gate) is disabled — otherwise a Defer/Reject
// during the await, with the #proposal- hash carve-out keeping selection
// stable, could change the proposal's state under the resumed Apply.
const applyGuardBusy = ref(false)
const busy = computed(
  () =>
    proposalActionBusyId.value !== null ||
    revisionBusy.value ||
    bulkDismissBusy.value ||
    batchApproveBusy.value ||
    batchConfirmationOpen.value ||
    batchExecuteBusy.value ||
    batchExecuteOpen.value ||
    applyGuardBusy.value,
)

/**
 * GH-1964 — which half of the revision lock the rail should explain.
 *
 * `revisionEditing` stays TRUE across a save (`useProposalRevisions.saveRevision`
 * only clears it once the POST resolves), so the saving state must be tested
 * first or a save would render as a cancellable edit.
 *
 * Only the revision lock gets an explanation. The other `busy` sources are
 * sub-second network round trips whose disabled treatment is self-explanatory;
 * this one is held indefinitely by an off-screen composer, which is what made
 * the rail read as broken.
 */
const editLock = computed<EditLock>(() => {
  if (revisionSaving.value) return 'saving'
  return revisionEditing.value ? 'editing' : 'off'
})

// True once the active proposal is settled (Applied/Rejected/Failed/Expired/
// Approved-then-expired). Reads the SHARED rule so Paper and Legacy never
// drift (#1124 / ADR-0038). Reactive to the 60s expiry clock via
// isProposalDismissable → isProposalExpired, so the rail swaps to "File away"
// the moment a focused proposal expires (#1161). #1161
const activeDismissable = computed(
  () =>
    !!activeProposal.value &&
    isProposalDismissable(activeProposal.value) &&
    isOwnProposal(activeProposal.value),
)

// Count of the caller's own settled proposals on the active board — including
// ones the queue currently hides (Applied/Rejected/Failed when showCompleted is
// off, or items outside the active 'mine'/'stale' filter). Bulk file-away is
// board-scoped housekeeping, not queue-scoped, so the count can exceed what's
// visible — and ≥1 reveals it so a single hidden settled proposal (which has no
// per-proposal rail in Paper) is never left unclearable. #1161
const bulkDismissableCount = computed(() => ownedDismissableIds.value.length)

// #1818: which half of the two-phase apply the ⏎ / primary button will run.
// `execute` is the state the walkthrough found illegible — approved, but the
// board is untouched until the second explicit call. A settled proposal (the
// filing rail) has no apply phase at all, so it reports `approve`.
const applyPhase = computed<ApplyPhase>(() => {
  const p = activeProposal.value
  if (!p || activeDismissable.value) return 'approve'
  return normalizeProposalStatus(p.status) === 'Approved' ? 'execute' : 'approve'
})

// #1830 round 2: the confirmation dialog must not claim "0 operations will be
// applied" for the revision-aware apply path onApply deliberately allows (zero
// original operations + a saved revision, #1235). `revisionCount` tracks the
// ACTIVE proposal only, so it is only passed while the proposal awaiting
// confirmation is still the active one — otherwise the dialog is told nothing
// (null) and falls back to copy that claims no count.
const applyConfirmRevisionCount = computed<number | null>(() => {
  const pending = executeConfirmProposal.value
  if (!pending) return null
  if (!proposalIdsEqual(activeProposal.value?.id, pending.id)) return null
  if (!revisionsLoaded.value) return null
  return revisionCount.value
})

// --- Apply-flow focus restoration (GH-1942) ----------------------------
//
// TdDialog restores focus to whatever `document.activeElement` was when it
// opened. That worked while the dialog opened synchronously inside the click:
// the rail's primary button still had focus. Now the button is `disabled` for
// the whole approve round trip, so the browser has already moved focus to
// <body> by the time the dialog mounts — TdDialog captures <body>, its restore
// is a no-op, and a keyboard user who backs out lands at the top of the
// document instead of on the control they just used.
//
// So the view captures the trigger itself, BEFORE the approve call, and puts
// focus back when the dialog closes. A dismissal restores immediately. An
// accepted apply cannot restore at close time — the rail is disabled for the
// whole execute round trip and focus() on a disabled control is a no-op — so
// that path defers the restore until the busy lock clears and re-queries the
// rail. That covers a failed execute (the rail re-enables for the retry); a
// SUCCESSFUL apply removes the proposal from the queue and unmounts the rail,
// leaving nothing to return to — that gap is the decision-feedback work
// tracked on GH-1940, not something a focus restore can paper over. TdDialog
// is a shared primitive with no return-focus target prop and is deliberately
// not touched here.
const mainColRef = ref<HTMLElement | null>(null)
let applyReturnFocusEl: HTMLElement | null = null

// The rail's primary control, whichever it currently is: the decision button,
// or the filing button the rail becomes once the proposal is applied and
// settled. Scoped to the main column so it cannot reach another surface.
function decisionRailFocusTarget(): HTMLElement | null {
  const root = mainColRef.value
  if (!root) return null
  return (
    root.querySelector<HTMLElement>('[data-testid="decision-apply"]') ??
    root.querySelector<HTMLElement>('[data-testid="decision-file-away"]')
  )
}

watch(executeConfirmProposal, (pending, previous) => {
  // Only on close (open → closed), never on the open itself.
  if (pending !== null || !previous) return
  const captured = applyReturnFocusEl
  applyReturnFocusEl = null
  // After the flush: the rail may have just re-rendered (execute lands → the
  // decision rail becomes the filing rail), and TdDialog's own <body> restore
  // runs in that same flush and would otherwise overwrite this.
  void nextTick(() => {
    restoreApplyFocus(captured)
  })
})

function isFocusable(el: HTMLElement): boolean {
  return !('disabled' in el && (el as HTMLButtonElement).disabled)
}

function restoreApplyFocus(captured: HTMLElement | null) {
  const target = captured?.isConnected ? captured : decisionRailFocusTarget()
  if (!target) return
  if (isFocusable(target)) {
    target.focus?.()
    return
  }
  // Completed-apply exit: execute is still in flight, the rail is disabled,
  // and focus() would silently no-op. Retry once when the busy lock clears,
  // re-querying because the rail re-renders into the filing rail by then.
  const lateRestore = () => {
    void nextTick(() => {
      const late =
        captured?.isConnected && isFocusable(captured)
          ? captured
          : decisionRailFocusTarget()
      if (late && isFocusable(late)) late.focus?.()
    })
  }
  if (!busy.value) {
    lateRestore()
    return
  }
  const stop = watch(busy, (isBusy) => {
    if (isBusy) return
    stop()
    lateRestore()
  })
}

async function onFileAway() {
  if (isArchivedHistory.value) return
  const p = activeProposal.value
  if (!p) return
  if (revisionBusy.value) {
    toast.info(t('review.toast.revisionBusyFileAway'))
    return
  }
  // Another dismiss/approve/reject/bulk action is already in flight.
  if (busy.value) return
  if (!activeDismissable.value) {
    toast.info(t('review.toast.notDismissableYet'))
    return
  }
  retainKnownDecisions([p])
  await handleDismissProposal(p.id)
  if (!proposals.value.some((proposal) => proposalIdsEqual(proposal.id, p.id))) {
    await clearProposalDeepLink(p.id)
  }
}

async function onFileAwayBulk() {
  if (isArchivedHistory.value) return
  if (busy.value) {
    toast.info(t('review.toast.bulkBusy'))
    return
  }
  const deepLinkedId = hashProposalId.value
  const filedAwayIds = [...ownedDismissableIds.value]
  retainKnownDecisions(
    proposals.value.filter((proposal) =>
      filedAwayIds.some((id) => proposalIdsEqual(proposal.id, id)),
    ),
  )
  await handleDismissApplied()
  if (
    deepLinkedId &&
    !proposals.value.some((proposal) => proposalIdsEqual(proposal.id, deepLinkedId))
  ) {
    await clearProposalDeepLink(deepLinkedId)
  }
}

async function onApply() {
  if (isArchivedHistory.value) return
  const p = activeProposal.value
  if (!p) return
  if (applyGuardBusy.value) return
  // Captured here, before anything can await: the primary button is disabled —
  // and so blurred — for the whole approve round trip (GH-1942).
  applyReturnFocusEl = decisionRailFocusTarget()
  if (revisionBusy.value) {
    toast.info(t('review.toast.revisionBusyApply'))
    return
  }
  if (!isApplyActionable(p)) {
    toast.info(t('review.toast.notApplyable'))
    return
  }
  // #1397 P2-A: SEAM INVARIANT — a zero-operation proposal is only approved (or
  // executed, #1414 round 4 P2-B: this guard sits BEFORE the Approved dispatch,
  // so pre-#1423 Approved zero-op data gets the same verdict instead of a doomed
  // execute round-trip) when its revision state is KNOWN (revisionsLoaded true).
  // A saved revision carries operations the backend applies revision-aware
  // (#1235); without the list we cannot distinguish "truly empty" from
  // "revised". Unknown after the attempt (failed, or cancelled by a concurrent
  // load) blocks the action. Since #1423 the backend enforces this at approve
  // time too (zero-op approve 400s server-side, revision-aware) — this guard is
  // UX-only: it saves the round-trip and gives the inline verdict.
  if ((p.operations?.length ?? 0) === 0) {
    if (!revisionsLoaded.value) {
      applyGuardBusy.value = true
      try {
        await loadRevisionState(p.id)
      } finally {
        applyGuardBusy.value = false
      }
      // The active proposal can change during the await; only keep deciding on it.
      if (!proposalIdsEqual(activeProposal.value?.id, p.id)) return
      // #1414 round 4 P2-A: identity is not enough — the proposal's STATE can
      // change under the await from another surface or session (deferred,
      // dismissed, status change) even while the shared busy lock holds this
      // rail. Re-check actionability on the CURRENT object and no-op if it is
      // no longer applyable or has been snoozed.
      const current = activeProposal.value
      if (!current || !isApplyActionable(current) || isProposalDeferred(current)) return
    }
    if (!revisionsLoaded.value) {
      // Load failed or was cancelled — the revision-history-unavailable error
      // toast (from loadRevisionState) covers the failure case; this names why
      // Apply did not proceed.
      toast.info(t('review.toast.revisionStateUnknown'))
      return
    }
    if (revisionCount.value === 0) {
      const approvedZeroOp =
        normalizeProposalStatus((activeProposal.value ?? p).status) === 'Approved'
      toast.info(
        approvedZeroOp
          ? t('review.toast.zeroOpApproved')
          : t('review.toast.zeroOpPending'),
      )
      return
    }
  }
  const status = normalizeProposalStatus((activeProposal.value ?? p).status)
  if (status === 'Approved') {
    // Phase 2 — opens the in-app confirmation (#1818); only its accept button
    // reaches executeProposal, preserving the explicit second step of ADR-0003.
    requestExecuteProposal(p.id)
    return
  }
  await handleApproveProposal(p.id)
  const recordedApproval = proposals.value.find((item) => proposalIdsEqual(item.id, p.id))
  if (recordedApproval && normalizeProposalStatus(recordedApproval.status) === 'Approved') {
    // The queue stays interactive while approval is in flight. Prefer the
    // explicit selection over the route hash because router navigation can lag
    // behind a queue click; a late response must never restore a proposal the
    // reviewer has already left. The explicit id also survives the approved row
    // leaving the visible queue, which keeps the intended receipt available when
    // the reviewer did stay at this decision locus.
    const currentDecisionLocusId = explicitActiveId.value ?? activeProposal.value?.id
    if (!proposalIdsEqual(currentDecisionLocusId, p.id)) return
    // Approval is its own decision. The remaining board write stays behind the
    // visible, explicit Apply action rather than opening a handoff dialog.
    recordDecisionReceipt(p.id, 'approved')
    return
  }
  // GH-1942: a successful approve hands STRAIGHT to the one deliberate execute
  // step. Before this, the user clicked the primary button, watched it relabel,
  // clicked it again, and only then got the dialog — three clicks for a
  // two-phase decision, and the middle one did nothing but open the third.
  //
  // ADR-0003 is untouched: approve and execute remain two separate, explicit
  // API calls in that order. The approve call has already returned here; the
  // execute call still happens ONLY if the human accepts the dialog, and
  // dismissing it leaves the proposal approved-but-not-applied with the banner
  // and the ember rail saying exactly that. Nothing auto-applies.
  //
  // GH-1942 L1: the queue rail stays clickable through the approve round trip
  // (only the decision buttons and the keymap take the busy lock), so the
  // reviewer can be looking at a different proposal by the time approve
  // returns. Opening the confirmation then would ask them to write a proposal
  // they have navigated away from. The approval itself stands either way — the
  // ember rail and the approved-but-not-applied banner carry it, and the
  // primary button reopens this same step — so skip the hand-off rather than
  // open it against a switched context.
  //
  // Known caveat: under the `stale` queue filter, approving removes the
  // proposal from the filtered list (stale is PendingReview-gated), the active
  // selection moves on, and this guard suppresses the confirmation with none of
  // the recovery affordances above on screen — the row is simply gone until the
  // filter changes. Matches pre-collapse behaviour; the durable fix is keeping
  // a just-decided row visible, which is GH-1940's progressive-disclosure work.
  if (!proposalIdsEqual(activeProposal.value?.id, p.id)) return
  const approved = proposals.value.find((item) => proposalIdsEqual(item.id, p.id))
  if (!approved) return
  // Approve failed (the composable toasts and leaves the row untouched), or the
  // row came back as something else entirely — either way there is no approved
  // proposal to offer, so do not open a confirmation for it.
  if (normalizeProposalStatus(approved.status) !== 'Approved') return
  requestExecuteProposal(p.id)
}

function onReject() {
  if (isArchivedHistory.value) return
  const p = activeProposal.value
  if (!p) return
  // ⌫ is dual-purpose: on a settled proposal the rail shows "File away", so
  // the same key files it away instead of rejecting (single-key consistency
  // for "remove this from my queue"). #1161
  if (activeDismissable.value) {
    void onFileAway()
    return
  }
  if (revisionBusy.value) {
    toast.info(t('review.toast.revisionBusyReject'))
    return
  }
  // #1414 round 4 P2-A: mirror onDefer/onFileAway — a decision must not fire
  // while another action holds the shared lock (notably the zero-op apply
  // guard's revision-load await). The keymap (!busy gate) and the disabled
  // button already block Reject during that window; this internal guard keeps
  // the invariant local so a future caller can't reopen the Defer/Reject-under-
  // Apply race the P2-A busy join closed.
  if (busy.value) return
  if (!isRejectActionable(p)) {
    toast.info(t('review.toast.notRejectable'))
    return
  }
  // GH-1969: opens the in-app reason dialog; only its accept button reaches
  // rejectProposal. The reason stays optional for Low/Medium risk.
  requestRejectProposal(p.id)
}

function onRequestEdit() {
  if (isArchivedHistory.value) return
  const p = activeProposal.value
  if (!p) return
  if (revisionSaving.value) return
  if (normalizeProposalStatus(p.status) !== 'PendingReview' || isProposalExpired(p)) {
    toast.info(t('review.toast.notEditable'))
    return
  }
  startRevisionEditing()
}

async function onDefer() {
  if (isArchivedHistory.value) return
  const p = activeProposal.value
  if (!p) return
  if (revisionBusy.value) {
    toast.info(t('review.toast.revisionBusyDefer'))
    return
  }
  // Another action is already in flight (approve/reject/defer/dismiss/bulk).
  if (busy.value) return
  // Defer shares Reject's precondition: a live, non-expired PendingReview proposal.
  if (!isRejectActionable(p)) {
    toast.info(t('review.toast.notDeferrable'))
    return
  }
  const deferred = await handleDeferProposal(p.id)
  // Only on SUCCESS: if we reached this proposal via a #proposal-<id> deep link, snoozing it must
  // drop it from the queue, so clear the hash (the visibleProposals carve-out then stops exempting
  // it from the deferred filter). On FAILURE we must NOT clear the hash — an already-snoozed
  // deep-linked proposal whose re-defer failed would otherwise vanish (its prior deferredUntil is
  // still in effect) with no retry path, despite the error toast.
  if (deferred) {
    recordDecisionReceipt(p.id, 'deferred')
    void clearProposalDeepLink(p.id)
  }
}

async function onConfirmExecute() {
  const proposalId = executeConfirmProposal.value?.id
  await confirmExecuteProposal()
  const applied = proposalId
    ? proposals.value.find((proposal) => proposalIdsEqual(proposal.id, proposalId))
    : undefined
  if (applied && normalizeProposalStatus(applied.status) === 'Applied') {
    recordDecisionReceipt(applied.id, 'applied')
  }
}

async function onConfirmReject(reason: string) {
  const proposalId = rejectPromptProposal.value?.id
  await confirmRejectProposal(reason)
  const rejected = proposalId
    ? proposals.value.find((proposal) => proposalIdsEqual(proposal.id, proposalId))
    : undefined
  if (rejected && normalizeProposalStatus(rejected.status) === 'Rejected') {
    recordDecisionReceipt(rejected.id, 'rejected')
  }
}

function onToggleProvenance() {
  toast.info(t('review.toast.provenanceToggleUnwired'))
}

// #1414 P2: revealing the stored `diffPreview` locally skips the `/diff` call
// that used to re-run AuthorizeProposalAsync, so re-authorize on reveal. Render
// SYNCHRONOUSLY (the #1397 seam invariant: local content is never network-gated),
// then probe access via GET proposal — which returns 200 for a still-readable
// terminal/expired proposal (unlike `/diff`, it does not 400 on expiry). ONLY a
// genuine 403/404 retracts the preview; a transient error must not tear down an
// inspectable local preview. The refreshed DTO is deliberately NOT rendered:
// #1397 keeps the decision-time stored artifact (a live re-render can drift).
// Guarded by requestId + proposal id so a late response for a toggled-off or
// switched proposal cannot tear down the wrong pane.
async function verifyStoredPreviewAccess(proposalId: string, requestId: number) {
  try {
    await automationApi.getProposal(proposalId)
  } catch (e: unknown) {
    if (!isAccessDeniedError(e)) return
    if (
      requestId !== latestDiffRequestId ||
      !proposalIdsEqual(previewDiffProposalId.value, proposalId)
    ) return
    clearPreviewDiff()
    toast.error(t('review.toast.noLongerAvailable'))
  }
}

// Present a proposal's STORED preview read-only (no live `/diff`). The stored
// `diffPreview` is LOCAL content and renders synchronously. Bumping the diff
// request id also cancels any live fetch that this preview supersedes, so a
// late 400 from a superseded request cannot overwrite the stored pane.
function presentStoredPreview(target: ApiProposal) {
  const requestId = ++latestDiffRequestId
  previewDiffProposalId.value = target.id
  previewDiff.value = target.diffPreview
  previewDiffMode.value = 'stored'
  previewDiffInvalidReason.value = null
  previewDiffLoading.value = false
  scrollDiffIntoView()
  void verifyStoredPreviewAccess(target.id, requestId)
}

async function onPreviewDiff() {
  const p = activeProposal.value
  if (!p) return

  // Already showing this proposal's diff → toggle it off.
  if (proposalIdsEqual(previewDiffProposalId.value, p.id)) {
    latestDiffRequestId += 1
    clearPreviewDiff()
    return
  }

  // Read-only / terminal proposals (expired, Applied, Rejected, Failed,
  // Dismissed) never fire the live diff — PR #1395 makes `/diff` 400 for them.
  // Present the stored `diffPreview` under an explicit read-only banner; when no
  // stored preview exists, the banner + a "no stored preview" state, never an
  // error toast (#1397 maintainer decision: stay inspectable, don't burden the
  // UI).
  //
  // SEAM INVARIANT (#1397 round 3): the stored preview is LOCAL content and
  // renders SYNCHRONOUSLY — no network gate may delay or veto it. The revision
  // metadata GET (which only gates the revised-note caveat, #1397 MEDIUM-2:
  // `diffPreview` is creation-time content revisions never update) runs async
  // AFTERWARDS, augment-only and silent: a slow GET must not make the toggle
  // look dead, and a failed GET must not error-toast over a presentable
  // preview. Staleness of the caveat itself is handled inside loadRevisionState
  // (generation counter + active-proposal-id re-check).
  //
  // PREVIEW-SOURCE SELECTION SEAM (#1397 Q1, maintainer ruling 2026-07-18):
  // for a terminal/expired proposal we present the STORED `diffPreview`
  // (`previewDiff.value = p.diffPreview` below) rather than a live diff. This is
  // BY DESIGN — the stored preview is the artifact the reviewer actually saw at
  // decision time; a live diff of a terminal proposal can drift from it (a later
  // revision, board change, or backend re-render), which would break review-first
  // provenance. The alternative (live diff for non-expired *terminal* proposals,
  // Codex's Q1 suggestion) would be switched HERE: narrow this branch to only
  // isExpired/domain-Expired and let non-expired terminal statuses fall through to
  // the live `/diff` fetch below. See issue #1397 for the option analysis and the
  // experiment path before making that flip.
  if (isProposalReadOnly(p, isProposalExpired(p))) {
    presentStoredPreview(p)
    if (!revisionsLoaded.value) {
      void loadRevisionState(p.id, { silent: true })
    }
    return
  }

  // A saved revision is rendered revision-aware by the backend (#1235), so a
  // proposal with a revision must fetch even when its ORIGINAL operations are
  // empty. Settle the revision list first so a still-loading revisionCount of 0
  // can't short-circuit a revised proposal to the invalid surface.
  if (!revisionsLoaded.value) {
    await loadRevisionState(p.id)
    if (!proposalIdsEqual(activeProposal.value?.id, p.id)) return
    // #1414 final round: identity is not enough. The proposal can transition to
    // read-only DURING the revision-load await — expire on the 60s clock, be
    // refreshed to a terminal status, or be executed from another session — all
    // with the SAME id, so the identity check above passes. Re-run the read-only
    // classification on the CURRENT object and divert to the stored preview
    // rather than falling through to a live `/diff` that #1395 guarantees will
    // 400 for a terminal/expired proposal (which would defeat #1397's stored-
    // preview guarantee and show the invalid/error surface). Mirrors the onApply
    // post-await re-check. The read-only watcher (which converts an ALREADY-open
    // pane) cannot cover this window: previewDiffProposalId is not set to this id
    // until after the await, so the watcher is inert here.
    const current = activeProposal.value
    if (current && isProposalReadOnly(current, isProposalExpired(current))) {
      presentStoredPreview(current)
      return
    }
  }

  // Zero-operation proposals: the backend `/diff` rejects them with 400
  // ValidationError "Proposal must contain at least one operation" — the same
  // rejection Apply gives (#1376 preview == apply). Surface that verdict as an
  // explicit invalid state (with or without a cached preview) so the reviewer
  // sees the rejection BEFORE approving, instead of a "No changes" surface they
  // could approve into a 400. Only take this path when the revision list is
  // authoritatively loaded and empty — a saved revision carries operations the
  // backend renders revision-aware (#1235), and if the revision load failed we
  // fetch so the backend, not a stale count, decides. #1397
  if (
    (p.operations?.length ?? 0) === 0 &&
    revisionsLoaded.value &&
    revisionCount.value === 0
  ) {
    latestDiffRequestId += 1
    previewDiffProposalId.value = p.id
    previewDiff.value = null
    previewDiffMode.value = 'invalid'
    previewDiffInvalidReason.value = t('review.diff.invalid.noOperations')
    previewDiffLoading.value = false
    scrollDiffIntoView()
    return
  }

  const requestId = ++latestDiffRequestId
  previewDiffProposalId.value = p.id
  previewDiff.value = null
  previewDiffMode.value = 'live'
  previewDiffInvalidReason.value = null
  previewDiffLoading.value = true
  // Scroll to the loading state immediately so a slow `/diff` doesn't look like a
  // no-op on a long review surface (the final result re-scrolls when it lands).
  scrollDiffIntoView()

  try {
    const diff = await automationApi.getProposalDiff(p.id)
    // Ignore stale responses and ones whose target proposal has changed.
    if (
      requestId !== latestDiffRequestId ||
      !proposalIdsEqual(previewDiffProposalId.value, p.id)
    ) return
    previewDiff.value = diff
    previewDiffMode.value = 'live'
    scrollDiffIntoView()
  } catch (e: unknown) {
    if (
      requestId !== latestDiffRequestId ||
      !proposalIdsEqual(previewDiffProposalId.value, p.id)
    ) return
    // A 400 ValidationError means the backend ran Apply's gates at diff time
    // (#1376/#1395). It carries one of two distinct reasons — "Proposal must
    // contain at least one operation" or "Proposal has expired" (the expiry
    // race: the 60s review clock can lag a server-side expiry). Present the
    // backend's ACTUAL message inline — never one hardcoded reason for every
    // 400 — rather than tearing the pane down + toasting (#1397 MEDIUM-1).
    if (isValidationError(e)) {
      previewDiff.value = null
      previewDiffMode.value = 'invalid'
      // Use the backend's ACTUAL reason, but treat a blank message as absent so
      // the template's specific "no operations" fallback copy applies rather than
      // the generic ValidationError string masking it (#1397 / #1414 review).
      previewDiffInvalidReason.value = getValidationReason(e)
      previewDiffLoading.value = false
      scrollDiffIntoView()
      return
    }
    // Any other failure (e.g. a 404 because the proposal was deleted/dismissed
    // from another session) stays a toast with a clean teardown.
    clearPreviewDiff()
    toast.error(getErrorDisplay(e, t('review.toast.diffFailed')).message)
  } finally {
    if (requestId === latestDiffRequestId) {
      previewDiffLoading.value = false
    }
  }
}

async function onSaveRevision(payload: Parameters<typeof saveRevision>[0]) {
  if (isArchivedHistory.value) return
  await saveRevision(payload)
  // Saving an edit changes what Apply will execute, so a diff already on screen is
  // now stale — drop it so the "reflects your saved edit" note cannot certify a
  // pre-revision preview (#1235). Re-opening the diff fetches the revision-aware one.
  if (previewDiffProposalId.value) {
    latestDiffRequestId += 1
    clearPreviewDiff()
  }
}

async function onReportBadSuggestion(proposalId: string) {
  if (isArchivedHistory.value) return
  if (!proposalId) {
    toast.error(t('review.toast.noProposalToReport'))
    return
  }
  // Don't double-submit while a report for this proposal is already in flight.
  if (proposalIdsEqual(reportingProposalId.value, proposalId)) return

  reportingProposalId.value = proposalId
  try {
    await automationApi.reportBadSuggestion(proposalId)
    // Pure feedback: the proposal stays exactly where it was (review-first, no decision).
    toast.success(t('review.toast.feedbackRecorded'))
  } catch (e: unknown) {
    toast.error(getErrorDisplay(e, t('review.toast.feedbackFailed')).message)
  } finally {
    reportingProposalId.value = null
  }
}

useReviewKeymap(
  {
    onApply,
    onReject,
    onRequestEdit,
    onDefer,
    onToggleProvenance,
    onPreviewDiff,
  },
  {
    // #1818: while the apply confirmation is open the dialog owns the keyboard —
    // ⏎ must not re-dispatch onApply behind it, and ⌫/D/E must not decide on a
    // proposal the user is being asked to confirm. GH-1969 gives the reject
    // dialog the same standing: ⌫ behind it would re-open the gate it IS.
    enabled: () =>
      !isArchivedHistory.value &&
      !busy.value &&
      activeProposal.value !== null &&
      (activeAppliedProposal.value === null || activeDismissable.value) &&
      executeConfirmProposal.value === null &&
      rejectPromptProposal.value === null &&
      (activeDecisionReceipt.value === null || activeDecisionReceipt.value === 'approved'),
    isActionEnabled: (action) => {
      // An applied record is read-only: the only live key is ⌫, whose #1161
      // dual-purpose branch files the record away — the affordance the filing
      // rail still advertises for the reviewer's own applied proposal.
      if (activeAppliedProposal.value !== null) return action === 'onReject'
      const receipt = activeDecisionReceipt.value
      return receipt === null || (receipt === 'approved' && action === 'onApply')
    },
  },
)

// --- Lifecycle ---------------------------------------------------------

/**
 * Whether a background queue tick may land right now (#2194).
 *
 * The poll must never move the ground under a decision in progress, so it holds
 * off while an action is in flight or a confirm dialog is open. `busy` already
 * folds in the revision-editor lock, bulk dismiss, and the batch-approve
 * confirmation; the two dialog refs cover the Apply and Reject gates, which are
 * the moments the reviewer is being asked to commit to the exact record on
 * screen. A held tick is simply skipped -- the next one lands 15 s later, and
 * closing the dialog also releases the hold.
 */
function canRefreshQueue(): boolean {
  return (
    !busy.value &&
    executeConfirmProposal.value === null &&
    rejectPromptProposal.value === null
  )
}

/**
 * Keep the shell's `Review · N` badge honest while Review is the open surface
 * (#2194 acceptance 3).
 *
 * The badge reads `workspace.homeSummary.workload.proposalsPendingReview`,
 * which `AppShell` fetches once when the session authenticates and which is
 * otherwise refreshed only by a capture or a Home visit -- so it froze at its
 * mount-time value and survived every decision made here.
 *
 * The trigger is the queue ARRAY, not the awaiting COUNT. Watching the count
 * misses two real cases: while Review is board-scoped a proposal arriving on
 * another board never moves the scoped count, and a decision that swaps one
 * pending row for another leaves it unchanged. Every decision path and every
 * successful background refresh assigns a new array, so this fires exactly once
 * per queue change. It is only a trigger -- the authoritative number is re-read
 * from the server, and `refreshWorkloadCounts` is version-guarded, silent on
 * failure, and a no-op until a summary exists.
 */
let badgeSyncArmed = false
watch(proposals, () => {
  // Skip the first assignment: that is the route-entry load, and AppShell has
  // already read the summary this badge renders (review round L-1).
  if (!badgeSyncArmed) {
    badgeSyncArmed = true
    return
  }
  void workspace.refreshWorkloadCounts()
})

onMounted(() => {
  startClock()
  void loadBoardOptions()
  void loadProposals()
  // The review queue has no realtime channel to subscribe to -- the only hub is
  // per-board and silent on proposals -- so a bounded, visibility-aware poll is
  // what keeps an open Review page from showing "Nothing waiting" while a
  // proposal sits pending server-side (#2194).
  startQueueRefresh(canRefreshQueue, { onQueueReplaced: onQueueReplacedByPoll })
  // Membership has no realtime event to subscribe to (the only hub is
  // per-board and silent on access grants), so the composable reads it once
  // here and refreshes on tab re-entry. See useWorkspaceCollaboration.
  void collaboration.start()
})

onUnmounted(() => {
  clearBatchSelection()
  stopClock()
  stopQueueRefresh()
  collaboration.stop()
})

function selectProposal(id: string) {
  // An explicit choice supersedes the #2215 A notice.
  activeProposalSettledElsewhere.value = null
  decisionReceipt.value = null
  explicitActiveId.value = id
  openProposal(id)
}

function returnToReview() {
  if (!unavailableProposalId.value) return
  void clearProposalDeepLink(unavailableProposalId.value)
}

function onQueueFilterChange(filter: QueueFilter) {
  clearBatchSelection()
  activeProposalSettledElsewhere.value = null
  // A receipt is authoritative until the reviewer explicitly selects another
  // proposal. In particular, a filter must not rewrite a deep link to a
  // fallback row after the linked proposal has just been decided. #1940
  if (decisionReceipt.value) {
    queueFilter.value = filter
    return
  }
  const selectedId = activeProposal.value?.id ?? explicitActiveId.value ?? hashProposalId.value
  queueFilter.value = filter
  const retained = selectedId
    ? filteredVisibleProposals.value.find((proposal) => proposalIdsEqual(proposal.id, selectedId))
    : undefined
  if (retained) {
    explicitActiveId.value = retained.id
    return
  }
  const fallbackId = preferredActiveProposalId(filteredVisibleProposals.value)
  explicitActiveId.value = fallbackId
  if (hashProposalId.value) {
    if (fallbackId) {
      openProposal(fallbackId)
    } else {
      // An empty filtered queue must not leave the old deep link active. The
      // hash carve-out is authoritative only while its target remains in the
      // selected queue; otherwise the filtered-empty state must render.
      void clearProposalDeepLink(hashProposalId.value)
    }
  }
}

async function onClearBoardScope() {
  clearBatchSelection()
  await clearBoardFilter()
}
</script>

<template>
  <div
    class="paper paper-review-deep"
    data-testid="paper-review-view"
    :data-history-mode="isArchivedHistory ? 'archived' : undefined"
  >
    <ReviewQueueRail
      :items="queueItems"
      :active-id="activeProposal?.id ?? null"
      :awaiting-count="awaitingCount"
      :stale-count="staleCount"
      :scope-label="boardScopeLabel"
      :scope-clear-label="$t('review.scope.clear')"
      :dismissable-count="bulkDismissableCount"
      :batch-selected-count="batchSelectedCount"
      :batch-executable-count="isArchivedHistory ? 0 : batchExecutableCount"
      :busy="busy"
      :recently-applied="recentlyApplied"
      :cadence="cadence"
      :apply-rate="applyRate"
      :author-partition-available="authorPartitionAvailable"
      @filter-change="onQueueFilterChange"
      @select="selectProposal"
      @toggle-batch="toggleBatchSelection"
      @request-batch-approval="requestBatchApproval"
      @request-batch-execute="requestBatchExecute"
      @file-away-all="onFileAwayBulk"
      @clear-scope="onClearBoardScope"
    />

    <div v-if="activeProposal" ref="mainColRef" class="paper-review-deep__main-col">
      <div
        v-if="revisionCount > 0"
        class="paper-review-deep__revision-badge"
        data-testid="revision-badge"
      >
        {{ revisionBadge }}
      </div>
      <ReviewMain
        :key="activeProposal.id"
        :serial="headerSerial"
        :meta="headerMeta"
        :title-parts="titleParts"
        :lede="lede"
        :decision-summary="decisionSummary"
        :busy="busy"
        :confidence="selectors.confidenceBreakdown.value"
        :before="before"
        :after="after"
        :fields="fields"
        :change-sub-title="changeSubTitle"
        :provenance="selectors.provenance.value"
        :evidence-links="selectors.evidenceLinks.value"
        :proposal-id="activeProposal?.id ?? ''"
        :side-effects="selectors.sideEffects.value"
        :conflicts="selectors.conflicts.value"
        :history="selectors.history.value"
        :dismissable="activeDismissable"
        :apply-phase="applyPhase"
        :edit-lock="editLock"
        :read-only="isArchivedHistory"
        :decision-receipt="activeDecisionReceipt"
        :applied-proposal="activeAppliedProposal"
        @apply="onApply"
        @reject="onReject"
        @request-edit="onRequestEdit"
        @defer="onDefer"
        @dismiss="onFileAway"
        @cancel-edit="cancelRevisionEditing"
        @report="onReportBadSuggestion"
      />
      <details
        class="paper-review-deep__technical-details"
        data-testid="paper-review-technical-details"
      >
        <summary>{{ $t('review.technical.summary') }}</summary>
        <button
          type="button"
          class="td-btn td-btn--secondary td-btn--sm"
          :disabled="!technicalDetails"
          @click="copyTechnicalDetails"
        >
          {{ technicalDetailsCopied ? $t('review.technical.copied') : $t('review.technical.copy') }}
        </button>
        <pre :aria-label="$t('review.technical.ariaLabel')">{{ technicalDetails }}</pre>
      </details>
      <section
        v-if="proposalIdsEqual(previewDiffProposalId, activeProposal.id)"
        ref="previewDiffSection"
        class="paper-review-deep__diff"
        data-testid="paper-review-diff"
      >
        <header class="paper-review-deep__diff-head">
          <span class="tk-serial paper-review-deep__diff-serial">{{ $t('review.diff.serial') }}</span>
          <h3 class="tk-h3 paper-review-deep__diff-title">{{ $t('review.diff.title') }}</h3>
          <span class="tk-meta paper-review-deep__diff-sub">{{ $t('review.diff.hint') }}</span>
        </header>
        <!-- Read-only banner: a terminal/expired proposal's stored preview (#1397) -->
        <p
          v-if="previewDiffMode === 'stored'"
          class="paper-review-deep__diff-banner tk-meta"
          role="status"
          data-testid="paper-review-diff-banner"
        >
          {{ $t('review.diff.storedBanner', { status: previewReadOnlyLabel }) }}
        </p>
        <!-- diffPreview is creation-time content revisions never update, so a revised
             proposal's stored preview — or the recorded-operations fallback when no
             preview was captured — is NOT what a revision-aware Apply would have
             executed. Disclose it, and word it for whichever is actually on screen
             (the fallback is not a "stored preview") (#1397 MEDIUM-2 / #1414 review). -->
        <p
          v-if="previewDiffMode === 'stored' && revisionCount > 0 && previewDiff"
          class="paper-review-deep__diff-caveat tk-meta"
          role="status"
          data-testid="paper-review-diff-revised-note"
        >
          ✎ {{ $t('review.diff.revised.lead') }}
          <strong>{{ $t('review.diff.revised.emphasis') }}</strong>
          {{ $t('review.diff.revised.storedTail') }}
        </p>
        <p
          v-else-if="previewDiffMode === 'stored' && revisionCount > 0 && storedOperationsFallback"
          class="paper-review-deep__diff-caveat tk-meta"
          role="status"
          data-testid="paper-review-diff-revised-note"
        >
          ✎ {{ $t('review.diff.revised.lead') }}
          <strong>{{ $t('review.diff.revised.emphasis') }}</strong>
          {{ $t('review.diff.revised.fallbackTail') }}
        </p>
        <p
          v-if="revisionCount > 0 && previewDiffMode === 'live'"
          class="paper-review-deep__diff-caveat tk-meta"
          data-testid="paper-review-diff-revision-caveat"
        >
          ✎ {{ $t('review.diff.liveCaveat.lead') }}
          <strong>{{ $t('review.diff.liveCaveat.emphasis') }}</strong>
          {{ $t('review.diff.liveCaveat.tail') }}
        </p>
        <div class="card paper-review-deep__diff-card">
          <p
            v-if="previewDiffLoading"
            class="paper-review-deep__diff-empty tk-meta"
            data-testid="paper-review-diff-loading"
          >
            {{ $t('review.diff.loading') }}
          </p>
          <!-- Invalid: the backend's Apply-time gates reject this proposal; render
               the ACTUAL reason (expired vs zero-op), never a hardcoded one
               (#1397 MEDIUM-1). -->
          <p
            v-else-if="previewDiffMode === 'invalid'"
            class="paper-review-deep__diff-invalid tk-meta"
            role="status"
            data-testid="paper-review-diff-invalid"
          >
            {{
              $t('review.diff.invalid.line', {
                reason: previewDiffInvalidReason || $t('review.diff.invalid.noOperations'),
              })
            }}
          </p>
          <!-- Read-only proposal without a stored preview: fall back to the
               proposal's own recorded operations before giving up (#1397 /
               Codex review on #1414). -->
          <pre
            v-else-if="storedOperationsFallback"
            class="paper-review-deep__diff-pre"
            role="region"
            :aria-label="$t('review.diff.recordedAriaLabel')"
            data-testid="paper-review-diff-stored-operations"
          >{{ storedOperationsFallback }}</pre>
          <p
            v-else-if="previewDiffMode === 'stored' && !previewDiff"
            class="paper-review-deep__diff-empty tk-meta"
            data-testid="paper-review-diff-stored-empty"
          >
            {{ $t('review.diff.storedEmpty') }}
          </p>
          <p
            v-else-if="!previewDiff"
            class="paper-review-deep__diff-empty tk-meta"
            data-testid="paper-review-diff-empty"
          >
            {{ $t('review.diff.empty') }}
          </p>
          <pre
            v-else
            class="paper-review-deep__diff-pre"
            role="region"
            :aria-label="
              previewDiffMode === 'stored'
                ? $t('review.diff.storedAriaLabel')
                : $t('review.diff.liveAriaLabel')
            "
            data-testid="paper-review-diff-pre"
          >{{ displayedPreviewDiff }}</pre>
        </div>
      </section>
      <ReviewRevisionEditor
        v-if="revisionEditing && !isArchivedHistory"
        :operations-payload="editablePayload"
        :saving="revisionSaving"
        @save="onSaveRevision"
        @cancel="cancelRevisionEditing"
      />
    </div>
    <div v-else class="paper-review-deep__empty" data-testid="paper-review-empty">
      <template v-if="queueAccessRevoked">
        <div class="tk-eyebrow">{{ $t('review.empty.eyebrow', { count: 0 }) }}</div>
        <h2 class="tk-h2" data-testid="paper-review-access-revoked">
          {{ $t('review.empty.accessRevoked.title') }}
        </h2>
        <p class="tk-lede">{{ $t('review.empty.accessRevoked.body') }}</p>
      </template>
      <template v-else-if="activeProposalSettledElsewhere">
        <div class="tk-eyebrow">{{ $t('review.empty.settledElsewhere.eyebrow') }}</div>
        <h2 class="tk-h2" data-testid="paper-review-settled-elsewhere">
          {{ $t('review.empty.settledElsewhere.title') }}
        </h2>
        <p class="tk-lede" role="status" aria-live="polite">
          {{ $t('review.empty.settledElsewhere.body') }}
        </p>
        <button
          ref="settledElsewhereReturnRef"
          type="button"
          class="paper-review-deep__clear-scope"
          data-testid="paper-review-settled-elsewhere-return"
          @click="dismissSettledElsewhereNotice"
        >
          {{ $t('review.empty.settledElsewhere.return') }}
        </button>
      </template>
      <template v-else-if="unavailableProposalId">
        <div class="tk-eyebrow">{{ $t('review.empty.unavailable.eyebrow') }}</div>
        <h2 class="tk-h2">{{ $t('review.empty.unavailable.title') }}</h2>
        <p class="tk-lede">
          {{ $t('review.empty.unavailable.body', { id: unavailableProposalId }) }}
        </p>
        <button
          type="button"
          class="paper-review-deep__clear-scope"
          data-testid="paper-review-unavailable-return"
          @click="returnToReview"
        >
          {{ $t('review.empty.unavailable.return') }}
        </button>
      </template>
      <template v-else-if="activeBoardFilter">
        <div class="tk-eyebrow">{{ $t('review.empty.eyebrow', { count: awaitingCount }) }}</div>
        <h2 class="tk-h2">{{ $t('review.empty.scoped.title', { scope: boardScopeLabel }) }}</h2>
        <p class="tk-lede">{{ $t('review.empty.scoped.body') }}</p>
        <button type="button" class="paper-review-deep__clear-scope" data-testid="paper-review-clear-scope" @click="clearBoardFilter">
          {{ $t('review.scope.clear') }}
        </button>
      </template>
      <template v-else-if="hasFilterEmptyState">
        <div class="tk-eyebrow">{{ $t('review.empty.eyebrow', { count: awaitingCount }) }}</div>
        <h2 class="tk-h2">{{ $t('review.empty.filtered.title', { filter: activeFilterLabel }) }}</h2>
        <p class="tk-lede">
          {{ $t('review.empty.filtered.body') }}
        </p>
      </template>
      <template v-else>
        <div class="tk-eyebrow">{{ $t('review.empty.eyebrow', { count: 0 }) }}</div>
        <h2 class="tk-h2">{{ $t('review.empty.title') }}</h2>
        <p class="tk-lede">
          {{ $t('review.empty.body') }}
        </p>
        <p v-if="proposalsLoading" class="tk-meta">{{ $t('review.empty.loading') }}</p>
      </template>
    </div>

    <ReviewRightRail
      v-if="activeProposal"
      :key="activeProposal.id"
      :author-name="authorName"
      :author-meta="authorMeta"
      :proposed-date="proposedDate"
      :proposed-time="proposedTime"
      :proposed-num="proposedNum"
      :why-now-body="whyNowBody"
      :breakdown="selectors.confidenceBreakdown.value"
      :similar-past="selectors.similarPast.value"
      :similar-past-apply-rate="selectors.similarPastApplyRate.value"
      :apply-phase="applyPhase"
      :apply-only="activeDecisionReceipt === 'approved'"
      :receipt-active="activeDecisionReceipt !== null"
      :applied-record="activeAppliedProposal !== null"
    />
    <aside v-else class="paper-review-deep__rail-empty"></aside>

    <!-- Phase-2 confirmation (#1818) — the app dialog idiom replacing the native
         confirm(); it carries the proposal summary so the user confirms what
         they are about to write to the board. -->
    <ApplyToBoardDialog
      :proposal="executeConfirmProposal"
      :busy="busy"
      :revision-count="applyConfirmRevisionCount"
      @confirm="onConfirmExecute"
      @cancel="cancelExecuteProposal"
    />

    <!-- Reason collection (GH-1969) — the in-app dialog that replaced the native
         window.prompt, the last browser dialog in the decision flow. -->
    <RejectProposalDialog
      :proposal="rejectPromptProposal"
      :busy="busy"
      :requires-reason="rejectRequiresReason"
      @confirm="onConfirmReject"
      @cancel="cancelRejectProposal"
    />

    <BatchApproveDialog
      :open="batchConfirmationOpen"
      :count="batchSelectedCount"
      :busy="batchApproveBusy"
      @confirm="confirmBatchApproval"
      @cancel="cancelBatchApproval"
    />

    <BatchExecuteDialog
      :open="batchExecuteOpen"
      :count="batchExecuteConfirmationCount"
      :busy="batchExecuteBusy"
      :receipts="batchExecuteReceipts"
      @confirm="confirmBatchExecute"
      @close="cancelBatchExecute"
    />
  </div>
</template>

<style scoped>
.paper-review-deep {
  display: grid;
  grid-template-columns: 280px 1fr 320px;
  min-height: 0;
  background: var(--paper);
  height: 100%;
  font-family: var(--sans);
}
.paper-review-deep__main-col {
  overflow: auto;
  min-width: 0;
}
.paper-review-deep__revision-badge {
  padding: 4px 12px;
  font-size: 11px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--ember, #c2410c);
  background: var(--paper-2);
  border-bottom: 1px solid var(--line);
  text-align: right;
}
.paper-review-deep__empty {
  padding: 80px 56px;
  text-align: left;
}
.paper-review-deep__clear-scope {
  margin-top: 16px;
  border: 1px solid var(--line);
  background: var(--paper-card);
  color: var(--ink);
  cursor: pointer;
  font-family: var(--mono);
  font-size: 11px;
  padding: 7px 10px;
}
.paper-review-deep__clear-scope:focus-visible {
  outline: 2px solid var(--ember);
  outline-offset: 2px;
}
.paper-review-deep__diff {
  margin: 0 36px 28px;
}
.paper-review-deep__diff-head {
  display: flex;
  align-items: baseline;
  gap: 14px;
  margin-bottom: 10px;
  padding-bottom: 8px;
  border-bottom: 1px solid var(--line-soft);
}
.paper-review-deep__diff-serial {
  color: var(--faint);
}
.paper-review-deep__diff-title {
  margin: 0;
}
.paper-review-deep__diff-sub {
  margin-left: auto;
}
.paper-review-deep__diff-card {
  padding: 0;
  overflow: hidden;
}
.paper-review-deep__diff-caveat {
  margin: 0 0 8px;
  color: var(--ink-2);
}
.paper-review-deep__diff-banner {
  margin: 0 0 8px;
  font-weight: 600;
  color: var(--ember, #c2410c);
}
.paper-review-deep__diff-invalid {
  padding: 16px;
  font-weight: 600;
  color: var(--ember, #c2410c);
}
.paper-review-deep__diff-empty {
  padding: 16px;
}
.paper-review-deep__diff-pre {
  margin: 0;
  padding: 16px;
  overflow-x: auto;
  font-family: var(--mono);
  font-size: 12.5px;
  line-height: 1.5;
  color: var(--ink-2, var(--ink));
  white-space: pre-wrap;
  word-break: break-word;
}
.paper-review-deep__rail-empty {
  border-left: 1px solid var(--line);
  background: var(--paper-2);
}
@media (max-width: 1100px) {
  .paper-review-deep {
    grid-template-columns: 240px 1fr;
  }
  .paper-review-deep__rail-empty,
  .paper-review-deep ::v-deep(.paper-review-right) {
    display: none;
  }
}
</style>
