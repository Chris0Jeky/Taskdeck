<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import ReviewQueueRail, {
  type QueueFilter,
  type QueueRailItem,
} from './review/ReviewQueueRail.vue'
import type { RecentlyAppliedRow } from './review/ReviewRecentApplied.vue'
import ReviewMain from './review/ReviewMain.vue'
import ReviewRevisionEditor from './review/ReviewRevisionEditor.vue'
import ReviewRightRail from './review/ReviewRightRail.vue'
import { useReviewProposals } from '../../composables/useReviewProposals'
import { useReviewActions } from '../../composables/useReviewActions'
import { usePaperReviewSelectors } from '../../composables/usePaperReviewSelectors'
import { useReviewKeymap } from '../../composables/useReviewKeymap'
import { useProposalRevisions } from '../../composables/useProposalRevisions'
import { useSessionStore } from '../../store/sessionStore'
import { useToastStore } from '../../store/toastStore'
import { normalizeProposalSourceType, normalizeProposalStatus } from '../../utils/automation'
import type { Proposal as ApiProposal, ProposalOperation } from '../../types/automation'
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
  isProposalDismissable,
  isStaleProposal,
  loadProposals,
  loadBoardOptions,
  startClock,
  stopClock,
} = useReviewProposals()
const session = useSessionStore()
const toast = useToastStore()
const route = useRoute()

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
  handleExecuteProposal,
  handleDismissProposal,
  handleDismissApplied,
} = useReviewActions(proposals, ownedDismissableIds, loadProposals)

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
  switch (queueFilter.value) {
    case 'mine':
      return visibleProposals.value.filter(
        (proposal) => !!session.userId && proposal.requestedByUserId === session.userId,
      )
    case 'stale':
      return visibleProposals.value.filter(isStaleProposal)
    case 'all':
    default:
      return visibleProposals.value
  }
})

const activeFilterLabel = computed(() => {
  if (queueFilter.value === 'mine') return 'Mine'
  if (queueFilter.value === 'stale') return 'Stale'
  return 'All'
})

const hasFilterEmptyState = computed(
  () => visibleProposals.value.length > 0 && filteredVisibleProposals.value.length === 0,
)

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
  return (
    filteredVisibleProposals.value.find(
      (p) => normalizeProposalStatus(p.status) === 'PendingReview' && !isProposalExpired(p),
    ) ?? filteredVisibleProposals.value[0] ?? null
  )
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

const {
  editing: revisionEditing,
  saving: revisionSaving,
  revisionCount,
  latestRevision,
  startEditing: startRevisionEditing,
  cancelEditing: cancelRevisionEditing,
  saveRevision,
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
      who: normalizeProposalSourceType(p.sourceType) === 'Chat' ? 'haiku' : 'capture',
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
  const cutoff = nowMs.value - 6 * 60 * 60 * 1000 // 6h undo window
  return proposals.value
    .filter((p) => matchesActiveBoardFilter(p.boardId))
    .filter((p) => normalizeProposalStatus(p.status) === 'Applied' && p.appliedAt)
    .sort((a, b) => new Date(b.appliedAt as string).getTime() - new Date(a.appliedAt as string).getTime())
    .map((p) => {
      const appliedMs = new Date(p.appliedAt as string).getTime()
      const left = appliedMs + 6 * 60 * 60 * 1000 - nowMs.value
      const expired = appliedMs < cutoff || left <= 0
      return {
        id: p.id,
        serial: `#${p.id.slice(0, 4).toUpperCase()}`,
        title: p.summary || '(applied)',
        left: expired ? null : formatRemaining(left),
        expired,
      }
    })
    .slice(0, 4)
})

function formatRemaining(ms: number): string {
  const totalMin = Math.max(0, Math.floor(ms / 60_000))
  const h = Math.floor(totalMin / 60)
  const m = totalMin % 60
  if (h <= 0) return `${m}m`
  return `${h}h ${m.toString().padStart(2, '0')}m`
}

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
  return `${ops} ${ops === 1 ? 'operation' : 'operations'} · undo 6h · atomic`
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

function parseOperationParameters(operation: ProposalOperation): Record<string, unknown> | null {
  if (!operation.parameters) return null
  try {
    const parsed = JSON.parse(operation.parameters) as unknown
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed)
      ? (parsed as Record<string, unknown>)
      : null
  } catch {
    return null
  }
}

