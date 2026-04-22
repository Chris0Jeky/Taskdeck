<script setup lang="ts">
import { onMounted, onUnmounted } from 'vue'
import WorkspaceHelpCallout from '../components/workspace/WorkspaceHelpCallout.vue'
import ReviewHeader from '../components/review/ReviewHeader.vue'
import ReviewSummaryCards from '../components/review/ReviewSummaryCards.vue'
import ReviewEmptyState from '../components/review/ReviewEmptyState.vue'
import ReviewProposalCard from '../components/review/ReviewProposalCard.vue'
import { useReviewProposals } from '../composables/useReviewProposals'
import { useReviewActions } from '../composables/useReviewActions'

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

    <div v-if="proposalsLoading" class="td-panel td-review__loading" aria-live="polite">
      Loading proposals to review...
    </div>

    <ReviewEmptyState
      v-else-if="visibleProposals.length === 0"
      @open-inbox="openInbox"
      @navigate="openRoute"
    />

    <section v-else class="td-review__list" aria-label="Proposals awaiting review">
      <ReviewProposalCard
        v-for="proposal in visibleProposals"
        :key="proposal.id"
        :proposal="proposal"
        :is-expired="isProposalExpired(proposal)"
        :is-busy="proposalActionBusyId === proposal.id"
        :selected-diff-proposal-id="selectedDiffProposalId"
        :selected-diff="selectedDiff"
        :capture-href="captureHrefForProposal(proposal)"
        :proposal-href="proposalHref(proposal)"
        @approve="handleApproveProposal"
        @reject="handleRejectProposal"
        @execute="handleExecuteProposal"
        @toggle-diff="handleToggleDiff"
        @dismiss="handleDismissProposal"
        @open-board="openBoard"
      />
    </section>
  </div>
</template>

<style scoped>
.td-review {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-4);
}

.td-review__loading {
  color: var(--td-text-secondary);
}

.td-review__list {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
}

@media (max-width: 640px) {
  .td-review {
    gap: var(--td-space-3);
  }
}
</style>
