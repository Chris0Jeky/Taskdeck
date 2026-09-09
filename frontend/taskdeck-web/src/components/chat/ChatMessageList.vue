<script setup lang="ts">
import { computed, ref } from 'vue'
import { marked } from 'marked'
import DOMPurify from 'dompurify'
import type { ChatMessage, ToolCallMetadata } from '../../types/chat'
import type { Board } from '../../types/board'
import { normalizeChatRole, extractParseHint } from '../../utils/chat'
import type { ParsedHintMessage } from '../../utils/chat'
import ChatParseHintCard from './ChatParseHintCard.vue'
import ChatToolCallDetails from './ChatToolCallDetails.vue'
import ChatProposalPreview from './ChatProposalPreview.vue'

const props = defineProps<{
  messages: ChatMessage[]
  sendingMessage: boolean
  eligibleBoards: Board[]
  loadingBoards: boolean
  selectedSessionBoardId: string | null
  selectedSessionBoardName: string
  pendingBoardMessageId: string | null
  bindingBoard: boolean
  bindingMessageId: string | null
  boardBindingError: string | null
  boardBindingReceipt: string | null
  boardLoadError: string | null
}>()

const emit = defineEmits<{
  (e: 'apply-hint-suggestion', example: string): void
  (e: 'open-proposal-review', proposalId: string): void
  (e: 'bind-board', messageId: string, boardId: string): void
  (e: 'continue-instruction', messageId: string): void
  (e: 'open-boards'): void
  (e: 'reload-boards'): void
}>()

const expandedHintIds = ref<Set<string>>(new Set())
const expandedToolMetaIds = ref<Set<string>>(new Set())
const selectedBoardIds = ref<Record<string, string>>({})

const truncationNotice = 'This response was cut short. Try a simpler question or rephrase.'

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