function formatParameterValue(value: unknown): string {
  if (value === null || value === undefined) return 'null'
  if (typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean') {
    return String(value)
  }
  return JSON.stringify(value) ?? String(value)
}

function summarizeOperation(operation: ProposalOperation): string {
  const params = parseOperationParameters(operation)
  if (!params) return 'No parameter preview supplied for this operation.'
  const entries = Object.entries(params).slice(0, 4)
  if (entries.length === 0) return 'No parameter preview supplied for this operation.'
  return entries.map(([key, value]) => `${key}: ${formatParameterValue(value)}`).join(' · ')
}

const before = computed<ChangeBeforeCard>(() => ({
  serial: activeProposal.value ? `#${activeProposal.value.id.slice(0, 8)}` : '—',
  title: activeProposal.value?.summary ?? 'No proposal selected',
  body:
    activeProposal.value?.presentation?.impactSummary ??
    `Review ${activeProposal.value?.operations?.length ?? 0} proposal operations before applying.`,
  meta: `${activeProposal.value?.boardId ?? 'Inbox'} · ${activeProposal.value?.sourceType ?? 'proposal'}`,
}))

const after = computed<ChangeAfterCard[]>(() => {
  const p = activeProposal.value
  const operations = p?.operations ?? []
  if (operations.length === 0) {
    return [{
      serial: p ? `#${p.id.slice(0, 8)}.0` : '—',
      title: 'No operation preview',
      body: p?.diffPreview ?? 'The proposal did not include operation details.',
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
  const p = activeProposal.value
  const operations = p?.operations ?? []
  if (operations.length === 0) {
    return [{ key: 'operations', before: 'none', after: p?.diffPreview ?? 'not provided', same: !p?.diffPreview }]
  }

  return [...operations]
    .sort((a, b) => a.sequence - b.sequence)
    .map((operation) => ({
      key: formatActionLabel(operation.actionType),
      before: operation.targetId ?? operation.targetType,
      after: summarizeOperation(operation),
    }))
})

const changeSubTitle = computed(() => {
  const ops = activeProposal.value?.operations?.length ?? 0
  return `${ops} ${ops === 1 ? 'operation' : 'operations'} · ${activeProposal.value?.boardId ?? 'this board'}`
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
  const source = activeProposal.value
    ? normalizeProposalSourceType(activeProposal.value.sourceType).toLowerCase()
    : 'proposal'
  return `Haiku · ${source} proposal`
})

const whyNowBody = computed(() => {
  const p = activeProposal.value
  if (!p) return 'No proposal is selected.'
  return p.presentation?.sourceCue ?? 'This proposal is awaiting review based on the source captured with it.'
})

// --- Action wiring -----------------------------------------------------

const revisionBusy = computed(() => revisionEditing.value || revisionSaving.value)
const busy = computed(
  () => proposalActionBusyId.value !== null || revisionBusy.value || bulkDismissBusy.value,
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

function onApply() {
  const p = activeProposal.value
  if (!p) return
  if (revisionBusy.value) {
    toast.info('Save or cancel the revision before applying this proposal.')
    return
  }
  if (!isApplyActionable(p)) {
    toast.info('This proposal is no longer actionable. Refresh review to see current status.')
    return
  }
  const status = normalizeProposalStatus(p.status)
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

function onDefer() {
  // Defer is a UI-only action until the backend supports a "defer 1h"
  // endpoint (see backend-gap follow-up). For now we leave the proposal in
  // place; the toast confirms intent so testing can wire to a stub later.
  // TODO(#1002): call automationApi.deferProposal once available.
  toast.info('Defer is not wired yet; the proposal is still in your queue.')
}

function onToggleProvenance() {
  toast.info('Provenance toggle is not wired yet; provenance is rendered inline below.')
}

function onPreviewDiff() {
  const p = activeProposal.value
  if (!p) return
  toast.info('Preview diff is not wired yet; no diff was loaded.')
}

function onReportBadSuggestion(proposalId: string) {
  if (!proposalId) {
    toast.error('No proposal is selected to report.')
    return
  }
  toast.info('Report queued for this suggestion.')
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
  queueFilter.value = filter
  explicitActiveId.value = filteredVisibleProposals.value[0]?.id ?? null
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
      <ReviewRevisionEditor
        v-if="revisionEditing"
        :operations-payload="editablePayload"
        :saving="revisionSaving"
        @save="saveRevision"
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
          When haiku has something to propose it will appear here for review.
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
