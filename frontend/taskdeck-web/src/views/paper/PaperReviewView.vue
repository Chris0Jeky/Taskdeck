<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import ReviewQueueRail, {
  type QueueFilter,
  type QueueRailItem,
} from './review/ReviewQueueRail.vue'
import type { RecentlyAppliedRow } from './review/ReviewRecentApplied.vue'
import ReviewMain from './review/ReviewMain.vue'
import ReviewRevisionEditor from './review/ReviewRevisionEditor.vue'
import ReviewRightRail from './review/ReviewRightRail.vue'
import { useReviewProposals, isProposalReadOnly } from '../../composables/useReviewProposals'
import { useReviewActions } from '../../composables/useReviewActions'
import { usePaperReviewSelectors } from '../../composables/usePaperReviewSelectors'
import { useReviewKeymap } from '../../composables/useReviewKeymap'
import { useProposalRevisions } from '../../composables/useProposalRevisions'
import { getErrorDisplay, getValidationReason, isAccessDeniedError, isValidationError } from '../../composables/useErrorMapper'
import { automationApi } from '../../api/automationApi'
import { useSessionStore } from '../../store/sessionStore'
import { useToastStore } from '../../store/toastStore'
import {
  normalizeProposalSourceType,
  normalizeProposalStatus,
  sortProposalsByRisk,
} from '../../utils/automation'
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
  nowMs,
  visibleProposals,
  dismissableProposalIds,
  matchesActiveBoardFilter,
  isProposalExpired,
  isApplyActionable,
  isRejectActionable,
  isProposalDeferred,
  isProposalDismissable,
  isStaleProposal,
  clearProposalDeepLink,
  loadProposals,
  loadBoardOptions,
  availableBoards,
  startClock,
  stopClock,
} = useReviewProposals()
const session = useSessionStore()
const toast = useToastStore()
const route = useRoute()
const displayVersion = ref(0)
const technicalDetailsCopied = ref(false)

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
    const proposal = proposals.value.find((p) => p.id === id)
    return !!proposal && isOwnProposal(proposal)
  }),
)

const {
  proposalActionBusyId,
  bulkDismissBusy,
  handleApproveProposal,
  handleRejectProposal,
  handleDeferProposal,
  handleExecuteProposal,
  handleDismissProposal,
  handleDismissApplied,
} = useReviewActions(proposals, ownedDismissableIds, loadProposals, isProposalExpired)

// --- Active proposal ---------------------------------------------------

const explicitActiveId = ref<string | null>(null)
const queueFilter = ref<QueueFilter>('all')

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

const activeFilterLabel = computed(() => {
  if (queueFilter.value === 'mine') return 'Mine'
  if (queueFilter.value === 'stale') return 'Stale'
  return 'All'
})

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

const activeProposal = computed<ApiProposal | null>(() => {
  if (explicitActiveId.value) {
    const found = filteredVisibleProposals.value.find((p) => p.id === explicitActiveId.value)
    if (found) return found
  }
  if (hashProposalId.value) {
    const found = filteredVisibleProposals.value.find((p) => p.id === hashProposalId.value)
    if (found) return found
  }
  // Default to the first pending-review item in the queue.
  const preferredId = preferredActiveProposalId(filteredVisibleProposals.value)
  return filteredVisibleProposals.value.find((proposal) => proposal.id === preferredId) ?? null
})

watch(
  () => activeProposal.value?.id,
  (id) => {
    if (id && !explicitActiveId.value) {
      // sync explicit id so subsequent action results stay anchored
      explicitActiveId.value = id
    }
  },
)

