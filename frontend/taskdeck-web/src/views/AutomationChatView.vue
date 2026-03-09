<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { chatApi } from '../api/chatApi'
import { boardsApi } from '../api/boardsApi'
import { useToastStore } from '../store/toastStore'
import type { ChatSession } from '../types/chat'
import type { Board } from '../types/board'
import { normalizeChatRole } from '../utils/chat'
import { getErrorDisplay } from '../composables/useErrorMapper'
import InputAssistField from '../components/common/InputAssistField.vue'
import { buildInputAssistOptions } from '../utils/inputAssist'
import type { InputAssistOption } from '../utils/inputAssist'
import { normalizeBoardIdQueryParam } from '../utils/navigation'

const router = useRouter()
const route = useRoute()
const toast = useToastStore()

const sessions = ref<ChatSession[]>([])
const availableBoards = ref<Board[]>([])
const selectedSession = ref<ChatSession | null>(null)
const loadingSessions = ref(false)
const loadingBoards = ref(false)
const creatingSession = ref(false)
const sendingMessage = ref(false)
let boardOptionsRequest: Promise<boolean> | null = null

const newSessionTitle = ref('')
const newSessionBoardId = ref('')
const selectedNewSessionBoardId = ref<string | null>(null)
const messageContent = ref('')
const requestProposal = ref(false)

const boardOptions = computed(() =>
  buildInputAssistOptions(
    availableBoards.value.map((board) => ({
      value: board.id,
      label: board.name,
      helperText: board.isArchived
        ? 'Archived board'
        : (board.description?.trim() || 'Active board'),
      keywords: [board.description ?? '', board.name],
    })),
  ),
)

const boardNameById = computed(() => (
  new Map(availableBoards.value.map((board) => [board.id, board.name]))
))

const boardById = computed(() => (
  new Map(availableBoards.value.map((board) => [board.id, board]))
))

const sortedMessages = computed(() => {
  const current = selectedSession.value
  if (!current) {
    return []
  }

  return [...current.recentMessages].sort((a, b) => (
    new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime()
  ))
})

const selectedSessionBoardName = computed(() => {
  const boardId = selectedSession.value?.boardId?.trim()
  if (!boardId) {
    return 'No board context'
  }

  return boardNameById.value.get(boardId) ?? 'Linked board context'
})

const pendingSessionBoardContextLabel = computed(() => {
  const selectedBoard = selectedNewSessionBoardId.value
    ? boardById.value.get(selectedNewSessionBoardId.value)
    : null
  if (selectedBoard) {
    return selectedBoard.name
  }

  if (newSessionBoardId.value.trim()) {
    return newSessionBoardId.value.trim()
  }

  return boardNameById.value.get(queryBoardId.value) ?? queryBoardId.value
})

const queryBoardId = computed(() => normalizeBoardIdQueryParam(route.query.boardId))

function normalizeSelectedBoardId(rawValue: string): string | null {
  const trimmed = rawValue.trim()
  if (!trimmed) {
    return null
  }

  const selectedBoard = selectedNewSessionBoardId.value
    ? boardById.value.get(selectedNewSessionBoardId.value)
    : null
  if (selectedBoard) {
    const normalizedSelectedId = selectedBoard.id.trim().toLowerCase()
    const normalizedSelectedName = selectedBoard.name.trim().toLowerCase()
    const normalizedInput = trimmed.toLowerCase()
    if (normalizedInput === normalizedSelectedId || normalizedInput === normalizedSelectedName) {
      return selectedBoard.id
    }
  }

  const normalized = trimmed.toLowerCase()
  const byId = availableBoards.value.find((board) => board.id.toLowerCase() === normalized)
  if (byId) {
    return byId.id
  }

  const nameMatches = availableBoards.value.filter((board) => board.name.trim().toLowerCase() === normalized)
  return nameMatches.length === 1 ? nameMatches[0]!.id : null
}

