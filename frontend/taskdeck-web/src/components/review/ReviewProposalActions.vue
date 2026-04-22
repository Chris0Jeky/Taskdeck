<script setup lang="ts">
import type { Proposal } from '../../types/automation'
import { normalizeProposalStatus } from '../../utils/automation'

defineProps<{
  proposal: Proposal
  isExpired: boolean
  isBusy: boolean
  selectedDiffProposalId: string | null
}>()

defineEmits<{
  (e: 'approve', proposalId: string): void
  (e: 'reject', proposalId: string, riskLevel: Proposal['riskLevel']): void
  (e: 'execute', proposalId: string): void
  (e: 'toggle-diff', proposalId: string): void
  (e: 'dismiss', proposalId: string): void
}>()
</script>

<template>
  <div class="td-review-card__actions">
    <!-- Expired proposal: show dismiss action instead of approve/reject/apply -->
    <template v-if="isExpired">
      <span class="td-review-card__expired-notice" role="status">
        This proposal has expired and can no longer be applied.
      </span>
      <button class="td-btn td-btn--secondary td-btn--sm" @click="$emit('toggle-diff', proposal.id)">
        {{ selectedDiffProposalId === proposal.id ? 'Hide Diff' : 'View Diff' }}
      </button>
      <button
        class="td-btn td-btn--secondary td-btn--sm"
        :disabled="isBusy"
        @click="$emit('dismiss', proposal.id)"
      >
        Dismiss
      </button>
    </template>

    <!-- Active proposal: show normal two-step flow -->
    <template v-else>
      <!-- Two-step flow indicator -->
      <div
        v-if="normalizeProposalStatus(proposal.status) === 'PendingReview'"
        class="td-review-card__flow-steps"
        role="list"
        aria-label="Two-step approval flow"
      >
        <span class="td-review-card__flow-step td-review-card__flow-step--active" role="listitem" aria-current="step">
          <span class="td-review-card__flow-step-num" aria-hidden="true">1</span>
          Approve
        </span>
        <span class="td-review-card__flow-arrow" aria-hidden="true">&#8594;</span>
        <span class="td-review-card__flow-step td-review-card__flow-step--pending" role="listitem">
          <span class="td-review-card__flow-step-num" aria-hidden="true">2</span>
          Apply to board
        </span>
      </div>
      <div
        v-else-if="normalizeProposalStatus(proposal.status) === 'Approved'"
        class="td-review-card__flow-steps"
        role="list"
        aria-label="Ready to apply"
      >
        <span class="td-review-card__flow-step td-review-card__flow-step--done" role="listitem">
          <span class="td-review-card__flow-step-num" aria-hidden="true">1</span>
          Approved
        </span>
        <span class="td-review-card__flow-arrow" aria-hidden="true">&#8594;</span>
        <span class="td-review-card__flow-step td-review-card__flow-step--active" role="listitem" aria-current="step">
          <span class="td-review-card__flow-step-num" aria-hidden="true">2</span>
          Apply to board
        </span>
      </div>

      <button class="td-btn td-btn--secondary td-btn--sm" @click="$emit('toggle-diff', proposal.id)">
        {{ selectedDiffProposalId === proposal.id ? 'Hide Diff' : 'View Diff' }}
      </button>
      <button
        class="td-btn td-btn--primary td-btn--sm"
        :disabled="isBusy || normalizeProposalStatus(proposal.status) !== 'PendingReview'"
        @click="$emit('approve', proposal.id)"
      >
        Approve for board
      </button>
      <button
        class="td-btn td-btn--danger td-btn--sm"
        :disabled="isBusy || normalizeProposalStatus(proposal.status) !== 'PendingReview'"
        @click="$emit('reject', proposal.id, proposal.riskLevel)"
      >
        Reject
      </button>
      <button
        class="td-btn td-btn--secondary td-btn--sm"
        :disabled="isBusy || normalizeProposalStatus(proposal.status) !== 'Approved'"
        @click="$emit('execute', proposal.id)"
      >
        Apply to board
      </button>
    </template>
  </div>
</template>

<style scoped>
.td-review-card__actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--td-space-2);
  align-items: center;
  position: sticky;
  bottom: 0;
  z-index: 2;
  background: var(--td-surface-container-low);
  padding-block: var(--td-space-2);
  margin-inline: calc(-1 * var(--td-space-5));
  padding-inline: var(--td-space-5);
  border-top: 1px solid var(--td-border-ghost);
}

.td-review-card__expired-notice {
  font-size: var(--td-font-xs);
  font-weight: 600;
  color: var(--td-color-warning);
  margin-inline-end: var(--td-space-2);
}

/* Two-step flow indicator */
.td-review-card__flow-steps {
  display: flex;
  align-items: center;
  gap: var(--td-space-2);
  margin-inline-end: var(--td-space-3);
  flex-shrink: 0;
}

.td-review-card__flow-arrow {
  color: var(--td-text-secondary);
  font-size: var(--td-font-sm);
}

.td-review-card__flow-step {
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
  font-size: var(--td-font-xs);
  font-weight: 600;
  padding: 0.1875rem 0.5rem;
  border-radius: var(--td-radius-pill, 999px);
  border: 1px solid transparent;
}

.td-review-card__flow-step-num {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 1.125rem;
  height: 1.125rem;
  border-radius: 50%;
  font-size: 0.6875rem;
  font-weight: 700;
  flex-shrink: 0;
}

.td-review-card__flow-step--active {
  color: var(--td-color-primary);
  background: var(--td-color-primary-light, rgba(var(--td-color-primary-rgb, 99 102 241) / 0.1));
  border-color: var(--td-color-primary);
}

.td-review-card__flow-step--active .td-review-card__flow-step-num {
  background: var(--td-color-primary);
  color: #fff;
}

.td-review-card__flow-step--pending {
  color: var(--td-text-secondary);
  background: var(--td-surface-container-high);
  border-color: var(--td-border-default);
}

.td-review-card__flow-step--pending .td-review-card__flow-step-num {
  background: var(--td-surface-container-highest);
  color: var(--td-text-secondary);
}

.td-review-card__flow-step--done {
  color: var(--td-color-success);
  background: var(--td-color-success-light);
  border-color: var(--td-color-success);
}

.td-review-card__flow-step--done .td-review-card__flow-step-num {
  background: var(--td-color-success);
  color: #fff;
}

.td-review-card__flow-step--expired {
  color: var(--td-color-error);
  background: var(--td-color-error-light);
  border-color: var(--td-color-error);
}

.td-review-card__flow-step--expired .td-review-card__flow-step-num {
  background: var(--td-color-error);
  color: #fff;
}

@media (max-width: 640px) {
  .td-review-card__actions {
    flex-direction: column;
    align-items: stretch;
    margin-inline: calc(-1 * var(--td-space-4));
    padding-inline: var(--td-space-4);
  }

  .td-review-card__actions .td-btn {
    width: 100%;
    min-height: 48px;
    font-size: var(--td-font-sm);
    justify-content: center;
  }

  .td-review-card__flow-steps {
    width: 100%;
    justify-content: center;
    margin-inline-end: 0;
  }
}
</style>
