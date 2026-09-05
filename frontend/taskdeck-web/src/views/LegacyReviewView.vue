<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import type { ComponentPublicInstance } from 'vue'
import { useRoute } from 'vue-router'
import WorkspaceHelpCallout from '../components/workspace/WorkspaceHelpCallout.vue'
import ReviewHeader from '../components/review/ReviewHeader.vue'
import ReviewSummaryCards from '../components/review/ReviewSummaryCards.vue'
import ReviewEmptyState from '../components/review/ReviewEmptyState.vue'
import ReviewProposalCard from '../components/review/ReviewProposalCard.vue'
import ApplyToBoardDialog from '../components/review/ApplyToBoardDialog.vue'
import RejectProposalDialog from '../components/review/RejectProposalDialog.vue'
import { TdSkeleton } from '../components/ui'
import { useReviewProposals } from '../composables/useReviewProposals'
import { useReviewActions } from '../composables/useReviewActions'
import { useVirtualList } from '../composables/useVirtualList'
import { proposalIdsEqual } from '../utils/proposalIdentity'
import { useWorkspaceStore } from '../store/workspaceStore'

const {
  proposals,
  proposalsLoading,
  boardFilterInput,
  activeBoardFilter,
  activeBoardName,
  isArchivedHistory,
  showCompleted,
  loadingBoards,
  boardOptions,
  visibleProposals,
  summaryCards,
  awaitingProposalIds,
  queueAnnouncementKey,
  queueScopeLoaded,
  queueAccessRevoked,
  queueRefreshStale,
  queueRefreshRefused,
  queueRefreshRecovered,
  queueRefreshRecoveredKind,
  unavailableProposalId,
  unavailableProposalMalformed,
  dismissableProposalIds,
  isProposalExpired,
  clearProposalDeepLink,
  loadProposals,
  loadBoardOptions,
  startClock,
  stopClock,
  startQueueRefresh,
  stopQueueRefresh,
  captureHrefForProposal,
  proposalHref,
  openRoute,
  openBoard,
  openInbox,
  applyBoardFilter,
  clearBoardFilter,
} = useReviewProposals()

const {
  proposalActionBusyId,
  selectedDiffProposalId,
  selectedDiff,
  selectedDiffMode,
  selectedDiffInvalidReason,
  selectedDiffRevised,
  executeConfirmProposal,
  rejectPromptProposal,
  rejectRequiresReason,
  handleApproveProposal,
  requestRejectProposal,
  cancelRejectProposal,
  confirmRejectProposal,
  requestExecuteProposal,
  cancelExecuteProposal,
  confirmExecuteProposal,
  handleToggleDiff,
  handleDismissProposal,
  handleDismissApplied,
} = useReviewActions(proposals, dismissableProposalIds, loadProposals, isProposalExpired)

watch(isArchivedHistory, (readOnly) => {
  if (!readOnly) return
  cancelExecuteProposal()
  cancelRejectProposal()
})

const route = useRoute()

const hashProposalId = computed(() => {
  const hash = route.hash
  if (!hash.startsWith('#proposal-')) return null
  const rawId = hash.slice('#proposal-'.length).trim()
  if (!rawId) return null
  try {
    return decodeURIComponent(rawId)
  } catch {
    return null
  }
})

// A valid proposal hash is an exact identity contract. While its target is
// hydrating or unavailable, render no other proposal's decision controls.
// Once hydrated, keep the canonical API object (and therefore its original id
// casing) as the only deep-linked card.
const renderedProposals = computed(() => {
  const proposalId = hashProposalId.value
  if (!proposalId) return visibleProposals.value
  // The exact hash is also the inspection path for a completed Applied record.
  // Completed proposals are intentionally absent from visibleProposals while
  // "Show completed" is off, so resolve the target from the hydrated canonical
  // collection and apply the board scope explicitly.
  const target = proposals.value.find((proposal) =>
    proposalIdsEqual(proposal.id, proposalId),
  )
  if (
    target &&
    activeBoardFilter.value &&
    (!target.boardId || !proposalIdsEqual(target.boardId, activeBoardFilter.value))
  ) {
    return []
  }
  return target ? [target] : []
})