function updateNewSessionBoardValue(value: string) {
  newSessionBoardId.value = value

  const selectedBoard = selectedNewSessionBoardId.value
    ? boardById.value.get(selectedNewSessionBoardId.value)
    : null
  if (!selectedBoard) {
    selectedNewSessionBoardId.value = null
    return
  }

  const normalizedValue = value.trim().toLowerCase()
  if (!normalizedValue) {
    selectedNewSessionBoardId.value = null
    return
  }

  const matchesSelectedBoard = normalizedValue === selectedBoard.id.trim().toLowerCase() ||
    normalizedValue === selectedBoard.name.trim().toLowerCase()
  if (!matchesSelectedBoard) {
    selectedNewSessionBoardId.value = null
  }
}

function handleNewSessionBoardSelect(option: InputAssistOption) {
  selectedNewSessionBoardId.value = option.value
  updateNewSessionBoardValue(option.label)
}

function applyRouteBoardContext() {
  if (!queryBoardId.value) {
    return
  }

  const matchedBoard = availableBoards.value.find((board) => board.id === queryBoardId.value)
  if (matchedBoard) {
    newSessionBoardId.value = matchedBoard.name
    selectedNewSessionBoardId.value = matchedBoard.id
  }
}

async function loadSessions() {
  try {
    loadingSessions.value = true
    sessions.value = await chatApi.getMySessions()
    if (!selectedSession.value && sessions.value.length > 0) {
      await loadSession(sessions.value[0]!.id)
    }
  } catch (e: unknown) {
    toast.error(getErrorDisplay(e, 'Failed to load chat sessions').message)
  } finally {
    loadingSessions.value = false
  }
}

async function loadSession(sessionId: string) {
  try {
    selectedSession.value = await chatApi.getSession(sessionId)
  } catch (e: unknown) {
    toast.error(getErrorDisplay(e, 'Failed to load chat session').message)
  }
}

async function handleCreateSession() {
  if (!newSessionTitle.value.trim()) {
    toast.error('Session title is required')
    return
  }

  if (newSessionBoardId.value.trim()) {
    const didLoadBoards = await loadBoardOptions()
    if (!didLoadBoards) {
      return
    }
  }

  const normalizedBoardId = normalizeSelectedBoardId(newSessionBoardId.value)
  if (newSessionBoardId.value.trim() && !normalizedBoardId) {
    toast.error('Choose a board from the list or leave board context blank.')
    return
  }

  try {
    creatingSession.value = true
    const created = await chatApi.createSession({
      title: newSessionTitle.value.trim(),
      boardId: normalizedBoardId,
    })
    newSessionTitle.value = ''
    newSessionBoardId.value = ''
    selectedNewSessionBoardId.value = null
    await loadSessions()
    await loadSession(created.id)
  } catch (e: unknown) {
    toast.error(getErrorDisplay(e, 'Failed to create session').message)
  } finally {
    creatingSession.value = false
  }
}

async function handleSendMessage() {
  if (!selectedSession.value) {
    toast.error('Select a session first')
    return
  }
  if (!messageContent.value.trim()) {
    return
  }

  try {
    sendingMessage.value = true
    await chatApi.sendMessage(selectedSession.value.id, {
      content: messageContent.value.trim(),
      requestProposal: requestProposal.value,
    })
    messageContent.value = ''
    requestProposal.value = false
    await loadSession(selectedSession.value.id)
  } catch (e: unknown) {
    toast.error(getErrorDisplay(e, 'Failed to send message').message)
  } finally {
    sendingMessage.value = false
  }
}

async function loadBoardOptions(): Promise<boolean> {
  if (boardOptionsRequest) {
    return await boardOptionsRequest
  }

  let request: Promise<boolean> | null = null
  request = (async () => {
    try {
      loadingBoards.value = true
      availableBoards.value = await boardsApi.getBoards()
      return true
    } catch (e: unknown) {
      toast.error(getErrorDisplay(e, 'Failed to load boards').message)
      return false
    } finally {
      loadingBoards.value = false
      if (boardOptionsRequest === request) {
        boardOptionsRequest = null
      }
    }
  })()

  boardOptionsRequest = request
  return await boardOptionsRequest
}

