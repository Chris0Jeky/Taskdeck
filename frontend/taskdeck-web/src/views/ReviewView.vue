<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { automationApi } from '../api/automationApi'
import { boardsApi } from '../api/boardsApi'
import WorkspaceHelpCallout from '../components/workspace/WorkspaceHelpCallout.vue'
import InputAssistField from '../components/common/InputAssistField.vue'
import { useToastStore } from '../store/toastStore'
import { createRequestId } from '../utils/requestId'
import {
  normalizeProposalRiskLevel,
  normalizeProposalSourceType,
  normalizeProposalStatus,
} from '../utils/automation'
import { buildInputAssistOptions } from '../utils/inputAssist'
import { normalizeBoardIdQueryParam } from '../utils/navigation'
import type { Proposal as ApiProposal } from '../types/automation'
import type { Board } from '../types/board'
import { getErrorDisplay } from '../composables/useErrorMapper'
import { usePerformanceMark } from '../composables/usePerformanceMark'

type ReviewSummaryCard = {
  id: string
  label: string
  value: number
  helper: string
}

const route = useRoute()
const router = useRouter()
const toast = useToastStore()
const reviewLoadPerf = usePerformanceMark('review-load')
const diffRenderPerf = usePerformanceMark('proposal-diff-render')

const proposals = ref<ApiProposal[]>([])
const proposalsLoading = ref(false)
const proposalActionBusyId = ref<string | null>(null)
const selectedDiffProposalId = ref<string | null>(null)
const selectedDiff = ref<string | null>(null)
let latestProposalLoadRequestId = 0
let latestDiffRequestId = 0
const availableBoards = ref<Board[]>([])
const loadingBoards = ref(false)
const boardFilterInput = ref('')
const activeBoardFilter = computed(() => normalizeBoardIdQueryParam(route.query.boardId))
const showCompleted = ref(false)

// Per-proposal collapsible section state (Record<bool> for Vue reactivity)
const expandedSections = ref<Record<string, Record<string, boolean>>>({})

function isSectionExpanded(proposalId: string, section: string): boolean {
  return !!expandedSections.value[proposalId]?.[section]
}

function toggleSection(proposalId: string, section: string) {
  if (!expandedSections.value[proposalId]) {
    expandedSections.value[proposalId] = {}
  }
  expandedSections.value[proposalId][section] = !expandedSections.value[proposalId][section]
}

// Per-proposal link dropdown state
const openLinkDropdown = ref<string | null>(null)

function toggleLinkDropdown(proposalId: string) {
  openLinkDropdown.value = openLinkDropdown.value === proposalId ? null : proposalId
}

function closeLinkDropdown(event: FocusEvent) {
  const nextFocus = event.relatedTarget as HTMLElement
  if (nextFocus?.closest('.td-review-card__links-dropdown-wrapper')) {
    return
  }
  openLinkDropdown.value = null
}

const completedStatuses = new Set(['Applied', 'Rejected', 'Failed', 'Expired', 'Dismissed'])

const boardOptions = computed(() =>
  buildInputAssistOptions(
    availableBoards.value.map((board) => ({
      value: board.id,
      label: board.name,
    })),
  ),
)

const activeBoardName = computed(() => {
  if (!activeBoardFilter.value) return ''
  const normalizedActiveId = normalizeBoardIdQueryParam(activeBoardFilter.value).toLowerCase()
  const board = availableBoards.value.find(
    (b) => normalizeBoardIdQueryParam(b.id).toLowerCase() === normalizedActiveId,
  )
  return board?.name ?? activeBoardFilter.value
})

function matchesActiveBoardFilter(boardId: string | null | undefined): boolean {
  if (!activeBoardFilter.value) {
    return true
  }

  const normalizedBoardId = normalizeBoardIdQueryParam(boardId).toLowerCase()
  return normalizedBoardId === activeBoardFilter.value.toLowerCase()
}

const visibleProposals = computed(() =>
  proposals.value.filter((proposal) => {
    if (!matchesActiveBoardFilter(proposal.boardId)) return false

    // Always hide dismissed proposals
    if (normalizeProposalStatus(proposal.status) === 'Dismissed') return false

    // When showCompleted is off, hide terminal-state proposals
    if (!showCompleted.value && completedStatuses.has(normalizeProposalStatus(proposal.status))) return false

    return true
  }),
)

