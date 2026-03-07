<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { automationApi } from '../api/automationApi'
import { useToastStore } from '../store/toastStore'
import { createRequestId } from '../utils/requestId'
import {
  normalizeProposalRiskLevel,
  normalizeProposalSourceType,
  normalizeProposalStatus,
} from '../utils/automation'
import type { Proposal as ApiProposal } from '../types/automation'
import { getErrorDisplay } from '../composables/useErrorMapper'

type ReviewSummaryCard = {
  id: string
  label: string
  value: number
  helper: string
}

const route = useRoute()
const router = useRouter()
const toast = useToastStore()

const proposals = ref<ApiProposal[]>([])
const proposalsLoading = ref(false)
const proposalActionBusyId = ref<string | null>(null)
const selectedDiffProposalId = ref<string | null>(null)
const selectedDiff = ref<string | null>(null)
let latestDiffRequestId = 0

const summaryCards = computed<ReviewSummaryCard[]>(() => {
  const pendingReview = proposals.value.filter(
    (proposal) => normalizeProposalStatus(proposal.status) === 'PendingReview',
  ).length
  const readyToExecute = proposals.value.filter(
    (proposal) => normalizeProposalStatus(proposal.status) === 'Approved',
  ).length
  const captureLinked = proposals.value.filter((proposal) => hasProvenanceContext(proposal)).length
  const appliedRecently = proposals.value.filter(
    (proposal) => normalizeProposalStatus(proposal.status) === 'Applied',
  ).length

  return [
    {
      id: 'pending-review',
      label: 'Pending review',
      value: pendingReview,
      helper: 'Changes waiting for an explicit decision.',
    },
    {
      id: 'ready-to-execute',
      label: 'Ready to execute',
      value: readyToExecute,
      helper: 'Approved proposals that can now land on boards.',
    },
    {
      id: 'capture-linked',
      label: 'Capture-linked',
      value: captureLinked,
      helper: 'Review items that came through the inbox loop.',
    },
    {
      id: 'applied',
      label: 'Applied',
      value: appliedRecently,
      helper: 'Proposals already executed successfully.',
    },
  ]
})

async function loadProposals() {
  try {
    proposalsLoading.value = true
    proposals.value = await automationApi.getProposals({ limit: 200 })
  } catch (e: unknown) {
    toast.error(getErrorDisplay(e, 'Failed to load proposals').message)
  } finally {
    proposalsLoading.value = false
  }

  await scrollToProposalFromHash()
}

function getProposalIdFromHash(hash: string): string | null {
  if (!hash.startsWith('#proposal-')) {
    return null
  }

  const rawId = hash.slice('#proposal-'.length).trim()
  if (!rawId) {
    return null
  }

  try {
    return decodeURIComponent(rawId)
  } catch {
    return null
  }
}

async function scrollToProposalFromHash() {
  const proposalId = getProposalIdFromHash(route.hash)
  if (!proposalId) {
    return
  }

  await nextTick()
  const element = document.getElementById(`proposal-${proposalId}`)
  element?.scrollIntoView({ block: 'nearest' })
}

async function handleApproveProposal(proposalId: string) {
  try {
    proposalActionBusyId.value = proposalId
    const updated = await automationApi.approveProposal(proposalId)
    proposals.value = proposals.value.map((proposal) => (proposal.id === proposalId ? updated : proposal))
    toast.success('Proposal approved')
  } catch (e: unknown) {
    toast.error(getErrorDisplay(e, 'Failed to approve proposal').message)
  } finally {
    proposalActionBusyId.value = null
  }
}

async function handleRejectProposal(proposalId: string, riskLevel: ApiProposal['riskLevel']) {
  const requiresReason = ['High', 'Critical'].includes(normalizeProposalRiskLevel(riskLevel))
  const reason = prompt(requiresReason ? 'Reason is required for this risk level:' : 'Optional rejection reason:') ?? ''
  if (requiresReason && !reason.trim()) {
    toast.error('Rejection reason is required for high and critical risk proposals')
    return
  }

  try {
    proposalActionBusyId.value = proposalId
    const updated = await automationApi.rejectProposal(proposalId, reason.trim())
    proposals.value = proposals.value.map((proposal) => (proposal.id === proposalId ? updated : proposal))
    toast.success('Proposal rejected')
  } catch (e: unknown) {
    toast.error(getErrorDisplay(e, 'Failed to reject proposal').message)
  } finally {
    proposalActionBusyId.value = null
  }
}