function openRoute(path: string) {
  void router.push(path)
}

function openProposalReview(proposalId: string) {
  void router.push({
    name: 'workspace-review',
    query: selectedSession.value?.boardId
      ? { boardId: selectedSession.value.boardId }
      : undefined,
    hash: `#proposal-${encodeURIComponent(proposalId)}`,
  })
}

onMounted(() => {
  void loadSessions()
  void loadBoardOptions().then(() => {
    applyRouteBoardContext()
  })
})

watch(
  () => [queryBoardId.value, availableBoards.value.length],
  () => {
    applyRouteBoardContext()
  },
)
</script>

<template>
  <div class="td-chat">
    <header class="td-panel td-chat__hero">
      <div class="td-chat__hero-copy">
        <span class="td-chat__eyebrow">Advanced</span>
        <h1 class="td-page-title">Automation Chat</h1>
        <p class="td-chat__subtitle">
          Use chat when you need to inspect or refine automation conversations manually. Proposal decisions still belong
          in Review, which remains the normal path.
        </p>
      </div>

      <div class="td-chat__hero-actions">
        <button class="td-btn td-btn--primary" @click="openRoute('/workspace/review')">Back to Review</button>
        <button class="td-btn td-btn--secondary" @click="openRoute('/workspace/automations/queue')">
          Open Queue (Advanced)
        </button>
      </div>
    </header>

    <div class="td-chat-layout">
      <aside class="td-chat-sessions">
        <div class="td-chat-section-head">
          <div>
            <h2 class="td-subtitle">Sessions</h2>
            <p class="td-chat-section-copy">
              Create a manual session when you need operator control. Pick a board by name if the conversation should
              stay anchored to one workspace.
            </p>
          </div>
          <button class="td-btn td-btn--secondary td-btn--sm" :disabled="loadingBoards" @click="loadBoardOptions">
            {{ loadingBoards ? 'Loading...' : 'Reload Boards' }}
          </button>
        </div>

        <div class="td-form-group">
          <input v-model="newSessionTitle" class="td-input" type="text" placeholder="Session title" />
          <InputAssistField
            v-model="newSessionBoardId"
            :options="boardOptions"
            aria-label="Board context"
            placeholder="Board context (optional)"
            no-results-text="No matching boards."
            @update:model-value="updateNewSessionBoardValue"
            @select="handleNewSessionBoardSelect"
          />
          <p v-if="queryBoardId" class="td-chat-board-context">
            Board context will stay anchored to {{ pendingSessionBoardContextLabel }}.
          </p>
          <button class="td-btn td-btn--primary td-btn--sm" @click="handleCreateSession" :disabled="creatingSession">
            {{ creatingSession ? 'Creating...' : 'Create Session' }}
          </button>
        </div>

        <div v-if="loadingSessions" class="td-loading">Loading sessions...</div>
        <div v-else-if="sessions.length === 0" class="td-empty td-empty--panel">
          <h3 class="td-empty__title">No chat sessions yet</h3>
          <p class="td-empty__copy">
            Return to Review for the normal proposal flow, or create a session here when you specifically need an
            operator conversation.
          </p>
          <div class="td-empty__actions">
            <button class="td-btn td-btn--primary td-btn--sm" @click="openRoute('/workspace/review')">
              Open Review
            </button>
          </div>
        </div>
        <button
          v-for="session in sessions"
          :key="session.id"
          :class="['td-session-item', { 'td-session-item--active': selectedSession?.id === session.id }]"
          @click="loadSession(session.id)"
        >
          <div class="td-session-title">{{ session.title }}</div>
          <div class="td-session-meta">{{ new Date(session.updatedAt).toLocaleString() }}</div>
        </button>
      </aside>

      <section class="td-chat-panel">
        <div v-if="!selectedSession" class="td-empty td-empty--panel">
          <h3 class="td-empty__title">Select or create a session</h3>
          <p class="td-empty__copy">
            Review handles the standard approve or reject path. Chat is here for manual operator follow-up when you
            need to inspect the conversation itself.
          </p>
          <div class="td-empty__actions">
            <button class="td-btn td-btn--primary td-btn--sm" @click="openRoute('/workspace/review')">
              Open Review
            </button>
          </div>
        </div>

        <template v-else>
          <div class="td-chat-header">
            <h2>{{ selectedSession.title }}</h2>
            <div class="td-chat-meta" :data-session-id="selectedSession.id">
              <span>{{ selectedSessionBoardName }}</span>
              <span class="td-chat-meta__detail"> | Session {{ selectedSession.id }}</span>
            </div>
          </div>

          <div class="td-chat-messages">
            <div v-if="sortedMessages.length === 0" class="td-empty td-empty--panel">
              <h3 class="td-empty__title">No messages yet</h3>
              <p class="td-empty__copy">
                Send a manual instruction below, or return to Review if you already have a proposal waiting for a
                decision.
              </p>
            </div>
            <div v-for="message in sortedMessages" :key="message.id" class="td-message">
              <div class="td-message-header">
                <span class="td-message-role">{{ normalizeChatRole(message.role) }}</span>
                <span class="td-message-time">{{ new Date(message.createdAt).toLocaleTimeString() }}</span>
              </div>
              <div class="td-message-content">{{ message.content }}</div>
              <div v-if="message.proposalId && message.messageType === 'proposal-reference'" class="td-message-proposal">
                <span>Proposal: {{ message.proposalId }}</span>
                <button
                  class="td-btn td-btn--secondary td-btn--xs"
                  @click="openProposalReview(message.proposalId)"
                >
                  Open in Review
                </button>
              </div>
            </div>
          </div>

          <div class="td-chat-compose">
            <textarea
              v-model="messageContent"
              class="td-textarea"
              rows="3"
              placeholder="Describe an automation instruction..."
              @keydown.ctrl.enter.prevent="handleSendMessage"
            ></textarea>
            <label class="td-checkbox">
              <input v-model="requestProposal" type="checkbox" />
              Request proposal generation
            </label>
            <button class="td-btn td-btn--primary" @click="handleSendMessage" :disabled="sendingMessage">
              {{ sendingMessage ? 'Sending...' : 'Send Message' }}
            </button>
          </div>
        </template>
      </section>
    </div>
  </div>
