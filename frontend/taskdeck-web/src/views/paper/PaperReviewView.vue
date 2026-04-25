<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import ReviewQueueRail, {
  type QueueRailItem,
} from './review/ReviewQueueRail.vue'
import type { RecentlyAppliedRow } from './review/ReviewRecentApplied.vue'
import ReviewMain from './review/ReviewMain.vue'
import ReviewRightRail from './review/ReviewRightRail.vue'
import { useReviewProposals } from '../../composables/useReviewProposals'
import { useReviewActions } from '../../composables/useReviewActions'
import { usePaperReviewSelectors } from '../../composables/usePaperReviewSelectors'
import { useReviewKeymap } from '../../composables/useReviewKeymap'
import { useSessionStore } from '../../store/sessionStore'
import { useToastStore } from '../../store/toastStore'
import { normalizeProposalStatus } from '../../utils/automation'
import type { Proposal as ApiProposal } from '../../types/automation'
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
  loadProposals,
  loadBoardOptions,
  startClock,
  stopClock,
} = useReviewProposals()
const session = useSessionStore()
const toast = useToastStore()

const {
  proposalActionBusyId,
  handleApproveProposal,
  handleRejectProposal,
  handleExecuteProposal,
  handleToggleDiff,
} = useReviewActions(proposals, dismissableProposalIds, loadProposals)

// --- Active proposal ---------------------------------------------------

const explicitActiveId = ref<string | null>(null)

