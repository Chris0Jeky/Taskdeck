<script setup lang="ts">
import { computed } from 'vue'
import type { Proposal } from '../../types/automation'
import {
  normalizeProposalRiskLevel,
  normalizeProposalSourceType,
  normalizeProposalStatus,
} from '../../utils/automation'
import type { ReviewDiffMode } from '../../composables/useReviewActions'
import ReviewProposalActions from './ReviewProposalActions.vue'
import ReviewProposalDetails from './ReviewProposalDetails.vue'

const props = defineProps<{
  proposal: Proposal
  isExpired: boolean
  isBusy: boolean
  selectedDiffProposalId: string | null
  selectedDiff: string | null
  selectedDiffMode: ReviewDiffMode | null
  /** The backend's actual /diff rejection reason for `invalid` mode (#1397 MEDIUM-1). */
  selectedDiffInvalidReason: string | null
  /** True when the stored preview's proposal has saved revisions; null = unknown (#1397 MEDIUM-2). */
  selectedDiffRevised: boolean | null
  captureHref: string
  proposalHref: string
}>()

// Whether the diff pane has anything to show for this proposal. Read-only
// (stored) and invalid states always render their banner/notice; a live diff
// only renders once its content arrives, so a slow fetch shows nothing rather
// than flashing a premature "no changes" (#1397).
const diffPaneVisible = computed(
  () =>
    props.selectedDiffProposalId === props.proposal.id &&
    (props.selectedDiffMode === 'stored' ||
      props.selectedDiffMode === 'invalid' ||
      !!props.selectedDiff),
)

// Read-only fallback when the proposal never captured a `diffPreview` (normal
// creation flows leave it null — Codex review on #1414): derive a minimal
// operation listing from the proposal's own recorded operations so a
// terminal/expired proposal that still HAS operations is inspectable instead of
// a dead "no stored preview" end. Local rendering only — the live `/diff` 400s
// for these proposals (#1397).
const storedOperationsFallback = computed(() => {
  if (props.selectedDiffMode !== 'stored' || props.selectedDiff) return null
  const ops = props.proposal.operations ?? []
  if (ops.length === 0) return null
  return [...ops]
    .sort((a, b) => a.sequence - b.sequence)
    .map(
      (op, index) =>
        `${index + 1}. ${op.actionType} ${op.targetType}${op.targetId ? ` (${op.targetId})` : ''}`,
    )
    .join('\n')
})

defineEmits<{
  (e: 'approve', proposalId: string): void
  (e: 'reject', proposalId: string, riskLevel: Proposal['riskLevel']): void
  (e: 'execute', proposalId: string): void
  (e: 'toggle-diff', proposalId: string): void
  (e: 'dismiss', proposalId: string): void
  (e: 'open-board', boardId: string): void
}>()

function formatDate(value: string | null): string {
  if (!value) {
    return '-'
  }
  return new Date(value).toLocaleString()
}

function readableSummary(proposal: Proposal): string {
  return proposal.presentation?.plainSummary || proposal.summary
}

function impactSummary(proposal: Proposal): string {
  return proposal.presentation?.impactSummary
    || `${proposal.operations.length} planned change${proposal.operations.length === 1 ? '' : 's'}.`
}

function getOperationHeadlines(proposal: Proposal): string[] {
  if (proposal.presentation?.operationHeadlines?.length) {
    return proposal.presentation.operationHeadlines
  }
  return proposal.operations.map((operation) => `${operation.actionType} ${operation.targetType}`)
}

function getAffectedEntities(proposal: Proposal) {
  return proposal.presentation?.affectedEntities ?? []
}

function hasProvenanceContext(proposal: Proposal): boolean {
  return !!captureSourceReference(proposal)
}

function captureSourceReference(proposal: Proposal): string | null {
  if (normalizeProposalSourceType(proposal.sourceType) !== 'Queue') {
    return null
  }
  if (!proposal.sourceReferenceId) {
    return null
  }
  const trimmed = proposal.sourceReferenceId.trim()
  return trimmed.length > 0 ? trimmed : null
}

function shortCorrelationId(correlationId: string | null | undefined): string {
  const trimmed = (correlationId || '').trim()
  return trimmed.length > 8 ? trimmed.slice(0, 8) + '...' : trimmed
}

function reviewStatusClass(status: Proposal['status']): string {
  if (props.isExpired) return 'td-review-status--expired'
  const normalized = normalizeProposalStatus(status)
  if (normalized === 'PendingReview') return 'td-review-status--pending'
  if (normalized === 'Approved') return 'td-review-status--approved'
  if (normalized === 'Expired') return 'td-review-status--expired'
  if (normalized === 'Applied') return 'td-review-status--applied'
  return 'td-review-status--secondary'
}

const statusLabels: Record<string, string> = {
  PendingReview: 'Review required',
  Approved: 'Approved, ready to apply',
  Applied: 'Applied to board',
  Rejected: 'Rejected',
  Failed: 'Failed',
  Expired: 'Expired',
  Dismissed: 'Dismissed',
}

