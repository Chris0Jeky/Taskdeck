<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useQueueStore } from '../store/queueStore'
import { useToastStore } from '../store/toastStore'
import { boardsApi } from '../api/boardsApi'
import InputAssistField from '../components/common/InputAssistField.vue'
import PaperHLBtn from '../components/paper/PaperHLBtn.vue'
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
  // Resolve typed input to a canonical board ID when it matches a known board
  // name or ID. Otherwise pass the raw value through so GUID validation in
  // handleSubmitRequest can catch invalid entries.
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

function statusClass(status: QueueStatus | number): string {
  const normalized = normalizeQueueStatus(status)
  const classes: Record<string, string> = {
    Pending: 'paper-queue__status-badge--warning',
    Processing: 'paper-queue__status-badge--info',
    Completed: 'paper-queue__status-badge--success',
    Failed: 'paper-queue__status-badge--error',
    Cancelled: 'paper-queue__status-badge--muted',
  }
  return classes[normalized] ?? 'paper-queue__status-badge--muted'
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
  <div class="paper-queue">
    <header class="paper-queue__panel paper-queue__hero">
      <div class="paper-queue__hero-copy">
        <span class="tk-eyebrow paper-queue__eyebrow">Advanced</span>
        <h1 class="tk-h2 paper-queue__title">Automation Queue</h1>
        <p class="tk-lede paper-queue__subtitle">
          Use this surface when you need to inspect or submit raw queue requests directly. Most users should stay in
          Review for proposal decisions and Inbox for capture-driven automation.
        </p>
      </div>

      <div class="paper-queue__hero-actions">
        <PaperHLBtn variant="ember" @click="openRoute('/workspace/review')">Back to Review</PaperHLBtn>
        <PaperHLBtn @click="openRoute('/workspace/automations/chat')">
          Open Chat (Advanced)
        </PaperHLBtn>
      </div>
    </header>

    <section class="paper-queue__panel paper-queue__explain">
      <h2 class="tk-h3 paper-queue__section-title">When to use queue directly</h2>
      <p class="paper-queue__section-desc">
        Queue is the operator path for manual requests, troubleshooting, and low-level inspection. Request types
        stay visible here on purpose because this is not the normal happy path.
      </p>
    </section>

    <div v-if="queue.stats" class="paper-queue__stats">
      <article class="paper-queue__stat-card">
        <div class="paper-queue__stat-value">{{ queue.stats.pendingCount }}</div>
        <div class="paper-queue__stat-label">Pending</div>
      </article>
      <article class="paper-queue__stat-card">
        <div class="paper-queue__stat-value">{{ queue.stats.processingCount }}</div>
        <div class="paper-queue__stat-label">Processing</div>
      </article>
      <article class="paper-queue__stat-card">
        <div class="paper-queue__stat-value">{{ queue.stats.completedCount }}</div>
        <div class="paper-queue__stat-label">Completed</div>
      </article>
      <article class="paper-queue__stat-card">
        <div class="paper-queue__stat-value">{{ queue.stats.failedCount }}</div>
        <div class="paper-queue__stat-label">Failed</div>
      </article>
      <article class="paper-queue__stat-card">
        <div class="paper-queue__stat-value">{{ getQueueTotal(queue.stats) }}</div>
        <div class="paper-queue__stat-label">Total</div>
      </article>
    </div>

    <section class="paper-queue__panel">
      <div class="paper-queue__toolbar">
        <div class="paper-queue__status-tabs">
          <button
            v-for="status in statusTabs"
            :key="status"
            :class="['paper-queue__status-tab', { 'paper-queue__status-tab--active': statusFilter === status }]"
            @click="handleStatusChange(status)"
          >
            {{ status }}
          </button>
        </div>

        <div class="paper-queue__toolbar-actions">
          <PaperHLBtn @click="handleProcessNext">Process Next</PaperHLBtn>
          <PaperHLBtn variant="ember" @click="showComposer = !showComposer">
            {{ showComposer ? 'Cancel' : '+ New Request' }}
          </PaperHLBtn>
        </div>
      </div>

      <div v-if="showComposer" class="paper-queue__composer">
        <div class="paper-queue__form-group">
          <label for="queue-request-type" class="paper-queue__label">Request Type (advanced)</label>
          <input
            id="queue-request-type"
            v-model="newRequestType"
            type="text"
            class="paper-queue__input"
            placeholder="instruction"
          />
          <div class="paper-queue__helper">
            Leave this as <strong>instruction</strong> for most manual requests. Capture triage requests are created
            through <strong>Inbox -&gt; Start Triage</strong>, not by typing them here.
          </div>
        </div>

        <div class="paper-queue__form-group">
          <p class="paper-queue__label">Board (optional)</p>
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
          <div class="paper-queue__helper">
            Board-scoped instructions (create card, move column, etc.) require a board selection.
          </div>
        </div>

        <div class="paper-queue__form-group">
          <label for="queue-instruction" class="paper-queue__label">Instruction</label>
          <textarea
            id="queue-instruction"
            v-model="newPayload"
            class="paper-queue__textarea"
            rows="6"
            placeholder='create card "Write MVP demo script"'
          ></textarea>
          <div class="paper-queue__helper">
            Supported patterns include: <strong>create card "title"</strong>, <strong>rename board to "name"</strong>,
            <strong>update board description "value"</strong>, <strong>move column "name" to position {n}</strong>,
            <strong>update card {id} title|description "value"</strong>, and
            <strong>move card {id} to column "name"</strong>.
          </div>
        </div>

        <PaperHLBtn variant="ember" :disabled="!canSubmitRequest" @click="handleSubmitRequest">
          {{ submitting ? 'Submitting...' : 'Submit Request' }}
        </PaperHLBtn>
      </div>

      <div v-if="queue.loading" class="paper-queue__loading">Loading queue requests...</div>

      <div v-else-if="queue.requests.length === 0" class="paper-queue__empty">
        <h2 class="tk-h3 paper-queue__section-title">No queue requests match this filter</h2>
        <p class="paper-queue__section-desc">
          That usually means the normal review flow is quiet right now. Go back to Review, or use Inbox if you want to
          create fresh capture-driven proposals.
        </p>
        <div class="paper-queue__empty-actions">
          <PaperHLBtn variant="ember" @click="openRoute('/workspace/review')">Open Review</PaperHLBtn>
          <PaperHLBtn @click="openRoute('/workspace/inbox')">Open Inbox</PaperHLBtn>
        </div>
      </div>

      <div v-else class="paper-queue__request-list">
        <article v-for="request in queue.requests" :key="request.id" class="paper-queue__request-card">
          <div class="paper-queue__request-header">
            <span class="paper-queue__request-type">{{ request.requestType }}</span>
            <span class="paper-queue__status-badge" :class="statusClass(request.status)">
              {{ normalizeQueueStatus(request.status) }}
            </span>
          </div>
          <div class="paper-queue__request-meta">
            <span>Created: {{ formatDate(request.createdAt) }}</span>
            <span v-if="request.processedAt">Processed: {{ formatDate(request.processedAt) }}</span>
          </div>
          <div v-if="request.errorMessage" class="paper-queue__request-error">{{ request.errorMessage }}</div>
          <div class="paper-queue__request-actions">
            <PaperHLBtn
              v-if="normalizeQueueStatus(request.status) === 'Pending'"
              class="paper-queue__cancel"
              @click="handleCancel(request.id)"
            >
              Cancel
            </PaperHLBtn>
          </div>
        </article>
      </div>
    </section>
  </div>
</template>

<style scoped>
/* ── Paper & Graphite — AutomationQueueView ──
   Styled against the Paper token system (--paper, --ink, --ember families).
   Tokens live under `.paper` / `.paper-night`, so var() fallbacks keep the
   surface legible if rendered outside the Paper shell. */

.paper-queue {
  display: flex;
  flex-direction: column;
  gap: var(--s-4, 16px);
  font-family: var(--sans, system-ui, sans-serif);
  /* Legacy ("off") mode: Paper vars are scoped to .paper/.paper-night, so a root
     that sets --ink must paint --paper alongside it or the near-black fallback
     lands on AppShell's Obsidian surface. No-op inside the Paper shell. */
  background: var(--paper, #f3eee5);
  color: var(--ink, #1a1814);
}

.paper-queue__panel {
  padding: var(--s-4, 16px);
  border-radius: var(--r-3, 6px);
  border: 1px solid var(--line, #d8d0bf);
  background: var(--paper-card, #fbf7ee);
  box-shadow: var(--shadow-card, 0 1px 0 #d8d0bf);
  display: flex;
  flex-direction: column;
  gap: var(--s-3, 12px);
}

.paper-queue__hero {
  flex-direction: row;
  justify-content: space-between;
  gap: var(--s-6, 24px);
  align-items: flex-start;
}

.paper-queue__hero-copy {
  display: flex;
  flex-direction: column;
  gap: var(--s-2, 8px);
  max-width: 720px;
}

.paper-queue__eyebrow { color: var(--ember, #a8421f); }
.paper-queue__title { margin: 0; font-size: var(--t-h2, 32px); }
.paper-queue__subtitle { margin: 0; color: var(--ink-2, #3a352d); }

.paper-queue__section-title { margin: 0; font-size: var(--t-lg, 18px); }
.paper-queue__section-desc { margin: 0; color: var(--ink-2, #3a352d); font-size: var(--t-md, 13.5px); line-height: 1.55; }

.paper-queue__hero-actions,
.paper-queue__empty-actions,
.paper-queue__toolbar-actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--s-2, 8px);
}

.paper-queue__stats {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(140px, 1fr));
  gap: var(--s-3, 12px);
}

.paper-queue__stat-card {
  background: var(--paper-card, #fbf7ee);
  border: 1px solid var(--line, #d8d0bf);
  border-radius: var(--r-3, 6px);
  box-shadow: var(--shadow-card, 0 1px 0 #d8d0bf);
  padding: var(--s-4, 16px);
}

.paper-queue__stat-value {
  font-family: var(--mono, ui-monospace, monospace);
  font-feature-settings: "tnum" 1;
  font-size: var(--t-h2, 32px);
  font-weight: 700;
  color: var(--ink-deep, #0a0908);
}

.paper-queue__stat-label {
  margin-top: var(--s-1, 4px);
  color: var(--mute, #6c6557);
  font-size: var(--t-sm, 12px);
  letter-spacing: 0.08em;
  text-transform: uppercase;
}

.paper-queue__toolbar {
  display: flex;
  justify-content: space-between;
  gap: var(--s-3, 12px);
  align-items: flex-start;
  flex-wrap: wrap;
}

.paper-queue__status-tabs {
  display: flex;
  gap: var(--s-2, 8px);
  flex-wrap: wrap;
}

.paper-queue__status-tab {
  border: 1px solid var(--line, #d8d0bf);
  border-radius: var(--r-2, 4px);
  background: var(--paper, #f3eee5);
  color: var(--ink-2, #3a352d);
  padding: var(--s-1, 4px) var(--s-3, 12px);
  font-family: inherit;
  font-size: var(--t-md, 13.5px);
  cursor: pointer;
  transition: background var(--d-quick, 140ms) var(--ease-paper, ease),
    border-color var(--d-quick, 140ms) var(--ease-paper, ease);
}

.paper-queue__status-tab:hover { background: var(--paper-2, #ebe5d8); }

.paper-queue__status-tab--active {
  color: var(--ember-ink, #6e2810);
  border-color: var(--ember, #a8421f);
  background: var(--ember-tint, #f0d9c8);
}

.paper-queue__composer {
  display: flex;
  flex-direction: column;
  gap: var(--s-3, 12px);
  padding: var(--s-4, 16px);
  border-radius: var(--r-2, 4px);
  background: var(--paper, #f3eee5);
  border: 1px solid var(--line, #d8d0bf);
  align-items: flex-start;
}

.paper-queue__form-group {
  display: flex;
  flex-direction: column;
  gap: var(--s-1, 4px);
  width: 100%;
}

.paper-queue__label {
  margin: 0;
  font-size: var(--t-xs, 10.5px);
  font-weight: 600;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  color: var(--mute, #6c6557);
}

.paper-queue__input,
.paper-queue__textarea {
  padding: var(--s-2, 8px) var(--s-3, 12px);
  border: 1px solid var(--line, #d8d0bf);
  border-radius: var(--r-2, 4px);
  font-family: var(--sans, system-ui, sans-serif);
  font-size: var(--t-md, 13.5px);
  background: var(--paper-card, #fbf7ee);
  color: var(--ink, #1a1814);
  transition: border-color var(--d-quick, 140ms) var(--ease-paper, ease);
}

.paper-queue__input:focus,
.paper-queue__textarea:focus {
  outline: none;
  border-color: var(--ember, #a8421f);
  box-shadow: 0 0 0 2px var(--ember-bloom, #a8421f1a);
}

.paper-queue__textarea {
  resize: vertical;
  font-family: var(--mono, ui-monospace, monospace);
}

.paper-queue__helper {
  color: var(--mute, #6c6557);
  font-size: var(--t-xs, 10.5px);
  line-height: 1.5;
}

.paper-queue__loading { color: var(--mute, #6c6557); }

.paper-queue__empty {
  display: flex;
  flex-direction: column;
  gap: var(--s-3, 12px);
}

.paper-queue__request-list {
  display: flex;
  flex-direction: column;
  gap: var(--s-3, 12px);
}

.paper-queue__request-card {
  border: 1px solid var(--line, #d8d0bf);
  border-radius: var(--r-2, 4px);
  padding: var(--s-4, 16px);
  background: var(--paper, #f3eee5);
}

.paper-queue__request-header {
  display: flex;
  justify-content: space-between;
  gap: var(--s-3, 12px);
  align-items: flex-start;
}

.paper-queue__request-type {
  font-family: var(--mono, ui-monospace, monospace);
  font-weight: 700;
  color: var(--ink-deep, #0a0908);
}

.paper-queue__status-badge {
  display: inline-flex;
  align-items: center;
  border-radius: var(--r-1, 2px);
  border: 1px solid currentColor;
  padding: var(--s-1, 4px) var(--s-2, 8px);
  font-size: var(--t-xs, 10.5px);
  font-weight: 700;
  color: var(--mute, #6c6557);
}

.paper-queue__status-badge--warning { color: var(--overdue, #8c4a26); }
.paper-queue__status-badge--info { color: var(--ember, #a8421f); }
.paper-queue__status-badge--success { color: var(--applied, #4a6b3f); }
.paper-queue__status-badge--error { color: var(--ember-deep, #7a2e15); }
.paper-queue__status-badge--muted { color: var(--mute, #6c6557); }

.paper-queue__request-meta {
  display: flex;
  flex-wrap: wrap;
  gap: var(--s-3, 12px);
  margin-top: var(--s-2, 8px);
  font-family: var(--mono, ui-monospace, monospace);
  font-size: var(--t-xs, 10.5px);
  color: var(--mute, #6c6557);
}

.paper-queue__request-error {
  margin-top: var(--s-2, 8px);
  color: var(--overdue, #8c4a26);
  font-size: var(--t-md, 13.5px);
}

.paper-queue__request-actions {
  margin-top: var(--s-3, 12px);
  display: flex;
  gap: var(--s-2, 8px);
}

@media (max-width: 900px) {
  .paper-queue__hero {
    flex-direction: column;
  }

  .paper-queue__request-header {
    flex-direction: column;
  }
}
</style>
