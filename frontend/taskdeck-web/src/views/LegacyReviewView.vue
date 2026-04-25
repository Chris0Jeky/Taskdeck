<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import WorkspaceHelpCallout from '../components/workspace/WorkspaceHelpCallout.vue'
import ReviewHeader from '../components/review/ReviewHeader.vue'
import ReviewSummaryCards from '../components/review/ReviewSummaryCards.vue'
import ReviewEmptyState from '../components/review/ReviewEmptyState.vue'
import ReviewProposalCard from '../components/review/ReviewProposalCard.vue'
import { TdSkeleton } from '../components/ui'
import { useReviewProposals } from '../composables/useReviewProposals'
import { useReviewActions } from '../composables/useReviewActions'
import { useVirtualList } from '../composables/useVirtualList'

const {
  proposals,
  proposalsLoading,
  boardFilterInput,
  activeBoardFilter,
  activeBoardName,
  showCompleted,
  loadingBoards,
  boardOptions,
  visibleProposals,
  summaryCards,
  dismissableProposalIds,
  isProposalExpired,
  loadProposals,
  loadBoardOptions,
  startClock,
  stopClock,
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
  handleApproveProposal,
  handleRejectProposal,
  handleExecuteProposal,
  handleToggleDiff,
  handleDismissProposal,
  handleDismissApplied,
} = useReviewActions(proposals, dismissableProposalIds, loadProposals)

const _vl = useVirtualList({
  count: computed(() => visibleProposals.value.length),
  estimateSize: 220,
  overscan: 3,
})

// vue-tsc >=3.2.6 does not count ref="name" in templates as a script read;
// these refs are intentionally bound via template ref attributes.
// @ts-expect-error TS6133
const reviewParentRef = _vl.parentRef
// @ts-expect-error TS6133
const reviewVirtualItemEls = _vl.virtualItemEls
const reviewVirtualRows = _vl.virtualRows
const reviewTotalSize = _vl.totalSize
const reviewTranslateY = _vl.translateY

/** Tracked keyboard cursor for ArrowUp/ArrowDown navigation. */
const activeReviewIndex = ref(0)

const route = useRoute()

/**
 * Scroll the virtualizer to the proposal targeted by the URL hash.
 * This ensures the targeted proposal is rendered in the virtual window
 * before the composable's scrollToProposalFromHash tries getElementById.
 */
function scrollVirtualizerToHashProposal() {
  const hash = route.hash
  if (!hash.startsWith('#proposal-')) return
  const rawId = hash.slice('#proposal-'.length).trim()
  if (!rawId) return
  let proposalId: string
  try {
    proposalId = decodeURIComponent(rawId)
  } catch {
    return
  }
  const index = visibleProposals.value.findIndex((p) => p.id === proposalId)
  if (index >= 0) {
    _vl.scrollToIndex(index)
    activeReviewIndex.value = index
  }
}

watch(
  () => [route.hash, visibleProposals.value.length] as const,
  async () => {
    scrollVirtualizerToHashProposal()
    await nextTick()
    scrollVirtualizerToHashProposal()
  },
  { flush: 'post' },
)

function handleReviewKeydown(event: KeyboardEvent) {
  if (visibleProposals.value.length === 0) return
  if (event.key === 'ArrowDown') {
    event.preventDefault()
    const next = Math.min(activeReviewIndex.value + 1, visibleProposals.value.length - 1)
    activeReviewIndex.value = next
    _vl.scrollToIndex(next)
  } else if (event.key === 'ArrowUp') {
    event.preventDefault()
    const prev = Math.max(activeReviewIndex.value - 1, 0)
    activeReviewIndex.value = prev
    _vl.scrollToIndex(prev)
  }
}

onMounted(() => {
  void loadBoardOptions()
  void loadProposals()
  startClock()
})

onUnmounted(() => {
  stopClock()
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
      @update:board-filter-input="boardFilterInput = $event"
      @update:show-completed="showCompleted = $event"
      @select-board="(option) => applyBoardFilter(option.value)"
      @clear-board-filter="clearBoardFilter"
      @dismiss-applied="handleDismissApplied"
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

    <div v-if="proposalsLoading" class="td-review__skeleton" aria-live="polite" role="status">
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

    <ReviewEmptyState
      v-else-if="visibleProposals.length === 0"
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
            :key="visibleProposals[virtualRow.index]?.id ?? String(virtualRow.key)"
            :data-index="virtualRow.index"
            ref="reviewVirtualItemEls"
            role="presentation"
          >
            <ReviewProposalCard
              v-if="visibleProposals[virtualRow.index]"
              :proposal="visibleProposals[virtualRow.index]!"
              :is-expired="isProposalExpired(visibleProposals[virtualRow.index]!)"
              :is-busy="proposalActionBusyId === visibleProposals[virtualRow.index]!.id"
              :selected-diff-proposal-id="selectedDiffProposalId"
              :selected-diff="selectedDiff"
              :capture-href="captureHrefForProposal(visibleProposals[virtualRow.index]!)"
              :proposal-href="proposalHref(visibleProposals[virtualRow.index]!)"
              @approve="handleApproveProposal"
              @reject="handleRejectProposal"
              @execute="handleExecuteProposal"
              @toggle-diff="handleToggleDiff"
              @dismiss="handleDismissProposal"
              @open-board="openBoard"
            />
          </div>
        </div>
      </div>
    </section>
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
