<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { automationApi } from '../api/automationApi'
import WorkspaceHelpCallout from '../components/workspace/WorkspaceHelpCallout.vue'
import { useToastStore } from '../store/toastStore'
import { createRequestId } from '../utils/requestId'
import {
  normalizeProposalRiskLevel,
  normalizeProposalSourceType,
  normalizeProposalStatus,
} from '../utils/automation'
import { normalizeBoardIdQueryParam } from '../utils/navigation'
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
let latestProposalLoadRequestId = 0
let latestDiffRequestId = 0
const activeBoardFilter = computed(() => normalizeBoardIdQueryParam(route.query.boardId))

function matchesActiveBoardFilter(boardId: string | null | undefined): boolean {
  if (!activeBoardFilter.value) {
    return true
  }

  const normalizedBoardId = normalizeBoardIdQueryParam(boardId)
  return normalizedBoardId === activeBoardFilter.value
}

const visibleProposals = computed(() => proposals.value.filter((proposal) => matchesActiveBoardFilter(proposal.boardId)))

const summaryCards = computed<ReviewSummaryCard[]>(() => {
  let pendingReview = 0
  let readyToExecute = 0
  let captureLinked = 0
  let appliedRecently = 0

  for (const proposal of visibleProposals.value) {
    const normalizedStatus = normalizeProposalStatus(proposal.status)

    if (normalizedStatus === 'PendingReview') {
      pendingReview += 1
    } else if (normalizedStatus === 'Approved') {
      readyToExecute += 1
    } else if (normalizedStatus === 'Applied') {
      appliedRecently += 1
    }

    if (hasProvenanceContext(proposal)) {
      captureLinked += 1
    }
  }

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
  const requestId = ++latestProposalLoadRequestId

  try {
    proposalsLoading.value = true
    const loadedProposals = await automationApi.getProposals({
      limit: 200,
      boardId: activeBoardFilter.value || undefined,
    })
    if (requestId !== latestProposalLoadRequestId) {
      return
    }

    proposals.value = loadedProposals
  } catch (e: unknown) {
    if (requestId !== latestProposalLoadRequestId) {
      return
    }

    toast.error(getErrorDisplay(e, 'Failed to load proposals').message)
  } finally {
    if (requestId === latestProposalLoadRequestId) {
      proposalsLoading.value = false
    }
  }

  if (requestId === latestProposalLoadRequestId) {
    await openProposalFromHash()
  }
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

function upsertProposal(proposal: ApiProposal) {
  const existingIndex = proposals.value.findIndex((current) => current.id === proposal.id)
  if (existingIndex >= 0) {
    proposals.value[existingIndex] = proposal
    return
  }

  const proposalCreatedAt = new Date(proposal.createdAt).getTime()
  const insertIndex = proposals.value.findIndex((current) => new Date(current.createdAt).getTime() < proposalCreatedAt)

  if (insertIndex >= 0) {
    proposals.value.splice(insertIndex, 0, proposal)
    return
  }

  proposals.value.push(proposal)
}

function isHttpNotFound(error: unknown): boolean {
  const candidate = error as { response?: { status?: number } } | null
  return candidate?.response?.status === 404
}

async function openProposalFromHash() {
  if (proposalsLoading.value) {
    return
  }

  const proposalId = getProposalIdFromHash(route.hash)
  if (!proposalId) {
    return
  }

  const currentProposal = proposals.value.find((proposal) => proposal.id === proposalId)
  if (currentProposal) {
    if (!matchesActiveBoardFilter(currentProposal.boardId)) {
      await router.replace({
        name: 'workspace-review',
        query: route.query,
      })
      return
    }

    await scrollToProposalFromHash()
    return
  }

  try {
    const fetchedProposal = await automationApi.getProposal(proposalId)
    if (getProposalIdFromHash(route.hash) !== proposalId) {
      return
    }

    if (!matchesActiveBoardFilter(fetchedProposal.boardId)) {
      await router.replace({
        name: 'workspace-review',
        query: route.query,
      })
      return
    }

    upsertProposal(fetchedProposal)
    await nextTick()
    await scrollToProposalFromHash()
  } catch (e: unknown) {
    if (getProposalIdFromHash(route.hash) !== proposalId) {
      return
    }

    if (isHttpNotFound(e)) {
      await router.replace({
        name: 'workspace-review',
        query: route.query,
      })

      return
    }

    toast.error(getErrorDisplay(e, 'Failed to load proposal').message)
  }
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
  const promptedReason = prompt(
    requiresReason ? 'Reason is required for this risk level:' : 'Optional rejection reason:',
  )
  if (promptedReason === null) {
    return
  }

  const reason = promptedReason.trim()
  if (requiresReason && !reason) {
    toast.error('Rejection reason is required for high and critical risk proposals')
    return
  }

  const reasonOrNull = reason.length > 0 ? reason : null

  try {
    proposalActionBusyId.value = proposalId
    const updated = await automationApi.rejectProposal(proposalId, reasonOrNull)
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

function readableSummary(proposal: ApiProposal): string {
  return proposal.presentation?.plainSummary || proposal.summary
}

function impactSummary(proposal: ApiProposal): string {
  return proposal.presentation?.impactSummary
    || `${proposal.operations.length} planned change${proposal.operations.length === 1 ? '' : 's'}.`
}

function sourceCue(proposal: ApiProposal): string {
  return proposal.presentation?.sourceCue || `Source: ${normalizeProposalSourceType(proposal.sourceType)}`
}

function riskCue(proposal: ApiProposal): string {
  return proposal.presentation?.riskCue || `Risk: ${normalizeProposalRiskLevel(proposal.riskLevel)}`
}

function operationHeadlines(proposal: ApiProposal): string[] {
  if (proposal.presentation?.operationHeadlines?.length) {
    return proposal.presentation.operationHeadlines
  }

  return proposal.operations.map((operation) => `${operation.actionType} ${operation.targetType}`)
}

function affectedEntities(proposal: ApiProposal) {
  return proposal.presentation?.affectedEntities ?? []
}

function openRoute(path: string) {
  void router.push(path)
}

function openBoard(boardId: string) {
  void router.push(`/workspace/boards/${boardId}`)
}

function inboxPath(boardId?: string | null, captureItemId?: string): string {
  const encodedBoardId = boardId ? encodeURIComponent(boardId) : null
  const query = encodedBoardId ? `?boardId=${encodedBoardId}` : ''
  const hash = captureItemId ? `#capture-${encodeURIComponent(captureItemId)}` : ''
  return `/workspace/inbox${query}${hash}`
}

function openInbox() {
  void router.push(inboxPath(activeBoardFilter.value))
}

function proposalHref(proposal: ApiProposal): string {
  const query = proposal.boardId ?? activeBoardFilter.value
  const encodedProposalId = encodeURIComponent(proposal.id)
  return query
    ? `/workspace/review?boardId=${encodeURIComponent(query)}#proposal-${encodedProposalId}`
    : `/workspace/review#proposal-${encodedProposalId}`
}

function captureHref(captureItemId: string, boardId?: string | null): string {
  return inboxPath(boardId, captureItemId)
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
  return sourceReference
    ? captureHref(sourceReference, proposal.boardId ?? activeBoardFilter.value)
    : inboxPath(activeBoardFilter.value)
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
    void openProposalFromHash()
  },
)

watch(
  () => activeBoardFilter.value,
  () => {
    void loadProposals()
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
        <p v-if="activeBoardFilter" class="td-review__board-filter">
          Showing proposals for board {{ activeBoardFilter }}.
        </p>
      </div>

      <div class="td-review__hero-actions">
        <button class="td-btn td-btn--primary" :disabled="proposalsLoading" @click="loadProposals">
          {{ proposalsLoading ? 'Refreshing...' : 'Refresh Review' }}
        </button>
        <button class="td-btn td-btn--secondary" @click="openInbox">Open Inbox</button>
        <button class="td-btn td-btn--secondary" @click="openRoute('/workspace/automations/queue')">
          Open Queue (Advanced)
        </button>
        <button class="td-btn td-btn--secondary" @click="openRoute('/workspace/automations/chat')">
          Open Chat (Advanced)
        </button>
      </div>
    </header>

    <WorkspaceHelpCallout
      topic="review"
      title="What is Review for?"
      description="Review is the trust gate. Proposed changes stop here before they touch a board, while queue and chat remain advanced/operator surfaces when you need to drive the workflow manually."
    >
      <template #actions>
        <button class="td-btn td-btn--secondary td-btn--sm" @click="openInbox">Open Inbox</button>
        <button class="td-btn td-btn--secondary td-btn--sm" @click="openRoute('/workspace/boards')">Open Boards</button>
      </template>
    </WorkspaceHelpCallout>

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

    <section v-else-if="visibleProposals.length === 0" class="td-panel td-review-empty">
      <h2 class="td-section-title">No proposals need review yet</h2>
      <p class="td-section-desc">
        Start from Inbox when you want Taskdeck to propose a change, or open Boards if you want to continue directly
        with the work that already landed.
      </p>
      <div class="td-review-empty__actions">
        <button class="td-btn td-btn--primary" @click="openInbox">Go to Inbox</button>
        <button class="td-btn td-btn--secondary" @click="openRoute('/workspace/boards')">Open Boards</button>
        <button class="td-btn td-btn--secondary" @click="openRoute('/workspace/home')">Back to Home</button>
      </div>
    </section>

    <section v-else class="td-review__list">
      <article
        v-for="proposal in visibleProposals"
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

        <div class="td-review-card__presentation">
          <p class="td-review-card__summary">{{ readableSummary(proposal) }}</p>
          <div class="td-review-card__cues">
            <span class="td-review-cue">{{ impactSummary(proposal) }}</span>
            <span class="td-review-cue">{{ riskCue(proposal) }}</span>
            <span class="td-review-cue">{{ sourceCue(proposal) }}</span>
          </div>
        </div>

        <div v-if="hasProvenanceContext(proposal)" class="td-review-card__provenance">
          <span class="td-provenance-chip">Capture-linked</span>
          <router-link class="td-btn td-btn--secondary td-btn--sm" :to="captureHrefForProposal(proposal)">
            Open Capture
          </router-link>
          <router-link class="td-btn td-btn--secondary td-btn--sm" :to="proposalHref(proposal)">
            Review Link
          </router-link>
          <button
            v-if="proposal.boardId"
            class="td-btn td-btn--secondary td-btn--sm"
            @click="openBoard(proposal.boardId)"
          >
            Open Board
          </button>
          <span v-if="proposal.correlationId.trim().length > 0" class="td-review-card__provenance-meta">
            Triage run: {{ proposal.correlationId }}
          </span>
        </div>

        <div v-if="affectedEntities(proposal).length > 0" class="td-review-card__entities">
          <span class="td-review-card__section-label">Affected</span>
          <div class="td-review-card__entity-list">
            <span
              v-for="entity in affectedEntities(proposal)"
              :key="`${proposal.id}-${entity.entityType}-${entity.entityId ?? 'none'}`"
              class="td-review-entity-chip"
            >
              {{ entity.label }} · {{ entity.changeCount }} change{{ entity.changeCount === 1 ? '' : 's' }}
            </span>
          </div>
        </div>

        <div v-if="operationHeadlines(proposal).length > 0" class="td-review-card__operations">
          <span class="td-review-card__section-label">Planned changes</span>
          <ul class="td-review-card__operation-list">
            <li
              v-for="(headline, headlineIndex) in operationHeadlines(proposal)"
              :key="`${proposal.id}-${headlineIndex}-${headline}`"
            >
              {{ headline }}
            </li>
          </ul>
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

.td-review__board-filter {
  margin: 0;
  color: var(--td-color-primary);
  font-size: var(--td-font-sm);
  font-weight: 600;
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
  font-family: 'Manrope', system-ui, sans-serif;
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

.td-review-card__summary {
  margin: 0;
  color: var(--td-text-primary);
  line-height: 1.6;
}

.td-review-card__cues {
  display: flex;
  flex-wrap: wrap;
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

.td-review-status--secondary {
  color: var(--td-text-secondary);
  background: var(--td-surface-container-high);
}

.td-review-card__provenance {
  display: flex;
  flex-wrap: wrap;
  gap: var(--td-space-2);
  align-items: center;
}

.td-provenance-chip {
  border-radius: var(--td-radius-pill, 999px);
  background: var(--td-surface-container-high);
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

.td-review-card__entities,
.td-review-card__operations {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
}

.td-review-card__section-label {
  font-size: var(--td-font-xs);
  font-weight: 700;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: var(--td-text-secondary);
}

.td-review-card__entity-list {
  display: flex;
  flex-wrap: wrap;
  gap: var(--td-space-2);
}

.td-review-entity-chip {
  border-radius: var(--td-radius-pill, 999px);
  background: var(--td-surface-container-highest);
  border: 1px solid var(--td-border-default);
  color: var(--td-text-primary);
  font-size: var(--td-font-xs);
  font-weight: 600;
  padding: 0.25rem 0.625rem;
}

.td-review-card__operation-list {
  margin: 0;
  padding-left: 1.25rem;
  color: var(--td-text-secondary);
  line-height: 1.6;
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
  background: var(--td-surface-container-lowest);
  color: var(--td-text-primary);
  font-size: var(--td-font-xs);
  overflow-x: auto;
  border: 1px solid var(--td-border-ghost);
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
