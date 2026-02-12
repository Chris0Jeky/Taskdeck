<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useQueueStore } from '../store/queueStore'
import { getQueueTotal, normalizeQueueStatus } from '../utils/queue'
import type { QueueStatus } from '../types/queue'

const queue = useQueueStore()

const activeTab = ref<'queue' | 'proposals'>('queue')
const statusFilter = ref('Pending')
const newRequestType = ref('')
const newPayload = ref('')
const showComposer = ref(false)
const submitting = ref(false)

const statusTabs = ['Pending', 'Processing', 'Completed', 'Failed', 'Cancelled']

onMounted(() => {
  queue.fetchByStatus(statusFilter.value)
  queue.fetchStats()
})

async function handleStatusChange(status: string) {
  statusFilter.value = status
  await queue.fetchByStatus(status)
}

async function handleSubmitRequest() {
  if (!newRequestType.value.trim() || !newPayload.value.trim()) return
  try {
    submitting.value = true
    await queue.submitRequest({
      requestType: newRequestType.value.trim(),
      payload: newPayload.value.trim(),
    })
    newRequestType.value = ''
    newPayload.value = ''
    showComposer.value = false
  } catch { /* handled by store */ } finally {
    submitting.value = false
  }
}

async function handleCancel(requestId: string) {
  if (confirm('Cancel this request?')) {
    await queue.cancelRequest(requestId)
  }
}

async function handleProcessNext() {
  await queue.processNext()
  await queue.fetchByStatus(statusFilter.value)
  await queue.fetchStats()
}

