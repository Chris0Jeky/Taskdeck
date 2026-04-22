<script setup lang="ts">
import { computed, ref } from 'vue'
import { marked } from 'marked'
import DOMPurify from 'dompurify'
import type { ChatMessage, ToolCallMetadata } from '../../types/chat'
import { normalizeChatRole, extractParseHint } from '../../utils/chat'
import type { ParsedHintMessage } from '../../utils/chat'
import ChatParseHintCard from './ChatParseHintCard.vue'
import ChatToolCallDetails from './ChatToolCallDetails.vue'

const props = defineProps<{
  messages: ChatMessage[]
  sendingMessage: boolean
}>()

const emit = defineEmits<{
  (e: 'apply-hint-suggestion', example: string): void
  (e: 'open-proposal-review', proposalId: string): void
}>()

const expandedHintIds = ref<Set<string>>(new Set())
const expandedToolMetaIds = ref<Set<string>>(new Set())

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
          @click="emit('open-proposal-review', message.proposalId)"
        >
          Open in Review
        </button>
      </div>
      <ChatToolCallDetails
        v-if="parseToolCallMetadata(message)"
        :metadata="parseToolCallMetadata(message)!"
        :message-id="message.id"
        :expanded="expandedToolMetaIds.has(message.id)"
        @toggle="toggleToolMeta"
      />
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
.td-btn--xs { padding: 2px 8px; font-size: 11px; }
.td-btn--secondary { background: var(--td-surface-tertiary); color: var(--td-text-primary); border: 1px solid var(--td-border-default); }
.td-btn--secondary:hover:not(:disabled) { background: var(--td-surface-hover); }
</style>