watch(
  hashProposalId,
  (id) => {
    if (!id) return
    if (filteredVisibleProposals.value.some((proposal) => proposal.id === id)) {
      explicitActiveId.value = id
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
  if (isProposalExpired(p)) return 'Expired'
  return normalizeProposalStatus(p.status)
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
watch(
  () => activeProposal.value?.id ?? null,
  (id) => {
    if (previewDiffProposalId.value && previewDiffProposalId.value !== id) {
      latestDiffRequestId += 1
      clearPreviewDiff()
    }
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
    if (!p || previewDiffProposalId.value !== p.id) return false
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
    if (!p || previewDiffProposalId.value !== p.id) return
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
} = useProposalRevisions(activeProposal)

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
  if (ms < 60_000) return `${Math.max(1, Math.floor(ms / 1000))}s`
  if (ms < 60 * 60_000) return `${Math.floor(ms / 60_000)}m`
  if (ms < 24 * 60 * 60_000) return `${Math.floor(ms / (60 * 60_000))}h`
  return `${Math.floor(ms / (24 * 60 * 60_000))}d`
}

function summariseReach(proposal: ApiProposal): string {
  const ops = proposal.operations?.length ?? 0
  if (ops === 0) return '—'
  return `${ops} ${ops === 1 ? 'op' : 'ops'}`
}

const queueItems = computed<QueueRailItem[]>(() =>
  filteredVisibleProposals.value.map((p) => {
    const stale = isStaleProposal(p)
    return {
      id: p.id,
      serial: `#${p.id.slice(0, 4).toUpperCase()}`,
      title: p.summary || '(no summary)',
      who: normalizeProposalSourceType(p.sourceType) === 'Chat' ? 'assistant' : 'capture',
      // Per-item rail confidence is not yet wired per-proposal — leave null until
      // the gap lands. Not contradictory with `authorMeta` below, which shows the
      // REAL aggregate /confidence breakdown for the single active proposal.
      confidence: null,
      age: ageLabel(p.createdAt),
      reach: summariseReach(p),
      mine: !!session.userId && p.requestedByUserId === session.userId,
      stale,
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
      title: p.summary || '(applied)',
      // Pass the backend ISO string straight through (same pattern as queueItems):
      // ageLabel degrades gracefully on an unparseable value, whereas the previous
      // Date→ms→Date→toISOString roundtrip threw RangeError on an invalid date.
      age: ageLabel(p.appliedAt as string),
    }))
    .slice(0, 4)
})

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
  () =>
    activeProposal.value?.presentation?.plainSummary ??
    'Awaiting decision. Review the change, provenance, and side-effects below before applying.',
)

const decisionSummary = computed(() => {
  const p = activeProposal.value
  if (!p) return 'Nothing to decide right now'
  const ops = p.operations?.length ?? 0
  return `${ops} ${ops === 1 ? 'operation' : 'operations'} · explicit review · atomic apply`
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
  const time = created.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
  return `${time} · ${status === 'PendingReview' ? 'awaiting decision' : status.toLowerCase()}`
})

function formatActionLabel(actionType: string): string {
  return actionType
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/[_-]+/g, ' ')
    .trim()
}

function summarizeOperation(operation: ProposalOperation): string {
  const proposal = activeProposal.value
  if (!proposal) return 'No parameter preview supplied for this operation.'
  void displayVersion.value
  return proposalDisplayNames.summarizeOperation(proposal, operation)
}

const before = computed<ChangeBeforeCard>(() => {
  void displayVersion.value
  return {
    serial: activeProposal.value ? `#${activeProposal.value.id.slice(0, 8)}` : '—',
    title: activeProposal.value?.summary ?? 'No proposal selected',
    body:
      activeProposal.value?.presentation?.impactSummary ??
      `Review ${activeProposal.value?.operations?.length ?? 0} proposal operations before applying.`,
    meta: `${proposalDisplayNames.boardLabel(activeProposal.value?.boardId)} · ${activeProposal.value?.sourceType ?? 'proposal'}`,
  }
})