</template>

<style scoped>
.td-chat {
  max-width: 1200px;
  display: flex;
  flex-direction: column;
  gap: var(--td-space-4);
}

.td-page-title {
  font-size: var(--td-font-2xl);
  font-weight: 700;
  color: var(--td-text-primary);
}

.td-chat__hero {
  display: flex;
  justify-content: space-between;
  gap: var(--td-space-6);
  align-items: flex-start;
}

.td-chat__hero-copy {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
  max-width: 720px;
}

.td-chat__eyebrow {
  font-size: var(--td-font-xs);
  font-weight: 700;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--td-color-primary);
}

.td-chat__subtitle {
  color: var(--td-text-secondary);
  line-height: 1.6;
}

.td-chat__hero-actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--td-space-2);
  justify-content: flex-end;
}

.td-chat-layout {
  display: grid;
  grid-template-columns: 320px 1fr;
  gap: var(--td-space-4);
}

.td-chat-sessions,
.td-chat-panel {
  background: var(--td-surface-primary);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-lg);
  padding: var(--td-space-4);
  min-height: 560px;
}

.td-chat-section-head {
  display: flex;
  gap: var(--td-space-3);
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: var(--td-space-3);
}

.td-subtitle {
  font-size: var(--td-font-base);
  margin-bottom: var(--td-space-1);
}

.td-chat-section-copy {
  font-size: var(--td-font-sm);
  color: var(--td-text-secondary);
  line-height: 1.5;
}

.td-chat-board-context {
  margin: 0;
  font-size: var(--td-font-xs);
  color: var(--td-color-primary);
  font-weight: 600;
}

.td-form-group {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
  margin-bottom: var(--td-space-3);
}

.td-input {
  padding: var(--td-space-2) var(--td-space-3);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-md);
  font-size: var(--td-font-sm);
}

