<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { marked } from 'marked'
import DOMPurify from 'dompurify'
import { chatApi } from '../api/chatApi'
import { boardsApi } from '../api/boardsApi'
import { useToastStore } from '../store/toastStore'
import type { ChatProviderHealth, ChatMessage, ChatSession, ToolCallMetadata } from '../types/chat'
import type { Board } from '../types/board'
import { normalizeChatRole, extractParseHint } from '../utils/chat'
import type { ParsedHintMessage } from '../utils/chat'
import { getErrorDisplay } from '../composables/useErrorMapper'
import InputAssistField from '../components/common/InputAssistField.vue'
import { buildInputAssistOptions } from '../utils/inputAssist'
import type { InputAssistOption } from '../utils/inputAssist'
import { normalizeBoardIdQueryParam } from '../utils/navigation'

function isAssistantOrSystemMessage(message: ChatMessage): boolean {
  const role = normalizeChatRole(message.role)
  return role === 'Assistant' || role === 'System'
}

function renderMarkdown(content: string): string {
  if (!content) {
    return ''
  }
  return DOMPurify.sanitize(marked.parse(content, { async: false }))
}

function isTruncatedJson(content: string): boolean {
  if (!content) return false
  const trimmed = content.trim()
  if (!trimmed.startsWith('{') && !trimmed.startsWith('[')) return false
  try {
    JSON.parse(trimmed)
    return false
  } catch {
    return true
  }
}

const truncationNotice = 'This response was cut short. Try a simpler question or rephrase.'

const router = useRouter()
const route = useRoute()
const toast = useToastStore()

const sessions = ref<ChatSession[]>([])
const availableBoards = ref<Board[]>([])
const selectedSession = ref<ChatSession | null>(null)
const loadingSessions = ref(false)
const loadingBoards = ref(false)
const loadingHealth = ref(false)
const creatingSession = ref(false)
const sendingMessage = ref(false)
let boardOptionsRequest: Promise<boolean> | null = null
const chatHealth = ref<ChatProviderHealth | null>(null)
const chatHealthLoadError = ref<string | null>(null)

const newSessionTitle = ref('')
const newSessionBoardId = ref('')
const selectedNewSessionBoardId = ref<string | null>(null)
const messageContent = ref('')
const requestProposal = ref(false)
const expandedHintIds = ref<Set<string>>(new Set())
const expandedToolMetaIds = ref<Set<string>>(new Set())

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

const parseHintsByMessageId = computed(() => {
  const map = new Map<string, ParsedHintMessage>()
  for (const message of sortedMessages.value) {
    if (message.messageType === 'parse-hint') {
      const hint = extractParseHint(message.content)
      if (hint) {
        map.set(message.id, hint)
      }
    }
  }
  return map
})

const lastMessageIsClarification = computed(() => {
  const msgs = sortedMessages.value
  if (msgs.length === 0) return false
  const last = msgs[msgs.length - 1]
  return last.messageType === 'clarification' && normalizeChatRole(last.role) === 'Assistant'
})