async function handleExecuteProposal(proposalId: string) {
  if (!confirm('Execute this approved proposal?')) {
    return
  }

  try {
    proposalActionBusyId.value = proposalId
    const updated = await automationApi.executeProposal(proposalId, createRequestId())
    proposals.value = proposals.value.map((proposal) => (proposal.id === proposalId ? updated : proposal))
    toast.success('Proposal executed')
  } catch (e: unknown) {
    toast.error(getErrorDisplay(e, 'Failed to execute proposal').message)
  } finally {
    proposalActionBusyId.value = null
  }
}

async function handleToggleDiff(proposalId: string) {
  if (selectedDiffProposalId.value === proposalId) {
    latestDiffRequestId += 1
    selectedDiffProposalId.value = null
    selectedDiff.value = null
    return
  }

  const requestId = ++latestDiffRequestId

  try {
    selectedDiffProposalId.value = proposalId
    selectedDiff.value = null

    const diff = await automationApi.getProposalDiff(proposalId)
    if (requestId !== latestDiffRequestId || selectedDiffProposalId.value !== proposalId) {
      return
    }

    selectedDiff.value = diff
  } catch (e: unknown) {
    if (requestId !== latestDiffRequestId || selectedDiffProposalId.value !== proposalId) {
      return
    }

    selectedDiffProposalId.value = null
    selectedDiff.value = null
    toast.error(getErrorDisplay(e, 'Failed to load proposal diff').message)
  }
}

function formatDate(value: string | null): string {
  if (!value) {
    return '-'
  }

  return new Date(value).toLocaleString()
}

function openRoute(path: string) {
  void router.push(path)
}

function proposalHref(proposalId: string): string {
  return `/workspace/review#proposal-${encodeURIComponent(proposalId)}`
}

function captureHref(captureItemId: string): string {
  return `/workspace/inbox#capture-${encodeURIComponent(captureItemId)}`
}

function captureSourceReference(proposal: ApiProposal): string | null {
  if (normalizeProposalSourceType(proposal.sourceType) !== 'Queue') {
    return null
  }

  if (!proposal.sourceReferenceId) {
    return null
  }

  const trimmed = proposal.sourceReferenceId.trim()
  return trimmed.length > 0 ? trimmed : null
}

function hasProvenanceContext(proposal: ApiProposal): boolean {
  return !!captureSourceReference(proposal)
}

function captureHrefForProposal(proposal: ApiProposal): string {
  const sourceReference = captureSourceReference(proposal)
  return sourceReference ? captureHref(sourceReference) : '/workspace/inbox'
}

function reviewStatusClass(status: ApiProposal['status']): string {
  const normalized = normalizeProposalStatus(status)
  if (normalized === 'PendingReview') return 'td-review-status--pending'
  if (normalized === 'Approved') return 'td-review-status--approved'
  if (normalized === 'Applied') return 'td-review-status--applied'
  return 'td-review-status--secondary'
}

onMounted(() => {
  void loadProposals()
})

watch(
  () => route.hash,
  () => {
    if (proposals.value.length === 0) {
      return
    }

    void scrollToProposalFromHash()
  },
)
</script>