function reviewStatusLabel(status: Proposal['status']): string {
  if (props.isExpired) return 'Expired'
  const normalized = normalizeProposalStatus(status)
  return statusLabels[normalized] ?? normalized
}

function riskLevelClass(riskLevel: Proposal['riskLevel']): string {
  const normalized = normalizeProposalRiskLevel(riskLevel)
  if (normalized === 'Low') return 'td-risk--low'
  if (normalized === 'Medium') return 'td-risk--medium'
  if (normalized === 'High') return 'td-risk--high'
  if (normalized === 'Critical') return 'td-risk--critical'
  return 'td-risk--low'
}
</script>

<template>
  <article
    :id="`proposal-${proposal.id}`"
    class="td-panel td-review-card"
  >
    <!-- Always visible: title, status badge, risk level, meta -->
    <div class="td-review-card__header">
      <div>
        <h2 class="td-review-card__title">{{ readableSummary(proposal) }}</h2>
        <div class="td-review-card__meta">
          <span :class="['td-risk-badge', riskLevelClass(proposal.riskLevel)]">
            {{ normalizeProposalRiskLevel(proposal.riskLevel) }} risk
          </span>
          <span>Created: {{ formatDate(proposal.createdAt) }}</span>
          <span>Source: {{ normalizeProposalSourceType(proposal.sourceType) }}</span>
        </div>
      </div>
      <span :class="['td-review-status', reviewStatusClass(proposal.status)]">
        {{ reviewStatusLabel(proposal.status) }}
      </span>
    </div>

    <!-- Impact cue always visible -->
    <div class="td-review-card__presentation">
      <span class="td-review-cue">{{ impactSummary(proposal) }}</span>
    </div>

    <!-- Action footer -->
    <ReviewProposalActions
      :proposal="proposal"
      :is-expired="isExpired"
      :is-busy="isBusy"
      :selected-diff-proposal-id="selectedDiffProposalId"
      @approve="$emit('approve', $event)"
      @reject="(id, risk) => $emit('reject', id, risk)"
      @execute="$emit('execute', $event)"
      @toggle-diff="$emit('toggle-diff', $event)"
      @dismiss="$emit('dismiss', $event)"
    />

    <!-- Collapsible details section -->
    <ReviewProposalDetails
      :proposal="proposal"
      :operation-headlines="getOperationHeadlines(proposal)"
      :affected-entities="getAffectedEntities(proposal)"
      :has-provenance="hasProvenanceContext(proposal)"
      :capture-href="captureHref"
      :proposal-href="proposalHref"
      :short-correlation-id="shortCorrelationId(proposal.correlationId)"
      @open-board="$emit('open-board', $event)"
    />

    <div
      v-if="diffPaneVisible"
      class="td-review-card__diff-wrapper"
      data-testid="review-diff-wrapper"
    >
      <!-- Read-only / terminal: stored preview under an explicit banner (#1397) -->
      <template v-if="selectedDiffMode === 'stored'">
        <span class="td-review-card__diff-banner" role="status" data-testid="review-diff-banner">
          {{ reviewStatusLabel(proposal.status) }} · read-only — showing the stored preview from
          the original submission.
        </span>
        <!-- diffPreview is creation-time content revisions never update, so a
             revised proposal's stored preview is NOT what a revision-aware Apply
             would have executed — disclose it (#1397 MEDIUM-2). -->
        <span
          v-if="selectedDiffRevised"
          class="td-review-card__diff-note td-review-card__diff-note--warn"
          role="status"
          data-testid="review-diff-revised-note"
        >
          This proposal was revised after submission — the stored preview shows the original
          operations, not the revised ones.
        </span>
        <pre
          v-if="selectedDiff"
          class="td-review-card__diff"
          role="region"
          aria-label="Stored proposal preview"
          data-testid="review-diff-stored"
        >{{ selectedDiff }}</pre>
        <template v-else-if="storedOperationsFallback">
          <span class="td-review-card__diff-note" data-testid="review-diff-stored-ops-note">
            No stored preview was captured — showing the proposal's recorded operations.
          </span>
          <pre
            class="td-review-card__diff"
            role="region"
            aria-label="Recorded proposal operations"
            data-testid="review-diff-stored-operations"
          >{{ storedOperationsFallback }}</pre>
        </template>
        <span
          v-else
          class="td-review-card__diff-note"
          data-testid="review-diff-stored-empty"
        >
          No stored preview is available for this proposal.
        </span>
      </template>

      <!-- Invalid: the backend rejected the diff with its Apply-time gates; render
           the backend's ACTUAL reason (expired vs zero-op), never a hardcoded
           one (#1397 MEDIUM-1). The fallback covers a missing message only. -->
      <span
        v-else-if="selectedDiffMode === 'invalid'"
        class="td-review-card__diff-note td-review-card__diff-note--warn"
        role="status"
        data-testid="review-diff-invalid"
      >
        {{ selectedDiffInvalidReason || 'This proposal contains no operations to apply' }} — Apply
        will reject this proposal.
      </span>

      <!-- Live diff for a still-actionable proposal -->
      <template v-else>
        <span class="td-review-card__diff-label">Operation details</span>
        <pre
          class="td-review-card__diff"
          role="region"
          aria-label="Proposal operation diff"
          data-testid="review-diff-pre"
        >{{ selectedDiff }}</pre>
      </template>
    </div>
  </article>
</template>

<style scoped>
.td-review-card {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
  max-height: 70vh;
  overflow-y: auto;
}

.td-review-card__header {
  display: flex;
  justify-content: space-between;
  gap: var(--td-space-3);
  align-items: flex-start;
}

.td-review-card__title {
  margin: 0;
  font-family: 'Manrope', system-ui, sans-serif;
  font-size: var(--td-font-lg);
  font-weight: 700;
  color: var(--td-text-primary);
}

.td-review-card__meta {
  display: flex;
  flex-wrap: wrap;
  gap: var(--td-space-3);
  margin-top: var(--td-space-1);
  font-size: var(--td-font-xs);
  color: var(--td-text-secondary);
}

.td-review-card__presentation {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
}

.td-review-cue {
  border-radius: var(--td-radius-pill, 999px);
  background: var(--td-surface-container-high);
  border: 1px solid var(--td-border-default);
  color: var(--td-text-secondary);
  font-size: var(--td-font-xs);
  font-weight: 600;
  padding: 0.25rem 0.625rem;
}

/* Risk level color-coded badges */
.td-risk-badge {
  border-radius: var(--td-radius-pill, 999px);
  font-size: var(--td-font-xs);
  font-weight: 700;
  padding: 0.125rem 0.5rem;
}

.td-risk--low {
  color: var(--td-color-success);
  background: var(--td-color-success-light);
}

.td-risk--medium {
  color: var(--td-color-warning);
  background: var(--td-color-warning-light);
}

.td-risk--high {
  color: var(--td-color-error);
  background: var(--td-color-error-light);
}

.td-risk--critical {
  color: var(--td-color-error);
  background: var(--td-color-error-light);
  border: 1px solid var(--td-color-error);
}

.td-review-status {
  display: inline-flex;
  align-items: center;
  border-radius: var(--td-radius-pill, 999px);
  border: 1px solid var(--td-border-default);
  padding: 0.25rem 0.625rem;
  font-size: var(--td-font-xs);
  font-weight: 700;
  white-space: nowrap;
}

.td-review-status--pending {
  color: var(--td-color-warning);
  background: var(--td-color-warning-light);
  border-color: var(--td-color-warning);
}

.td-review-status--approved {
  color: var(--td-color-success);
  background: var(--td-color-success-light);
  border-color: var(--td-color-success);
}

.td-review-status--applied {
  color: var(--td-color-info);
  background: var(--td-color-info-light);
  border-color: var(--td-color-info);
}

.td-review-status--expired {
  color: var(--td-color-warning);
  background: var(--td-color-warning-light);
  border-color: var(--td-color-warning);
}

.td-review-status--secondary {
  color: var(--td-text-secondary);
  background: var(--td-surface-container-high);
}

.td-review-card__diff-wrapper {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-1);
}

.td-review-card__diff-label {
  font-size: var(--td-font-xs);
  color: var(--td-text-tertiary);
  font-weight: 500;
}

.td-review-card__diff-banner {
  font-size: var(--td-font-xs);
  font-weight: 600;
  color: var(--td-color-warning);
}

.td-review-card__diff-note {
  font-size: var(--td-font-xs);
  color: var(--td-text-secondary);
}

.td-review-card__diff-note--warn {
  color: var(--td-color-warning);
  font-weight: 600;
}

.td-review-card__diff {
  margin: 0;
  padding: var(--td-space-3);
  border-radius: var(--td-radius-md);
  background: var(--td-surface-container-lowest);
  color: var(--td-text-primary);
  font-size: var(--td-font-xs);
  overflow-x: auto;
  border: 1px solid var(--td-border-ghost);
  white-space: pre-wrap;
  word-break: break-word;
}

@media (max-width: 900px) {
  .td-review-card__header {
    flex-direction: column;
  }
}

@media (max-width: 640px) {
  .td-review-card {
    gap: var(--td-space-2);
    padding: var(--td-space-4);
    max-height: 80vh;
  }

  .td-review-card__title {
    font-size: var(--td-font-base);
  }

  .td-review-card__meta {
    flex-direction: column;
    gap: var(--td-space-1);
  }

  .td-review-card__diff {
    font-size: var(--td-font-xs);
    padding: var(--td-space-2);
  }
}
</style>