/**
 * The empty state that replaces the unavailable panel when the queue behind it
 * is empty. `tabindex="-1"` in the template makes it a programmatic focus
 * target only — it never enters the tab order.
 */
const reviewEmptyStateRef = ref<ComponentPublicInstance | null>(null)

/**
 * Leave the refused deep link (#2214). Mirror of `PaperReviewView.returnToReview`
 * so the two skins cannot drift (#1124 / ADR-0038): clearing the hash is the only
 * action offered, and it is taken against the id the unavailable state names —
 * never against whatever the hash happens to hold by then.
 *
 * The click removes the panel the control lives in, so focus is moved on
 * deliberately (#2599 item 2). Without it focus falls to `<body>`: nothing is
 * announced and the reviewer's next keystroke acts on nothing. Paper's
 * settled-elsewhere notice has handed focus on this way since #2215.
 */
async function returnToReview() {
  const proposalId = unavailableProposalId.value
  if (!proposalId) return
  await clearProposalDeepLink(proposalId)
  // After the DOM has settled on whichever of the two replaces the panel.
  await nextTick()
  focusQueueAfterUnavailableReturn()
}

/**
 * The queue the panel was standing in front of, or the empty state that stands
 * in for it. The queue list is this skin's focusable queue: it carries the
 * "Proposals awaiting review" label and the Arrow cursor, which starts on the
 * first row — the same target Paper reaches as its first queue row.
 */
function focusQueueAfterUnavailableReturn() {
  if (renderedProposals.value.length > 0) {
    reviewParentRef.value?.focus?.()
    return
  }
  const emptyState = reviewEmptyStateRef.value?.$el as HTMLElement | undefined
  emptyState?.focus?.()
}

async function dismissProposalAndReconcileHash(proposalId: string) {
  await handleDismissProposal(proposalId)
  if (!proposals.value.some((proposal) => proposalIdsEqual(proposal.id, proposalId))) {
    await clearProposalDeepLink(proposalId)
  }
}

async function dismissAppliedAndReconcileHash() {
  const deepLinkedId = hashProposalId.value
  await handleDismissApplied()
  if (
    deepLinkedId &&
    !proposals.value.some((proposal) => proposalIdsEqual(proposal.id, deepLinkedId))
  ) {
    await clearProposalDeepLink(deepLinkedId)
  }
}

const _vl = useVirtualList({
  count: computed(() => renderedProposals.value.length),
  estimateSize: 220,
  overscan: 3,
})

// vue-tsc >=3.2.6 does not count ref="name" in templates as a script read;
// these refs are intentionally bound via template ref attributes.
// `reviewParentRef` needs no such directive: the focus handoff above reads it.
const reviewParentRef = _vl.parentRef
// @ts-expect-error TS6133
const reviewVirtualItemEls = _vl.virtualItemEls
const reviewVirtualRows = _vl.virtualRows
const reviewTotalSize = _vl.totalSize
const reviewTranslateY = _vl.translateY

/** Tracked keyboard cursor for ArrowUp/ArrowDown navigation. */
const activeReviewIndex = ref(0)

/**
 * Scroll the virtualizer to the proposal targeted by the URL hash.
 * This ensures the targeted proposal is rendered in the virtual window
 * before the composable's scrollToProposalFromHash tries getElementById.
 */
function scrollVirtualizerToHashProposal() {
  const proposalId = hashProposalId.value
  if (!proposalId) return
  const index = renderedProposals.value.findIndex((proposal) =>
    proposalIdsEqual(proposal.id, proposalId),
  )
  if (index >= 0) {
    _vl.scrollToIndex(index)
    activeReviewIndex.value = index
  }
}

watch(
  () => [route.hash, renderedProposals.value.length] as const,
  async () => {
    scrollVirtualizerToHashProposal()
    await nextTick()
    scrollVirtualizerToHashProposal()
  },
  { flush: 'post' },
)