<template>
  <div class="td-review">
    <header class="td-panel td-review__hero">
      <div class="td-review__hero-copy">
        <span class="td-review__eyebrow">Review</span>
        <h1 class="td-page-title">Review</h1>
        <p class="td-review__subtitle">
          Review proposed changes before anything touches a board. Queue and chat remain advanced surfaces when you
          need manual/operator control.
        </p>
      </div>

      <div class="td-review__hero-actions">
        <button class="td-btn td-btn--primary" :disabled="proposalsLoading" @click="loadProposals">
          {{ proposalsLoading ? 'Refreshing...' : 'Refresh Review' }}
        </button>
        <button class="td-btn td-btn--secondary" @click="openRoute('/workspace/inbox')">Open Inbox</button>
        <button class="td-btn td-btn--secondary" @click="openRoute('/workspace/automations/queue')">
          Open Queue (Advanced)
        </button>
        <button class="td-btn td-btn--secondary" @click="openRoute('/workspace/automations/chat')">
          Open Chat (Advanced)
        </button>
      </div>
    </header>

    <section class="td-review__summary">
      <article v-for="card in summaryCards" :key="card.id" class="td-panel td-review-summary-card">
        <span class="td-review-summary-card__value">{{ card.value }}</span>
        <span class="td-review-summary-card__label">{{ card.label }}</span>
        <span class="td-review-summary-card__helper">{{ card.helper }}</span>
      </article>
    </section>

    <div v-if="proposalsLoading" class="td-panel td-review__loading" aria-live="polite">
      Loading proposals to review...
    </div>

    <section v-else-if="proposals.length === 0" class="td-panel td-review-empty">
      <h2 class="td-section-title">No proposals need review yet</h2>
      <p class="td-section-desc">
        Start from Inbox when you want Taskdeck to propose a change, or open Boards if you want to continue directly
        with the work that already landed.
      </p>
      <div class="td-review-empty__actions">
        <button class="td-btn td-btn--primary" @click="openRoute('/workspace/inbox')">Go to Inbox</button>
        <button class="td-btn td-btn--secondary" @click="openRoute('/workspace/boards')">Open Boards</button>
        <button class="td-btn td-btn--secondary" @click="openRoute('/workspace/home')">Back to Home</button>
      </div>
    </section>

    <section v-else class="td-review__list">
      <article
        v-for="proposal in proposals"
        :id="`proposal-${proposal.id}`"
        :key="proposal.id"
        class="td-panel td-review-card"
      >
        <div class="td-review-card__header">
          <div>
            <h2 class="td-review-card__title">{{ proposal.summary }}</h2>
            <div class="td-review-card__meta">
              <span>Risk: {{ normalizeProposalRiskLevel(proposal.riskLevel) }}</span>
              <span>Created: {{ formatDate(proposal.createdAt) }}</span>
              <span>Source: {{ normalizeProposalSourceType(proposal.sourceType) }}</span>
            </div>
          </div>
          <span :class="['td-review-status', reviewStatusClass(proposal.status)]">
            {{ normalizeProposalStatus(proposal.status) }}
          </span>
        </div>

        <div v-if="hasProvenanceContext(proposal)" class="td-review-card__provenance">
          <span class="td-provenance-chip">Capture-linked</span>
          <a class="td-btn td-btn--secondary td-btn--sm" :href="captureHrefForProposal(proposal)">Open Capture</a>
          <a class="td-btn td-btn--secondary td-btn--sm" :href="proposalHref(proposal.id)">Review Link</a>
          <span v-if="proposal.correlationId.trim().length > 0" class="td-review-card__provenance-meta">
            Triage run: {{ proposal.correlationId }}
          </span>
        </div>

        <div class="td-review-card__actions">
          <button class="td-btn td-btn--secondary td-btn--sm" @click="handleToggleDiff(proposal.id)">
            {{ selectedDiffProposalId === proposal.id ? 'Hide Diff' : 'View Diff' }}
          </button>
          <button
            class="td-btn td-btn--primary td-btn--sm"
            :disabled="proposalActionBusyId === proposal.id || normalizeProposalStatus(proposal.status) !== 'PendingReview'"
            @click="handleApproveProposal(proposal.id)"
          >
            Approve
          </button>
          <button
            class="td-btn td-btn--danger td-btn--sm"
            :disabled="proposalActionBusyId === proposal.id || normalizeProposalStatus(proposal.status) !== 'PendingReview'"
            @click="handleRejectProposal(proposal.id, proposal.riskLevel)"
          >
            Reject
          </button>
          <button
            class="td-btn td-btn--secondary td-btn--sm"
            :disabled="proposalActionBusyId === proposal.id || normalizeProposalStatus(proposal.status) !== 'Approved'"
            @click="handleExecuteProposal(proposal.id)"
          >
            Execute
          </button>
        </div>

        <pre v-if="selectedDiffProposalId === proposal.id && selectedDiff" class="td-review-card__diff">{{ selectedDiff }}</pre>
      </article>
    </section>
  </div>
