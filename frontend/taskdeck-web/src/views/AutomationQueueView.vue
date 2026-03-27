<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useQueueStore } from '../store/queueStore'
import { useToastStore } from '../store/toastStore'
import { boardsApi } from '../api/boardsApi'
import InputAssistField from '../components/common/InputAssistField.vue'
import { buildInputAssistOptions } from '../utils/inputAssist'
import { getQueueTotal, normalizeQueueStatus } from '../utils/queue'
import type { Board } from '../types/board'
import type { QueueStatus } from '../types/queue'

const router = useRouter()
const queue = useQueueStore()
const toast = useToastStore()

const statusFilter = ref('Pending')
const newRequestType = ref('instruction')
const newBoardId = ref('')
const boardDisplayValue = ref('')
const newPayload = ref('')
const showComposer = ref(false)
const submitting = ref(false)
const availableBoards = ref<Board[]>([])
const loadingBoards = ref(false)

const boardOptions = computed(() =>
  buildInputAssistOptions(
    availableBoards.value.map((board) => ({
      value: board.id,
      label: board.name,
    })),
  ),
)

function handleBoardSelect(option: { value: string; label: string }) {
  newBoardId.value = option.value
  boardDisplayValue.value = option.label
}

function handleBoardInput(value: string) {
  boardDisplayValue.value = value
  // If the user clears or edits the display, clear the stored ID unless it still matches
  const matchingBoard = availableBoards.value.find(
    (b) => b.name === value || b.id === value,
  )
  newBoardId.value = matchingBoard ? matchingBoard.id : value
}