function handleReviewKeydown(event: KeyboardEvent) {
  if (renderedProposals.value.length === 0) return
  if (event.key === 'ArrowDown') {
    event.preventDefault()
    const next = Math.min(activeReviewIndex.value + 1, renderedProposals.value.length - 1)
    activeReviewIndex.value = next
    _vl.scrollToIndex(next)
  } else if (event.key === 'ArrowUp') {
    event.preventDefault()
    const prev = Math.max(activeReviewIndex.value - 1, 0)
    activeReviewIndex.value = prev
    _vl.scrollToIndex(prev)
  }
}

const workspace = useWorkspaceStore()

// Most of this skin's own copy is still hardcoded English, so the awaiting-count
// announcement below is plain text, matching the hardcoded copy around it.
// The states this skin SHARES with Paper are the exception and go through the
// catalog rather than being forked per skin: the refused-deep-link panel reuses
// `review.empty.unavailable.*`, and the background-refresh disclosures reuse
// `review.queue.degraded.*` and `review.queue.refused.*` (#2214).
// The count and the identity the announcement is keyed on come from ONE
// composable predicate (#2214 item 4). Reading the number off `summaryCards`'
// `pending-review` card computed the same thing a second way and left the skin
// with no notion of WHICH proposals it stood for, which is how a count-neutral
// replacement stayed silent. The rendered value is unchanged: that card counts
// exactly `visibleProposals` in `PendingReview` and not expired.
const awaitingCount = computed(() => awaitingProposalIds.value.length)
const awaitingAnnouncement = computed(() =>
  awaitingCount.value === 1
    ? '1 proposal awaiting review.'
    : `${awaitingCount.value} proposals awaiting review.`,
)

/**
 * Whether `awaitingCount` is a real count right now (#2214, #2599 item 1). Kept
 * in step with `ReviewQueueRail.countIsAnnounceable`, which gates the Paper
 * skin's identical region on the same two states (#1124 / ADR-0038).
 *
 * The first term was `!proposalsLoading`, which asked the wrong question. An
 * explicit `loadProposals` raises that flag WITHOUT clearing `proposals`, so
 * the announcement node unmounted for the length of every reload and remounted
 * with the identical sentence: the region wrote count -> '' -> count and the
 * restore is spoken. `queueScopeLoaded` asks whether a read has landed for the
 * board on screen instead, so a same-scope reload (the header Refresh, the
 * dismiss path) is silent, while the first read, a board-filter change and a
 * failed read still withhold.
 */
const countIsAnnounceable = computed(
  () => queueScopeLoaded.value && !queueAccessRevoked.value,
)

/**
 * Same badge contract as the Paper skin (#2194 acceptance 3): the shell's
 * `Review · N` count is a home-summary workload figure AppShell reads once at
 * sign-in, and nothing here ever refreshed it. Triggered on the queue ARRAY so a
 * board-scoped view still re-reads when another board's proposal arrives.
 */
let badgeSyncArmed = false
watch(proposals, () => {
  // Skip the route-entry load; AppShell has already read that summary.
  if (!badgeSyncArmed) {
    badgeSyncArmed = true
    return
  }
  void workspace.refreshWorkloadCounts()
})

/**
 * Mirror of the Paper guard (#2194): hold a background queue tick while an
 * action is in flight or a confirm dialog is open, so the record the reviewer
 * is being asked to commit to cannot change underneath the decision. Kept in
 * step with `PaperReviewView.canRefreshQueue` so the two skins cannot drift
 * (the #1124 / ADR-0038 class).
 */
function canRefreshQueue(): boolean {
  return (
    proposalActionBusyId.value === null &&
    executeConfirmProposal.value === null &&
    rejectPromptProposal.value === null
  )
}

onMounted(() => {
  void loadBoardOptions()
  void loadProposals()
  startClock()
  startQueueRefresh(canRefreshQueue)
})

onUnmounted(() => {
  stopClock()
  stopQueueRefresh()
})
</script>