const after = computed<ChangeAfterCard[]>(() => {
  void displayVersion.value
  const p = activeProposal.value
  const operations = p?.operations ?? []
  if (operations.length === 0) {
    return [{
      serial: p ? `#${p.id.slice(0, 8)}.0` : '—',
      title: 'No operation preview',
      body: p?.diffPreview ? proposalDisplayNames.displayDiff(p, p.diffPreview) : 'The proposal did not include operation details.',
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
      key: 'operations',
      before: 'none',
      after: p?.diffPreview ? proposalDisplayNames.displayDiff(p, p.diffPreview) : 'not provided',
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
  return `${ops} ${ops === 1 ? 'operation' : 'operations'} · ${proposalDisplayNames.boardLabel(activeProposal.value?.boardId)}`
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
  return d.toLocaleString('default', { month: 'short', day: '2-digit' })
})

const proposedTime = computed(() => {
  const p = activeProposal.value
  if (!p) return ''
  return new Date(p.createdAt).toLocaleTimeString([], {
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
  // Only the confidence score is real wire data. Latency and token counts are
  // not yet surfaced by the backend, so we do not fabricate them here. #1136
  const c = selectors.confidenceBreakdown.value
  // Defensive: the type says overall is always a number, but guard against a
  // malformed/NaN value so toFixed can never throw on the deep-review surface.
  if (!c || !Number.isFinite(c.overall)) return ''
  return `${c.overall.toFixed(2)} confidence`
})

const authorName = computed(() => {
  const normalized = activeProposal.value
    ? normalizeProposalSourceType(activeProposal.value.sourceType)
    : null
  if (!normalized) return 'Proposal'
  // Same actor split as the queue rail above: only chat-driven proposals come from
  // the configured AI provider; capture triage may be the deterministic extractor
  // (see ReviewProvenance), so it must not be attributed to "Assistant".
  const actor = normalized === 'Chat' ? 'Assistant' : 'Capture'
  return `${actor} · ${normalized.toLowerCase()} proposal`
})

const whyNowBody = computed(() => {
  const p = activeProposal.value
  if (!p) return 'No proposal is selected.'
  return p.presentation?.sourceCue ?? 'This proposal is awaiting review based on the source captured with it.'
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
    applyGuardBusy.value,
)

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

function onFileAway() {
  const p = activeProposal.value
  if (!p) return
  if (revisionBusy.value) {
    toast.info('Save or cancel the revision before filing this proposal away.')
    return
  }
  // Another dismiss/approve/reject/bulk action is already in flight.
  if (busy.value) return
  if (!activeDismissable.value) {
    toast.info('This proposal is still active and cannot be filed away yet.')
    return
  }
  void handleDismissProposal(p.id)
}

function onFileAwayBulk() {
  if (busy.value) {
    toast.info('Wait for the current action to finish before filing more away.')
    return
  }
  void handleDismissApplied()
}

async function onApply() {
  const p = activeProposal.value
  if (!p) return
  if (applyGuardBusy.value) return
  if (revisionBusy.value) {
    toast.info('Save or cancel the revision before applying this proposal.')
    return
  }
  if (!isApplyActionable(p)) {
    toast.info('This proposal is no longer actionable. Refresh review to see current status.')
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
      if (activeProposal.value?.id !== p.id) return
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
      toast.info('Revision history is unavailable, so this proposal cannot be verified for Apply. Try again.')
      return
    }
    if (revisionCount.value === 0) {
      const approvedZeroOp =
        normalizeProposalStatus((activeProposal.value ?? p).status) === 'Approved'
      toast.info(
        approvedZeroOp
          ? 'This proposal contains no operations — applying it to the board will be rejected.'
          : 'This proposal contains no operations to apply — Apply will reject it. Reject or file it away instead.',
      )
      return
    }
  }
  const status = normalizeProposalStatus((activeProposal.value ?? p).status)
  if (status === 'Approved') {
    void handleExecuteProposal(p.id)
    return
  }
  void handleApproveProposal(p.id)
}

function onReject() {
  const p = activeProposal.value
  if (!p) return
  // ⌫ is dual-purpose: on a settled proposal the rail shows "File away", so
  // the same key files it away instead of rejecting (single-key consistency
  // for "remove this from my queue"). #1161
  if (activeDismissable.value) {
    onFileAway()
    return
  }
  if (revisionBusy.value) {
    toast.info('Save or cancel the revision before rejecting this proposal.')
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
    toast.info('This proposal can no longer be rejected. Refresh review to see current status.')
    return
  }
  void handleRejectProposal(p.id, p.riskLevel)
}

function onRequestEdit() {
  const p = activeProposal.value
  if (!p) return
  if (revisionSaving.value) return
  if (normalizeProposalStatus(p.status) !== 'PendingReview' || isProposalExpired(p)) {
    toast.info('This proposal can no longer be edited.')
    return
  }
  startRevisionEditing()
}

async function onDefer() {
  const p = activeProposal.value
  if (!p) return
  if (revisionBusy.value) {
    toast.info('Save or cancel the revision before deferring this proposal.')
    return
  }
  // Another action is already in flight (approve/reject/defer/dismiss/bulk).
  if (busy.value) return
  // Defer shares Reject's precondition: a live, non-expired PendingReview proposal.
  if (!isRejectActionable(p)) {
    toast.info('This proposal can no longer be deferred.')
    return
  }
  const deferred = await handleDeferProposal(p.id)
  // Only on SUCCESS: if we reached this proposal via a #proposal-<id> deep link, snoozing it must
  // drop it from the queue, so clear the hash (the visibleProposals carve-out then stops exempting
  // it from the deferred filter). On FAILURE we must NOT clear the hash — an already-snoozed
  // deep-linked proposal whose re-defer failed would otherwise vanish (its prior deferredUntil is
  // still in effect) with no retry path, despite the error toast.
  if (deferred) void clearProposalDeepLink(p.id)
}

function onToggleProvenance() {
  toast.info('Provenance toggle is not wired yet; provenance is rendered inline below.')
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
    if (requestId !== latestDiffRequestId || previewDiffProposalId.value !== proposalId) return
    clearPreviewDiff()
    toast.error('This proposal is no longer available to you.')
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
  if (previewDiffProposalId.value === p.id) {
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
    if (activeProposal.value?.id !== p.id) return
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
    previewDiffInvalidReason.value = 'This proposal contains no operations to apply'
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
    if (requestId !== latestDiffRequestId || previewDiffProposalId.value !== p.id) return
    previewDiff.value = diff
    previewDiffMode.value = 'live'
    scrollDiffIntoView()
  } catch (e: unknown) {
    if (requestId !== latestDiffRequestId || previewDiffProposalId.value !== p.id) return
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
    toast.error(getErrorDisplay(e, 'Failed to load proposal diff').message)
  } finally {
    if (requestId === latestDiffRequestId) {
      previewDiffLoading.value = false
    }
  }
}

async function onSaveRevision(payload: Parameters<typeof saveRevision>[0]) {
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
  if (!proposalId) {
    toast.error('No proposal is selected to report.')
    return
  }
  // Don't double-submit while a report for this proposal is already in flight.
  if (reportingProposalId.value === proposalId) return

  reportingProposalId.value = proposalId
  try {
    await automationApi.reportBadSuggestion(proposalId)
    // Pure feedback: the proposal stays exactly where it was (review-first, no decision).
    toast.success('Feedback recorded for this suggestion.')
  } catch (e: unknown) {
    toast.error(getErrorDisplay(e, 'Failed to record feedback').message)
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
    enabled: () => !busy.value && activeProposal.value !== null,
  },
)

// --- Lifecycle ---------------------------------------------------------

onMounted(() => {
  startClock()
  void loadBoardOptions()
  void loadProposals()
})

onUnmounted(() => {
  stopClock()
})

function selectProposal(id: string) {
  explicitActiveId.value = id
}

function onQueueFilterChange(filter: QueueFilter) {
  const selectedId = activeProposal.value?.id ?? explicitActiveId.value ?? hashProposalId.value
  queueFilter.value = filter
  if (selectedId && filteredVisibleProposals.value.some((proposal) => proposal.id === selectedId)) {
    explicitActiveId.value = selectedId
    return
  }
  explicitActiveId.value = preferredActiveProposalId(filteredVisibleProposals.value)
}
</script>

<template>
  <div class="paper paper-review-deep" data-testid="paper-review-view">
    <ReviewQueueRail
      :items="queueItems"
      :active-id="activeProposal?.id ?? null"
      :awaiting-count="awaitingCount"
      :stale-count="staleCount"
      :dismissable-count="bulkDismissableCount"
      :busy="busy"
      :recently-applied="recentlyApplied"
      @filter-change="onQueueFilterChange"
      @select="selectProposal"
      @file-away-all="onFileAwayBulk"
    />

    <div v-if="activeProposal" class="paper-review-deep__main-col">
      <div
        v-if="revisionCount > 0"
        class="paper-review-deep__revision-badge"
        data-testid="revision-badge"
      >
        {{ revisionCount }} {{ revisionCount === 1 ? 'revision' : 'revisions' }}
      </div>
      <ReviewMain
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
        :proposal-id="activeProposal?.id ?? ''"
        :side-effects="selectors.sideEffects.value"
        :conflicts="selectors.conflicts.value"
        :history="selectors.history.value"
        :dismissable="activeDismissable"
        @apply="onApply"
        @reject="onReject"
        @request-edit="onRequestEdit"
        @defer="onDefer"
        @dismiss="onFileAway"
        @report="onReportBadSuggestion"
      />
      <details
        class="paper-review-deep__technical-details"
        data-testid="paper-review-technical-details"
      >
        <summary>Technical details</summary>
        <button
          type="button"
          class="td-btn td-btn--secondary td-btn--sm"
          :disabled="!technicalDetails"
          @click="copyTechnicalDetails"
        >
          {{ technicalDetailsCopied ? 'Copied' : 'Copy technical details' }}
        </button>
        <pre aria-label="Proposal technical details">{{ technicalDetails }}</pre>
      </details>
      <section
        v-if="previewDiffProposalId === activeProposal.id"
        ref="previewDiffSection"
        class="paper-review-deep__diff"
        data-testid="paper-review-diff"
      >
        <header class="paper-review-deep__diff-head">
          <span class="tk-serial paper-review-deep__diff-serial">§ DIFF</span>
          <h3 class="tk-h3 paper-review-deep__diff-title">Operation details</h3>
          <span class="tk-meta paper-review-deep__diff-sub">Press Space to hide</span>
        </header>
        <!-- Read-only banner: a terminal/expired proposal's stored preview (#1397) -->
        <p
          v-if="previewDiffMode === 'stored'"
          class="paper-review-deep__diff-banner tk-meta"
          role="status"
          data-testid="paper-review-diff-banner"
        >
          {{ previewReadOnlyLabel }} · read-only — showing the stored preview from the
          original submission.
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
          ✎ This proposal was <strong>revised</strong> after submission — the stored
          preview shows the original operations, not the revised ones.
        </p>
        <p
          v-else-if="previewDiffMode === 'stored' && revisionCount > 0 && storedOperationsFallback"
          class="paper-review-deep__diff-caveat tk-meta"
          role="status"
          data-testid="paper-review-diff-revised-note"
        >
          ✎ This proposal was <strong>revised</strong> after submission — the recorded
          operations show the original submission, not the revised one.
        </p>
        <p
          v-if="revisionCount > 0 && previewDiffMode === 'live'"
          class="paper-review-deep__diff-caveat tk-meta"
          data-testid="paper-review-diff-revision-caveat"
        >
          ✎ This preview reflects your latest <strong>saved edit</strong> — the
          revised operations, not the original proposal.
        </p>
        <div class="card paper-review-deep__diff-card">
          <p
            v-if="previewDiffLoading"
            class="paper-review-deep__diff-empty tk-meta"
            data-testid="paper-review-diff-loading"
          >
            Loading diff…
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
            {{ previewDiffInvalidReason || 'This proposal contains no operations to apply' }}
            — Apply will reject this proposal.
          </p>
          <!-- Read-only proposal without a stored preview: fall back to the
               proposal's own recorded operations before giving up (#1397 /
               Codex review on #1414). -->
          <pre
            v-else-if="storedOperationsFallback"
            class="paper-review-deep__diff-pre"
            role="region"
            aria-label="Recorded proposal operations"
            data-testid="paper-review-diff-stored-operations"
          >{{ storedOperationsFallback }}</pre>
          <p
            v-else-if="previewDiffMode === 'stored' && !previewDiff"
            class="paper-review-deep__diff-empty tk-meta"
            data-testid="paper-review-diff-stored-empty"
          >
            No stored preview is available for this proposal.
          </p>
          <p
            v-else-if="!previewDiff"
            class="paper-review-deep__diff-empty tk-meta"
            data-testid="paper-review-diff-empty"
          >
            No changes to preview for this proposal.
          </p>
          <pre
            v-else
            class="paper-review-deep__diff-pre"
            role="region"
            :aria-label="previewDiffMode === 'stored' ? 'Stored proposal preview' : 'Proposal operation diff'"
            data-testid="paper-review-diff-pre"
          >{{ displayedPreviewDiff }}</pre>
        </div>
      </section>
      <ReviewRevisionEditor
        v-if="revisionEditing"
        :operations-payload="editablePayload"
        :saving="revisionSaving"
        @save="onSaveRevision"
        @cancel="cancelRevisionEditing"
      />
    </div>
    <div v-else class="paper-review-deep__empty" data-testid="paper-review-empty">
      <template v-if="hasFilterEmptyState">
        <div class="tk-eyebrow">Queue · {{ awaitingCount }} awaiting</div>
        <h2 class="tk-h2">No matches in {{ activeFilterLabel }}.</h2>
        <p class="tk-lede">
          Switch filters to review proposals that are still waiting elsewhere in the queue.
        </p>
      </template>
      <template v-else>
        <div class="tk-eyebrow">Queue · 0 awaiting</div>
        <h2 class="tk-h2">Nothing waiting. Good.</h2>
        <p class="tk-lede">
          When the assistant has something to propose it will appear here for review.
        </p>
        <p v-if="proposalsLoading" class="tk-meta">Loading proposals…</p>
      </template>
    </div>

    <ReviewRightRail
      v-if="activeProposal"
      :author-name="authorName"
      :author-meta="authorMeta"
      :proposed-date="proposedDate"
      :proposed-time="proposedTime"
      :proposed-num="proposedNum"
      :why-now-body="whyNowBody"
      :breakdown="selectors.confidenceBreakdown.value"
      :similar-past="selectors.similarPast.value"
      :similar-past-apply-rate="selectors.similarPastApplyRate.value"
    />
    <aside v-else class="paper-review-deep__rail-empty"></aside>
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