const statusTabs = ['Pending', 'Processing', 'Completed', 'Failed', 'Cancelled']
const guidPatterns = [
  /^[0-9a-fA-F]{32}$/,
  /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/,
  /^\{[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\}$/,
  /^\([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\)$/,
]
const boardScopedInstructionPattern = /\b(create card|rename board|update board|move column|update card|move card)\b/i

const canSubmitRequest = computed(() => {
  return !submitting.value && newRequestType.value.trim().length > 0 && newPayload.value.trim().length > 0
})

async function loadQueueData() {
  await queue.fetchByStatus(statusFilter.value)
  await queue.fetchStats()
}

function isSupportedGuidFormat(value: string): boolean {
  return guidPatterns.some((pattern) => pattern.test(value))
}

async function handleStatusChange(status: string) {
  statusFilter.value = status
  try {
    await queue.fetchByStatus(status)
  } catch {
    // Store handles toast + error state.
  }
}

async function handleSubmitRequest() {
  const trimmedRequestType = newRequestType.value.trim()
  const trimmedPayload = newPayload.value.trim()
  if (!trimmedRequestType || !trimmedPayload) {
    toast.error('Request type and instruction are required.')
    return
  }

  const trimmedBoardId = newBoardId.value.trim()
  if (!trimmedBoardId && boardScopedInstructionPattern.test(trimmedPayload)) {
    toast.error('Board is required for board-scoped instructions. Select one from the board picker.')
    return
  }

  if (trimmedBoardId && !isSupportedGuidFormat(trimmedBoardId)) {
    toast.error('Board ID must be a valid board selection or GUID.')
    return
  }

  try {
    submitting.value = true
    await queue.submitRequest({
      requestType: trimmedRequestType,
      payload: trimmedPayload,
      ...(trimmedBoardId ? { boardId: trimmedBoardId } : {}),
    })
    newRequestType.value = 'instruction'
    newBoardId.value = ''
    boardDisplayValue.value = ''
    newPayload.value = ''
    showComposer.value = false
  } catch {
    // Store handles toast + error state.
  } finally {
    submitting.value = false
  }
}

async function handleCancel(requestId: string) {
  if (!confirm('Cancel this request?')) {
    return
  }

  try {
    await queue.cancelRequest(requestId)
  } catch {
    // Store handles toast + error state.
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

function openRoute(path: string) {
  void router.push(path)
}

function formatDate(value: string | null): string {
  if (!value) {
    return '-'
  }

  return new Date(value).toLocaleString()
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

async function loadBoardOptions() {
  try {
    loadingBoards.value = true
    availableBoards.value = await boardsApi.getBoards(undefined, true)
  } catch {
    // Board options are non-critical.
  } finally {
    loadingBoards.value = false
  }
}

onMounted(() => {
  void loadBoardOptions()
  loadQueueData().catch(() => {
    // Store handles queue errors.
  })
})
</script>

<template>
  <div class="td-queue">
    <header class="td-panel td-queue__hero">
      <div class="td-queue__hero-copy">
        <span class="td-queue__eyebrow">Advanced</span>
        <h1 class="td-page-title">Automation Queue</h1>
        <p class="td-queue__subtitle">
          Use this surface when you need to inspect or submit raw queue requests directly. Most users should stay in
          Review for proposal decisions and Inbox for capture-driven automation.
        </p>
      </div>

      <div class="td-queue__hero-actions">
        <button class="td-btn td-btn--primary" @click="openRoute('/workspace/review')">Back to Review</button>
        <button class="td-btn td-btn--secondary" @click="openRoute('/workspace/automations/chat')">
          Open Chat (Advanced)
        </button>
      </div>
    </header>

    <section class="td-panel td-queue__explain">
      <h2 class="td-section-title">When to use queue directly</h2>
      <p class="td-section-desc">
        Queue is the operator path for manual requests, troubleshooting, and low-level inspection. Request types
        stay visible here on purpose because this is not the normal happy path.
      </p>
    </section>

    <div v-if="queue.stats" class="td-queue__stats">
      <article class="td-stat-card">
        <div class="td-stat-value">{{ queue.stats.pendingCount }}</div>
        <div class="td-stat-label">Pending</div>
      </article>
      <article class="td-stat-card">
        <div class="td-stat-value">{{ queue.stats.processingCount }}</div>
        <div class="td-stat-label">Processing</div>
      </article>
      <article class="td-stat-card">
        <div class="td-stat-value">{{ queue.stats.completedCount }}</div>
        <div class="td-stat-label">Completed</div>
      </article>
      <article class="td-stat-card">
        <div class="td-stat-value">{{ queue.stats.failedCount }}</div>
        <div class="td-stat-label">Failed</div>
      </article>
      <article class="td-stat-card">
        <div class="td-stat-value">{{ getQueueTotal(queue.stats) }}</div>
        <div class="td-stat-label">Total</div>
      </article>
    </div>

    <section class="td-panel td-queue__panel">
      <div class="td-queue__toolbar">
        <div class="td-status-tabs">
          <button
            v-for="status in statusTabs"
            :key="status"
            :class="['td-status-tab', { 'td-status-tab--active': statusFilter === status }]"
            @click="handleStatusChange(status)"
          >
            {{ status }}
          </button>
        </div>

        <div class="td-queue__toolbar-actions">
          <button class="td-btn td-btn--secondary td-btn--sm" @click="handleProcessNext">Process Next</button>
          <button class="td-btn td-btn--primary td-btn--sm" @click="showComposer = !showComposer">
            {{ showComposer ? 'Cancel' : '+ New Request' }}
          </button>
        </div>
      </div>

      <div v-if="showComposer" class="td-queue__composer">
        <div class="td-form-group">
          <label class="td-label">Request Type (advanced)</label>
          <input v-model="newRequestType" type="text" class="td-input" placeholder="instruction" />
          <div class="td-helper">
            Leave this as <strong>instruction</strong> for most manual requests. Capture triage requests are created
            through <strong>Inbox -&gt; Start Triage</strong>, not by typing them here.
          </div>
        </div>

        <div class="td-form-group">
          <label class="td-label">Board (optional)</label>
          <InputAssistField
            :model-value="boardDisplayValue"
            :options="boardOptions"
            aria-label="Board for queue request"
            placeholder="Select a board..."
            no-results-text="No matching boards."
            :disabled="loadingBoards"
            @update:model-value="handleBoardInput"
            @select="handleBoardSelect"
          />
          <div class="td-helper">
            Board-scoped instructions (create card, move column, etc.) require a board selection.
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
            <strong>update card {id} title|description "value"</strong>, and
            <strong>move card {id} to column "name"</strong>.
          </div>
        </div>

        <button class="td-btn td-btn--primary" :disabled="!canSubmitRequest" @click="handleSubmitRequest">
          {{ submitting ? 'Submitting...' : 'Submit Request' }}
        </button>
      </div>

      <div v-if="queue.loading" class="td-loading">Loading queue requests...</div>

      <div v-else-if="queue.requests.length === 0" class="td-queue-empty">
        <h2 class="td-section-title">No queue requests match this filter</h2>
        <p class="td-section-desc">
          That usually means the normal review flow is quiet right now. Go back to Review, or use Inbox if you want to
          create fresh capture-driven proposals.
        </p>
        <div class="td-queue-empty__actions">
          <button class="td-btn td-btn--primary" @click="openRoute('/workspace/review')">Open Review</button>
          <button class="td-btn td-btn--secondary" @click="openRoute('/workspace/inbox')">Open Inbox</button>
        </div>
      </div>

      <div v-else class="td-request-list">
        <article v-for="request in queue.requests" :key="request.id" class="td-request-card">
          <div class="td-request-header">
            <span class="td-request-type">{{ request.requestType }}</span>
            <span class="td-status-badge" :style="{ color: statusColor(request.status), borderColor: statusColor(request.status) }">
              {{ normalizeQueueStatus(request.status) }}
            </span>
          </div>
          <div class="td-request-meta">
            <span>Created: {{ formatDate(request.createdAt) }}</span>
            <span v-if="request.processedAt">Processed: {{ formatDate(request.processedAt) }}</span>
          </div>
          <div v-if="request.errorMessage" class="td-request-error">{{ request.errorMessage }}</div>
          <div class="td-request-actions">
            <button
              v-if="normalizeQueueStatus(request.status) === 'Pending'"
              class="td-btn td-btn--danger td-btn--sm"
              @click="handleCancel(request.id)"
            >
              Cancel
            </button>
          </div>
        </article>
      </div>
    </section>
  </div>
</template>

<style scoped>
.td-queue {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-4);
}

.td-queue__hero {
  display: flex;
  justify-content: space-between;
  gap: var(--td-space-6);
  align-items: flex-start;
}

.td-queue__hero-copy {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
  max-width: 720px;
}

.td-queue__eyebrow {
  font-size: var(--td-font-xs);
  font-weight: 700;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--td-text-tertiary);
}

.td-queue__subtitle {
  color: var(--td-text-secondary);
  line-height: 1.6;
}

.td-queue__hero-actions,
.td-queue-empty__actions,
.td-queue__toolbar-actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--td-space-2);
}

.td-queue__stats {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(140px, 1fr));
  gap: var(--td-space-3);
}