<template>
  <div class="td-review" role="region" aria-label="Proposal review">
    <ReviewHeader
      :active-board-filter="activeBoardFilter"
      :active-board-name="activeBoardName"
      :board-filter-input="boardFilterInput"
      :board-options="boardOptions"
      :loading-boards="loadingBoards"
      :show-completed="showCompleted"
      :proposals-loading="proposalsLoading"
      :dismissable-count="dismissableProposalIds.length"
      :read-only="isArchivedHistory"
      @update:board-filter-input="boardFilterInput = $event"
      @update:show-completed="showCompleted = $event"
      @select-board="(option) => applyBoardFilter(option.value)"
      @clear-board-filter="clearBoardFilter"
      @dismiss-applied="dismissAppliedAndReconcileHash"
      @refresh="loadProposals"
      @open-inbox="openInbox"
      @navigate="openRoute"
    />

    <WorkspaceHelpCallout
      topic="review"
      title="What is Review for?"
      description="Review is the approval step. Taskdeck proposes changes first, then waits for your decision before anything is applied."
    >
      <template #actions>
        <button class="td-btn td-btn--secondary td-btn--sm" @click="openInbox">Open Inbox</button>
        <button class="td-btn td-btn--secondary td-btn--sm" @click="openRoute('/workspace/boards')">Open Boards</button>
      </template>
    </WorkspaceHelpCallout>

    <ReviewSummaryCards :cards="summaryCards" />

    <!-- The queue now changes without user action (#2194); announce it politely.
         The count is only speakable when it is a real count (#2214). While the
         read is in flight it is 0 because nothing has been read yet, and once
         access is revoked it is 0 because the queue was withdrawn and cleared —
         a CHANGE, so an ungated region speaks "0 proposals awaiting review."
         beside a panel saying the queue is gone. The region withholds its
         CONTENT rather than being unmounted, because a live region inserted at
         the same moment its text appears is unreliably announced.

         The sentence sits in a node KEYED on the queue's ordered awaiting ids
         (#2214 item 4). A live region only speaks when it changes, so a poll
         that removed one pending proposal and added another rendered the same
         string and said nothing. Re-keying replaces this node inside the region
         that stays mounted, and a node addition is what `aria-live`'s default
         `aria-relevant="additions text"` announces — same sentence, same count,
         spoken once. A byte-identical queue keeps its key and stays silent.
         Paper keys the identical region on the same value (#1124 / ADR-0038). -->
    <p class="sr-only" role="status" aria-live="polite" data-testid="review-queue-live">
      <span
        v-if="countIsAnnounceable"
        :key="queueAnnouncementKey"
        data-testid="review-queue-announcement"
      >{{ awaitingAnnouncement }}</span>
    </p>

    <!-- Recovery is the half of the degraded disclosure that was missing
         (#2214). The warning below simply disappears when the queue is
         trustworthy again, which tells a reviewer who was not watching that
         corner nothing at all. Built like the count region above it: MOUNTED
         throughout, withholding its text, because a live region inserted at the
         same moment its text appears is unreliably announced (#2593). The
         signal comes from the shared composable, so both skins announce the
         same transition with the same sentence (ADR-0038 / #1124).

         TWO sentences through this one region (#2638 item 2), chosen by the
         kind the composable reports: a 'degraded' recovery follows a completed
         read and may say the rows are current, while a 'refused' one is raised
         as soon as the LIST read answers — a tick that may never replace the
         queue — so it says only that the server is accepting refreshes again.
         Paper picks the key the same way. -->
    <p
      class="sr-only"
      role="status"
      aria-live="polite"
      aria-atomic="true"
      data-testid="review-queue-recovered"
    >{{
      queueRefreshRecovered && !queueAccessRevoked
        ? $t(
            queueRefreshRecoveredKind === 'refused'
              ? 'review.queue.refused.recovered'
              : 'review.queue.degraded.recovered',
          )
        : ''
    }}</p>

    <!-- The refused-refresh disclosure (#2214 item 2). Same construction and
         the same reason as the two regions above: the visible warning below
         mounts at the same moment it gains its text, which is the case a live
         region announces unreliably. This one is always mounted and withholds
         its text until the disclosure rises. -->
    <p
      class="sr-only"
      role="status"
      aria-live="polite"
      aria-atomic="true"
      data-testid="review-queue-refused"
    >{{ queueRefreshRefused && !queueAccessRevoked ? $t('review.queue.refused.body') : '' }}</p>

    <!-- ONE visible slot for both background-refresh disclosures, because they
         are alternatives rather than additions: "refreshes are being refused"
         subsumes "the queue may be out of date", and rendering both would put
         "while Taskdeck retries" next to a sentence saying the retries are
         being refused. The refusal wins when both stand — it is the stronger
         statement and the only one with something for the reviewer to do. -->
    <div
      v-if="(queueRefreshStale || queueRefreshRefused) && !queueAccessRevoked"
      class="td-panel"
      role="status"
      aria-live="polite"
      aria-atomic="true"
      data-testid="review-queue-stale"
    >
      <p>{{ queueRefreshRefused ? $t('review.queue.refused.body') : $t('review.queue.degraded.body') }}</p>
    </div>

    <div
      v-if="queueAccessRevoked"
      class="td-panel"
      role="status"
      data-testid="review-access-revoked"
    >
      <p>This review queue is no longer available to you.</p>
      <p>
        Your access to these boards changed, so the queue was cleared and has stopped
        updating. Reload or pick a board you can still reach.
      </p>
    </div>

    <div v-else-if="proposalsLoading" class="td-review__skeleton" aria-live="polite" role="status">
      <span class="sr-only">Loading proposals to review...</span>
      <div v-for="n in 3" :key="n" class="td-panel td-review__skeleton-card">
        <div class="td-review__skeleton-header">
          <TdSkeleton width="60px" height="20px" />
          <TdSkeleton width="200px" height="16px" />
          <TdSkeleton width="80px" height="14px" />
        </div>
        <TdSkeleton width="90%" height="14px" />
        <TdSkeleton width="70%" height="14px" />
        <div class="td-review__skeleton-actions">
          <TdSkeleton width="90px" height="32px" />
          <TdSkeleton width="80px" height="32px" />
          <TdSkeleton width="70px" height="32px" />
        </div>
      </div>
    </div>

    <!-- A hash-pinned proposal the server refused or could not bind (400/403/404)
         is an identity failure of the link the reviewer followed, not an empty
         queue (#2214).
         Saying so — and offering the way back — is the whole difference between
         "your link is dead" and "there is nothing to review". Ordered before
         the empty state, and still requiring an empty render, so a pin that has
         already resolved shows its proposal instead. -->
    <div
      v-else-if="unavailableProposalId && renderedProposals.length === 0"
      class="td-panel"
      role="status"
      data-testid="review-unavailable-target"
    >
      <p>{{ $t('review.empty.unavailable.eyebrow') }}</p>
      <!-- A 400 means the by-id route could not bind the id, so the link never
           named a proposal: it cannot be retried and it will not come back.
           Saying "it may have been applied, archived, or removed" there sends
           the reviewer to wait for a recovery that cannot arrive (#2214). -->
      <p>{{ unavailableProposalMalformed ? $t('review.empty.unavailable.malformedTitle') : $t('review.empty.unavailable.title') }}</p>
      <p>{{ unavailableProposalMalformed ? $t('review.empty.unavailable.malformedBody', { id: unavailableProposalId }) : $t('review.empty.unavailable.body', { id: unavailableProposalId }) }}</p>
      <button
        type="button"
        class="td-btn td-btn--secondary td-btn--sm"
        data-testid="review-unavailable-return"
        @click="returnToReview"
      >
        {{ $t('review.empty.unavailable.return') }}
      </button>
    </div>

    <!-- `tabindex="-1"` makes this a programmatic focus target and nothing
         else: leaving the unavailable pin with an empty queue hands focus here
         (#2599 item 2), and it never enters the tab order. -->
    <ReviewEmptyState
      v-else-if="renderedProposals.length === 0"
      ref="reviewEmptyStateRef"
      tabindex="-1"
      @open-inbox="openInbox"
      @navigate="openRoute"
    />

    <!-- eslint-disable-next-line vuejs-accessibility/no-static-element-interactions -- scrollable virtual list with keyboard handler -->
    <section
      v-else
      ref="reviewParentRef"
      class="td-review__list td-review__list--virtual"
      aria-label="Proposals awaiting review"
      tabindex="0"
      @keydown="handleReviewKeydown"
    >
      <div
        role="presentation"
        class="td-virtual-scroll-sizer"
        :style="{ '--td-virtual-size': `${reviewTotalSize}px` }"
      >
        <div
          role="presentation"
          class="td-virtual-scroll-offset"
          :style="{ '--td-virtual-offset': `${reviewTranslateY}px` }"
        >
          <div
            v-for="virtualRow in reviewVirtualRows"
            :key="renderedProposals[virtualRow.index]?.id ?? String(virtualRow.key)"
            :data-index="virtualRow.index"
            ref="reviewVirtualItemEls"
            role="presentation"
          >
            <ReviewProposalCard
              v-if="renderedProposals[virtualRow.index]"
              :proposal="renderedProposals[virtualRow.index]!"
              :is-expired="isProposalExpired(renderedProposals[virtualRow.index]!)"
              :is-busy="proposalIdsEqual(proposalActionBusyId, renderedProposals[virtualRow.index]!.id)"
              :selected-diff-proposal-id="selectedDiffProposalId"
              :selected-diff="selectedDiff"
              :selected-diff-mode="selectedDiffMode"
              :selected-diff-invalid-reason="selectedDiffInvalidReason"
              :selected-diff-revised="selectedDiffRevised"
              :capture-href="captureHrefForProposal(renderedProposals[virtualRow.index]!)"
              :proposal-href="proposalHref(renderedProposals[virtualRow.index]!)"
              :read-only="isArchivedHistory"
              @approve="handleApproveProposal"
              @reject="requestRejectProposal"
              @execute="requestExecuteProposal"
              @toggle-diff="handleToggleDiff"
              @dismiss="dismissProposalAndReconcileHash"
              @open-board="openBoard"
            />
          </div>
        </div>
      </div>
    </section>

    <!-- Phase-2 confirmation (#1818): the app dialog idiom, carrying the proposal
         summary, in place of the native confirm(). -->
    <ApplyToBoardDialog
      :proposal="executeConfirmProposal"
      :busy="proposalActionBusyId !== null"
      @confirm="confirmExecuteProposal"
      @cancel="cancelExecuteProposal"
    />

    <!-- Reason collection (GH-1969): the app dialog idiom in place of the native
         window.prompt. The gate lives in the shared composable, so both review
         surfaces collect the reason the same way. -->
    <RejectProposalDialog
      :proposal="rejectPromptProposal"
      :busy="proposalActionBusyId !== null"
      :requires-reason="rejectRequiresReason"
      @confirm="confirmRejectProposal"
      @cancel="cancelRejectProposal"
    />
  </div>
</template>

<style scoped>
.td-review {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-4);
}

.td-review__skeleton {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
}

.td-review__skeleton-card {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
}

.td-review__skeleton-header {
  display: flex;
  align-items: center;
  gap: var(--td-space-3);
}

.td-review__skeleton-actions {
  display: flex;
  gap: var(--td-space-2);
  margin-top: var(--td-space-2);
}

.td-review__list {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
}

.td-review__list--virtual {
  max-height: 80vh;
  overflow-y: auto;
  contain: layout paint;
  outline: none;
}

.td-review__list--virtual:focus-visible {
  box-shadow: inset 0 0 0 2px rgba(255, 77, 77, 0.35);
}

@media (max-width: 640px) {
  .td-review {
    gap: var(--td-space-3);
  }
}
</style>