function checkTruncatedJson(content: string): boolean {
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

const truncatedJsonIds = computed(() => {
  const ids = new Set<string>()
  for (const message of props.messages) {
    const role = normalizeChatRole(message.role)
    if ((role === 'Assistant' || role === 'System') && checkTruncatedJson(message.content)) {
      ids.add(message.id)
    }
  }
  return ids
})

const parseHintsByMessageId = computed(() => {
  const map = new Map<string, ParsedHintMessage>()
  for (const message of props.messages) {
    if (message.messageType === 'parse-hint') {
      const hint = extractParseHint(message.content)
      if (hint) {
        map.set(message.id, hint)
      }
    }
  }
  return map
})

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

const toolMetaByMessageId = computed(() => {
  const map = new Map<string, ToolCallMetadata>()
  for (const message of props.messages) {
    if (!message.toolCallMetadataJson) continue
    try {
      const parsed = JSON.parse(message.toolCallMetadataJson) as ToolCallMetadata
      map.set(message.id, parsed)
    } catch {
      // skip unparseable metadata
    }
  }
  return map
})

function getToolMeta(message: ChatMessage): ToolCallMetadata | null {
  return toolMetaByMessageId.value.get(message.id) ?? null
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

function selectedBoardId(messageId: string): string {
  return selectedBoardIds.value[messageId]
    ?? (props.eligibleBoards.length === 1 ? props.eligibleBoards[0]!.id : '')
}

function updateSelectedBoardId(messageId: string, boardId: string) {
  selectedBoardIds.value = {
    ...selectedBoardIds.value,
    [messageId]: boardId,
  }
}

function bindSelectedBoard(messageId: string) {
  const boardId = selectedBoardId(messageId)
  if (boardId) emit('bind-board', messageId, boardId)
}
</script>

<template>
  <div class="td-chat-messages">
    <div v-if="messages.length === 0" class="td-empty td-empty--panel">
      <h3 class="td-empty__title">No messages yet</h3>
      <p class="td-empty__copy">
        Send a manual instruction below, or return to Review if you already have a proposal waiting for a
        decision.
      </p>
    </div>
    <div
      v-for="message in messages"
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
      <div v-if="message.messageType === 'action-needs-board'" class="td-message-outcome-badge">
        Board needed before a proposal can be created
      </div>
      <div v-if="message.messageType === 'action-no-proposal'" class="td-message-outcome-badge">
        No proposal was created
      </div>
      <template v-if="message.messageType === 'parse-hint' && getParseHint(message)">
        <div
          class="td-message-content td-message-content--markdown"
          v-html="renderMarkdown(getParseHint(message)!.textBeforeHint)"
        ></div>
        <ChatParseHintCard
          :hint="getParseHint(message)!"
          :message-id="message.id"
          :expanded="expandedHintIds.has(message.id)"
          @apply-suggestion="emit('apply-hint-suggestion', $event)"
          @toggle-patterns="toggleHintPatterns"
        />
      </template>
      <template v-else>
        <div
          v-if="truncatedJsonIds.has(message.id)"
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
      <details v-if="message.contextSources?.length" class="td-context-receipt">
        <summary>Sources included in this turn ({{ message.contextSources.length }})</summary>
        <ul>
          <li v-for="source in message.contextSources" :key="`${source.kind}:${source.id}`">
            {{ source.title }} — {{ source.kind === 'private-source' ? 'Private original' : source.kind === 'private-memory' ? 'Private memory' : source.kind === 'thinking' ? 'Shared thinking' : 'Card' }}
            <span v-if="source.kind !== 'card' && source.revision !== null"> · version {{ source.revision }}</span>
            <span v-if="source.truncated"> · excerpt</span>
            <span v-if="source.supersededByAssetId"> · superseded historical source</span>
            <details v-if="source.contentHash"><summary>Source fingerprint</summary><code style="overflow-wrap: anywhere">{{ source.contentHash }}</code></details>
          </li>
        </ul>
        <p>These sources were checked when this message was sent. Answers can remain in this private conversation after a source changes.</p>
      </details>
      <div v-if="message.proposalId && message.messageType === 'proposal-reference'" class="td-message-proposal">
        <span>Proposal: {{ message.proposalId }}</span>
        <button
          class="td-btn td-btn--secondary td-btn--xs"
          @click="emit('open-proposal-review', message.proposalId)"
        >
          Open in Review
        </button>
      </div>
      <ChatToolCallDetails
        v-if="getToolMeta(message)"
        :metadata="getToolMeta(message)!"
        :message-id="message.id"
        :expanded="expandedToolMetaIds.has(message.id)"
        @toggle="toggleToolMeta"
      />
      <ChatProposalPreview
        v-if="message.proposalId && message.messageType === 'proposal-reference'"
        :proposal-id="message.proposalId"
        :board-id="selectedSessionBoardId"
      />
      <section
        v-if="message.id === pendingBoardMessageId"
        class="td-board-recovery"
        aria-label="Link a board to continue this instruction"
      >
        <template v-if="selectedSessionBoardId">
          <p class="td-board-recovery__receipt" role="status">
            Linked to {{ boardBindingReceipt ?? selectedSessionBoardName }}. The retained instruction has not been sent again.
          </p>
          <button
            class="td-btn td-btn--primary td-btn--sm"
            :disabled="sendingMessage"
            @click="emit('continue-instruction', message.id)"
          >
            {{ sendingMessage ? 'Continuing...' : 'Continue retained instruction' }}
          </button>
        </template>
        <template v-else>
          <template v-if="boardLoadError">
            <p class="td-board-recovery__error" role="alert">
              Unable to load writable boards: {{ boardLoadError }}
            </p>
            <button
              class="td-btn td-btn--secondary td-btn--sm"
              :disabled="loadingBoards"
              @click="emit('reload-boards')"
            >
              {{ loadingBoards ? 'Retrying...' : 'Retry loading boards' }}
            </button>
          </template>
          <template v-if="loadingBoards">
            <p class="td-board-recovery__copy">Loading writable boards...</p>
          </template>
          <template v-else-if="eligibleBoards.length === 0 && !boardLoadError">
            <p class="td-board-recovery__copy">
              There are no active boards you can edit. Create a board or ask an owner for edit access, then reload boards.
            </p>
            <button class="td-btn td-btn--secondary td-btn--sm" @click="emit('open-boards')">
              Open Boards
            </button>
          </template>
          <template v-else-if="eligibleBoards.length > 0">
            <label class="td-board-recovery__label" :for="`chat-board-${message.id}`">
              Board for this session
            </label>
            <select
              :id="`chat-board-${message.id}`"
              class="td-board-recovery__select"
              :value="selectedBoardId(message.id)"
              :disabled="bindingBoard && bindingMessageId === message.id"
              @change="updateSelectedBoardId(message.id, ($event.target as HTMLSelectElement).value)"
            >
              <option v-if="eligibleBoards.length > 1" value="">Choose a board</option>
              <option v-for="board in eligibleBoards" :key="board.id" :value="board.id">
                {{ board.name }}
              </option>
            </select>
            <button
              class="td-btn td-btn--primary td-btn--sm"
              :disabled="!selectedBoardId(message.id) || (bindingBoard && bindingMessageId === message.id)"
              @click="bindSelectedBoard(message.id)"
            >
              {{ bindingBoard && bindingMessageId === message.id ? 'Linking...' : 'Link board' }}
            </button>
          </template>
        </template>
        <p v-if="boardBindingError" class="td-board-recovery__error" role="alert">
          {{ boardBindingError }}
        </p>
      </section>
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
</template>

<style scoped>
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

.td-message-content--markdown :deep(:is(h1, h2, h3, h4, h5, h6)) {
  margin: var(--td-space-2) 0 var(--td-space-1);
  font-weight: 700;
  color: var(--td-text-primary);
}
.td-message-content--markdown :deep(h1) { font-size: var(--td-font-xl); }
.td-message-content--markdown :deep(h2) { font-size: var(--td-font-lg); }
.td-message-content--markdown :deep(h3) { font-size: var(--td-font-base); }

.td-message-content--markdown :deep(:is(ul, ol)) {
  margin: 0 0 var(--td-space-2);
  padding-left: var(--td-space-4);
}
.td-message-content--markdown :deep(li) { margin-bottom: var(--td-space-1); }

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

.td-message-content--markdown :deep(strong) { font-weight: 700; }
.td-message-content--markdown :deep(em) { font-style: italic; }
.td-message-content--markdown :deep(blockquote) {
  border-left: 3px solid var(--td-border-default);
  margin: 0 0 var(--td-space-2);
  padding-left: var(--td-space-3);
  color: var(--td-text-secondary);
}

.td-message-content--markdown :deep(hr) { border: none; border-top: 1px solid var(--td-border-default); margin: var(--td-space-2) 0; }
.td-message-content--markdown :deep(a) { color: var(--td-color-primary); text-decoration: underline; }

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

.td-message-outcome-badge {
  font-size: var(--td-font-xs);
  color: var(--td-color-warning, #9c5b00);
  font-weight: 700;
  margin-bottom: var(--td-space-1);
}

.td-board-recovery {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--td-space-2);
  margin-top: var(--td-space-3);
  padding: var(--td-space-3);
  border: 1px solid var(--td-color-info, #3182ce);
  border-radius: var(--td-radius-md);
  background: var(--td-surface-secondary);
}

.td-board-recovery__label {
  width: 100%;
  font-size: var(--td-font-xs);
  font-weight: 700;
}

.td-board-recovery__select {
  min-width: 180px;
  flex: 1;
  padding: var(--td-space-2);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-md);
  background: var(--td-surface-primary);
  color: var(--td-text-primary);
}

.td-board-recovery__copy,
.td-board-recovery__receipt,
.td-board-recovery__error {
  width: 100%;
  margin: 0;
  font-size: var(--td-font-sm);
}

.td-board-recovery__receipt {
  color: var(--td-color-success, #26734d);
  font-weight: 700;
}

.td-board-recovery__error {
  color: var(--td-color-danger, #b42318);
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

.td-empty { text-align: center; color: var(--td-text-secondary); padding: var(--td-space-4); }
.td-empty--panel { display: flex; flex-direction: column; gap: var(--td-space-2); align-items: center; justify-content: center; min-height: 160px; }
.td-empty__title { margin: 0; font-size: var(--td-font-base); color: var(--td-text-primary); }
.td-empty__copy { margin: 0; max-width: 420px; line-height: 1.5; }

.td-btn { padding: var(--td-space-2) var(--td-space-4); border: none; border-radius: var(--td-radius-md); font-size: var(--td-font-sm); font-weight: 600; cursor: pointer; }
.td-btn--sm { padding: var(--td-space-1) var(--td-space-3); font-size: var(--td-font-xs); }
.td-btn--xs { padding: 2px 8px; font-size: 11px; }
.td-btn--primary { background: var(--td-color-primary); color: var(--td-text-inverse); }
.td-btn--secondary { background: var(--td-surface-tertiary); color: var(--td-text-primary); border: 1px solid var(--td-border-default); }
.td-btn--primary:hover:not(:disabled) { background: var(--td-color-primary-hover); }
.td-btn--secondary:hover:not(:disabled) { background: var(--td-surface-hover); }
.td-btn:disabled { opacity: 0.6; cursor: not-allowed; }
</style>