.td-session-item {
  width: 100%;
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-md);
  background: var(--td-surface-secondary);
  padding: var(--td-space-2);
  text-align: left;
  cursor: pointer;
  margin-bottom: var(--td-space-2);
}

.td-session-item--active {
  border-color: var(--td-color-primary);
  background: var(--td-color-primary-light);
}

.td-session-title {
  font-weight: 600;
  font-size: var(--td-font-sm);
}

.td-session-meta {
  font-size: var(--td-font-xs);
  color: var(--td-text-tertiary);
  margin-top: var(--td-space-1);
}

.td-chat-header {
  padding-bottom: var(--td-space-2);
  border-bottom: 1px solid var(--td-border-default);
  margin-bottom: var(--td-space-3);
}

.td-chat-meta {
  color: var(--td-text-tertiary);
  font-size: var(--td-font-xs);
}

.td-chat-meta__detail {
  font-family: monospace;
}

.td-chat-messages {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
  min-height: 360px;
  max-height: 420px;
  overflow-y: auto;
  margin-bottom: var(--td-space-3);
}

.td-message {
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-md);
  padding: var(--td-space-2);
}

.td-message-header {
  display: flex;
  justify-content: space-between;
  margin-bottom: var(--td-space-1);
}

.td-message-role {
  font-weight: 600;
  font-size: var(--td-font-xs);
}

.td-message-time {
  font-size: var(--td-font-xs);
  color: var(--td-text-tertiary);
}

.td-message-content {
  white-space: pre-wrap;
  font-size: var(--td-font-sm);
}

.td-chat-compose {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
}

.td-textarea {
  padding: var(--td-space-2) var(--td-space-3);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-md);
  font-size: var(--td-font-sm);
  resize: vertical;
}

.td-checkbox {
  display: inline-flex;
  align-items: center;
  gap: var(--td-space-1);
  font-size: var(--td-font-xs);
  color: var(--td-text-secondary);
}

.td-btn {
  padding: var(--td-space-2) var(--td-space-4);
  border: none;
  border-radius: var(--td-radius-md);
  font-size: var(--td-font-sm);
  font-weight: 600;
  cursor: pointer;
}

.td-btn--sm {
  padding: var(--td-space-1) var(--td-space-3);
  font-size: var(--td-font-xs);
}

.td-btn--xs {
  padding: 2px 8px;
  font-size: 11px;
}

.td-btn--primary {
  background: var(--td-color-primary);
  color: var(--td-text-inverse);
}

.td-btn--primary:hover:not(:disabled) {
  background: var(--td-color-primary-hover);
}

.td-btn--secondary {
  background: var(--td-surface-tertiary);
  color: var(--td-text-primary);
  border: 1px solid var(--td-border-default);
}

.td-btn--secondary:hover:not(:disabled) {
  background: var(--td-surface-hover);
}

.td-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.td-loading,
.td-empty {
  text-align: center;
  color: var(--td-text-secondary);
  padding: var(--td-space-4);
}

.td-empty--panel {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
  align-items: center;
  justify-content: center;
  min-height: 160px;
}

.td-empty__title {
  margin: 0;
  font-size: var(--td-font-base);
  color: var(--td-text-primary);
}

.td-empty__copy {
  margin: 0;
  max-width: 420px;
  line-height: 1.5;
}

.td-empty__actions {
  display: flex;
  gap: var(--td-space-2);
  flex-wrap: wrap;
  justify-content: center;
}

.td-message-proposal {
  margin-top: var(--td-space-1);
  font-size: var(--td-font-xs);
  color: var(--td-color-primary);
  font-family: monospace;
  display: inline-flex;
  align-items: center;
  gap: var(--td-space-2);
}

@media (max-width: 900px) {
  .td-chat__hero,
  .td-chat-section-head {
    flex-direction: column;
  }

  .td-chat-layout {
    grid-template-columns: 1fr;
  }

  .td-chat-sessions,
  .td-chat-panel {
    min-height: 0;
  }
}
</style>