const activeProposal = computed<ApiProposal | null>(() => {
  if (explicitActiveId.value) {
    const found = visibleProposals.value.find((p) => p.id === explicitActiveId.value)
    if (found) return found
  }
  // Default to the first pending-review item in the queue.
  return (
    visibleProposals.value.find(
      (p) => normalizeProposalStatus(p.status) === 'PendingReview' && !isProposalExpired(p),
    ) ?? visibleProposals.value[0] ?? null
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

const selectors = usePaperReviewSelectors(activeProposal)

// --- Queue rail data ---------------------------------------------------

const awaitingCount = computed(() => {
  return visibleProposals.value.filter(
    (p) =>
      normalizeProposalStatus(p.status) === 'PendingReview' && !isProposalExpired(p),
  ).length
})

const staleCount = computed(() => {
  // A proposal is "stale" when older than 24h and still pending review.
  const cutoff = nowMs.value - 24 * 60 * 60 * 1000
  return visibleProposals.value.filter((p) => {
    if (normalizeProposalStatus(p.status) !== 'PendingReview') return false
    return new Date(p.createdAt).getTime() < cutoff
  }).length
})

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
  visibleProposals.value.map((p) => {
    const ageMs = nowMs.value - new Date(p.createdAt).getTime()
    const stale =
      normalizeProposalStatus(p.status) === 'PendingReview' && ageMs >= 24 * 60 * 60 * 1000
    return {
      id: p.id,
      serial: `#${p.id.slice(0, 4).toUpperCase()}`,
      title: p.summary || '(no summary)',
      who: p.sourceType === 'Chat' ? 'haiku' : 'capture',
      // Confidence is not yet on the wire — leave null until the gap lands.
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
    .map((p) => {
      const appliedMs = new Date(p.appliedAt as string).getTime()
      const left = appliedMs + 6 * 60 * 60 * 1000 - nowMs.value
      const expired = appliedMs < cutoff || left <= 0
      return {
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

// Demo "before/after" cards remain feature-flagged via the selectors module.
// TODO(#1002): replace with backend-supplied field-level diffs.
const before = computed<ChangeBeforeCard>(() => ({
  serial: 'C-090',
  title: 'Implement dark mode',
  body: 'Apply Paper-at-Night tokens across all surfaces. Three-way variable swap with QA pass on every screen.',
  meta: '· theme · 0/0 subtasks · 1d in column',
}))

const after = computed<ChangeAfterCard[]>(() => [
  {
    serial: 'C-090',
    title: 'Tokens · darken & QA',
    body: 'Migrate the token sheet; verify contrast at AA on every surface.',
    status: 'kept',
  },
  {
    serial: 'C-090a',
    title: 'Components · mode switch',
    body: 'All components use semantic vars; ship a `data-theme` toggle with sticky preference.',
    status: 'new',
  },
  {
    serial: 'C-090b',
    title: 'Hand-off · screenshots & PR',
    body: 'Capture every surface in both modes. PR with QA evidence and reviewer checklist.',
    status: 'new',
  },
])

const fields = computed<FieldDiff[]>(() => [
  { key: 'title', before: 'Implement dark mode', after: 'Tokens · darken & QA · Components · mode switch · Hand-off' },
  { key: 'subtasks', before: '0/0', after: '2/4 · 1/3 · 0/2 (3 + 3 + 2 = 8 total)' },
  { key: 'labels', before: 'theme', after: 'theme · ui (added on hand-off card only)' },
  { key: 'due', before: '—', after: 'kept blank · respects backlog convention' },
  { key: 'assignee', before: 'Daniel L.', after: 'Daniel L. (preserved across all 3)', same: true },
])

const changeSubTitle = computed(() => {
  const ops = activeProposal.value?.operations?.length ?? 0
  return `${ops || 3} changes · ${activeProposal.value?.boardId ?? 'this board'}`
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
  const c = selectors.confidenceBreakdown.value
  return `${c.overall.toFixed(2)} confidence · 4s · 1.2k tokens`
})

const whyNowBody =
  'Haiku noticed this card has accumulated several distinct workstreams in its body and crossed your "split this" threshold (Settings → Heuristics).'

// --- Action wiring -----------------------------------------------------

const busy = computed(() => proposalActionBusyId.value !== null)

function isDecisionActionable(proposal: ApiProposal): boolean {
  const status = normalizeProposalStatus(proposal.status)
  return (status === 'PendingReview' || status === 'Approved') && !isProposalExpired(proposal)
}

function onApply() {
  const p = activeProposal.value
  if (!p) return
  if (!isDecisionActionable(p)) {
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
  if (!isDecisionActionable(p)) {
    toast.info('This proposal is no longer actionable. Refresh review to see current status.')
    return
  }
  void handleRejectProposal(p.id, p.riskLevel)
}

function onRequestEdit() {
  const p = activeProposal.value
  if (!p) return
  void handleToggleDiff(p.id)
}

function onDefer() {
  // Defer is a UI-only action until the backend supports a "defer 1h"
  // endpoint (see backend-gap follow-up). For now we leave the proposal in
  // place; the toast confirms intent so testing can wire to a stub later.
  // TODO(#1002): call automationApi.deferProposal once available.
  toast.info('Defer is not wired yet; the proposal is still in your queue.')
}

function onToggleProvenance() {
  // Provenance is rendered inline in the main column. This handler is
  // wired so the keymap test can verify the binding fires; once a
  // collapsible mode lands we will toggle a `ref<boolean>`.
}

function onPreviewDiff() {
  const p = activeProposal.value
  if (!p) return
  void handleToggleDiff(p.id)
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
</script>

<template>
  <div class="paper paper-review-deep" data-testid="paper-review-view">
    <ReviewQueueRail
      :items="queueItems"
      :active-id="activeProposal?.id ?? null"
      :awaiting-count="awaitingCount"
      :stale-count="staleCount"
      :recently-applied="recentlyApplied"
      @select="selectProposal"
    />

    <ReviewMain
      v-if="activeProposal"
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
      :side-effects="selectors.sideEffects.value"
      :conflicts="selectors.conflicts.value"
      :history="selectors.history.value"
      @apply="onApply"
      @reject="onReject"
      @request-edit="onRequestEdit"
      @defer="onDefer"
    />
    <div v-else class="paper-review-deep__empty" data-testid="paper-review-empty">
      <div class="tk-eyebrow">Queue · 0 awaiting</div>
      <h2 class="tk-h2">Nothing waiting. Good.</h2>
      <p class="tk-lede">
        When haiku has something to propose it will appear here for review.
      </p>
      <p v-if="proposalsLoading" class="tk-meta">Loading proposals…</p>
    </div>

    <ReviewRightRail
      v-if="activeProposal"
      :author-name="'Haiku · local'"
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
