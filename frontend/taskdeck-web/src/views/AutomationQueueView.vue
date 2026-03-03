<script setup lang="ts">
import { nextTick, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { automationApi } from '../api/automationApi'
import { useQueueStore } from '../store/queueStore'
import { useToastStore } from '../store/toastStore'
import { getQueueTotal, normalizeQueueStatus } from '../utils/queue'
import { createRequestId } from '../utils/requestId'
import { normalizeProposalRiskLevel, normalizeProposalSourceType, normalizeProposalStatus } from '../utils/automation'
import type { QueueStatus } from '../types/queue'
import type { Proposal as ApiProposal } from '../types/automation'
import { getErrorDisplay } from '../composables/useErrorMapper'

type UiProposalStatus = 'pending-review' | 'approved' | 'rejected' | 'applied' | 'failed'

const queue = useQueueStore()
const toast = useToastStore()
const route = useRoute()
const router = useRouter()

const activeTab = ref<'queue' | 'proposals'>('queue')
const statusFilter = ref('Pending')
// Most manual queue requests are instruction-based; default accordingly.
const newRequestType = ref('instruction')
const newBoardId = ref('')
const newPayload = ref('')
const showComposer = ref(false)
const submitting = ref(false)

const proposals = ref<ApiProposal[]>([])
const proposalsLoading = ref(false)
const proposalActionBusyId = ref<string | null>(null)
const selectedDiffProposalId = ref<string | null>(null)
const selectedDiff = ref<string | null>(null)

const statusTabs = ['Pending', 'Processing', 'Completed', 'Failed', 'Cancelled']

function syncTabFromRoute() {
  activeTab.value = route.name === 'workspace-automations-proposals' ? 'proposals' : 'queue'
}

function toProposalStatus(status: ApiProposal['status']): UiProposalStatus {
  const normalized = normalizeProposalStatus(status)
  const mapping: Record<string, UiProposalStatus> = {
    PendingReview: 'pending-review',
    Approved: 'approved',
    Rejected: 'rejected',
    Applied: 'applied',
    Failed: 'failed',
    Expired: 'failed',
  }
  return mapping[normalized] ?? 'pending-review'
}

async function loadQueueData() {
  await queue.fetchByStatus(statusFilter.value)
  await queue.fetchStats()
}

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

onMounted(() => {
  syncTabFromRoute()

  loadQueueData().catch(() => {
    // Store handles queue errors.
  })

  if (activeTab.value === 'proposals') {
    loadProposals().catch(() => {
      // Proposal load errors handled locally.
    })
  }
})

watch(() => route.name, () => {
  syncTabFromRoute()
})

watch(() => route.hash, () => {
  if (activeTab.value !== 'proposals' || proposals.value.length === 0) {
    return
  }

  void scrollToProposalFromHash()
})

watch(activeTab, (tab) => {
  if (tab === 'proposals') {
    void loadProposals()
    if (route.name !== 'workspace-automations-proposals') {
      void router.replace({ name: 'workspace-automations-proposals' })
    }
  } else if (route.name !== 'workspace-automations-queue') {
    void router.replace({ name: 'workspace-automations-queue' })
  }
})

async function handleStatusChange(status: string) {
  statusFilter.value = status
  try {
    await queue.fetchByStatus(status)
  } catch {
    // Store handles toast + error state.
  }
}

async function handleSubmitRequest() {
  if (!newRequestType.value.trim() || !newPayload.value.trim()) return
  const trimmedBoardId = newBoardId.value.trim()
  try {
    submitting.value = true
    await queue.submitRequest({
      requestType: newRequestType.value.trim(),
      payload: newPayload.value.trim(),
      ...(trimmedBoardId ? { boardId: trimmedBoardId } : {}),
    })
    newRequestType.value = 'instruction'
    newBoardId.value = ''
    newPayload.value = ''
    showComposer.value = false
  } catch {
    // Store handles toast + error state.
  } finally {
    submitting.value = false
  }
}

async function handleCancel(requestId: string) {
  if (confirm('Cancel this request?')) {
    try {
      await queue.cancelRequest(requestId)
    } catch {
      // Store handles toast + error state.
    }
  }
}

async function handleProcessNext() {
  try {
    await queue.processNext()
    await queue.fetchByStatus(statusFilter.value)
    await queue.fetchStats()
  } catch {
    // Store handles toast + error state.
  }
}

async function handleApproveProposal(proposalId: string) {
  try {
    proposalActionBusyId.value = proposalId
    const updated = await automationApi.approveProposal(proposalId)
    proposals.value = proposals.value.map(p => (p.id === proposalId ? updated : p))
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
    proposals.value = proposals.value.map(p => (p.id === proposalId ? updated : p))
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
    proposals.value = proposals.value.map(p => (p.id === proposalId ? updated : p))
    toast.success('Proposal executed')
  } catch (e: unknown) {
    toast.error(getErrorDisplay(e, 'Failed to execute proposal').message)
  } finally {
    proposalActionBusyId.value = null
  }
}

async function handleToggleDiff(proposalId: string) {
  if (selectedDiffProposalId.value === proposalId) {
    selectedDiffProposalId.value = null
    selectedDiff.value = null
    return
  }

  try {
    selectedDiffProposalId.value = proposalId
    selectedDiff.value = await automationApi.getProposalDiff(proposalId)
  } catch (e: unknown) {
    selectedDiffProposalId.value = null
    selectedDiff.value = null
    toast.error(getErrorDisplay(e, 'Failed to load proposal diff').message)
  }
}

function formatDate(d: string | null): string {
  if (!d) return '-'
  return new Date(d).toLocaleString()
}

function proposalHref(proposalId: string): string {
  return `/workspace/automations/proposals#proposal-${encodeURIComponent(proposalId)}`
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

function statusColor(status: QueueStatus | number): string {
  const normalized = normalizeQueueStatus(status)
  const colors: Record<string, string> = {
    Pending: 'var(--td-color-warning)',
    Processing: 'var(--td-color-info)',
    Completed: 'var(--td-color-success)',
    Failed: 'var(--td-color-error)',
    Cancelled: 'var(--td-text-tertiary)',
  }
  return colors[normalized] ?? 'var(--td-text-secondary)'
}
</script>

<template>
  <div class="td-automation">
    <h1 class="td-page-title">Automations</h1>

    <div v-if="queue.stats" class="td-stats-grid">
      <div class="td-stat-card">
        <div class="td-stat-value">{{ queue.stats.pendingCount }}</div>
        <div class="td-stat-label">Pending</div>
      </div>
      <div class="td-stat-card">
        <div class="td-stat-value">{{ queue.stats.processingCount }}</div>
        <div class="td-stat-label">Processing</div>
      </div>
      <div class="td-stat-card">
        <div class="td-stat-value">{{ queue.stats.completedCount }}</div>
        <div class="td-stat-label">Completed</div>
      </div>
      <div class="td-stat-card">
        <div class="td-stat-value">{{ queue.stats.failedCount }}</div>
        <div class="td-stat-label">Failed</div>
      </div>
      <div class="td-stat-card">
        <div class="td-stat-value">{{ getQueueTotal(queue.stats) }}</div>
        <div class="td-stat-label">Total</div>
      </div>
    </div>

    <div class="td-tabs">
      <button :class="['td-tab', { 'td-tab--active': activeTab === 'queue' }]" @click="activeTab = 'queue'">Queue</button>
      <button :class="['td-tab', { 'td-tab--active': activeTab === 'proposals' }]" @click="activeTab = 'proposals'">Proposals</button>
      <button class="td-tab td-tab--link" @click="router.push('/workspace/automations/chat')">Open Chat</button>
    </div>

    <div v-if="activeTab === 'queue'" class="td-queue-panel">
      <div class="td-queue-toolbar">
        <div class="td-status-tabs">
          <button
            v-for="s in statusTabs"
            :key="s"
            :class="['td-status-tab', { 'td-status-tab--active': statusFilter === s }]"
            @click="handleStatusChange(s)"
          >{{ s }}</button>
        </div>
        <div class="td-queue-actions">
          <button class="td-btn td-btn--secondary td-btn--sm" @click="handleProcessNext">Process Next</button>
          <button class="td-btn td-btn--primary td-btn--sm" @click="showComposer = !showComposer">
            {{ showComposer ? 'Cancel' : '+ New Request' }}
          </button>
        </div>
      </div>

      <div v-if="showComposer" class="td-composer">
        <div class="td-form-group">
          <label class="td-label">Request Type (advanced)</label>
          <input v-model="newRequestType" type="text" class="td-input" placeholder="instruction" />
          <div class="td-helper">
            Leave this as <strong>instruction</strong> for most manual requests. Capture triage requests are created via
            <strong>Inbox -> Start Triage</strong>.
          </div>
        </div>
        <div class="td-form-group">
          <label class="td-label">Board ID (optional)</label>
          <input
            v-model="newBoardId"
            type="text"
            class="td-input"
            placeholder="board-123 (required for board-scoped instructions)"
          />
          <div class="td-helper">
            Board-scoped instructions require a <strong>Board ID</strong> (for example board rename/description updates
            and board-local move operations).
          </div>
        </div>
        <div class="td-form-group">
          <label class="td-label">Instruction</label>
          <textarea
            v-model="newPayload"
            class="td-textarea"
            rows="6"
            placeholder='create card "Write MVP demo script"'
          ></textarea>
          <div class="td-helper">
            Supported patterns include: <strong>create card "title"</strong>, <strong>rename board to "name"</strong>,
            <strong>update board description "value"</strong>, <strong>move column "name" to position {n}</strong>,
            <strong>update card {id} title|description "value"</strong>, <strong>move card {id} to column "name"</strong>.
            Use <strong>Inbox -> Start Triage</strong> for capture requests.
          </div>
        </div>
        <button class="td-btn td-btn--primary" @click="handleSubmitRequest" :disabled="submitting">
          {{ submitting ? 'Submitting...' : 'Submit Request' }}
        </button>
      </div>

      <div v-if="queue.loading" class="td-loading">Loading...</div>

      <div v-else class="td-request-list">
        <div v-if="queue.requests.length === 0" class="td-empty">No requests found.</div>
        <div v-for="req in queue.requests" :key="req.id" class="td-request-card">
          <div class="td-request-header">
            <span class="td-request-type">{{ req.requestType }}</span>
            <span class="td-status-badge" :style="{ color: statusColor(req.status), borderColor: statusColor(req.status) }">
              {{ normalizeQueueStatus(req.status) }}
            </span>
          </div>
          <div class="td-request-meta">
            <span>Created: {{ formatDate(req.createdAt) }}</span>
            <span v-if="req.processedAt">Processed: {{ formatDate(req.processedAt) }}</span>
          </div>
          <div v-if="req.errorMessage" class="td-request-error">{{ req.errorMessage }}</div>
          <div class="td-request-actions">
            <button
              v-if="normalizeQueueStatus(req.status) === 'Pending'"
              class="td-btn td-btn--danger td-btn--sm"
              @click="handleCancel(req.id)"
            >Cancel</button>
          </div>
        </div>
      </div>
    </div>

    <div v-if="activeTab === 'proposals'" class="td-proposals-panel">
      <div class="td-proposals-toolbar">
        <button class="td-btn td-btn--secondary td-btn--sm" @click="loadProposals" :disabled="proposalsLoading">Refresh</button>
      </div>

      <div v-if="proposalsLoading" class="td-loading">Loading proposals...</div>
      <div v-else-if="proposals.length === 0" class="td-empty">No proposals found.</div>

      <div v-else class="td-proposal-list">
        <div
          v-for="proposal in proposals"
          :key="proposal.id"
          :id="`proposal-${proposal.id}`"
          class="td-proposal-card"
        >
          <div class="td-proposal-header">
            <div class="td-proposal-title">{{ proposal.summary }}</div>
            <span class="td-proposal-status">{{ normalizeProposalStatus(proposal.status) }}</span>
          </div>
          <div class="td-proposal-meta">
            <span>Risk: {{ normalizeProposalRiskLevel(proposal.riskLevel) }}</span>
            <span>Created: {{ formatDate(proposal.createdAt) }}</span>
            <span>Source: {{ proposal.sourceType }}</span>
          </div>
          <div v-if="hasProvenanceContext(proposal)" class="td-proposal-provenance">
            <span class="td-provenance-chip">Capture-linked</span>
            <a
              v-if="captureSourceReference(proposal)"
              class="td-btn td-btn--secondary td-btn--sm"
              :href="captureHrefForProposal(proposal)"
            >
              Open Capture
            </a>
            <a class="td-btn td-btn--secondary td-btn--sm" :href="proposalHref(proposal.id)">
              Proposal Anchor
            </a>
            <span v-if="proposal.correlationId.trim().length > 0" class="td-provenance-meta">
              Triage run: {{ proposal.correlationId }}
            </span>
          </div>
          <div class="td-proposal-actions">
            <button class="td-btn td-btn--secondary td-btn--sm" @click="handleToggleDiff(proposal.id)">
              {{ selectedDiffProposalId === proposal.id ? 'Hide Diff' : 'View Diff' }}
            </button>
            <button
              class="td-btn td-btn--primary td-btn--sm"
              :disabled="proposalActionBusyId === proposal.id || toProposalStatus(proposal.status) !== 'pending-review'"
              @click="handleApproveProposal(proposal.id)"
            >
              Approve
            </button>
            <button
              class="td-btn td-btn--danger td-btn--sm"
              :disabled="proposalActionBusyId === proposal.id || toProposalStatus(proposal.status) !== 'pending-review'"
              @click="handleRejectProposal(proposal.id, proposal.riskLevel)"
            >
              Reject
            </button>
            <button
              class="td-btn td-btn--secondary td-btn--sm"
              :disabled="proposalActionBusyId === proposal.id || toProposalStatus(proposal.status) !== 'approved'"
              @click="handleExecuteProposal(proposal.id)"
            >
              Execute
            </button>
          </div>
          <pre v-if="selectedDiffProposalId === proposal.id && selectedDiff" class="td-diff">{{ selectedDiff }}</pre>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.td-automation { max-width: 980px; }
.td-page-title { font-size: var(--td-font-2xl); font-weight: 700; margin-bottom: var(--td-space-6); color: var(--td-text-primary); }
.td-stats-grid { display: grid; grid-template-columns: repeat(5, 1fr); gap: var(--td-space-3); margin-bottom: var(--td-space-6); }
.td-stat-card { background: var(--td-surface-primary); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-lg); padding: var(--td-space-4); text-align: center; }
.td-stat-value { font-size: var(--td-font-2xl); font-weight: 700; color: var(--td-text-primary); }
.td-stat-label { font-size: var(--td-font-xs); color: var(--td-text-tertiary); text-transform: uppercase; margin-top: var(--td-space-1); }
.td-tabs { display: flex; gap: 0; margin-bottom: var(--td-space-4); border-bottom: 2px solid var(--td-border-default); }
.td-tab { padding: var(--td-space-2) var(--td-space-4); border: none; background: transparent; font-size: var(--td-font-sm); font-weight: 500; cursor: pointer; color: var(--td-text-secondary); border-bottom: 2px solid transparent; margin-bottom: -2px; }
.td-tab--active { color: var(--td-color-primary); border-bottom-color: var(--td-color-primary); }
.td-tab--link { margin-left: auto; color: var(--td-color-primary); }
.td-queue-panel, .td-proposals-panel { background: var(--td-surface-primary); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-lg); padding: var(--td-space-4); }
.td-queue-toolbar { display: flex; justify-content: space-between; align-items: center; margin-bottom: var(--td-space-4); flex-wrap: wrap; gap: var(--td-space-2); }
.td-status-tabs { display: flex; gap: var(--td-space-1); }
.td-status-tab { padding: var(--td-space-1) var(--td-space-3); border: 1px solid var(--td-border-default); background: var(--td-surface-secondary); border-radius: var(--td-radius-md); font-size: var(--td-font-xs); cursor: pointer; }
.td-status-tab--active { background: var(--td-color-primary-light); border-color: var(--td-color-primary); color: var(--td-color-primary); }
.td-queue-actions, .td-proposals-toolbar { display: flex; gap: var(--td-space-2); }
.td-composer { background: var(--td-surface-secondary); border-radius: var(--td-radius-md); padding: var(--td-space-4); margin-bottom: var(--td-space-4); display: flex; flex-direction: column; gap: var(--td-space-3); }
.td-form-group { display: flex; flex-direction: column; gap: var(--td-space-1); }
.td-label { font-size: var(--td-font-sm); font-weight: 500; color: var(--td-text-secondary); }
.td-helper { font-size: var(--td-font-xs); color: var(--td-text-tertiary); line-height: 1.2rem; }
.td-input { padding: var(--td-space-2) var(--td-space-3); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-md); font-size: var(--td-font-sm); }
.td-input:focus { outline: none; border-color: var(--td-border-focus); box-shadow: var(--td-focus-ring); }
.td-textarea { padding: var(--td-space-2) var(--td-space-3); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-md); font-size: var(--td-font-sm); font-family: monospace; resize: vertical; }
.td-textarea:focus { outline: none; border-color: var(--td-border-focus); box-shadow: var(--td-focus-ring); }
.td-btn { padding: var(--td-space-2) var(--td-space-4); border: none; border-radius: var(--td-radius-md); font-size: var(--td-font-sm); font-weight: 600; cursor: pointer; }
.td-btn--sm { padding: var(--td-space-1) var(--td-space-3); font-size: var(--td-font-xs); }
.td-btn--primary { background: var(--td-color-primary); color: var(--td-text-inverse); }
.td-btn--primary:hover:not(:disabled) { background: var(--td-color-primary-hover); }
.td-btn--secondary { background: var(--td-surface-tertiary); color: var(--td-text-primary); border: 1px solid var(--td-border-default); }
.td-btn--secondary:hover:not(:disabled) { background: var(--td-surface-hover); }
.td-btn--danger { background: var(--td-color-error); color: var(--td-text-inverse); }
.td-btn:disabled { opacity: 0.6; cursor: not-allowed; }
.td-loading, .td-empty { text-align: center; padding: var(--td-space-6); color: var(--td-text-secondary); }
.td-request-list, .td-proposal-list { display: flex; flex-direction: column; gap: var(--td-space-2); }
.td-request-card, .td-proposal-card { border: 1px solid var(--td-border-default); border-radius: var(--td-radius-md); padding: var(--td-space-3); }
.td-request-header, .td-proposal-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: var(--td-space-2); gap: var(--td-space-2); }
.td-request-type { font-weight: 600; font-size: var(--td-font-sm); }
.td-status-badge { font-size: var(--td-font-xs); padding: 1px 8px; border: 1px solid; border-radius: var(--td-radius-sm); font-weight: 500; }
.td-request-meta, .td-proposal-meta { font-size: var(--td-font-xs); color: var(--td-text-tertiary); display: flex; gap: var(--td-space-3); flex-wrap: wrap; }
.td-request-error { font-size: var(--td-font-sm); color: var(--td-color-error); margin-top: var(--td-space-2); }
.td-request-actions, .td-proposal-actions { margin-top: var(--td-space-2); display: flex; gap: var(--td-space-2); flex-wrap: wrap; }
.td-proposal-title { font-weight: 600; font-size: var(--td-font-sm); }
.td-proposal-provenance {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--td-space-2);
  margin-top: var(--td-space-2);
}
.td-provenance-chip {
  font-size: var(--td-font-xs);
  font-weight: 600;
  border-radius: var(--td-radius-sm);
  padding: 1px 8px;
  color: var(--td-color-primary);
  background: var(--td-color-primary-light);
}
.td-provenance-meta { font-size: var(--td-font-xs); color: var(--td-text-tertiary); }
.td-proposal-status { font-size: var(--td-font-xs); color: var(--td-text-secondary); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-sm); padding: 1px 8px; }
.td-diff { margin-top: var(--td-space-2); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-sm); background: var(--td-surface-secondary); padding: var(--td-space-2); font-size: var(--td-font-xs); white-space: pre-wrap; }
</style>