const summaryCards = computed<ReviewSummaryCard[]>(() => {
  let pendingReview = 0
  let readyToExecute = 0
  let captureLinked = 0
  let appliedRecently = 0

  for (const proposal of visibleProposals.value) {
    const normalizedStatus = normalizeProposalStatus(proposal.status)

    if (normalizedStatus === 'PendingReview') {
      pendingReview += 1
    } else if (normalizedStatus === 'Approved' && !isProposalExpired(proposal)) {
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
  reviewLoadPerf.start()
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
    reviewLoadPerf.end()
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
    toast.success('Proposal approved for board application')
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
  if (!confirm('Apply this approved proposal to the board now?')) {
    return
  }

  try {
    proposalActionBusyId.value = proposalId
    const updated = await automationApi.executeProposal(proposalId, createRequestId())
    proposals.value = proposals.value.map((proposal) => (proposal.id === proposalId ? updated : proposal))
    toast.success('Proposal applied to board')
  } catch (e: unknown) {
    toast.error(getErrorDisplay(e, 'Failed to apply proposal to board').message)
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

  diffRenderPerf.start()
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
  } finally {
    diffRenderPerf.end()
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

// sourceCue and riskCue replaced by color-coded risk badge and inline meta display

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

function shortCorrelationId(correlationId: string): string {
  const trimmed = correlationId.trim()
  return trimmed.length > 8 ? trimmed.slice(0, 8) + '...' : trimmed
}

function captureHrefForProposal(proposal: ApiProposal): string {
  const sourceReference = captureSourceReference(proposal)
  return sourceReference
    ? captureHref(sourceReference, proposal.boardId ?? activeBoardFilter.value)
    : inboxPath(activeBoardFilter.value)
}

/**
 * Checks whether a proposal is effectively expired.
 * Prefers the server-authoritative isExpired flag; falls back to client-side comparison.
 */
function isProposalExpired(proposal: ApiProposal): boolean {
  if (typeof proposal.isExpired === 'boolean') {
    return proposal.isExpired
  }
  return new Date(proposal.expiresAt).getTime() < Date.now()
}

/**
 * Returns true when a proposal is in a state that can be dismissed.
 * Includes terminal statuses and approved-but-expired proposals.
 */
function isProposalDismissable(proposal: ApiProposal): boolean {
  const status = normalizeProposalStatus(proposal.status)
  return (
    status === 'Applied' ||
    status === 'Rejected' ||
    status === 'Failed' ||
    status === 'Expired' ||
    (status === 'Approved' && isProposalExpired(proposal))
  )
}

async function handleDismissProposal(proposalId: string) {
  try {
    proposalActionBusyId.value = proposalId
    const result = await automationApi.dismissProposals([proposalId])
    if (result.dismissed > 0) {
      proposals.value = proposals.value.filter((p) => p.id !== proposalId)
      toast.success('Proposal dismissed.')
    }
  } catch (e: unknown) {
    toast.error(getErrorDisplay(e, 'Failed to dismiss proposal').message)
  } finally {
    proposalActionBusyId.value = null
  }
}

function reviewStatusClass(proposal: ApiProposal): string {
  const normalized = normalizeProposalStatus(proposal.status)
  if (normalized === 'PendingReview') return 'td-review-status--pending'
  if (normalized === 'Approved') {
    return isProposalExpired(proposal) ? 'td-review-status--expired' : 'td-review-status--approved'
  }
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

function reviewStatusLabel(proposal: ApiProposal): string {
  const normalized = normalizeProposalStatus(proposal.status)
  if (normalized === 'Approved' && isProposalExpired(proposal)) {
    return 'Expired (was approved)'
  }
  return statusLabels[normalized] ?? normalized
}

function riskLevelClass(riskLevel: ApiProposal['riskLevel']): string {
  const normalized = normalizeProposalRiskLevel(riskLevel)
  if (normalized === 'Low') return 'td-risk--low'
  if (normalized === 'Medium') return 'td-risk--medium'
  if (normalized === 'High') return 'td-risk--high'
  if (normalized === 'Critical') return 'td-risk--critical'
  return 'td-risk--low'
}

async function loadBoardOptions() {
  try {
    loadingBoards.value = true
    availableBoards.value = await boardsApi.getBoards(undefined, true)
  } catch {
    // Board options are non-critical; proposals still work without them.
  } finally {
    loadingBoards.value = false
  }
}

function applyBoardFilter(boardId: string) {
  const trimmed = boardId.trim()
  boardFilterInput.value = ''
  if (trimmed) {
    void router.push({ name: 'workspace-review', query: { boardId: trimmed } })
  } else {
    void router.push({ name: 'workspace-review' })
  }
}

const dismissableProposalIds = computed(() =>
  proposals.value
    .filter((p) => isProposalDismissable(p))
    .filter((p) => matchesActiveBoardFilter(p.boardId))
    .map((p) => p.id),
)

async function handleDismissApplied() {
  const ids = dismissableProposalIds.value
  if (ids.length === 0) {
    toast.info('No completed proposals to clear.')
    return
  }

  try {
    const result = await automationApi.dismissProposals(ids)
    if (result.dismissed === ids.length) {
      // All requested proposals were dismissed; safe to remove them all locally
      const dismissedSet = new Set(ids)
      proposals.value = proposals.value.filter((p) => !dismissedSet.has(p.id))
    } else {
      // Server dismissed fewer than requested (possible clock skew on expiry checks).
      // Reload to get authoritative state rather than guessing which ones were dismissed.
      await loadProposals()
    }
    toast.success(`Cleared ${result.dismissed} completed proposal${result.dismissed === 1 ? '' : 's'}.`)
  } catch (e: unknown) {
    toast.error(getErrorDisplay(e, 'Failed to clear proposals').message)
  }
}

function clearBoardFilter() {
  boardFilterInput.value = ''
  void router.push({ name: 'workspace-review' })
}

onMounted(() => {
  void loadBoardOptions()
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
  <div class="td-review" role="region" aria-label="Proposal review">
    <header class="td-panel td-review__hero">
      <div class="td-review__hero-copy">
        <span class="td-review__eyebrow" aria-hidden="true">Review</span>
        <h1 class="td-page-title">Review</h1>
        <p class="td-review__subtitle">
          Nothing changes on a board until you approve it here.
        </p>
        <p v-if="activeBoardFilter" class="td-review__board-filter">
          Showing proposals for <strong>{{ activeBoardName }}</strong>.
          <button class="td-btn td-btn--link td-btn--sm" @click="clearBoardFilter">Show all boards</button>
        </p>
      </div>

      <div class="td-review__board-selector">
        <InputAssistField
          v-model="boardFilterInput"
          :options="boardOptions"
          aria-label="Filter by board"
          placeholder="Filter proposals by board..."
          no-results-text="No matching boards."
          :disabled="loadingBoards"
          @select="(option) => applyBoardFilter(option.value)"
        />
      </div>

      <div class="td-review__hero-actions">
        <label class="td-review__toggle">
          <input v-model="showCompleted" type="checkbox" class="td-review__toggle-input" />
          <span class="td-review__toggle-label">Show completed</span>
        </label>
        <button
          class="td-btn td-btn--secondary"
          :disabled="dismissableProposalIds.length === 0"
          @click="handleDismissApplied"
        >
          Clear completed ({{ dismissableProposalIds.length }})
        </button>
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
      description="Review is the approval step. Taskdeck proposes changes first, then waits for your decision before anything is applied."
    >
      <template #actions>
        <button class="td-btn td-btn--secondary td-btn--sm" @click="openInbox">Open Inbox</button>
        <button class="td-btn td-btn--secondary td-btn--sm" @click="openRoute('/workspace/boards')">Open Boards</button>
      </template>
    </WorkspaceHelpCallout>

    <section class="td-review__summary" aria-label="Review statistics">
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

    <section v-else class="td-review__list" aria-label="Proposals awaiting review">
      <article
        v-for="proposal in visibleProposals"
        :id="`proposal-${proposal.id}`"
        :key="proposal.id"
        class="td-panel td-review-card"
      >
        <!-- Always visible: title, status badge, risk level (color-coded), meta -->
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
          <span :class="['td-review-status', reviewStatusClass(proposal)]">
            {{ reviewStatusLabel(proposal) }}
          </span>
        </div>

        <!-- Impact cue always visible -->
        <div class="td-review-card__presentation">
          <span class="td-review-cue">{{ impactSummary(proposal) }}</span>
        </div>

        <!-- Action footer — rendered before detail sections so it stays visible without scrolling -->
        <div class="td-review-card__actions">
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
            v-else-if="normalizeProposalStatus(proposal.status) === 'Approved' && !isProposalExpired(proposal)"
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
          <div
            v-else-if="normalizeProposalStatus(proposal.status) === 'Approved' && isProposalExpired(proposal)"
            class="td-review-card__flow-steps"
            role="list"
            aria-label="Expired proposal"
          >
            <span class="td-review-card__flow-step td-review-card__flow-step--expired" role="listitem">
              <span class="td-review-card__flow-step-num" aria-hidden="true">1</span>
              Approved
            </span>
            <span class="td-review-card__flow-arrow" aria-hidden="true">&#8594;</span>
            <span class="td-review-card__flow-step td-review-card__flow-step--expired" role="listitem">
              <span class="td-review-card__flow-step-num" aria-hidden="true">!</span>
              Expired
            </span>
          </div>
          <div
            v-else-if="normalizeProposalStatus(proposal.status) === 'Expired'"
            class="td-review-card__flow-steps"
            role="list"
            aria-label="Expired proposal"
          >
            <span class="td-review-card__flow-step td-review-card__flow-step--expired" role="listitem">
              Expired before review
            </span>
          </div>

          <button class="td-btn td-btn--secondary td-btn--sm" @click="handleToggleDiff(proposal.id)">
            {{ selectedDiffProposalId === proposal.id ? 'Hide Diff' : 'View Diff' }}
          </button>
          <button
            class="td-btn td-btn--primary td-btn--sm"
            :disabled="proposalActionBusyId === proposal.id || normalizeProposalStatus(proposal.status) !== 'PendingReview'"
            @click="handleApproveProposal(proposal.id)"
          >
            Approve for board
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
            :disabled="proposalActionBusyId === proposal.id || normalizeProposalStatus(proposal.status) !== 'Approved' || isProposalExpired(proposal)"
            @click="handleExecuteProposal(proposal.id)"
          >
            Apply to board
          </button>
          <button
            v-if="isProposalDismissable(proposal)"
            class="td-btn td-btn--secondary td-btn--sm"
            :disabled="proposalActionBusyId === proposal.id"
            @click="handleDismissProposal(proposal.id)"
          >
            Dismiss
          </button>
        </div>

        <!-- Collapsible details section — below actions so expanding them never pushes actions out of view -->
        <div v-if="affectedEntities(proposal).length > 0 || operationHeadlines(proposal).length > 0 || hasProvenanceContext(proposal)" class="td-review-card__details">
          <!-- Collapsible: Affected cards (count badge, expandable) -->
          <div v-if="affectedEntities(proposal).length > 0" class="td-review-card__collapsible">
            <button
              class="td-review-card__collapse-toggle"
              :aria-expanded="isSectionExpanded(proposal.id, 'entities')"
              @click="toggleSection(proposal.id, 'entities')"
            >
              <span class="td-review-card__collapse-icon" aria-hidden="true" :class="{ 'td-review-card__collapse-icon--open': isSectionExpanded(proposal.id, 'entities') }">&#9654;</span>
              <span class="td-review-card__section-label">Affected cards</span>
              <span class="td-review-card__count-badge">{{ affectedEntities(proposal).length }}</span>
            </button>
            <div v-if="isSectionExpanded(proposal.id, 'entities')" class="td-review-card__entity-list">
              <span
                v-for="entity in affectedEntities(proposal)"
                :key="`${proposal.id}-${entity.entityType}-${entity.entityId ?? 'none'}`"
                class="td-review-entity-chip"
              >
                {{ entity.label }} · {{ entity.changeCount }} change{{ entity.changeCount === 1 ? '' : 's' }}
              </span>
            </div>
          </div>

          <!-- Collapsible: Planned changes -->
          <div v-if="operationHeadlines(proposal).length > 0" class="td-review-card__collapsible">
            <button
              class="td-review-card__collapse-toggle"
              :aria-expanded="isSectionExpanded(proposal.id, 'operations')"
              @click="toggleSection(proposal.id, 'operations')"
            >
              <span class="td-review-card__collapse-icon" aria-hidden="true" :class="{ 'td-review-card__collapse-icon--open': isSectionExpanded(proposal.id, 'operations') }">&#9654;</span>
              <span class="td-review-card__section-label">Planned changes</span>
              <span class="td-review-card__count-badge">{{ operationHeadlines(proposal).length }}</span>
            </button>
            <div v-if="isSectionExpanded(proposal.id, 'operations')">
              <ul class="td-review-card__operation-list">
                <li
                  v-for="(headline, headlineIndex) in operationHeadlines(proposal)"
                  :key="`${proposal.id}-${headlineIndex}-${headline}`"
                >
                  {{ headline }}
                </li>
              </ul>
            </div>
          </div>

          <!-- Collapsible: Provenance / Technical details -->
          <div v-if="hasProvenanceContext(proposal)" class="td-review-card__collapsible">
            <button
              class="td-review-card__collapse-toggle"
              :aria-expanded="isSectionExpanded(proposal.id, 'provenance')"
              @click="toggleSection(proposal.id, 'provenance')"
            >
              <span class="td-review-card__collapse-icon" aria-hidden="true" :class="{ 'td-review-card__collapse-icon--open': isSectionExpanded(proposal.id, 'provenance') }">&#9654;</span>
              <span class="td-review-card__section-label">Technical details</span>
              <span class="td-provenance-chip">Capture-linked</span>
            </button>
            <div v-if="isSectionExpanded(proposal.id, 'provenance')" class="td-review-card__provenance-content">
              <span
                v-if="proposal.correlationId.trim().length > 0"
                class="td-review-card__provenance-meta"
                :title="proposal.correlationId.trim()"
                :aria-label="`Triage run: ${proposal.correlationId.trim()}`"
                tabindex="0"
              >
                Triage run: {{ shortCorrelationId(proposal.correlationId) }}
              </span>
              <!-- Links dropdown -->
              <div class="td-review-card__links-dropdown-wrapper">
                <button
                  class="td-btn td-btn--secondary td-btn--sm"
                  :aria-expanded="openLinkDropdown === proposal.id"
                  @click="toggleLinkDropdown(proposal.id)"
                  @blur="closeLinkDropdown"
                >
                  Links &#9662;
                </button>
                <div
                  v-if="openLinkDropdown === proposal.id"
                  class="td-review-card__links-dropdown"
                  role="menu"
                >
                  <router-link
                    class="td-review-card__links-dropdown-item"
                    role="menuitem"
                    :to="captureHrefForProposal(proposal)"
                    @mousedown.prevent
                  >
                    Open Capture
                  </router-link>
                  <router-link
                    class="td-review-card__links-dropdown-item"
                    role="menuitem"
                    :to="proposalHref(proposal)"
                    @mousedown.prevent
                  >
                    Review Link
                  </router-link>
                  <button
                    v-if="proposal.boardId"
                    class="td-review-card__links-dropdown-item"
                    role="menuitem"
                    @mousedown.prevent
                    @click="openBoard(proposal.boardId)"
                  >
                    Open Board
                  </button>
                </div>
              </div>
            </div>
          </div>
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
  display: flex;
  align-items: center;
  gap: var(--td-space-2);
}

.td-review__board-selector {
  max-width: 320px;
}

.td-review__hero-actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--td-space-2);
  justify-content: flex-end;
  align-items: center;
}

.td-review__toggle {
  display: flex;
  align-items: center;
  gap: var(--td-space-2);
  cursor: pointer;
  user-select: none;
}

.td-review__toggle-input {
  accent-color: var(--td-color-primary);
  width: 16px;
  height: 16px;
  cursor: pointer;
}

.td-review__toggle-label {
  font-size: var(--td-font-sm);
  font-weight: 600;
  color: var(--td-text-secondary);
  white-space: nowrap;
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

/* Collapsible sections */
.td-review-card__collapsible {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
}

.td-review-card__collapse-toggle {
  display: flex;
  align-items: center;
  gap: var(--td-space-2);
  background: none;
  border: none;
  padding: var(--td-space-1) 0;
  cursor: pointer;
  color: var(--td-text-primary);
  font-family: inherit;
  text-align: left;
}

.td-review-card__collapse-toggle:hover {
  color: var(--td-color-primary);
}

.td-review-card__collapse-icon {
  font-size: 0.625rem;
  transition: transform 0.15s ease;
  display: inline-block;
  color: var(--td-text-secondary);
}

.td-review-card__collapse-icon--open {
  transform: rotate(90deg);
}

.td-review-card__count-badge {
  border-radius: var(--td-radius-pill, 999px);
  background: var(--td-surface-container-highest);
  color: var(--td-text-secondary);
  font-size: var(--td-font-xs);
  font-weight: 700;
  padding: 0.0625rem 0.4375rem;
  min-width: 1.25rem;
  text-align: center;
}

/* Provenance content inside collapsible */
.td-review-card__provenance-content {
  display: flex;
  flex-wrap: wrap;
  gap: var(--td-space-2);
  align-items: center;
  padding-left: calc(0.625rem + var(--td-space-2));
}

/* Links dropdown */
.td-review-card__links-dropdown-wrapper {
  position: relative;
  display: inline-block;
}

.td-review-card__links-dropdown {
  position: absolute;
  top: 100%;
  left: 0;
  z-index: 10;
  min-width: 160px;
  margin-top: var(--td-space-1);
  padding: var(--td-space-1) 0;
  background: var(--td-surface-container);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-md);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
  display: flex;
  flex-direction: column;
}

.td-review-card__links-dropdown-item {
  display: block;
  padding: var(--td-space-2) var(--td-space-3);
  font-size: var(--td-font-sm);
  color: var(--td-text-primary);
  text-decoration: none;
  background: none;
  border: none;
  text-align: left;
  cursor: pointer;
  font-family: inherit;
}

.td-review-card__links-dropdown-item:hover {
  background: var(--td-surface-container-high);
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
  color: var(--td-color-error);
  background: var(--td-color-error-light);
  border-color: var(--td-color-error);
}

.td-review-status--secondary {
  color: var(--td-text-secondary);
  background: var(--td-surface-container-high);
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
  max-height: 12rem;
  overflow-y: auto;
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
  max-height: 12rem;
  overflow-y: auto;
}

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

/* Details section wrapper (collapsibles live here, below the action footer) */
.td-review-card__details {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
  border-top: 1px solid var(--td-border-ghost);
  padding-top: var(--td-space-2);
}

.td-review-card__action-cue--approved {
  color: var(--td-color-success);
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

@media (max-width: 640px) {
  .td-review {
    gap: var(--td-space-3);
  }

  .td-review__hero {
    gap: var(--td-space-4);
    padding: var(--td-space-4);
  }

  .td-review__board-selector {
    max-width: 100%;
    width: 100%;
  }

  .td-review__hero-actions {
    flex-direction: column;
    width: 100%;
  }

  .td-review__hero-actions .td-btn {
    width: 100%;
    min-height: 44px;
    justify-content: center;
  }

  .td-review__summary {
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: var(--td-space-2);
  }

  .td-review-summary-card {
    padding: var(--td-space-3);
  }

  .td-review-summary-card__value {
    font-size: var(--td-font-xl);
  }

  .td-review-summary-card__helper {
    display: none;
  }

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

  .td-review-card__diff {
    font-size: var(--td-font-xs); /* Avoid sub-16px sizes that trigger iOS auto-zoom */
    padding: var(--td-space-2);
  }

  .td-review-card__provenance-content {
    flex-direction: column;
    align-items: stretch;
    padding-left: 0;
  }

  .td-review-card__links-dropdown {
    position: static;
    box-shadow: none;
    border: 1px solid var(--td-border-default);
    margin-top: var(--td-space-1);
  }

  .td-review-empty__actions {
    flex-direction: column;
  }

  .td-review-empty__actions .td-btn {
    width: 100%;
    min-height: 44px;
    justify-content: center;
  }

  .td-review__board-filter {
    flex-direction: column;
    gap: var(--td-space-1);
  }
}
</style>