async function handleSkipClarification() {
  if (!selectedSession.value) return
  try {
    sendingMessage.value = true
    await chatApi.sendMessage(selectedSession.value.id, {
      content: 'Just do your best',
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

const llmHealthState = computed(() => {
  if (loadingHealth.value) {
    return 'loading'
  }

  if (chatHealthLoadError.value) {
    return 'error'
  }

  if (!chatHealth.value) {
    return 'unknown'
  }

  if (chatHealth.value.isMock) {
    return 'mock'
  }

  const vs = chatHealth.value.verificationStatus
  if (vs === 'verified') {
    return 'verified'
  }

  if (vs === 'failed') {
    return 'failed'
  }

  if (chatHealth.value.isAvailable) {
    return 'configured'
  }

  return 'unavailable'
})

const llmStatusTitle = computed(() => {
  switch (llmHealthState.value) {
    case 'loading':
      return 'Checking LLM status'
    case 'verified':
      return 'Live LLM verified'
    case 'configured':
      return 'Live LLM configured'
    case 'failed':
      return 'LLM verification failed'
    case 'mock':
      return 'Live LLM not active'
    case 'unavailable':
      return 'Live LLM unavailable'
    case 'error':
      return 'LLM status unavailable'
    default:
      return 'LLM status unknown'
  }
})

const llmStatusCopy = computed(() => {
  if (llmHealthState.value === 'loading') {
    return 'Resolving the current provider before manual chat work starts.'
  }

  if (llmHealthState.value === 'error') {
    return chatHealthLoadError.value ?? 'Taskdeck could not resolve provider status for this chat surface.'
  }

  if (!chatHealth.value) {
    return 'Taskdeck has not reported provider health yet.'
  }

  const providerLabel = chatHealth.value.model
    ? `${chatHealth.value.providerName} (${chatHealth.value.model})`
    : chatHealth.value.providerName

  if (llmHealthState.value === 'verified') {
    return `${providerLabel} is live and responding. The probe confirmed reachability.`
  }

  if (llmHealthState.value === 'configured') {
    return `Taskdeck is configured to use ${providerLabel}, but this health check does not prove the upstream provider accepted a live request yet. Use Verify LLM to confirm reachability.`
  }

  if (llmHealthState.value === 'failed') {
    return chatHealth.value.errorMessage
      ? `${providerLabel} verification failed: ${chatHealth.value.errorMessage}`
      : `${providerLabel} verification failed. The probe could not confirm reachability.`
  }

  if (llmHealthState.value === 'mock') {
    return `Taskdeck is currently using the Mock provider. Responses stay deterministic and do not prove a live LLM hookup.`
  }

  return chatHealth.value.errorMessage
    ? `${providerLabel} is not ready: ${chatHealth.value.errorMessage}`
    : `${providerLabel} is not ready for live requests.`
})

const llmStatusMeta = computed(() => {
  if (!chatHealth.value || llmHealthState.value === 'loading' || llmHealthState.value === 'error') {
    return null
  }

  return chatHealth.value.model
    ? `${chatHealth.value.providerName} | ${chatHealth.value.model}`
    : chatHealth.value.providerName
})

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

async function loadProviderHealth(options?: { probe?: boolean }) {
  try {
    loadingHealth.value = true
    chatHealthLoadError.value = null
    chatHealth.value = await chatApi.getHealth(options)
  } catch (e: unknown) {
    chatHealthLoadError.value = getErrorDisplay(e, 'Failed to load LLM status').message
    toast.error(chatHealthLoadError.value)
  } finally {
    loadingHealth.value = false
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

function resolveReviewBoardId(): string | null {
  const sessionBoardId = selectedSession.value?.boardId?.trim()
  if (sessionBoardId) {
    return sessionBoardId
  }

  return queryBoardId.value
}

function pushToReview(hash?: string) {
  const boardId = resolveReviewBoardId()
  void router.push({
    name: 'workspace-review',
    query: boardId ? { boardId } : undefined,
    hash,
  })
}

function getParseHint(message: ChatMessage): ParsedHintMessage | null {
  return parseHintsByMessageId.value.get(message.id) ?? null
}

function toggleHintPatterns(messageId: string) {
  const updated = new Set(expandedHintIds.value)
  if (updated.has(messageId)) {
    updated.delete(messageId)
  } else {
    updated.add(messageId)
  }
  expandedHintIds.value = updated
}

function applyHintSuggestion(example: string) {
  messageContent.value = example
  requestProposal.value = true
}

function parseToolCallMetadata(message: ChatMessage): ToolCallMetadata | null {
  if (!message.toolCallMetadataJson) return null
  try {
    return JSON.parse(message.toolCallMetadataJson) as ToolCallMetadata
  } catch {
    return null
  }
}

function toggleToolMeta(messageId: string) {
  const updated = new Set(expandedToolMetaIds.value)
  if (updated.has(messageId)) {
    updated.delete(messageId)
  } else {
    updated.add(messageId)
  }
  expandedToolMetaIds.value = updated
}

function formatToolName(toolName: string): string {
  return toolName.replace(/_/g, ' ').replace(/\b\w/g, (c) => c.toUpperCase())
}

function openReviewRoute() {
  pushToReview()
}

function openProposalReview(proposalId: string) {
  pushToReview(`#proposal-${encodeURIComponent(proposalId)}`)
}

onMounted(() => {
  void loadSessions()
  void loadProviderHealth()
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
        <button class="td-btn td-btn--secondary" :disabled="loadingHealth" @click="loadProviderHealth()">
          {{ loadingHealth ? 'Checking provider...' : 'Refresh LLM Status' }}
        </button>
        <button class="td-btn td-btn--secondary" :disabled="loadingHealth" @click="loadProviderHealth({ probe: true })">
          {{ loadingHealth ? 'Probing...' : 'Verify LLM' }}
        </button>
        <button class="td-btn td-btn--primary" @click="openReviewRoute">Back to Review</button>
        <button class="td-btn td-btn--secondary" @click="openRoute('/workspace/automations/queue')">
          Open Queue (Advanced)
        </button>
      </div>
    </header>

    <section
      class="td-chat-status"
      :class="`td-chat-status--${llmHealthState}`"
      :data-llm-health-state="llmHealthState"
    >
      <div>
        <h2 class="td-chat-status__title">{{ llmStatusTitle }}</h2>
        <p class="td-chat-status__copy">{{ llmStatusCopy }}</p>
      </div>
      <span v-if="llmStatusMeta" class="td-chat-status__meta">{{ llmStatusMeta }}</span>
    </section>

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
          <input v-model="newSessionTitle" class="td-input" type="text" aria-label="Session title" placeholder="Session title" />
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
            <button class="td-btn td-btn--primary td-btn--sm" @click="openReviewRoute">
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
            <button class="td-btn td-btn--primary td-btn--sm" @click="openReviewRoute">
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
            <div
              v-for="message in sortedMessages"
              :key="message.id"
              class="td-message"
              :class="{
                'td-message--degraded': message.messageType === 'degraded',
                'td-message--clarification': message.messageType === 'clarification'
              }"
              :data-message-type="message.messageType"
            >
              <div class="td-message-header">
                <span class="td-message-role">{{ normalizeChatRole(message.role) }}</span>
                <span class="td-message-time">{{ new Date(message.createdAt).toLocaleTimeString() }}</span>
              </div>
              <div v-if="message.messageType === 'degraded'" class="td-message-degraded-warning">
                Degraded response{{ message.degradedReason ? `: ${message.degradedReason}` : '' }}
              </div>
              <div v-if="message.messageType === 'clarification'" class="td-message-clarification-badge">
                Asking for clarification
              </div>
              <template v-if="message.messageType === 'parse-hint' && getParseHint(message)">
                <div
                  class="td-message-content td-message-content--markdown"
                  v-html="renderMarkdown(getParseHint(message)!.textBeforeHint)"
                ></div>
                <div class="td-hint-card" role="region" aria-label="Instruction format hint">
                  <div class="td-hint-card__header">
                    <span class="td-hint-card__icon" aria-hidden="true">i</span>
                    <span class="td-hint-card__title">
                      {{ getParseHint(message)!.hint.detectedIntent
                        ? `Detected intent: ${getParseHint(message)!.hint.detectedIntent}`
                        : 'Could not detect intent' }}
                    </span>
                  </div>
                  <p class="td-hint-card__suggestion">
                    Try this format:
                    <code>{{ getParseHint(message)!.hint.closestPattern }}</code>
                  </p>
                  <div class="td-hint-card__actions">
                    <button
                      class="td-btn td-btn--primary td-btn--sm"
                      @click="applyHintSuggestion(getParseHint(message)!.hint.exampleInstruction)"
                    >
                      Try this instead
                    </button>
                    <button
                      class="td-btn td-btn--secondary td-btn--sm"
                      :aria-expanded="expandedHintIds.has(message.id)"
                      @click="toggleHintPatterns(message.id)"
                    >
                      {{ expandedHintIds.has(message.id) ? 'Hide all patterns' : 'Show all patterns' }}
                    </button>
                  </div>
                  <ul
                    v-if="expandedHintIds.has(message.id)"
                    class="td-hint-card__patterns"
                    aria-label="Supported instruction patterns"
                  >
                    <li
                      v-for="(pattern, index) in getParseHint(message)!.hint.supportedPatterns"
                      :key="index"
                    >
                      <code>{{ pattern }}</code>
                    </li>
                  </ul>
                </div>
              </template>
              <template v-else>
                <div
                  v-if="isAssistantOrSystemMessage(message) && isTruncatedJson(message.content)"
                  class="td-message-content td-message-content--truncated"
                >
                  {{ truncationNotice }}
                </div>
                <div
                  v-else-if="isAssistantOrSystemMessage(message)"
                  class="td-message-content td-message-content--markdown"
                  v-html="renderMarkdown(message.content)"
                ></div>
                <div v-else class="td-message-content">{{ message.content }}</div>
              </template>
              <div v-if="message.proposalId && message.messageType === 'proposal-reference'" class="td-message-proposal">
                <span>Proposal: {{ message.proposalId }}</span>
                <button
                  class="td-btn td-btn--secondary td-btn--xs"
                  @click="openProposalReview(message.proposalId)"
                >
                  Open in Review
                </button>
              </div>
              <div v-if="parseToolCallMetadata(message)" class="td-tool-meta">
                <button
                  class="td-tool-meta__toggle"
                  :aria-expanded="expandedToolMetaIds.has(message.id)"
                  @click="toggleToolMeta(message.id)"
                >
                  <span class="td-tool-meta__icon" aria-hidden="true">{{ expandedToolMetaIds.has(message.id) ? '&#9660;' : '&#9654;' }}</span>
                  {{ parseToolCallMetadata(message)!.tool_calls.length }} tool call{{ parseToolCallMetadata(message)!.tool_calls.length === 1 ? '' : 's' }} in {{ parseToolCallMetadata(message)!.rounds }} round{{ parseToolCallMetadata(message)!.rounds === 1 ? '' : 's' }}
                </button>
                <div v-if="expandedToolMetaIds.has(message.id)" class="td-tool-meta__details">
                  <div
                    v-for="(call, idx) in parseToolCallMetadata(message)!.tool_calls"
                    :key="idx"
                    class="td-tool-call"
                    :class="{ 'td-tool-call--error': call.is_error }"
                  >
                    <div class="td-tool-call__header">
                      <span class="td-tool-call__round">R{{ call.round }}</span>
                      <span class="td-tool-call__name">{{ formatToolName(call.tool) }}</span>
                      <span v-if="call.is_error" class="td-tool-call__badge td-tool-call__badge--error">Error</span>
                      <span v-else-if="call.tool.startsWith('propose_')" class="td-tool-call__badge td-tool-call__badge--proposal">Proposal</span>
                    </div>
                    <div class="td-tool-call__summary">{{ call.result_summary }}</div>
                  </div>
                </div>
              </div>
            </div>

            <div v-if="sendingMessage" class="td-message td-message--tool-status" data-message-type="tool-status">
              <div class="td-message-header">
                <span class="td-message-role">System</span>
              </div>
              <div class="td-message-content td-tool-status">
                <span class="td-tool-status__spinner" aria-hidden="true"></span>
                Processing your request...
              </div>
            </div>
          </div>

          <div class="td-chat-compose">
            <div v-if="lastMessageIsClarification" class="td-clarification-skip">
              <span class="td-clarification-skip__hint">The assistant is asking for more details.</span>
              <button
                class="td-btn td-btn--secondary td-btn--sm"
                :disabled="sendingMessage"
                @click="handleSkipClarification"
              >
                Skip, just do your best
              </button>
            </div>
            <textarea
              v-model="messageContent"
              aria-label="Automation instruction"
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

.td-chat-status {
  display: flex;
  justify-content: space-between;
  gap: var(--td-space-3);
  align-items: flex-start;
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-lg);
  padding: var(--td-space-3) var(--td-space-4);
  background: var(--td-surface-primary);
}

.td-chat-status--configured {
  border-color: var(--td-color-warning, #b7791f);
  background: color-mix(in srgb, var(--td-surface-primary) 86%, #fff4d6 14%);
}

.td-chat-status--verified {
  border-color: var(--td-color-success, #2f855a);
  background: color-mix(in srgb, var(--td-surface-primary) 80%, #c6f6d5 20%);
}

.td-chat-status--failed {
  border-color: var(--td-color-danger, #c53030);
  background: color-mix(in srgb, var(--td-surface-primary) 86%, #fed7d7 14%);
}

.td-chat-status--mock,
.td-chat-status--unavailable,
.td-chat-status--error {
  border-color: var(--td-color-warning, #b7791f);
  background: color-mix(in srgb, var(--td-surface-primary) 86%, #fff4d6 14%);
}

.td-chat-status__title {
  margin: 0 0 var(--td-space-1);
  font-size: var(--td-font-sm);
  font-weight: 700;
}

.td-chat-status__copy {
  margin: 0;
  color: var(--td-text-secondary);
  line-height: 1.5;
}

.td-chat-status__meta {
  font-size: var(--td-font-xs);
  color: var(--td-text-tertiary);
  font-family: monospace;
  white-space: nowrap;
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

.td-message-content--truncated {
  color: var(--td-text-secondary);
  font-style: italic;
}

.td-message-content--markdown {
  white-space: normal;
}

.td-message-content--markdown :deep(p) {
  margin: 0 0 var(--td-space-2);
}

.td-message-content--markdown :deep(p:last-child) {
  margin-bottom: 0;
}

.td-message-content--markdown :deep(h1),
.td-message-content--markdown :deep(h2),
.td-message-content--markdown :deep(h3),
.td-message-content--markdown :deep(h4),
.td-message-content--markdown :deep(h5),
.td-message-content--markdown :deep(h6) {
  margin: var(--td-space-2) 0 var(--td-space-1);
  font-weight: 700;
  color: var(--td-text-primary);
}

.td-message-content--markdown :deep(h1) { font-size: var(--td-font-xl); }
.td-message-content--markdown :deep(h2) { font-size: var(--td-font-lg); }
.td-message-content--markdown :deep(h3) { font-size: var(--td-font-base); }

.td-message-content--markdown :deep(ul),
.td-message-content--markdown :deep(ol) {
  margin: 0 0 var(--td-space-2);
  padding-left: var(--td-space-4);
}

.td-message-content--markdown :deep(li) {
  margin-bottom: var(--td-space-1);
}

.td-message-content--markdown :deep(code) {
  font-family: monospace;
  font-size: 0.9em;
  background: var(--td-surface-tertiary);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-sm);
  padding: 1px 4px;
}

.td-message-content--markdown :deep(pre) {
  background: var(--td-surface-tertiary);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-md);
  padding: var(--td-space-2) var(--td-space-3);
  overflow-x: auto;
  margin: 0 0 var(--td-space-2);
}

.td-message-content--markdown :deep(pre code) {
  background: none;
  border: none;
  padding: 0;
  font-size: var(--td-font-sm);
}

.td-message-content--markdown :deep(strong) {
  font-weight: 700;
}

.td-message-content--markdown :deep(em) {
  font-style: italic;
}

.td-message-content--markdown :deep(blockquote) {
  border-left: 3px solid var(--td-border-default);
  margin: 0 0 var(--td-space-2);
  padding-left: var(--td-space-3);
  color: var(--td-text-secondary);
}

.td-message-content--markdown :deep(hr) {
  border: none;
  border-top: 1px solid var(--td-border-default);
  margin: var(--td-space-2) 0;
}

.td-message-content--markdown :deep(a) {
  color: var(--td-color-primary);
  text-decoration: underline;
}

.td-message--degraded {
  border-left: 3px solid var(--td-color-warning, #d69e2e);
  padding-left: var(--td-space-2);
}

.td-message-degraded-warning {
  font-size: var(--td-font-xs);
  color: var(--td-color-warning, #d69e2e);
  font-weight: 600;
  margin-bottom: var(--td-space-1);
}

.td-message--clarification {
  border-left: 3px solid var(--td-color-info, #3182ce);
  padding-left: var(--td-space-2);
}

.td-message-clarification-badge {
  font-size: var(--td-font-xs);
  color: var(--td-color-info, #3182ce);
  font-weight: 600;
  margin-bottom: var(--td-space-1);
}

.td-clarification-skip {
  display: flex;
  align-items: center;
  gap: var(--td-space-2);
  padding: var(--td-space-2);
  background: var(--td-bg-secondary, #f7fafc);
  border: 1px solid var(--td-color-info, #3182ce);
  border-radius: var(--td-radius, 6px);
}

.td-clarification-skip__hint {
  font-size: var(--td-font-sm);
  color: var(--td-text-secondary);
  flex: 1;
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

.td-hint-card {
  margin-top: var(--td-space-2);
  border: 1px solid var(--td-color-primary, #3182ce);
  border-radius: var(--td-radius-md);
  padding: var(--td-space-3);
  background: color-mix(in srgb, var(--td-surface-primary) 90%, #ebf4ff 10%);
}

.td-hint-card__header {
  display: flex;
  align-items: center;
  gap: var(--td-space-2);
  margin-bottom: var(--td-space-2);
}

.td-hint-card__icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 20px;
  height: 20px;
  border-radius: 50%;
  background: var(--td-color-primary, #3182ce);
  color: var(--td-text-inverse, #fff);
  font-size: 12px;
  font-weight: 700;
  flex-shrink: 0;
}

.td-hint-card__title {
  font-size: var(--td-font-sm);
  font-weight: 600;
  color: var(--td-text-primary);
}

.td-hint-card__suggestion {
  margin: 0 0 var(--td-space-2);
  font-size: var(--td-font-sm);
  color: var(--td-text-secondary);
}

.td-hint-card__suggestion code {
  font-family: monospace;
  font-size: 0.9em;
  background: var(--td-surface-tertiary);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-sm);
  padding: 1px 4px;
}

.td-hint-card__actions {
  display: flex;
  gap: var(--td-space-2);
  flex-wrap: wrap;
}

.td-hint-card__patterns {
  margin: var(--td-space-2) 0 0;
  padding-left: var(--td-space-4);
  list-style: disc;
}

.td-hint-card__patterns li {
  margin-bottom: var(--td-space-1);
  font-size: var(--td-font-sm);
}

.td-hint-card__patterns code {
  font-family: monospace;
  font-size: 0.9em;
  background: var(--td-surface-tertiary);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-sm);
  padding: 1px 4px;
}

/* Tool call metadata expander */
.td-tool-meta {
  margin-top: var(--td-space-2);
  border-top: 1px solid var(--td-border-default);
  padding-top: var(--td-space-1);
}

.td-tool-meta__toggle {
  background: none;
  border: none;
  cursor: pointer;
  font-size: var(--td-font-xs);
  color: var(--td-text-tertiary);
  padding: var(--td-space-1) 0;
  display: flex;
  align-items: center;
  gap: var(--td-space-1);
}

.td-tool-meta__toggle:hover {
  color: var(--td-text-secondary);
}

.td-tool-meta__icon {
  font-size: 8px;
  line-height: 1;
}

.td-tool-meta__details {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-1);
  margin-top: var(--td-space-1);
}

.td-tool-call {
  padding: var(--td-space-1) var(--td-space-2);
  border-left: 2px solid var(--td-border-default);
  font-size: var(--td-font-xs);
}

.td-tool-call--error {
  border-left-color: var(--td-color-danger, #e53e3e);
}

.td-tool-call__header {
  display: flex;
  align-items: center;
  gap: var(--td-space-1);
  margin-bottom: 2px;
}

.td-tool-call__round {
  font-weight: 700;
  color: var(--td-text-tertiary);
  min-width: 20px;
}

.td-tool-call__name {
  font-weight: 600;
  color: var(--td-text-primary);
}

.td-tool-call__badge {
  font-size: 10px;
  font-weight: 700;
  padding: 1px 4px;
  border-radius: var(--td-radius-sm);
}

.td-tool-call__badge--error {
  background: color-mix(in srgb, var(--td-surface-primary) 80%, #fed7d7 20%);
  color: var(--td-color-danger, #e53e3e);
}

.td-tool-call__badge--proposal {
  background: color-mix(in srgb, var(--td-surface-primary) 80%, #c6f6d5 20%);
  color: var(--td-color-success, #2f855a);
}

.td-tool-call__summary {
  color: var(--td-text-secondary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 400px;
}

/* Tool status indicator during sending */
.td-message--tool-status {
  border-style: dashed;
  background: color-mix(in srgb, var(--td-surface-primary) 95%, var(--td-color-primary) 5%);
}

.td-tool-status {
  display: flex;
  align-items: center;
  gap: var(--td-space-2);
  color: var(--td-text-secondary);
  font-style: italic;
}

.td-tool-status__spinner {
  display: inline-block;
  width: 14px;
  height: 14px;
  border: 2px solid var(--td-border-default);
  border-top-color: var(--td-color-primary);
  border-radius: 50%;
  animation: td-spin 0.8s linear infinite;
}

@keyframes td-spin {
  to { transform: rotate(360deg); }
}

@media (max-width: 900px) {
  .td-chat__hero,
  .td-chat-section-head,
  .td-chat-status {
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