function formatDate(d: string | null): string {
  if (!d) return '—'
  return new Date(d).toLocaleString()
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

    <!-- Stats Cards -->
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

    <!-- Tab Bar -->
    <div class="td-tabs">
      <button
        :class="['td-tab', { 'td-tab--active': activeTab === 'queue' }]"
        @click="activeTab = 'queue'"
      >Queue</button>
      <button
        :class="['td-tab', { 'td-tab--active': activeTab === 'proposals' }]"
        @click="activeTab = 'proposals'"
      >Proposals</button>
    </div>

    <!-- Queue Tab -->
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
          <button class="td-btn td-btn--secondary td-btn--sm" @click="handleProcessNext">
            ▶ Process Next
          </button>
          <button class="td-btn td-btn--primary td-btn--sm" @click="showComposer = !showComposer">
            {{ showComposer ? 'Cancel' : '+ New Request' }}
          </button>
        </div>
      </div>

      <!-- Composer -->
      <div v-if="showComposer" class="td-composer">
        <div class="td-form-group">
          <label class="td-label">Request Type</label>
          <input v-model="newRequestType" type="text" class="td-input" placeholder="e.g., board-update, card-create" />
        </div>
        <div class="td-form-group">
          <label class="td-label">Payload (JSON)</label>
          <textarea v-model="newPayload" class="td-textarea" rows="4" placeholder='{"boardId": "...", "action": "..."}'></textarea>
        </div>
        <button class="td-btn td-btn--primary" @click="handleSubmitRequest" :disabled="submitting">
          {{ submitting ? 'Submitting...' : 'Submit Request' }}
        </button>
      </div>

      <!-- Loading -->
      <div v-if="queue.loading" class="td-loading">Loading...</div>

      <!-- Request List -->
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

    <!-- Proposals Tab (Placeholder) -->
    <div v-if="activeTab === 'proposals'" class="td-proposals-panel">
      <div class="td-placeholder">
        <div class="td-placeholder__icon">📋</div>
        <h3>Automation Proposals</h3>
        <p>Proposal review and approval workflow will be available when the backend proposal endpoints are implemented.</p>
        <p class="td-placeholder__detail">
          This surface will support:
          proposal creation, diff preview, approve/reject/edit flows, and risk-level assessment.
        </p>
      </div>
    </div>
  </div>
</template>

<style scoped>
.td-automation { max-width: 900px; }
.td-page-title { font-size: var(--td-font-2xl); font-weight: 700; margin-bottom: var(--td-space-6); color: var(--td-text-primary); }
.td-stats-grid { display: grid; grid-template-columns: repeat(5, 1fr); gap: var(--td-space-3); margin-bottom: var(--td-space-6); }
.td-stat-card { background: var(--td-surface-primary); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-lg); padding: var(--td-space-4); text-align: center; }
.td-stat-value { font-size: var(--td-font-2xl); font-weight: 700; color: var(--td-text-primary); }
.td-stat-label { font-size: var(--td-font-xs); color: var(--td-text-tertiary); text-transform: uppercase; margin-top: var(--td-space-1); }
.td-tabs { display: flex; gap: 0; margin-bottom: var(--td-space-4); border-bottom: 2px solid var(--td-border-default); }
.td-tab { padding: var(--td-space-2) var(--td-space-4); border: none; background: transparent; font-size: var(--td-font-sm); font-weight: 500; cursor: pointer; color: var(--td-text-secondary); border-bottom: 2px solid transparent; margin-bottom: -2px; }
.td-tab--active { color: var(--td-color-primary); border-bottom-color: var(--td-color-primary); }
.td-queue-panel { background: var(--td-surface-primary); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-lg); padding: var(--td-space-4); }
.td-queue-toolbar { display: flex; justify-content: space-between; align-items: center; margin-bottom: var(--td-space-4); flex-wrap: wrap; gap: var(--td-space-2); }
.td-status-tabs { display: flex; gap: var(--td-space-1); }
.td-status-tab { padding: var(--td-space-1) var(--td-space-3); border: 1px solid var(--td-border-default); background: var(--td-surface-secondary); border-radius: var(--td-radius-md); font-size: var(--td-font-xs); cursor: pointer; }
.td-status-tab--active { background: var(--td-color-primary-light); border-color: var(--td-color-primary); color: var(--td-color-primary); }
.td-queue-actions { display: flex; gap: var(--td-space-2); }
.td-composer { background: var(--td-surface-secondary); border-radius: var(--td-radius-md); padding: var(--td-space-4); margin-bottom: var(--td-space-4); display: flex; flex-direction: column; gap: var(--td-space-3); }
.td-form-group { display: flex; flex-direction: column; gap: var(--td-space-1); }
.td-label { font-size: var(--td-font-sm); font-weight: 500; color: var(--td-text-secondary); }
.td-input { padding: var(--td-space-2) var(--td-space-3); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-md); font-size: var(--td-font-sm); }
.td-input:focus { outline: none; border-color: var(--td-border-focus); box-shadow: var(--td-focus-ring); }
.td-textarea { padding: var(--td-space-2) var(--td-space-3); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-md); font-size: var(--td-font-sm); font-family: monospace; resize: vertical; }
.td-textarea:focus { outline: none; border-color: var(--td-border-focus); box-shadow: var(--td-focus-ring); }
.td-btn { padding: var(--td-space-2) var(--td-space-4); border: none; border-radius: var(--td-radius-md); font-size: var(--td-font-sm); font-weight: 600; cursor: pointer; }
.td-btn--sm { padding: var(--td-space-1) var(--td-space-3); font-size: var(--td-font-xs); }
.td-btn--primary { background: var(--td-color-primary); color: var(--td-text-inverse); }
.td-btn--primary:hover:not(:disabled) { background: var(--td-color-primary-hover); }
.td-btn--secondary { background: var(--td-surface-tertiary); color: var(--td-text-primary); border: 1px solid var(--td-border-default); }
.td-btn--secondary:hover { background: var(--td-surface-hover); }
.td-btn--danger { background: var(--td-color-error); color: var(--td-text-inverse); }
.td-btn:disabled { opacity: 0.6; cursor: not-allowed; }
.td-loading { text-align: center; padding: var(--td-space-6); color: var(--td-text-secondary); }
.td-empty { text-align: center; padding: var(--td-space-6); color: var(--td-text-tertiary); }
.td-request-list { display: flex; flex-direction: column; gap: var(--td-space-2); }
.td-request-card { border: 1px solid var(--td-border-default); border-radius: var(--td-radius-md); padding: var(--td-space-3); }
.td-request-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: var(--td-space-2); }
.td-request-type { font-weight: 600; font-size: var(--td-font-sm); }
.td-status-badge { font-size: var(--td-font-xs); padding: 1px 8px; border: 1px solid; border-radius: var(--td-radius-sm); font-weight: 500; }
.td-request-meta { font-size: var(--td-font-xs); color: var(--td-text-tertiary); display: flex; gap: var(--td-space-3); }
.td-request-error { font-size: var(--td-font-sm); color: var(--td-color-error); margin-top: var(--td-space-2); }
.td-request-actions { margin-top: var(--td-space-2); }
.td-proposals-panel { background: var(--td-surface-primary); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-lg); padding: var(--td-space-8); }
.td-placeholder { text-align: center; }
.td-placeholder__icon { font-size: 3rem; margin-bottom: var(--td-space-4); }
.td-placeholder h3 { font-size: var(--td-font-lg); font-weight: 600; margin-bottom: var(--td-space-2); }
.td-placeholder p { font-size: var(--td-font-sm); color: var(--td-text-secondary); margin-bottom: var(--td-space-2); }
.td-placeholder__detail { font-size: var(--td-font-xs); color: var(--td-text-tertiary); }
</style>