</template>

<style scoped>
.td-review {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-4);
}

.td-review__hero {
  display: flex;
  justify-content: space-between;
  gap: var(--td-space-6);
  align-items: flex-start;
}

.td-review__hero-copy {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
  max-width: 720px;
}

.td-review__eyebrow {
  font-size: var(--td-font-xs);
  font-weight: 700;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--td-color-primary);
}

.td-review__subtitle {
  color: var(--td-text-secondary);
  line-height: 1.6;
}

.td-review__hero-actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--td-space-2);
  justify-content: flex-end;
}

.td-review__summary {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: var(--td-space-3);
}

.td-review-summary-card {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.td-review-summary-card__value {
  font-size: var(--td-font-2xl);
  font-weight: 700;
  color: var(--td-text-primary);
}

.td-review-summary-card__label {
  font-size: var(--td-font-sm);
  font-weight: 600;
  color: var(--td-text-primary);
}

.td-review-summary-card__helper {
  font-size: var(--td-font-xs);
  color: var(--td-text-secondary);
  line-height: 1.5;
}

.td-review__loading {
  color: var(--td-text-secondary);
}

.td-review-empty {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
}

.td-review-empty__actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--td-space-2);
}

.td-review__list {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
}

.td-review-card {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
}

.td-review-card__header {
  display: flex;
  justify-content: space-between;
  gap: var(--td-space-3);
  align-items: flex-start;
}

.td-review-card__title {
  margin: 0;
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
  color: #92400e;
  background: #fef3c7;
  border-color: #f59e0b;
}

.td-review-status--approved {
  color: #065f46;
  background: #d1fae5;
  border-color: #10b981;
}

.td-review-status--applied {
  color: #1d4ed8;
  background: #dbeafe;
  border-color: #3b82f6;
}

.td-review-status--secondary {
  color: var(--td-text-secondary);
  background: var(--td-surface-secondary);
}

.td-review-card__provenance {
  display: flex;
  flex-wrap: wrap;
  gap: var(--td-space-2);
  align-items: center;
}

.td-provenance-chip {
  border-radius: var(--td-radius-pill, 999px);
  background: var(--td-surface-secondary);
  border: 1px solid var(--td-border-default);
  color: var(--td-text-secondary);
  font-size: var(--td-font-xs);
  font-weight: 600;
  padding: 0.25rem 0.625rem;
}

.td-review-card__provenance-meta {
  font-size: var(--td-font-xs);
  color: var(--td-text-secondary);
}

.td-review-card__actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--td-space-2);
}

.td-review-card__diff {
  margin: 0;
  padding: var(--td-space-3);
  border-radius: var(--td-radius-md);
  background: var(--td-text-primary);
  color: #e2e8f0;
  font-size: var(--td-font-xs);
  overflow-x: auto;
}

.td-btn {
  padding: var(--td-space-2) var(--td-space-3);
  border-radius: var(--td-radius-md);
  border: 1px solid transparent;
  cursor: pointer;
  text-decoration: none;
}

.td-btn--sm {
  padding: var(--td-space-1) var(--td-space-3);
  font-size: var(--td-font-xs);
}

.td-btn--primary {
  background: var(--td-color-primary);
  color: var(--td-text-inverse);
}

.td-btn--secondary {
  background: var(--td-surface-tertiary);
  color: var(--td-text-primary);
  border-color: var(--td-border-default);
}

.td-btn--danger {
  background: var(--td-color-error);
  color: var(--td-text-inverse);
}

.td-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

@media (max-width: 900px) {
  .td-review__hero {
    flex-direction: column;
  }

  .td-review__hero-actions {
    justify-content: flex-start;
  }

  .td-review-card__header {
    flex-direction: column;
  }
}
</style>