.td-stat-card {
  background: var(--td-surface-primary);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-lg);
  padding: var(--td-space-4);
}

.td-stat-value {
  font-size: var(--td-font-2xl);
  font-weight: 700;
  color: var(--td-text-primary);
}

.td-stat-label {
  margin-top: 0.25rem;
  color: var(--td-text-secondary);
  font-size: var(--td-font-sm);
}

.td-queue__panel,
.td-queue__explain {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
}

.td-queue__toolbar {
  display: flex;
  justify-content: space-between;
  gap: var(--td-space-3);
  align-items: flex-start;
  flex-wrap: wrap;
}

.td-status-tabs {
  display: flex;
  gap: var(--td-space-2);
  flex-wrap: wrap;
}

.td-status-tab {
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-pill, 999px);
  background: var(--td-surface-secondary);
  color: var(--td-text-secondary);
  padding: 0.375rem 0.875rem;
  font-size: var(--td-font-sm);
  cursor: pointer;
}

.td-status-tab--active {
  color: var(--td-text-primary);
  border-color: var(--td-border-focus);
  background: var(--td-surface-tertiary);
}

.td-queue__composer {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
  padding: var(--td-space-4);
  border-radius: var(--td-radius-md);
  background: var(--td-surface-secondary);
  border: 1px solid var(--td-border-default);
}

.td-form-group {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-1);
}

.td-label {
  font-size: var(--td-font-sm);
  font-weight: 600;
  color: var(--td-text-secondary);
}

.td-input,
.td-textarea {
  padding: var(--td-space-2) var(--td-space-3);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-md);
  font-size: var(--td-font-sm);
  background: var(--td-surface-primary);
}

.td-textarea {
  resize: vertical;
}

.td-helper {
  color: var(--td-text-secondary);
  font-size: var(--td-font-xs);
  line-height: 1.5;
}

.td-loading {
  color: var(--td-text-secondary);
}

.td-queue-empty {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
}

.td-request-list {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
}

.td-request-card {
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-md);
  padding: var(--td-space-4);
  background: var(--td-surface-primary);
}

.td-request-header {
  display: flex;
  justify-content: space-between;
  gap: var(--td-space-3);
  align-items: flex-start;
}

.td-request-type {
  font-weight: 700;
  color: var(--td-text-primary);
}

.td-status-badge {
  display: inline-flex;
  align-items: center;
  border-radius: var(--td-radius-pill, 999px);
  border: 1px solid currentColor;
  padding: 0.25rem 0.625rem;
  font-size: var(--td-font-xs);
  font-weight: 700;
}

.td-request-meta {
  display: flex;
  flex-wrap: wrap;
  gap: var(--td-space-3);
  margin-top: var(--td-space-2);
  font-size: var(--td-font-xs);
  color: var(--td-text-secondary);
}

.td-request-error {
  margin-top: var(--td-space-2);
  color: var(--td-color-error);
  font-size: var(--td-font-sm);
}

.td-request-actions {
  margin-top: var(--td-space-3);
  display: flex;
  gap: var(--td-space-2);
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
  .td-queue__hero {
    flex-direction: column;
  }

  .td-request-header {
    flex-direction: column;
  }
}
</style>
